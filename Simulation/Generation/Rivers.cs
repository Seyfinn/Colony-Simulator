using GodColony.Simulation.Map;

namespace GodColony.Simulation.Generation;

/// <summary>
/// Trace les rivières : chacune naît sur un flanc de montagne et ne fait que descendre (ou longer un replat)
/// jusqu'au lac, à la mer ou à une autre rivière. Le chemin est le moins coûteux, avec un peu de hasard
/// pour qu'il serpente au lieu de couper tout droit.
/// </summary>
public static class Rivers
{
    private const int RiverCount = 2;

    /// <summary>Une rivière plus courte que cela n'est qu'un ruisseau sans intérêt : on cherche une autre source.</summary>
    private const int MinLength = 40;

    /// <summary>Deux sources doivent être éloignées, pour que les rivières couvrent des régions différentes.</summary>
    private const int MinSourceSpacing = 50;

    private const int SourceTries = 800;
    private const int EdgeMargin = 6;

    private static readonly (int Dx, int Dy, float Cost)[] Moves =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.41421356f), (1, -1, 1.41421356f), (-1, 1, 1.41421356f), (-1, -1, 1.41421356f),
    ];

    /// <summary>Les cases de toutes les rivières, de la source à l'embouchure.</summary>
    public static List<(int X, int Y)> Generate(int[] elevation, int width, int height, int seed)
    {
        var river = new bool[width * height];
        var sources = new List<(int X, int Y)>();
        var tiles = new List<(int X, int Y)>();

        for (int attempt = 0; attempt < SourceTries && sources.Count < RiverCount; attempt++)
        {
            int x = EdgeMargin + (int)(Noise.Hash01(attempt, 1, 91, seed) * (width - 2 * EdgeMargin));
            int y = EdgeMargin + (int)(Noise.Hash01(attempt, 2, 91, seed) * (height - 2 * EdgeMargin));
            int e = elevation[y * width + x];
            // Sur un flanc de montagne : assez haut pour descendre longtemps, pas tout en haut des sommets.
            if (e < LocalMap.MountainElevation + 1 || e > LocalMap.MountainElevation + 2)
                continue;
            if (sources.Any(s => Math.Max(Math.Abs(s.X - x), Math.Abs(s.Y - y)) < MinSourceSpacing))
                continue;

            List<(int X, int Y)>? path = Flow(elevation, river, width, height, x, y, seed + attempt);
            if (path is null || path.Count < MinLength)
                continue;

            sources.Add((x, y));
            foreach ((int px, int py) in path)
            {
                river[py * width + px] = true;
                tiles.Add((px, py));
            }
        }
        return tiles;
    }

    /// <summary>Le chemin le moins coûteux de la source jusqu'à l'eau, sans jamais remonter.</summary>
    private static List<(int X, int Y)>? Flow(int[] elevation, bool[] river, int width, int height, int sourceX, int sourceY, int seed)
    {
        int n = width * height;
        var cost = new float[n];
        var parent = new int[n];
        Array.Fill(cost, float.MaxValue);
        Array.Fill(parent, -1);
        var open = new PriorityQueue<int, float>();
        int start = sourceY * width + sourceX;
        cost[start] = 0f;
        open.Enqueue(start, 0f);

        while (open.TryDequeue(out int current, out float priority))
        {
            if (priority > cost[current])
                continue;
            int cx = current % width, cy = current / width;

            // Arrivée : un lac, la mer, ou une rivière déjà tracée (confluent).
            if (current != start && (elevation[current] <= LocalMap.WaterLevel || river[current]))
                return Trace(parent, width, current, elevation);

            foreach ((int dx, int dy, float step) in Moves)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;
                int next = ny * width + nx;
                if (elevation[next] > elevation[current])
                    continue;

                // L'eau préfère descendre ; sur un replat, le hasard la fait serpenter.
                float wander = 1f + 1.2f * Noise.Hash01(nx, ny, 93, seed);
                float slope = elevation[next] < elevation[current] ? 0.6f : 1f;
                float total = cost[current] + step * wander * slope;
                if (total < cost[next])
                {
                    cost[next] = total;
                    parent[next] = current;
                    open.Enqueue(next, total);
                }
            }
        }
        return null;
    }

    /// <summary>Remonte le chemin jusqu'à la source ; le dernier élément (l'embouchure) n'est une rivière que s'il n'est pas déjà de l'eau.</summary>
    private static List<(int X, int Y)> Trace(int[] parent, int width, int end, int[] elevation)
    {
        var path = new List<(int X, int Y)>();
        for (int i = end; i >= 0; i = parent[i])
            path.Add((i % width, i / width));
        path.Reverse();
        if (elevation[end] <= LocalMap.WaterLevel)
            path.RemoveAt(path.Count - 1);
        return path;
    }
}
