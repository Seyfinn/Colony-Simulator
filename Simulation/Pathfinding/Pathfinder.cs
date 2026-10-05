using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.Simulation.Pathfinding;

/// <summary>
/// Recherche de chemin A* sur la carte locale, en 8 directions.
/// Respecte le relief : on ne franchit qu'un niveau à la fois, et monter coûte plus que descendre. Le coût d'un pas est la durée réelle de son
/// parcours (<see cref="TraversalCost"/>) : les sentiers et chemins de terre le raccourcissent, exactement comme pour le mouvement des colons.
/// Quand il est rattaché à une colonie, les emprises de ses bâtiments sont des obstacles (on les contourne ; on y entre par leur porte, voir
/// <see cref="LocalNavigation"/>) ; la case de départ est toujours permise, pour qu'on puisse ressortir d'un bâtiment.
/// </summary>
public sealed class Pathfinder
{
    private static readonly (int Dx, int Dy)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// Au-delà de ce nombre de cases explorées sans arriver, on vérifie que le but est seulement atteignable :
    /// sans cela, un but inaccessible fait explorer toute la carte avant qu'on y renonce.
    /// </summary>
    private const int ExpandedBeforeCheck = 256;

    /// <summary>
    /// Un but d'où partent plus de chemins que cela n'est pas isolé : on n'en cherche pas plus et la recherche continue.
    /// Les buts inaccessibles sont presque toujours perchés sur un petit replat (la roche que vise un mineur).
    /// </summary>
    private const int MaxIsolatedTiles = 1024;

    private readonly LocalMap _map;
    private readonly float[] _cost;
    private readonly int[] _parent;
    private readonly int[] _seen;
    private readonly int[] _closed;
    private readonly PriorityQueue<int, float> _open = new();
    private int _generation;

    // Pour remonter depuis le but : les cases déjà vues et la file des cases à examiner.
    private readonly int[] _backSeen;
    private readonly int[] _backQueue = new int[MaxIsolatedTiles];
    private int _backGeneration;

    /// <summary>La colonie dont les bâtiments sont des obstacles (null : le terrain nu, comme pour les tests du pathfinder seul).</summary>
    internal Colony? Owner { get; set; }

    public Pathfinder(LocalMap map)
    {
        _map = map;
        int n = map.Width * map.Height;
        _cost = new float[n];
        _parent = new int[n];
        _seen = new int[n];
        _closed = new int[n];
        _backSeen = new int[n];
    }

    /// <summary>
    /// Renvoie la liste des cases à parcourir (sans la case de départ), ou null si la destination est inaccessible.
    /// </summary>
    public List<(int X, int Y)>? FindPath(int startX, int startY, int goalX, int goalY, int maxStep = 1, int maxExpanded = 40000)
    {
        if (!_map.IsWalkable(goalX, goalY) || !_map.InBounds(startX, startY))
            return null;
        if (startX == goalX && startY == goalY)
            return [];
        LocalSpatialIndex? index = Owner?.Spatial;
        // Un but à l'intérieur d'une emprise ne s'atteint pas par l'extérieur : c'est à la navigation d'ajouter la porte.
        if (index is not null && index.BlocksWalking(goalX, goalY))
            return null;

        _generation++;
        _open.Clear();
        int width = _map.Width;
        int start = startY * width + startX, goal = goalY * width + goalX;
        float minStep = _map.Roads.MinFactor * LocalMap.RuggednessMin / SettlementRules.WalkTilesPerSecond;
        _cost[start] = 0;
        _seen[start] = _generation;
        _parent[start] = -1;
        _open.Enqueue(start, Heuristic(startX, startY, goalX, goalY, minStep));

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
            if (expanded == ExpandedBeforeCheck && IsCutOff(startX, startY, goalX, goalY, maxStep, index))
                return null;

            int cx = current % width, cy = current / width;
            foreach ((int dx, int dy) in Directions)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!_map.CanStep(cx, cy, nx, ny, maxStep))
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                // En diagonale, on ne coupe pas les coins : les deux cases adjacentes doivent être praticables.
                if (diagonal && (!_map.CanStep(cx, cy, cx + dx, cy, maxStep) || !_map.CanStep(cx, cy, cx, cy + dy, maxStep)))
                    continue;

                int next = ny * width + nx;
                if (_closed[next] == _generation)
                    continue;
                if (index is not null && index.BlocksWalking(next) && next != goal)
                    continue;

                float cost = _cost[current] + TraversalCost.StepSeconds(_map, cx, cy, nx, ny);
                if (_seen[next] == _generation && cost >= _cost[next])
                    continue;

                _seen[next] = _generation;
                _cost[next] = cost;
                _parent[next] = current;
                _open.Enqueue(next, cost + Heuristic(nx, ny, goalX, goalY, minStep));
            }
        }
        return null;
    }

    /// <summary>
    /// Vrai si le but est hors d'atteinte : en remontant depuis lui, on recense toutes les cases d'où un pas y mène,
    /// puis celles d'où un pas mène à celles-là, et ainsi de suite, sans jamais croiser le départ. Faux si on croise
    /// le départ ou si l'on recense trop de cases pour conclure vite : la recherche continue alors comme si de rien n'était.
    /// </summary>
    private bool IsCutOff(int startX, int startY, int goalX, int goalY, int maxStep, LocalSpatialIndex? index)
    {
        _backGeneration++;
        int width = _map.Width;
        int start = startY * width + startX, goal = goalY * width + goalX;
        int head = 0, tail = 0;
        _backSeen[goal] = _backGeneration;
        _backQueue[tail++] = goal;
        while (head < tail)
        {
            int current = _backQueue[head++];
            int cx = current % width, cy = current / width;
            foreach ((int dx, int dy) in Directions)
            {
                // Une voisine d'où l'on vient par ce pas, avec les règles de la recherche (coins compris en diagonale).
                int px = cx - dx, py = cy - dy;
                if (!_map.InBounds(px, py) || _backSeen[py * width + px] == _backGeneration
                    || !_map.CanStep(px, py, cx, cy, maxStep))
                    continue;
                if (dx != 0 && dy != 0 && (!_map.CanStep(px, py, cx, py, maxStep) || !_map.CanStep(px, py, px, cy, maxStep)))
                    continue;
                int previous = py * width + px;
                if (previous == start)
                    return false;
                _backSeen[previous] = _backGeneration;
                // On ne passe jamais par l'eau ni par l'intérieur d'un bâtiment : seul le départ peut s'y trouver.
                if (!_map.IsWalkable(px, py) || (index is not null && index.BlocksWalking(previous)))
                    continue;
                if (tail == MaxIsolatedTiles)
                    return false;
                _backQueue[tail++] = previous;
            }
        }
        return true;
    }

    private List<(int X, int Y)> BuildPath(int goal, int width)
    {
        var path = new List<(int X, int Y)>();
        for (int node = goal; _parent[node] != -1; node = _parent[node])
            path.Add((node % width, node / width));
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Distance « octile » (la plus courte possible en 8 directions sur terrain plat) multipliée par la durée minimale d'un pas : celle de la meilleure
    /// route de la carte. Garder une heuristique non réduite avec des routes moins coûteuses surestimerait les distances et perdrait le meilleur chemin.
    /// </summary>
    private static float Heuristic(int x, int y, int goalX, int goalY, float minStepSeconds) =>
        TraversalCost.Octile(x - goalX, y - goalY) * minStepSeconds;
}
