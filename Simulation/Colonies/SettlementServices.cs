using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Ce qu'un lieu de la colonie permet de faire. Un espace de rencontre ne vaut pas automatiquement un point de dépôt.</summary>
[Flags]
public enum ServiceUse
{
    None = 0,
    /// <summary>On y mange (le repas est retiré une seule fois du stock commun, au début de l'action).</summary>
    Meal = 1,
    /// <summary>On s'y retrouve, on s'y détend, on y bavarde.</summary>
    Meet = 2,
    /// <summary>On y dépose une récolte et l'on y prend des matériaux : un accès au stock commun.</summary>
    Stock = 4,
}

/// <summary>
/// Un lieu où les habitants se rendent : valeur dérivée des bâtiments achevés et des espaces publics, jamais sauvegardée. Son identifiant est stable
/// (issu de son camp, de son bâtiment ou de sa parcelle) ; ses cellules sont celles où l'on se tient pour l'utiliser.
/// </summary>
public sealed record ServicePoint(int Id, ServiceUse Uses, int DistrictId, int[] Cells)
{
    public bool Provides(ServiceUse use) => (Uses & use) == use;
}

/// <summary>
/// Les lieux de repas, de rencontre, de dépôt et de récupération de matériaux de la colonie. Il n'y a qu'<b>un</b> stock économique commun : le camp
/// et les entrepôts <i>achevés</i> sont les points physiques où l'on y accède. Un entrepôt en chantier n'est jamais un service disponible. Cette version
/// abstrait l'approvisionnement collectif des repas : pas d'inventaires locaux, pas de charrettes entre dépôts.
/// </summary>
public static class SettlementServices
{
    public const int CampId = 1;
    private const int BuildingBase = 1000;
    private const int PublicBase = 100000;

    /// <summary>Nombre de cases de la clairière où l'on se tient pour manger ou déposer : les plus proches du feu.</summary>
    private const int CampCells = 12;

    /// <summary>Un espace public secondaire ne devient un lieu de repas que lorsqu'un logement achevé le jouxte.</summary>
    private const int PublicSpaceReach = 8;

    /// <summary>Les lieux utilisables maintenant, dans un ordre stable (le camp d'abord, puis par identifiant).</summary>
    public static IReadOnlyList<ServicePoint> Points(Colony colony)
    {
        ServiceCache cache = colony.ServiceCache ??= new ServiceCache();
        SettlementLayout layout = colony.Layout;
        int completed = 0;
        foreach (Building building in colony.Buildings)
            if (building.IsComplete)
                completed++;
        if (cache.Points is not null && cache.Completed == completed && cache.Buildings == colony.Buildings.Count && cache.LayoutRevision == layout.Revision)
            return cache.Points;

        var points = new List<ServicePoint>
        {
            new(CampId, ServiceUse.Meal | ServiceUse.Meet | ServiceUse.Stock, layout.FirstOf(DistrictKind.Civic)?.Id ?? -1, CampInteractionCells(colony)),
        };
        foreach (Building building in colony.Buildings)
        {
            if (!building.IsComplete)
                continue;
            ServiceUse uses = building.Type switch
            {
                BuildingType.Storehouse or BuildingType.MineDepot => ServiceUse.Stock,
                BuildingType.Tavern => ServiceUse.Meal | ServiceUse.Meet,
                _ => ServiceUse.None,
            };
            if (uses != ServiceUse.None)
                points.Add(new ServicePoint(BuildingBase + building.Id, uses, building.DistrictId, WorkCells(colony, building)));
        }
        foreach (PlotReservation parcel in layout.Parcels)
        {
            if (parcel.Kind != ParcelKind.PublicSpace || parcel.State == ReservationState.Released || !HasCompleteHutNear(colony, parcel))
                continue;
            points.Add(new ServicePoint(PublicBase + parcel.Id, ServiceUse.Meal | ServiceUse.Meet, parcel.DistrictId,
                parcel.Tiles.Select(t => layout.Cell(t.X, t.Y)).ToArray()));
        }
        points.Sort((a, b) => a.Id.CompareTo(b.Id));
        cache.Points = points;
        cache.Completed = completed;
        cache.Buildings = colony.Buildings.Count;
        cache.LayoutRevision = layout.Revision;
        return points;
    }

    private static int[] CampInteractionCells(Colony colony)
    {
        SettlementLayout layout = colony.Layout;
        var cells = new List<int>();
        foreach ((int x, int y) in colony.GatherSpots)
        {
            if (cells.Count == CampCells)
                break;
            cells.Add(layout.Cell(x, y));
        }
        return cells.ToArray();
    }

    private static bool HasCompleteHutNear(Colony colony, PlotReservation space)
    {
        foreach (Building building in colony.Buildings)
            if (building.IsHut && building.IsComplete && building.DistrictId == space.DistrictId
                && Math.Max(Math.Abs(building.X - space.X), Math.Abs(building.Y - space.Y)) <= PublicSpaceReach)
                return true;
        return false;
    }

    /// <summary>
    /// Les cases où l'on se tient pour travailler dans un bâtiment, ou le servir : son accès, puis les cases libres qui le touchent (au plus trois),
    /// dans un ordre stable. Un bâtiment sans porte (parcelle ancienne) se travaille dans son emprise, comme avant.
    /// </summary>
    public static int[] WorkCells(Colony colony, Building building)
    {
        SettlementLayout layout = colony.Layout;
        LocalMap map = colony.Map;
        if (!building.HasDoor)
            return building.Tiles.Select(t => layout.Cell(t.X, t.Y)).ToArray();

        LocalSpatialIndex index = colony.Spatial;
        var cells = new List<int> { layout.Cell(building.AccessX, building.AccessY) };
        foreach ((int dx, int dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, 1), (1, -1), (-1, -1) })
        {
            if (cells.Count == 3)
                break;
            int x = building.AccessX + dx, y = building.AccessY + dy;
            if (!map.InBounds(x, y) || !map.IsWalkable(x, y) || map.IsWaterway(x, y) || building.Contains(x, y)
                || index.Has(x, y, CellUse.Building | CellUse.Field | CellUse.Grave))
                continue;
            if (!map.CanStep(building.AccessX, building.AccessY, x, y))
                continue;
            cells.Add(layout.Cell(x, y));
        }
        return cells.ToArray();
    }

    /// <summary>Le service de cet usage le plus proche (à vol d'oiseau) d'un point, ou null. À distance égale, l'identifiant le plus bas.</summary>
    public static ServicePoint? Nearest(Colony colony, ServiceUse use, int x, int y)
    {
        ServicePoint? best = null;
        float bestDistance = float.MaxValue;
        foreach (ServicePoint point in Points(colony))
        {
            if (!point.Provides(use))
                continue;
            float distance = DistanceTo(colony, point, x, y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }
        return best;
    }

    /// <summary>La distance octile (en cases) du point à la case de service la plus proche.</summary>
    public static float DistanceTo(Colony colony, ServicePoint point, int x, int y)
    {
        SettlementLayout layout = colony.Layout;
        float best = float.MaxValue;
        foreach (int cell in point.Cells)
        {
            (int cx, int cy) = layout.Decode(cell);
            best = Math.Min(best, Pathfinding.TraversalCost.Octile(cx - x, cy - y));
        }
        return best;
    }

    /// <summary>La case de service du point la plus proche (à vol d'oiseau) d'un point donné ; la première à distance égale.</summary>
    public static int NearestCell(Colony colony, ServicePoint point, int x, int y)
    {
        SettlementLayout layout = colony.Layout;
        int bestCell = point.Cells[0];
        float best = float.MaxValue;
        foreach (int cell in point.Cells)
        {
            (int cx, int cy) = layout.Decode(cell);
            float distance = Pathfinding.TraversalCost.Octile(cx - x, cy - y);
            if (distance < best)
            {
                best = distance;
                bestCell = cell;
            }
        }
        return bestCell;
    }
}

/// <summary>Les lieux dérivés, gardés tant que ni les bâtiments ni le plan ne changent (jamais sauvegardés).</summary>
internal sealed class ServiceCache
{
    internal IReadOnlyList<ServicePoint>? Points;
    internal int Completed, Buildings, LayoutRevision;
}
