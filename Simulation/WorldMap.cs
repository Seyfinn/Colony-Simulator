using System.Runtime.CompilerServices;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.World;

namespace GodColony.Simulation;

/// <summary>Le chemin d'une caravane de case en case, avec le coût cumulé à chaque étape (pour l'animer à la bonne vitesse).</summary>
public sealed record WorldRoute(IReadOnlyList<int> Tiles, IReadOnlyList<float> Cumulative)
{
    public float Cost => Cumulative.Count == 0 ? 0f : Cumulative[^1];

    /// <summary>La case et la fraction parcourue vers la suivante, pour un avancement de 0 (départ) à 1 (arrivée).</summary>
    public (int From, int To, float T) At(float progress)
    {
        if (Tiles.Count == 1)
            return (Tiles[0], Tiles[0], 0f);
        float target = Math.Clamp(progress, 0f, 1f) * Cost;
        for (int i = 1; i < Tiles.Count; i++)
            if (target <= Cumulative[i])
            {
                float span = Cumulative[i] - Cumulative[i - 1];
                return (Tiles[i - 1], Tiles[i], span <= 0 ? 1f : (target - Cumulative[i - 1]) / span);
            }
        return (Tiles[^2], Tiles[^1], 1f);
    }
}

/// <summary>
/// La carte du monde : une grille d'hexagones (biomes, relief, climat, fleuves) et la case où vit chaque colonie.
/// Les caravanes y suivent le chemin le moins pénible : vite en prairie, lentement en forêt, en marais ou en montagne,
/// jamais à travers l'océan ni les sommets infranchissables.
/// </summary>
public sealed class WorldMap
{
    /// <summary>Cases de prairie plate qu'une caravane parcourt par jour de jeu.</summary>
    public const float CaravanTilesPerDay = 6f;

    /// <summary>Deux colonies sont au moins à cette distance (en cases) l'une de l'autre.</summary>
    public const int MinimumColonyDistance = 2;

    private readonly Dictionary<Colony, int> _tiles = [];

    /// <summary>
    /// Les chemins déjà calculés, rangés à part : ils se recalculent à volonté et ne font pas partie de l'état
    /// de la partie (la sauvegarde n'a pas à les enregistrer).
    /// </summary>
    private static readonly ConditionalWeakTable<WorldMap, Dictionary<(int, int), WorldRoute?>> RouteCache = new();
    private Dictionary<(int, int), WorldRoute?> Routes => RouteCache.GetValue(this, _ => []);

    public WorldMap(WorldGrid grid) => Grid = grid;

    public WorldGrid Grid { get; }

    public int TileOf(Colony colony) => _tiles[colony];

    /// <summary>Centre de la case de la colonie, en largeurs d'hexagone (voir <see cref="WorldGrid.Center"/>).</summary>
    public (float X, float Y) PositionOf(Colony colony)
    {
        WorldTile tile = Grid[_tiles[colony]];
        return WorldGrid.Center(tile.Col, tile.Row);
    }

    public Colony? ColonyAt(int tile) => _tiles.FirstOrDefault(p => p.Value == tile).Key;

    public bool CanSettle(int tile, out string reason)
    {
        if (tile < 0 || tile >= Grid.Tiles.Length)
            reason = "Choisissez une case de la carte du monde.";
        else if (Grid[tile].IsOcean)
            reason = "On ne fonde pas de colonie en pleine mer.";
        else if (!Grid[tile].Habitable)
            reason = Grid[tile].Relief == Relief.Impassable
                ? "Ces sommets sont infranchissables : personne ne peut y vivre."
                : $"{Grid[tile].Info.Name} : aucun peuple ne peut y vivre.";
        else if (_tiles.Values.Any(t => Grid.Distance(t, tile) < MinimumColonyDistance))
            reason = "Cette case est trop proche d'une autre colonie.";
        else
        {
            reason = $"{Grid[tile].Describe()} : région libre.";
            return true;
        }
        return false;
    }

    internal void PlaceAt(Colony colony, int tile)
    {
        _tiles[colony] = tile;
        Routes.Clear();
    }

    /// <summary>Le chemin le moins pénible entre deux colonies, ou null si la mer ou les sommets les séparent.</summary>
    public WorldRoute? Route(Colony a, Colony b)
    {
        int from = _tiles[a], to = _tiles[b];
        if (Routes.TryGetValue((from, to), out WorldRoute? cached))
            return cached;
        WorldRoute? route = FindRoute(from, to);
        Routes[(from, to)] = route;
        return route;
    }

    public bool Connected(Colony a, Colony b) => Route(a, b) is not null;

    /// <summary>Distance de marche entre deux colonies, en cases de prairie plate (infinie si aucun chemin).</summary>
    public float Distance(Colony a, Colony b) => Route(a, b)?.Cost ?? float.PositiveInfinity;

    /// <summary>Jours de marche d'une caravane entre deux colonies.</summary>
    public float TravelDays(Colony a, Colony b) => Distance(a, b) / CaravanTilesPerDay;

    /// <summary>
    /// La colonie située en aval sur le fleuve qui traverse la case de <paramref name="colony"/> : elle reçoit moins d'eau
    /// si l'on barre la rivière en amont. Null si la case n'a pas de rivière ou si aucune colonie n'est plus bas.
    /// </summary>
    public Colony? DownstreamOf(Colony colony)
    {
        int tile = _tiles[colony];
        if (Grid[tile].River == 0)
            return null;
        for (int next = Grid[tile].FlowsTo, steps = 0; next >= 0 && steps < Grid.Tiles.Length; next = Grid[next].FlowsTo, steps++)
            if (ColonyAt(next) is { } other && other != colony)
                return other;
        return null;
    }

    /// <summary>Coût d'entrée dans une case : moyenne des deux cases traversées (on quitte l'une, on entre dans l'autre).</summary>
    private float StepCost(int from, int to) => 0.5f * (Grid[from].TravelCost + Grid[to].TravelCost);

    private WorldRoute? FindRoute(int from, int to)
    {
        int n = Grid.Tiles.Length;
        var cost = new float[n];
        var previous = new int[n];
        Array.Fill(cost, float.PositiveInfinity);
        Array.Fill(previous, -1);
        cost[from] = 0f;
        var open = new PriorityQueue<int, float>();
        open.Enqueue(from, Grid.Distance(from, to));
        while (open.Count > 0)
        {
            int current = open.Dequeue();
            if (current == to)
                break;
            foreach (int next in Grid.Neighbors(current))
            {
                // Les colonies elles-mêmes sont toujours accessibles, même au bord d'un sommet.
                bool passable = next == to || float.IsFinite(Grid[next].TravelCost);
                if (!passable)
                    continue;
                float step = next == to && !float.IsFinite(Grid[next].TravelCost) ? Grid[current].TravelCost : StepCost(current, next);
                float total = cost[current] + step;
                if (total >= cost[next])
                    continue;
                cost[next] = total;
                previous[next] = current;
                open.Enqueue(next, total + Grid.Distance(next, to));
            }
        }
        if (!float.IsFinite(cost[to]))
            return null;
        var tiles = new List<int>();
        for (int t = to; t >= 0; t = previous[t])
            tiles.Add(t);
        tiles.Reverse();
        var cumulative = tiles.Select(t => cost[t]).ToList();
        return new WorldRoute(tiles, cumulative);
    }

    /// <summary>
    /// Choisit les cases des peuples de départ : le premier s'installe au cœur des terres sur la case qui lui plaît le plus ;
    /// les suivants, sur une case à leur goût à quelques jours de marche (3 à 8 cases), reliée par la terre.
    /// </summary>
    internal List<int> ChooseStartingTiles(IReadOnlyList<Species> peoples)
    {
        var chosen = new List<int>();
        WorldTile[] tiles = Grid.Tiles;
        (float cx, float cy) = WorldGrid.Center(Grid.Width / 2, Grid.Height / 2);
        int[] region = LandRegions();
        foreach (Species species in peoples)
        {
            int best = -1;
            float bestScore = float.MinValue;
            foreach (int maxDistance in new[] { 8, 12, int.MaxValue })
            {
                foreach (WorldTile tile in tiles)
                {
                    if (!tile.Habitable || chosen.Any(c => Grid.Distance(c, tile.Index) < 3))
                        continue;
                    float score = species.Appeal(tile);
                    if (chosen.Count == 0)
                    {
                        (float x, float y) = WorldGrid.Center(tile.Col, tile.Row);
                        score -= 0.12f * MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        // Les fleuves sont rares et changent la vie d'une colonie (pêche, champs irrigués, moulin, barrage) :
                        // le premier peuple s'installe sur leurs rives ; les autres, plus loin, n'ont pas toujours cette chance.
                        score += tile.River switch { 2 => 3f, 1 => 2f, _ => 0f };
                    }
                    else
                    {
                        if (region[tile.Index] != region[chosen[0]] || Grid.Distance(chosen[0], tile.Index) > maxDistance)
                            continue;
                        score -= 0.1f * Grid.Distance(chosen[0], tile.Index);
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = tile.Index;
                    }
                }
                if (best >= 0)
                    break;
            }
            if (best < 0)
                best = tiles.First(t => t.Habitable && !chosen.Contains(t.Index)).Index;
            chosen.Add(best);
        }
        return chosen;
    }

    /// <summary>Une case libre et agréable pour un peuple, près des colonies existantes (pour les fondations automatiques).</summary>
    public int SuggestTile(Species species)
    {
        int best = -1;
        float bestScore = float.MinValue;
        foreach (WorldTile tile in Grid.Tiles)
        {
            if (!CanSettle(tile.Index, out _))
                continue;
            float score = species.Appeal(tile);
            if (_tiles.Count > 0)
                score -= 0.15f * _tiles.Values.Min(t => Grid.Distance(t, tile.Index));
            if (score > bestScore)
            {
                bestScore = score;
                best = tile.Index;
            }
        }
        return best;
    }

    /// <summary>Numéro de la terre (continent ou île) de chaque case praticable, -1 pour la mer et les sommets.</summary>
    private int[] LandRegions()
    {
        var region = new int[Grid.Tiles.Length];
        Array.Fill(region, -1);
        int next = 0;
        foreach (WorldTile start in Grid.Tiles)
        {
            if (region[start.Index] >= 0 || !float.IsFinite(start.TravelCost))
                continue;
            var queue = new Queue<int>();
            queue.Enqueue(start.Index);
            region[start.Index] = next;
            while (queue.Count > 0)
                foreach (int n in Grid.Neighbors(queue.Dequeue()))
                    if (region[n] < 0 && float.IsFinite(Grid[n].TravelCost))
                    {
                        region[n] = next;
                        queue.Enqueue(n);
                    }
            next++;
        }
        return region;
    }
}
