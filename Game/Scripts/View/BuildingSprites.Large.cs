using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

public static partial class BuildingSprites
{
    private static readonly Dictionary<(BuildingType Type, WoodlandBiome Biome, int Width, int Height), ImageTexture> Ouvrages = [];

    /// <summary>La silhouette suit l'emprise réelle, avec des détails à l'échelle des habitants.</summary>
    public static ImageTexture For(Building building, WoodlandBiome biome = WoodlandBiome.TemperatePlain)
    {
        if (building.Width == 2 && building.Height == 2)
            return Get(building.Type.ToString(), biome);
        var key = (building.Type, biome, building.Width, building.Height);
        if (Ouvrages.TryGetValue(key, out var cached)) return cached;
        string kind = building.IsDam && building.Height > building.Width ? "dam_side" : building.Type.ToString().ToLowerInvariant();
        string suffix = building.IsExtension ? "extension" : building.Width == 1 && building.Height == 1 ? "small" : "large";
        int width = building.Width * 32, height = building.Height * 32 + 16;
        var provided = AssetLibrary.Get($"buildings/{kind}_{biome.ToString().ToLowerInvariant()}_{suffix}.png")
            ?? AssetLibrary.Get($"buildings/{kind}_{suffix}.png");
        if (provided is not null && provided.GetWidth() == width && provided.GetHeight() == height)
            return Ouvrages[key] = provided;
        return Ouvrages[key] = OuvrageSource(building, biome).Texture();
    }

    internal static PixelArt OuvrageSource(Building b, WoodlandBiome biome)
    {
        int w = b.Width * 32, h = b.Height * 32 + 16;
        var a = new PixelArt(w, h);
        if (b.Type == BuildingType.MineDepot) GrandeMine(a, biome);
        else if (b.IsDam) GrandBarrage(a, b.Height > b.Width);
        else if (b.Type == BuildingType.Pen) GrandEnclos(a, biome, b.IsExtension);
        else if (b.Type == BuildingType.Market) GrandMarche(a, biome);
        else if (b.Width == 1)
        {
            Image image = b.Type == BuildingType.Cask ? CaskSource("empty").Image : Get(b.Type.ToString(), biome).GetImage();
            image.Resize(w, h, Image.Interpolation.Nearest);
            a.Image.BlitRect(image, new Rect2I(0, 0, w, h), Vector2I.Zero);
        }
        else
        {
            // Une aile de service prolonge le bâtiment, avec des détails à la taille des habitants.
            int baseY = h - 80;
            a.Box(51, baseY + 38, w - 58, 35, Plaster(biome));
            Masonry(a, 51, h - 10, w - 58, 7, Stone);
            a.Polygon(Ink, new(49, baseY + 40), new(61, baseY + 14), new(w - 15, baseY + 19), new(w - 2, baseY + 43));
            a.Polygon(RoofColor(biome), new(51, baseY + 39), new(62, baseY + 15), new(w - 16, baseY + 20), new(w - 4, baseY + 41));
            for (int x = 68; x < w - 14; x += 22)
            {
                Beam(a, x - 4, baseY + 43, 3, 30);
                a.Box(x, baseY + 49, 10, 13, Wood);
                a.Box(x + 1, baseY + 50, 8, 10, Glass);
            }
            Image core = Get(b.Type.ToString(), biome).GetImage();
            a.Image.BlitRect(core, new Rect2I(0, 0, 64, 80), new Vector2I(0, baseY));
            if (b.Type == BuildingType.Storehouse)
            {
                for (int x = 69; x < w - 18; x += 25) { Logs(a, x, h - 8); Sack(a, x + 12, h - 19); }
                a.Box(66, h - 38, 30, 27, Wood.Darkened(0.3f));
                for (int x = 67; x < 95; x += 5) a.Line(x, h - 37, x, h - 12, Wood);
                Beam(a, 65, h - 40, 32, 3);
            }
        }
        return a;
    }

    private static void GrandeMine(PixelArt a, WoodlandBiome biome)
    {
        // Le sol usé relie la galerie, le puits et le hangar de tri, tous à l'échelle d'un ouvrier.
        CourTexturee(a, 5, 65, 182, 75, C(130, 122, 100));
        a.Polygon(Mortar, new(3, 75), new(12, 49), new(29, 34), new(53, 39), new(73, 65), new(70, 112), new(5, 113));
        a.Polygon(Stone, new(6, 71), new(14, 50), new(30, 37), new(38, 63), new(29, 88), new(8, 100));
        a.Polygon(Stone.Darkened(.16f), new(31, 37), new(51, 42), new(68, 63), new(61, 94), new(38, 83));
        a.Line(15, 52, 29, 40, StoneLight); a.Line(12, 68, 27, 60, StoneLight);
        a.Line(47, 45, 42, 57, Mortar); a.Line(54, 56, 65, 68, StoneLight);
        Masonry(a, 13, 70, 13, 40, Stone, 7, 5); Masonry(a, 53, 68, 12, 42, Stone, 7, 5);
        a.Oval(39, 78, 16, 17, Ink); a.Box(24, 78, 30, 32, Ink);
        a.Box(28, 80, 21, 26, C(29, 34, 32));
        Beam(a, 24, 70, 4, 39); Beam(a, 50, 70, 4, 39); Beam(a, 23, 69, 32, 5);
        // Deux portiques qui se perdent dans le noir donnent de la profondeur à la galerie.
        a.Line(30, 82, 30, 102, Wood.Darkened(.3f)); a.Line(45, 82, 45, 102, Wood.Darkened(.3f));
        a.Line(30, 82, 45, 82, Wood); a.Box(33, 88, 10, 2, Wood.Darkened(.4f));
        Roof(a, biome, 7, 71, 47, 69);
        a.Box(15, 81, 5, 8, Ink); a.Box(16, 83, 3, 4, Brass); a.Dot(17, 83, StoneLight);
        // Le puits est cerclé de pierre ; les pieds évasés et les boulons portent le chevalement.
        a.Oval(108, 112, 27, 12, Mortar); a.Oval(107, 109, 23, 10, Stone);
        a.Oval(107, 109, 17, 7, Soot);
        foreach (int x in new[] { 83, 126 }) Masonry(a, x - 3, 112, 13, 8, Stone, 6, 4);
        a.Polygon(Ink, new(89, 17), new(98, 17), new(90, 116), new(81, 116));
        a.Polygon(Wood, new(91, 18), new(96, 18), new(87, 114), new(84, 114));
        a.Line(91, 19, 83, 113, WoodLight);
        a.Polygon(Ink, new(120, 17), new(128, 17), new(137, 117), new(127, 117));
        a.Polygon(Wood, new(122, 19), new(126, 19), new(133, 115), new(129, 115));
        a.Line(122, 19, 129, 113, WoodLight);
        foreach (int y in new[] { 24, 55, 85 })
        {
            Beam(a, 86, y, 47, 5);
            a.Line(90, y + 5, 126, y + 29, Ink); a.Line(90, y + 4, 126, y + 28, WoodLight);
            a.Line(126, y + 5, 90, y + 29, Wood);
            foreach (int x in new[] { 89, 127 }) { a.Box(x, y, 3, 4, Mortar); a.Dot(x + 1, y + 1, StoneLight); }
        }
        Beam(a, 84, 13, 53, 7);
        a.Oval(109, 15, 11, 11, Ink); a.Oval(109, 15, 9, 9, WoodLight); a.Oval(109, 15, 6, 6, Soot);
        a.Line(102, 10, 116, 20, WoodLight); a.Line(104, 22, 114, 8, WoodLight); a.Oval(109, 15, 2, 2, Stone);
        a.Line(106, 24, 106, 97, C(197, 181, 134)); a.Line(112, 24, 112, 97, WoodLight);
        a.Box(99, 96, 23, 17, Ink); a.Box(101, 98, 19, 12, Wood);
        for (int x = 102; x < 120; x += 5) a.Line(x, 99, x, 109, WoodLight);
        a.Box(100, 102, 22, 2, Mortar); a.Box(100, 109, 22, 2, Mortar);
        // Échelle de service, tambour du treuil et atelier couvert.
        a.Line(73, 51, 73, 114, Wood); a.Line(79, 51, 79, 114, WoodLight);
        for (int y = 53; y < 113; y += 6) a.Line(73, y, 79, y, WoodLight);
        a.Box(141, 75, 42, 28, Wood.Darkened(.25f));
        for (int x = 144; x < 182; x += 5) a.Line(x, 76, x, 100, Wood);
        Beam(a, 140, 73, 4, 31); Beam(a, 179, 73, 4, 31);
        Roof(a, biome, 136, 188, 47, 72);
        a.Box(145, 91, 33, 5, WoodLight); Beam(a, 148, 96, 3, 10); Beam(a, 174, 96, 3, 10);
        a.Box(153, 85, 12, 6, Mortar); a.Line(153, 85, 164, 85, StoneLight);
        a.Line(172, 78, 172, 89, WoodLight); a.Line(168, 80, 176, 78, Stone);
        CaisseDetaillee(a, 145, 111, 17, 13); CaisseDetaillee(a, 166, 106, 16, 12);
        a.Oval(93, 128, 8, 5, Ink); a.Box(84, 121, 18, 7, Wood);
        for (int x = 85; x < 101; x += 3) a.Line(x, 121, x, 127, StoneLight);
        a.Line(101, 123, 111, 123, Ink); a.Line(111, 123, 111, 116, WoodLight);
        foreach (int x in new[] { 27, 49 })
        {
            a.Line(x, 105, x + 28, 140, Soot); a.Line(x + 1, 105, x + 29, 140, StoneLight);
        }
        for (int y = 110; y < 140; y += 6) a.Line(24 + (y - 105) * 4 / 5, y, 53 + (y - 105) * 4 / 5, y, Wood);
        a.Oval(57, 132, 20, 4, C(92, 89, 76));
        CaisseDetaillee(a, 43, 113, 29, 14);
        a.Box(44, 115, 27, 2, Mortar); a.Box(44, 123, 27, 2, Mortar);
        a.Oval(49, 128, 4, 4, Ink); a.Oval(67, 128, 4, 4, Ink);
        a.Oval(49, 128, 2, 2, Stone); a.Oval(67, 128, 2, 2, Stone);
        for (int i = 0; i < 8; i++)
        {
            int x = 141 + i % 4 * 11, y = 132 - i / 4 * 7;
            a.Polygon(Ink, new(x - 6, y + 3), new(x - 4, y - 4), new(x + 2, y - 7), new(x + 7, y - 1), new(x + 5, y + 5));
            a.Polygon(Stone.Darkened(.2f), new(x - 4, y + 1), new(x - 3, y - 4), new(x + 2, y - 5), new(x + 5, y), new(x + 1, y + 3));
            a.Line(x - 3, y - 4, x + 1, y - 5, StoneLight);
        }
        Logs(a, 112, 134); Sack(a, 14, 121);
    }

    private static void GrandBarrage(PixelArt a, bool side)
    {
        int w = a.Image.GetWidth(), h = a.Image.GetHeight();
        if (side)
        {
            // Culées allongées dans le sens de la berge, vanne et passerelle au centre du courant.
            Masonry(a, 17, 18, 33, h - 27, Stone, 9, 6);
            Masonry(a, 50, 29, 23, h - 34, Stone.Darkened(0.2f), 9, 6);
            a.Line(17, 18, 49, 18, StoneLight);
            a.Box(15, 71, 60, 33, Soot); a.Box(19, 75, 49, 24, Wood);
            for (int y = 76; y < 100; y += 5) a.Line(20, y, 67, y + 7, WoodLight);
            Beam(a, 12, 58, 5, 62); Beam(a, 74, 63, 5, 62); Beam(a, 11, 57, 68, 5);
            a.Line(45, 60, 45, 101, Brass); a.Oval(45, 59, 7, 7, WoodLight); a.Oval(45, 59, 4, 4, Soot);
            for (int y = 26; y < h - 8; y += 18) { Beam(a, 11, y, 3, 12); a.Line(12, y, 12, y + 19, WoodLight); }
        }
        else
        {
            Masonry(a, 5, 38, w - 10, 66, Stone, 10, 7);
            a.Polygon(Stone.Darkened(0.23f), new(5, 104), new(18, 82), new(w - 18, 82), new(w - 5, 104));
            foreach (int x in new[] { 48, 80, 112 })
            {
                a.Oval(x, 68, 12, 16, Ink); a.Box(x - 12, 68, 25, 30, Soot);
                a.Box(x - 9, 64, 19, 29, Wood);
                for (int y = 65; y < 91; y += 5) a.Line(x - 8, y, x + 8, y, WoodLight);
                Beam(a, x - 15, 22, 4, 69); Beam(a, x + 12, 22, 4, 69);
                a.Line(x, 27, x, 82, Brass); a.Oval(x, 22, 6, 6, WoodLight); a.Oval(x, 22, 3, 3, Soot);
            }
            Masonry(a, 5, 27, 25, 77, Stone, 10, 7); Masonry(a, w - 30, 27, 25, 77, Stone, 10, 7);
            a.Box(4, 27, 27, 3, StoneLight); a.Box(w - 31, 27, 27, 3, StoneLight);
            Beam(a, 7, 40, w - 14, 5); a.Line(9, 40, w - 10, 40, WoodLight);
            for (int x = 9; x < w - 9; x += 16) Beam(a, x, 31, 2, 14);
            a.Line(10, 32, w - 10, 32, WoodLight);
        }
    }

    private static void GrandEnclos(PixelArt a, WoodlandBiome biome, bool extension)
    {
        int w = a.Image.GetWidth(), h = a.Image.GetHeight();
        CourTexturee(a, 5, 43, w - 10, h - 47, C(130, 132, 87));
        // Des touffes et de la paille autour des équipements, en laissant le centre aux véritables animaux.
        for (int i = 0; i < w / 4; i++)
        {
            int x = 10 + i * 23 % (w - 20), y = 51 + i * 13 % (h - 64);
            a.Line(x, y, x + 3, y - 1, i % 3 == 0 ? C(183, 159, 97) : C(104, 119, 73));
            if (i % 3 != 0) a.Line(x + 1, y, x, y - 3, C(151, 158, 95));
        }
        foreach (int y in new[] { 39, 47, h - 15, h - 7 }) Beam(a, 7, y, w - 13, 3);
        for (int x = 7; x < w - 5; x += 13)
        {
            foreach (int y in new[] { 33, h - 21 })
            {
                a.Box(x + 3, y + 4, 3, 20, C(91, 92, 61));
                Beam(a, x, y, 4, 21); a.Box(x, y, 4, 2, C(207, 171, 111));
                a.Dot(x + 2, y + 8, StoneLight); a.Dot(x + 2, y + 16, Ink);
            }
        }
        foreach (int x in new[] { 5, w - 7 })
        {
            Beam(a, x, 44, 3, h - 48);
            for (int y = 54; y < h - 19; y += 16) { Beam(a, x - 1, y, 4, 12); a.Dot(x, y, WoodLight); }
        }
        if (!extension)
        {
            a.Box(10, 41, 50, 25, Wood.Darkened(.4f));
            for (int x = 13; x < 59; x += 5) a.Line(x, 42, x, 64, Wood);
            a.Box(14, 61, 43, 5, C(167, 137, 71));
            for (int x = 15; x < 55; x += 4) a.Line(x, 61, x + 3, 64, C(217, 185, 107));
            Beam(a, 10, 40, 4, 29); Beam(a, 58, 42, 4, 27); Beam(a, 10, 43, 51, 3);
            a.Line(14, 45, 22, 54, WoodLight); a.Line(58, 46, 50, 54, WoodLight);
            Roof(a, biome, 4, 69, 16, 40);
            a.Line(18, 19, 56, 23, WoodLight); a.Line(23, 22, 58, 26, RoofColor(biome).Lightened(.2f));
            // Mangeoire à barreaux et balle de paille à l'abri.
            a.Box(18, 49, 25, 12, Wood); a.Box(20, 50, 21, 9, Brass.Darkened(.2f));
            for (int x = 21; x < 43; x += 5) a.Line(x, 49, x, 62, WoodLight);
            Sack(a, 46, 55);
        }
        int troughX = extension ? 18 : 79;
        a.Oval(troughX + 18, 69, 19, 4, C(101, 105, 68));
        a.Box(troughX, 55, 34, 13, Ink); a.Box(troughX + 1, 57, 32, 10, Wood);
        a.Box(troughX + 3, 57, 28, 6, Glass.Darkened(.18f));
        a.Line(troughX + 4, 58, troughX + 29, 58, C(120, 166, 160));
        a.Line(troughX + 8, 60, troughX + 17, 60, C(88, 136, 140));
        a.Box(troughX + 1, 64, 32, 2, WoodLight);
        foreach (int dx in new[] { 5, 27 }) { a.Box(troughX + dx, 55, 2, 13, Mortar); a.Dot(troughX + dx, 56, StoneLight); }
        if (extension) { CaisseDetaillee(a, 12, 38, 23, 10); Sack(a, 40, 38); }
        else { a.Line(72, 38, 72, 55, WoodLight); a.Line(68, 40, 77, 40, Mortar); }
    }

    private static void GrandMarche(PixelArt a, WoodlandBiome biome)
    {
        int w = a.Image.GetWidth(), h = a.Image.GetHeight();
        CourTexturee(a, 4, 43, w - 8, h - 47, C(158, 144, 111));
        // Petites dalles disjointes, allée usée et étals de métiers différents.
        for (int y = 57; y < h - 8; y += 11)
        for (int x = 10 + y % 3 * 3; x < w - 12; x += 17)
        {
            a.Box(x, y, 10, 4, C(132, 129, 108));
            a.Line(x + 1, y, x + 8, y, C(189, 176, 142));
        }
        EtalDetaille(a, 8, 18, C(157, 80, 57), 0);
        if (w > 64) EtalDetaille(a, 75, 23, C(87, 119, 109), 1);
        EtalDetaille(a, w == 64 ? 8 : 40, 64, RoofColor(biome).Darkened(.1f), 2);
        if (w > 64)
        {
            CaisseDetaillee(a, 9, 81, 18, 14); Sack(a, 27, 85);
            CaisseDetaillee(a, 100, 85, 15, 13); Sack(a, 109, 70);
            // Une balance, une enseigne de bois et un rouleau de toile donnent leur fonction aux stands.
            Beam(a, 65, 34, 3, 28); a.Box(57, 38, 15, 10, Ink); a.Box(58, 39, 13, 8, WoodLight);
            a.Line(61, 42, 69, 42, Wood); a.Line(61, 44, 67, 44, Wood);
        }
    }
}
