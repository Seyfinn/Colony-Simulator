using System;
using Godot;

namespace GodColony.View;

/// <summary>Six apparences en 32 × 48 pixels. Elles partagent le même arbre dans la simulation.</summary>
public static class TreeSprites
{
    public const int StyleCount = 6;
    public const int DetailCount = 6;

    public static ImageTexture Create(int style, int detail = 0)
    {
        var image = Image.CreateEmpty(32, 48, false, Image.Format.Rgba8);
        switch (style)
        {
            case 1: Birch(image); break;
            case 2: Pine(image); break;
            case 3: Willow(image); break;
            case 4: Acacia(image); break;
            case 5: Tropical(image); break;
            default: Oak(image); break;
        }
        if (detail != 0) AddDetails(image, style, detail);
        return ImageTexture.CreateFromImage(image);
    }

    private static (Color Dark, Color Leaf, Color Light) LeafColors(int style) => style switch
    {
        1 => (Color.Color8(59, 106, 71), Color.Color8(104, 153, 91), Color.Color8(157, 184, 113)),
        2 => (Color.Color8(36, 76, 63), Color.Color8(56, 111, 85), Color.Color8(103, 150, 112)),
        3 => (Color.Color8(64, 98, 62), Color.Color8(102, 143, 75), Color.Color8(154, 174, 102)),
        4 => (Color.Color8(77, 90, 47), Color.Color8(119, 136, 65), Color.Color8(172, 175, 93)),
        5 => (Color.Color8(29, 76, 53), Color.Color8(49, 126, 69), Color.Color8(103, 168, 85)),
        _ => (Color.Color8(43, 82, 56), Color.Color8(77, 119, 64), Color.Color8(127, 161, 84)),
    };

    private static void AddDetails(Image image, int style, int detail)
    {
        var palette = LeafColors(style);
        var leaves = new bool[32 * 48];
        Color tone = detail switch
        {
            1 => new Color(0.92f, 0.96f, 1),
            2 => new Color(1.07f, 1.04f, 0.96f),
            4 => new Color(0.94f, 0.95f, 1.04f),
            5 => new Color(1.06f, 0.99f, 0.92f),
            _ => Colors.White,
        };
        for (int y = 0; y < 48; y++)
        for (int x = 0; x < 32; x++)
        {
            Color original = image.GetPixel(x, y);
            if (original != palette.Dark && original != palette.Leaf && original != palette.Light) continue;
            leaves[y * 32 + x] = true;
            float patch = 1 + ((x / 3 * 7 + y / 3 * 11 + detail * 13 + style * 5) % 11 - 5) * 0.009f;
            float shadow = detail is 1 or 4 ? 1 - (detail == 1 ? x : 31 - x) / 31f * 0.11f : 1;
            image.SetPixel(x, y, new Color(
                Math.Clamp(original.R * tone.R * patch * shadow, 0, 1),
                Math.Clamp(original.G * tone.G * patch * shadow, 0, 1),
                Math.Clamp(original.B * tone.B * patch * shadow, 0, 1), original.A));
        }
        if (detail == 4)
            for (int y = 1; y < 40; y++)
            for (int x = 1; x < 31; x++)
                if (leaves[y * 32 + x] && (x + y * 3 + style * 5) % 17 == 0
                    && (image.GetPixel(x - 1, y).A == 0 || image.GetPixel(x + 1, y).A == 0))
                    image.SetPixel(x, y, Colors.Transparent);
        if (detail == 3)
        {
            var accents = style switch
            {
                1 => new[] { (14, 8), (20, 23) },
                2 => new[] { (13, 17), (21, 30) },
                3 => new[] { (8, 20), (23, 22), (11, 28) },
                _ => new[] { (9, 11), (22, 20) },
            };
            foreach (var (x, y) in accents)
            {
                if (!leaves[y * 32 + x]) continue;
                if (style == 2)
                {
                    Box(image, x, y, 2, 3, Color.Color8(114, 88, 54));
                    Box(image, x, y, 1, 1, Color.Color8(181, 142, 84));
                }
                else if (style == 1)
                {
                    Box(image, x, y, 1, 3, Color.Color8(193, 181, 111));
                    Box(image, x, y, 1, 1, Color.Color8(235, 224, 159));
                }
                else
                {
                    Box(image, x - 1, y, 3, 1, Color.Color8(230, 222, 175));
                    Box(image, x, y - 1, 1, 3, Color.Color8(245, 235, 198));
                    Box(image, x, y, 1, 1, Color.Color8(206, 170, 88));
                }
            }
        }
        if (detail == 5)
            Box(image, 15, 42, 2, 2, style == 1 ? Color.Color8(79, 98, 81) : Color.Color8(77, 65, 44));
        if (detail is 2 or 4) image.FlipX();
    }

    private static void Oak(Image image)
    {
        Color bark = Color.Color8(125, 89, 54), shade = Color.Color8(76, 62, 43);
        Box(image, 13, 25, 6, 23, shade);
        Box(image, 13, 27, 2, 20, bark);
        Box(image, 10, 46, 12, 2, shade);
        Box(image, 11, 46, 4, 1, bark);
        Branch(image, 15, 33, -1, 7, bark);
        Branch(image, 17, 32, 1, 7, shade);
        var (dark, leaf, light) = LeafColors(0);
        Crown(image, 18, 22, 12, 12, dark, leaf, light, 1);
        Crown(image, 8, 20, 7, 10, dark, leaf, light, 2);
        Crown(image, 22, 15, 9, 11, dark, leaf, light, 3);
        Crown(image, 12, 12, 10, 11, dark, leaf, light, 4);
    }

    private static void Birch(Image image)
    {
        Color bark = Color.Color8(220, 218, 178), shade = Color.Color8(137, 159, 139), knot = Color.Color8(65, 83, 70);
        Box(image, 15, 19, 4, 29, shade);
        Box(image, 15, 21, 2, 26, bark);
        Box(image, 13, 47, 8, 1, knot);
        Branch(image, 15, 32, -1, 6, bark);
        Branch(image, 17, 29, 1, 5, shade);
        foreach (int y in new[] { 28, 34, 39, 44 })
        {
            Box(image, y % 2 == 0 ? 15 : 17, y, 2, 1, knot);
            Box(image, 18, y + 1, 1, 1, knot);
        }
        var (dark, leaf, light) = LeafColors(1);
        Crown(image, 18, 23, 7, 10, dark, leaf, light, 5);
        Crown(image, 12, 19, 6, 10, dark, leaf, light, 6);
        Crown(image, 16, 10, 7, 10, dark, leaf, light, 7);
    }

    private static void Pine(Image image)
    {
        Color trunk = Color.Color8(112, 84, 56), trunkShade = Color.Color8(65, 61, 46);
        Box(image, 14, 27, 5, 21, trunkShade);
        Box(image, 14, 28, 2, 19, trunk);
        Box(image, 12, 47, 9, 1, trunkShade);
        var (dark, leaf, light) = LeafColors(2);
        // Trois étages de branches : la silhouette reste immédiatement différente des feuillus.
        foreach (var tier in new[] { (Top: 22, Height: 17, Width: 14), (Top: 12, Height: 17, Width: 11), (Top: 2, Height: 18, Width: 8) })
            for (int y = tier.Top; y <= tier.Top + tier.Height; y++)
            {
                int half = 1 + (y - tier.Top) * tier.Width / tier.Height;
                if (y % 4 == 0 && half > 2) half--;
                for (int x = 16 - half; x <= 16 + half; x++)
                {
                    Color color = y >= tier.Top + tier.Height - 2 || x > 16 + half / 2 ? dark : leaf;
                    if (x < 16 && (x / 2 + y / 3 * 5) % 7 == 0 && y < tier.Top + tier.Height - 2) color = light;
                    image.SetPixel(x, y, color);
                }
            }
    }

    private static void Willow(Image image)
    {
        Color bark = Color.Color8(131, 110, 73), shade = Color.Color8(83, 83, 55);
        Box(image, 13, 25, 6, 23, shade);
        Box(image, 13, 28, 2, 19, bark);
        Box(image, 10, 46, 13, 2, shade);
        Branch(image, 14, 31, -1, 8, bark);
        Branch(image, 17, 31, 1, 8, shade);
        var (dark, leaf, light) = LeafColors(3);
        Crown(image, 9, 20, 8, 10, dark, leaf, light, 8);
        Crown(image, 23, 20, 8, 11, dark, leaf, light, 9);
        Crown(image, 16, 13, 13, 10, dark, leaf, light, 10);
        // Rameaux retombants, plus clairs côté soleil ; le tronc reste visible au centre.
        foreach (int x in new[] { 3, 6, 10, 22, 26, 29 })
        {
            int length = 9 + (x * 7 % 6);
            int top = 24 - Math.Abs(x - 16) / 5;
            for (int y = top; y < top + length; y++)
            {
                int drift = y % 5 == 0 ? 1 : 0;
                Color color = x < 16 ? leaf : dark;
                Box(image, x + drift, y, y % 3 == 0 ? 2 : 1, 1, color);
                if (x < 16 && y % 4 == 1) Box(image, x - 1, y, 1, 1, light);
            }
        }
    }

    private static void Acacia(Image image)
    {
        Color bark = Color.Color8(158, 115, 67), shade = Color.Color8(91, 73, 45);
        Box(image, 15, 24, 4, 24, shade); Box(image, 15, 26, 2, 21, bark);
        Box(image, 12, 47, 10, 1, shade);
        Branch(image, 15, 33, -1, 9, bark); Branch(image, 18, 30, 1, 8, shade);
        var (dark, leaf, light) = LeafColors(4);
        Crown(image, 9, 18, 9, 5, dark, leaf, light, 11);
        Crown(image, 22, 17, 9, 5, dark, leaf, light, 12);
        Crown(image, 15, 12, 12, 5, dark, leaf, light, 13);
    }

    private static void Tropical(Image image)
    {
        Color bark = Color.Color8(132, 105, 58), shade = Color.Color8(63, 71, 43);
        Box(image, 13, 22, 7, 26, shade); Box(image, 14, 24, 3, 23, bark);
        Branch(image, 15, 31, -1, 8, bark); Branch(image, 18, 30, 1, 8, shade);
        Box(image, 10, 46, 14, 2, shade);
        var (dark, leaf, light) = LeafColors(5);
        Crown(image, 9, 22, 9, 11, dark, leaf, light, 14);
        Crown(image, 22, 20, 9, 11, dark, leaf, light, 15);
        Crown(image, 16, 11, 13, 10, dark, leaf, light, 16);
        // Lianes devant le tronc, sans augmenter l'emprise ni la quantité de bois.
        for (int y = 28; y < 43; y++)
        {
            int x = 21 + y / 6 % 2;
            image.SetPixel(x, y, leaf);
            if (y % 4 == 0) Box(image, x - 1, y, 3, 1, light);
        }
    }

    private static void Crown(Image image, int cx, int cy, int rx, int ry, Color dark, Color leaf, Color light, int seed)
    {
        for (int y = Math.Max(0, cy - ry); y <= Math.Min(47, cy + ry); y++)
        for (int x = Math.Max(0, cx - rx); x <= Math.Min(31, cx + rx); x++)
        {
            float dx = (x - cx) / (float)rx, dy = (y - cy) / (float)ry;
            float edge = dx * dx + dy * dy;
            edge += ((x / 2 * 7 + y / 2 * 11 + seed) % 7 - 3) * 0.018f;
            if (edge > 1) continue;
            int cluster = (x / 3 * 7 + y / 3 * 13 + seed * 5) % 11;
            float sun = -dx * 0.65f - dy * 0.75f;
            Color color = edge > 0.87f || sun < -0.35f ? dark : sun > 0.4f ? light : leaf;
            if (cluster == 0 && edge < 0.78f) color = dark;
            if (cluster == 3 && sun > 0.1f && edge < 0.72f) color = light;
            image.SetPixel(x, y, color);
        }
    }

    private static void Branch(Image image, int x, int y, int direction, int length, Color color)
    {
        for (int i = 0; i < length; i++) Box(image, x + direction * i, y - i, 2, 2, color);
    }

    private static void Box(Image image, int x, int y, int width, int height, Color color)
    {
        for (int py = Math.Max(0, y); py < Math.Min(48, y + height); py++)
        for (int px = Math.Max(0, x); px < Math.Min(32, x + width); px++) image.SetPixel(px, py, color);
    }
}
