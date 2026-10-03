using GodColony.Simulation.Map;

namespace GodColony.Simulation.Pathfinding;

/// <summary>
/// Recherche de chemin A* sur la carte locale, en 8 directions.
/// Respecte le relief : on ne franchit qu'un niveau à la fois, et monter coûte plus que descendre.
/// </summary>
public sealed class Pathfinder
{
    private static readonly (int Dx, int Dy)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    private const float Diagonal = 1.41421356f;
    private const float UphillPenalty = 0.5f;

    private readonly LocalMap _map;
    private readonly float[] _cost;
    private readonly int[] _parent;
    private readonly int[] _seen;
    private readonly int[] _closed;
    private readonly PriorityQueue<int, float> _open = new();
    private int _generation;

    public Pathfinder(LocalMap map)
    {
        _map = map;
        int n = map.Width * map.Height;
        _cost = new float[n];
        _parent = new int[n];
        _seen = new int[n];
        _closed = new int[n];
    }

    /// <summary>
    /// Renvoie la liste des cases à parcourir (sans la case de départ), ou null si la destination est inaccessible.
    /// </summary>
    public List<(int X, int Y)>? FindPath(int startX, int startY, int goalX, int goalY, int maxExpanded = 40000)
    {
        if (!_map.IsWalkable(goalX, goalY) || !_map.InBounds(startX, startY))
            return null;
        if (startX == goalX && startY == goalY)
            return [];

        _generation++;
        _open.Clear();
        int width = _map.Width;
        int start = startY * width + startX, goal = goalY * width + goalX;
        _cost[start] = 0;
        _seen[start] = _generation;
        _parent[start] = -1;
        _open.Enqueue(start, Heuristic(startX, startY, goalX, goalY));

        int expanded = 0;
        while (_open.TryDequeue(out int current, out _))
        {
            if (_closed[current] == _generation)
                continue;
            _closed[current] = _generation;
            if (current == goal)
                return BuildPath(goal, width);
            if (++expanded > maxExpanded)
                return null;

            int cx = current % width, cy = current / width;
            foreach ((int dx, int dy) in Directions)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!_map.CanStep(cx, cy, nx, ny))
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                // En diagonale, on ne coupe pas les coins : les deux cases adjacentes doivent être praticables.
                if (diagonal && (!_map.CanStep(cx, cy, cx + dx, cy) || !_map.CanStep(cx, cy, cx, cy + dy)))
                    continue;

                int next = ny * width + nx;
                if (_closed[next] == _generation)
                    continue;

                float step = (diagonal ? Diagonal : 1f) * _map.MoveCost(nx, ny);
                if (_map.GetElevation(nx, ny) > _map.GetElevation(cx, cy))
                    step += UphillPenalty;
                float cost = _cost[current] + step;
                if (_seen[next] == _generation && cost >= _cost[next])
                    continue;

                _seen[next] = _generation;
                _cost[next] = cost;
                _parent[next] = current;
                _open.Enqueue(next, cost + Heuristic(nx, ny, goalX, goalY));
            }
        }
        return null;
    }

    private List<(int X, int Y)> BuildPath(int goal, int width)
    {
        var path = new List<(int X, int Y)>();
        for (int node = goal; _parent[node] != -1; node = _parent[node])
            path.Add((node % width, node / width));
        path.Reverse();
        return path;
    }

    /// <summary>Distance « octile » : la plus courte possible en 8 directions sur terrain plat.</summary>
    private static float Heuristic(int x, int y, int goalX, int goalY)
    {
        int dx = Math.Abs(x - goalX), dy = Math.Abs(y - goalY);
        return dx + dy + (Diagonal - 2f) * Math.Min(dx, dy);
    }
}
