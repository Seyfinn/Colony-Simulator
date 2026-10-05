using Godot;

namespace GodColony.View;

public static partial class BuildingSprites
{
    private static void MineDepot(PixelArt a, WoodlandBiome biome)
    {
        CourTexturee(a, 3, 46, 57, 31, C(130, 122, 100));
        a.Polygon(Mortar, new(3, 48), new(10, 26), new(23, 18), new(39, 33), new(40, 65), new(5, 68));
        a.Polygon(Stone, new(6, 43), new(12, 28), new(23, 21), new(27, 39), new(18, 52));
        Masonry(a, 7, 43, 10, 25, Stone, 6, 4); Masonry(a, 30, 41, 9, 27, Stone, 6, 4);
        a.Oval(24, 47, 9, 11, Ink); a.Box(16, 47, 16, 21, Soot);
        Beam(a, 15, 43, 3, 25); Beam(a, 30, 43, 3, 25); Beam(a, 15, 42, 18, 3);
        Roof(a, biome, 3, 43, 27, 42);
        Beam(a, 43, 18, 3, 51); Beam(a, 56, 18, 3, 51); Beam(a, 41, 18, 20, 3);
        a.Line(46, 28, 55, 54, WoodLight); a.Line(55, 28, 46, 54, Wood);
        a.Oval(51, 19, 6, 6, Ink); a.Oval(51, 19, 4, 4, WoodLight); a.Oval(51, 19, 2, 2, Soot);
        a.Line(50, 25, 50, 61, Brass); CaisseDetaillee(a, 46, 61, 11, 9);
        a.Line(19, 62, 26, 78, StoneLight); a.Line(28, 62, 37, 78, StoneLight);
        for (int y = 65; y < 78; y += 4) a.Line(18 + (y - 62) / 2, y, 29 + (y - 62) / 2, y, Wood);
        CaisseDetaillee(a, 20, 67, 15, 8); a.Oval(23, 76, 2, 2, Ink); a.Oval(33, 76, 2, 2, Ink);
    }
    private static void PotteryKiln(PixelArt a, WoodlandBiome biome)
    {
        Kiln(a, biome);
        a.Box(6, 57, 17, 13, Ink); a.Box(7, 58, 15, 11, C(174, 94, 62));
        a.Box(10, 55, 9, 3, C(218, 150, 102)); a.Box(12, 57, 5, 2, Soot);
        a.Box(37, 60, 11, 9, C(185, 116, 71)); a.Box(40, 58, 5, 3, Ink);
    }
    private static void Tannery(PixelArt a, WoodlandBiome biome)
    {
        Cottage(a, biome);
        Beam(a, 7, 46, 3, 25); Beam(a, 25, 46, 3, 25); Beam(a, 7, 46, 21, 3);
        a.Polygon(C(189, 143, 84), new(10, 50), new(17, 53), new(24, 50), new(23, 64), new(17, 68), new(11, 64));
        a.Box(36, 61, 18, 9, Ink); a.Box(37, 62, 16, 6, C(98, 80, 62)); a.Box(38, 63, 14, 3, Glass);
    }
    private static void Goldsmith(PixelArt a, WoodlandBiome biome)
    {
        Forge(a, biome);
        a.Box(9, 55, 19, 4, Ink); a.Box(10, 55, 17, 2, WoodLight);
        Beam(a, 10, 59, 3, 11); Beam(a, 24, 59, 3, 11);
        a.Box(14, 51, 8, 4, Brass); a.Box(15, 51, 6, 1, C(252, 228, 158));
        a.Box(23, 52, 3, 3, C(187, 56, 75));
    }
}
