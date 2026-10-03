using GodColony.Simulation.Map;

namespace GodColony.Simulation.Generation;

/// <summary>
/// Génère une carte locale tempérée : lacs, rivières, plaines en terrasses, forêts et montagnes.
/// </summary>
public static class MapGenerator
{
    // Proportions visées pour chaque grande zone de la carte.
    private const float WaterShare = 0.08f;
    private const float MountainShare = 0.22f;

    public static LocalMap Generate(int width, int height, int seed)
    {
        var map = new LocalMap(width, height, seed);
        int[] elevation = ComputeElevation(width, height, seed);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int e = elevation[y * width + x];
            SoilType soil = ChooseSoil(x, y, e, elevation, width, height, seed);
            (FloraType flora, float growth) = ChooseFlora(x, y, e, soil, seed);
            map.SetGenerated(x, y, e, soil, flora, growth);
        }

        foreach ((int x, int y, int downX, int downY) in Rivers.Generate(elevation, width, height, seed))
            map.SetRiver(x, y, downX, downY);
        map.ComputeBanks();
        return map;
    }

    /// <summary>
    /// Calcule un relief brut avec du bruit, puis le répartit par rang :
    /// les 8 % les plus bas deviennent de l'eau, les 22 % les plus hauts de la montagne,
    /// le reste des plaines en terrasses. Toutes les graines donnent ainsi des cartes équilibrées.
    /// </summary>
    private static int[] ComputeElevation(int width, int height, int seed)
    {
        int n = width * height;
        var raw = new float[n];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float hills = Noise.Fractal2D(x / 40f, y / 40f, seed, 5);
            float ridge = 1f - MathF.Abs(Noise.Fractal2D(x / 70f, y / 70f, seed + 7, 3) * 2f - 1f);
            raw[y * width + x] = hills * 0.7f + ridge * ridge * 0.45f;
        }

        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => raw[a].CompareTo(raw[b]));

        int waterCount = (int)(n * WaterShare);
        int mountainStart = (int)(n * (1f - MountainShare));
        int plainLow = LocalMap.WaterLevel + 1;
        int plainHigh = LocalMap.MountainElevation - 1;

        var elevation = new int[n];
        for (int rank = 0; rank < n; rank++)
        {
            int e;
            if (rank < waterCount)
                e = rank < waterCount / 2 ? 1 : 2;
            else if (rank < mountainStart)
                e = Band(rank, waterCount, mountainStart, plainLow, plainHigh);
            else
                e = Band(rank, mountainStart, n, LocalMap.MountainElevation, LocalMap.MaxElevation);
            elevation[order[rank]] = e;
        }
        return elevation;
    }

    private static int Band(int rank, int start, int end, int low, int high)
    {
        float t = (rank - start) / (float)(end - start);
        return Math.Min(high, low + (int)(t * (high - low + 1)));
    }

    private static SoilType ChooseSoil(int x, int y, int e, int[] elevation, int width, int height, int seed)
    {
        if (e == LocalMap.WaterLevel + 1 && IsNearWater(x, y, elevation, width, height))
            return SoilType.Sand;
        float moisture = Noise.Fractal2D(x / 25f, y / 25f, seed + 13, 4);
        return moisture < 0.38f ? SoilType.Dirt : SoilType.Grass;
    }

    private static bool IsNearWater(int x, int y, int[] elevation, int width, int height)
    {
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < width && ny < height && elevation[ny * width + nx] <= LocalMap.WaterLevel)
                return true;
        }
        return false;
    }

    private static (FloraType, float) ChooseFlora(int x, int y, int e, SoilType soil, int seed)
    {
        if (e <= LocalMap.WaterLevel || soil == SoilType.Sand)
            return (FloraType.None, 0f);

        float roll = Noise.Hash01(x, y, 1, seed);
        float growth = 0.4f + 0.6f * Noise.Hash01(x, y, 2, seed);
        float forest = Noise.Fractal2D(x / 18f, y / 18f, seed + 31, 4);

        if (e >= LocalMap.MountainElevation)
            return e <= LocalMap.MountainElevation + 1 && roll < 0.04f ? (FloraType.Tree, growth) : (FloraType.None, 0f);

        float treeChance = soil == SoilType.Grass ? Math.Clamp((forest - 0.45f) * 4f, 0f, 0.9f) : 0.03f;
        if (roll < treeChance)
            return (FloraType.Tree, growth);
        if (roll > 0.985f)
            return (FloraType.Bush, growth);
        return (FloraType.None, 0f);
    }
}
