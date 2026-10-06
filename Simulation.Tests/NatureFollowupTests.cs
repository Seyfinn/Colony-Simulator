using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class NatureFollowupTests
{
    [Fact]
    public void Le_butin_en_route_reste_dans_la_masse_monetaire()
    {
        var world = new WorldState(12345, colonyCount: 2, migration: false, lifecycle: false, trade: false);
        Colony a = world.Colonies[0], b = world.Colonies[1];
        long mass = MonetaryLedger.Mass(world);
        Assert.True(b.Stock.TryTake(ResourceType.Coins, 50));
        var party = new WarParty(a, b, [], 0, world.Clock.Ticks, world.Clock.Ticks + 100);
        party.Loot[ResourceType.Coins] = 50; world.WarParties.Add(party);
        Assert.Equal(mass, MonetaryLedger.Mass(world));
        Assert.Equal(0, world.Money.Imbalance(world));
    }

    [Fact]
    public void Un_champ_eloigne_declenche_la_demande_et_la_forge_fabrique_la_charrette()
    {
        var world = new WorldState(777, startingColonists: 10, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        colony.Fields.Clear();
        // Le coin le plus éloigné du camp met le dépôt à plus de trente-trois cases.
        int x = colony.CampX < colony.Map.Width / 2 ? colony.Map.Width - 6 : 2;
        int y = colony.CampY < colony.Map.Height / 2 ? colony.Map.Height - 6 : 2;
        colony.Fields.Add(new Field(x, y));
        Assert.True(Farming.AverageDepotRoundTripSeconds(colony) > Carts.RoundTripThresholdSeconds);
        Assert.Equal(1, Carts.Wanted(colony));
        Assert.Equal(1, ExtendedIndustry.Target(colony, ResourceType.Carts));
        Knowledge.Grant(colony, [Discovery.Metallurgy], world.Clock.Ticks);
        Building forge = Urbanism.BuildInstantly(colony.Map, colony, BuildingType.Forge)!;
        Assert.NotNull(forge);
        colony.Stock.Add(ResourceType.Tools, 100); colony.Stock.Add(ResourceType.Wood, 100);
        colony.Stock.Add(ResourceType.Iron, 1); colony.Stock.Add(ResourceType.Food, 1000);
        ColonyBrain.Think(colony, colony.Map, world.Clock);
        Assert.Equal(ResourceType.Carts, ExtendedIndustry.RecipeFor(colony, BuildingType.Forge, null)!.Output);
        Colonist worker = colony.PresentMembers.First();
        worker.Sector = WorkSector.Craft;
        for (int i = 0; i < 3 * TimeConstants.TicksPerDay && colony.Stock.Get(ResourceType.Carts) == 0; i++)
        { world.Clock.Advance(); ColonistAI.Tick(worker, world); }
        Assert.Equal(1, colony.Stock.Get(ResourceType.Carts));
        Assert.Equal(0, Carts.Wanted(colony));
        Assert.Equal(1, ResourceAccounting.Total(colony.Stock, ResourceType.Iron, ResourceFlow.Usage));
    }
}
