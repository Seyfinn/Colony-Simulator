using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Le lac qu'un barrage formerait : les cases noyées et le niveau de l'eau retenue.</summary>
public sealed record Reservoir((int X, int Y) Dam, int Level, List<(int X, int Y)> Tiles);

/// <summary>
/// L'eau retenue par les barrages. Un barrage posé sur une case de rivière relève l'eau d'un niveau : les terres
/// plates de l'amont, au niveau de la rivière ou un niveau plus haut, sont noyées (jusqu'à 160 cases).
/// En aval, la rivière coule moins fort. Rien de tout cela n'est une simulation de fluide : la retenue est
/// calculée une fois, quand le barrage est achevé.
/// </summary>
public static class Hydrology
{
    public const int MinReservoirTiles = 8;

    /// <summary>
    /// Taille maximale de la retenue. Dans une vaste plaine plate, l'eau pourrait s'étendre à l'infini : la retenue
    /// ne gagne que les cases les plus proches de la rivière, jusqu'à ce plafond.
    /// </summary>
    public const int MaxReservoirTiles = 160;

    /// <summary>Les deux culées encadrent jusqu'à trois cases de rivière.</summary>
    public const int MaxDamRiverWidth = 3;

    /// <summary>Ouvrage centré sur la rivière ; son grand côté traverse le courant.</summary>
    public static Building DamAt(LocalMap map, int riverX, int riverY)
    {
        bool side = map.RiverDownstream(riverX, riverY) is { } next && Math.Abs(next.X - riverX) > Math.Abs(next.Y - riverY);
        (int width, int height) = Building.FootprintOf(BuildingType.Dam);
        if (side) (width, height) = (height, width);
        return new Building(BuildingType.Dam, riverX - width / 2, riverY - height / 2) { Width = width, Height = height };
    }

    /// <summary>Les fondations restent dans la carte, sans condamner le camp, les champs, les accès ni les ouvrages existants.</summary>
    private static bool DamFits(LocalMap map, Building dam)
    {
        foreach ((int x, int y) in dam.Tiles)
            if (!map.InBounds(x, y) || map.IsWater(x, y) || map.IsMountain(x, y) || map.Scratch.IsBlocked(y * map.Width + x))
                return false;
        return true;
    }

    /// <summary>Distance maximale (en cases) entre le camp et le barrage.</summary>
    public const int SearchRadius = 30;

    /// <summary>Nombre de cases de rivière, en aval, dont le débit baisse.</summary>
    public const int DownstreamReach = 25;

    /// <summary>Part du débit des rivières qui reste à une colonie voisine, plus bas sur le fleuve, quand on bâtit un barrage plus haut.</summary>
    public const float NeighborFlowFactor = 0.75f;

    /// <summary>Rancune qu'un barrage fait naître chez la colonie d'aval, et son maximum.</summary>
    public const float GrudgePerDam = 1f;
    public const float MaxGrudge = 3f;

    /// <summary>Part du débit qui reste en aval d'un barrage.</summary>
    public const float DownstreamFlowFactor = 0.5f;

    private static readonly (int Dx, int Dy)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// Que noierait un barrage sur cette case de rivière ? On part des cases de rivière juste en amont et on étend l'eau
    /// sur les terres plates voisines, côté amont seulement, case après case en s'éloignant de la rivière, sans dépasser
    /// <see cref="MaxReservoirTiles"/>. Renvoie null si le barrage ne retiendrait rien d'utile (trop peu de cases),
    /// si la rivière est trop large pour être barrée, ou si la retenue engloutirait un champ, un bâtiment, une tombe ou le camp.
    /// </summary>
    public static Reservoir? FindReservoir(LocalMap map, Colony colony, int damX, int damY, Building? existing = null)
    {
        MarkWhatToProtect(map, colony, map.Scratch);
        return FindReservoirAround(map, damX, damY, existing);
    }

    /// <summary>Comme <see cref="FindReservoir"/>, une fois marqué ce qu'il faut protéger (voir <see cref="MarkWhatToProtect"/>).</summary>
    private static Reservoir? FindReservoirAround(LocalMap map, int damX, int damY, Building? existing = null)
    {
        if (!map.InBounds(damX, damY) || !map.IsRiver(damX, damY) || map.IsFlooded(damX, damY) || map.RiverWidth(damX, damY) > MaxDamRiverWidth)
            return null;
        Building ouvrage = existing ?? DamAt(map, damX, damY);
        if (!DamFits(map, ouvrage))
            return null;
        int baseLevel = map.GetElevation(damX, damY);
        int level = baseLevel + 1;
        int width = map.Width;

        // Les cases de rivière juste en amont, assez basses pour être noyées (dans l'ordre de RiverUpstream).
        SearchScratch scratch = map.Scratch;
        scratch.NewSearch();
        List<int> queue = scratch.Queue, tiles = scratch.Found;
        scratch.Visit(damY * width + damX);
        foreach ((int sx, int sy) in map.RiverUpstream(damX, damY))
        {
            int seed = sy * width + sx;
            if (map.GetElevation(sx, sy) <= level && !scratch.IsVisited(seed))
            {
                scratch.Visit(seed);
                queue.Add(seed);
            }
        }
        if (queue.Count == 0)
            return null;

        // Le sens du courant à la hauteur du barrage : l'eau ne gagne que le côté amont.
        int dirX = queue[0] % width - damX, dirY = queue[0] / width - damY;

        for (int head = 0; head < queue.Count && tiles.Count < MaxReservoirTiles; head++)
        {
            int x = queue[head] % width, y = queue[head] / width;
            if (scratch.IsBlocked(queue[head]))
                return null;
            // Les fondations ne deviennent pas un lac : le courant traverse la vanne au centre de l'ouvrage.
            if (!ouvrage.Contains(x, y))
                tiles.Add(queue[head]);

            foreach ((int dx, int dy) in Steps)
            {
                int nx = x + dx, ny = y + dy;
                if (!map.InBounds(nx, ny) || scratch.IsVisited(ny * width + nx))
                    continue;
                if ((nx - damX) * dirX + (ny - damY) * dirY <= 0)
                    continue;
                int elevation = map.GetElevation(nx, ny);
                // La retenue gagne les terres plates entre le niveau de la rivière et celui de l'eau retenue, pas la roche.
                if (map.IsMountain(nx, ny) || elevation < baseLevel || elevation > level || map.IsWater(nx, ny))
                    continue;
                scratch.Visit(ny * width + nx);
                queue.Add(ny * width + nx);
            }
        }
        return tiles.Count < MinReservoirTiles
            ? null
            : new Reservoir((damX, damY), level, tiles.Select(t => (t % width, t / width)).ToList());
    }

    /// <summary>
    /// Ce qu'une retenue ne doit pas engloutir : les cases de canal prévues, les abords du camp, les champs, les bâtiments
    /// et les tombes. Marqué une fois pour toute une recherche de site.
    /// </summary>
    private static void MarkWhatToProtect(LocalMap map, Colony colony, SearchScratch scratch)
    {
        scratch.NewObstacles();
        void Protect(int x, int y)
        {
            if (map.InBounds(x, y))
                scratch.Block(y * map.Width + x);
        }

        foreach ((int x, int y) in colony.CanalTiles)
            Protect(x, y);
        for (int y = colony.CampY - 3; y <= colony.CampY + 3; y++)
        for (int x = colony.CampX - 3; x <= colony.CampX + 3; x++)
            Protect(x, y);
        foreach (Field field in colony.Fields)
            for (int y = field.Y; y < field.Y + Field.Size; y++)
            for (int x = field.X; x < field.X + Field.Size; x++)
                Protect(x, y);
        foreach (Building building in colony.Buildings)
            foreach ((int x, int y) in building.Tiles)
                Protect(x, y);
        foreach (Grave grave in colony.Graves)
            Protect(grave.X, grave.Y);
        // La retenue ne coupe pas les accès du village : place, tracés réservés, espaces publics et abords des parcelles.
        if (colony.Map is not null) // une colonie bâtie à la main, sans carte, n'a pas encore de plan
        {
            SettlementLayout layout = colony.Layout;
            foreach (int cell in layout.PlazaCells)
                scratch.Block(cell);
            foreach (RoadSegment segment in layout.RoadSegments)
                foreach (int cell in segment.Cells)
                    scratch.Block(cell);
            foreach (PlotReservation parcel in layout.ActiveParcels)
                if (parcel.Kind == ParcelKind.PublicSpace)
                    foreach ((int x, int y) in parcel.Tiles)
                        Protect(x, y);
        }
    }

    /// <summary>
    /// Le meilleur emplacement de barrage près du camp : une retenue assez grande, d'autant meilleure qu'elle
    /// borde des champs (de l'eau à portée) et qu'elle est proche du camp.
    /// </summary>
    public static (int X, int Y, Reservoir Reservoir)? FindSite(LocalMap map, Colony colony)
    {
        (int X, int Y, Reservoir Reservoir)? best = null;
        float bestScore = float.MinValue;
        MarkWhatToProtect(map, colony, map.Scratch);

        for (int dy = -SearchRadius; dy <= SearchRadius; dy++)
        for (int dx = -SearchRadius; dx <= SearchRadius; dx++)
        {
            int x = colony.CampX + dx, y = colony.CampY + dy;
            if (!map.InBounds(x, y) || !map.IsRiver(x, y) || NearDam(colony, x, y))
                continue;
            if (FindReservoirAround(map, x, y) is not { } reservoir)
                continue;

            int fieldsNearby = colony.Fields.Count(f => reservoir.Tiles.Any(t =>
                Math.Max(Math.Abs(t.X - (f.X + Field.Size / 2)), Math.Abs(t.Y - (f.Y + Field.Size / 2))) <= 7));
            float score = reservoir.Tiles.Count + 12f * fieldsNearby - 0.4f * Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (score > bestScore)
            {
                bestScore = score;
                best = (x, y, reservoir);
            }
        }
        return best;
    }

    /// <summary>Un barrage se tient déjà à moins de six cases.</summary>
    private static bool NearDam(Colony colony, int x, int y)
    {
        foreach (Building b in colony.Buildings)
            if (b.IsDam && Math.Max(Math.Abs(b.RiverX - x), Math.Abs(b.RiverY - y)) < 6)
                return true;
        return false;
    }

    /// <summary>
    /// Le débit qui fait tourner un moulin : celui de la rivière qui le longe (moindre si un barrage en amont la retient),
    /// 1 au bord d'un lac ou d'un canal. 0 si aucune eau ne touche le bâtiment.
    /// </summary>
    public static float MillFlow(LocalMap map, Building mill)
    {
        float best = 0f;
        for (int y = mill.Y - 1; y <= mill.Y + mill.Height; y++)
        for (int x = mill.X - 1; x <= mill.X + mill.Width; x++)
        {
            bool inside = x >= mill.X && x < mill.X + mill.Width && y >= mill.Y && y < mill.Y + mill.Height;
            bool corner = (x < mill.X || x >= mill.X + mill.Width) && (y < mill.Y || y >= mill.Y + mill.Height);
            if (inside || corner || !map.InBounds(x, y))
                continue;
            if (map.IsFlooded(x, y) || map.IsCanalWet(x, y))
                best = Math.Max(best, 1f);
            else if (map.IsRiver(x, y))
                best = Math.Max(best, map.GetFlow(x, y));
        }
        return best;
    }

    /// <summary>Outil de développement : bâtit tout de suite le meilleur barrage possible, sans prière ni travail.</summary>
    public static Building? BuildInstantly(LocalMap map, Colony colony)
    {
        if (FindSite(map, colony) is not { } site)
            return null;
        Building dam = Urbanism.PlanBuilding(map, colony, BuildingType.Dam, site.X, site.Y);
        dam.Progress = 1f;
        CompleteDam(map, colony, dam);
        return dam;
    }

    /// <summary>
    /// Le barrage est achevé : l'eau monte en amont, le débit baisse en aval. Renvoie la retenue formée,
    /// ou null si le terrain a changé et que le barrage ne retient plus rien.
    /// </summary>
    public static Reservoir? CompleteDam(LocalMap map, Colony colony, Building dam)
    {
        // Le barrage lui-même ne doit pas compter comme un obstacle à protéger : on le met de côté le temps du calcul.
        colony.Buildings.Remove(dam);
        Reservoir? reservoir = FindReservoir(map, colony, dam.RiverX, dam.RiverY, dam);
        colony.Buildings.Add(dam);
        if (reservoir is null)
            return null;

        map.Flood(reservoir.Tiles, reservoir.Level);

        (int X, int Y)? next = map.RiverDownstream(dam.RiverX, dam.RiverY);
        for (int i = 0; i < DownstreamReach && next is { } tile && map.IsRiver(tile.X, tile.Y); i++)
        {
            map.ReduceFlow(tile.X, tile.Y, DownstreamFlowFactor);
            next = map.RiverDownstream(tile.X, tile.Y);
        }

        // Plus bas sur le même fleuve, l'établissement suivant reçoit moins d'eau (de la même colonie ou d'une autre) : le calcul est territorial.
        if (colony.LocalSettlement.Downstream is { Status: not SettlementStatus.Closed } lower)
        {
            lower.Map.ScaleRiverFlows(NeighborFlowFactor);
            if (lower.Owner != colony)
            {
                lower.Owner.Grudges[colony] = Math.Min(MaxGrudge, lower.Owner.GrudgeAgainst(colony) + GrudgePerDam);
                ColonyBrain.Say(lower.Owner, colony.Clock, $"Le barrage de {colony.Name} retient l'eau : notre rivière coule moins fort. On leur en veut.");
            }
            else
                ColonyBrain.Say(colony, colony.Clock, $"Le barrage retient l'eau : la rivière coule moins fort à {lower.Name}.");
        }
        return reservoir;
    }
}
