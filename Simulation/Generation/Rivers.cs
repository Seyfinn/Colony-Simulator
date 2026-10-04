using GodColony.Simulation.Map;

namespace GodColony.Simulation.Generation;

/// <summary>Une case de rivière : la case vers laquelle elle coule (ou (-1, -1) à l'embouchure) et la largeur du fleuve à cet endroit.</summary>
public readonly record struct RiverTile(int X, int Y, int DownX, int DownY, int Width);

/// <summary>
/// Trace les rivières : chacune naît sur un flanc de montagne et ne fait que descendre (ou longer un replat)
/// jusqu'au lac, à la mer ou à une autre rivière. Le chemin est le moins coûteux, avec un peu de hasard
/// pour qu'il serpente au lieu de couper tout droit.
///
/// Une rivière commence en ruisseau d'une case de large, puis s'élargit à mesure qu'elle descend : en plaine,
/// c'est un fleuve de 3 à 5 cases, toujours peu profond (on le traverse à gué). Le lit élargi est aplani et ses
/// berges adoucies, pour qu'aucune falaise n'enferme les gués.
/// </summary>
public static class Rivers
{
    private const int RiverCount = 2;

    /// <summary>
    /// Une rivière plus courte que cela (pour une carte de 200 cases) n'est qu'un ruisseau sans intérêt : on cherche une autre source.
    /// La seconde rivière, qui doit s'écarter de la première, peut être un peu plus courte.
    /// </summary>
    private const int MinLength = 60, MinSecondLength = 40;

    /// <summary>Deux sources doivent être éloignées, pour que les rivières couvrent des régions différentes.</summary>
    private const int MinSourceSpacing = 60;

    /// <summary>On essaie plusieurs sources et on garde la rivière la plus longue : un grand fleuve plutôt qu'un filet.</summary>
    private const int CandidatesPerRiver = 6;

    private const int SourceTries = 1500;
    private const int EdgeMargin = 8;

    /// <summary>
    /// Longueur de ruisseau (une seule case de large) après la sortie de la montagne, avant que le lit ne s'élargisse :
    /// c'est le seul endroit où l'on peut barrer la rivière.
    /// </summary>
    private const int StreamLength = 28;

    /// <summary>Rayon du lit à l'endroit où il s'élargit, puis au plus large, et distance (en cases) sur laquelle il grandit.</summary>
    private const float MinRadius = 1.3f, MaxRadius = 2.2f;
    private const float WideningLength = 50f;

    /// <summary>Distance (en cases) sur laquelle les berges d'un lit élargi sont adoucies pour rester praticables.</summary>
    private const int BankSmoothing = 4;

    private static readonly (int Dx, int Dy, float Cost)[] Moves =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.41421356f), (1, -1, 1.41421356f), (-1, 1, 1.41421356f), (-1, -1, 1.41421356f),
    ];

    /// <summary>
    /// Les cases de toutes les rivières, de la source à l'embouchure. Aplanit au passage le lit des tronçons larges
    /// (modifie <paramref name="elevation"/>).
    /// </summary>
    public static List<RiverTile> Generate(int[] elevation, int width, int height, int seed)
    {
        float scale = MathF.Max(width, height) / 200f;
        int spacing = (int)(MinSourceSpacing * scale);
        int margin = Math.Max(4, (int)(EdgeMargin * scale));

        var center = new bool[width * height];
        var sources = new List<(int X, int Y)>();
        var rivers = new List<(List<(int X, int Y)> Path, (int X, int Y) Mouth)>();

        for (int river = 0; river < RiverCount; river++)
        {
            int minLength = (int)((river == 0 ? MinLength : MinSecondLength) * scale);
            List<(int X, int Y)>? best = null;
            (int X, int Y) bestMouth = default, bestSource = default;
            int candidates = 0;
            for (int attempt = river * SourceTries; attempt < (river + 1) * SourceTries && candidates < CandidatesPerRiver; attempt++)
            {
                int x = margin + (int)(Noise.Hash01(attempt, 1, 91, seed) * (width - 2 * margin));
                int y = margin + (int)(Noise.Hash01(attempt, 2, 91, seed) * (height - 2 * margin));
                int e = elevation[y * width + x];
                // Sur un flanc de montagne : assez haut pour descendre longtemps, pas tout en haut des sommets.
                if (e < LocalMap.MountainElevation + 1 || e > LocalMap.MountainElevation + 2)
                    continue;
                if (sources.Any(s => Math.Max(Math.Abs(s.X - x), Math.Abs(s.Y - y)) < spacing))
                    continue;

                List<(int X, int Y)>? path = Flow(elevation, center, width, height, x, y, seed + attempt, out (int X, int Y) mouth);
                if (path is null || path.Count < minLength)
                    continue;
                candidates++;
                if (best is null || path.Count > best.Count)
                {
                    best = path;
                    bestMouth = mouth;
                    bestSource = (x, y);
                }
            }
            if (best is null)
                continue;

            sources.Add(bestSource);
            rivers.Add((best, bestMouth));
            foreach ((int px, int py) in best)
                center[py * width + px] = true;
        }

        var tiles = new List<RiverTile>();
        var claimed = new bool[width * height];
        foreach ((List<(int X, int Y)> path, (int X, int Y) mouth) in rivers)
            tiles.AddRange(Widen(path, mouth, elevation, width, height, seed, claimed));
        return tiles;
    }

    /// <summary>
    /// Donne son lit à la rivière : le fil central, plus des cases de chaque côté dès que la rivière a quitté la montagne.
    /// Chaque case garde la case vers laquelle elle coule, voisine de la sienne, pour que le courant, les barrages
    /// et l'affichage suivent le même sens partout.
    /// </summary>
    private static List<RiverTile> Widen(List<(int X, int Y)> path, (int X, int Y) mouth, int[] elevation, int width, int height, int seed, bool[] claimed)
    {
        int n = path.Count;
        var radius = new float[n];
        int exit = n;
        for (int i = 0; i < n; i++)
            if (elevation[path[i].Y * width + path[i].X] < LocalMap.MountainElevation) { exit = i; break; }
        int wideStart = exit + StreamLength;
        for (int i = wideStart; i < n; i++)
        {
            float growth = Math.Clamp((i - wideStart) / WideningLength, 0f, 1f);
            // Le lit ondule : un peu plus large, un peu plus étroit, par tronçons de quelques cases.
            float wave = Noise.Value2D(i / 6f, 0.5f, seed + 211) - 0.5f;
            radius[i] = MinRadius + (MaxRadius - MinRadius) * growth + wave * 1.0f;
        }

        // Cases du lit : la case centrale (propriétaire = son rang dans le tracé) et les cases à portée du rayon.
        var owner = new Dictionary<int, int>();
        var lateral = new Dictionary<int, float>();
        for (int i = 0; i < n; i++)
        {
            int ci = path[i].Y * width + path[i].X;
            owner[ci] = i;
            lateral[ci] = 0f;
        }
        for (int i = wideStart; i < n; i++)
        {
            if (radius[i] < 1f)
                continue;
            int reach = (int)MathF.Ceiling(radius[i]);
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                int x = path[i].X + dx, y = path[i].Y + dy;
                if (x < 0 || y < 0 || x >= width || y >= height)
                    continue;
                // Des berges irrégulières : le bord du lit avance ou recule d'une case à l'autre.
                if (distance > radius[i] + (Noise.Hash01(x, y, 97, seed) - 0.5f) * 0.7f)
                    continue;
                int t = y * width + x;
                int e = elevation[t];
                // Ni dans un lac, ni sur la roche : le lit élargi reste en plaine.
                if (e <= LocalMap.WaterLevel || e >= LocalMap.MountainElevation || claimed[t])
                    continue;
                // Une case à portée de deux tranches revient à la plus proche ; le fil central, lui, ne change jamais.
                if (!owner.ContainsKey(t) || (lateral[t] > 0f && distance < lateral[t]))
                {
                    owner[t] = i;
                    lateral[t] = distance;
                }
            }
        }

        // Le lit est plat : chaque case prend l'altitude de la case centrale dont elle dépend.
        var pathElevation = new int[n];
        for (int i = 0; i < n; i++)
            pathElevation[i] = elevation[path[i].Y * width + path[i].X];
        foreach ((int t, int i) in owner)
            elevation[t] = pathElevation[i];

        // Largeur au droit de chaque case : le nombre de cases du lit qui dépendent de la même case centrale.
        var crossSection = new int[n];
        foreach ((_, int i) in owner)
            crossSection[i]++;

        // Seul le lit élargi adoucit ses berges : la roche du ruisseau de montagne reste telle quelle.
        SmoothBanks(elevation, width, height, owner.Where(o => o.Value >= wideStart).Select(o => o.Key));

        var result = new List<RiverTile>(owner.Count);
        foreach ((int t, int i) in owner)
        {
            int x = t % width, y = t / width;
            claimed[t] = true;
            bool isCenter = lateral[t] == 0f;
            (int dx, int dy) = isCenter
                ? (i + 1 < n ? path[i + 1] : mouth)
                : Downstream(x, y, i, lateral[t], owner, lateral, width, height, elevation);
            result.Add(new RiverTile(x, y, dx, dy, Math.Max(1, crossSection[i])));
        }
        return result;
    }

    /// <summary>
    /// Vers quelle case voisine coule une case du lit élargi : celle qui est un peu plus en aval, en restant du même côté
    /// du fleuve ; à défaut, plus près du fil central ; à défaut, le lac dans lequel le fleuve se jette. (-1, -1) si rien.
    /// </summary>
    private static (int X, int Y) Downstream(int x, int y, int rank, float side, Dictionary<int, int> owner, Dictionary<int, float> lateral, int width, int height, int[] elevation)
    {
        (int X, int Y) best = (-1, -1);
        (int Ahead, float Drift) bestKey = (int.MaxValue, float.MaxValue);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;
            if (!owner.TryGetValue(ny * width + nx, out int nRank) || nRank <= rank)
                continue;
            var key = (nRank - rank, MathF.Abs(lateral[ny * width + nx] - side));
            if (key.Item1 < bestKey.Ahead || (key.Item1 == bestKey.Ahead && key.Item2 < bestKey.Drift))
            {
                best = (nx, ny);
                bestKey = key;
            }
        }
        if (best.X >= 0)
            return best;

        // Plus aucune case en aval : on se rapproche du fil central de la même tranche.
        float closest = side;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height)
                continue;
            if (owner.TryGetValue(ny * width + nx, out int nRank) && nRank == rank && lateral[ny * width + nx] < closest)
            {
                best = (nx, ny);
                closest = lateral[ny * width + nx];
            }
        }
        if (best.X >= 0)
            return best;

        // Le bout du fleuve : il se jette dans le lac voisin.
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < width && ny < height && elevation[ny * width + nx] <= LocalMap.WaterLevel)
                return (nx, ny);
        }
        return (-1, -1);
    }

    /// <summary>
    /// Les cases autour du lit élargi ne doivent pas faire plus d'une marche avec lui : sans cela, une falaise
    /// pourrait border le fleuve et enfermer les gués. Le relief est adouci en s'éloignant du lit, sur quelques cases.
    /// </summary>
    private static void SmoothBanks(int[] elevation, int width, int height, IEnumerable<int> bed)
    {
        var depth = new Dictionary<int, int>();
        var frontier = new Queue<int>();
        foreach (int t in bed)
        {
            depth[t] = 0;
            frontier.Enqueue(t);
        }
        while (frontier.Count > 0)
        {
            int current = frontier.Dequeue();
            int d = depth[current];
            if (d >= BankSmoothing)
                continue;
            int cx = current % width, cy = current / width;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;
                int next = ny * width + nx;
                if (depth.ContainsKey(next))
                    continue;
                depth[next] = d + 1;
                frontier.Enqueue(next);
                // Un lac reste un lac ; on ne creuse ni ne remblaie dans l'eau.
                if (elevation[next] <= LocalMap.WaterLevel)
                    continue;
                int from = elevation[current];
                elevation[next] = Math.Clamp(elevation[next], Math.Max(from - 1, LocalMap.WaterLevel + 1), from + 1);
            }
        }
    }

    /// <summary>Le chemin le moins coûteux de la source jusqu'à l'eau, sans jamais remonter.</summary>
    private static List<(int X, int Y)>? Flow(int[] elevation, bool[] river, int width, int height, int sourceX, int sourceY, int seed, out (int X, int Y) mouth)
    {
        mouth = (-1, -1);
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
            {
                mouth = (current % width, current / width);
                return Trace(parent, width, current, elevation);
            }

            foreach ((int dx, int dy, float step) in Moves)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;
                int next = ny * width + nx;
                if (elevation[next] > elevation[current])
                    continue;

                // L'eau préfère descendre ; sur un replat, un hasard qui varie doucement la fait serpenter en larges courbes.
                float wander = 1f + 2.4f * Noise.Value2D(nx / 6f, ny / 6f, seed + 93) + 0.3f * Noise.Hash01(nx, ny, 93, seed);
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
