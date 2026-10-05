using System;
using System.Collections.Generic;
using Godot;

namespace GodColony.View;

/// <summary>Architecture rurale en 3/4, avec une silhouette propre à chaque métier.</summary>
public static partial class BuildingSprites
{
    private static readonly Dictionary<(string Kind, WoodlandBiome Biome), ImageTexture> Textures = [];
    private static readonly Color Ink = C(49, 55, 48), Wood = C(108, 72, 47), WoodLight = C(177, 126, 73);
    private static readonly Color Stone = C(147, 155, 139), StoneLight = C(199, 199, 166), Mortar = C(87, 100, 89);
    private static readonly Color Soot = C(43, 48, 45), Brass = C(217, 169, 79), Glass = C(61, 106, 112);
    private static Color C(byte r, byte g, byte b) => Color.Color8(r, g, b);

    public static ImageTexture Get(string kind, WoodlandBiome biome = WoodlandBiome.TemperatePlain)
    {
        var key = (kind, biome);
        if (Textures.TryGetValue(key, out var texture)) return texture;

        // Une image fournie dans Game/Assets/buildings/ prime sur le dessin en code (voir le cahier des charges).
        string file = kind == "DamSide" ? "dam_side" : kind.ToLowerInvariant();
        string variant = biome.ToString().ToLowerInvariant();
        if ((AssetLibrary.Get($"buildings/{file}_{variant}.png") ?? AssetLibrary.Get($"buildings/{file}.png")) is { } provided)
            return Textures[key] = provided;

        if (System.Array.IndexOf(WorkshopArt.Buildings, kind) >= 0)
            return Textures[key] = WorkshopSource(kind, biome).Texture();
        if (kind == "Cask") return Textures[key] = CaskSource("empty").Texture();

        bool dam = kind is "Dam" or "DamSide";
        var art = new PixelArt(dam ? 32 : 64, dam ? 48 : 80);
        switch (kind)
        {
            case "Kiln": Kiln(art, biome); break;
            case "Bloomery": Bloomery(art, biome); break;
            case "Forge": Forge(art, biome); break;
            case "Mill": Mill(art, biome); break;
            case "Oven": Bakery(art, biome); break;
            case "Pen": Pen(art, biome); break;
            case "Loom": Loom(art, biome); break;
            case "Market": Market(art, biome); break;
            case "Infirmary": Infirmary(art, biome); break;
            case "Storehouse": Storehouse(art, biome); break;
            case "Well": Well(art, biome); break;
            case "Tavern": Tavern(art, biome); break;
            case "School": School(art, biome); break;
            case "Cask": Cask(art, biome); break;
            case "MineDepot": MineDepot(art, biome); break;
            case "PotteryKiln": PotteryKiln(art, biome); break;
            case "Tannery": Tannery(art, biome); break;
            case "Goldsmith": Goldsmith(art, biome); break;
            case "Dam": Dam(art); break;
            case "DamSide": SideDam(art); break;
            default: Cottage(art, biome); break;
        }
        return Textures[key] = art.Texture();
    }

    private static Color RoofColor(WoodlandBiome biome) => biome switch
    {
        WoodlandBiome.Dryland => C(193, 120, 77),
        WoodlandBiome.CoolForest => C(115, 132, 113),
        WoodlandBiome.Highland => C(99, 132, 143),
        WoodlandBiome.WetBank => C(145, 128, 87),
        _ => C(199, 161, 89),
    };
    private static Color Plaster(WoodlandBiome biome) => biome switch
    {
        WoodlandBiome.Dryland => C(223, 198, 153),
        WoodlandBiome.Highland => C(171, 180, 163),
        WoodlandBiome.CoolForest => C(193, 195, 161),
        _ => C(221, 205, 164),
    };

    private static void Foundation(PixelArt a, int x = 6, int width = 53)
    {
        a.Box(x, 70, width, 7, Mortar);
        Masonry(a, x + 1, 70, width - 2, 5, Stone, 7, 4);
        a.Box(x + 1, 76, width - 2, 1, Ink);
    }
    private static void Masonry(PixelArt a, int x, int y, int width, int height, Color baseColor, int bw = 8, int bh = 5)
    {
        a.Box(x, y, width, height, baseColor.Darkened(0.3f));
        for (int py = y; py < y + height; py++)
        for (int px = x; px < x + width; px++)
        {
            int row = (py - y) / bh, offset = row % 2 * (bw / 2);
            int bx = (px - x + offset) / bw;
            if ((py - y) % bh == bh - 1 || (px - x + offset) % bw == bw - 1) continue;
            float grain = ((bx * 19 + row * 7) % 5 - 2) * 0.027f;
            Color color = grain > 0 ? baseColor.Lightened(grain) : baseColor.Darkened(-grain);
            if ((py - y) % bh == 0) color = color.Lightened(0.13f);
            a.Dot(px, py, color);
        }
    }
    private static void Beam(PixelArt a, int x, int y, int width, int height)
    {
        a.Box(x, y, width, height, Ink);
        a.Box(x + 1, y, width - 1, height - 1, Wood);
        if (width > height) a.Box(x + 1, y, width - 1, 1, WoodLight);
        else a.Box(x + 1, y, 1, height - 1, WoodLight);
    }
    private static void Roof(PixelArt a, WoodlandBiome biome, int left = 2, int right = 62, int top = 11, int bottom = 43)
    {
        Color color = RoofColor(biome);
        // Deux pans : la lumière vient du haut à gauche, la rive droite descend en perspective.
        a.Polygon(Ink, new(left, bottom), new(left + 15, top), new(right - 13, top + 4), new(right, bottom + 3), new(right - 14, bottom + 7));
        a.Polygon(color, new(left + 1, bottom - 1), new(left + 16, top + 1), new(right - 14, top + 5), new(right - 14, bottom + 5));
        a.Polygon(color.Darkened(0.25f), new(right - 13, top + 5), new(right - 1, bottom + 2), new(right - 13, bottom + 5));
        bool tiles = biome is WoodlandBiome.Highland or WoodlandBiome.Dryland;
        for (int y = top + 2; y < bottom + 5; y++)
        for (int x = left + 2; x < right - 13; x++)
        {
            if (a.Image.GetPixel(x, y) != color) continue;
            int row = (y - top) / 5;
            if ((y - top) % 5 == 4) a.Dot(x, y, color.Darkened(0.14f));
            else if (tiles && (x + row % 2 * 4) % 8 == 0) a.Dot(x, y, color.Darkened(0.12f));
            else if ((x * 17 + y * 7) % 23 < 3) a.Dot(x, y, color.Lightened(0.13f));
        }
        a.Line(left + 16, top + 1, right - 14, top + 5, color.Lightened(0.3f));
        a.Line(left + 1, bottom, right - 14, bottom + 6, WoodLight);
        a.Line(right - 13, bottom + 6, right - 1, bottom + 3, Wood);
    }
    private static void Window(PixelArt a, int x, int y, bool flowers = false)
    {
        a.Box(x - 1, y - 1, 11, 13, Wood);
        a.Box(x, y, 9, 10, Soot);
        a.Box(x + 1, y + 1, 7, 8, Glass);
        a.Box(x + 1, y + 1, 3, 3, C(159, 192, 174));
        a.Box(x + 4, y, 1, 10, WoodLight);
        a.Box(x, y + 5, 9, 1, WoodLight);
        a.Box(x - 2, y + 11, 13, 2, StoneLight);
        if (!flowers) return;
        a.Box(x - 1, y + 12, 11, 3, Wood);
        a.Box(x, y + 12, 9, 1, C(86, 129, 77));
        foreach (int i in new[] { 1, 5, 8 }) { a.Dot(x + i, y + 10, C(208, 116, 95)); a.Dot(x + i - 1, y + 11, C(232, 189, 133)); }
    }
    private static void Chimney(PixelArt a, int x, int y, int height, Color? color = null)
    {
        Color stone = color ?? Stone;
        a.Box(x, y, 10, height, Mortar);
        Masonry(a, x + 1, y + 1, 8, height - 1, stone, 5, 4);
        a.Box(x - 1, y, 12, 3, stone.Lightened(0.18f));
        a.Box(x + 2, y, 6, 1, Soot);
        a.Box(x + 8, y + 3, 2, height - 3, stone.Darkened(0.22f));
    }
    private static void Logs(PixelArt a, int x, int y)
    {
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 2; col++)
        {
            int px = x + col * 6 + row % 2 * 2, py = y - row * 4;
            a.Box(px, py, 8, 4, Wood.Darkened(0.1f));
            a.Box(px, py, 7, 1, WoodLight);
            a.Oval(px + 1, py + 2, 2, 2, C(204, 160, 95));
            a.Dot(px + 1, py + 2, Wood);
        }
    }
    private static void Sack(PixelArt a, int x, int y)
    {
        a.Oval(x + 4, y + 6, 5, 6, C(152, 130, 86));
        a.Oval(x + 3, y + 5, 3, 5, C(218, 196, 144));
        a.Box(x + 1, y - 1, 5, 2, C(218, 196, 144));
        a.Box(x + 1, y + 1, 5, 1, Wood);
        a.Line(x + 2, y + 5, x + 2, y + 9, C(237, 220, 173));
    }
    private static void Hearth(PixelArt a, int x, int y, int width, int height)
    {
        a.Oval(x + width / 2, y + 3, width / 2 + 3, 6, Stone);
        a.Box(x - 2, y + 3, width + 4, height - 1, Stone);
        a.Oval(x + width / 2, y + 3, width / 2, 4, Soot);
        a.Box(x, y + 3, width, height - 2, Soot);
        a.Box(x + 1, y + height - 2, width - 2, 2, C(172, 72, 40));
        for (int i = 1; i < width - 1; i += 3)
        {
            a.Box(x + i, y + height - 4 - i % 2, 2, 3, C(229, 126, 50));
            a.Dot(x + i, y + height - 3, C(255, 199, 91));
        }
        a.Box(x - 3, y + height + 1, width + 6, 2, Mortar);
    }

    private static void Cottage(PixelArt a, WoodlandBiome biome)
    {
        Color plaster = Plaster(biome);
        a.Polygon(plaster.Darkened(0.22f), new(48, 39), new(59, 36), new(59, 71), new(48, 75));
        a.Box(7, 42, 42, 31, plaster);
        a.Box(7, 43, 42, 6, plaster.Darkened(0.16f));
        Foundation(a);
        foreach (int x in new[] { 7, 24, 46 }) Beam(a, x, 43, 3, 30);
        Beam(a, 7, 68, 42, 3);
        a.Line(10, 48, 23, 63, Wood); a.Line(38, 63, 46, 49, Wood);
        Roof(a, biome);
        // Lucarne, cheminée, volet latéral et entrée abritée.
        a.Polygon(Wood, new(13, 36), new(20, 23), new(29, 38));
        a.Polygon(plaster, new(16, 35), new(20, 27), new(26, 36));
        a.Box(19, 30, 5, 5, Glass); a.Box(19, 30, 2, 2, C(167, 194, 164));
        a.Line(12, 35, 20, 22, WoodLight); a.Line(20, 22, 30, 37, Wood);
        Chimney(a, 46, 8, 19);
        Window(a, 12, 51, biome != WoodlandBiome.Highland);
        a.Box(29, 50, 13, 24, Wood);
        a.Box(31, 52, 9, 22, C(72, 58, 42));
        for (int x = 32; x < 40; x += 3) a.Box(x, 53, 1, 20, Wood);
        a.Dot(38, 64, Brass);
        a.Box(29, 52, 13, 1, WoodLight);
        a.Polygon(Wood, new(27, 51), new(30, 47), new(42, 47), new(45, 52));
        a.Line(28, 51, 44, 52, WoodLight);
        a.Box(28, 74, 15, 2, StoneLight); a.Box(27, 77, 17, 2, Stone);
        a.Box(53, 49, 4, 9, Wood); a.Box(54, 50, 2, 7, Glass);
        a.Box(43, 56, 3, 6, Ink); a.Box(44, 57, 1, 3, Brass);
        a.Oval(8, 73, 4, 3, C(128, 101, 65)); a.Box(5, 71, 6, 1, WoodLight);
    }

    private static void Kiln(PixelArt a, WoodlandBiome biome)
    {
        Foundation(a, 3, 58);
        a.Oval(24, 53, 22, 21, Ink);
        a.Oval(24, 51, 21, 20, C(105, 112, 74));
        a.Oval(20, 48, 16, 17, C(153, 143, 92));
        for (int y = 34; y < 71; y++)
        for (int x = 4; x < 44; x++)
        {
            Color c = a.Image.GetPixel(x, y);
            if (c.A < 1 || c == Ink) continue;
            if (y % 6 == 0 && x > 8 && x < 40) a.Dot(x, y, c.Darkened(0.14f));
            else if ((x * 11 + y * 7) % 37 == 0) a.Dot(x, y, c.Lightened(0.15f));
        }
        a.Box(20, 28, 9, 6, Mortar); a.Box(19, 27, 11, 2, StoneLight); a.Box(22, 27, 5, 1, Soot);
        Hearth(a, 18, 57, 12, 12);
        // Petit abri à bûches, posé à côté du dôme de terre.
        Beam(a, 43, 46, 3, 26); Beam(a, 59, 44, 3, 29);
        a.Polygon(Ink, new(39, 47), new(44, 37), new(62, 39), new(63, 48));
        a.Polygon(RoofColor(biome).Darkened(0.12f), new(40, 45), new(45, 38), new(61, 40), new(62, 46));
        a.Line(40, 46, 62, 47, WoodLight);
        Logs(a, 44, 69);
        a.Oval(9, 71, 5, 3, Soot); a.Box(6, 68, 3, 2, C(94, 106, 97)); a.Dot(11, 70, Stone);
        a.Box(34, 57, 1, 16, WoodLight); a.Line(32, 73, 37, 69, Wood);
    }

    private static void Bloomery(PixelArt a, WoodlandBiome biome)
    {
        Foundation(a);
        // Fût légèrement évasé, briques réfractaires et couronne de pierre.
        a.Box(12, 18, 25, 54, Mortar);
        Masonry(a, 13, 19, 22, 51, C(173, 143, 113), 7, 5);
        a.Box(31, 21, 5, 49, C(130, 113, 90));
        a.Box(9, 66, 31, 5, Stone); a.Box(10, 66, 29, 1, StoneLight);
        a.Box(10, 15, 29, 5, Mortar); a.Box(11, 15, 27, 2, StoneLight);
        a.Oval(24, 14, 12, 4, Stone); a.Oval(24, 14, 8, 2, Soot);
        a.Line(13, 13, 29, 12, StoneLight);
        Hearth(a, 19, 55, 11, 14);
        // Auvent du soufflet : cuir plissé, bras de levier et tuyère.
        Beam(a, 39, 45, 3, 28); Beam(a, 58, 44, 3, 29);
        a.Polygon(Ink, new(36, 46), new(42, 32), new(57, 33), new(63, 46));
        a.Polygon(RoofColor(biome).Darkened(0.14f), new(37, 44), new(43, 33), new(56, 34), new(61, 44));
        a.Line(37, 44, 61, 45, WoodLight);
        a.Box(37, 63, 20, 3, Wood);
        a.Polygon(C(171, 110, 65), new(36, 61), new(53, 54), new(57, 61), new(36, 65));
        for (int x = 40; x < 56; x += 3) a.Line(x, 61, x - 2, 64, Wood);
        a.Line(36, 63, 31, 64, StoneLight); a.Line(48, 54, 57, 50, WoodLight);
        a.Box(42, 67, 12, 5, Ink); a.Box(43, 67, 10, 1, C(142, 164, 155));
        a.Oval(7, 72, 4, 3, C(158, 100, 64)); a.Dot(6, 70, C(227, 170, 96));
    }

    private static void Forge(PixelArt a, WoodlandBiome biome)
    {
        Foundation(a);
        a.Box(9, 43, 47, 27, C(83, 81, 64));
        a.Box(9, 43, 23, 23, C(111, 101, 76));
        for (int y = 48; y < 69; y += 5) a.Box(10, y, 44, 1, C(90, 82, 65));
        Masonry(a, 37, 39, 17, 30, Stone, 6, 4);
        Hearth(a, 41, 55, 9, 10);
        foreach (int x in new[] { 7, 32, 55 }) Beam(a, x, 40, 3, 34);
        Beam(a, 8, 44, 48, 3);
        a.Line(10, 47, 18, 55, WoodLight); a.Line(55, 47, 49, 53, WoodLight);
        Roof(a, biome == WoodlandBiome.TemperatePlain ? WoodlandBiome.Highland : biome, 2, 62, 11, 39);
        Chimney(a, 43, 6, 22);
        // L'enclume a un bec, une table brillante et un pied massif.
        a.Oval(23, 70, 9, 4, Wood); a.Box(19, 61, 8, 10, Wood); a.Box(20, 64, 2, 6, WoodLight);
        a.Polygon(Ink, new(12, 55), new(33, 55), new(29, 60), new(25, 61), new(25, 64), new(19, 64), new(19, 60));
        a.Box(16, 55, 17, 2, C(191, 210, 189)); a.Box(18, 58, 11, 2, C(105, 137, 136));
        a.Line(12, 55, 17, 56, StoneLight);
        Beam(a, 13, 48, 14, 2);
        a.Box(15, 50, 1, 6, WoodLight); a.Box(13, 51, 5, 2, Stone);
        a.Line(23, 50, 21, 56, C(164, 183, 162)); a.Line(24, 50, 25, 56, C(164, 183, 162));
        a.Oval(49, 72, 5, 4, C(76, 100, 98)); a.Oval(49, 69, 4, 2, C(143, 171, 161));
        a.Oval(49, 69, 2, 1, Glass); Logs(a, 4, 75);
    }

    private static void Mill(PixelArt a, WoodlandBiome biome)
    {
        Color plaster = Plaster(biome);
        Foundation(a, 5, 45);
        Masonry(a, 8, 55, 36, 19, Stone, 7, 5);
        a.Box(8, 33, 36, 23, plaster);
        a.Box(9, 34, 34, 5, plaster.Darkened(0.17f));
        foreach (int x in new[] { 7, 26, 42 }) Beam(a, x, 33, 3, 26);
        Beam(a, 8, 53, 35, 3);
        a.Line(11, 40, 24, 52, Wood); a.Line(29, 52, 42, 40, Wood);
        Roof(a, biome, 1, 54, 4, 33);
        Window(a, 13, 40);
        a.Box(28, 58, 11, 16, Wood); a.Box(30, 60, 7, 14, Wood.Darkened(0.3f));
        a.Box(31, 60, 1, 14, WoodLight); a.Dot(36, 67, Brass);
        a.Box(27, 75, 14, 2, StoneLight);
        // Coursier du moulin ; la roue à aubes est une animation indépendante.
        a.Box(44, 71, 19, 6, Mortar); a.Box(46, 71, 16, 3, C(104, 170, 175));
        // La roue est désormais dessinée séparément, pour tourner selon le débit réel.
        a.Line(47, 75, 51, 75, C(205, 226, 199)); a.Line(56, 73, 61, 73, C(190, 220, 204));
        Sack(a, 9, 65);
        a.Box(32, 38, 7, 9, Wood); a.Box(33, 39, 5, 7, C(73, 100, 94));
        // Épi sculpté sur l'enseigne.
        a.Line(35, 39, 35, 45, Brass); a.Dot(34, 40, Brass); a.Dot(36, 42, Brass);
    }

    private static void Bakery(PixelArt a, WoodlandBiome biome)
    {
        Color plaster = Plaster(biome);
        Foundation(a);
        a.Box(7, 39, 40, 32, plaster);
        a.Box(7, 40, 40, 6, plaster.Darkened(0.17f));
        Beam(a, 7, 40, 3, 32); Beam(a, 44, 40, 3, 32);
        Roof(a, biome == WoodlandBiome.TemperatePlain ? WoodlandBiome.Dryland : biome, 2, 59, 12, 39);
        Window(a, 13, 46);
        // Four voûté accolé à la boulangerie et cheminée de briques chaudes.
        a.Oval(47, 60, 14, 13, Mortar);
        a.Oval(45, 58, 12, 12, C(189, 136, 92));
        for (int y = 48; y < 70; y += 4)
        {
            int half = (int)(12 * Math.Sqrt(Math.Max(0, 1 - (y - 58) * (y - 58) / 144f)));
            a.Line(45 - half + 1, y, 45 + half - 1, y, C(151, 100, 68));
        }
        Chimney(a, 49, 27, 27, C(173, 122, 85));
        Hearth(a, 42, 60, 10, 10);
        // Auvent de toile, comptoir et pains entaillés.
        a.Polygon(Ink, new(10, 58), new(14, 53), new(34, 53), new(38, 59));
        a.Polygon(C(223, 190, 130), new(11, 57), new(15, 54), new(33, 54), new(36, 57));
        for (int x = 13; x <= 34; x += 6) a.Box(x, 58, 3, 3, C(185, 115, 79));
        Beam(a, 12, 61, 3, 13); Beam(a, 33, 61, 3, 13);
        a.Box(13, 64, 23, 7, Wood); a.Box(12, 63, 25, 2, WoodLight);
        for (int i = 0; i < 3; i++)
        {
            a.Oval(18 + i * 6, 62, 3, 2, C(220, 162, 81));
            a.Dot(17 + i * 6, 61, C(252, 218, 151));
        }
        Sack(a, 7, 66); a.Line(58, 58, 58, 75, WoodLight); a.Oval(58, 56, 2, 3, C(207, 157, 89));
    }

    private static void SideDam(PixelArt a)
    {
        // Même vanne vue latéralement lorsque le courant traverse la carte d'est en ouest.
        a.Polygon(Mortar, new(6, 15), new(24, 20), new(29, 44), new(12, 47));
        Masonry(a, 8, 18, 13, 11, Stone, 5, 4);
        Masonry(a, 12, 36, 16, 10, Stone, 6, 4);
        a.Box(10, 29, 12, 8, Soot);
        a.Box(10, 31, 13, 4, Color.Color8(92, 158, 157));
        a.Line(9, 33, 15, 33, Color.Color8(188, 218, 199));
        a.Box(19, 28, 5, 10, Wood); a.Box(19, 29, 1, 9, WoodLight);
        foreach (int y in new[] { 30, 34 }) a.Box(20, y, 3, 1, Wood.Darkened(0.3f));
        a.Line(7, 16, 23, 20, StoneLight); a.Line(12, 36, 28, 39, StoneLight);
        Beam(a, 21, 10, 3, 19); Beam(a, 25, 24, 3, 19);
        a.Line(22, 10, 27, 24, WoodLight); a.Line(23, 10, 28, 24, Wood);
        a.Oval(22, 9, 4, 4, Ink); a.Oval(22, 9, 3, 3, WoodLight); a.Oval(22, 9, 1, 1, Soot);
        a.Line(19, 9, 25, 9, Brass); a.Line(22, 6, 22, 12, Brass);
        a.Box(22, 13, 1, 18, StoneLight);
    }

    private static void Dam(PixelArt a)
    {
        // Une seule case de large ; le bâti s'élève au-dessus, sans agrandir l'emprise.
        a.Box(0, 28, 32, 16, Mortar);
        Masonry(a, 0, 28, 10, 16, Stone, 5, 4);
        Masonry(a, 22, 28, 10, 16, Stone, 5, 4);
        a.Box(10, 29, 12, 14, Soot); a.Box(11, 34, 10, 10, C(65, 113, 117));
        for (int x = 12; x < 21; x += 3)
        {
            a.Box(x, 35, 1, 10, C(128, 197, 188)); a.Dot(x, 44, C(209, 231, 204));
        }
        a.Box(9, 29, 14, 7, Wood); a.Box(10, 29, 12, 1, WoodLight);
        for (int y = 31; y < 36; y += 2) a.Box(10, y, 12, 1, Wood.Darkened(0.2f));
        a.Box(0, 26, 32, 3, StoneLight); a.Box(0, 44, 10, 2, Ink); a.Box(22, 44, 10, 2, Ink);
        foreach (int x in new[] { 5, 24 })
        {
            Beam(a, x, 14, 3, 26);
            a.Box(x - 1, 38, 5, 3, Mortar); a.Dot(x + 1, 23, Brass);
        }
        Beam(a, 4, 14, 25, 4);
        a.Line(7, 20, 13, 25, WoodLight); a.Line(25, 20, 20, 25, WoodLight);
        a.Box(15, 17, 2, 14, StoneLight); a.Box(15, 22, 2, 5, Mortar);
        a.Oval(16, 11, 5, 5, Ink); a.Oval(16, 11, 4, 4, WoodLight); a.Oval(16, 11, 2, 2, Soot);
        a.Line(12, 11, 20, 11, Brass); a.Line(16, 7, 16, 15, Brass); a.Dot(16, 11, StoneLight);
        a.Line(2, 46, 9, 46, C(173, 215, 201)); a.Line(21, 47, 28, 47, C(173, 215, 201));
    }
}
