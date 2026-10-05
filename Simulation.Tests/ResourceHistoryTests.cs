using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class ResourceHistoryTests
{
    private static WorldState World(int colonies = 1) => new(42, 128, 128, colonyCount: colonies,
        startingColonists: 10, migration: false, lifecycle: false, trade: false);

    private static void Days(WorldState world, int count)
    {
        for (int i = 0; i < count * TimeConstants.TicksPerDay; i++) world.Clock.Advance();
    }

    [Fact]
    public void Les_flux_distinguent_production_usage_echanges_et_transferts()
    {
        var stock = new Stockpile();
        stock.Add(ResourceType.Wood, 30);
        stock.Add(ResourceType.Wood, 7, ResourceFlow.Purchase);
        stock.Add(ResourceType.Wood, 9, ResourceFlow.Transfer);
        Assert.True(stock.TryTake(ResourceType.Wood, 4));
        Assert.True(stock.TryTake(ResourceType.Wood, 5, ResourceFlow.Sale));
        Assert.True(stock.TryTake(ResourceType.Wood, 3, ResourceFlow.Transfer));
        Assert.True(stock.TryTake(ResourceType.Wood, 2, ResourceFlow.Loss));
        Assert.False(stock.TryTake(ResourceType.Wood, 1000));
        Assert.Equal(30, ResourceAccounting.Total(stock, ResourceType.Wood, ResourceFlow.Production));
        Assert.Equal(4, ResourceAccounting.Total(stock, ResourceType.Wood, ResourceFlow.Usage));
        Assert.Equal(7, ResourceAccounting.Total(stock, ResourceType.Wood, ResourceFlow.Purchase));
        Assert.Equal(5, ResourceAccounting.Total(stock, ResourceType.Wood, ResourceFlow.Sale));
    }

    [Fact]
    public void Les_releves_mesurent_les_flux_par_jour_sans_inventer_le_stock_initial()
    {
        var world = World();
        var colony = world.Colonies[0];
        var history = new ResourceHistory();
        colony.Stock.Add(ResourceType.Wood, 100, ResourceFlow.Transfer);
        history.Observe(world);
        Assert.Empty(history.Of(colony, ResourceType.Wood));
        colony.Stock.Add(ResourceType.Wood, 24);
        colony.Stock.TryTake(ResourceType.Wood, 10);
        colony.Stock.Add(ResourceType.Wood, 8, ResourceFlow.Purchase);
        colony.Stock.TryTake(ResourceType.Wood, 6, ResourceFlow.Sale);
        history.Observe(world);
        Assert.Empty(history.Of(colony, ResourceType.Wood));
        Days(world, 2);
        history.Observe(world);
        var sample = Assert.Single(history.Of(colony, ResourceType.Wood));
        Assert.Equal(12, sample.Production);
        Assert.Equal(5, sample.Usage);
        Assert.Equal(4, sample.Purchases);
        Assert.Equal(3, sample.Sales);
        Days(world, 1); history.Observe(world);
        Assert.Equal(0, history.Of(colony, ResourceType.Wood)[^1].Production);
    }

    [Fact]
    public void L_historique_reste_borne_et_garde_les_pics_recents()
    {
        var world = World(); var colony = world.Colonies[0]; var history = new ResourceHistory();
        history.Observe(world);
        for (int day = 1; day <= ResourceHistory.MaxSamples + 15; day++)
        {
            colony.Stock.Add(ResourceType.Wood, day, ResourceFlow.Purchase);
            Days(world, 1); history.Observe(world);
        }
        var samples = history.Of(colony, ResourceType.Wood);
        Assert.Equal(ResourceHistory.MaxSamples, samples.Count);
        Assert.Equal(16, samples[0].Day);
        Assert.Equal(ResourceHistory.MaxSamples + 15, samples[^1].Purchases);
    }

    [Fact]
    public void La_caravane_compte_les_echanges_conclus_une_fois_et_ne_produit_rien_au_retour()
    {
        var world = World(2); var from = world.Colonies[0]; var host = world.Colonies[1];
        from.Stock.Add(ResourceType.Tools, 30, ResourceFlow.Transfer);
        host.Stock.Add(ResourceType.Coins, 500, ResourceFlow.Transfer);
        var deal = Economy.Clear(from, host, ResourceType.Tools, 10);
        Assert.True(deal.Units > 0);
        var caravan = Trade.Depart(world, new TradePlan(from, host,
            [new TradeLine(ResourceType.Tools, deal.Units, deal.UnitPrice, true)], 100, 1, 1));
        Assert.NotNull(caravan);
        Assert.Equal(0, ResourceAccounting.Total(from.Stock, ResourceType.Tools, ResourceFlow.Usage));
        Assert.Equal(0, ResourceAccounting.Total(from.Stock, ResourceType.Tools, ResourceFlow.Sale));
        while (world.Clock.Ticks < caravan!.ArriveTicks) world.Clock.Advance();
        Trade.Hourly(world);
        int sold = caravan.Settled.Sum(l => l.Units);
        Assert.True(sold > 0);
        Assert.Equal(sold, ResourceAccounting.Total(from.Stock, ResourceType.Tools, ResourceFlow.Sale));
        Assert.Equal(sold, ResourceAccounting.Total(host.Stock, ResourceType.Tools, ResourceFlow.Purchase));
        Assert.Equal(0, ResourceAccounting.Total(host.Stock, ResourceType.Tools, ResourceFlow.Production));
        while (world.Clock.Ticks < caravan.ReturnTicks) world.Clock.Advance();
        Trade.Hourly(world);
        Assert.Equal(CaravanState.Home, caravan.State);
        Assert.Equal(sold, ResourceAccounting.Total(from.Stock, ResourceType.Tools, ResourceFlow.Sale));
        Assert.Equal(0, ResourceAccounting.Total(from.Stock, ResourceType.Tools, ResourceFlow.Production));
    }
}
