using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class LocalMapTests(ITestOutputHelper output)
{
    private const int Size = 160;

    [Fact]
    public void La_meme_graine_donne_la_meme_carte()
    {
        var a = MapGenerator.Generate(Size, Size, 42);
        var b = MapGenerator.Generate(Size, Size, 42);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            Assert.Equal(a.GetElevation(x, y), b.GetElevation(x, y));
            Assert.Equal(a.GetSurface(x, y), b.GetSurface(x, y));
            Assert.Equal(a.GetFlora(x, y), b.GetFlora(x, y));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(12345)]
    public void La_carte_contient_eau_plaines_montagnes_forets_et_fer(int seed)
    {
        var map = MapGenerator.Generate(Size, Size, seed);
        int water = 0, mountain = 0, trees = 0, ironVisible = 0, ironHidden = 0, rockLayers = 0;

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            if (map.IsWater(x, y)) water++;
            if (map.IsMountain(x, y))
            {
                mountain++;
                if (map.GetSurface(x, y) == Surface.IronOre) ironVisible++;
                for (int level = LocalMap.MinMiningElevation; level < map.GetElevation(x, y); level++)
                {
                    rockLayers++;
                    if (map.MaterialAt(x, y, level) == Material.IronOre) ironHidden++;
                }
            }
            if (map.GetFlora(x, y) == FloraType.Tree) trees++;
        }

        int total = Size * Size;
        output.WriteLine($"graine {seed} : eau {100f * water / total:F1} %, montagne {100f * mountain / total:F1} %, " +
                         $"arbres {100f * trees / total:F1} %, fer en surface {ironVisible}, fer dans la roche {100f * ironHidden / rockLayers:F1} %");

        Assert.InRange(water / (float)total, 0.05f, 0.12f);
        Assert.InRange(mountain / (float)total, 0.18f, 0.26f);
        Assert.True(trees > total * 0.08, "Il devrait y avoir des forêts.");
        Assert.True(ironVisible > 0, "Un peu de fer devrait affleurer en surface.");
        Assert.InRange(ironHidden / (float)rockLayers, 0.04f, 0.25f);
    }

    [Fact]
    public void Miner_retire_une_couche_et_rend_sa_matiere()
    {
        var map = MapGenerator.Generate(Size, Size, 42);
        (int x, int y) = FindMineableTile(map);
        int before = map.GetElevation(x, y);
        Material expected = map.TopMaterial(x, y);
        int changes = 0;
        map.TileChanged += (_, _) => changes++;

        Material extracted = map.Mine(x, y);

        Assert.Equal(expected, extracted);
        Assert.Equal(before - 1, map.GetElevation(x, y));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void On_ne_peut_pas_miner_la_terre_ni_creuser_sous_la_nappe()
    {
        var map = MapGenerator.Generate(Size, Size, 42);
        (int x, int y) = FindMineableTile(map);
        while (map.CanMine(x, y))
            map.Mine(x, y);

        Assert.Equal(LocalMap.MinMiningElevation, map.GetElevation(x, y));

        for (int py = 0; py < Size; py++)
        for (int px = 0; px < Size; px++)
            if (!map.IsMountain(px, py) && !map.IsWater(px, py))
            {
                Assert.False(map.CanMine(px, py));
                return;
            }
    }

    private static (int, int) FindMineableTile(LocalMap map)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.CanMine(x, y))
                return (x, y);
        throw new InvalidOperationException("Aucune case minable.");
    }
}
