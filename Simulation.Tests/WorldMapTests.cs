using GodColony.Simulation.Colonies;
using GodColony.Simulation.World;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class WorldMapTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(12345)]
    [InlineData(7)]
    [InlineData(2026)]
    public void Le_monde_a_des_oceans_des_biomes_varies_et_des_fleuves(int seed)
    {
        WorldGrid grid = WorldGenerator.Generate(seed);
        output.WriteLine(Ascii(grid));
        var land = grid.Tiles.Where(t => !t.IsOcean).ToList();
        Assert.InRange(land.Count / (float)grid.Tiles.Length, 0.5f, 0.7f);
        // Les bords du monde sont de l'océan.
        Assert.All(grid.Tiles.Where(t => t.Row == 0 || t.Col == 0), t => Assert.True(t.IsOcean));
        var biomes = land.GroupBy(t => t.Biome).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine(string.Join(", ", biomes.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
        Assert.True(biomes.Count >= 7, "au moins sept biomes différents");
        Assert.Contains(land, t => t.River == 2);
        // Toute l'eau finit dans l'océan.
        foreach (WorldTile tile in land.Where(t => t.River > 0))
        {
            int steps = 0, at = tile.Index;
            while (!grid[at].IsOcean && steps++ < grid.Tiles.Length)
                at = grid[at].FlowsTo;
            Assert.True(grid[at].IsOcean);
        }
    }

    [Fact]
    public void Les_peuples_de_depart_s_installent_chez_eux_et_peuvent_commercer()
    {
        var world = new WorldState(12345, colonyCount: 4, migration: false, lifecycle: false);
        WorldMap map = world.WorldMap;
        foreach (Colony colony in world.Colonies)
        {
            WorldTile tile = map.Grid[map.TileOf(colony)];
            output.WriteLine($"{colony.Name} : {tile.Describe()}, rivière {tile.River}, {tile.Temperature:0} °C");
            Assert.True(tile.Habitable);
            Assert.Equal(tile.Biome, colony.Map.Biome);
        }
        Colony humans = world.Colonies[0];
        foreach (Colony other in world.Colonies.Skip(1))
        {
            Assert.True(map.Connected(humans, other));
            Assert.InRange(map.TravelDays(humans, other), 0.3f, 4f);
            Assert.InRange(map.Grid.Distance(map.TileOf(humans), map.TileOf(other)), 3, 12);
        }
    }

    [Fact]
    public void Une_meme_case_donne_toujours_la_meme_region_et_le_biome_change_le_terrain()
    {
        var world = new WorldState(12345, colonyCount: 0);
        WorldGrid grid = world.WorldMap.Grid;
        WorldTile forest = grid.Tiles.First(t => t.Habitable && t.Biome == Biome.TemperateForest);
        WorldTile dry = grid.Tiles.First(t => t.Habitable && t.Biome is Biome.Desert or Biome.Steppe or Biome.Tundra);
        var a = world.GenerateColonyMap(forest.Index);
        var b = world.GenerateColonyMap(forest.Index);
        Assert.Equal(Trees(a), Trees(b));
        Assert.True(Trees(a) > 2 * Trees(world.GenerateColonyMap(dry.Index)));
    }

    private static int Trees(GodColony.Simulation.Map.LocalMap map) =>
        Enumerable.Range(0, map.Width * map.Height).Count(i => map.GetFlora(i % map.Width, i / map.Width) == GodColony.Simulation.Map.FloraType.Tree);

    private static string Ascii(WorldGrid grid)
    {
        var text = new System.Text.StringBuilder();
        for (int row = 0; row < grid.Height; row++)
        {
            if ((row & 1) == 1) text.Append(' ');
            for (int col = 0; col < grid.Width; col++)
            {
                WorldTile t = grid[grid.IndexOf(col, row)];
                char c = t.Biome switch
                {
                    Biome.Ocean => '~', Biome.IceSheet => '#', Biome.Tundra => 't', Biome.BorealForest => 'b',
                    Biome.TemperateForest => 'F', Biome.Grassland => '.', Biome.Steppe => ',', Biome.Desert => 'd',
                    Biome.Savanna => 's', Biome.TropicalForest => 'J', Biome.Swamp => 'm', _ => '?',
                };
                if (t.Relief == Relief.Impassable) c = 'A';
                else if (t.Relief == Relief.Mountains) c = '^';
                if (t.River == 2 && !t.IsOcean) c = '=';
                text.Append(c).Append(' ');
            }
            text.AppendLine();
        }
        return text.ToString();
    }
}
