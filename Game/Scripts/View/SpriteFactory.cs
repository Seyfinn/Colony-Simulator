using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>
/// Sprites en pixel art dessinés à leur résolution native, adaptés aux cases de 32 pixels.
/// </summary>
public static class SpriteFactory
{
    public const int TreeVariantCount = TreeSprites.StyleCount;
    public const int TreeDetailVariantCount = TreeSprites.DetailCount;
    private static readonly ImageTexture?[,] Trees = new ImageTexture?[TreeVariantCount, TreeDetailVariantCount];
    private static ImageTexture? _bush;
    private static ImageTexture? _bushEmpty;
    private static ImageTexture[]? _campfire;
    private static ImageTexture? _firepit, _grave, _chat, _sleep;
    private static readonly Dictionary<(int Id, bool Elder, WoodlandBiome Biome), ImageTexture[]> Colonists = [];

    /// <summary>Chêne, bouleau, conifère, saule, acacia et arbre tropical en 32 × 48 pixels.</summary>
    public static ImageTexture Tree => TreeVariant(0);
    public static ImageTexture TreeVariant(int variant, int detail = 0)
    {
        variant = Math.Clamp(variant, 0, TreeVariantCount - 1);
        detail = Math.Clamp(detail, 0, TreeDetailVariantCount - 1);
        return Trees[variant, detail] ??= TreeSprites.Create(variant, detail);
    }

    /// <summary>Maison de 64 × 80 pixels : architecture adaptée au milieu.</summary>
    public static ImageTexture Hut => BuildingSprites.Get("Hut");

    public static ImageTexture BuildingSprite(string kind, WoodlandBiome biome = WoodlandBiome.TemperatePlain)
    {
        return BuildingSprites.Get(kind, biome);
    }


    private static void FillBox(Image image, int x, int y, int width, int height, Color color)
    {
        for (int py = Math.Max(0, y); py < Math.Min(image.GetHeight(), y + height); py++)
        for (int px = Math.Max(0, x); px < Math.Min(image.GetWidth(), x + width); px++) image.SetPixel(px, py, color);
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

    /// <summary>Apparence du peuple, de l'âge, du milieu et du métier réellement portés par le colon.</summary>
    public static ImageTexture[] Colonist(Colonist person, WoodlandBiome biome) => PeoplesSprites.Get(PeoplesSprites.Describe(person, biome));

    /// <summary>Accès aux sprites préparés, même avant l'apparition d'un peuple dans une partie.</summary>
    public static ImageTexture[] Colonist(int id, PeopleLook people, WoodlandBiome biome = WoodlandBiome.TemperatePlain,
        LifeStage stage = LifeStage.Adult, Sex sex = Sex.Male, OutfitTrade trade = OutfitTrade.Everyday) =>
        PeoplesSprites.Get(new(id, people, biome, stage, sex, trade));

    /// <summary>Apparence humaine historique, conservée pour les appels existants.</summary>
    public static ImageTexture[] Colonist(int id, bool elder = false, WoodlandBiome biome = WoodlandBiome.TemperatePlain)
    {
        var key = (id, elder, biome);
        if (Colonists.TryGetValue(key, out ImageTexture[]? frames))
            return frames;
        Color hair = elder ? Color.Color8(170, 173, 163) : Hair[id * 7 % Hair.Length];
        Color clothes = BiomeVisuals.Clothing(biome, id, Clothes[id % Clothes.Length]);
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
        frames = [BuildColonist(palette, 0, biome, id), BuildColonist(palette, 1, biome, id), BuildColonist(palette, 2, biome, id), BuildColonist(palette, 3, biome, id)];
        Colonists[key] = frames;
        return frames;
    }

    private static ImageTexture BuildColonist(Dictionary<char, Color> palette, int frame, WoodlandBiome biome, int id)
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
        BiomeVisuals.Dress(image, biome, id, palette['C'], palette['s'], frame);
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
