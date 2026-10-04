using GodColony.Simulation.Generation;

namespace GodColony.Simulation.World;

/// <summary>
/// Génère la carte du monde : des continents entourés d'océan, un climat qui va de la banquise au nord aux jungles
/// au sud, des pluies plus fortes près des côtes, des chaînes de montagnes et des fleuves qui descendent jusqu'à la mer.
/// Le biome de chaque case découle de sa température et de ses pluies.
/// </summary>
public static class WorldGenerator
{
    /// <summary>Températures moyennes à l'extrême nord et à l'extrême sud de la carte, au niveau de la mer.</summary>
    private const float NorthTemperature = -12f, SouthTemperature = 34f;

    /// <summary>
    /// Les fleuves sont rares : un fleuve par tranche de <see cref="LandPerGreatRiver"/> cases de terre (au moins deux), qui traverse
    /// le continent de la montagne à la mer sur au moins <see cref="MinGreatRiverLength"/> cases, et quelques affluents ou petites
    /// rivières (<see cref="MinTributaryLength"/> cases au moins), qui ne partent jamais d'une source voisine d'une autre.
    /// </summary>
    private const int LandPerGreatRiver = 500, MinGreatRiverLength = 30, MinTributaryLength = 12;
    private const int GreatRiverSourceSpacing = 8, TributarySourceSpacing = 5;

    /// <summary>Nombre de cases de rivière en amont (la sienne comprise) à partir duquel une rivière devient un grand fleuve.</summary>
    private const int GreatRiverVolume = 12;

    public static WorldGrid Generate(int seed, int width = WorldGrid.DefaultWidth, int height = WorldGrid.DefaultHeight)
    {
        var grid = new WorldGrid(width, height);
        Elevation(grid, seed);
        ShapeRelief(grid, seed);
        Climate(grid, seed);
        TraceRivers(grid);
        foreach (WorldTile tile in grid.Tiles)
            tile.Biome = tile.IsOceanByElevation(grid) ? Biome.Ocean : ChooseBiome(tile);
        return grid;
    }

    private static bool IsOceanByElevation(this WorldTile tile, WorldGrid grid) => tile.Elevation < grid.SeaLevel;

    /// <summary>
    /// Des continents de grande taille : bruit très lisse, abaissé près des bords pour que le monde soit entouré d'océan.
    /// Le niveau de la mer est choisi pour que l'océan couvre toujours la même part du monde.
    /// </summary>
    private static void Elevation(WorldGrid grid, int seed)
    {
        float maxX = grid.Width, maxY = grid.Height * 0.866f;
        foreach (WorldTile tile in grid.Tiles)
        {
            (float x, float y) = WorldGrid.Center(tile.Col, tile.Row);
            float continents = Noise.Fractal2D(x / 16f, y / 16f, seed + 501, 4, 0.5f);
            float detail = Noise.Fractal2D(x / 5f, y / 5f, seed + 503, 2, 0.5f);
            // Distance au bord, de 0 (sur le bord) à 1 (au centre) : les bords s'enfoncent sous la mer.
            float edge = Math.Min(Math.Min(x, maxX - x) / (0.18f * maxX), Math.Min(y, maxY - y) / (0.22f * maxY));
            float falloff = 1f - Math.Clamp(edge, 0f, 1f);
            tile.Elevation = 0.75f * continents + 0.25f * detail - 0.55f * falloff * falloff;
        }
        Normalize(grid.Tiles, t => t.Elevation, (t, v) => t.Elevation = v);
        float[] sorted = grid.Tiles.Select(t => t.Elevation).OrderBy(e => e).ToArray();
        grid.SeaLevel = sorted[(int)(sorted.Length * WorldGrid.OceanShare)];
        foreach (WorldTile tile in grid.Tiles)
            tile.Coastal = !tile.IsOceanByElevation(grid)
                && grid.Neighbors(tile.Index).Any(n => grid[n].IsOceanByElevation(grid));
    }

    /// <summary>
    /// Les montagnes forment des chaînes (bruit « en crête ») plutôt que des taches. On répartit les terres par rang :
    /// 4 % de sommets infranchissables, 10 % de montagnes, 24 % de collines, le reste plat.
    /// </summary>
    private static void ShapeRelief(WorldGrid grid, int seed)
    {
        var land = grid.Tiles.Where(t => !t.IsOceanByElevation(grid)).ToList();
        var score = new Dictionary<WorldTile, float>();
        foreach (WorldTile tile in land)
        {
            (float x, float y) = WorldGrid.Center(tile.Col, tile.Row);
            float ridge = 1f - MathF.Abs(Noise.Fractal2D(x / 9f, y / 9f, seed + 509, 3, 0.5f) * 2f - 1f);
            float height = (tile.Elevation - grid.SeaLevel) / (1f - grid.SeaLevel);
            score[tile] = 0.55f * ridge * ridge + 0.45f * height;
        }
        land.Sort((a, b) => score[b].CompareTo(score[a]));
        for (int rank = 0; rank < land.Count; rank++)
        {
            float share = rank / (float)land.Count;
            land[rank].Relief = share < 0.04f ? Relief.Impassable
                : share < 0.14f ? Relief.Mountains
                : share < 0.38f ? Relief.Hills
                : Relief.Flat;
            // Un sommet sur la côte ferait un mur au bord de la mer : on l'abaisse en montagne.
            if (land[rank].Relief == Relief.Impassable && land[rank].Coastal)
                land[rank].Relief = Relief.Mountains;
        }
    }

    /// <summary>
    /// La température baisse du sud au nord et avec l'altitude. Les pluies viennent d'un bruit à grande échelle,
    /// renforcé près des côtes ; elles sont ensuite réparties par rang pour que chaque monde ait autant de déserts que de jungles.
    /// </summary>
    private static void Climate(WorldGrid grid, int seed)
    {
        var land = new List<WorldTile>();
        foreach (WorldTile tile in grid.Tiles)
        {
            (float x, float y) = WorldGrid.Center(tile.Col, tile.Row);
            float latitude = tile.Row / (float)(grid.Height - 1);
            float altitude = tile.Relief switch { Relief.Hills => 3f, Relief.Mountains => 8f, Relief.Impassable => 14f, _ => 0f };
            float wobble = (Noise.Fractal2D(x / 7f, y / 7f, seed + 521, 2) - 0.5f) * 8f;
            tile.Temperature = NorthTemperature + (SouthTemperature - NorthTemperature) * latitude + wobble - altitude;

            float rain = Noise.Fractal2D(x / 11f, y / 11f, seed + 523, 3, 0.5f);
            if (tile.Coastal) rain += 0.12f;
            tile.Rainfall = rain;
            if (!tile.IsOceanByElevation(grid))
                land.Add(tile);
        }
        land.Sort((a, b) => a.Rainfall.CompareTo(b.Rainfall));
        for (int rank = 0; rank < land.Count; rank++)
            land[rank].Rainfall = rank / (float)Math.Max(1, land.Count - 1);
        foreach (WorldTile tile in grid.Tiles.Where(t => t.IsOceanByElevation(grid)))
            tile.Rainfall = 1f;
    }

    /// <summary>
    /// L'eau de pluie descend de case en case jusqu'à la mer. On « remplit » d'abord les cuvettes depuis l'océan
    /// (inondation par priorité) : chaque case de terre sait ainsi vers quelle voisine elle s'écoule, sans jamais rester bloquée.
    /// Puis on trace quelques fleuves : chacun part d'un sommet éloigné de la mer et suit l'écoulement jusqu'à l'océan, d'un bout
    /// du continent à l'autre ; de petites rivières les rejoignent. Plus une rivière a d'amont, plus elle est large.
    /// </summary>
    private static void TraceRivers(WorldGrid grid)
    {
        int n = grid.Tiles.Length;
        var visited = new bool[n];
        var order = new List<int>(n);
        var queue = new PriorityQueue<int, float>();
        foreach (WorldTile tile in grid.Tiles)
            if (tile.IsOceanByElevation(grid))
            {
                visited[tile.Index] = true;
                order.Add(tile.Index);
                queue.Enqueue(tile.Index, tile.Elevation);
            }
        var level = grid.Tiles.Select(t => t.Elevation).ToArray();
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int next in grid.Neighbors(current))
            {
                if (visited[next])
                    continue;
                visited[next] = true;
                level[next] = Math.Max(level[next], level[current] + 1e-4f);
                grid[next].FlowsTo = current;
                order.Add(next);
                queue.Enqueue(next, level[next]);
            }
        }
        foreach (WorldTile tile in grid.Tiles.Where(t => t.IsOceanByElevation(grid)))
            tile.FlowsTo = -1;

        // Distance à la mer en suivant l'écoulement : l'ordre de l'inondation garantit que l'aval est déjà connu.
        var toSea = new int[n];
        foreach (int index in order)
            toSea[index] = grid[index].IsOceanByElevation(grid) ? 0 : toSea[grid[index].FlowsTo] + 1;

        var isRiver = new bool[n];
        var sources = new List<int>();
        int land = order.Count(i => !grid[i].IsOceanByElevation(grid));
        int greatRivers = Math.Max(2, land / LandPerGreatRiver);

        // Les sources possibles : les cases les plus éloignées de la mer, de préférence en montagne, du plus loin au plus proche.
        List<int> candidates = order
            .Where(i => !grid[i].IsOceanByElevation(grid))
            .OrderByDescending(i => grid[i].Relief >= Relief.Hills ? 1 : 0)
            .ThenByDescending(i => toSea[i])
            .ThenBy(i => i)
            .ToList();

        // Si le continent est étroit, un fleuve ne peut pas être aussi long : on exige les trois quarts du plus long parcours possible.
        int longest = candidates.Count == 0 ? 0 : candidates.Max(i => toSea[i]);
        int minGreat = Math.Clamp(longest * 3 / 4, MinTributaryLength, MinGreatRiverLength);
        PlaceRivers(grid, candidates, isRiver, sources, greatRivers, minGreat, GreatRiverSourceSpacing, requireWholeCourse: true);
        PlaceRivers(grid, candidates, isRiver, sources, Math.Max(2, greatRivers - 1), MinTributaryLength, TributarySourceSpacing, requireWholeCourse: false);

        // Des sources vers la mer : chaque case de rivière compte celles de l'amont et les passe à l'aval.
        var volume = new int[n];
        for (int i = order.Count - 1; i >= 0; i--)
        {
            WorldTile tile = grid[order[i]];
            if (!isRiver[tile.Index])
                continue;
            volume[tile.Index]++;
            if (tile.FlowsTo >= 0 && isRiver[tile.FlowsTo])
                volume[tile.FlowsTo] += volume[tile.Index];
            tile.River = volume[tile.Index] >= GreatRiverVolume ? 2 : 1;
        }
    }

    /// <summary>
    /// Trace jusqu'à <paramref name="count"/> rivières de plus, chacune de la source la plus lointaine encore libre jusqu'à la mer
    /// ou jusqu'à une rivière déjà tracée. Un grand fleuve doit couler sur <paramref name="minLength"/> cases (<paramref name="requireWholeCourse"/> :
    /// jusqu'à la mer) ; un affluent, apporter au moins autant de cases nouvelles.
    /// </summary>
    private static void PlaceRivers(WorldGrid grid, List<int> candidates, bool[] isRiver, List<int> sources,
        int count, int minLength, int spacing, bool requireWholeCourse)
    {
        int placed = 0;
        foreach (int source in candidates)
        {
            if (placed >= count)
                return;
            if (isRiver[source] || sources.Any(s => grid.Distance(s, source) < spacing))
                continue;

            var course = new List<int>();
            bool reachesSea = false;
            for (int at = source; at >= 0; at = grid[at].FlowsTo)
            {
                if (grid[at].IsOceanByElevation(grid))
                {
                    reachesSea = true;
                    break;
                }
                if (isRiver[at])
                    break;
                course.Add(at);
            }
            if (course.Count < minLength || (requireWholeCourse && !reachesSea))
                continue;

            foreach (int tile in course)
                isRiver[tile] = true;
            sources.Add(source);
            placed++;
        }
    }

    private static Biome ChooseBiome(WorldTile tile)
    {
        float t = tile.Temperature, r = tile.Rainfall;
        bool lowland = tile.Relief == Relief.Flat;
        if (t < -8f) return Biome.IceSheet;
        if (t < -1f) return Biome.Tundra;
        if (t < 6f) return r > 0.3f ? Biome.BorealForest : Biome.Tundra;
        if (t < 19f)
        {
            if (r < 0.12f) return Biome.Desert;
            if (r < 0.32f) return Biome.Steppe;
            if (r < 0.58f) return Biome.Grassland;
            if (r > 0.9f && lowland) return Biome.Swamp;
            return Biome.TemperateForest;
        }
        if (r < 0.25f) return Biome.Desert;
        if (r < 0.55f) return Biome.Savanna;
        if (r > 0.9f && lowland) return Biome.Swamp;
        return Biome.TropicalForest;
    }

    private static void Normalize(WorldTile[] tiles, Func<WorldTile, float> get, Action<WorldTile, float> set)
    {
        float min = tiles.Min(get), max = tiles.Max(get);
        foreach (WorldTile tile in tiles)
            set(tile, (get(tile) - min) / Math.Max(1e-6f, max - min));
    }
}
