using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class LogisticsTests
{
    private static WorldState Monde() => new(12345, startingColonists: 10, colonyCount: 2, migration: false, lifecycle: false, trade: false);

    private static TradePlan? PlanApresRencontre(WorldState world, Colony from, Colony to, bool emergency = false)
    {
        Trade.ObserveMarket(world, from, to);
        return Trade.Plan(world, from, to, emergency);
    }

    private static void Fixer(Colony colony, ResourceType resource, int amount)
    {
        Assert.True(colony.Stock.TryTake(resource, colony.Stock.Get(resource)));
        colony.Stock.Add(resource, amount, ResourceFlow.Transfer);
    }

    [Fact]
    public void Un_bien_reserve_ne_peut_etre_mange_vendu_ou_charge_deux_fois()
    {
        var stock = new Stockpile();
        var cargo = new Stockpile();
        stock.Add(ResourceType.Grain, 8);
        StockReservation promise = stock.Reserve("Ravitaillement", ResourceType.Grain, 6, 100, 10, 0)!;
        Assert.Equal(8, stock.Get(ResourceType.Grain));
        Assert.Equal(2, stock.Available(ResourceType.Grain));
        Assert.False(stock.TryTake(ResourceType.Grain, 3));
        Assert.Null(stock.Reserve("Autre voyage", ResourceType.Grain, 3, 50, 10, 0));
        Assert.True(stock.LoadReservation(promise, cargo, 1));
        Assert.False(stock.LoadReservation(promise, cargo, 1));
        Assert.Equal(8, stock.Get(ResourceType.Grain) + cargo.Get(ResourceType.Grain));
        Assert.Empty(stock.Reservations);
    }

    [Fact]
    public void Les_intrants_engages_ne_financent_pas_une_fabrication_et_une_perte_reste_physique()
    {
        WorldState world = Monde();
        Colony colony = world.Colonies[0];
        Fixer(colony, ResourceType.IronOre, 3);
        Fixer(colony, ResourceType.Charcoal, 2);
        var promise = colony.Stock.Reserve("Mine", ResourceType.IronOre, 3, 100, 1000, world.Clock.Ticks)!;
        Assert.False(ToolChain.TryTakeInputs(colony, ToolChain.RecipeFor(BuildingType.Bloomery), out _));
        Assert.Equal(2, colony.Stock.Get(ResourceType.Charcoal));
        Assert.True(colony.Stock.TryTake(ResourceType.IronOre, 1, ResourceFlow.Loss));
        Assert.False(promise.Active);
        Assert.Equal(2, colony.Stock.Available(ResourceType.IronOre));
    }

    [Fact]
    public void Les_promesses_expirent_et_la_survie_libere_le_confort()
    {
        var stock = new Stockpile();
        stock.Add(ResourceType.Grain, 10);
        var comfort = stock.Reserve("Fête", ResourceType.Grain, 3, 10, 5, 0)!;
        var rescue = stock.Reserve("Secours", ResourceType.Grain, 5, 100, 10, 0)!;
        stock.CancelReservationsBelow(100);
        Assert.False(comfort.Active);
        Assert.True(rescue.Active);
        stock.ExpireReservations(10);
        Assert.False(rescue.Active);
        Assert.Equal(10, stock.Available(ResourceType.Grain));
    }

    [Fact]
    public void Un_transfert_conserve_l_age_et_la_viande_perdue_annule_sa_promesse()
    {
        var source = new Stockpile();
        var cargo = new Stockpile();
        var destination = new Stockpile();
        source.Add(ResourceType.Meat, 6);
        source.AgeMeat(); source.AgeMeat();
        Assert.True(source.TryTransferTo(cargo, ResourceType.Meat, 4));
        Assert.Equal(4, cargo.MeatAtLeast(2));
        cargo.AgeMeat();
        Assert.True(cargo.TryTransferTo(destination, ResourceType.Meat, 4));
        Assert.Equal(4, destination.MeatAtLeast(3));
        var promise = destination.Reserve("Cuisine", ResourceType.Meat, 4, 10, 100, 0)!;
        Assert.Equal(2, destination.SpoilMeat(3, 0.5f));
        Assert.False(promise.Active);
        Assert.Equal(2, destination.Available(ResourceType.Meat));
        Assert.Equal(6, source.Get(ResourceType.Meat) + destination.Get(ResourceType.Meat) + 2);
        Assert.Equal(0, ResourceAccounting.Total(destination, ResourceType.Meat, ResourceFlow.Production));
    }

    [Fact]
    public void Les_quantites_negatives_et_le_debordement_ne_modifient_pas_le_stock()
    {
        var stock = new Stockpile();
        stock.Add(ResourceType.Wood, int.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Add(ResourceType.Wood, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.TryTake(ResourceType.Wood, -1));
        Assert.Throws<OverflowException>(() => stock.Add(ResourceType.Wood, 1));
        var source = new Stockpile();
        source.Add(ResourceType.Wood, 1);
        Assert.False(source.TryTransferTo(stock, ResourceType.Wood, 1));
        Assert.Equal(1, source.Get(ResourceType.Wood));
        Assert.Equal(int.MaxValue, stock.Get(ResourceType.Wood));
    }

    [Fact]
    public void Un_depart_impossible_ne_retire_ni_biens_ni_personnes()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Fixer(from, ResourceType.Tools, 1);
        int food = from.Stock.FoodUnits, coins = from.Stock.Get(ResourceType.Coins);
        var plan = new TradePlan(from, to, [new(ResourceType.Tools, 3, 1, true)], 50, 1, 1);
        Assert.Null(Trade.Depart(world, plan));
        Assert.Equal(1, from.Stock.Get(ResourceType.Tools));
        Assert.Equal(food, from.Stock.FoodUnits);
        Assert.Equal(coins, from.Stock.Get(ResourceType.Coins));
        Assert.Equal(10, from.Members.Count);
        Assert.Empty(from.Transients);
        Assert.Empty(from.Stock.Reservations);
        Assert.Empty(world.Caravans);
    }

    [Fact]
    public void Une_crise_autorise_un_achat_utile_sans_retire_les_producteurs_de_vivres()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Fixer(from, ResourceType.Food, 28);
        Fixer(from, ResourceType.Grain, 0);
        Fixer(from, ResourceType.Bread, 0);
        Fixer(to, ResourceType.Bread, 300);
        ColonyBrain.Think(from, from.Map, world.Clock);
        Assert.True(Trade.NeedsEmergencyFood(from));
        Assert.False(from.Sensors!.SurvivalAssured);
        foreach (Colonist worker in from.Members) worker.Sector = WorkSector.Food;
        foreach (Colonist worker in from.Members.Take(2)) worker.Sector = WorkSector.Free;
        var producers = from.Members.Skip(2).ToArray();
        Assert.True(PlanApresRencontre(world, from, to, emergency: true) is not null,
            $"Trajet {world.WorldMap.TravelDays(from, to)} j, nutrition {from.Stock.AvailableNutrition}, grain proposé {Economy.Clear(to, from, ResourceType.Bread, 36).Units}");
        Trade.Daily(world, from);
        Caravan caravan = Assert.Single(world.Caravans);
        Assert.All(caravan.Plan, l => Assert.True(!l.IsSale && ResourceCatalog.Nutrition(l.Good) > 0));
        Assert.All(producers, p => Assert.Contains(p, from.Members));
        Assert.True(caravan.Provisions.FoodUnits > 0);
        Assert.True(caravan.LoadWeight <= Trade.CapacityOf(from, to));
    }

    [Fact]
    public void Un_secours_sans_provisions_ou_sans_bras_disponibles_ne_part_pas()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Fixer(from, ResourceType.Food, 28);
        Fixer(to, ResourceType.Bread, 300);
        foreach (Colonist worker in from.Members) worker.Sector = WorkSector.Food;
        TradePlan plan = PlanApresRencontre(world, from, to, emergency: true)!;
        Assert.NotNull(plan);
        Assert.Null(Trade.Depart(world, plan));
        Fixer(from, ResourceType.Food, 0);
        Assert.Null(PlanApresRencontre(world, from, to, emergency: true));
        Assert.Empty(world.Caravans);
    }

    [Fact]
    public void Les_invendes_ne_permettent_pas_de_surcharger_le_retour_et_les_pieces_restent_physiques()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Fixer(from, ResourceType.Wood, 30);
        Fixer(to, ResourceType.Wood, 0);
        Fixer(to, ResourceType.Tools, 30);
        var plan = new TradePlan(from, to, [new(ResourceType.Wood, 12, 1, true), new(ResourceType.Tools, 20, 100, false)], 100, 1, 1);
        Fixer(from, ResourceType.Coins, 2200);
        // Les pièces occupent elles-mêmes de la place : ce départ trop chargé est refusé.
        Assert.Null(Trade.Depart(world, plan));
        plan = new(from, to, [new(ResourceType.Wood, 12, 1, true), new(ResourceType.Tools, 12, 10, false)], 100, 1, 1);
        Caravan caravan = Trade.Depart(world, plan)!;
        Assert.NotNull(caravan);
        Fixer(to, ResourceType.Wood, 1000); // l'hôte ne veut plus acheter le chargement.
        int coins = from.Stock.Get(ResourceType.Coins) + to.Stock.Get(ResourceType.Coins) + caravan.Coins;
        while (world.Clock.Ticks < caravan.ArriveTicks) world.Clock.Advance();
        Trade.Hourly(world);
        Assert.True(caravan.LoadWeight <= Trade.CapacityOf(from, to));
        Assert.Equal(coins, from.Stock.Get(ResourceType.Coins) + to.Stock.Get(ResourceType.Coins) + caravan.Coins);
    }

    [Fact]
    public void Les_voyageurs_mangent_leurs_provisions_une_fois_par_heure()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Fixer(from, ResourceType.Tools, 30);
        Fixer(to, ResourceType.Tools, 0);
        Fixer(to, ResourceType.Coins, 800);
        Caravan caravan = Trade.Depart(world, PlanApresRencontre(world, from, to)!)!;
        Assert.NotNull(caravan);
        foreach (Colonist trader in caravan.Traders)
        {
            from.Transients.Remove(trader);
            trader.Needs.Food = 0.3f;
        }
        int food = caravan.Provisions.FoodUnits;
        for (int i = 0; i < Math.Ceiling(TimeConstants.TicksPerHour); i++) world.Clock.Advance();
        Trade.Hourly(world);
        Assert.Equal(food - Trade.TradersPerCaravan, caravan.Provisions.FoodUnits);
        Assert.All(caravan.Traders, t => Assert.True(t.Needs.Food > 0.3f));
        int remaining = caravan.Provisions.FoodUnits;
        Trade.Hourly(world);
        Assert.Equal(remaining, caravan.Provisions.FoodUnits);
    }

    [Fact]
    public void Un_reglement_impossible_ne_debite_ni_pieces_ni_marchandises()
    {
        var seller = new Stockpile();
        var buyer = new Stockpile();
        seller.Add(ResourceType.Meat, 3);
        seller.AgeMeat();
        buyer.Add(ResourceType.Meat, int.MaxValue);
        buyer.Add(ResourceType.Coins, 10);
        Assert.False(seller.TrySellTo(buyer, ResourceType.Meat, 2, 5));
        Assert.Equal(3, seller.Get(ResourceType.Meat));
        Assert.Equal(10, buyer.Get(ResourceType.Coins));
        Assert.True(buyer.TryTake(ResourceType.Meat, 2));
        Assert.True(seller.TrySellTo(buyer, ResourceType.Meat, 2, 5));
        Assert.Equal(2, buyer.MeatAtLeast(1));
        Assert.Equal(5, seller.Get(ResourceType.Coins));
        Assert.Equal(5, buyer.Get(ResourceType.Coins));
    }

    [Fact]
    public void Une_cargaison_qui_designe_le_stock_du_village_est_refusee_au_chargement()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        var caravan = new Caravan(from, to, [], [], 0, world.Clock.Ticks, world.Clock.Ticks + 100, world.Clock.Ticks + 200);
        typeof(Caravan).GetField("_inventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(caravan, from.Stock);
        world.Caravans.Add(caravan);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-stock-partage-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            Assert.Throws<InvalidDataException>(() => WorldSave.Load(path));
            Assert.Equal(ColonyFounder.StartingCoins, from.Stock.Get(ResourceType.Coins));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Les_promesses_les_ages_et_la_suite_du_voyage_se_sauvegardent_exactement()
    {
        WorldState world = Monde();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Fixer(from, ResourceType.Tools, 30);
        Fixer(to, ResourceType.Tools, 0);
        Fixer(to, ResourceType.Coins, 800);
        Caravan caravan = Trade.Depart(world, PlanApresRencontre(world, from, to)!)!;
        from.Stock.Add(ResourceType.Meat, 8);
        from.Stock.AgeMeat(); from.Stock.AgeMeat();
        Assert.True(from.Stock.TryTransferTo(caravan.Inventory, ResourceType.Meat, 2));
        from.Stock.Reserve("Cuisine", ResourceType.Meat, 4, 20, world.Clock.Ticks + 10 * TimeConstants.TicksPerDay, world.Clock.Ticks);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-logistique-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Equal(2, loaded.Colonies[0].Stock.Available(ResourceType.Meat));
            Assert.Equal(2, loaded.Caravans[0].Inventory.MeatAtLeast(2));
            Assert.Same(loaded.Caravans[0].Cargo, loaded.Caravans[0].Inventory.Amounts);
            for (int i = 0; i < 4000; i++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
