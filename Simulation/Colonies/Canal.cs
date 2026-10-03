using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Un canal d'irrigation : une suite de cases creusées, de la rivière (ou d'un canal déjà en eau) jusqu'à un champ.
/// L'eau n'avance qu'à mesure que le fossé est creusé sans interruption depuis la source.
/// </summary>
public sealed class Canal
{
    private readonly bool[] _dug;

    /// <param name="tiles">Les cases à creuser, de la source jusqu'au champ ; chacune touche la précédente.</param>
    public Canal(List<(int X, int Y)> tiles, Field target)
    {
        Tiles = tiles;
        Target = target;
        _dug = new bool[tiles.Count];
    }

    /// <summary>De la source (près de l'eau) à l'arrivée (près du champ).</summary>
    public IReadOnlyList<(int X, int Y)> Tiles { get; }

    /// <summary>Le champ que ce canal doit irriguer.</summary>
    public Field Target { get; }

    /// <summary>Nombre de cases où l'eau coule déjà : les cases creusées d'affilée depuis la source.</summary>
    public int Flowing { get; private set; }

    public int DugCount => _dug.Count(d => d);
    public bool IsComplete => DugCount == Tiles.Count;

    public bool IsDug(int index) => _dug[index];

    /// <summary>Les cases qui restent à creuser.</summary>
    public IEnumerable<(int X, int Y)> TilesToDig() => Tiles.Where((_, i) => !_dug[i]);

    public bool Contains(int x, int y) => Tiles.Contains((x, y));

    /// <summary>Une case vient d'être creusée : l'eau avance si le fossé est désormais continu. Renvoie les cases qui se remplissent.</summary>
    internal List<(int X, int Y)> MarkDug(int x, int y)
    {
        int index = ((List<(int X, int Y)>)Tiles).IndexOf((x, y));
        var filled = new List<(int X, int Y)>();
        if (index < 0)
            return filled;
        _dug[index] = true;
        while (Flowing < Tiles.Count && _dug[Flowing])
        {
            filled.Add(Tiles[Flowing]);
            Flowing++;
        }
        return filled;
    }
}

/// <summary>
/// Trace des canaux d'irrigation : le chemin le moins coûteux de l'eau jusqu'au champ, sans jamais remonter.
/// On part du champ et on remonte vers l'eau, car l'eau ne coule que vers le bas.
/// </summary>
public static class Irrigation
{
    /// <summary>Longueur maximale d'un canal (en cases) : au-delà, la colonie renonce.</summary>
    public const int MaxLength = 28;

    /// <summary>On irrigue un champ dont moins de la moitié des parcelles le sont déjà.</summary>
    private const float WellIrrigated = 0.5f;

    private static readonly (int Dx, int Dy)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Part des parcelles du champ qui sont irriguées.</summary>
    public static float IrrigatedShare(LocalMap map, Field field) =>
        field.Plots.Count(p => map.IsIrrigated(p.X, p.Y)) / (float)field.Plots.Count;

    /// <summary>Les champs qui méritent encore un canal, et qu'aucun canal (même en chantier) ne dessert.</summary>
    public static IEnumerable<Field> FieldsToIrrigate(LocalMap map, Colony colony) =>
        colony.Fields.Where(f => IrrigatedShare(map, f) < WellIrrigated && !colony.Canals.Any(c => c.Target == f));

    /// <summary>Le meilleur canal vers un des champs à irriguer (le plus court), ou null s'il n'y en a pas.</summary>
    public static Canal? PlanBest(LocalMap map, Colony colony)
    {
        Canal? best = null;
        foreach (Field field in FieldsToIrrigate(map, colony))
            if (FindRoute(map, colony, field) is { } route && (best is null || route.Count < best.Tiles.Count))
                best = new Canal(route, field);
        return best;
    }

    /// <summary>
    /// Ce qu'un canal ne doit pas traverser, en plus de l'eau et de la roche : les cases de canal déjà prévues, les abords du feu,
    /// les bâtiments et le passage qui les entoure, les champs et les tombes. Marqué une fois pour tout un tracé.
    /// </summary>
    private static void MarkObstacles(LocalMap map, Colony colony, SearchScratch scratch)
    {
        scratch.NewObstacles();
        void Block(int x, int y)
        {
            if (map.InBounds(x, y))
                scratch.Block(y * map.Width + x);
        }

        foreach ((int x, int y) in colony.CanalTiles)
            Block(x, y);
        for (int y = colony.CampY - 2; y <= colony.CampY + 2; y++)
        for (int x = colony.CampX - 2; x <= colony.CampX + 2; x++)
            Block(x, y);
        foreach (Building b in colony.Buildings)
            for (int y = b.Y - 1; y <= b.Y + b.Height; y++)
            for (int x = b.X - 1; x <= b.X + b.Width; x++)
                Block(x, y);
        foreach (Field f in colony.Fields)
            for (int y = f.Y; y < f.Y + Field.Size; y++)
            for (int x = f.X; x < f.X + Field.Size; x++)
                Block(x, y);
        foreach (Grave g in colony.Graves)
            Block(g.X, g.Y);
    }

    /// <summary>Un canal ne doit pas passer sur un bâtiment, un champ, une tombe, le feu ou l'eau (voir <see cref="MarkObstacles"/>).</summary>
    private static bool CanDig(LocalMap map, SearchScratch scratch, int x, int y) =>
        map.InBounds(x, y) && map.IsWalkable(x, y) && !map.IsWaterway(x, y) && !map.IsMountain(x, y)
        && !scratch.IsBlocked(y * map.Width + x);

    /// <summary>
    /// Une source possible pour la case (x, y) : une case de rivière ou de canal en eau, voisine et au moins aussi haute.
    /// </summary>
    private static bool TouchesSource(LocalMap map, int x, int y)
    {
        foreach ((int dx, int dy) in Steps)
        {
            int sx = x + dx, sy = y + dy;
            if (map.InBounds(sx, sy) && (map.IsRiver(sx, sy) || map.IsCanalWet(sx, sy) || map.IsFlooded(sx, sy))
                && map.WaterHeight(sx, sy) >= map.GetElevation(x, y))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Du champ vers l'eau : on part des cases qui bordent le champ, on remonte de case en case (jamais plus de
    /// un niveau, jamais vers le bas) jusqu'à toucher une rivière ou un canal en eau. Renvoie le chemin de la source au champ.
    /// </summary>
    public static List<(int X, int Y)>? FindRoute(LocalMap map, Colony colony, Field field)
    {
        // Les cases sont repérées par leur rang sur la carte ; une case sans précédent (-1) est un départ, au bord du champ.
        SearchScratch scratch = map.Scratch;
        MarkObstacles(map, colony, scratch);
        scratch.NewSearch();
        float[] cost = scratch.Cost;
        int[] parent = scratch.Parent;
        PriorityQueue<int, float> open = scratch.Open;
        int width = map.Width;

        for (int y = field.Y - 1; y <= field.Y + Field.Size; y++)
        for (int x = field.X - 1; x <= field.X + Field.Size; x++)
        {
            bool border = x < field.X || x >= field.X + Field.Size || y < field.Y || y >= field.Y + Field.Size;
            bool corner = (x < field.X || x >= field.X + Field.Size) && (y < field.Y || y >= field.Y + Field.Size);
            if (!border || corner || !CanDig(map, scratch, x, y))
                continue;
            int index = y * width + x;
            scratch.Visit(index);
            cost[index] = StepCost(map, x, y);
            parent[index] = -1;
            open.Enqueue(index, cost[index]);
        }

        while (open.TryDequeue(out int tile, out float priority))
        {
            if (priority > cost[tile])
                continue;
            if (priority > MaxLength)
                return null;
            int tx = tile % width, ty = tile / width;
            if (TouchesSource(map, tx, ty))
            {
                // On a remonté jusqu'à l'eau : le chemin de la source au champ suit les parents.
                var route = new List<(int X, int Y)> { (tx, ty) };
                for (int next = parent[tile]; next >= 0; next = parent[next])
                    route.Add((next % width, next / width));
                return route;
            }

            foreach ((int dx, int dy) in Steps)
            {
                int ux = tx + dx, uy = ty + dy;
                if (!CanDig(map, scratch, ux, uy))
                    continue;
                int rise = map.GetElevation(ux, uy) - map.GetElevation(tx, ty);
                if (rise is < 0 or > 1)
                    continue;
                float total = priority + StepCost(map, ux, uy);
                int upstream = uy * width + ux;
                if (!scratch.IsVisited(upstream) || total < cost[upstream])
                {
                    scratch.Visit(upstream);
                    cost[upstream] = total;
                    parent[upstream] = tile;
                    open.Enqueue(upstream, total);
                }
            }
        }
        return null;
    }

    /// <summary>Creuser est plus long à travers une forêt : il faut arracher les arbres.</summary>
    private static float StepCost(LocalMap map, int x, int y) => map.GetFlora(x, y) is FloraType.Tree or FloraType.Bush ? 1.6f : 1f;
}
