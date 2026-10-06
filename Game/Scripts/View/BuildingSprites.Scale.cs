using GodColony.Simulation.Colonies;

namespace GodColony.View;

public static partial class BuildingSprites
{
    internal static void Mint(PixelArt a, WoodlandBiome biome)
    {
        Foundation(a); Masonry(a, 8, 38, 48, 32, Stone); Roof(a, biome, top: 17);
        a.Box(12, 47, 15, 23, Soot); Beam(a, 10, 45, 19, 3);
        Beam(a, 35, 47, 3, 22); Beam(a, 49, 47, 3, 22); Beam(a, 34, 45, 19, 4);
        a.Box(40, 48, 5, 12, Brass); a.Box(36, 62, 15, 4, Ink);
        for (int x = 15; x < 26; x += 4) { a.Box(x, 64, 3, 2, Brass); a.Dot(x, 64, StoneLight); }
        a.Box(26, 31, 13, 9, Wood); a.Box(30, 33, 5, 5, Brass);
    }

    private static void Shrine(PixelArt a, WoodlandBiome biome) => Sanctuaire(a, biome);

    private static void Sanctuaire(PixelArt a, WoodlandBiome biome)
    {
        int w = a.Image.GetWidth(), h = a.Image.GetHeight();
        Masonry(a, 5, h - 13, w - 10, 10, Stone);
        a.Box(10, h - 20, w - 20, 7, StoneLight);
        foreach (int x in new[] { 14, w - 21 })
        {
            Masonry(a, x, 36, 7, h - 56, Stone);
            a.Box(x - 2, 34, 11, 4, StoneLight); a.Box(x - 2, h - 23, 11, 4, StoneLight);
        }
        Roof(a, biome, 3, w - 3, 10, 33);
        a.Box(w / 2 - 8, h - 32, 16, 8, Stone);
        a.Box(w / 2 - 11, h - 35, 22, 4, StoneLight);
        a.Box(w / 2 - 2, 22, 4, 7, Brass);
    }

    private static void ModuleAtelier(PixelArt a, WoodlandBiome biome, BuildingType type)
    {
        int h = a.Image.GetHeight();
        Masonry(a, 7, h - 39, 49, 33, Stone);
        Roof(a, biome, 3, 61, h - 72, h - 40);
        Beam(a, 8, h - 38, 3, 32); Beam(a, 52, h - 38, 3, 32);
        a.Box(25, h - 29, 15, 23, Soot);
        if (type == BuildingType.Oven)
        {
            Chimney(a, 42, h - 89, 29); a.Box(26, h - 15, 13, 3, Brass);
            Logs(a, 11, h - 9);
        }
        else { Sack(a, 12, h - 17); Sack(a, 43, h - 17); a.Box(26, h - 30, 13, 4, StoneLight); }
    }
}
