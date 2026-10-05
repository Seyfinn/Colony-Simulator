using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class SupplierTests
{
    private static WorldState Monde()
    {
        var world = new WorldState(12345, startingColonists: 10, colonyCount: 2, migration: false, lifecycle: false, trade: false);
        foreach (Colony colony in world.Colonies) colony.Stock.Add(ResourceType.Food, 200);
        world.Colonies[0].Stock.Add(ResourceType.Tools, 30);
        world.Colonies[1].Stock.Add(ResourceType.Coins, 1000);
        return world;
    }

    private static void Heures(WorldState world, int hours)
    {
        for (int hour = 0; hour < hours; hour++)
        {
            for (int tick = 0; tick < Math.Ceiling(TimeConstants.TicksPerHour); tick++) world.Clock.Advance();
            Trade.Hourly(world);
        }
    }

    private static Caravan Envoyer(WorldState world)
    {
        Colony from = world.Colonies[0], to = world.Colonies[1];
        var plan = new TradePlan(from, to, [new(ResourceType.Tools, 3, 40, true)], 200, 20, 2);
        Caravan trip = Trade.Depart(world, plan)!;
        Assert.NotNull(trip);
        foreach (Colonist trader in trip.Traders) from.Transients.Remove(trader);
        return trip;
    }

    [Fact]
    public void Les_offres_arrivent_avec_les_marchands_et_ne_s_actualisent_pas_a_distance()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        TradePlan contact = Trade.Plan(world, from, to)!;
        Assert.True(contact.ContactOnly);
        Assert.Empty(contact.Lines);
        Caravan trip = Trade.Depart(world, contact)!;
        Assert.NotNull(trip);
        for (int hour = 0; trip.State == CaravanState.Outbound && hour < 100; hour++) Heures(world, 1);
        Assert.Equal(CaravanState.Returning, trip.State);
        Assert.Empty(Trade.Suppliers(world, from));
        Assert.Single(Trade.Suppliers(world, to));
        for (int hour = 0; trip.State != CaravanState.Home && hour < 100; hour++) Heures(world, 1);
        SupplierMemory memory = Assert.Single(Trade.Suppliers(world, from));
        TradePlan before = Trade.Plan(world, from, to)!;
        Assert.NotNull(before);
        Assert.False(before.ContactOnly);
        MarketOffer[] offers = memory.Offers.ToArray();
        to.Stock.Add(ResourceType.Tools, 100);
        Assert.True(to.Stock.TryTake(ResourceType.Coins, to.Stock.Get(ResourceType.Coins)));
        TradePlan after = Trade.Plan(world, from, to)!;
        Assert.Equal(before.Lines, after.Lines);
        Assert.Equal(offers, memory.Offers);
    }

    [Fact]
    public void Les_commandes_et_fabrications_reduisent_l_achat_sans_creer_un_stock_comestible()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[1], to = world.Colonies[0];
        from.Stock.Add(ResourceType.Iron, 2); from.Stock.Add(ResourceType.Charcoal, 1);
        Recipe recipe = ToolChain.RecipeFor(BuildingType.Forge);
        Assert.True(ToolChain.TryTakeInputs(from, recipe, out _, out Stockpile inputs));
        from.Members[0].Activity = new Activity(ActivityKind.Craft, from.CampX, from.CampY, 100)
        {
            InputsTaken = true, Started = true, InputsInventory = inputs, CommittedRecipe = recipe,
            Building = new Building(BuildingType.Forge, from.CampX, from.CampY),
        };
        var returning = new Caravan(from, to, [], [], 0, 0, 100, TimeConstants.TicksPerDay) { State = CaravanState.Returning };
        returning.Inventory.Add(ResourceType.Tools, 4);
        returning.Inventory.Add(ResourceType.Grain, 20);
        world.Caravans.Add(returning);
        decimal edible = from.Stock.AvailableNutrition;
        SupplyForecast forecast = Trade.Forecast(world, from, ResourceType.Tools);
        Assert.Equal(4, forecast.Incoming); Assert.Equal(1, forecast.InProduction);
        Assert.Equal(0, forecast.PurchaseNeed); Assert.Equal(0, forecast.Physical);
        Assert.Equal(20, Trade.Forecast(world, from, ResourceType.Grain).Incoming);
        Assert.Equal(edible, from.Stock.AvailableNutrition);
        returning.BlockedReason = "Passage fermé";
        Assert.Equal(0, Trade.Forecast(world, from, ResourceType.Tools).Incoming);
        Assert.Equal(4, Trade.Forecast(world, from, ResourceType.Tools).PurchaseNeed);
        var outbound = new Caravan(from, to, [], [new(ResourceType.Tools, 2, 10, false)], 0, 0, 100, TimeConstants.TicksPerDay);
        world.Caravans.Add(outbound);
        Assert.Equal(2, Trade.Forecast(world, from, ResourceType.Tools).Ordered);
        Assert.Equal(2, Trade.Forecast(world, from, ResourceType.Tools).PurchaseNeed);
    }

    [Fact]
    public void Un_passage_coupe_attend_sans_perdre_la_cargaison_et_reprend_apres_sauvegarde()
    {
        WorldState world = Monde(); Caravan trip = Envoyer(world);
        int start = trip.Route!.Tiles[0];
        int[] exits = world.WorldMap.Grid.Neighbors(start).ToArray();
        foreach (int tile in exits) world.WorldMap.SetPassageClosed(tile, true);
        Heures(world, 1);
        Assert.NotNull(trip.BlockedReason);
        Assert.Equal(0, trip.RouteIndex); Assert.Equal(0, trip.SegmentTravelCost);
        Assert.Equal(3, trip.Inventory.Get(ResourceType.Tools));
        Assert.Empty(Trade.Commitments(world, trip.From));
        string path = Path.Combine(Path.GetTempPath(), "GodColony-commerce-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Equal(trip.BlockedReason, loaded.Caravans[0].BlockedReason);
            Assert.Equal(3, loaded.Caravans[0].Inventory.Get(ResourceType.Tools));
            foreach (int tile in exits) { world.WorldMap.SetPassageClosed(tile, false); loaded.WorldMap.SetPassageClosed(tile, false); }
            Heures(world, 100); Heures(loaded, 100);
            Assert.Empty(world.Caravans); Assert.Empty(loaded.Caravans);
            Assert.Equal(world.Colonies[0].Stock.Get(ResourceType.Coins), loaded.Colonies[0].Stock.Get(ResourceType.Coins));
            Assert.Equal(world.Colonies[1].Stock.Get(ResourceType.Tools), loaded.Colonies[1].Stock.Get(ResourceType.Tools));
            Assert.Equal(Trade.Suppliers(world, world.Colonies[0]).Single().Refusals,
                Trade.Suppliers(loaded, loaded.Colonies[0]).Single().Refusals);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Une_guerre_apres_depart_fait_revenir_les_biens_sans_echange_ni_teleportation()
    {
        WorldState world = Monde(); Caravan trip = Envoyer(world);
        int coins = world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)) + trip.Coins;
        Heures(world, 1);
        world.Pacts.Add(new Pact(trip.From, trip.To, PactKind.War, world.Clock.Ticks));
        Heures(world, 1);
        Assert.Contains(trip, world.Caravans);
        Assert.Equal(3, trip.Inventory.Get(ResourceType.Tools));
        Heures(world, 100);
        Assert.True(trip.Aborted); Assert.Equal(CaravanState.Home, trip.State);
        Assert.Equal(30, trip.From.Stock.Get(ResourceType.Tools)); Assert.Equal(0, trip.To.Stock.Get(ResourceType.Tools));
        Assert.Equal(coins, world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)));
    }

    [Fact]
    public void Une_route_coupee_prend_un_detour_depuis_sa_position_reelle()
    {
        WorldState world = Monde(); Caravan trip = Envoyer(world);
        int closed = trip.Route!.Tiles[1];
        world.WorldMap.SetPassageClosed(closed, true);
        Heures(world, 1);
        Assert.Null(trip.BlockedReason);
        Assert.DoesNotContain(closed, trip.Route!.Tiles);
        Assert.Equal(world.WorldMap.TileOf(trip.From), trip.Route.Tiles[0]);
        Assert.Equal(3, trip.Inventory.Get(ResourceType.Tools));
        Assert.True(trip.SegmentTravelCost > 0);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Les_formats_precedents_restent_chargeables(int version)
    {
        WorldState world = WorldSave.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"commerce-v{version}.gcsave")).World;
        Assert.Equal(12345, world.Seed);
        Assert.Empty(world.SupplierMemories);
        Assert.Empty(world.WorldMap.ClosedPassages);
        world.Step();
    }

    [Theory]
    [InlineData(0.1, 0)]
    [InlineData(1.5, 5)]
    public void Le_paiement_et_le_bilan_suivent_les_biens_reels_meme_apres_sauvegarde(double prix, int paiement)
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        int food = from.Stock.Get(ResourceType.Food), tools = from.Stock.Get(ResourceType.Tools);
        int purse = from.Stock.Get(ResourceType.Coins);
        int allCoins = world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins));
        double foodCost = Economy.Cost(from, ResourceType.Food), toolCost = Economy.Cost(from, ResourceType.Tools);
        Caravan trip = Trade.Depart(world, new TradePlan(from, to, [new(ResourceType.Tools, 3, prix, true)], 999, 1, 1))!;
        Assert.NotNull(trip);
        foreach (Colonist trader in trip.Traders) from.Transients.Remove(trader);
        Heures(world, 1);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-bilan-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Equal(trip.DepartureValues, loaded.Caravans[0].DepartureValues);
            Heures(world, 100); Heures(loaded, 100);
            TradeRecord record = Assert.Single(from.Trades);
            Assert.Equal(paiement, record.NetCoins);
            Assert.Equal(paiement, from.Stock.Get(ResourceType.Coins) - purse);
            Assert.Equal(allCoins, world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)));
            Assert.Equal(paiement == 0 ? tools : tools - 3, from.Stock.Get(ResourceType.Tools));
            Assert.Equal(paiement, record.Lines.Sum(l => l.Total));
            double absence = (record.Ticks - trip.DepartTicks) / (double)TimeConstants.TicksPerDay * trip.Traders.Count * Trade.WorkHoursPerDay;
            double expected = paiement + (from.Stock.Get(ResourceType.Tools) - tools) * toolCost
                + (from.Stock.Get(ResourceType.Food) - food) * foodCost - absence;
            Assert.Equal(expected, record.GainHours!.Value, 7);
            Assert.True(record.GainHours < 0);
            Assert.Equal(999, record.ExpectedGainHours);
            Assert.Equal(record.GainHours, from.LifetimeTradeGainHours);
            Assert.Equal(record.GainHours, loaded.Colonies[0].Trades.Single().GainHours);
            Assert.Equal(record.CostHours, Trade.Suppliers(world, from).Single().LastCostHours);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Les_livraisons_locales_et_les_futs_reduisent_les_achats_sans_devenir_disponibles()
    {
        WorldState world = Monde(); Colony colony = world.Colonies[1];
        colony.Members[0].Carrying = (ResourceType.Tools, 2);
        colony.Transients.Add(colony.Members[0]); // Un même habitant ne compte qu'une fois.
        colony.Members[1].Carrying = (ResourceType.Tools, 1);
        colony.Members[1].CarryingTo = new Building(BuildingType.Hut, colony.CampX, colony.CampY);
        SupplyForecast tools = Trade.Forecast(world, colony, ResourceType.Tools);
        Assert.Equal(2, tools.LocalIncoming); Assert.Equal(3, tools.PurchaseNeed); Assert.Equal(0, tools.Available);
        colony.Buildings.Add(new Building(BuildingType.Tavern, colony.CampX, colony.CampY) { Progress = 1 });
        var cask = new Building(BuildingType.Cask, colony.CampX + 2, colony.CampY) { Progress = 1 };
        colony.Buildings.Add(cask); Cuisine.StartBrewing(colony, cask, 12, world.Clock);
        decimal nutrition = colony.Stock.AvailableNutrition;
        SupplyForecast beer = Trade.Forecast(world, colony, ResourceType.Beer);
        Assert.Equal(Cuisine.BeerRecipe.OutputAmount, beer.Fermenting);
        Assert.Equal(0, beer.PurchaseNeed); Assert.Equal(0, beer.Physical); Assert.Equal(0, beer.Available);
        Assert.Equal(0, Trade.Forecast(world, colony, ResourceType.Beer, horizonDays: 4).Fermenting);
        Assert.True(Trade.Forecast(world, colony, ResourceType.Beer, horizonDays: 4).PurchaseNeed > 0);
        Assert.Equal(nutrition, colony.Stock.AvailableNutrition);
        colony.Buildings.RemoveAll(b => b.Type == BuildingType.Tavern);
        Assert.Equal(0, Trade.Forecast(world, colony, ResourceType.Beer).Fermenting);
    }
}
