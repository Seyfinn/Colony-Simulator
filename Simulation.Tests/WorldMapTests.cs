using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
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

    [Theory]
    [InlineData(12345)]
    [InlineData(7)]
    [InlineData(2026)]
    [InlineData(1)]
    [InlineData(99)]
    public void Quelques_grands_fleuves_traversent_le_continent_et_les_rivieres_sont_rares(int seed)
    {
        WorldGrid grid = WorldGenerator.Generate(seed);
        var land = grid.Tiles.Where(t => !t.IsOcean).ToList();
        var rivers = land.Where(t => t.River > 0).ToList();
        int mouths = rivers.Count(t => grid[t.FlowsTo].IsOcean);
        int longest = rivers.Max(t => CourseLength(grid, t));
        output.WriteLine($"graine {seed} : {rivers.Count} cases de rivière sur {land.Count} ({rivers.Count / (float)land.Count:P1}), " +
                         $"{rivers.Count(t => t.River == 2)} de grand fleuve, {mouths} embouchures, plus long cours {longest} cases");

        // Rares : quelques pour cent des terres, et seulement une poignée de fleuves qui se jettent à la mer.
        Assert.InRange(rivers.Count / (float)land.Count, 0.02f, 0.09f);
        Assert.InRange(mouths, 1, 6);
        Assert.Contains(rivers, t => t.River == 2);
        // Longues : un fleuve va de la montagne à la mer en traversant bien des cases, au lieu de naître et de mourir sur place.
        Assert.True(longest >= 18, $"Le plus long fleuve ne compte que {longest} cases.");
        // Pas de ruisseau isolé : toute case de rivière a une rivière en amont ou en aval, jusqu'à la mer.
        Assert.All(rivers, t => Assert.True(grid[t.FlowsTo].River > 0 || grid[t.FlowsTo].IsOcean));
    }

    private static int CourseLength(WorldGrid grid, WorldTile start)
    {
        int length = 0;
        for (int at = start.Index; !grid[at].IsOcean && length <= grid.Tiles.Length; at = grid[at].FlowsTo)
            length++;
        return length;
    }

    [Fact]
    public void Une_region_sans_fleuve_n_a_ni_riviere_ni_ruisseau_et_une_colonie_y_vit_sans_eau()
    {
        var world = new WorldState(12345, colonyCount: 0, migration: false, lifecycle: false);
        WorldGrid grid = world.WorldMap.Grid;
        // Même dans une région humide : la pluie ne fait plus naître de ruisseau à elle seule.
        WorldTile dry = grid.Tiles.First(t => t.Habitable && t.River == 0 && t.Rainfall > 0.5f && !t.Coastal
            && t.Biome is Biome.Grassland or Biome.TemperateForest);
        Assert.Equal(0, MapStyle.For(dry).RiverCount);
        LocalMap map = world.GenerateColonyMap(dry.Index);
        Assert.DoesNotContain(Enumerable.Range(0, map.Width * map.Height), i => map.IsRiver(i % map.Width, i / map.Width));

        // Sans eau, sans pêche ni moulin, la colonie cueille, sème et vit : personne ne meurt de faim en un an.
        (int x, int y) = ColonyFounder.FindCampSite(map);
        Assert.True(world.TryFoundColony(map, x, y, "Terre sèche", Species.Human, 8, dry.Index, out Colony? colony, out string reason), reason);
        Assert.Empty(WorkSites.FishingSpots(map, colony!));
        var watch = new StarvationWatch();
        for (long i = 0; i < TimeConstants.TicksPerYear; i++)
        {
            world.Step();
            if (i % TimeConstants.TicksPerDay == 0)
                watch.Observe(colony!);
        }
        output.WriteLine($"Colonie sans eau : {colony!.Members.Count} colons, {colony.Stock.FoodUnits} unités de nourriture, {colony.Deaths.Count} tombes");
        Assert.Null(watch.Victim);
        Assert.DoesNotContain(colony.Deaths, g => g.Cause == "faim");
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
