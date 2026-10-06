using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>L'ordre des besoins d'un établissement : la survie passe toujours avant le reste.</summary>
public enum SupplyPriority { Survival = 0, Regular = 1, Tools = 2, Industrial = 3, Expansion = 4, Comfort = 5 }

/// <summary>
/// Un besoin d'un établissement : des vivres (une nutrition) ou une quantité d'un bien, avec sa priorité, son échéance et sa raison.
/// Un besoin n'est pas un stock : rien n'arrive avant qu'un voyage l'apporte.
/// </summary>
public sealed record SupplyNeed(int SettlementId, ResourceType? Good, int Units, decimal Nutrition, SupplyPriority Priority, long DueTicks, string Reason);

/// <summary>
/// La logistique interne d'une colonie à plusieurs établissements : relever les besoins de chacun, les classer, puis charger au village qui a du
/// surplus de quoi les couvrir dans la limite d'une charge réelle. Rapprocher un surplus d'un manque ne crée aucun bien : les marchandises voyagent,
/// avec leurs provisions. Les ruptures qui durent déclenchent l'évacuation plutôt qu'une promesse de livraison fictive.
/// </summary>
public static class LogisticsPlanner
{
    /// <summary>Jours de nourriture qu'un établissement garde en réserve avant qu'on s'en inquiète.</summary>
    public const int SurvivalDays = 5;
    public const int RegularDays = 10;

    /// <summary>Jours de nourriture que le village qui donne garde pour lui.</summary>
    public const int SourceReserveDays = 4;

    /// <summary>Jours de suite sans une journée de nourriture avant d'évacuer un camp qu'on n'arrive pas à ravitailler.</summary>
    public const int ProlongedShortageDays = 3;

    private const int CapacityPerPerson = 40;

    /// <summary>Jours minimum entre deux livraisons non urgentes vers un même établissement, et charge minimale qui justifie un voyage.</summary>
    public const int RegularIntervalDays = 4;
    public const double MinWorthwhileWeight = 20;

    private static decimal DailyNutrition(int people) => Math.Max(1, people) * (decimal)Trade.TravelerNutritionPerDay;

    /// <summary>Les besoins d'un établissement, du plus pressé au moins pressé.</summary>
    public static List<SupplyNeed> Needs(WorldState world, Colony owner, Settlement place)
    {
        using var scope = owner.UseSettlement(place);
        var needs = new List<SupplyNeed>();
        int people = place.Population.Count;
        if (people == 0) return needs;
        long now = world.Clock.Ticks;
        decimal daily = DailyNutrition(people), have = place.Stock.AvailableNutrition;
        long dueFood = now + (long)(have / daily * TimeConstants.TicksPerDay);
        if (have < daily * SurvivalDays)
            needs.Add(new SupplyNeed(place.Id, null, 0, daily * SurvivalDays - have, SupplyPriority.Survival, dueFood, "nourriture"));
        else if (have < daily * RegularDays)
            needs.Add(new SupplyNeed(place.Id, null, 0, daily * RegularDays - have, SupplyPriority.Regular, dueFood, "nourriture régulière"));
        int wood = place.Stock.Available(ResourceType.Wood), heating = (int)ColonyBrain.HeatingTarget(owner, world.Clock.Season) + 10;
        if (wood < Math.Max(12, heating))
            needs.Add(new SupplyNeed(place.Id, ResourceType.Wood, Math.Max(12, heating) - wood, 0,
                wood < 12 ? SupplyPriority.Survival : SupplyPriority.Regular, now + TimeConstants.TicksPerDay * (wood < 12 ? 2 : 6), "chauffage"));
        int tools = ToolChain.ToolsWanted(owner) - place.Stock.Get(ResourceType.Tools);
        if (tools > 0) needs.Add(new SupplyNeed(place.Id, ResourceType.Tools, tools, 0, SupplyPriority.Tools, now + TimeConstants.TicksPerDay * 10, "outils"));
        foreach (ResourceType good in Economy.Tradable.Where(g => ResourceCatalog.Nutrition(g) == 0 && g is not (ResourceType.Wood or ResourceType.Tools or ResourceType.Stone)))
        {
            int shortage = Economy.Shortage(owner, good);
            if (shortage > 0) needs.Add(new SupplyNeed(place.Id, good, shortage, 0, SupplyPriority.Industrial, now + TimeConstants.TicksPerDay * 15, "intrants d'atelier"));
        }
        foreach (Building site in place.Buildings.Where(b => !b.IsComplete))
        {
            if (site.StoneStillToBring - place.Stock.Get(ResourceType.Stone) > 0)
                needs.Add(new SupplyNeed(place.Id, ResourceType.Stone, site.StoneStillToBring - place.Stock.Get(ResourceType.Stone), 0, SupplyPriority.Expansion, now + TimeConstants.TicksPerDay * 20, "chantier"));
            if (site.WoodStillToBring - place.Stock.Get(ResourceType.Wood) > 0)
                needs.Add(new SupplyNeed(place.Id, ResourceType.Wood, site.WoodStillToBring - place.Stock.Get(ResourceType.Wood), 0, SupplyPriority.Expansion, now + TimeConstants.TicksPerDay * 20, "chantier"));
        }
        return Uncovered(world, place, needs).OrderBy(n => n.Priority).ThenBy(n => n.DueTicks).ThenBy(n => n.Good is null ? -1 : (int)n.Good).ToList();
    }

    /// <summary>
    /// Ce qui manque encore une fois retranché ce qu'une livraison en route apporte déjà : un besoin couvert par une cargaison qui roule n'est jamais compté deux fois
    /// (ni chargé une seconde fois pour remplir une charrette).
    /// </summary>
    private static IEnumerable<SupplyNeed> Uncovered(WorldState world, Settlement place, List<SupplyNeed> needs)
    {
        var arriving = world.Caravans.Where(t => t.Purpose == TerritorialPurpose.Supply && t.ToSettlementId == place.Id && !t.Delivered && t.State != CaravanState.Home).ToList();
        if (arriving.Count == 0)
            return needs;
        var covered = new Dictionary<ResourceType, int>();
        decimal nutrition = 0;
        foreach (Caravan trip in arriving)
            foreach ((ResourceType good, int units) in trip.Inventory.Amounts)
            {
                covered[good] = covered.GetValueOrDefault(good) + units;
                nutrition += units * ResourceCatalog.Nutrition(good);
            }
        var result = new List<SupplyNeed>();
        foreach (SupplyNeed need in needs)
        {
            if (need.Good is null)
            {
                decimal take = Math.Min(nutrition, need.Nutrition);
                nutrition -= take;
                if (need.Nutrition - take > 0) result.Add(need with { Nutrition = need.Nutrition - take });
            }
            else
            {
                int take = Math.Min(covered.GetValueOrDefault(need.Good.Value), need.Units);
                covered[need.Good.Value] = covered.GetValueOrDefault(need.Good.Value) - take;
                if (need.Units - take > 0) result.Add(need with { Units = need.Units - take });
            }
        }
        return result;
    }

    /// <summary>Ce que le village qui donne peut céder sans se priver : surplus au-delà de ses propres besoins et de ses réserves de survie.</summary>
    private static int Spare(WorldState world, Colony owner, Settlement source, ResourceType good)
    {
        using var scope = owner.UseSettlement(source);
        return good switch
        {
            ResourceType.Wood => Math.Max(0, source.Stock.Available(good) - ((int)ColonyBrain.HeatingTarget(owner, world.Clock.Season) + 10)),
            ResourceType.Stone => Math.Max(0, source.Stock.Available(good) - ColonyBrain.StoneReserveTarget),
            ResourceType.Tools => Math.Max(0, source.Stock.Available(good) - ToolChain.ToolsWanted(owner)),
            _ => Economy.Surplus(owner, good),
        };
    }

    /// <summary>Un chargement qui couvre les besoins du plus pressé au moins pressé dans la limite de la charge, avec des vivres variés pour les besoins de nutrition.</summary>
    public static Dictionary<ResourceType, int>? Cargo(WorldState world, Colony owner, Settlement source, IReadOnlyList<SupplyNeed> needs, int people, double routeDays) =>
        Cargo(world, owner, source, needs, people, routeDays, out _);

    /// <summary>Comme ci-dessus, et renvoie la charge utile du voyage (la capacité dont les vivres du trajet sont déjà déduits), pour mesurer son remplissage.</summary>
    public static Dictionary<ResourceType, int>? Cargo(WorldState world, Colony owner, Settlement source, IReadOnlyList<SupplyNeed> needs, int people, double routeDays, out double usefulCapacity)
    {
        // Les vivres du voyage pèsent sur la charge : on les réserve d avance.
        double capacity = CapacityPerPerson * people * Trade.SupplyCapacityFactor(owner) - 2 * routeDays * people * (double)Trade.TravelerNutritionPerDay / (double)Stockpile.BreadMealValue - 4;
        usefulCapacity = Math.Max(0, capacity);
        var cargo = new Dictionary<ResourceType, int>();
        decimal foodSpare;
        using (owner.UseSettlement(source))
            foodSpare = Math.Max(0m, source.Stock.AvailableNutrition - DailyNutrition(source.Population.Count) * SourceReserveDays
                - (decimal)(2 * routeDays + 1) * people * (decimal)Trade.TravelerNutritionPerDay);
        foreach (SupplyNeed need in needs)
        {
            if (need.Good is null)
            {
                decimal wanted = Math.Min(need.Nutrition, foodSpare);
                // Les denrées qui se gâtent d'abord, puis ce qui se conserve ; ce qui est déjà promis plus haut dans le chargement n'est pas repris.
                foreach (ResourceType food in ResourceCatalog.TravelFood)
                {
                    if (wanted <= 0 || capacity <= 0) break;
                    decimal each = ResourceCatalog.Nutrition(food);
                    int available = Math.Max(0, source.Stock.Available(food) - cargo.GetValueOrDefault(food));
                    int units = (int)Math.Min(Math.Min((decimal)available, decimal.Ceiling(wanted / each)), (decimal)Math.Floor(capacity / ResourceCatalog.Weight(food)));
                    if (units <= 0) continue;
                    cargo[food] = cargo.GetValueOrDefault(food) + units;
                    capacity -= units * ResourceCatalog.Weight(food);
                    wanted -= units * each; foodSpare -= units * each;
                }
            }
            else
            {
                int spare = Spare(world, owner, source, need.Good.Value) - cargo.GetValueOrDefault(need.Good.Value);
                int units = (int)Math.Min(Math.Min(need.Units, spare), Math.Floor(capacity / ResourceCatalog.Weight(need.Good.Value)));
                if (units <= 0) continue;
                cargo[need.Good.Value] = cargo.GetValueOrDefault(need.Good.Value) + units;
                capacity -= units * ResourceCatalog.Weight(need.Good.Value);
            }
        }
        return cargo.Count == 0 ? null : cargo;
    }

    /// <summary>Le ravitaillement du jour : le besoin le plus urgent d'un autre établissement, couvert dans la limite de la charge d'un voyage.</summary>
    internal static bool PlanSupply(WorldState world, Colony owner, Settlement source)
    {
        const int people = 2;
        var candidates = owner.Settlements.Where(s => s != source && s.Status == SettlementStatus.Active)
            .Select(s => (Place: s, Needs: Needs(world, owner, s))).Where(p => p.Needs.Count > 0)
            .OrderBy(p => p.Needs[0].Priority).ThenBy(p => p.Needs[0].DueTicks).ThenBy(p => p.Place.Id).ToList();
        SupplyBatching.Prune(source, candidates.Select(p => p.Place.Id));
        foreach ((Settlement place, List<SupplyNeed> needs) in candidates)
        {
            var route = world.WorldMap.TravelRoute(source.RegionTileIndex, place.RegionTileIndex, Trade.HostileRegions(world, owner));
            if (route is null) continue;
            double days = route.Cost / WorldMap.CaravanTilesPerDay;
            long arrival = world.Clock.Ticks + (long)(days * TimeConstants.TicksPerDay);
            // Une livraison de survie qui arriverait trop tard est quand même envoyée (mieux vaut tard), mais signalée.
            if (needs[0].Priority == SupplyPriority.Survival && source.Population.Count > 0 && arrival > needs[0].DueTicks)
                ColonyBrain.Say(owner, world.Clock, $"{place.Name} manque de {needs[0].Reason} : la livraison prévue arrivera après la rupture.");
            Dictionary<ResourceType, int>? cargo = Cargo(world, owner, source, needs, people, days, out double useful);
            if (cargo is null) continue;
            // Une urgence part sans seuil ; une attente d'un jour de plus qui ferait arriver le chargement après la rupture est interdite, quelle que soit la priorité nominale.
            bool urgent = needs[0].Priority == SupplyPriority.Survival || arrival + SupplyBatching.MaxWaitDays * TimeConstants.TicksPerDay > needs[0].DueTicks;
            if (!SupplyBatching.ShouldDepart(source, place, world.Clock.Ticks, urgent, ResourceCatalog.WeightOf(cargo), useful, MinWorthwhileWeight, out _)) continue;
            // Au départ : les stocks sont revalidés par le voyage lui-même ; l'attente ne s'efface que si le départ réussit.
            if (TerritorialTravel.Depart(world, source, place.RegionTileIndex, TerritorialPurpose.Supply, cargo, people, place) is not null)
            {
                place.LastSupplyTicks = world.Clock.Ticks;
                SupplyBatching.Clear(source, place);
                return true;
            }
        }
        return false;
    }

    /// <summary>Le retour de surplus d'un camp : ce dont le village principal manque et que le camp peut céder, sans toucher à ses propres vivres.</summary>
    internal static bool PlanReturn(WorldState world, Colony owner, Settlement main)
    {
        foreach (Settlement camp in owner.Settlements.Where(s => s != main && s.Status == SettlementStatus.Active && s.Population.Count > 2).OrderBy(s => s.Id))
        {
            var wanted = new List<SupplyNeed>();
            using (owner.UseSettlement(main))
                foreach (ResourceType good in Economy.Tradable.Where(g => ResourceCatalog.Nutrition(g) == 0 && g != ResourceType.Wood))
                {
                    // Ce dont le village manque, et ce que son commerce extérieur voudrait vendre : le commerce passe par le village principal.
                    int shortage = Economy.Shortage(owner, good) + Math.Max(0, owner.ExportInterest.GetValueOrDefault(good) - main.Stock.Get(good));
                    if (shortage > 0) wanted.Add(new SupplyNeed(main.Id, good, shortage, 0, SupplyPriority.Industrial, 0, "surplus du camp"));
                }
            if (wanted.Count == 0) continue;
            var route = world.WorldMap.TravelRoute(camp.RegionTileIndex, main.RegionTileIndex, Trade.HostileRegions(world, owner));
            if (route is null) continue;
            Dictionary<ResourceType, int>? cargo = Cargo(world, owner, camp, wanted, 2, route.Cost / WorldMap.CaravanTilesPerDay, out double useful);
            if (cargo is null)
            {
                SupplyBatching.Clear(camp, main);
                continue;
            }
            // Un surplus de camp n'est jamais urgent : on groupe la charge (70 % remplie, ou un jour d'attente au plus).
            if (SupplyBatching.ShouldDepart(camp, main, world.Clock.Ticks, urgent: false, ResourceCatalog.WeightOf(cargo), useful, MinWorthwhileWeight, out _)
                && TerritorialTravel.Depart(world, camp, main.RegionTileIndex, TerritorialPurpose.Supply, cargo, 2, main) is not null)
            {
                SupplyBatching.Clear(camp, main);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Chaque jour : compte les jours de rupture de nourriture de chaque camp. Si la rupture dure et qu'aucune livraison n'a pu la combler,
    /// la colonie évacue le camp vers le village principal (habitants et biens transportables) plutôt que d'attendre un ravitaillement qui ne vient pas.
    /// </summary>
    internal static void WatchShortage(WorldState world, Settlement place)
    {
        Colony owner = place.Owner;
        if (place == owner.PrimarySettlement || place.Status != SettlementStatus.Active || place.Population.Count == 0) { place.ShortageDays = 0; return; }
        bool short1 = place.Stock.AvailableNutrition < DailyNutrition(place.Population.Count);
        place.ShortageDays = short1 ? place.ShortageDays + 1 : 0;
        if (place.ShortageDays < ProlongedShortageDays || world.Caravans.Any(t => t.From == owner && t.ToSettlementId == place.Id && t.State != CaravanState.Home && t.Purpose == TerritorialPurpose.Supply)) return;
        var goods = new Dictionary<ResourceType, int>();
        double room = place.Population.Count * 25;
        foreach (var item in place.Stock.Amounts.OrderBy(p => p.Key))
        { int units = Math.Min(item.Value, (int)(room / ResourceCatalog.Weight(item.Key))); if (units > 0) { goods[item.Key] = units; room -= units * ResourceCatalog.Weight(item.Key); } }
        if (TerritorialTravel.Depart(world, place, owner.PrimarySettlement.RegionTileIndex, TerritorialPurpose.Evacuation, goods, place.Population.Count, owner.PrimarySettlement) is not null)
            ColonyBrain.Say(owner, world.Clock, $"{place.Name} n'a plus de quoi manger depuis {place.ShortageDays} jours et aucun ravitaillement n'arrive : ses habitants rentrent au village.");
    }
}
