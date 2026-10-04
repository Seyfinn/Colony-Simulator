using Godot;

namespace GodColony.View;

/// <summary>Les bâtiments de la vie du village : enclos, métier à tisser, marché, infirmerie, entrepôt, puits, taverne, école.</summary>
public static partial class BuildingSprites
{
    internal static PixelArt VillageSource(string kind)
    {
        var a = new PixelArt(64, 80);
        var biome = WoodlandBiome.TemperatePlain;
        switch (kind)
        {
            case "Pen": Pen(a, biome); break;
            case "Loom": Loom(a, biome); break;
            case "Market": Market(a, biome); break;
            case "Infirmary": Infirmary(a, biome); break;
            case "Storehouse": Storehouse(a, biome); break;
            case "Well": Well(a, biome); break;
            case "Tavern": Tavern(a, biome); break;
            case "School": School(a, biome); break;
        }
        // Patine commune : pavés, ombre des avant-toits et petits détails du métier.
        if (kind != "Pen" && kind != "Well")
        {
            for (int x = 9; x < 57; x += 7) { a.Box(x, 76, 5, 2, StoneLight); a.Dot(x + 5, 77, Mortar); }
            a.Line(9, 45, 22, 45, WoodLight);
        }
        if (kind == "Market") { a.Box(19, 53, 1, 6, Brass); a.Oval(19, 53, 3, 2, StoneLight); }
        if (kind == "Tavern") { a.Box(23, 63, 14, 2, WoodLight); a.Box(25, 65, 2, 5, Wood); a.Box(34, 65, 2, 5, Wood); }
        if (kind == "School") { a.Box(8, 66, 6, 5, C(115, 132, 113)); a.Box(9, 65, 4, 1, CreamColor); }
        return a;
    }
    private static Color CreamColor => C(245, 233, 201);

    private static void Pen(PixelArt a, WoodlandBiome biome)
    {
        // Enclos vide : seules les données d'élevage autorisent l'affichage d'une bête.
        a.Oval(32, 60, 28, 14, C(138, 112, 72));
        a.Oval(32, 59, 25, 11, C(160, 136, 88));
        for (int x = 9; x < 56; x += 4) { a.Dot(x, 56 + x * 7 % 5, C(116, 134, 76)); a.Dot(x + 2, 64 - x % 4, C(116, 134, 76)); }
        // Clôture : piquets et deux lisses, plus basse côté spectateur.
        foreach (int x in new[] { 5, 15, 25, 35, 45, 55 })
        {
            a.Box(x, 48, 2, 24, Ink); a.Box(x, 48, 1, 23, WoodLight);
        }
        a.Line(5, 53, 56, 53, Wood); a.Line(5, 59, 56, 59, Wood); a.Line(5, 54, 56, 54, WoodLight);
        a.Line(5, 66, 56, 66, Wood); a.Line(5, 67, 56, 67, WoodLight);
        // Auge et petit abri de paille.
        a.Box(40, 70, 14, 4, Wood); a.Box(41, 70, 12, 1, C(204, 176, 98));
        a.Polygon(C(204, 176, 98), new(5, 46), new(12, 36), new(24, 36), new(30, 46));
        a.Line(5, 46, 30, 46, Wood);
    }

    private static void Loom(PixelArt a, WoodlandBiome biome)
    {
        Foundation(a);
        Beam(a, 7, 40, 3, 32); Beam(a, 52, 40, 3, 32);
        Roof(a, biome == WoodlandBiome.TemperatePlain ? WoodlandBiome.WetBank : biome, 2, 60, 14, 40);
        a.Box(10, 44, 42, 26, C(209, 190, 150).Darkened(0.2f));
        // Le métier : deux montants, un cadre de fils tendus, une navette et de la laine cardée.
        Beam(a, 17, 46, 3, 26); Beam(a, 41, 46, 3, 26); Beam(a, 16, 46, 29, 3); Beam(a, 16, 62, 29, 3);
        for (int x = 21; x < 41; x += 2) a.Line(x, 49, x, 61, C(236, 225, 196));
        a.Box(21, 55, 20, 2, C(176, 74, 62)); a.Box(21, 52, 20, 1, C(86, 120, 126));
        a.Oval(31, 58, 5, 1, WoodLight);
        a.Box(23, 66, 15, 4, Wood); a.Box(24, 66, 13, 1, WoodLight);
        // Écheveaux et balles de laine.
        a.Oval(10, 70, 4, 3, C(238, 232, 216)); a.Oval(9, 69, 2, 2, C(252, 249, 240));
        a.Oval(52, 71, 4, 3, C(176, 74, 62)); a.Oval(51, 70, 2, 2, C(214, 128, 96));
        a.Box(55, 62, 4, 9, Wood); a.Line(55, 62, 58, 66, StoneLight);
    }

    private static void Market(PixelArt a, WoodlandBiome biome)
    {
        Foundation(a, 3, 58);
        // Deux étals sous auvent rayé, caisses de fruits, sacs et balance.
        foreach (int x in new[] { 6, 34 })
        {
            Beam(a, x, 40, 3, 32); Beam(a, x + 20, 40, 3, 32);
            a.Polygon(Ink, new(x - 3, 44), new(x + 3, 34), new(x + 21, 34), new(x + 27, 44));
            for (int i = 0; i < 6; i++)
                a.Polygon(i % 2 == 0 ? C(193, 78, 66) : C(238, 224, 190), new(x - 2 + i * 5, 43), new(x + 3 + i * 3, 35), new(x + 6 + i * 3, 35), new(x + 3 + i * 5, 43));
            a.Box(x + 1, 62, 22, 8, Wood); a.Box(x, 61, 24, 2, WoodLight);
        }
        foreach (int x in new[] { 9, 14, 37, 43 })
        {
            a.Box(x, 57, 4, 4, C(204, 76, 58)); a.Dot(x, 57, C(246, 147, 108));
        }
        foreach (int x in new[] { 18, 49 }) a.Oval(x, 58, 3, 3, C(214, 170, 74));
        Sack(a, 27, 60);
        a.Line(30, 44, 30, 52, Brass); a.Line(26, 52, 34, 52, Brass); a.Oval(26, 54, 2, 1, Brass); a.Oval(34, 54, 2, 1, Brass);
        a.Box(2, 72, 12, 4, Wood); a.Box(3, 72, 10, 1, WoodLight);
    }

    private static void Infirmary(PixelArt a, WoodlandBiome biome)
    {
        // Une chaumière claire, marquée d'une croix rouge et bordée d'un petit jardin d'herbes.
        Cottage(a, biome);
        a.Box(29, 31, 13, 11, C(245, 240, 224)); a.Box(29, 31, 13, 1, Ink); a.Box(29, 41, 13, 1, Ink);
        a.Box(34, 33, 3, 7, C(196, 52, 52)); a.Box(32, 35, 7, 3, C(196, 52, 52));
        a.Box(52, 64, 10, 8, Wood); a.Box(53, 64, 8, 2, C(86, 129, 77));
        foreach (int x in new[] { 54, 57, 60 }) { a.Dot(x, 63, C(108, 160, 92)); a.Dot(x, 62, C(222, 206, 120)); }
    }

    private static void Storehouse(PixelArt a, WoodlandBiome biome)
    {
        // Un long hangar de planches, à grand portail double, sous un toit bas.
        Foundation(a, 3, 58);
        a.Box(5, 38, 55, 33, Wood.Lightened(0.05f));
        for (int x = 6; x < 60; x += 4) a.Box(x, 38, 1, 33, Wood.Darkened(0.25f));
        Roof(a, biome, 0, 62, 14, 38);
        a.Box(17, 46, 30, 26, Wood.Darkened(0.35f)); a.Box(18, 47, 13, 25, Wood); a.Box(33, 47, 13, 25, Wood);
        a.Line(18, 48, 30, 71, WoodLight); a.Line(30, 48, 18, 71, WoodLight); a.Line(34, 48, 45, 71, WoodLight); a.Line(45, 48, 34, 71, WoodLight);
        a.Box(31, 58, 2, 4, Brass);
        a.Box(25, 40, 14, 5, Soot); a.Box(26, 41, 12, 3, C(116, 92, 52));
        Sack(a, 6, 63); Sack(a, 49, 62); a.Box(53, 66, 7, 6, Wood); a.Box(54, 66, 5, 1, WoodLight);
        a.Oval(9, 56, 3, 3, C(214, 170, 74));
    }

    private static void Well(PixelArt a, WoodlandBiome biome)
    {
        // Margelle de pierre, deux montants, un petit toit, une poulie et un seau.
        a.Oval(32, 66, 18, 8, Mortar);
        a.Oval(32, 64, 16, 7, Stone);
        a.Oval(32, 62, 11, 4, Soot); a.Oval(32, 63, 8, 2, C(66, 118, 124));
        Masonry(a, 16, 62, 32, 9, Stone, 6, 4);
        a.Oval(32, 62, 16, 5, StoneLight); a.Oval(32, 62, 11, 3, Soot); a.Oval(32, 63, 7, 2, C(66, 118, 124));
        Beam(a, 17, 34, 3, 32); Beam(a, 44, 34, 3, 32);
        a.Polygon(Ink, new(11, 36), new(32, 21), new(53, 36));
        a.Polygon(RoofColor(biome), new(14, 35), new(32, 24), new(50, 35));
        a.Line(14, 35, 50, 35, WoodLight);
        Beam(a, 18, 38, 28, 3);
        a.Line(32, 41, 32, 56, WoodLight); a.Box(29, 55, 6, 5, Wood); a.Box(30, 55, 4, 1, WoodLight);
        a.Oval(32, 40, 3, 3, Ink); a.Oval(32, 40, 2, 2, Brass);
    }

    private static void Tavern(PixelArt a, WoodlandBiome biome)
    {
        // Une grande maison aux fenêtres chaudes, avec une enseigne à chope suspendue.
        Cottage(a, biome);
        a.Box(12, 51, 9, 10, C(238, 188, 92)); a.Box(13, 52, 7, 8, C(250, 214, 128));
        a.Line(48, 46, 58, 46, Wood); a.Box(55, 46, 1, 4, Ink);
        a.Box(52, 50, 8, 8, Wood); a.Box(53, 51, 6, 6, C(233, 215, 160));
        a.Box(54, 52, 3, 4, C(214, 150, 62)); a.Box(57, 53, 2, 2, C(214, 150, 62)); a.Box(54, 52, 3, 1, C(252, 244, 220));
        a.Box(6, 66, 6, 8, Wood); a.Box(7, 66, 4, 1, WoodLight); a.Line(6, 69, 11, 69, Ink);
    }

    private static void School(PixelArt a, WoodlandBiome biome)
    {
        // Une chaumière coiffée d'un petit clocher, avec une ardoise devant la porte.
        Cottage(a, biome);
        Beam(a, 28, 12, 3, 14); Beam(a, 40, 12, 3, 14);
        a.Polygon(Ink, new(25, 13), new(35, 3), new(46, 13));
        a.Polygon(RoofColor(biome), new(27, 12), new(35, 5), new(44, 12));
        a.Oval(35, 20, 3, 4, Brass); a.Box(34, 22, 3, 3, Brass.Darkened(0.3f));
        a.Box(53, 62, 9, 9, Wood); a.Box(54, 63, 7, 6, Soot);
        a.Line(55, 65, 59, 65, C(240, 236, 220)); a.Line(55, 67, 58, 67, C(240, 236, 220));
        a.Line(52, 71, 52, 75, WoodLight); a.Line(62, 71, 62, 75, WoodLight);
    }
}
