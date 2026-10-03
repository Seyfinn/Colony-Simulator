using System;
using System.Collections.Generic;
using Godot;

namespace GodColony.View;

/// <summary>
/// Sprites en pixel art dessinés à leur résolution native, adaptés aux cases de 32 pixels.
/// </summary>
public static class SpriteFactory
{
    private static readonly ImageTexture?[] Trees = new ImageTexture?[3];
    private static ImageTexture? _bush;
    private static ImageTexture? _bushEmpty;
    private static ImageTexture[]? _campfire;
    private static ImageTexture? _firepit, _grave, _chat, _sleep;
    private static readonly Dictionary<(int Id, bool Elder), ImageTexture[]> Colonists = [];
    private static readonly Dictionary<string, ImageTexture> Workshops = [];

    /// <summary>Arbres de 32 × 48 pixels, avec trois silhouettes et palettes.</summary>
    public static ImageTexture Tree => TreeVariant(0);
    public static ImageTexture TreeVariant(int variant)
    {
        variant = Math.Clamp(variant, 0, Trees.Length - 1);
        return Trees[variant] ??= BuildTree(variant);
    }

    private static ImageTexture? _hut;

    /// <summary>Hutte de 64 × 80 pixels : murs à colombages et toit de chaume.</summary>
    public static ImageTexture Hut => _hut ??= BuildHut();

    public static ImageTexture BuildingSprite(string kind)
    {
        if (kind == "Hut") return Hut;
        if (!Workshops.TryGetValue(kind, out var texture))
            Workshops[kind] = texture = BuildWorkshop(kind);
        return texture;
    }

    private static ImageTexture BuildWorkshop(string kind)
    {
        var image = Image.CreateEmpty(64, 80, false, Image.Format.Rgba8);
        Color stone = Color.Color8(144, 158, 140), joint = Color.Color8(78, 95, 87);
        Color timber = Color.Color8(104, 78, 50), light = Color.Color8(173, 131, 78);
        FillBox(image, 5, 72, 54, 5, joint);
        FillBox(image, 6, 72, 51, 2, stone);
        if (kind == "Kiln")
        {
            Oval(image, 32, 54, 25, 20, Color.Color8(101, 104, 72));
            Oval(image, 29, 51, 20, 16, Color.Color8(156, 151, 100));
            for (int y = 38; y < 68; y += 5)
            {
                int half = (int)(23 * Math.Sqrt(Math.Max(0, 1 - (y - 54) * (y - 54) / 400f)));
                FillBox(image, 32 - half, y, half * 2, 1, Color.Color8(119, 123, 80));
            }
            FillBox(image, 28, 31, 8, 7, joint);
            FillBox(image, 29, 30, 6, 2, stone);
            Oval(image, 32, 61, 7, 8, joint);
            FillBox(image, 25, 61, 15, 11, joint);
            FillBox(image, 28, 60, 9, 11, Color.Color8(42, 51, 47));
            FillBox(image, 43, 69, 13, 3, timber);
            FillBox(image, 43, 68, 12, 1, light);
        }
        else if (kind == "Bloomery")
        {
            FillBox(image, 17, 18, 31, 54, joint);
            FillBox(image, 19, 19, 27, 52, stone);
            FillBox(image, 40, 19, 6, 52, Color.Color8(111, 133, 119));
            for (int y = 22; y < 69; y += 6)
            {
                FillBox(image, 19, y, 27, 1, joint);
                for (int x = 20 + y / 6 % 2 * 5; x < 45; x += 10) FillBox(image, x, y + 1, 1, 5, joint);
            }
            FillBox(image, 15, 16, 35, 4, stone);
            FillBox(image, 18, 14, 29, 2, Color.Color8(190, 201, 172));
            FillBox(image, 23, 13, 19, 2, Color.Color8(52, 67, 61));
            FillBox(image, 25, 53, 13, 19, joint);
            FillBox(image, 28, 55, 7, 15, Color.Color8(52, 60, 54));
            FillBox(image, 5, 58, 10, 13, timber);
            FillBox(image, 6, 59, 8, 2, light);
            FillBox(image, 7, 62, 6, 5, Color.Color8(151, 112, 76));
        }
        else
        {
            // Forge ouverte : charpente, petit foyer et enclume sous un toit d'ardoise.
            foreach (int x in new[] { 7, 52 })
            {
                FillBox(image, x, 40, 4, 33, timber);
                FillBox(image, x, 40, 1, 32, light);
            }
            FillBox(image, 8, 66, 48, 6, timber);
            FillBox(image, 38, 40, 15, 26, joint);
            for (int y = 41; y < 65; y += 5) FillBox(image, 39, y, 13, 3, stone);
            FillBox(image, 42, 54, 8, 9, Color.Color8(43, 55, 50));
            FillBox(image, 20, 58, 8, 12, timber);
            FillBox(image, 16, 53, 17, 3, Color.Color8(173, 192, 179));
            FillBox(image, 19, 56, 11, 4, Color.Color8(102, 133, 126));
            FillBox(image, 20, 60, 9, 2, joint);
            FillBox(image, 12, 54, 5, 1, Color.Color8(173, 192, 179));
            for (int y = 10; y <= 43; y++)
            {
                int left = 17 - (y - 10) * 14 / 33;
                int right = 48 + (y - 10) * 13 / 33;
                for (int x = left; x <= right; x++)
                    image.SetPixel(x, y, y % 5 == 0 ? joint : (x / 5 + y / 5) % 3 == 0
                        ? Color.Color8(132, 158, 148) : Color.Color8(96, 128, 120));
            }
            FillBox(image, 3, 43, 58, 3, timber);
            FillBox(image, 46, 7, 9, 22, joint);
            FillBox(image, 47, 7, 7, 3, stone);
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildHut()
    {
        var image = Image.CreateEmpty(64, 80, false, Image.Format.Rgba8);
        Color timber = Color.Color8(100, 69, 46), timberLight = Color.Color8(151, 107, 66);
        Color wall = Color.Color8(205, 180, 131), wallShade = Color.Color8(167, 139, 99);
        Color stone = Color.Color8(118, 117, 104), stoneLight = Color.Color8(162, 156, 132);
        FillBox(image, 6, 43, 49, 30, wall);
        FillBox(image, 55, 42, 6, 29, wallShade);
        // Grain discret de l'enduit et ombre sous la corniche.
        for (int y = 45; y < 73; y++)
        for (int x = 8; x < 55; x++)
            if ((x * 17 + y * 11) % 47 == 0) image.SetPixel(x, y, wallShade.Lerp(wall, 0.65f));
        FillBox(image, 6, 43, 49, 5, wallShade);
        foreach (int x in new[] { 6, 24, 38, 53 })
        {
            FillBox(image, x, 43, 3, 30, timber);
            FillBox(image, x, 49, 1, 22, timberLight);
        }
        FillBox(image, 6, 65, 49, 2, timber);
        FillBox(image, 6, 72, 54, 5, stone);
        for (int x = 7; x < 60; x += 7)
        {
            FillBox(image, x, 72, 5, 1, stoneLight);
            image.SetPixel(x, 75, timber);
        }
        // Porte en planches, encadrée de bois, avec seuil de pierre.
        FillBox(image, 27, 50, 11, 25, timber);
        FillBox(image, 29, 53, 7, 21, Color.Color8(73, 53, 38));
        for (int x = 29; x < 36; x += 3) FillBox(image, x, 54, 1, 18, timberLight.Lerp(timber, 0.6f));
        image.SetPixel(34, 64, Color.Color8(221, 183, 87));
        FillBox(image, 26, 75, 14, 3, stoneLight);
        // Deux petites fenêtres en retrait, croisillons et rebords.
        foreach (int x in new[] { 12, 43 })
        {
            FillBox(image, x - 1, 51, 10, 12, timber);
            FillBox(image, x, 52, 8, 9, Color.Color8(49, 78, 81));
            FillBox(image, x, 52, 3, 3, Color.Color8(126, 167, 155));
            FillBox(image, x + 3, 52, 1, 9, timberLight);
            FillBox(image, x, 56, 8, 1, timberLight);
            FillBox(image, x - 2, 62, 12, 2, timberLight);
        }
        // Chaume en mèches courtes : lumière en haut à gauche, pan droit ombré.
        for (int y = 8; y <= 44; y++)
        {
            int left = 17 - (y - 8) * 14 / 36;
            int right = 48 + (y - 8) * 13 / 36;
            for (int x = left; x <= right; x++)
            {
                bool side = x > 46 + (y - 8) / 4;
                int straw = (x / 2 * 13 + y / 4 * 7) % 9;
                Color c = side ? Color.Color8(142, 108, 61) : Color.Color8(191, 155, 86);
                if (straw < 2) c = c.Lightened(0.16f);
                if (y % 6 == 5) c = c.Darkened(0.13f);
                if (x == left || x == right || y == 44) c = timber;
                image.SetPixel(x, y, c);
            }
        }
        FillBox(image, 17, 7, 32, 2, Color.Color8(222, 185, 108));
        FillBox(image, 4, 45, 56, 2, timber);
        return ImageTexture.CreateFromImage(image);
    }

    private static void FillBox(Image image, int x, int y, int width, int height, Color color)
    {
        for (int py = y; py < y + height; py++)
        for (int px = x; px < x + width; px++)
            image.SetPixel(px, py, color);
    }

    private static ImageTexture? _stump;

    /// <summary>Souche de 16 × 14 pixels, avec anneaux de croissance et racines.</summary>
    public static ImageTexture Stump => _stump ??= BuildStump();

    public static ImageTexture Bush => _bush ??= BuildBush(withBerries: true);
    public static ImageTexture BushEmpty => _bushEmpty ??= BuildBush(withBerries: false);

    /// <summary>Foyer de pierre de 32 × 32 pixels et quatre poses de flamme.</summary>
    public static ImageTexture[] Campfire => _campfire ??= [BuildCampfire(0), BuildCampfire(1), BuildCampfire(2), BuildCampfire(3)];
    public static ImageTexture Firepit => _firepit ??= BuildCampfire(-1);
    public static ImageTexture Grave => _grave ??= BuildGrave();
    public static ImageTexture ChatBubble => _chat ??= BuildChatBubble();
    public static ImageTexture Sleep => _sleep ??= BuildSleep();

    // Colon de 16 × 24 pixels. Contour, mèches, visage et tunique ont leur propre palette.
    private static readonly string[] ColonistBody =
    [
        ".....oooooo.....",
        "....ohhhhhho....",
        "....ohHHhhho....",
        "...ohHHHhhhho...",
        "...ohhsssssho...",
        "....osSssSso....",
        "....osesseso....",
        "....osssssso....",
        ".....osssso.....",
        ".....osSSso.....",
        "....ooLssLoo....",
        "...ocCLLLCcco...",
        "...ocCCCCCcco...",
        "...ocCCCCCcco...",
        "....oCCCCCco....",
        "....oCCCCCco....",
        "....ocCCCCco....",
        "....otttttto....",
        ".....oppppo.....",
    ];
    private static readonly string[][] ColonistLegs =
    [
        [".....oppppo.....", ".....opoppo.....", ".....op.opo.....", "....obb.obbo....", "....ooo.oooo...."],
        ["....opppppo.....", "....oppoopo.....", "....opo..opo....", "...obbo..opo....", "...oooo..obbo..."],
        [".....oppppo.....", ".....opoppo.....", ".....op.opo.....", "....obb.obbo....", "....ooo.oooo...."],
        [".....opppppo....", ".....opooppo....", "....opo..opo....", "....opo..obbo...", "...obbo..oooo..."],
    ];

    private static readonly Color[] Clothes =
    [
        Color.Color8(173, 94, 80), Color.Color8(80, 133, 139), Color.Color8(207, 169, 94), Color.Color8(121, 153, 110),
        Color.Color8(147, 112, 141), Color.Color8(190, 130, 87), Color.Color8(101, 155, 148), Color.Color8(211, 200, 163),
    ];
    private static readonly Color[] Hair =
    [
        Color.Color8(47, 42, 38), Color.Color8(102, 74, 53), Color.Color8(205, 174, 110),
        Color.Color8(155, 91, 59), Color.Color8(136, 122, 100), Color.Color8(74, 61, 51),
    ];
    private static readonly Color[] Skin =
    [
        Color.Color8(238, 199, 158), Color.Color8(217, 169, 125), Color.Color8(189, 139, 96),
        Color.Color8(145, 102, 70), Color.Color8(107, 76, 54), Color.Color8(227, 183, 145),
    ];

    /// <summary>Quatre poses de marche et cheveux gris pour les anciens, sans modifier la simulation.</summary>
    public static ImageTexture[] Colonist(int id, bool elder = false)
    {
        var key = (id, elder);
        if (Colonists.TryGetValue(key, out ImageTexture[]? frames))
            return frames;
        Color hair = elder ? Color.Color8(170, 173, 163) : Hair[id * 7 % Hair.Length];
        Color clothes = Clothes[id % Clothes.Length];
        Color skin = Skin[id * 5 % Skin.Length];
        var palette = new Dictionary<char, Color>
        {
            ['o'] = Color.Color8(45, 43, 38),
            ['h'] = hair,
            ['H'] = hair.Lightened(0.24f),
            ['s'] = skin,
            ['S'] = skin.Lightened(0.12f),
            ['e'] = Color.Color8(48, 44, 39),
            ['c'] = clothes.Darkened(0.23f),
            ['C'] = clothes,
            ['L'] = clothes.Lightened(0.26f),
            ['t'] = Color.Color8(101, 76, 46),
            ['p'] = Color.Color8(81, 78, 62),
            ['b'] = Color.Color8(55, 46, 37),
        };
        frames = [BuildColonist(palette, 0), BuildColonist(palette, 1), BuildColonist(palette, 2), BuildColonist(palette, 3)];
        Colonists[key] = frames;
        return frames;
    }

    private static ImageTexture BuildColonist(Dictionary<char, Color> palette, int frame)
    {
        var image = Image.CreateEmpty(16, 24, false, Image.Format.Rgba8);
        string[] rows = [.. ColonistBody, .. ColonistLegs[frame]];
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < 16; x++)
            if (palette.TryGetValue(rows[y][x], out Color color))
                image.SetPixel(x, y, color);
        // Les bras balancent à l'opposé des jambes ; les épaules restent stables.
        int swing = frame == 1 ? 1 : frame == 3 ? -1 : 0;
        foreach (int side in new[] { 0, 1 })
        {
            int x = side == 0 ? 2 : 12;
            int y = 13 + (side == 0 ? swing : -swing);
            FillBox(image, x, y, 2, 3, palette['c']);
            FillBox(image, x, y + 3, 2, 2, palette['s']);
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildCampfire(int frame)
    {
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        Oval(image, 16, 24, 14, 6, Color.Color8(65, 66, 55));
        Oval(image, 16, 23, 11, 4, Color.Color8(88, 70, 51));
        for (int x = 7; x < 25; x++)
        {
            int y = 20 + (x - 7) / 4;
            FillBox(image, x, y, 1, 3, Color.Color8(87, 59, 41));
            FillBox(image, x, 26 - (x - 7) / 4, 1, 2, Color.Color8(141, 92, 52));
        }
        foreach (var stone in new[] { (5, 22), (8, 26), (15, 28), (23, 26), (28, 22), (25, 18), (8, 18) })
        {
            Oval(image, stone.Item1, stone.Item2, 3, 2, Color.Color8(99, 112, 105));
            FillBox(image, stone.Item1 - 1, stone.Item2 - 1, 3, 1, Color.Color8(174, 179, 149));
        }
        if (frame < 0) return ImageTexture.CreateFromImage(image);
        for (int x = 11; x < 23; x += 3) image.SetPixel(x, 24, Color.Color8(224, 102, 48));
        int lean = frame is 1 or 2 ? 1 : -1;
        for (int y = 5; y < 24; y++)
        for (int x = 8; x < 25; x++)
        {
            float center = 16 + lean * (24 - y) / 9f;
            float half = 1 + (y - 5) * 0.35f;
            if (y < 8 + frame % 2 || Math.Abs(x - center) > half) continue;
            Color c = Math.Abs(x - 16) < half * 0.45f && y > 14
                ? Color.Color8(255, 230, 152) : x < center ? Color.Color8(248, 179, 75) : Color.Color8(219, 105, 49);
            image.SetPixel(x, y, c);
        }
        FillBox(image, 10 + frame, 14 - frame, 2, 4, Color.Color8(245, 168, 63));
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildGrave()
    {
        var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        Color rim = Color.Color8(69, 85, 79), stone = Color.Color8(145, 159, 140);
        Oval(image, 16, 12, 8, 8, rim);
        FillBox(image, 8, 12, 17, 15, rim);
        Oval(image, 15, 12, 6, 6, stone);
        FillBox(image, 10, 12, 13, 13, stone);
        FillBox(image, 10, 12, 2, 12, Color.Color8(180, 188, 158));
        FillBox(image, 21, 12, 2, 13, Color.Color8(112, 134, 121));
        FillBox(image, 6, 26, 21, 3, rim);
        FillBox(image, 7, 26, 18, 1, stone);
        // Petit losange gravé, inscription et fleurs au pied.
        foreach (var p in new[] { (16, 10), (15, 11), (17, 11), (16, 12) }) image.SetPixel(p.Item1, p.Item2, rim);
        FillBox(image, 13, 17, 7, 1, rim);
        FillBox(image, 14, 20, 5, 1, rim);
        FillBox(image, 25, 23, 1, 6, Color.Color8(88, 127, 82));
        FillBox(image, 24, 24, 3, 2, Color.Color8(202, 151, 126));
        image.SetPixel(25, 24, ArtDirection.Cream);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildChatBubble()
    {
        var image = Image.CreateEmpty(24, 16, false, Image.Format.Rgba8);
        FillBox(image, 3, 0, 18, 13, ArtDirection.Charcoal);
        FillBox(image, 1, 2, 22, 9, ArtDirection.Charcoal);
        FillBox(image, 3, 1, 18, 11, ArtDirection.Cream);
        FillBox(image, 2, 3, 20, 7, ArtDirection.Cream);
        FillBox(image, 7, 12, 4, 3, ArtDirection.Charcoal);
        FillBox(image, 8, 11, 2, 3, ArtDirection.Cream);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildSleep()
    {
        var image = Image.CreateEmpty(12, 12, false, Image.Format.Rgba8);
        Oval(image, 5, 6, 4, 4, ArtDirection.Cream);
        Oval(image, 7, 4, 4, 4, Colors.Transparent);
        FillBox(image, 9, 2, 1, 3, ArtDirection.Sage);
        FillBox(image, 8, 3, 3, 1, ArtDirection.Sage);
        return ImageTexture.CreateFromImage(image);
    }

    private static void Oval(Image image, int cx, int cy, int rx, int ry, Color color)
    {
        for (int y = Math.Max(0, cy - ry); y <= Math.Min(image.GetHeight() - 1, cy + ry); y++)
        for (int x = Math.Max(0, cx - rx); x <= Math.Min(image.GetWidth() - 1, cx + rx); x++)
            if ((x - cx) * (x - cx) / (float)(rx * rx) + (y - cy) * (y - cy) / (float)(ry * ry) <= 1)
                image.SetPixel(x, y, color);
    }

    private static ImageTexture BuildTree(int variant)
    {
        var image = Image.CreateEmpty(32, 48, false, Image.Format.Rgba8);
        Color trunk = Color.Color8(137, 99, 62);
        Color trunkDark = Color.Color8(83, 66, 45);
        for (int y = 25; y < 48; y++)
        for (int x = 13; x < 19; x++)
        {
            if (y < 44 && (x == 13 || x == 18)) continue;
            image.SetPixel(x, y, x < 16 ? trunk : trunkDark);
        }

        Color leafDark = Color.Color8(43, (byte)(83 + variant * 5), 57);
        Color leaf = Color.Color8((byte)(66 + variant * 8), (byte)(119 + variant * 6), 66);
        Color leafLight = Color.Color8((byte)(110 + variant * 8), (byte)(155 + variant * 5), 83);
        Color highlight = Color.Color8(149, 179, 104);
        for (int y = 0; y < 38; y++)
        for (int x = 0; x < 32; x++)
        {
            float dx = x - 15.5f, dy = y - (variant == 1 ? 19f : 18f);
            float d = dx * dx / (variant == 1 ? 160f : 225f) + dy * dy / 310f;
            d += ((x / 3 * 7 + y / 3 * 13 + variant * 3) % 9 - 4) * 0.018f;
            if (d > 1f) continue;
            float light = -dx * 0.6f - dy * 0.8f;
            int cluster = (x / 3 * 7 + y / 3 * 13 + variant * 5) % 11;
            Color c = light > 6f ? leafLight : light < -7f || d > 0.88f ? leafDark : leaf;
            if (cluster == 0 && d < 0.8f) c = leafDark;
            if (cluster == 3 && light > 8f && d < 0.75f) c = highlight;
            image.SetPixel(x, y, c);
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildStump()
    {
        var image = Image.CreateEmpty(16, 14, false, Image.Format.Rgba8);
        Color bark = Color.Color8(122, 90, 60), top = Color.Color8(204, 165, 106), dark = Color.Color8(75, 63, 45);
        FillBox(image, 3, 5, 10, 7, bark);
        FillBox(image, 3, 5, 2, 7, dark);
        FillBox(image, 11, 5, 2, 7, dark);
        FillBox(image, 1, 11, 14, 2, dark);
        Oval(image, 8, 5, 6, 3, dark);
        Oval(image, 8, 5, 5, 2, top);
        Oval(image, 8, 5, 3, 1, bark);
        FillBox(image, 7, 5, 3, 1, top);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildBush(bool withBerries)
    {
        var image = Image.CreateEmpty(24, 20, false, Image.Format.Rgba8);
        Color leaf = Color.Color8(83, 131, 68);
        Color leafDark = Color.Color8(48, 92, 57);
        Color leafLight = Color.Color8(126, 163, 82);
        Color berry = Color.Color8(180, 62, 73);
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 24; x++)
        {
            float dx = x - 11.5f, dy = y - 10f;
            if (dx * dx / 132f + dy * dy / 85f > 1f) continue;
            image.SetPixel(x, y, dy > 4 || dx > 8 ? leafDark
                : (x / 3 + y / 3 * 7) % 5 == 0 && dy < 2 ? leafLight : leaf);
        }
        if (withBerries)
            foreach ((int x, int y) in new[] { (6, 6), (14, 4), (10, 10), (16, 12), (4, 12) })
            {
                image.SetPixel(x, y, berry);
                image.SetPixel(x + 1, y, Color.Color8(230, 125, 114));
                image.SetPixel(x, y + 1, berry);
                image.SetPixel(x + 1, y + 1, berry);
            }
        return ImageTexture.CreateFromImage(image);
    }
}
