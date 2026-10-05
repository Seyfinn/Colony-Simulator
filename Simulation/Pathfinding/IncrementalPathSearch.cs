using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.Simulation.Pathfinding;

public enum SearchStatus : byte
{
    /// <summary>La recherche continue : le budget du rendez-vous est épuisé.</summary>
    Running,
    /// <summary>Un chemin existe.</summary>
    Found,
    /// <summary>Aucun chemin : la frontière est épuisée sans que le budget de durée ait rien coupé.</summary>
    Unreachable,
    /// <summary>La frontière est épuisée, mais des cases ont été écartées parce qu'elles dépassaient la durée permise : le trajet est trop long, pas introuvable.</summary>
    TooFar,
    /// <summary>Le plafond d'expansions d'une recherche est atteint.</summary>
    Capped,
    /// <summary>Le terrain ou l'occupation a changé dans la fenêtre explorée : le résultat n'est plus fiable, il faut recommencer.</summary>
    Stale,
}

/// <summary>
/// L'état persistant d'un A* de planification : coûts, parents, cases fermées, frontière et départages, dans des tableaux que le sérialiseur connaît
/// (jamais une <c>PriorityQueue</c>, que le format ne sait pas écrire). Il reste lisible tel quel : charger une partie au milieu d'une recherche la
/// reprend à la même case, avec les mêmes départages, donc le même résultat qu'en continu.
/// </summary>
public sealed class PathSearchState
{
    public int StartCell { get; internal set; }
    public int GoalCell { get; internal set; }

    /// <summary>L'emprise future du bâtiment, comptée comme déjà occupée (largeur 0 : aucune).</summary>
    public int BlockX { get; internal set; }
    public int BlockY { get; internal set; }
    public int BlockWidth { get; internal set; }
    public int BlockHeight { get; internal set; }

    /// <summary>Durée maximale d'un trajet, en secondes : au-delà, la case est écartée (et comptée dans <see cref="PrunedByBudget"/>).</summary>
    public float MaxSeconds { get; internal set; }
    public int PrunedByBudget { get; internal set; }

    /// <summary>Les révisions à l'ouverture de la recherche : une mutation pertinente dans la fenêtre explorée la périme.</summary>
    public int OccupancyRevision { get; internal set; }
    public int TerrainRevision { get; internal set; }
    public int SurfaceRevision { get; internal set; }

    /// <summary>La fenêtre explorée jusqu'ici (rectangle englobant des cases vues).</summary>
    public int MinX { get; internal set; }
    public int MinY { get; internal set; }
    public int MaxX { get; internal set; }
    public int MaxY { get; internal set; }

    public int Expanded { get; internal set; }
    public SearchStatus Status { get; internal set; }

    /// <summary>La durée du chemin trouvé, en secondes.</summary>
    public float ResultSeconds { get; internal set; }

    internal float[] Cost = [];
    internal int[] Parent = [];
    /// <summary>0 : inconnue, 1 : en frontière, 2 : fermée.</summary>
    internal byte[] Mark = [];

    // Le tas binaire de la frontière : cases, clés et numéros d'ordre (le départage à clé égale : la case entrée la première).
    internal int[] HeapCells = [];
    internal float[] HeapKeys = [];
    internal int[] HeapSeq = [];
    internal int HeapCount;
    internal int NextSeq;
}

/// <summary>
/// Un A* reprenable pour la planification urbaine : il avance de quelques expansions par rendez-vous et garde son état entre deux. Son contexte est indépendant
/// du pathfinder individuel des colons, dont le travail mutable ne doit jamais être écrasé entre deux lots. Même relief, mêmes diagonales, même coût de
/// pas que le mouvement (<see cref="TraversalCost"/>) ; l'emprise future est déjà occupée, les champs, tombes et bâtiments existants sont des obstacles.
/// </summary>
public static class IncrementalPathSearch
{
    private static readonly (int Dx, int Dy)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>Surcoût d'un arbre ou d'un buisson à dégager sur un tracé (en secondes) : on les évite quand on le peut.</summary>
    private const float TreeClearingSeconds = 0.15f;
    private const float BushClearingSeconds = 0.25f;

    [ThreadStatic] private static Stack<PathSearchState>? _pool;

    /// <summary>Ouvre une recherche de <paramref name="start"/> à <paramref name="goal"/> (index de cellule). Réutilise les tableaux d'une recherche terminée.</summary>
    public static PathSearchState Begin(LocalMap map, LocalSpatialIndex index, int start, int goal, float maxSeconds,
        (int X, int Y, int Width, int Height)? blocked = null)
    {
        PathSearchState state = Rent(map.Width * map.Height);
        state.StartCell = start;
        state.GoalCell = goal;
        state.MaxSeconds = maxSeconds;
        (state.BlockX, state.BlockY, state.BlockWidth, state.BlockHeight) = blocked ?? (0, 0, 0, 0);
        state.OccupancyRevision = index.Revision;
        state.TerrainRevision = map.TerrainRevision;
        state.SurfaceRevision = map.Roads.SurfaceRevision;
        state.MinX = state.MaxX = start % map.Width;
        state.MinY = state.MaxY = start / map.Width;
        state.Status = SearchStatus.Running;
        state.Cost[start] = 0f;
        state.Parent[start] = -1;
        state.Mark[start] = 1;
        Push(state, start, Heuristic(map, start, goal));
        return state;
    }

    /// <summary>Rend les tableaux de travail pour une prochaine recherche.</summary>
    public static void Release(PathSearchState state)
    {
        state.Status = SearchStatus.Capped;
        (_pool ??= new()).Push(state);
    }

    private static PathSearchState Rent(int cells)
    {
        if (_pool is { Count: > 0 } pool && pool.Peek().Cost.Length == cells)
        {
            PathSearchState reused = pool.Pop();
            Array.Fill(reused.Cost, float.PositiveInfinity);
            Array.Clear(reused.Parent);
            Array.Clear(reused.Mark);
            // Le tas est petit et sa capacité fait partie de l'état sauvegardé : il repart toujours de la même taille, pour qu'une partie rechargée soit identique.
            reused.HeapCells = new int[256];
            reused.HeapKeys = new float[256];
            reused.HeapSeq = new int[256];
            reused.HeapCount = 0;
            reused.NextSeq = 0;
            reused.Expanded = 0;
            reused.PrunedByBudget = 0;
            reused.ResultSeconds = 0f;
            return reused;
        }
        var state = new PathSearchState
        {
            Cost = new float[cells],
            Parent = new int[cells],
            Mark = new byte[cells],
            HeapCells = new int[256],
            HeapKeys = new float[256],
            HeapSeq = new int[256],
        };
        Array.Fill(state.Cost, float.PositiveInfinity);
        return state;
    }

    /// <summary>
    /// Avance d'au plus <paramref name="budget"/> expansions (qui sont décomptées) sans jamais dépasser <paramref name="cap"/> au total. Ne modifie rien
    /// d'autre que l'état : aucune mutation du monde.
    /// </summary>
    public static SearchStatus Advance(PathSearchState state, LocalMap map, LocalSpatialIndex index, ref int budget, int cap)
    {
        if (state.Status != SearchStatus.Running)
            return state.Status;
        if (IsStale(state, map, index))
            return state.Status = SearchStatus.Stale;

        int width = map.Width;
        while (budget > 0)
        {
            if (state.HeapCount == 0)
                return state.Status = state.PrunedByBudget > 0 ? SearchStatus.TooFar : SearchStatus.Unreachable;
            int current = Pop(state);
            if (state.Mark[current] == 2)
                continue;
            state.Mark[current] = 2;
            if (current == state.GoalCell)
            {
                state.ResultSeconds = state.Cost[current];
                return state.Status = SearchStatus.Found;
            }
            if (state.Expanded >= cap)
                return state.Status = SearchStatus.Capped;
            state.Expanded++;
            budget--;

            int cx = current % width, cy = current / width;
            foreach ((int dx, int dy) in Directions)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!map.CanStep(cx, cy, nx, ny))
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                if (diagonal && (!map.CanStep(cx, cy, cx + dx, cy) || !map.CanStep(cx, cy, cx, cy + dy)))
                    continue;
                int next = ny * width + nx;
                if (state.Mark[next] == 2 || IsBlocked(state, index, nx, ny, next))
                    continue;

                float step = TraversalCost.StepSeconds(map, cx, cy, nx, ny) + FloraSeconds(map, nx, ny);
                float cost = state.Cost[current] + step;
                if (cost > state.MaxSeconds)
                {
                    state.PrunedByBudget++;
                    continue;
                }
                if (cost >= state.Cost[next])
                    continue;
                state.Cost[next] = cost;
                state.Parent[next] = current;
                state.Mark[next] = 1;
                state.MinX = Math.Min(state.MinX, nx);
                state.MaxX = Math.Max(state.MaxX, nx);
                state.MinY = Math.Min(state.MinY, ny);
                state.MaxY = Math.Max(state.MaxY, ny);
                Push(state, next, cost + Heuristic(map, next, state.GoalCell));
            }
        }
        return SearchStatus.Running;
    }

    /// <summary>Le chemin trouvé, de la case suivant le départ jusqu'au but (index de cellule) ; null si la recherche n'a rien trouvé.</summary>
    public static List<int>? PathOf(PathSearchState state)
    {
        if (state.Status != SearchStatus.Found)
            return null;
        var path = new List<int>();
        for (int node = state.GoalCell; state.Parent[node] != -1; node = state.Parent[node])
            path.Add(node);
        path.Reverse();
        return path;
    }

    private static bool IsStale(PathSearchState state, LocalMap map, LocalSpatialIndex index) =>
        state.SurfaceRevision != map.Roads.SurfaceRevision
        || !map.TerrainUnchangedSince(state.TerrainRevision, state.MinX - 1, state.MinY - 1, state.MaxX + 1, state.MaxY + 1)
        || !index.UnchangedSince(state.OccupancyRevision, state.MinX - 1, state.MinY - 1, state.MaxX + 1, state.MaxY + 1);

    private static bool IsBlocked(PathSearchState state, LocalSpatialIndex index, int x, int y, int cell)
    {
        if (cell == state.StartCell)
            return false;
        if (state.BlockWidth > 0 && x >= state.BlockX && y >= state.BlockY && x < state.BlockX + state.BlockWidth && y < state.BlockY + state.BlockHeight)
            return true;
        return (index.UseAt(cell) & (CellUse.Building | CellUse.Field | CellUse.Grave)) != 0 && !index.IsLegacyOpen(x, y);
    }

    private static float FloraSeconds(LocalMap map, int x, int y) => map.GetFlora(x, y) switch
    {
        FloraType.Tree => TreeClearingSeconds,
        FloraType.Bush => BushClearingSeconds,
        _ => 0f,
    };

    private static float Heuristic(LocalMap map, int cell, int goal)
    {
        int width = map.Width;
        return TraversalCost.Octile(cell % width - goal % width, cell / width - goal / width) * map.Roads.MinFactor * LocalMap.RuggednessMin / SettlementRules.WalkTilesPerSecond;
    }

    // --- Tas binaire (clé, puis numéro d'ordre) ---

    private static void Push(PathSearchState s, int cell, float key)
    {
        if (s.HeapCount == s.HeapCells.Length)
        {
            Array.Resize(ref s.HeapCells, s.HeapCount * 2);
            Array.Resize(ref s.HeapKeys, s.HeapCount * 2);
            Array.Resize(ref s.HeapSeq, s.HeapCount * 2);
        }
        int i = s.HeapCount++;
        int seq = s.NextSeq++;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (Less(s.HeapKeys[parent], s.HeapSeq[parent], key, seq))
                break;
            s.HeapCells[i] = s.HeapCells[parent];
            s.HeapKeys[i] = s.HeapKeys[parent];
            s.HeapSeq[i] = s.HeapSeq[parent];
            i = parent;
        }
        s.HeapCells[i] = cell;
        s.HeapKeys[i] = key;
        s.HeapSeq[i] = seq;
    }

    private static int Pop(PathSearchState s)
    {
        int top = s.HeapCells[0];
        int last = --s.HeapCount;
        if (last > 0)
        {
            int cell = s.HeapCells[last];
            float key = s.HeapKeys[last];
            int seq = s.HeapSeq[last];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= last)
                    break;
                if (child + 1 < last && Less(s.HeapKeys[child + 1], s.HeapSeq[child + 1], s.HeapKeys[child], s.HeapSeq[child]))
                    child++;
                if (Less(key, seq, s.HeapKeys[child], s.HeapSeq[child]))
                    break;
                s.HeapCells[i] = s.HeapCells[child];
                s.HeapKeys[i] = s.HeapKeys[child];
                s.HeapSeq[i] = s.HeapSeq[child];
                i = child;
            }
            s.HeapCells[i] = cell;
            s.HeapKeys[i] = key;
            s.HeapSeq[i] = seq;
        }
        return top;
    }

    private static bool Less(float keyA, int seqA, float keyB, int seqB) => keyA < keyB || (keyA == keyB && seqA < seqB);
}
