using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Une ligne d'un échange : tant d'unités d'un bien, à tel prix l'unité, vendues ou achetées par la colonie qui envoie la caravane.</summary>
public sealed record TradeLine(ResourceType Good, int Units, double UnitPrice, bool IsSale)
{
    public double Total => Units * UnitPrice;
}

/// <summary>Ce qu'une colonie compte échanger avec une autre, et ce que cela lui rapporte comparé à ce que cela coûte.</summary>
public sealed record TradePlan(Colony From, Colony To, IReadOnlyList<TradeLine> Lines, double GainHours, double CostHours, double TripDays)
{
    /// <summary>Bilan en pièces pour la colonie qui envoie la caravane (positif : elle encaisse).</summary>
    public double NetCoins => Lines.Sum(l => l.IsSale ? l.Total : -l.Total);
    public bool Emergency { get; init; }
    public bool ContactOnly { get; init; }
}

/// <summary>Le compte rendu d'un voyage achevé, gardé par chaque colonie pour s'en souvenir.</summary>
public sealed record TradeRecord(long Ticks, string Partner, IReadOnlyList<TradeLine> Lines, int NetCoins, bool WeSent)
{
    public double? GainHours { get; init; }
    public double? CostHours { get; init; }
    public double? ExpectedGainHours { get; init; }
}

public enum CaravanState { Outbound, Returning, Home }

/// <summary>
/// Une caravane : quelques colons partent avec des marchandises (et des pièces pour acheter), marchent jusqu'à
/// l'autre colonie, concluent l'échange, puis rentrent avec ce qu'ils rapportent. Pendant ce temps, ils manquent à leur colonie.
/// Les pièces sont physiques : elles voyagent avec la caravane.
/// </summary>
public sealed class Caravan
{
    internal Caravan(Colony from, Colony to, List<Colonist> traders, IReadOnlyList<TradeLine> lines, double planGainHours,
        long departTicks, long arriveTicks, long returnTicks)
    {
        PlanGainHours = planGainHours;
        From = from;
        To = to;
        Traders = traders;
        Plan = lines.ToArray();
        DepartTicks = departTicks;
        ArriveTicks = arriveTicks;
        ReturnTicks = returnTicks;
        PlannedReturnTicks = returnTicks;
        LastRouteTicks = departTicks;
        LastNeedsTicks = departTicks;
        _inventory = new Stockpile(Cargo);
        _provisions = new Stockpile();
    }

    public int Id { get; internal set; }
    public int FromSettlementId { get; internal set; }
    public int ToSettlementId { get; internal set; }
    public TerritorialPurpose Purpose { get; internal set; }
    public int TargetRegion { get; internal set; }
    public bool Delivered { get; internal set; }
    public List<GodColony.Simulation.Map.DepositKnowledge>? SurveyReports { get; internal set; }

    /// <summary>Pour une prospection : temps réellement travaillé sur place, et profondeur de sondage qui en résulte.</summary>
    public long WorkedTicks { get; internal set; }

    /// <summary>Pour un aménagement de route : l'arête (régions voisines) et le niveau visé.</summary>
    public int RoadEdgeA { get; internal set; }
    public int RoadEdgeB { get; internal set; }
    public int RoadLevel { get; internal set; }
    public int SurveyReach { get; internal set; }
    public Colony From { get; }
    public Colony To { get; }
    public IReadOnlyList<Colonist> Traders { get; }
    public IReadOnlyList<TradeLine> Plan { get; }
    public long DepartTicks { get; }
    public long ArriveTicks { get; internal set; }
    public long ReturnTicks { get; internal set; }
    public long PlannedReturnTicks { get; internal set; }
    public bool ContactOnly { get; internal set; }
    public bool Aborted { get; internal set; }
    public double ProvisionCostHours { get; internal set; }
    /// <summary>Quantités chargées et coûts unitaires au départ ; null pour un ancien voyage sans bilan reconstituable.</summary>
    public IReadOnlyList<TradeLine>? DepartureValues { get; internal set; }
    public List<MarketOffer> OutboundOffers { get; internal set; } = [];
    public List<MarketOffer> ReturnOffers { get; internal set; } = [];
    public long ReportTicks { get; internal set; } = -1;
    public int OutboundBudget { get; internal set; }
    public int ReturnBudget { get; internal set; }
    public WorldRoute? Route { get; internal set; }
    public int RouteIndex { get; internal set; }
    public double SegmentTravelCost { get; internal set; }
    internal long LastRouteTicks { get; set; }
    internal long RestUntilTicks { get; set; }
    internal int RouteRevision { get; set; } = -1;
    public CaravanState State { get; internal set; } = CaravanState.Outbound;

    /// <summary>Le travail que l'échange devait épargner, si tout se passait comme prévu.</summary>
    public double PlanGainHours { get; }

    /// <summary>Les pièces emportées au départ (pour calculer ce que le voyage a vraiment rapporté).</summary>
    public int CoinsAtDeparture { get; internal set; }

    /// <summary>Ce qu'elle transporte en ce moment : au départ, les marchandises à vendre ; au retour, ce qu'elle rapporte.</summary>
    private Stockpile? _inventory;
    private Stockpile? _provisions;
    public Stockpile Inventory => _inventory ??= new Stockpile(Cargo);
    public Stockpile Provisions => _provisions ??= new Stockpile();
    public Dictionary<ResourceType, int> Cargo { get; } = [];
    internal long LastNeedsTicks { get; set; }
    public string? BlockedReason { get; internal set; }
    public double LoadWeight => ResourceCatalog.WeightOf(Cargo) + ResourceCatalog.WeightOf(Provisions.Amounts);

    /// <summary>Les pièces qu'elle porte : de quoi acheter à l'aller, le produit des ventes au retour.</summary>
    public int Coins
    {
        get => Cargo.GetValueOrDefault(ResourceType.Coins);
        internal set => Cargo[ResourceType.Coins] = value;
    }

    /// <summary>
    /// Où se trouve la caravane sur la route, de 0 (à la colonie qui l'envoie) à 1 (chez l'hôte) :
    /// elle avance à l'aller, attend l'échange, puis revient.
    /// </summary>
    public float RoutePosition(long nowTicks)
    {
        if (Route is { } route && route.Cost > 0)
            return Math.Clamp((float)((route.Cumulative[Math.Min(RouteIndex, route.Cumulative.Count - 1)] + SegmentTravelCost) / route.Cost), 0, 1);
        if (nowTicks <= ArriveTicks)
            return Math.Clamp((nowTicks - DepartTicks) / (float)Math.Max(1, ArriveTicks - DepartTicks), 0f, 1f);
        long backStart = ReturnTicks - (ArriveTicks - DepartTicks);
        if (nowTicks <= backStart)
            return 1f; // elle échange chez l'hôte
        return Math.Clamp(1f - (nowTicks - backStart) / (float)Math.Max(1, ReturnTicks - backStart), 0f, 1f);
    }

    /// <summary>Avancement du voyage entier, de 0 (départ) à 1 (retour).</summary>
    public float Progress(long nowTicks) => Route is null
        ? Math.Clamp((nowTicks - DepartTicks) / (float)Math.Max(1, ReturnTicks - DepartTicks), 0f, 1f)
        : State == CaravanState.Home ? 1 : (State == CaravanState.Outbound ? 0 : 0.5f) + RoutePosition(nowTicks) * 0.5f;

    internal List<TradeLine> Settled { get; } = [];
}

/// <summary>
/// Le commerce entre colonies : chaque matin, une colonie compare ses valeurs à celles de ses voisines et, si un
/// échange rapporte nettement plus qu'il ne coûte en travail de caravane, envoie des colons le conclure.
/// Le prix est négocié au milieu des deux valeurs : les deux colonies y gagnent.
/// </summary>
public static partial class Trade
{
    /// <summary>Heure à laquelle les colonies réfléchissent à leurs échanges.</summary>
    public const int PlanningHour = 9;

    public const int TradersPerCaravan = 2;

    /// <summary>Charge maximale d'une caravane, en unités de poids (provisions et pièces comprises).</summary>
    public const int CarryCapacity = 36;

    /// <summary>Les étals du plus grand marché préparent davantage de marchandises pour chaque voyage.</summary>
    public static int CapacityOf(Colony from, Colony to)
    {
        int capacity = CarryCapacity + Math.Max(MarketBonus(from), MarketBonus(to));
        // La monnaie frappée allège les comptes : on emporte un quart de plus.
        return Knowledge.Has(from, Discovery.Coinage) ? (int)(capacity * Knowledge.CoinageCapacityFactor) : capacity;
    }

    private static int MarketBonus(Colony colony) => colony.Buildings
        .Where(b => b.Type == BuildingType.Market && b.IsComplete).Sum(b => CarryCapacity * b.Width * b.Height / 24);

    /// <summary>Entre alliés, un échange n'a pas besoin de rapporter plus que le voyage ne coûte.</summary>
    public const double AlliedGainOverCost = 1.0;

    /// <summary>Chaque niveau de négoce des marchands rabat ce pourcentage du coût d'un voyage (au plus 40 %).</summary>
    private const double TradingSkillDiscount = 0.02;

    /// <summary>Heures de travail utiles par jour et par colon : le coût d'un colon en voyage.</summary>
    public const double WorkHoursPerDay = 8;

    /// <summary>Le gain doit dépasser le coût du voyage d'au moins ce facteur : on ne risque pas la route pour une broutille.</summary>
    public const double RequiredGainOverCost = 1.25;

    /// <summary>Écart de valeur minimal (en proportion) pour qu'un échange vaille la peine.</summary>
    public const double MinValueGap = 0.15;

    /// <summary>Jours à attendre après un départ avant d'en préparer un autre.</summary>
    public const int DaysBetweenCaravans = 2;

    /// <summary>Chaque point de rancune ajoute ce multiple du coût de voyage à ce qu'un échange doit rapporter (un barrage : trois fois plus).</summary>
    public const double GrudgeCostFactor = 2.0;

    /// <summary>Colons minimum pour oser en envoyer en voyage.</summary>
    public const int MinColonistsToTrade = 6;

    /// <summary>Unités qu'une colonie veut produire au plus pour l'exportation d'un bien sur son horizon, et voyages comptés pour les transporter.</summary>
    private const int ExportHorizonUnits = 24, ExportTrips = 2;
    private const int MaxRecords = 20;

    // ---------- Préparation ----------

    /// <summary>
    /// Le meilleur échange possible de <paramref name="from"/> vers <paramref name="to"/> : on vend ce qu'on a en trop
    /// à celui qui en manque et en estime plus la valeur, on achète ce qu'on manque à celui qui en a trop et l'estime moins.
    /// Renvoie null si rien ne rapporte assez.
    /// </summary>
    public static TradePlan? Plan(WorldState world, Colony from, Colony to, bool emergency = false)
    {
        if (from == to || !world.Colonies.Contains(from) || !world.Colonies.Contains(to)
            || !world.WorldMap.Connected(from, to) || Diplomacy.AtWar(world, from, to))
            return null;
        WorldRoute? route = RouteForTrade(world, from, world.WorldMap.TileOf(from), world.WorldMap.TileOf(to));
        if (route is null) return null;
        float tripDays = 2f * route.Cost / WorldMap.CaravanTilesPerDay + 0.5f;
        double bargain = Math.Min(0.4, TradingSkillDiscount * Specialties.TraderLevel(PickTraders(from, emergency)));
        double cost = TradersPerCaravan * tripDays * WorkHoursPerDay * (1 - bargain)
            * (Knowledge.Has(from, Discovery.Coinage) ? Knowledge.CoinageCostFactor : 1.0);
        int capacity = CapacityOf(from, to);

        SupplierMemory? memory = Suppliers(world, from).FirstOrDefault(m => m.Supplier == to);
        if (memory is null || memory.AgeDays(world.Clock.Ticks) > OfferLifetimeDays)
        {
            var food = ProvisionLoad(from, tripDays, []);
            return food is not null && SafeDeparture(from, [], food) && LoadFits([], food, capacity)
                ? new TradePlan(from, to, [], 0, cost + food.Sum(p => p.Amount * Economy.Cost(from, p.Resource)), tripDays) { ContactOnly = true } : null;
        }
        var candidates = new List<(TradeLine Line, double GainPerUnit)>();
        foreach (MarketOffer offer in memory.Offers)
        {
            ResourceType good = offer.Good;
            Clearing sale = emergency ? Clearing.None : KnownClearing(world, from, offer, true, capacity, false);
            if (sale.Units > 0)
                candidates.Add((new TradeLine(good, sale.Units, sale.UnitPrice, IsSale: true), sale.GainHours / sale.Units));

            // J'achète : chaque unité vient tant que je lui accorde plus de valeur qu'eux.
            Clearing purchase = emergency && ResourceCatalog.Nutrition(good) == 0
                ? Clearing.None : KnownClearing(world, from, offer, false, capacity, emergency);
            if (purchase.Units > 0)
                candidates.Add((new TradeLine(good, purchase.Units, purchase.UnitPrice, IsSale: false), purchase.GainHours / purchase.Units));
        }
        if (candidates.Count == 0)
            return null;

        // On remplit la caravane en commençant par ce qui rapporte le plus par unité transportée.
        var lines = new List<(TradeLine Line, double Gain)>();
        double room = capacity;
        foreach ((TradeLine line, double gain) in candidates.OrderByDescending(c => c.GainPerUnit / ResourceCatalog.Weight(c.Line.Good)))
        {
            int units = Math.Min(line.Units, (int)(room / ResourceCatalog.Weight(line.Good)));
            if (units <= 0)
                continue;
            lines.Add((line with { Units = units }, gain));
            room -= units * ResourceCatalog.Weight(line.Good);
        }

        // Les pièces sont physiques : chacun ne paie que ce que contient sa bourse.
        lines = FitPurse(lines, isSale: false, available: from.Stock.Available(ResourceType.Coins));
        lines = FitPurse(lines, isSale: true, available: memory.BuyingBudget);
        List<(ResourceType Resource, int Amount)>? provisions;
        while (lines.Count > 0)
        {
            var proposed = lines.Select(l => l.Line).ToList();
            provisions = ProvisionLoad(from, tripDays, proposed);
            if (provisions is not null && LoadFits(proposed, provisions, capacity) && SafeDeparture(from, proposed, provisions))
                break;
            var last = lines[^1];
            if (last.Line.Units <= 1) lines.RemoveAt(lines.Count - 1);
            else lines[^1] = (last.Line with { Units = last.Line.Units - 1 }, last.Gain);
        }
        lines.RemoveAll(l => l.Line.Total < 1);
        if (lines.Count == 0) return null;
        provisions = ProvisionLoad(from, tripDays, lines.Select(l => l.Line))!;
        cost += provisions.Sum(p => p.Amount * Economy.Cost(from, p.Resource));

        // La rancune (d'un côté ou de l'autre) rend le voyage moins tentant : il faut qu'il rapporte bien plus.
        double grudge = Math.Max(from.GrudgeAgainst(to), to.GrudgeAgainst(from));
        double gainHours = lines.Sum(l => l.Line.Units * l.Gain);
        double required = Diplomacy.AreAllied(world, from, to) ? AlliedGainOverCost : RequiredGainOverCost;
        decimal boughtNutrition = lines.Where(l => !l.Line.IsSale).Sum(l => l.Line.Units * ResourceCatalog.Nutrition(l.Line.Good));
        if (emergency ? boughtNutrition <= provisions.Sum(p => p.Amount * ResourceCatalog.Nutrition(p.Resource))
            : gainHours < cost * required * (1 + GrudgeCostFactor * grudge))
            return null;
        return new TradePlan(from, to, lines.Select(l => l.Line).ToList(), gainHours, cost, tripDays) { Emergency = emergency };
    }

    /// <summary>Réduit les achats d'un côté (les moins rentables d'abord) jusqu'à ce que la bourse suffise.</summary>
    private static List<(TradeLine Line, double Gain)> FitPurse(List<(TradeLine Line, double Gain)> lines, bool isSale, int available)
    {
        // Les lignes d'achat de la colonie qui envoie sont payées avec ses pièces (isSale = false) ;
        // ses lignes de vente sont payées par le destinataire (isSale = true).
        var result = lines.ToList();
        double spend() => result.Where(l => l.Line.IsSale == isSale).Sum(l => Math.Ceiling(l.Line.Total));
        while (spend() > available)
        {
            int worst = -1;
            double worstGain = double.MaxValue;
            for (int i = 0; i < result.Count; i++)
                if (result[i].Line.IsSale == isSale && result[i].Gain < worstGain)
                    (worst, worstGain) = (i, result[i].Gain);
            if (worst < 0)
                break;
            TradeLine line = result[worst].Line;
            if (line.Units <= 1)
                result.RemoveAt(worst);
            else
                result[worst] = (line with { Units = line.Units - 1 }, result[worst].Gain);
        }
        return result;
    }

    /// <summary>
    /// Chaque matin : la colonie met à jour ce qu'elle voudrait produire pour ses voisines, puis envoie une caravane
    /// si un échange en vaut la peine.
    /// </summary>
    public static void Daily(WorldState world, Colony colony)
    {
        // On ne commerce qu'avec les colonies qu'une caravane peut atteindre à pied (ni mer ni sommets entre elles), et jamais avec l'ennemi.
        List<Colony> partners = world.Colonies.Where(c => c != colony && c.PresentMembers.Count > 0 && world.WorldMap.Connected(colony, c)
                && !Diplomacy.AtWar(world, colony, c))
            .OrderBy(c => world.WorldMap.Distance(colony, c)).ToList();
        if (partners.Count == 0)
            return;

        ProductionPlanner.Revise(world, colony, partners);
        UpdateExportInterest(world, colony, partners);

        long now = world.Clock.Ticks;
        bool busy = world.Caravans.Any(c => c.From == colony && c.State != CaravanState.Home);
        bool resting = now - colony.LastCaravanTicks < DaysBetweenCaravans * TimeConstants.TicksPerDay;
        bool emergency = NeedsEmergencyFood(colony);
        bool strong = colony.PresentMembers.Count >= MinColonistsToTrade && ((colony.Sensors?.SurvivalAssured ?? true) || emergency);
        if (busy || resting || !strong)
            return;

        TradePlan? best = null;
        foreach (Colony partner in partners)
            if (Plan(world, colony, partner, emergency) is { } plan && (best is null || PlanScore(world, plan) > PlanScore(world, best)))
                best = plan;
        if (best is not null)
            Depart(world, best);
    }

    /// <summary>
    /// Ce que la colonie fabriquerait volontiers en plus pour ses voisines : des outils, quand elle sait les faire
    /// moins cher qu'elles et qu'elles en manquent. C'est ainsi que la spécialisation naît du commerce.
    /// </summary>
    private static void UpdateExportInterest(WorldState world, Colony colony, List<Colony> partners)
    {
        // L'objectif d'exportation d'une filière retenue : la demande agrégée des acheteurs solvables, non déjà promise, plafonnée par le transport et un horizon.
        foreach (ResourceType good in Economy.Tradable)
        {
            if (!colony.FocusGoods.Contains(good)) { colony.ExportInterest[good] = 0; continue; }
            double mine = Economy.Cost(colony, good) * (1 + MinValueGap);
            int demand = 0, perTrip = int.MaxValue;
            foreach (Colony partner in partners)
            {
                SupplierMemory? memory = Suppliers(world, colony).FirstOrDefault(m => m.Supplier == partner);
                MarketOffer? offer = memory?.Offers.FirstOrDefault(o => o.Good == good);
                if (memory is null || memory.AgeDays(world.Clock.Ticks) > OfferLifetimeDays || offer is null
                    || offer.BuyPrice < mine + ProductionPlanner.TransportPerUnit(world, colony, partner, good)) continue;
                // Chaque acheteur compte pour ce qu'il veut et peut payer ; les commandes de plusieurs acheteurs s'additionnent.
                int solvent = (int)Math.Min(int.MaxValue, memory.BuyingBudget / Math.Max(1.0, offer.BuyPrice));
                demand += Math.Min(offer.Wanted, solvent);
                perTrip = Math.Min(perTrip, (int)Math.Max(1, CapacityOf(colony, partner) / ResourceCatalog.Weight(good)));
            }
            int promised = world.Caravans.Where(c => c.From == colony && !c.Aborted && c.Purpose == TerritorialPurpose.Commerce)
                .Sum(c => c.Plan.Where(l => l.IsSale && l.Good == good).Sum(l => l.Units));
            // Sur l'horizon, la capacité de transport est celle de quelques voyages, pas d'un seul.
            colony.ExportInterest[good] = perTrip == int.MaxValue ? 0 : Math.Clamp(demand - promised, 0, Math.Min(ExportHorizonUnits, perTrip * ExportTrips));
        }
    }

    // ---------- Départ ----------

    /// <summary>Les colons qui partent : des adultes libres, de préférence ceux dont la colonie a le moins besoin.</summary>
    private static List<Colonist> PickTraders(Colony colony, bool emergency = false) =>
        colony.PresentMembers
            .Where(m => m.Stage == LifeStage.Adult && m.Partner is null && m.PregnantUntilTicks is null && m.Transit == TransitState.None
                && m.Ailment == Ailment.None && m.Needs.Food > 0.2f
                && (!emergency || m.Sector is not (WorkSector.Food or WorkSector.Farm or WorkSector.Wood)))
            .OrderByDescending(m => m.Sector == WorkSector.Free)
            .ThenBy(m => m.Id)
            .Take(TradersPerCaravan)
            .ToList();

    /// <summary>La caravane quitte la colonie avec ses marchandises et ses pièces.</summary>
    public static Caravan? Depart(WorldState world, TradePlan plan)
    {
        if (plan.From == plan.To || !world.Colonies.Contains(plan.From) || !world.Colonies.Contains(plan.To)
            || !world.WorldMap.Connected(plan.From, plan.To) || Diplomacy.AtWar(world, plan.From, plan.To)
            || world.Caravans.Any(c => c.From == plan.From && c.State != CaravanState.Home)
            || (plan.Lines.Count == 0 && !plan.ContactOnly) || plan.Lines.Any(l => l.Units <= 0 || !double.IsFinite(l.UnitPrice) || l.UnitPrice <= 0
                || !Economy.Tradable.Contains(l.Good))
            || !double.IsFinite(plan.GainHours) || !double.IsFinite(plan.CostHours))
            return null;
        bool emergency = plan.Emergency || NeedsEmergencyFood(plan.From);
        List<Colonist> traders = PickTraders(plan.From, emergency);
        if (traders.Count < TradersPerCaravan)
            return null;

        long now = world.Clock.Ticks;
        WorldRoute? route = RouteForTrade(world, plan.From, world.WorldMap.TileOf(plan.From), world.WorldMap.TileOf(plan.To));
        if (route is null) return null;
        double oneWayDays = route.Cost / WorldMap.CaravanTilesPerDay;
        double tripDays = 2 * oneWayDays + 0.5;
        var provisions = ProvisionLoad(plan.From, tripDays, plan.Lines);
        double payment = PurchaseCoins(plan.Lines);
        if (provisions is null || payment > int.MaxValue || payment > plan.From.Stock.Available(ResourceType.Coins)
            || !LoadFits(plan.Lines, provisions, CapacityOf(plan.From, plan.To))
            || !SafeDeparture(plan.From, plan.Lines, provisions)
            || traders.Any(t => Migration.FindEdgePoint(world, plan.From, t.TileX, t.TileY) is null))
            return null;
        long oneWay = Math.Max(1, (long)Math.Ceiling(oneWayDays * TimeConstants.TicksPerDay));
        var caravan = new Caravan(plan.From, plan.To, traders, plan.Lines, plan.GainHours, now, now + oneWay, now + 2 * oneWay + TimeConstants.TicksPerDay / 2);

        caravan.ContactOnly = plan.ContactOnly;
        caravan.Route = route;
        caravan.RouteRevision = RoutingRevision(world, plan.From);
        caravan.OutboundOffers = Publish(plan.From);
        caravan.OutboundBudget = plan.From.Stock.Available(ResourceType.Coins);
        caravan.ProvisionCostHours = provisions.Sum(p => p.Amount * Economy.Cost(plan.From, p.Resource));
        caravan.DepartureValues = Economy.Tradable.Concat(provisions.Select(p => p.Resource)).Distinct()
            .Select(g => new TradeLine(g, plan.Lines.Where(l => l.IsSale && l.Good == g).Sum(l => l.Units)
                + provisions.Where(p => p.Resource == g).Sum(p => p.Amount), Economy.Cost(plan.From, g), false)).ToArray();

        // Préparer toutes les promesses avant le moindre retrait : un départ impossible ne laisse ni débit ni colon détaché.
        List<(StockReservation Reservation, Stockpile Destination)> reservations = [];
        var loads = plan.Lines.Where(l => l.IsSale).Select(l => (l.Good, l.Units, caravan.Inventory))
            .Concat(provisions.Select(p => (p.Resource, p.Amount, caravan.Provisions)))
            .Append((ResourceType.Coins, (int)payment, caravan.Inventory));
        foreach (var (resource, amount, destination) in loads)
        {
            if (amount == 0) continue;
            StockReservation? reservation = plan.From.Stock.Reserve("Départ de caravane", resource, amount, emergency ? 100 : 50, now + 1, now);
            if (reservation is null)
            {
                foreach (var held in reservations) plan.From.Stock.CancelReservation(held.Reservation);
                return null;
            }
            reservations.Add((reservation, destination));
        }
        foreach (var held in reservations)
            if (!plan.From.Stock.LoadReservation(held.Reservation, held.Destination, now))
                throw new InvalidOperationException("Le chargement préparé n'est plus disponible.");
        caravan.CoinsAtDeparture = caravan.Coins;
        world.RegisterTrip(caravan);

        // Les marchands sortent de la colonie en marchant jusqu'au bord de la carte, puis le voyage se poursuit hors écran.
        foreach (Colonist trader in traders)
        {
            ColonistAI.DetachFromColony(trader);
            if (Migration.FindEdgePoint(world, plan.From, trader.TileX, trader.TileY) is not null)
            {
                trader.Transit = TransitState.Leaving;
                plan.From.Transients.Add(trader);
            }
        }
        plan.From.LastCaravanTicks = now;
        world.Caravans.Add(caravan);

        if (plan.ContactOnly)
        {
            ColonyBrain.Say(plan.From, world.Clock, $"Des marchands partent rencontrer {plan.To.Name} pour découvrir ses offres.");
            return caravan;
        }
        string sells = Describe(plan.Lines.Where(l => l.IsSale)), buys = Describe(plan.Lines.Where(l => !l.IsSale));
        ColonyBrain.Say(plan.From, world.Clock,
            $"Une caravane part pour {plan.To.Name} : " +
            (sells.Length > 0 ? $"nous y vendons {sells}" : "") + (sells.Length > 0 && buys.Length > 0 ? ", " : "") +
            (buys.Length > 0 ? $"nous y achetons {buys}" : "") + $" (gain attendu {plan.GainHours:0} h de travail).");
        return caravan;
    }

    private static string Describe(IEnumerable<TradeLine> lines) =>
        string.Join(", ", lines.Select(l => $"{l.Units} {GoodName(l.Good, l.Units)}"));

    public static string GoodName(ResourceType good, int units = 2) => good switch
    {
        ResourceType.Grain => "céréales",
        ResourceType.Bread => "pains",
        ResourceType.Flour => "farine",
        ResourceType.Wood => "bois",
        ResourceType.Stone => "pierre",
        ResourceType.IronOre => "minerai de fer",
        ResourceType.Charcoal => "charbon de bois",
        ResourceType.Iron => "fer",
        ResourceType.Tools => units > 1 ? "outils" : "outil",
        ResourceType.Coins => "pièces",
        ResourceType.Wool => "laine",
        ResourceType.Clothes => units > 1 ? "vêtements" : "vêtement",
        ResourceType.Salt => "sel",
        ResourceType.Spices => "épices",
        ResourceType.Hardwood => "bois dur",
        ResourceType.Chickens => units > 1 ? "poules" : "poule",
        ResourceType.Sheep => units > 1 ? "moutons" : "mouton",
        ResourceType.Cows => units > 1 ? "vaches" : "vache",
        ResourceType.Milk => "lait",
        ResourceType.Eggs => "œufs",
        ResourceType.Meat => "viande",
        ResourceType.SaltedMeat => "viande salée",
        ResourceType.Cake => units > 1 ? "gâteaux" : "gâteau",
        ResourceType.Stew => "ragoût",
        ResourceType.Beer => "bière",
        _ => (int)good >= 27 ? ResourceCatalog.Name(good) : "vivres",
    };

    // ---------- En route ----------

    /// <summary>Chaque heure : les caravanes arrivent, échangent, puis rentrent.</summary>
    public static void Hourly(WorldState world)
    {
        long now = world.Clock.Ticks;
        foreach (Caravan caravan in world.Caravans.ToList())
        {
            using var scope = caravan.From.UseSettlement(world.SettlementById(caravan.FromSettlementId) ?? caravan.From.PrimarySettlement);
            MaintainTravelers(world, caravan);
            AdvanceVoyage(world, caravan);
        }
    }

    /// <summary>
    /// Arrivée : l'échange se conclut aux prix convenus, pour autant que chacun ait encore de quoi payer et de quoi livrer
    /// (les stocks ont pu changer pendant le voyage).
    /// </summary>
    private static void Settle(WorldState world, Caravan caravan)
    {
        if (caravan.Purpose != TerritorialPurpose.Commerce) { TerritorialTravel.Arrive(world, caravan); return; }
        Colony seller = caravan.From, host = caravan.To;
        foreach (TradeLine line in caravan.Plan)
        {
            if (line.IsSale)
            {
                // Je vends à l'hôte : il achète ce qu'il juge valoir ce prix, et paie ce qu'il peut.
                int carried = caravan.Cargo.GetValueOrDefault(line.Good);
                int affordable = (int)Math.Min(carried, host.Stock.Available(ResourceType.Coins) / line.UnitPrice);
                int coinRoom = (int)Math.Min(int.MaxValue, Math.Floor(Math.Max(0, CapacityOf(seller, host) - caravan.LoadWeight)
                    / (line.UnitPrice * ResourceCatalog.Weight(ResourceType.Coins))));
                affordable = Math.Min(affordable, coinRoom);
                int wanted = Economy.UnitsWillingToBuy(host, line.Good, line.UnitPrice, line.Units);
                SupplyForecast need = Forecast(world, host, line.Good);
                wanted = Math.Min(wanted, need.Urgent ? need.Shortage : need.PurchaseNeed);
                int units = Math.Min(Math.Min(wanted, carried), affordable);
                if (units <= 0)
                    continue;
                int pay = LotPayment(units, line.UnitPrice);
                if (pay == 0) continue;
                if (!caravan.Inventory.TrySellTo(host.Stock, line.Good, units, pay, outgoing: ResourceFlow.Transfer))
                    continue;
                ResourceAccounting.Record(seller.Stock, line.Good, ResourceFlow.Sale, units);
                caravan.Settled.Add(line with { Units = units, UnitPrice = pay / (double)units });
            }
            else
            {
                // J'achète à l'hôte : il livre ce qu'il a de trop, je paie avec les pièces que je porte.
                int available = Economy.UnitsWillingToSell(host, line.Good, line.UnitPrice, line.Units);
                int affordable = (int)Math.Min(available, caravan.Coins / line.UnitPrice);
                // Retirer les pièces ne peut pas rendre la charge plus lourde ; ce calcul conservateur protège aussi les invendus.
                int room = (int)Math.Floor(Math.Max(0, CapacityOf(seller, host) - caravan.LoadWeight) / ResourceCatalog.Weight(line.Good));
                int units = Math.Min(Math.Min(available, affordable), room);
                int pay = LotPayment(units, line.UnitPrice);
                if (pay == 0) continue;
                if (!host.Stock.TrySellTo(caravan.Inventory, line.Good, units, pay, incoming: ResourceFlow.Transfer))
                    continue;
                ResourceAccounting.Record(seller.Stock, line.Good, ResourceFlow.Purchase, units);
                caravan.Settled.Add(line with { Units = units, UnitPrice = pay / (double)units });
            }
        }
        // Les marchands parlent de ce qu'ils savent faire : un peu du savoir de chacun passe chez l'autre.
        Knowledge.Share(world, seller, host);
        if (caravan.Settled.Count > 0)
            Diplomacy.OnTrade(seller, host);

        // Négocier fait progresser : chaque marchand de la caravane s'exerce au négoce.
        foreach (Colonist trader in caravan.Traders)
            trader.Skills.Practice(SkillType.Trading, 40f);
        Remember(world, host, seller, caravan.OutboundOffers, caravan.DepartTicks, caravan.OutboundBudget);
        caravan.ReturnOffers = Publish(host);
        caravan.ReturnBudget = host.Stock.Available(ResourceType.Coins);
        caravan.ReportTicks = world.Clock.Ticks;
        caravan.State = CaravanState.Returning;

        if (caravan.Settled.Count > 0)
        {
            string sold = Describe(caravan.Settled.Where(l => l.IsSale)), bought = Describe(caravan.Settled.Where(l => !l.IsSale));
            // Du point de vue de l'hôte, la caravane achète ce que l'autre vend et inversement.
            ColonyBrain.Say(host, world.Clock,
                $"La caravane de {seller.Name} est arrivée : " +
                (sold.Length > 0 ? $"nous lui achetons {sold}" : "") + (sold.Length > 0 && bought.Length > 0 ? ", " : "") +
                (bought.Length > 0 ? $"nous lui vendons {bought}" : "") + ".");
        }
        else
            ColonyBrain.Say(host, world.Clock, $"La caravane de {seller.Name} repart sans avoir rien pu échanger.");
    }

    /// <summary>Retour : les colons rentrent avec leurs marchandises et leurs pièces.</summary>
    private static void ComeHome(WorldState world, Caravan caravan)
    {
        if (caravan.Purpose != TerritorialPurpose.Commerce) { TerritorialTravel.Return(world, caravan); return; }
        Colony colony = caravan.From;
        if (caravan.Inventory.Amounts.Keys.Concat(caravan.Provisions.Amounts.Keys).Distinct()
            .Any(good => (long)colony.Stock.Get(good) + caravan.Inventory.Get(good) + caravan.Provisions.Get(good) > int.MaxValue))
        {
            caravan.BlockedReason = "Le stock d'arrivée est plein : le chargement reste dans la caravane.";
            return;
        }
        caravan.BlockedReason = null;
        var outcome = Outcome(world, caravan);
        ReceiveReport(world, caravan, outcome.Cost);
        int net = caravan.Coins - caravan.CoinsAtDeparture;
        foreach (Stockpile inventory in new[] { caravan.Inventory, caravan.Provisions })
            foreach ((ResourceType good, int amount) in inventory.Amounts.ToArray())
                if (amount > 0 && !inventory.TryTransferTo(colony.Stock, good, amount))
                    throw new InvalidOperationException("Le stock ne peut pas recevoir le retour de caravane.");

        // Les colons reviennent, fatigués du voyage : on les voit arriver du bord de la carte et marcher jusqu'au camp.
        // (S'ils étaient encore en train de sortir, ils s'arrêtent là et rentrent aussitôt.)
        foreach (Colonist trader in caravan.Traders)
        {
            colony.Transients.Remove(trader);
            trader.TravelId = 0;
            trader.LocationSettlementId = caravan.FromSettlementId;
            trader.Activity = null;
            if (Migration.FindEdgePoint(world, colony, colony.CampX, colony.CampY) is { } entry)
            {
                trader.X = trader.PrevX = entry.X + 0.5f;
                trader.Y = trader.PrevY = entry.Y + 0.5f;
                trader.Transit = TransitState.Arriving;
                trader.ReturningTrader = true;
                colony.Transients.Add(trader);
            }
            else
            {
                (int x, int y) = colony.GatherSpots[world.Random.Next(Math.Min(12, colony.GatherSpots.Count))];
                trader.X = trader.PrevX = x + 0.5f;
                trader.Y = trader.PrevY = y + 0.5f;
                trader.Transit = TransitState.None;
                colony.PresentMembers.Add(trader);
            }
        }
        colony.FillVacancies();
        colony.AssignSectors();

        // Ce que le voyage a vraiment rapporté en pièces (les arrondis des lignes ne comptent pas).
        var record = new TradeRecord(world.Clock.Ticks, caravan.To.Name, caravan.Settled.ToList(), net, WeSent: true)
        {
            GainHours = outcome.Gain, CostHours = outcome.Cost, ExpectedGainHours = caravan.PlanGainHours,
        };
        colony.Trades.Add(record);
        if (colony.Trades.Count > MaxRecords)
            colony.Trades.RemoveAt(0);
        colony.LifetimeTradeGainHours += outcome.Gain ?? 0;

        string sold = Describe(caravan.Settled.Where(l => l.IsSale)), bought = Describe(caravan.Settled.Where(l => !l.IsSale));
        ColonyBrain.Say(colony, world.Clock,
            caravan.Settled.Count == 0
                ? $"Notre caravane rentre de {caravan.To.Name} les mains vides."
                : $"Notre caravane est rentrée de {caravan.To.Name} : " +
                  (sold.Length > 0 ? $"vendu {sold}" : "") + (sold.Length > 0 && bought.Length > 0 ? ", " : "") +
                  (bought.Length > 0 ? $"acheté {bought}" : "") + $" ; solde {net:+0;-0;0} pièces."
                  + (outcome.Gain is { } gain ? $" Bilan du voyage : {gain:+0;-0;0} h." : ""));

        // L'hôte garde aussi une trace de la visite.
        var hostRecord = new TradeRecord(world.Clock.Ticks, colony.Name, caravan.Settled.Select(l => l with { IsSale = !l.IsSale }).ToList(), -net, WeSent: false);
        caravan.To.Trades.Add(hostRecord);
        if (caravan.To.Trades.Count > MaxRecords)
            caravan.To.Trades.RemoveAt(0);

        caravan.State = CaravanState.Home;
        world.Caravans.Remove(caravan);
        world.CompletedCaravans++;
    }
}

