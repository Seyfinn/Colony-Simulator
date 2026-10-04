using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Un jalon que la colonie peut atteindre : un nom, ce qu'il célèbre, et la condition pour l'obtenir.</summary>
public sealed record Milestone(string Id, string Title, Func<Colony, GameClock, bool> Reached);

/// <summary>
/// Les objectifs de la partie : des jalons que chaque colonie atteint au fil de son histoire (population, premières
/// fabrications, premier barrage…). Atteindre un jalon est célébré dans le journal et réjouit un peu tout le monde.
/// </summary>
public static class Milestones
{
    /// <summary>Foi gagnée par chaque colon à chaque jalon.</summary>
    private const float FaithGain = 0.03f;

    public static readonly IReadOnlyList<Milestone> All =
    [
        new("pop10", "Dix habitants", (c, _) => c.Members.Count >= 10),
        new("pop20", "Vingt habitants", (c, _) => c.Members.Count >= 20),
        new("pop40", "Quarante habitants : une vraie bourgade", (c, _) => c.Members.Count >= 40),
        new("birth", "Premier enfant né dans la colonie", (c, _) => c.Members.Any(m => m.Mother is not null)),
        new("year2", "Une année entière survécue", (_, k) => k.Year >= 2),
        new("harvest", "Première grande moisson (20 céréales)", (c, _) => c.Labor.TotalProduced(ResourceType.Grain) >= 20),
        new("bread", "Premier pain cuit", (c, _) => c.Labor.TotalProduced(ResourceType.Bread) >= 1),
        new("tool", "Premier outil de fer forgé", (c, _) => c.Labor.TotalProduced(ResourceType.Tools) >= 1),
        new("dam", "Un barrage domine la rivière", (c, _) => c.Buildings.Any(b => b.IsDam && b.IsComplete)),
        new("flock", "Un enclos peuplé de bêtes", (c, _) => Husbandry.Pens(c) > 0 && Husbandry.Animals(c) > 0),
        new("cattle", "Une vache à l'étable : lait et labours", (c, _) => c.Cows > 0),
        new("salted", "Une réserve de viande salée pour les mauvais jours", (c, _) => c.Stock.Get(ResourceType.SaltedMeat) >= 10),
        new("cake", "Premier gâteau de fête", (c, _) => c.Labor.TotalProduced(ResourceType.Cake) >= 1),
        new("stew", "Premier ragoût mijoté", (c, _) => c.Labor.TotalProduced(ResourceType.Stew) >= 1),
        new("beer", "Première bière brassée", (c, _) => c.Labor.TotalProduced(ResourceType.Beer) >= 1),
        new("pens", "Plusieurs enclos : un vrai élevage", (c, _) => Husbandry.Pens(c) >= 2),
        new("clothes", "Premiers vêtements tissés", (c, _) => c.Labor.TotalProduced(ResourceType.Clothes) >= 1),
        new("trade", "Première caravane rentrée", (c, _) => c.Trades.Any(t => t.WeSent)),
        new("market", "Un marché ouvre ses étals", (c, _) => Civic.Has(c, BuildingType.Market)),
        new("village", "Une vraie vie de village : taverne et école", (c, _) => Civic.Has(c, BuildingType.Tavern) && Civic.Has(c, BuildingType.School)),
    ];

    public static int Reached(Colony colony) => colony.Achievements.Count;

    /// <summary>Chaque matin : y a-t-il un jalon de plus ?</summary>
    public static void Daily(Colony colony, GameClock clock)
    {
        foreach (Milestone milestone in All)
        {
            if (colony.Achievements.ContainsKey(milestone.Id) || !milestone.Reached(colony, clock))
                continue;
            colony.Achievements[milestone.Id] = clock.Ticks;
            foreach (Colonist colonist in colony.Members)
                colonist.Needs.Faith = Math.Min(1f, colonist.Needs.Faith + FaithGain);
            ColonyBrain.Say(colony, clock, $"★ Objectif atteint : {milestone.Title}.");
        }
    }
}

/// <summary>
/// Les événements : un incendie, des pillards, un colporteur de passage. Ils restent rares, ne frappent pas une colonie
/// naissante, et la préparation compte : un puits maîtrise le feu, des outils et des bras défendent contre les pillards.
/// Leur hasard passe par un générateur à part (<see cref="WorldState.Chance"/>).
/// </summary>
public static class Events
{
    /// <summary>Jours de répit au début de la partie.</summary>
    public const int GraceDays = 10;

    public const float FireChancePerDay = 0.006f;
    public const float RaidChancePerDay = 0.015f;
    public const float PeddlerChancePerDay = 0.03f;

    public const int MinPopulationForRaid = 10;
    public const int MinDaysForRaid = 40;
    public const int MinPopulationForFire = 8;

    public static void Daily(WorldState world, Colony colony)
    {
        if (world.Clock.TotalDays < GraceDays)
            return;
        Random chance = world.Chance;

        if (colony.Members.Count >= MinPopulationForFire && chance.NextSingle() < FireChancePerDay * (colony.DroughtDaysLeft > 0 ? 3f : 1f))
            Fire(world, colony);

        if (colony.Members.Count >= MinPopulationForRaid && world.Clock.TotalDays >= MinDaysForRaid && chance.NextSingle() < RaidChancePerDay)
            Raid(world, colony);

        float peddler = PeddlerChancePerDay * (Civic.Has(colony, BuildingType.Market) ? 2f : 1f);
        if (colony.Members.Count >= 6 && chance.NextSingle() < peddler)
            Peddler(world, colony);
    }

    /// <summary>Un incendie : un bâtiment achevé est menacé ; un puits l'éteint à temps.</summary>
    public static void Fire(WorldState world, Colony colony)
    {
        var buildings = colony.Buildings.Where(b => b.IsComplete && !b.IsDam).ToList();
        if (buildings.Count == 0)
            return;
        Building target = buildings[world.Chance.Next(buildings.Count)];
        if (Civic.Has(colony, BuildingType.Well))
        {
            colony.RecordEvent(new(ColonyEventKind.Fire, target.X, target.Y, world.Clock.Ticks, ColonyEventOutcome.Extinguished, target.Type));
            int lost = colony.Stock.Get(ResourceType.Wood) / 12;
            colony.Stock.TryTake(ResourceType.Wood, lost, ResourceFlow.Loss);
            ColonyBrain.Say(colony, world.Clock, $"Le feu prend {Building.AtThe(target.Type)} : grâce au puits, il est vite éteint.");
        }
        else
            Civic.Burn(colony, target, world.Clock);
    }

    /// <summary>
    /// Des pillards attaquent une colonie qui a quelque chose à prendre. Les défenseurs (adultes, outils de fer) font la différence :
    /// repoussés, ils repartent ; sinon ils emportent pièces, outils et grain, et blessent quelques colons.
    /// </summary>
    public static void Raid(WorldState world, Colony colony)
    {
        Stockpile stock = colony.Stock;
        if (stock.Get(ResourceType.Coins) < 100 && stock.Get(ResourceType.Tools) == 0 && stock.Get(ResourceType.Grain) < 20)
            return;

        int adults = colony.Members.Count(m => m.Stage == LifeStage.Adult && m.Ailment == Ailment.None);
        float defense = (adults + 1.5f * Math.Min(stock.Get(ResourceType.Tools), adults))
            * (Knowledge.Has(colony, Discovery.Fortification) ? Knowledge.FortificationFactor : 1f)
            + Diplomacy.AlliedHelp(world, colony, attacker: null);
        float strength = 4f + colony.Members.Count / 3f + world.Clock.Year + 2f * world.Chance.NextSingle();
        GameClock clock = world.Clock;

        if (defense >= strength)
        {
            colony.RecordEvent(new(ColonyEventKind.Raid, 1, colony.CampY, clock.Ticks, ColonyEventOutcome.Repelled));
            ColonyBrain.Say(colony, clock, "Des pillards rôdent autour de la colonie, mais nos défenseurs les repoussent sans perte.");
            return;
        }

        colony.RecordEvent(new(ColonyEventKind.Raid, 1, colony.CampY, clock.Ticks, ColonyEventOutcome.Pillaged));
        int coins = stock.Get(ResourceType.Coins) * 3 / 10;
        int tools = stock.Get(ResourceType.Tools) / 5;
        int grain = stock.Get(ResourceType.Grain) / 6;
        stock.TryTake(ResourceType.Coins, coins, ResourceFlow.Loss);
        world.CoinsLostToEvents += coins;
        stock.TryTake(ResourceType.Tools, tools, ResourceFlow.Loss);
        stock.TryTake(ResourceType.Grain, grain, ResourceFlow.Loss);

        int wounded = 0;
        foreach (Colonist victim in colony.Members.Where(m => m.Stage != LifeStage.Child && m.Ailment == Ailment.None)
                     .OrderBy(_ => world.Chance.Next()).Take(Math.Max(1, (int)MathF.Ceiling((strength - defense) / 3f))).ToList())
        {
            Health.Fall(colony, victim, Ailment.Injured, 72 + world.Chance.Next(0, 48), clock, "");
            wounded++;
        }
        ColonyBrain.Say(colony, clock, $"Des pillards attaquent et nous prennent {coins} pièces, {tools} outils et {grain} céréales ; {wounded} colons sont blessés.");
    }

    /// <summary>
    /// Un colporteur de passage : une fois sur deux (quand la colonie a un surplus) il lui achète ce dont elle déborde, au prix
    /// de revient ; sinon il lui propose une denrée lointaine (sel, épices ou bois dur) que la région ne produit pas,
    /// pour peu qu'elle ait de quoi payer. Les pièces qu'il emporte ou apporte sont comptées dans <see cref="WorldState.CoinsLostToEvents"/>,
    /// si bien qu'en moyenne il ne gonfle ni ne vide la monnaie.
    /// </summary>
    public static void Peddler(WorldState world, Colony colony)
    {
        var surplus = Economy.Tradable.Where(g => !Specialties.IsSpecialty(g) && Economy.Surplus(colony, g) >= 3).ToList();
        if (surplus.Count > 0 && world.Chance.NextSingle() < 0.5f)
        {
            ResourceType bought = surplus.OrderByDescending(g => Economy.Surplus(colony, g)).First();
            int boughtUnits = Math.Min(Economy.Surplus(colony, bought), 6);
            int pay = Math.Max(1, (int)Math.Ceiling(boughtUnits * Economy.Cost(colony, bought)));
            colony.Stock.TryTake(bought, boughtUnits, ResourceFlow.Sale);
            colony.Stock.Add(ResourceType.Coins, pay, ResourceFlow.Transfer);
            world.CoinsLostToEvents -= pay;
            colony.RecordEvent(new(ColonyEventKind.Peddler, colony.CampX, colony.CampY, world.Clock.Ticks, ColonyEventOutcome.Traded));
            ColonyBrain.Say(colony, world.Clock, $"Un colporteur de passage nous achète {boughtUnits} {Trade.GoodName(bought, boughtUnits)} pour {pay} pièces.");
            return;
        }

        ResourceType native = Specialties.NativeOf(colony);
        // Les denrées que la région ne produit pas, et les bêtes qui manquent à l'enclos et que la région élève mal.
        ResourceType[] offers = Specialties.Goods.Where(g => g != native)
            .Concat(Husbandry.Species.Where(s => Husbandry.Abundance(colony, s) < 0.8f && Husbandry.CanAffordLivestock(colony, s)
                && Husbandry.CapacityOf(colony, s) - Husbandry.Count(colony, s) - colony.Stock.Get(s) > 0))
            .ToArray();
        ResourceType good = offers[world.Chance.Next(offers.Length)];
        bool animal = Husbandry.IsLivestock(good);
        int units = animal ? 1 : 3 + world.Chance.Next(0, 4);
        int price = (int)Math.Ceiling(units * (animal ? Economy.Cost(colony, good) : Economy.BaselineCost(good)));
        GameClock clock = world.Clock;

        if (colony.Stock.TryTake(ResourceType.Coins, price, ResourceFlow.Transfer))
        {
            colony.RecordEvent(new(ColonyEventKind.Peddler, colony.CampX, colony.CampY, clock.Ticks, ColonyEventOutcome.Traded));
            world.CoinsLostToEvents += price;
            colony.Stock.Add(good, units, ResourceFlow.Purchase);
            ColonyBrain.Say(colony, clock, $"Un colporteur de passage nous cède {units} {Trade.GoodName(good, units)} pour {price} pièces.");
        }
        else
        {
            colony.RecordEvent(new(ColonyEventKind.Peddler, colony.CampX, colony.CampY, clock.Ticks, ColonyEventOutcome.Unaffordable));
            ColonyBrain.Say(colony, clock, $"Un colporteur propose {(animal ? "des bêtes" : "du " + Specialties.Name(good))}, mais nous n'avons pas de quoi payer.");
        }
    }
}
