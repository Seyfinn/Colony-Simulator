namespace GodColony.Simulation.Generation;

/// <summary>
/// Bruit déterministe : la même graine donne toujours le même monde.
/// Sert à générer le relief, l'humidité, les forêts et les filons de minerai.
/// </summary>
public static class Noise
{
    /// <summary>Nombre pseudo-aléatoire entre 0 et 1, toujours le même pour les mêmes coordonnées.</summary>
    public static float Hash01(int x, int y, int z, int seed) => Hash(x, y, z, seed) / (float)uint.MaxValue;

    public static float Value2D(float x, float y, int seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float u = Smooth(x - xi), v = Smooth(y - yi);
        float a = Hash01(xi, yi, 0, seed), b = Hash01(xi + 1, yi, 0, seed);
        float c = Hash01(xi, yi + 1, 0, seed), d = Hash01(xi + 1, yi + 1, 0, seed);
        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    public static float Value3D(float x, float y, float z, int seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float u = Smooth(x - xi), v = Smooth(y - yi), w = Smooth(z - zi);
        float Plane(int zz) => Lerp(
            Lerp(Hash01(xi, yi, zz, seed), Hash01(xi + 1, yi, zz, seed), u),
            Lerp(Hash01(xi, yi + 1, zz, seed), Hash01(xi + 1, yi + 1, zz, seed), u), v);
        return Lerp(Plane(zi), Plane(zi + 1), w);
    }

    /// <summary>Superpose plusieurs échelles de bruit pour un rendu plus naturel. Résultat entre 0 et 1.</summary>
    public static float Fractal2D(float x, float y, int seed, int octaves)
    {
        float sum = 0, amplitude = 1, frequency = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Value2D(x * frequency, y * frequency, seed + i * 101);
            norm += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return sum / norm;
    }

    public static float Fractal3D(float x, float y, float z, int seed, int octaves)
    {
        float sum = 0, amplitude = 1, frequency = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Value3D(x * frequency, y * frequency, z * frequency, seed + i * 101);
            norm += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return sum / norm;
    }

    private static uint Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA6Bu;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0xC2B2AE35u;
            h = (h << 17) | (h >> 15);
            h ^= (uint)z * 0x27D4EB2Fu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
