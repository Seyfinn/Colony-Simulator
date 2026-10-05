using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// La façade historique de l'urbanisme : où placer ses bâtiments. Le choix de l'emplacement est confié au planificateur de quartiers
/// (<see cref="SettlementPlanner"/>, <see cref="SitePlanner"/>) : un site se trouve par quartier, avec son accès réservé, son score et ses trajets, plus dans un anneau autour du feu.
/// Les <c>Find*</c> sont des sondes pures (aucune modification du monde) ; les <c>Plan*</c> revalident l'emplacement demandé avec son profil et son accès. Une sonde pure
/// n'est pas nécessairement rapide : la pensée horaire de la colonie ne les appelle pas, elle passe par des demandes persistantes (voir <see cref="SettlementPlanner.Poll"/>).
/// </summary>
public static class Urbanism
{
    /// <summary>Le genre de développement d'un type de bâtiment (pour la priorité et l'identité de sa demande).</summary>
    internal static DevelopmentKind KindOf(BuildingType type) => type switch
    {
        BuildingType.Hut => DevelopmentKind.Housing,
        BuildingType.Storehouse => DevelopmentKind.Logistics,
        _ => new Building(type, 0, 0) is { IsWorkshop: true } ? DevelopmentKind.Workshop : DevelopmentKind.Civic,
    };

    /// <summary>Renvoie la case en haut à gauche d'un emplacement libre pour une hutte de 2 × 2, ou null s'il n'y en a pas.</summary>
    public static (int X, int Y)? FindHutSite(LocalMap map, Colony colony) => FindSite(map, colony, BuildingType.Hut);

    /// <summary>
    /// Le meilleur emplacement d'un bâtiment de ce type, ou null : le type précis compte (le moulin a besoin d'eau vive, la charbonnière se tient à l'écart des logements, le fût près de sa taverne).
    /// Sonde pure : ne modifie rien, ne consomme aucun hasard.
    /// </summary>
    public static (int X, int Y)? FindSite(LocalMap map, Colony colony, BuildingType type)
    {
        PlacementProposal? proposal = SettlementPlanner.Probe(colony, KindOf(type), type, urgent: false, out _);
        return proposal is null ? null : (proposal.X, proposal.Y);
    }

    /// <summary>
    /// Un moulin à eau se pose au bord d'une rivière, d'un lac de retenue ou d'un canal en eau, sur un terrain plat, avec un débit réel (<see cref="Hydrology.MillFlow"/>) ;
    /// la recherche s'étend de pôle en pôle plutôt que dans un rayon fixe autour du feu. Null s'il n'y a pas d'eau vive à portée.
    /// </summary>
    public static (int X, int Y)? FindMillSite(LocalMap map, Colony colony) => FindSite(map, colony, BuildingType.Mill);

    /// <summary>
    /// Une case pour une tombe : le cimetière se tient à l'écart du camp et grandit autour de la première tombe,
    /// sur un terrain dégagé, loin des huttes, des champs et des accès réservés.
    /// </summary>
    public static (int X, int Y)? FindGraveSite(LocalMap map, Colony colony)
    {
        Grave? first = colony.Graves.FirstOrDefault(g => g.X >= 0);
        (int cx, int cy) = first is not null ? (first.X, first.Y) : (colony.CampX - 10, colony.CampY + 5);

        (int X, int Y)? best = null;
        int bestDistance = int.MaxValue;
        LocalSpatialIndex index = colony.Spatial;
        for (int dy = -18; dy <= 18; dy++)
        for (int dx = -18; dx <= 18; dx++)
        {
            int x = colony.CampX + dx, y = colony.CampY + dy;
            int fromCamp = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (fromCamp < 8 || !IsGraveTile(map, colony, index, x, y))
                continue;
            int distance = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (x, y);
            }
        }
        return best;
    }

    private static bool IsGraveTile(LocalMap map, Colony colony, LocalSpatialIndex index, int x, int y)
    {
        if (!map.InBounds(x, y) || !map.IsWalkable(x, y) || map.IsWaterway(x, y) || colony.CanalTiles.Contains((x, y)) || map.IsMountain(x, y) || map.GetFlora(x, y) != FloraType.None)
            return false;
        if (index.Has(x, y, CellUse.Corridor | CellUse.Courtyard | CellUse.PublicSpace | CellUse.Plaza | CellUse.Building | CellUse.Field))
            return false;
        foreach (Grave grave in colony.Graves)
            if (Math.Max(Math.Abs(grave.X - x), Math.Abs(grave.Y - y)) < 2)
                return false;
        foreach (Building building in colony.Buildings)
            if (x >= building.X - 1 && x <= building.X + building.Width && y >= building.Y - 1 && y <= building.Y + building.Height)
                return false;
        foreach (Field field in colony.Fields)
            if (x >= field.X - 1 && x <= field.X + Field.Size && y >= field.Y - 1 && y <= field.Y + Field.Size)
                return false;
        return true;
    }

    /// <summary>Ouvre un chantier de hutte à l'emplacement demandé : on dégage le terrain (souches comprises) et on pose les fondations.</summary>
    public static Building PlanHut(LocalMap map, Colony colony, int x, int y) => PlanBuilding(map, colony, BuildingType.Hut, x, y);

    /// <summary>
    /// Outil de développement : pose tout de suite un bâtiment achevé, sur le meilleur emplacement de son type (une hutte en quartier résidentiel, un atelier à sa place).
    /// L'outil garde son exemption économique de développement mais respecte les contraintes physiques, l'association à un quartier et les notifications d'achèvement.
    /// </summary>
    public static Building? BuildInstantly(LocalMap map, Colony colony, BuildingType type)
    {
        if (FindSite(map, colony, type) is not { } s)
            return null;
        Building building = PlanBuilding(map, colony, type, s.X, s.Y);
        building.Progress = 1f;
        SettlementPlanner.OnObjectCompleted(colony, building);
        return building;
    }

    /// <summary>
    /// Ouvre le chantier d'un bâtiment à l'emplacement demandé. L'emplacement est revalidé avec le profil du type et son accès (quartier, porte, tracé réservé) ; un outil ou un
    /// test qui insiste sur un site que les règles refusent obtient tout de même son bâtiment, enregistré comme un écart historique (accès traversable comme avant).
    /// </summary>
    public static Building PlanBuilding(LocalMap map, Colony colony, BuildingType type, int x, int y)
    {
        // Pour un barrage, les coordonnées demandées désignent la rivière ; son emprise inclut les deux berges.
        if (type == BuildingType.Dam)
        {
            Building dam = Hydrology.DamAt(map, x, y);
            foreach ((int tx, int ty) in dam.Tiles)
                map.ClearFlora(tx, ty);
            colony.Buildings.Add(dam);
            SettlementPlanner.Adopt(colony, dam);
            return dam;
        }
        CommitResult result = SettlementPlanner.PlanAt(colony, KindOf(type), type, x, y);
        if (result.Building is { } planned)
            return planned;
        Building building = Raise(map, colony, type, x, y);
        SettlementPlanner.Adopt(colony, building);
        return building;
    }

    /// <summary>La création brute : on dégage le terrain de l'emprise et le chantier rejoint la colonie. Seule l'admission d'un projet (ou une migration) l'appelle.</summary>
    internal static Building Raise(LocalMap map, Colony colony, BuildingType type, int x, int y)
    {
        var building = new Building(type, x, y);
        foreach ((int tx, int ty) in building.Tiles)
            map.ClearFlora(tx, ty);
        colony.Buildings.Add(building);
        return building;
    }
}
