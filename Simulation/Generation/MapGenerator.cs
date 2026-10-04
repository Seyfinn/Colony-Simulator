using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.Simulation.Generation;

/// <summary>
/// Génère une carte locale : de grandes zones nettes (un massif de montagnes, de vastes forêts, de grandes plaines
/// dégagées, un lac ou une mer) traversées par des fleuves larges.
///
/// Le secret est l'échelle du bruit : chaque grande zone vient d'un bruit dont les formes font plusieurs dizaines de cases,
/// et les détails fins sont presque effacés. Les proportions (part de montagne, d'eau, de forêt) sont imposées par rang,
/// si bien que toutes les graines donnent des cartes équilibrées.
/// </summary>
public static class MapGenerator
{
    /// <summary>Côté d'une carte locale, en cases.</summary>
    public const int DefaultSize = 200;

    /// <summary>
    /// Part de la carte qui reste de la terre nue, sèche, hors forêt (le reste des plaines est de l'herbe).
    /// L'humidité va de 0,30 (très sec) à 0,70 (détrempé) ; sous <see cref="DryMoisture"/>, le sol est nu.
    /// </summary>
    private const float DryShare = 0.20f;
    private const float DryMoisture = 0.30f + 0.40f * DryShare;

    public static LocalMap Generate(int width, int height, int seed, MapStyle? style = null)
    {
        style ??= MapStyle.Temperate;
        var map = new LocalMap(width, height, seed);
        int[] elevation = ComputeElevation(width, height, seed, style);
        // Les rivières larges aplanissent leur lit : on les trace avant de poser le sol et les arbres.
        List<RiverTile> rivers = Rivers.Generate(elevation, width, height, seed);
        float[] humidity = ComputeHumidity(width, height, seed);
        float forestThreshold = 1f - style.ForestShare;
        float scale = MathF.Max(width, height) / 200f;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int e = elevation[y * width + x];
            float u = humidity[y * width + x];
            float moisture = 0.30f + 0.40f * u;
            SoilType soil = ChooseSoil(x, y, e, moisture, elevation, width, height);
            (FloraType flora, float growth) = ChooseFlora(x, y, e, soil, u, forestThreshold, style.ForestShare, seed, scale);
            map.SetGenerated(x, y, e, soil, flora, growth, moisture);
        }

        foreach (RiverTile river in rivers)
            map.SetRiver(river.X, river.Y, river.DownX, river.DownY, river.Width);
        map.ComputeBanks();
        map.SoilRichness = style.SoilRichness;
        return map;
    }

    /// <summary>
    /// Calcule un relief brut avec du bruit à très grande échelle, puis le répartit par rang :
    /// les 8 % les plus bas deviennent de l'eau, les 22 % les plus hauts de la montagne,
    /// le reste des plaines en terrasses. Les formes font plusieurs dizaines de cases : un seul massif,
    /// de vastes terrasses, un grand lac.
    /// </summary>
    private static int[] ComputeElevation(int width, int height, int seed, MapStyle style)
    {
        int n = width * height;
        float size = MathF.Max(width, height);
        var raw = new float[n];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Un léger gauchissement des coordonnées casse l'alignement sur la grille du bruit.
            float wx = x + (Noise.Fractal2D(x / (0.25f * size), y / (0.25f * size), seed + 3, 2) - 0.5f) * 0.18f * size;
            float wy = y + (Noise.Fractal2D(x / (0.25f * size), y / (0.25f * size), seed + 5, 2) - 0.5f) * 0.18f * size;
            float hills = Noise.Fractal2D(wx / (0.9f * size), wy / (0.9f * size), seed, 4, 0.30f);
            float ridge = 1f - MathF.Abs(Noise.Fractal2D(wx / (0.4f * size), wy / (0.4f * size), seed + 7, 2, 0.4f) * 2f - 1f);
            raw[y * width + x] = hills * 0.8f + ridge * ridge * 0.10f;
        }

        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => raw[a].CompareTo(raw[b]));

        int waterCount = (int)(n * style.WaterShare);
        int mountainStart = (int)(n * (1f - style.MountainShare));
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

    /// <summary>
    /// L'humidité de chaque case, de 0 à 1, répartie par rang (autant de cases sèches que détrempées) : les forêts
    /// poussent dans les plus humides, la terre nue s'étend dans les plus sèches. À très grande échelle.
    /// </summary>
    private static float[] ComputeHumidity(int width, int height, int seed)
    {
        int n = width * height;
        float size = MathF.Max(width, height);
        var raw = new float[n];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            raw[y * width + x] = Noise.Fractal2D(x / (0.3f * size), y / (0.3f * size), seed + 13, 3, 0.42f);

        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => raw[a].CompareTo(raw[b]));

        var humidity = new float[n];
        for (int rank = 0; rank < n; rank++)
            humidity[order[rank]] = rank / (float)(n - 1);
        return humidity;
    }

    private static SoilType ChooseSoil(int x, int y, int e, float moisture, int[] elevation, int width, int height)
    {
        if (e == LocalMap.WaterLevel + 1 && IsNearWater(x, y, elevation, width, height))
            return SoilType.Sand;
        return moisture < DryMoisture ? SoilType.Dirt : SoilType.Grass;
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

    /// <summary>
    /// Les arbres suivent l'humidité : dense au cœur des zones les plus humides (la forêt), clairsemé en lisière, presque
    /// absent en plaine. De petites clairières trouent la forêt. Quelques buissons à baies poussent partout, moins sous les arbres.
    /// </summary>
    private static (FloraType, float) ChooseFlora(int x, int y, int e, SoilType soil, float u, float forestThreshold, float forestShare, int seed, float scale)
    {
        if (e <= LocalMap.WaterLevel || soil == SoilType.Sand)
            return (FloraType.None, 0f);

        float roll = Noise.Hash01(x, y, 1, seed);
        float growth = 0.4f + 0.6f * Noise.Hash01(x, y, 2, seed);

        float inside = Smooth(-0.07f, 0.10f, u - forestThreshold);
        float depth = Smooth(0f, 0.5f, Math.Clamp((u - forestThreshold) / forestShare, 0f, 1f));
        float density = 0.012f + inside * (0.50f + 0.34f * depth);
        float clearing = Smooth(0.25f, 0.38f, Noise.Fractal2D(x / (9f * scale), y / (9f * scale), seed + 41, 2));
        density *= 1f - inside * (1f - clearing) * 0.8f;

        if (e >= LocalMap.MountainElevation)
        {
            // Au pied de la montagne la forêt monte un peu ; plus haut, seulement de rares arbres.
            float foothill = e == LocalMap.MountainElevation ? 0.5f : e == LocalMap.MountainElevation + 1 ? 0.2f : 0f;
            float chance = e <= LocalMap.MountainElevation + 1 ? MathF.Max(0.04f, density * foothill) : 0f;
            return roll < chance ? (FloraType.Tree, growth) : (FloraType.None, 0f);
        }

        if (roll < density)
            return (FloraType.Tree, growth);
        float bushRate = 0.015f * (1f - 0.6f * inside);
        if (roll > 1f - bushRate)
            return (FloraType.Bush, growth);
        return (FloraType.None, 0f);
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
