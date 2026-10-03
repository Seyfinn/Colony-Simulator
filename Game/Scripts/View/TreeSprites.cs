using System;
using Godot;

namespace GodColony.View;

/// <summary>Quatre apparences en 32 × 48 pixels. Elles partagent le même arbre dans la simulation.</summary>
public static class TreeSprites
{
    public const int StyleCount = 4;

    public static ImageTexture Create(int style)
    {
        var image = Image.CreateEmpty(32, 48, false, Image.Format.Rgba8);
        switch (style)
        {
            case 1: Birch(image); break;
            case 2: Pine(image); break;
            case 3: Willow(image); break;
            default: Oak(image); break;
        }
        return ImageTexture.CreateFromImage(image);
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
        Color dark = Color.Color8(43, 82, 56), leaf = Color.Color8(77, 119, 64), light = Color.Color8(127, 161, 84);
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
        Color dark = Color.Color8(59, 106, 71), leaf = Color.Color8(104, 153, 91), light = Color.Color8(157, 184, 113);
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
        Color dark = Color.Color8(36, 76, 63), leaf = Color.Color8(56, 111, 85), light = Color.Color8(103, 150, 112);
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
        Color dark = Color.Color8(64, 98, 62), leaf = Color.Color8(102, 143, 75), light = Color.Color8(154, 174, 102);
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
