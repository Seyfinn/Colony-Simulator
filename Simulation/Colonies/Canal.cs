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

    /// <summary>Un canal ne doit pas passer sur un bâtiment, un champ, une tombe, le feu ou l'eau.</summary>
    private static bool CanDig(LocalMap map, Colony colony, int x, int y)
    {
        if (!map.InBounds(x, y) || !map.IsWalkable(x, y) || map.IsWaterway(x, y) || map.IsMountain(x, y))
            return false;
        if (colony.CanalTiles.Contains((x, y)) || Math.Max(Math.Abs(x - colony.CampX), Math.Abs(y - colony.CampY)) <= 2)
            return false;
        foreach (Building b in colony.Buildings)
            if (x >= b.X - 1 && x <= b.X + b.Width && y >= b.Y - 1 && y <= b.Y + b.Height)
                return false;
        foreach (Field f in colony.Fields)
            if (f.Contains(x, y))
                return false;
        foreach (Grave g in colony.Graves)
            if (g.X == x && g.Y == y)
                return false;
        return true;
    }

    /// <summary>
    /// Une source possible pour la case (x, y) : une case de rivière ou de canal en eau, voisine et au moins aussi haute.
    /// </summary>
    private static bool TouchesSource(LocalMap map, int x, int y)
    {
        foreach ((int dx, int dy) in Steps)
        {
            int sx = x + dx, sy = y + dy;
            if (map.InBounds(sx, sy) && (map.IsRiver(sx, sy) || map.IsCanalWet(sx, sy)) && map.GetElevation(sx, sy) >= map.GetElevation(x, y))
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
        var cost = new Dictionary<(int X, int Y), float>();
        var parent = new Dictionary<(int X, int Y), (int X, int Y)>();
        var open = new PriorityQueue<(int X, int Y), float>();

        for (int y = field.Y - 1; y <= field.Y + Field.Size; y++)
        for (int x = field.X - 1; x <= field.X + Field.Size; x++)
        {
            bool border = x < field.X || x >= field.X + Field.Size || y < field.Y || y >= field.Y + Field.Size;
            bool corner = (x < field.X || x >= field.X + Field.Size) && (y < field.Y || y >= field.Y + Field.Size);
            if (!border || corner || !CanDig(map, colony, x, y))
                continue;
            cost[(x, y)] = StepCost(map, x, y);
            open.Enqueue((x, y), cost[(x, y)]);
        }

        while (open.TryDequeue(out (int X, int Y) tile, out float priority))
        {
            if (priority > cost[tile])
                continue;
            if (priority > MaxLength)
                return null;
            if (TouchesSource(map, tile.X, tile.Y))
            {
                // On a remonté jusqu'à l'eau : le chemin de la source au champ suit les parents.
                var route = new List<(int X, int Y)> { tile };
                while (parent.TryGetValue(route[^1], out (int X, int Y) next))
                    route.Add(next);
                return route;
            }

            foreach ((int dx, int dy) in Steps)
            {
                (int X, int Y) upstream = (tile.X + dx, tile.Y + dy);
                if (!CanDig(map, colony, upstream.X, upstream.Y))
                    continue;
                int rise = map.GetElevation(upstream.X, upstream.Y) - map.GetElevation(tile.X, tile.Y);
                if (rise is < 0 or > 1)
                    continue;
                float total = priority + StepCost(map, upstream.X, upstream.Y);
                if (!cost.TryGetValue(upstream, out float known) || total < known)
                {
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
