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
}

/// <summary>Le compte rendu d'un voyage achevé, gardé par chaque colonie pour s'en souvenir.</summary>
public sealed record TradeRecord(long Ticks, string Partner, IReadOnlyList<TradeLine> Lines, int NetCoins, bool WeSent);

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
        Plan = lines;
        DepartTicks = departTicks;
        ArriveTicks = arriveTicks;
        ReturnTicks = returnTicks;
    }

    public Colony From { get; }
    public Colony To { get; }
    public IReadOnlyList<Colonist> Traders { get; }
    public IReadOnlyList<TradeLine> Plan { get; }
    public long DepartTicks { get; }
    public long ArriveTicks { get; }
    public long ReturnTicks { get; }
    public CaravanState State { get; internal set; } = CaravanState.Outbound;

    /// <summary>Le travail que l'échange devait épargner, si tout se passait comme prévu.</summary>
    public double PlanGainHours { get; }

    /// <summary>Les pièces emportées au départ (pour calculer ce que le voyage a vraiment rapporté).</summary>
    public int CoinsAtDeparture { get; internal set; }

    /// <summary>Ce qu'elle transporte en ce moment : au départ, les marchandises à vendre ; au retour, ce qu'elle rapporte.</summary>
    public Dictionary<ResourceType, int> Cargo { get; } = [];

    /// <summary>Les pièces qu'elle porte : de quoi acheter à l'aller, le produit des ventes au retour.</summary>
    public int Coins
    {
        get => Cargo.GetValueOrDefault(ResourceType.Coins);
        internal set => Cargo[ResourceType.Coins] = value;
    }

    /// <summary>Avancement du voyage entier, de 0 (départ) à 1 (retour).</summary>
    public float Progress(long nowTicks) => Math.Clamp((nowTicks - DepartTicks) / (float)(ReturnTicks - DepartTicks), 0f, 1f);

    internal List<TradeLine> Settled { get; } = [];
}

/// <summary>
/// Le commerce entre colonies : chaque matin, une colonie compare ses valeurs à celles de ses voisines et, si un
/// échange rapporte nettement plus qu'il ne coûte en travail de caravane, envoie des colons le conclure.
/// Le prix est négocié au milieu des deux valeurs : les deux colonies y gagnent.
/// </summary>
public static class Trade
{
    /// <summary>Heure à laquelle les colonies réfléchissent à leurs échanges.</summary>
    public const int PlanningHour = 9;

    public const int TradersPerCaravan = 2;

    /// <summary>Charge maximale d'une caravane, en unités (tous biens confondus).</summary>
    public const int CarryCapacity = 36;

    /// <summary>Heures de travail utiles par jour et par colon : le coût d'un colon en voyage.</summary>
    public const double WorkHoursPerDay = 8;

    /// <summary>Le gain doit dépasser le coût du voyage d'au moins ce facteur : on ne risque pas la route pour une broutille.</summary>
    public const double RequiredGainOverCost = 1.25;

    /// <summary>Écart de valeur minimal (en proportion) pour qu'un échange vaille la peine.</summary>
    public const double MinValueGap = 0.15;

    /// <summary>Jours à attendre après un départ avant d'en préparer un autre.</summary>
    public const int DaysBetweenCaravans = 2;

    /// <summary>Colons minimum pour oser en envoyer en voyage.</summary>
    public const int MinColonistsToTrade = 6;

    private const int MaxExportInterest = 6;
    private const int MaxRecords = 20;

    // ---------- Préparation ----------

    /// <summary>
    /// Le meilleur échange possible de <paramref name="from"/> vers <paramref name="to"/> : on vend ce qu'on a en trop
    /// à celui qui en manque et en estime plus la valeur, on achète ce qu'on manque à celui qui en a trop et l'estime moins.
    /// Renvoie null si rien ne rapporte assez.
    /// </summary>
    public static TradePlan? Plan(WorldState world, Colony from, Colony to)
    {
        float tripDays = 2f * world.WorldMap.TravelDays(from, to) + 0.5f;
        double cost = TradersPerCaravan * tripDays * WorkHoursPerDay;

        var candidates = new List<(TradeLine Line, double GainPerUnit)>();
        foreach (ResourceType good in Economy.Tradable)
        {
            // Je vends : chaque unité part tant qu'ils lui accordent plus de valeur que moi.
            Clearing sale = Economy.Clear(from, to, good, CarryCapacity);
            if (sale.Units > 0)
                candidates.Add((new TradeLine(good, sale.Units, sale.UnitPrice, IsSale: true), sale.GainHours / sale.Units));

            // J'achète : chaque unité vient tant que je lui accorde plus de valeur qu'eux.
            Clearing purchase = Economy.Clear(to, from, good, CarryCapacity);
            if (purchase.Units > 0)
                candidates.Add((new TradeLine(good, purchase.Units, purchase.UnitPrice, IsSale: false), purchase.GainHours / purchase.Units));
        }
        if (candidates.Count == 0)
            return null;

        // On remplit la caravane en commençant par ce qui rapporte le plus par unité transportée.
        var lines = new List<(TradeLine Line, double Gain)>();
        int room = CarryCapacity;
        foreach ((TradeLine line, double gain) in candidates.OrderByDescending(c => c.GainPerUnit))
        {
            int units = Math.Min(line.Units, room);
            if (units <= 0)
                break;
            lines.Add((line with { Units = units }, gain));
            room -= units;
        }

        // Les pièces sont physiques : chacun ne paie que ce que contient sa bourse.
        lines = FitPurse(lines, isSale: false, available: from.Stock.Get(ResourceType.Coins));
        lines = FitPurse(lines, isSale: true, available: to.Stock.Get(ResourceType.Coins));

        double gainHours = lines.Sum(l => l.Line.Units * l.Gain);
        if (lines.Count == 0 || gainHours < cost * RequiredGainOverCost)
            return null;
        return new TradePlan(from, to, lines.Select(l => l.Line).ToList(), gainHours, cost, tripDays);
    }

    /// <summary>Réduit les achats d'un côté (les moins rentables d'abord) jusqu'à ce que la bourse suffise.</summary>
    private static List<(TradeLine Line, double Gain)> FitPurse(List<(TradeLine Line, double Gain)> lines, bool isSale, int available)
    {
        // Les lignes d'achat de la colonie qui envoie sont payées avec ses pièces (isSale = false) ;
        // ses lignes de vente sont payées par le destinataire (isSale = true).
        var result = lines.ToList();
        double spend() => result.Where(l => l.Line.IsSale == isSale).Sum(l => l.Line.Total);
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
        List<Colony> partners = world.Colonies.Where(c => c != colony).OrderBy(c => world.WorldMap.Distance(colony, c)).ToList();
        if (partners.Count == 0)
            return;

        UpdateExportInterest(colony, partners);

        long now = world.Clock.Ticks;
        bool busy = world.Caravans.Any(c => c.From == colony && c.State != CaravanState.Home);
        bool resting = now - colony.LastCaravanTicks < DaysBetweenCaravans * TimeConstants.TicksPerDay;
        bool strong = colony.Members.Count >= MinColonistsToTrade && (colony.Sensors?.SurvivalAssured ?? true);
        if (busy || resting || !strong)
            return;

        TradePlan? best = null;
        foreach (Colony partner in partners)
            if (Plan(world, colony, partner) is { } plan && (best is null || plan.GainHours - plan.CostHours > best.GainHours - best.CostHours))
                best = plan;
        if (best is not null)
            Depart(world, best);
    }

    /// <summary>
    /// Ce que la colonie fabriquerait volontiers en plus pour ses voisines : des outils, quand elle sait les faire
    /// moins cher qu'elles et qu'elles en manquent. C'est ainsi que la spécialisation naît du commerce.
    /// </summary>
    private static void UpdateExportInterest(Colony colony, List<Colony> partners)
    {
        int tools = 0;
        foreach (Colony partner in partners)
        {
            if (colony.Labor.HoursPerUnit(ResourceType.Tools) is null && colony.Stock.Get(ResourceType.Tools) == 0)
                continue;
            // Combien d'outils le voisin paierait-il plus cher que ce qu'ils nous coûtent à fabriquer ?
            double mine = Economy.Cost(colony, ResourceType.Tools) * (1 + MinValueGap);
            int wanted = Economy.UnitsWillingToBuy(partner, ResourceType.Tools, mine, MaxExportInterest);
            tools = Math.Max(tools, wanted);
        }
        colony.ExportInterest[ResourceType.Tools] = tools;
    }

    // ---------- Départ ----------

    /// <summary>Les colons qui partent : des adultes libres, de préférence ceux dont la colonie a le moins besoin.</summary>
    private static List<Colonist> PickTraders(Colony colony) =>
        colony.Members
            .Where(m => m.Stage == LifeStage.Adult && m.Partner is null && m.PregnantUntilTicks is null && m.Transit == TransitState.None)
            .OrderByDescending(m => m.Sector == WorkSector.Free)
            .ThenBy(m => m.Id)
            .Take(TradersPerCaravan)
            .ToList();

    /// <summary>La caravane quitte la colonie avec ses marchandises et ses pièces.</summary>
    public static Caravan? Depart(WorldState world, TradePlan plan)
    {
        List<Colonist> traders = PickTraders(plan.From);
        if (traders.Count < TradersPerCaravan)
            return null;

        long now = world.Clock.Ticks;
        long oneWay = (long)(world.WorldMap.TravelDays(plan.From, plan.To) * TimeConstants.TicksPerDay);
        var caravan = new Caravan(plan.From, plan.To, traders, plan.Lines, plan.GainHours, now, now + oneWay, now + 2 * oneWay + TimeConstants.TicksPerDay / 2);

        // Les marchandises à vendre et les pièces pour acheter quittent le stock (ce qu'il n'y a plus en stock n'est pas emporté).
        int coins = 0;
        foreach (TradeLine line in plan.Lines)
        {
            if (line.IsSale)
            {
                if (plan.From.Stock.TryTake(line.Good, line.Units))
                    caravan.Cargo[line.Good] = caravan.Cargo.GetValueOrDefault(line.Good) + line.Units;
            }
            else
                coins += (int)Math.Ceiling(line.Total);
        }
        coins = Math.Min(coins, plan.From.Stock.Get(ResourceType.Coins));
        plan.From.Stock.TryTake(ResourceType.Coins, coins);
        caravan.Coins = coins;
        caravan.CoinsAtDeparture = coins;

        foreach (Colonist trader in traders)
            ColonistAI.DetachFromColony(trader);
        plan.From.LastCaravanTicks = now;
        world.Caravans.Add(caravan);

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
        _ => "vivres",
    };

    // ---------- En route ----------

    /// <summary>Chaque heure : les caravanes arrivent, échangent, puis rentrent.</summary>
    public static void Hourly(WorldState world)
    {
        long now = world.Clock.Ticks;
        foreach (Caravan caravan in world.Caravans.ToList())
        {
            if (caravan.State == CaravanState.Outbound && now >= caravan.ArriveTicks)
                Settle(world, caravan);
            if (caravan.State == CaravanState.Returning && now >= caravan.ReturnTicks)
                ComeHome(world, caravan);
        }
    }

    /// <summary>
    /// Arrivée : l'échange se conclut aux prix convenus, pour autant que chacun ait encore de quoi payer et de quoi livrer
    /// (les stocks ont pu changer pendant le voyage).
    /// </summary>
    private static void Settle(WorldState world, Caravan caravan)
    {
        Colony seller = caravan.From, host = caravan.To;
        foreach (TradeLine line in caravan.Plan)
        {
            if (line.IsSale)
            {
                // Je vends à l'hôte : il achète ce qu'il juge valoir ce prix, et paie ce qu'il peut.
                int carried = caravan.Cargo.GetValueOrDefault(line.Good);
                int affordable = line.UnitPrice <= 0 ? carried : (int)Math.Min(carried, host.Stock.Get(ResourceType.Coins) / line.UnitPrice);
                int wanted = Economy.UnitsWillingToBuy(host, line.Good, line.UnitPrice, line.Units);
                int units = Math.Min(Math.Min(wanted, carried), affordable);
                if (units <= 0)
                    continue;
                int pay = (int)Math.Round(units * line.UnitPrice);
                pay = Math.Min(pay, host.Stock.Get(ResourceType.Coins));
                host.Stock.TryTake(ResourceType.Coins, pay);
                host.Stock.Add(line.Good, units);
                caravan.Cargo[line.Good] = carried - units;
                caravan.Coins += pay;
                caravan.Settled.Add(line with { Units = units });
            }
            else
            {
                // J'achète à l'hôte : il livre ce qu'il a de trop, je paie avec les pièces que je porte.
                int available = Economy.UnitsWillingToSell(host, line.Good, line.UnitPrice, line.Units);
                int affordable = line.UnitPrice <= 0 ? available : (int)Math.Min(available, caravan.Coins / line.UnitPrice);
                int units = Math.Min(available, affordable);
                if (units <= 0 || !host.Stock.TryTake(line.Good, units))
                    continue;
                int pay = Math.Min((int)Math.Round(units * line.UnitPrice), caravan.Coins);
                caravan.Coins -= pay;
                host.Stock.Add(ResourceType.Coins, pay);
                caravan.Cargo[line.Good] = caravan.Cargo.GetValueOrDefault(line.Good) + units;
                caravan.Settled.Add(line with { Units = units });
            }
        }
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
        Colony colony = caravan.From;
        foreach ((ResourceType good, int amount) in caravan.Cargo)
            if (amount > 0)
                colony.Stock.Add(good, amount);

        // Les colons rejoignent le camp, fatigués du voyage.
        foreach (Colonist trader in caravan.Traders)
        {
            (int x, int y) = colony.GatherSpots[world.Random.Next(Math.Min(12, colony.GatherSpots.Count))];
            trader.X = trader.PrevX = x + 0.5f;
            trader.Y = trader.PrevY = y + 0.5f;
            trader.Needs.Food = Math.Min(trader.Needs.Food, 0.7f);
            trader.Needs.Rest = Math.Min(trader.Needs.Rest, 0.6f);
            colony.Members.Add(trader);
        }
        colony.FillVacancies();
        colony.AssignSectors();

        // Ce que le voyage a vraiment rapporté en pièces (les arrondis des lignes ne comptent pas).
        int net = caravan.Coins - caravan.CoinsAtDeparture;
        var record = new TradeRecord(world.Clock.Ticks, caravan.To.Name, caravan.Settled.ToList(), net, WeSent: true);
        colony.Trades.Add(record);
        if (colony.Trades.Count > MaxRecords)
            colony.Trades.RemoveAt(0);
        int plannedUnits = caravan.Plan.Sum(l => l.Units);
        colony.LifetimeTradeGainHours += plannedUnits == 0 ? 0 : caravan.PlanGainHours * caravan.Settled.Sum(l => l.Units) / plannedUnits;

        string sold = Describe(caravan.Settled.Where(l => l.IsSale)), bought = Describe(caravan.Settled.Where(l => !l.IsSale));
        ColonyBrain.Say(colony, world.Clock,
            caravan.Settled.Count == 0
                ? $"Notre caravane rentre de {caravan.To.Name} les mains vides."
                : $"Notre caravane est rentrée de {caravan.To.Name} : " +
                  (sold.Length > 0 ? $"vendu {sold}" : "") + (sold.Length > 0 && bought.Length > 0 ? ", " : "") +
                  (bought.Length > 0 ? $"acheté {bought}" : "") + $" ; solde {net:+0;-0;0} pièces.");

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

