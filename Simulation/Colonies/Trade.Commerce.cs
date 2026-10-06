using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Une offre publiée lors d'une rencontre, sans copie du stock caché du fournisseur.</summary>
public sealed record MarketOffer(ResourceType Good, int Available, int Wanted, double SellPrice, double BuyPrice);

/// <summary>Ce qu'un peuple a effectivement appris d'un partenaire et de ses livraisons.</summary>
public sealed class SupplierMemory(Colony observer, Colony supplier)
{
    public Colony Observer { get; } = observer;
    public Colony Supplier { get; } = supplier;
    public List<MarketOffer> Offers { get; } = [];
    public long ObservedTicks { get; internal set; }
    public int BuyingBudget { get; internal set; }
    public int Deliveries { get; internal set; }
    public int Refusals { get; internal set; }
    public int Delays { get; internal set; }
    public double LastTripDays { get; internal set; }
    public double LastCostHours { get; internal set; }
    public double Reliability => (Deliveries + 1d) / (Deliveries + Refusals + 2d);
    public double AgeDays(long ticks) => Math.Max(0, ticks - ObservedTicks) / (double)TimeConstants.TicksPerDay;
    public double Confidence(long ticks) => Reliability / (1 + AgeDays(ticks) / 10);
}

/// <summary>Une commande portée par un voyage réel ; seule une cargaison acquise est confirmée.</summary>
public sealed record TradeCommitment(Caravan Trip, ResourceType Good, int Units, long DueTicks, bool Confirmed);

/// <summary>Les biens comestibles restent distincts des promesses et des arrivées futures.</summary>
public sealed record SupplyForecast(int Physical, int Available, int Reserved, int Processing,
    int InProduction, int LocalIncoming, int Fermenting, int Incoming, int Ordered, int Shortage, int PurchaseNeed, bool Urgent);

public static partial class Trade
{
    public const int OfferLifetimeDays = 30;
    public static IEnumerable<SupplierMemory> Suppliers(WorldState world, Colony colony) =>
        world.SupplierMemories.Where(m => m.Observer == colony);

    private static SupplierMemory Memory(WorldState world, Colony observer, Colony supplier)
    {
        SupplierMemory? memory = Suppliers(world, observer).FirstOrDefault(m => m.Supplier == supplier);
        if (memory is not null) return memory;
        memory = new SupplierMemory(observer, supplier);
        world.SupplierMemories.Add(memory);
        return memory;
    }

    private static List<MarketOffer> Publish(Colony colony) => Economy.Tradable.Select(g =>
        new MarketOffer(g, Economy.Surplus(colony, g), Economy.Shortage(colony, g),
            Economy.KeepValue(colony, g, colony.Stock.Available(g)),
            Economy.UseValue(colony, g, colony.Stock.Available(g)))).ToList();

    private static void Remember(WorldState world, Colony observer, Colony supplier,
        IEnumerable<MarketOffer> offers, long observedTicks, int buyingBudget)
    {
        SupplierMemory memory = Memory(world, observer, supplier);
        if (observedTicks < memory.ObservedTicks) return;
        memory.Offers.Clear(); memory.Offers.AddRange(offers);
        memory.ObservedTicks = observedTicks;
        memory.BuyingBudget = buyingBudget;
    }

    /// <summary>Réservé aux rencontres effectives et aux scénarios contrôlés de simulation.</summary>
    internal static void ObserveMarket(WorldState world, Colony observer, Colony supplier) =>
        Remember(world, observer, supplier, Publish(supplier), world.Clock.Ticks, supplier.Stock.Available(ResourceType.Coins));

    public static IReadOnlyList<TradeCommitment> Commitments(WorldState world, Colony colony)
    {
        var result = new List<TradeCommitment>();
        foreach (Caravan trip in world.Caravans.Where(c => c.State != CaravanState.Home && (!c.Aborted || c.State == CaravanState.Returning)
            && (c.State == CaravanState.Returning ? (c.FromSettlementId == 0 ? c.From.PrimarySettlementId : c.FromSettlementId) == colony.LocalSettlement.Id
                : c.Purpose == TerritorialPurpose.Supply ? (c.ToSettlementId == 0 ? c.To.PrimarySettlementId : c.ToSettlementId) == colony.LocalSettlement.Id : (c.FromSettlementId == 0 ? c.From.PrimarySettlementId : c.FromSettlementId) == colony.LocalSettlement.Id)))
        {
            if (trip.BlockedReason is not null || world.Clock.Ticks > trip.ReturnTicks + TimeConstants.TicksPerHour) continue;
            if (trip.State == CaravanState.Returning)
            {
                foreach (var cargo in trip.Cargo.Where(p => p.Value > 0 && p.Key != ResourceType.Coins))
                    result.Add(new(trip, cargo.Key, cargo.Value, trip.ReturnTicks, true));
            }
            else if (trip.Purpose == TerritorialPurpose.Supply)
                foreach (var cargo in trip.Cargo.Where(p => p.Value > 0 && p.Key != ResourceType.Coins))
                    result.Add(new(trip, cargo.Key, cargo.Value, trip.ArriveTicks, true));
            else if (trip.Purpose == TerritorialPurpose.Commerce)
                foreach (TradeLine line in trip.Plan.Where(l => !l.IsSale))
                    result.Add(new(trip, line.Good, line.Units, trip.ReturnTicks, false));
        }
        return result;
    }

    public static SupplyForecast Forecast(WorldState world, Colony colony, ResourceType good, int horizonDays = 5)
    {
        int physical = colony.Stock.Get(good), available = colony.Stock.Available(good);
        Activity[] jobs = colony.PresentMembers.Select(m => m.Activity).OfType<Activity>()
            .Where(a => a is { Kind: ActivityKind.Craft, InputsTaken: true }).ToArray();
        int processing = Bounded(jobs.Sum(a => (long)(a.InputsInventory?.Get(good) ?? 0)));
        int production = Bounded(jobs.Where(a => a.CommittedRecipe?.Output == good)
            .Sum(a => (long)a.CommittedRecipe!.OutputAmount));
        long deadline = world.Clock.Ticks + Math.Max(0, horizonDays) * (long)TimeConstants.TicksPerDay;
        int localIncoming = Bounded(colony.PresentMembers.Concat(colony.Transients).Distinct()
            .Where(m => m.Colony == colony && m.CarryingTo is null && m.Carrying?.Type == good)
            .Sum(m => (long)m.Carrying!.Value.Amount));
        int fermenting = good is ResourceType.Beer or ResourceType.Wine && Civic.Has(colony, BuildingType.Tavern)
            ? Bounded(colony.Buildings.Count(b => b.Type == BuildingType.Cask && b.IsComplete
                && b.IsBrewing && (b.BrewProduct ?? ResourceType.Beer) == good && b.BrewReadyTicks <= deadline) * (long)(good == ResourceType.Wine ? 2 : Cuisine.BeerRecipe.OutputAmount)) : 0;
        TradeCommitment[] orders = Commitments(world, colony).Where(c => c.Good == good && c.DueTicks <= deadline).ToArray();
        int incoming = Bounded(orders.Where(c => c.Confirmed).Sum(c => (long)c.Units));
        int ordered = Bounded(orders.Where(c => !c.Confirmed).Sum(c => (long)c.Units));
        int shortage = (int)Math.Max(0, Math.Ceiling(Economy.Need(colony, good) - available));
        int buy = Bounded(Math.Max(0L, (long)shortage - production - localIncoming - fermenting - incoming - ordered));
        bool urgent = ResourceCatalog.Nutrition(good) > 0 && NeedsEmergencyFood(colony);
        return new(physical, available, physical - available, processing, production, localIncoming, fermenting, incoming, ordered, shortage, buy, urgent);
    }

    private static int Bounded(long amount) => (int)Math.Clamp(amount, 0, int.MaxValue);

    // Un lot inférieur à une pièce attend un regroupement ; aucune marchandise n'est cédée gratuitement.
    private static int LotPayment(int units, double price) => units <= 0 || units * price < 1
        ? 0 : (int)Math.Round(units * price, MidpointRounding.AwayFromZero);

    private static Clearing KnownClearing(WorldState world, Colony mine, MarketOffer offer, bool sale, int maxUnits, bool emergency)
    {
        int units = 0;
        double mineTotal = 0, price = 0;
        SupplyForecast forecast = Forecast(world, mine, offer.Good);
        int limit = sale ? Math.Min(offer.Wanted, Economy.Surplus(mine, offer.Good))
            : Math.Min(offer.Available, emergency ? Economy.Shortage(mine, offer.Good) : forecast.PurchaseNeed);
        for (int k = 0; k < Math.Min(limit, maxUnits); k++)
        {
            double mineValue = sale ? Economy.KeepValue(mine, offer.Good, forecast.Available - k)
                : Economy.UseValue(mine, offer.Good, forecast.Available + k);
            double sellerValue = sale ? mineValue : offer.SellPrice, buyerValue = sale ? offer.BuyPrice : mineValue;
            if (!emergency && buyerValue <= sellerValue * (1 + MinValueGap) + 0.01) break;
            if (sellerValue <= 0 || buyerValue <= 0) break;
            units++; mineTotal += mineValue; price = (sellerValue + buyerValue) / 2;
        }
        // L'expéditeur paie le voyage : seul son propre gain peut le justifier, pas celui offert au partenaire.
        double gain = sale ? units * price - mineTotal : mineTotal - units * price;
        return units == 0 ? Clearing.None : new(units, price, Math.Max(0, gain));
    }

    /// <summary>Bilan des biens réellement revenus et des pièces payées, aux coûts connus au départ. Les pertes restent à charge du voyage.</summary>
    private static (double? Gain, double Cost) Outcome(WorldState world, Caravan trip)
    {
        double absence = Math.Max(0, world.Clock.Ticks - trip.DepartTicks) / (double)TimeConstants.TicksPerDay
            * trip.Traders.Count * WorkHoursPerDay;
        double purchases = trip.Settled.Where(l => !l.IsSale).Sum(l => l.Total);
        if (trip.DepartureValues is null) return (null, trip.ProvisionCostHours + absence + purchases);
        double charged = 0, imports = 0;
        foreach (TradeLine value in trip.DepartureValues)
        {
            int sold = trip.Settled.Where(l => l.IsSale && l.Good == value.Good).Sum(l => l.Units);
            long returned = (long)trip.Inventory.Get(value.Good) + trip.Provisions.Get(value.Good);
            long recovered = Math.Min(returned, Math.Max(0, value.Units - sold));
            charged += (value.Units - recovered) * value.UnitPrice;
            imports += (returned - recovered) * value.UnitPrice;
        }
        double cost = charged + purchases + absence;
        return (trip.Settled.Where(l => l.IsSale).Sum(l => l.Total) + imports - cost, cost);
    }

    private static void ReceiveReport(WorldState world, Caravan trip, double costHours)
    {
        if (trip.ReportTicks < 0) return;
        Remember(world, trip.From, trip.To, trip.ReturnOffers, trip.ReportTicks, trip.ReturnBudget);
        SupplierMemory memory = Memory(world, trip.From, trip.To);
        int expected = trip.Plan.Sum(l => l.Units), delivered = trip.Settled.Sum(l => l.Units);
        if (!trip.ContactOnly && !trip.Aborted)
        {
            if (expected > 0 && delivered >= expected) memory.Deliveries++;
            else memory.Refusals++;
        }
        if (world.Clock.Ticks > trip.PlannedReturnTicks + TimeConstants.TicksPerHour) memory.Delays++;
        memory.LastTripDays = (world.Clock.Ticks - trip.DepartTicks) / (double)TimeConstants.TicksPerDay;
        memory.LastCostHours = costHours;
    }
}
