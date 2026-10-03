using System;
using System.Collections.Generic;
using Godot;

namespace GodColony.View;

/// <summary>
/// Fabrique de petits sprites en pixel art, dessinés par le code.
/// Ce sont des graphismes provisoires, à remplacer plus tard par de vrais dessins.
/// </summary>
public static class SpriteFactory
{
    private static ImageTexture? _tree;
    private static ImageTexture? _bush;
    private static ImageTexture? _bushEmpty;
    private static ImageTexture[]? _campfire;
    private static readonly Dictionary<int, ImageTexture[]> Colonists = [];

    /// <summary>Arbre de 16 × 24 pixels : le tronc touche le bas de la case, le feuillage déborde au-dessus.</summary>
    public static ImageTexture Tree => _tree ??= BuildTree();

    public static ImageTexture Bush => _bush ??= BuildBush(withBerries: true);
    public static ImageTexture BushEmpty => _bushEmpty ??= BuildBush(withBerries: false);

    /// <summary>Feu de camp de 12 × 12 pixels, en deux images pour faire danser la flamme.</summary>
    public static ImageTexture[] Campfire => _campfire ??= [BuildCampfire(0), BuildCampfire(1)];

    // Colon de 8 × 12 pixels. h = cheveux, s = peau, c = vêtement, p = pantalon, b = bottes.
    private static readonly string[] ColonistBody =
    [
        "..hhhh..",
        ".hhhhhh.",
        ".hssssh.",
        "..ssss..",
        "..ssss..",
        ".cccccc.",
        "cccccccc",
        "s.cccc.s",
        "..cccc..",
        "..pppp..",
    ];
    private static readonly string[][] ColonistLegs = [["..p..p..", "..b..b.."], [".p....p.", ".b....b."]];

    private static readonly Color[] Clothes =
    [
        Color.Color8(168, 52, 48), Color.Color8(52, 92, 160), Color.Color8(196, 160, 60), Color.Color8(90, 132, 70),
        Color.Color8(128, 72, 140), Color.Color8(200, 112, 50), Color.Color8(70, 140, 150), Color.Color8(150, 140, 120),
    ];
    private static readonly Color[] Hair =
    [
        Color.Color8(40, 28, 20), Color.Color8(96, 60, 30), Color.Color8(200, 160, 90), Color.Color8(150, 60, 30), Color.Color8(120, 120, 120),
    ];
    private static readonly Color[] Skin =
    [
        Color.Color8(240, 200, 170), Color.Color8(214, 168, 128), Color.Color8(170, 120, 84), Color.Color8(110, 72, 48),
    ];

    /// <summary>Les deux images de marche d'un colon. Couleurs de peau, de cheveux et de vêtements tirées de son identifiant.</summary>
    public static ImageTexture[] Colonist(int id)
    {
        if (Colonists.TryGetValue(id, out ImageTexture[]? frames))
            return frames;
        var palette = new Dictionary<char, Color>
        {
            ['h'] = Hair[id * 7 % Hair.Length],
            ['s'] = Skin[id * 3 % Skin.Length],
            ['c'] = Clothes[id % Clothes.Length],
            ['p'] = Color.Color8(70, 56, 44),
            ['b'] = Color.Color8(40, 30, 24),
        };
        frames = [BuildColonist(palette, 0), BuildColonist(palette, 1)];
        Colonists[id] = frames;
        return frames;
    }

    private static ImageTexture BuildColonist(Dictionary<char, Color> palette, int frame)
    {
        var image = Image.CreateEmpty(8, 12, false, Image.Format.Rgba8);
        string[] rows = [.. ColonistBody, .. ColonistLegs[frame]];
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < 8; x++)
            if (palette.TryGetValue(rows[y][x], out Color color))
                image.SetPixel(x, y, color);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildCampfire(int frame)
    {
        var image = Image.CreateEmpty(12, 12, false, Image.Format.Rgba8);
        Color log = Color.Color8(100, 66, 38), logDark = Color.Color8(70, 44, 26);
        for (int x = 1; x < 11; x++)
        {
            image.SetPixel(x, 10, log);
            image.SetPixel(x, 11, logDark);
        }
        Color outer = Color.Color8(230, 110, 30), inner = Color.Color8(255, 210, 80);
        int sway = frame == 0 ? 0 : 1;
        for (int y = 2; y < 10; y++)
        {
            int half = (y - 2) / 2 + 1;
            for (int x = 6 - half + sway; x < 6 + half + sway - (y < 4 ? 1 : 0); x++)
                image.SetPixel(Math.Clamp(x, 0, 11), y, Math.Abs(x - 6 - sway) < half - 1 && y > 4 ? inner : outer);
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildTree()
    {
        var image = Image.CreateEmpty(16, 24, false, Image.Format.Rgba8);
        Color trunk = Color.Color8(96, 66, 40);
        Color trunkDark = Color.Color8(72, 48, 30);
        for (int y = 15; y < 24; y++)
        {
            image.SetPixel(7, y, trunk);
            image.SetPixel(8, y, trunkDark);
        }

        Color leafDark = Color.Color8(38, 84, 40);
        Color leaf = Color.Color8(52, 108, 48);
        Color leafLight = Color.Color8(78, 138, 58);
        for (int y = 0; y < 18; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = x - 7.5f, dy = y - 8.5f;
            float d = dx * dx / 56f + dy * dy / 72f;
            if (d > 1f) continue;
            // Lumière venant d'en haut à gauche, ombre en bas à droite.
            float light = -dx * 0.6f - dy * 0.8f;
            Color c = light > 3f ? leafLight : light < -3.5f || d > 0.85f ? leafDark : leaf;
            if ((x * 7 + y * 13) % 11 == 0) c = leafDark;
            image.SetPixel(x, y, c);
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildBush(bool withBerries)
    {
        var image = Image.CreateEmpty(12, 10, false, Image.Format.Rgba8);
        Color leaf = Color.Color8(60, 112, 50);
        Color leafDark = Color.Color8(44, 88, 40);
        Color berry = Color.Color8(190, 40, 52);
        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 12; x++)
        {
            float dx = x - 5.5f, dy = y - 5f;
            if (dx * dx / 36f + dy * dy / 25f > 1f) continue;
            image.SetPixel(x, y, dy > 1.5f ? leafDark : leaf);
        }
        if (withBerries)
            foreach ((int x, int y) in new[] { (3, 3), (7, 2), (5, 5), (8, 6), (2, 6) })
                image.SetPixel(x, y, berry);
        return ImageTexture.CreateFromImage(image);
    }
}
