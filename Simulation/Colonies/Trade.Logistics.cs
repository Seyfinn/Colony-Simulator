using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

public static partial class Trade
{
    /// <summary>Les adultes en voyage mangent autant qu'à pied sur la carte ; un jour de marge couvre un retard modeste.</summary>
    public const float TravelerNutritionPerDay = 24f * 0.04f;
    public const float ProvisionMarginDays = 1f;
    public const float EmergencyFoodDays = 2f;

    public static bool NeedsEmergencyFood(Colony colony) =>
        colony.Stock.AvailableNutrition < Math.Max(1, colony.PresentMembers.Count) * (decimal)(TravelerNutritionPerDay * EmergencyFoodDays);

    private static List<(ResourceType Resource, int Amount)>? ProvisionLoad(Colony colony, double tripDays,
        IEnumerable<TradeLine> lines)
    {
        decimal wanted = (decimal)((tripDays + ProvisionMarginDays) * TradersPerCaravan * TravelerNutritionPerDay);
        var sales = lines.Where(l => l.IsSale).GroupBy(l => l.Good).ToDictionary(g => g.Key, g => g.Sum(l => l.Units));
        return ProvisionLoad(colony, wanted, sales);
    }

    /// <summary>Des vivres variés pour la nutrition demandée, sans toucher aux marchandises déjà réservées au chargement ; null si le stock ne suffit pas.</summary>
    internal static List<(ResourceType Resource, int Amount)>? ProvisionLoad(Colony colony, decimal wanted, IReadOnlyDictionary<ResourceType, int> reserved, bool partial = false)
    {
        var sales = reserved;
        List<(ResourceType, int)> load = [];
        foreach (ResourceType food in new[] { ResourceType.Food, ResourceType.Fish }.Concat(ResourceCatalog.TravelFood))
        {
            int available = Math.Max(0, colony.Stock.Available(food) - sales.GetValueOrDefault(food));
            decimal nutrition = ResourceCatalog.Nutrition(food);
            int take = (int)Math.Min(available, decimal.Ceiling(wanted / nutrition));
            if (take <= 0) continue;
            load.Add((food, take));
            wanted -= take * nutrition;
            if (wanted <= 0) return load;
        }
        // Une évacuation part avec ce qu'il reste, même s'il n'y a pas de quoi tout le trajet : rester serait pire.
        return partial ? load : null;
    }

    private static double ProvisionWeight(IEnumerable<(ResourceType Resource, int Amount)> load) =>
        load.Sum(p => p.Amount * ResourceCatalog.Weight(p.Resource));

    private static double PurchaseCoins(IEnumerable<TradeLine> lines) =>
        lines.Where(l => !l.IsSale).Sum(l => Math.Ceiling(l.Total));

    private static bool LoadFits(IReadOnlyList<TradeLine> lines,
        IReadOnlyList<(ResourceType Resource, int Amount)> provisions, int capacity)
    {
        double wallet = PurchaseCoins(lines);
        double saleWeight = lines.Where(l => l.IsSale).Sum(l => l.Units * ResourceCatalog.Weight(l.Good));
        double purchaseWeight = lines.Where(l => !l.IsSale).Sum(l => l.Units * ResourceCatalog.Weight(l.Good));
        double receipts = lines.Where(l => l.IsSale).Sum(l => Math.Ceiling(l.Total));
        // Au retour, une marchandise invendue peut occuper la place prévue pour les achats.
        return saleWeight + ProvisionWeight(provisions) + wallet * ResourceCatalog.Weight(ResourceType.Coins) <= capacity + 0.0001
            && Math.Max(saleWeight, purchaseWeight) + ProvisionWeight(provisions)
                + (wallet + receipts) * ResourceCatalog.Weight(ResourceType.Coins) <= capacity + 0.0001;
    }

    private static bool SafeDeparture(Colony colony, IReadOnlyList<TradeLine> lines,
        IReadOnlyList<(ResourceType Resource, int Amount)> provisions)
    {
        decimal nutritionLeaving = provisions.Sum(p => p.Amount * ResourceCatalog.Nutrition(p.Resource))
            + lines.Where(l => l.IsSale).Sum(l => l.Units * ResourceCatalog.Nutrition(l.Good));
        decimal homeDay = Math.Max(1, colony.PresentMembers.Count - TradersPerCaravan) * (decimal)TravelerNutritionPerDay;
        return colony.Stock.AvailableNutrition - nutritionLeaving >= homeDay;
    }

    /// <summary>Les besoins hors carte sont entretenus une fois par heure ; les voyageurs sur la carte gardent leur actualisation locale.</summary>
    private static void MaintainTravelers(WorldState world, Caravan caravan)
    {
        long elapsed = world.Clock.Ticks - caravan.LastNeedsTicks;
        for (long tick = caravan.LastNeedsTicks / TimeConstants.TicksPerDay + 1; tick <= world.Clock.TotalDays; tick++)
        {
            caravan.Inventory.AgeMeat();
            caravan.Inventory.SpoilMeat(3, 0.5f);
            caravan.Provisions.AgeMeat();
            caravan.Provisions.SpoilMeat(3, 0.5f);
        }
        if (elapsed <= 0) return;
        float hours = elapsed / TimeConstants.TicksPerHour;
        foreach (Colonist trader in caravan.Traders)
        {
            if (caravan.From.Transients.Contains(trader)) continue;
            trader.Needs.Food -= 0.04f * hours;
            trader.Needs.Rest += (world.Clock.IsNight ? 0.12f : -0.045f) * hours;
            if (trader.Needs.Food <= 0.4f && caravan.Provisions.TryTakeMeal(out float value))
                trader.Needs.Food += value;
            trader.Needs.Clamp();
        }
        caravan.LastNeedsTicks = world.Clock.Ticks;
    }
}
