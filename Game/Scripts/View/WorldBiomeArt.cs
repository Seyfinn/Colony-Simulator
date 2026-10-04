using System;
using Godot;
using GodColony.Simulation.World;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>Palette et source reproductible des quatorze images T-012 ; export par la scène de validation.</summary>
public static class WorldBiomeArt
{
    public const int Width = 32, Height = 37;

    public static Color Ground(Biome biome) => biome switch
    {
        Biome.Ocean => Color.Color8(63, 108, 144),
        Biome.IceSheet => Color.Color8(216, 230, 223),
        Biome.Tundra => Color.Color8(142, 157, 136),
        Biome.BorealForest => Color.Color8(65, 108, 82),
        Biome.TemperateForest => Color.Color8(98, 140, 85),
        Biome.Grassland => Color.Color8(146, 164, 100),
        Biome.Steppe => Color.Color8(178, 166, 105),
        Biome.Desert => Color.Color8(214, 186, 137),
        Biome.Savanna => Color.Color8(192, 158, 93),
        Biome.TropicalForest => Color.Color8(51, 116, 69),
        Biome.Swamp => Color.Color8(97, 117, 71),
        _ => Colors.Magenta,
    };

    public static bool Inside(int x, int y)
    {
        float edge = Math.Min(y + 0.5f, Height - y - 0.5f);
        float half = Math.Min(16, edge * 32 / 18.5f);
        return Math.Abs(x + 0.5f - 16) <= half;
    }

    public static Image Hex(Biome biome)
    {
        var a = new PixelArt(Width, Height);
        Color ground = Ground(biome);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            if (Inside(x, y))
            {
                float patch = Noise.Value2D(x / 9f, y / 10f, 211 + (int)biome);
                a.Dot(x, y, patch > 0.65f ? ground.Lightened(0.035f)
                    : patch < 0.3f ? ground.Darkened(0.035f) : ground);
            }

        switch (biome)
        {
            case Biome.Ocean:
                Wave(a, 5, 14, ground.Lightened(0.18f)); Wave(a, 17, 25, ground.Lightened(0.15f));
                break;
            case Biome.IceSheet:
                a.Polygon(ground.Darkened(0.1f), new(5, 16), new(12, 12), new(19, 14), new(24, 22), new(15, 24), new(7, 21));
                a.Polygon(ground.Lightened(0.07f), new(5, 14), new(12, 10), new(19, 12), new(23, 19), new(14, 21), new(7, 18));
                a.Line(11, 26, 18, 29, ground.Darkened(0.12f));
                break;
            case Biome.Tundra:
                a.Oval(10, 16, 5, 2, ground.Darkened(0.12f));
                a.Oval(21, 25, 4, 2, ground.Lightened(0.12f));
                Tuft(a, 20, 14, ground.Darkened(0.22f));
                break;
            case Biome.BorealForest:
                Pine(a, 9, 19); Pine(a, 21, 25); Pine(a, 17, 13);
                break;
            case Biome.TemperateForest:
                Broadleaf(a, 10, 20, false); Broadleaf(a, 22, 24, false); Broadleaf(a, 20, 13, false);
                break;
            case Biome.Grassland:
                a.Oval(11, 19, 6, 3, ground.Lightened(0.055f));
                Tuft(a, 9, 17, ground.Darkened(0.2f)); Tuft(a, 22, 25, ground.Darkened(0.14f));
                break;
            case Biome.Steppe:
                a.Oval(13, 21, 8, 3, ground.Lightened(0.065f));
                Tuft(a, 9, 15, Color.Color8(129, 130, 70)); Tuft(a, 22, 25, Color.Color8(145, 140, 76));
                break;
            case Biome.Desert:
                Dune(a, 6, 17, ground); Dune(a, 17, 26, ground);
                break;
            case Biome.Savanna:
                Acacia(a, 11, 20); Tuft(a, 22, 25, ground.Darkened(0.25f));
                break;
            case Biome.TropicalForest:
                Broadleaf(a, 8, 20, true); Broadleaf(a, 20, 26, true); Broadleaf(a, 19, 14, true);
                break;
            case Biome.Swamp:
                Color water = Color.Color8(75, 119, 107);
                a.Oval(10, 19, 6, 3, water.Darkened(0.1f)); a.Oval(10, 18, 6, 2, water);
                a.Oval(22, 25, 4, 2, water); a.Box(7, 17, 5, 1, water.Lightened(0.19f));
                Tuft(a, 21, 17, ground.Darkened(0.27f)); Tuft(a, 8, 27, ground.Darkened(0.23f));
                break;
        }
        Clip(a.Image);
        return a.Image;
    }

    public static Image ReliefImage(Relief relief)
    {
        var a = new PixelArt(Width, Height);
        Color dark = Color.Color8(83, 88, 80), main = Color.Color8(139, 143, 128), light = Color.Color8(180, 179, 151);
        if (relief == Relief.Hills)
        {
            a.Oval(11, 22, 8, 4, new Color(dark, 0.45f));
            a.Oval(11, 20, 7, 4, new Color(main, 0.68f));
            a.Oval(9, 18, 4, 2, new Color(light, 0.62f));
            a.Oval(23, 18, 6, 4, new Color(dark, 0.45f));
            a.Oval(22, 17, 5, 4, new Color(main, 0.65f));
            a.Oval(21, 15, 3, 2, new Color(light, 0.62f));
        }
        else if (relief is Relief.Mountains or Relief.Impassable)
        {
            a.Oval(16, 26, 12, 2, new Color(dark, 0.3f));
            Peak(a, 21, 12, 8, 12, main.Darkened(0.12f), dark, relief == Relief.Impassable);
            Peak(a, 13, relief == Relief.Impassable ? 7 : 10, 10, relief == Relief.Impassable ? 19 : 16,
                light, main.Darkened(0.17f), relief == Relief.Impassable);
        }
        Clip(a.Image);
        return a.Image;
    }

    private static void Wave(PixelArt a, int x, int y, Color color)
    {
        a.Box(x, y, 7, 2, color); a.Box(x + 7, y - 1, 3, 2, color);
    }

    private static void Tuft(PixelArt a, int x, int y, Color color)
    {
        a.Box(x, y, 2, 4, color); a.Box(x - 3, y + 1, 2, 2, color); a.Box(x + 3, y + 1, 2, 2, color);
    }

    private static void Pine(PixelArt a, int x, int y)
    {
        Color dark = Color.Color8(36, 69, 54), light = Color.Color8(93, 132, 89);
        a.Box(x - 1, y - 2, 2, 4, Color.Color8(117, 100, 62));
        a.Polygon(dark, new(x - 5, y), new(x, y - 10), new(x + 5, y));
        a.Polygon(light, new(x - 4, y - 2), new(x, y - 10), new(x, y - 2));
    }

    private static void Broadleaf(PixelArt a, int x, int y, bool tropical)
    {
        Color leaf = tropical ? Color.Color8(77, 143, 72) : Color.Color8(129, 163, 91);
        Color dark = tropical ? Color.Color8(30, 78, 49) : Color.Color8(51, 96, 57);
        int radius = tropical ? 6 : 5;
        a.Box(x - 1, y - 3, 2, 5, Color.Color8(107, 91, 53));
        a.Oval(x, y - 6, radius, 5, dark); a.Oval(x - 1, y - 7, radius - 1, 4, leaf);
        a.Oval(x - 3, y - 8, 2, 2, leaf.Lightened(0.12f));
    }

    private static void Acacia(PixelArt a, int x, int y)
    {
        Color bark = Color.Color8(117, 91, 51), leaf = Color.Color8(127, 141, 71);
        a.Box(x, y - 6, 2, 8, bark); a.Line(x, y - 3, x - 4, y - 7, bark);
        a.Oval(x, y - 8, 7, 3, Color.Color8(78, 98, 49)); a.Oval(x - 1, y - 9, 6, 2, leaf);
    }

    private static void Dune(PixelArt a, int x, int y, Color ground)
    {
        a.Polygon(ground.Darkened(0.09f), new(x - 2, y + 3), new(x + 3, y - 2), new(x + 10, y), new(x + 13, y + 3));
        a.Polygon(ground.Lightened(0.09f), new(x - 2, y + 1), new(x + 3, y - 2), new(x + 10, y), new(x + 8, y + 1));
    }

    private static void Peak(PixelArt a, int x, int y, int half, int height, Color light, Color shade, bool snow)
    {
        a.Polygon(shade, new(x - half, y + height), new(x, y), new(x + half, y + height));
        a.Polygon(light, new(x - half, y + height), new(x, y), new(x - 1, y + height - 2));
        if (snow)
        {
            a.Polygon(Color.Color8(219, 230, 217), new(x - half / 2, y + height / 2), new(x, y),
                new(x + half / 2, y + height / 2), new(x + 1, y + height / 3), new(x - 2, y + height / 2));
            a.Polygon(Color.Color8(247, 245, 226), new(x - half / 2, y + height / 2), new(x, y), new(x - 1, y + height / 3));
        }
    }

    private static void Clip(Image image)
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            if (!Inside(x, y)) image.SetPixel(x, y, Colors.Transparent);
    }
}
