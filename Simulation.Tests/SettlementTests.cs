using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;

namespace GodColony.Simulation.Tests;

public sealed class SettlementTests
{
    [Fact]
    public void Les_autorites_locales_et_les_identites_survivent_a_la_reprise()
    {
        var world = new WorldState(12345, startingColonists: 10, colonyCount: 2, trade: false);
        Assert.Equal(2, world.Settlements.Count());
        Assert.NotEqual(world.Colonies[0].Id, world.Colonies[1].Id);
        foreach (Colony colony in world.Colonies)
        {
            Settlement settlement = Assert.Single(colony.Settlements);
            Assert.Same(settlement.Stock, colony.Stock); Assert.Same(settlement.Buildings, colony.Buildings);
            Assert.Same(settlement.Map, world.Regions[settlement.RegionTileIndex].Map);
            Assert.Equal(colony.Members.Count, settlement.PresentColonists.Count());
            Assert.All(colony.Members, c => Assert.Equal(settlement.Id, c.HomeSettlementId));
        }
        world.Colonies[0].Stock.Add(ResourceType.Wood, 11);
        int wood = world.Colonies[1].Stock.Get(ResourceType.Wood);
        Assert.Equal(wood, world.Colonies[1].PrimarySettlement.Stock.Get(ResourceType.Wood));
        string path = Path.Combine(Path.GetTempPath(), "GodColony-etablissement-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world); WorldState loaded = WorldSave.Load(path).World;
            Assert.Equal(world.Settlements.Select(s => s.Id), loaded.Settlements.Select(s => s.Id));
            Assert.Equal(world.Colonies[0].Stock.Get(ResourceType.Wood), loaded.Colonies[0].PrimarySettlement.Stock.Get(ResourceType.Wood));
            for (int tick = 0; tick < 250; tick++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Le_format_v5_conserve_son_village_et_recoit_un_etablissement()
    {
        WorldState world = WorldSave.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "territoires-v5.gcsave")).World;
        Assert.Equal(12345, world.Seed);
        Assert.Equal(2, world.Settlements.Count());
        Assert.Equal(20, world.Colonies.Sum(c => c.Members.Count));
        Assert.All(world.Colonies, c => Assert.Same(c.Layout, c.PrimarySettlement.Layout));
        world.Step();
    }
}
