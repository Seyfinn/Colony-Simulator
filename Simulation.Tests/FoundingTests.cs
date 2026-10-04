using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public class FoundingTests
{
    [Fact]
    public void Un_monde_vierge_peut_avancer_puis_accueillir_sa_premiere_colonie()
    {
        var world = new WorldState(12345, colonyCount: 0, migration: false, lifecycle: false);
        Assert.Empty(world.Colonies);
        long start = world.Clock.Ticks;
        world.Step();
        Assert.Equal(start + 1, world.Clock.Ticks);

        LocalMap map = world.GenerateColonyMap(Species.Elf);
        (int x, int y) = ColonyFounder.FindCampSite(map);
        Assert.True(world.TryFoundColony(map, x, y, "  Clairerive  ", Species.Elf, 12, -4, 3,
            out Colony? colony, out string reason), reason);
        Assert.Same(colony, Assert.Single(world.Colonies));
        Assert.Same(map, world.Map);
        Assert.Equal("Clairerive", colony!.Name);
        Assert.Equal((x, y), (colony.CampX, colony.CampY));
        Assert.Equal((-4f, 3f), world.WorldMap.PositionOf(colony));
        Assert.Equal(48, colony.Stock.Get(ResourceType.Food));
        Assert.Equal(ColonyFounder.StartingCoins, colony.Stock.Get(ResourceType.Coins));
        Assert.Equal(12, colony.Members.Count);
        Assert.All(colony.Members, c =>
        {
            Assert.Equal(Species.Elf, c.Species);
            Assert.Same(colony, c.Colony);
            Assert.True(map.IsWalkable(c.TileX, c.TileY));
        });
        for (int i = 0; i < TimeConstants.TicksPerHour; i++) world.Step();
        Assert.NotEmpty(colony.Thoughts);
    }

    [Fact]
    public void La_fondation_en_partie_conserve_les_positions_et_les_identifiants()
    {
        var world = new WorldState(12345, colonyCount: 2, migration: false, lifecycle: false);
        var originalPositions = world.Colonies.Select(world.WorldMap.PositionOf).ToArray();
        var oldIds = world.Colonies.SelectMany(c => c.Members).Select(c => c.Id).ToHashSet();
        Colony upstream = world.Colonies[^1];
        LocalMap map = world.GenerateColonyMap(Species.Orc);
        (int x, int y) = ColonyFounder.FindCampSite(map);
        long ticks = world.Clock.Ticks;
        Assert.True(world.TryFoundColony(map, x, y, "Nouvelle steppe", Species.Orc, 8, 0, -9,
            out Colony? colony, out string reason), reason);
        Assert.Equal(ticks, world.Clock.Ticks);
        Assert.Equal(originalPositions, world.Colonies.Take(2).Select(world.WorldMap.PositionOf));
        Assert.Same(colony, upstream.Downstream);
        Assert.NotSame(world.Map, colony!.Map);
        Assert.All(colony.Members, c => Assert.DoesNotContain(c.Id, oldIds));
        Assert.True(world.WorldMap.TravelDays(world.Colonies[0], colony) > 0);
        for (int i = 0; i < TimeConstants.TicksPerDay; i++) world.Step();
    }

    [Theory]
    [InlineData("", 8, 0, 0)]
    [InlineData("Test", 4, 0, 0)]
    [InlineData("Test", 21, 0, 0)]
    [InlineData("Test", 8, 13, 0)]
    [InlineData("Première colonie", 8, 0, -9)]
    public void Une_demande_invalide_ne_modifie_pas_le_monde(string name, int founders, float wx, float wy)
    {
        var world = new WorldState(12345);
        LocalMap map = world.GenerateColonyMap(Species.Dwarf);
        (int x, int y) = ColonyFounder.FindCampSite(map);
        int count = world.Colonies.Count;
        Assert.False(world.TryFoundColony(map, x, y, name, Species.Dwarf, founders, wx, wy,
            out Colony? colony, out string reason));
        Assert.Null(colony);
        Assert.NotEmpty(reason);
        Assert.Equal(count, world.Colonies.Count);
        Assert.Null(world.Colonies[^1].Downstream);
    }

    [Fact]
    public void Le_terrain_et_les_regions_occupees_sont_refuses_sans_consommer_le_hasard()
    {
        var world = new WorldState(12345);
        var control = new WorldState(12345);
        LocalMap map = world.GenerateColonyMap(Species.Human);
        (int x, int y) = ColonyFounder.FindCampSite(map);
        Assert.False(world.TryFoundColony(map, 0, 0, "Bord", Species.Human, 8, 0, -9, out _, out _));
        var occupied = world.WorldMap.PositionOf(world.Colonies[0]);
        Assert.False(world.TryFoundColony(map, x, y, "Chevauchement", Species.Human, 8, occupied.X, occupied.Y, out _, out _));
        Assert.False(world.TryFoundColony(world.Map, x, y, "Même carte", Species.Human, 8, 0, -9, out _, out _));
        Assert.False(world.TryFoundColony(map, x, y, "Position infinie", Species.Human, 8, float.NaN, 0, out _, out _));
        Assert.Equal(control.Random.Next(), world.Random.Next());
        Assert.Single(world.Colonies);
    }

    [Fact]
    public void Un_camp_ne_peut_pas_etre_place_dans_l_eau_ou_sur_une_falaise()
    {
        LocalMap map = MapGenerator.Generate(128, 128, 12345);
        var water = Enumerable.Range(0, map.Width * map.Height)
            .Select(i => (X: i % map.Width, Y: i / map.Width)).First(p => map.IsWater(p.X, p.Y));
        Assert.False(ColonyFounder.CanFoundAt(map, water.X, water.Y, out _));
        var mountain = Enumerable.Range(0, map.Width * map.Height)
            .Select(i => (X: i % map.Width, Y: i / map.Width)).First(p => map.IsMountain(p.X, p.Y));
        Assert.False(ColonyFounder.CanFoundAt(map, mountain.X, mountain.Y, out _));
    }
}
