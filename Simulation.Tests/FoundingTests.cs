using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public class FoundingTests
{
    [Fact]
    public void Le_tirage_aleatoire_de_fondateurs_reste_entre_5_et_15_et_couvre_toute_la_plage()
    {
        var random = new Random(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 2000; i++)
        {
            int count = ColonyFounder.RandomFounderCount(random);
            Assert.InRange(count, 5, 15);
            seen.Add(count);
        }
        Assert.Equal(11, seen.Count);
    }

    [Fact]
    public void Un_monde_vierge_peut_avancer_puis_accueillir_sa_premiere_colonie()
    {
        var world = new WorldState(12345, colonyCount: 0, migration: false, lifecycle: false);
        Assert.Empty(world.Colonies);
        long start = world.Clock.Ticks;
        world.Step();
        Assert.Equal(start + 1, world.Clock.Ticks);

        int tile = world.WorldMap.SuggestTile(Species.Elf);
        LocalMap map = world.GenerateColonyMap(tile);
        (int x, int y) = ColonyFounder.FindCampSite(map);
        Assert.True(world.TryFoundColony(map, x, y, "  Clairerive  ", Species.Elf, 12, tile,
            out Colony? colony, out string reason), reason);
        Assert.Same(colony, Assert.Single(world.Colonies));
        Assert.Same(map, world.Map);
        Assert.Equal("Clairerive", colony!.Name);
        Assert.Equal((x, y), (colony.CampX, colony.CampY));
        Assert.Equal(tile, world.WorldMap.TileOf(colony));
        Assert.Equal(world.WorldMap.Grid[tile].Biome, map.Biome);
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
        var originalTiles = world.Colonies.Select(world.WorldMap.TileOf).ToArray();
        var oldIds = world.Colonies.SelectMany(c => c.Members).Select(c => c.Id).ToHashSet();
        int tile = world.WorldMap.SuggestTile(Species.Orc);
        LocalMap map = world.GenerateColonyMap(tile);
        (int x, int y) = ColonyFounder.FindCampSite(map);
        long ticks = world.Clock.Ticks;
        Assert.True(world.TryFoundColony(map, x, y, "Nouvelle steppe", Species.Orc, 8, tile,
            out Colony? colony, out string reason), reason);
        Assert.Equal(ticks, world.Clock.Ticks);
        Assert.Equal(originalTiles, world.Colonies.Take(2).Select(world.WorldMap.TileOf));
        Assert.NotSame(world.Map, colony!.Map);
        Assert.All(colony.Members, c => Assert.DoesNotContain(c.Id, oldIds));
        Assert.True(world.WorldMap.TravelDays(world.Colonies[0], colony) > 0);
        for (int i = 0; i < TimeConstants.TicksPerDay; i++) world.Step();
    }

    [Theory]
    [InlineData("", 8, false)]
    [InlineData("Test", 4, false)]
    [InlineData("Test", 21, false)]
    [InlineData("Test", 8, true)]
    [InlineData("Première colonie", 8, false)]
    public void Une_demande_invalide_ne_modifie_pas_le_monde(string name, int founders, bool inTheSea)
    {
        var world = new WorldState(12345);
        int tile = inTheSea ? 0 : world.WorldMap.SuggestTile(Species.Dwarf);
        LocalMap map = world.GenerateColonyMap(world.WorldMap.SuggestTile(Species.Dwarf));
        (int x, int y) = ColonyFounder.FindCampSite(map);
        int count = world.Colonies.Count;
        Assert.False(world.TryFoundColony(map, x, y, name, Species.Dwarf, founders, tile,
            out Colony? colony, out string reason));
        Assert.Null(colony);
        Assert.NotEmpty(reason);
        Assert.Equal(count, world.Colonies.Count);
    }

    [Fact]
    public void Le_terrain_et_les_regions_occupees_sont_refuses_sans_consommer_le_hasard()
    {
        var world = new WorldState(12345);
        var control = new WorldState(12345);
        int tile = world.WorldMap.SuggestTile(Species.Human);
        LocalMap map = world.GenerateColonyMap(tile);
        (int x, int y) = ColonyFounder.FindCampSite(map);
        Assert.False(world.TryFoundColony(map, 0, 0, "Bord", Species.Human, 8, tile, out _, out _));
        int occupied = world.WorldMap.TileOf(world.Colonies[0]);
        Assert.False(world.TryFoundColony(map, x, y, "Chevauchement", Species.Human, 8, occupied, out _, out _));
        Assert.False(world.TryFoundColony(world.Map, x, y, "Même carte", Species.Human, 8, tile, out _, out _));
        Assert.False(world.TryFoundColony(map, x, y, "Hors du monde", Species.Human, 8, -1, out _, out _));
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
