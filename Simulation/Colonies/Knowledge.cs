using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les savoirs qu'une colonie peut découvrir. Les premiers ouvrent des bâtiments (métallurgie : charbonnière, bas fourneau
/// et forge ; meunerie : moulin et four…), ceux du bourg donnent des avantages (assolement, fortifications…).
/// </summary>
public enum Discovery
{
    Agriculture, Husbandry, Metallurgy, Milling, Masonry, Irrigation,
    Weaving, Medicine, Brewing, Commerce, Hydraulics, Writing, Diplomacy,
    CropRotation, Fortification, Warfare, Herbalism, Coinage, Philosophy,
}

/// <summary>Les âges d'une colonie, du camp de huttes au bourg.</summary>
public enum Age { Wood, Iron, Village, Town }

/// <summary>La fiche d'un savoir : son nom, ce qu'il apporte, son rang (1 à 3), ce qu'il faut savoir avant, son coût en points de savoir.</summary>
public sealed record DiscoveryInfo(Discovery Id, string Name, string Effect, int Tier, IReadOnlyList<Discovery> Requires)
{
    public float Cost => Knowledge.TierCost(Tier);
}

/// <summary>
/// Le progrès d'une colonie. Chaque jour, ses habitants accumulent du savoir (les adultes curieux, les anciens qui transmettent,
/// bien plus avec une école) et l'appliquent à un savoir à découvrir. La colonie étudie d'abord ce qui lui manque pour bâtir
/// ce qu'elle veut (un atelier, un bâtiment du village, un canal) : on ne bâtit plus dans un ordre fixe, on bâtit ce que l'on sait faire.
/// Chaque peuple part avec ses propres savoirs (les nains connaissent le fer), et les caravanes font circuler ceux des voisins.
/// </summary>
public static class Knowledge
{
    public static float TierCost(int tier) => tier switch { 1 => 6f, 2 => 20f, _ => 50f };

    /// <summary>Chaque savoir connu au-delà des quatre premiers rend les suivants 12 % plus longs à découvrir : le progrès ralentit.</summary>
    public const float CostGrowthPerDiscovery = 0.12f;

    /// <summary>Ce que coûte un savoir à cette colonie, compte tenu de tout ce qu'elle sait déjà.</summary>
    public static float CostFor(Colony colony, Discovery discovery) =>
        Info(discovery).Cost * (1f + CostGrowthPerDiscovery * Math.Max(0, colony.Known.Count - 4));

    /// <summary>Points de savoir par jour : un adulte en apporte 0,1 (plus s'il est curieux), un ancien 0,25, un adolescent 0,04.</summary>
    private const float AdultPoints = 0.1f;
    private const float ElderPoints = 0.25f;
    private const float TeenPoints = 0.04f;
    private const float MinDailyPoints = 0.2f;

    /// <summary>L'école multiplie le savoir accumulé ; la philosophie encore un peu.</summary>
    public const float SchoolFactor = 1.6f;
    public const float PhilosophyFactor = 1.3f;

    /// <summary>Part du coût d'un savoir apprise d'une caravane venue d'une colonie qui le connaît (le double entre alliés).</summary>
    public const float TradeShare = 0.15f;

    /// <summary>Bonus de référence de l'assolement, avant le facteur de durée du cycle agricole.</summary>
    public const int CropRotationBonus = 1;

    public const float FortificationFactor = 1.5f;
    public const float WarfareFactor = 1.4f;
    public const float HerbalismFactor = 0.6f;
    public const float CoinageCapacityFactor = 1.25f;
    public const float CoinageCostFactor = 0.85f;

    private const float DiscoveryFaithGain = 0.02f;

    public static readonly IReadOnlyList<DiscoveryInfo> All =
    [
        new(Discovery.Agriculture, "Agriculture", "Semer et moissonner les céréales.", 1, []),
        new(Discovery.Husbandry, "Élevage", "Bâtir un enclos : poules, moutons et vaches.", 1, [Discovery.Agriculture]),
        new(Discovery.Metallurgy, "Métallurgie", "Charbonnière, bas fourneau et forge : des outils de fer.", 1, []),
        new(Discovery.Milling, "Meunerie", "Moulin à eau et four : farine et pain.", 1, [Discovery.Agriculture]),
        new(Discovery.Masonry, "Maçonnerie", "Puits et entrepôt.", 1, []),
        new(Discovery.Irrigation, "Irrigation", "Creuser des canaux vers les champs secs.", 1, [Discovery.Agriculture]),
        new(Discovery.Weaving, "Tissage", "Métier à tisser : vêtements de laine.", 2, [Discovery.Husbandry]),
        new(Discovery.Medicine, "Médecine", "Infirmerie : les malades guérissent deux fois plus vite.", 2, []),
        new(Discovery.Brewing, "Brasserie", "Taverne et fûts de bière.", 2, [Discovery.Milling]),
        new(Discovery.Commerce, "Commerce", "Marché : troc avec les nomades, caravanes plus chargées.", 2, [Discovery.Agriculture]),
        new(Discovery.Hydraulics, "Hydraulique", "Barrer une rivière pour former un lac.", 2, [Discovery.Irrigation, Discovery.Masonry]),
        new(Discovery.Writing, "Écriture", "École : les enfants s'instruisent et le savoir progresse plus vite.", 2, [Discovery.Commerce]),
        new(Discovery.Diplomacy, "Diplomatie", "Sceller des alliances ; les voisins sont mieux disposés, les trêves plus longues.", 2, [Discovery.Commerce]),
        new(Discovery.CropRotation, "Assolement", "Une à deux céréales de plus par parcelle moissonnée, selon le rendement.", 3, [Discovery.Irrigation, Discovery.Husbandry]),
        new(Discovery.Fortification, "Fortifications", "Les défenseurs comptent moitié plus contre pillards et ennemis.", 3, [Discovery.Masonry, Discovery.Metallurgy]),
        new(Discovery.Warfare, "Art de la guerre", "Les guerriers comptent 40 % de plus à l'attaque.", 3, [Discovery.Metallurgy]),
        new(Discovery.Herbalism, "Pharmacopée", "Les fièvres sont moins fréquentes (−40 %).", 3, [Discovery.Medicine]),
        new(Discovery.Coinage, "Monnaie frappée", "Caravanes plus chargées (+25 %), voyages moins coûteux et atelier de frappe (quota annuel partagé).", 3, [Discovery.Commerce, Discovery.Metallurgy]),
        new(Discovery.Philosophy, "Philosophie", "Le savoir progresse 30 % plus vite.", 3, [Discovery.Writing]),
    ];

    public static DiscoveryInfo Info(Discovery discovery) => All[(int)discovery];

    public static string Name(Discovery discovery) => Info(discovery).Name;

    public static string AgeName(Age age) => age switch
    {
        Age.Wood => "Âge du bois",
        Age.Iron => "Âge du fer",
        Age.Village => "Âge du village",
        _ => "Âge du bourg",
    };

    /// <summary>Les savoirs que chaque peuple connaît en s'installant, en plus de l'agriculture.</summary>
    public static IReadOnlyList<Discovery> StartingKnowledge(Species species) =>
        species == Species.Dwarf ? [Discovery.Agriculture, Discovery.Metallurgy, Discovery.Masonry]
        : species == Species.Elf ? [Discovery.Agriculture, Discovery.Medicine, Discovery.Irrigation]
        : species == Species.Orc ? [Discovery.Agriculture, Discovery.Husbandry, Discovery.Metallurgy]
        : [Discovery.Agriculture, Discovery.Husbandry, Discovery.Masonry];

    /// <summary>Donne à une colonie naissante les savoirs de son peuple, sans les annoncer.</summary>
    internal static void Grant(Colony colony, IEnumerable<Discovery> discoveries, long ticks)
    {
        foreach (Discovery discovery in discoveries)
            colony.Known.TryAdd(discovery, ticks);
    }

    public static bool Has(Colony colony, Discovery discovery) => colony.Known.ContainsKey(discovery);

    /// <summary>Peut-on étudier ce savoir ? Il n'est pas encore connu et tout ce qu'il demande l'est.</summary>
    public static bool CanResearch(Colony colony, Discovery discovery) =>
        !Has(colony, discovery) && Info(discovery).Requires.All(r => Has(colony, r));

    /// <summary>Le savoir qu'il faut pour bâtir ce bâtiment (null pour une hutte).</summary>
    public static Discovery? Required(BuildingType type) => type switch
    {
        BuildingType.Pen => Discovery.Husbandry,
        BuildingType.Kiln or BuildingType.Bloomery or BuildingType.Forge => Discovery.Metallurgy,
        BuildingType.Mill or BuildingType.Oven => Discovery.Milling,
        BuildingType.Well or BuildingType.Storehouse => Discovery.Masonry,
        BuildingType.Loom => Discovery.Weaving,
        BuildingType.Infirmary => Discovery.Medicine,
        BuildingType.Tavern or BuildingType.Cask => Discovery.Brewing,
        BuildingType.PotteryKiln => Discovery.Masonry,
        BuildingType.Tannery => Discovery.Husbandry,
        BuildingType.Goldsmith => Discovery.Metallurgy,
        BuildingType.Mint => Discovery.Coinage,
        BuildingType.Shrine => Discovery.Masonry,
        BuildingType.Market => Discovery.Commerce,
        BuildingType.School => Discovery.Writing,
        BuildingType.Dam => Discovery.Hydraulics,
        _ => null,
    };

    /// <summary>La colonie sait-elle bâtir ce bâtiment ?</summary>
    public static bool Allows(Colony colony, BuildingType type) => Required(type) is not { } needed || Has(colony, needed);

    /// <summary>Les bâtiments qu'un savoir permet de bâtir.</summary>
    public static IEnumerable<BuildingType> Unlocks(Discovery discovery) =>
        Enum.GetValues<BuildingType>().Where(t => Required(t) == discovery);

    public static Age AgeOf(Colony colony)
    {
        if (colony.Known.Keys.Count(d => Info(d).Tier == 3) >= 3)
            return Age.Town;
        if (colony.Known.Keys.Count(d => Info(d).Tier == 2) >= 3)
            return Age.Village;
        return Has(colony, Discovery.Metallurgy) ? Age.Iron : Age.Wood;
    }

    /// <summary>Points de savoir que la colonie accumule chaque jour.</summary>
    public static float DailyPoints(Colony colony)
    {
        float points = 0f;
        foreach (Colonist member in colony.PresentMembers)
            points += member.Stage switch
            {
                LifeStage.Adult => AdultPoints * (1f + 0.4f * member.Personality[Axis.Curiosite]),
                LifeStage.Elder => ElderPoints,
                LifeStage.Teen => TeenPoints,
                _ => 0f,
            };
        if (Civic.Has(colony, BuildingType.School))
            points *= SchoolFactor;
        if (Has(colony, Discovery.Philosophy))
            points *= PhilosophyFactor;
        return Math.Max(MinDailyPoints, points);
    }

    /// <summary>Points déjà accumulés sur un savoir (0 s'il n'a pas été entamé).</summary>
    public static float Progress(Colony colony, Discovery discovery) => colony.ResearchProgress.GetValueOrDefault(discovery);

    /// <summary>Jours qu'il faudra encore pour découvrir le savoir étudié (null s'il n'y en a pas).</summary>
    public static float? DaysLeft(Colony colony) => colony.Researching is { } target
        ? Math.Max(0f, CostFor(colony, target) - Progress(colony, target)) / DailyPoints(colony)
        : null;

    /// <summary>
    /// Ce que la colonie voudrait savoir faire : le savoir du prochain atelier ou bâtiment du village qu'elle bâtirait si elle le connaissait,
    /// ou de quoi arroser ses champs secs. Null si rien ne lui manque.
    /// </summary>
    public static Discovery? Wish(WorldState world, Colony colony)
    {
        if (FoodChain.NextWorkshopToBuild(colony, colony.Map) is { } food && !Allows(colony, food))
            return Required(food);
        if (ToolChain.NextWorkshopToBuild(colony) is { } iron && !Allows(colony, iron))
            return Required(iron);
        foreach (BuildingType type in Civic.Candidates(colony))
            if (!Allows(colony, type))
                return Required(type);
        bool riverRegion = world.WorldMap.Grid[world.WorldMap.TileOf(colony)].River > 0;
        if (riverRegion && colony.Fields.Any(f => Irrigation.IrrigatedShare(colony.Map, f) < 0.5f))
            return Has(colony, Discovery.Irrigation) ? Discovery.Hydraulics : Discovery.Irrigation;
        return null;
    }

    /// <summary>Le premier savoir à étudier pour atteindre <paramref name="goal"/> : lui-même, ou ce qu'il demande et qu'on ignore encore.</summary>
    public static Discovery? StepToward(Colony colony, Discovery goal)
    {
        if (Has(colony, goal))
            return null;
        foreach (Discovery needed in Info(goal).Requires)
            if (StepToward(colony, needed) is { } step)
                return step;
        return goal;
    }

    /// <summary>
    /// Le savoir à étudier : ce qui manque à la colonie pour bâtir ce qu'elle veut, sinon celui qu'elle étudiait déjà,
    /// sinon le moins coûteux de ceux qu'elle peut aborder (à coût égal, dans l'ordre de l'arbre).
    /// </summary>
    public static Discovery? ChooseResearch(WorldState world, Colony colony)
    {
        if (Wish(world, colony) is { } wish && StepToward(colony, wish) is { } step)
            return step;
        if (colony.Researching is { } current && CanResearch(colony, current))
            return current;
        return All.Where(d => CanResearch(colony, d.Id))
            .OrderBy(d => d.Cost - Progress(colony, d.Id)).ThenBy(d => d.Id)
            .Select(d => (Discovery?)d.Id).FirstOrDefault();
    }

    /// <summary>Chaque matin : la colonie accumule du savoir et l'applique au savoir qu'elle étudie.</summary>
    public static void Daily(WorldState world, Colony colony)
    {
        if (colony.PresentMembers.Count == 0)
            return;
        colony.Researching = ChooseResearch(world, colony);
        if (colony.Researching is not { } target)
            return;
        float points = 0;
        foreach (Settlement place in colony.Settlements.Where(s => s.Status == SettlementStatus.Active))
        { using var scope = colony.UseSettlement(place); points += DailyPoints(colony); }
        AddProgress(colony, target, points, world.Clock, source: null);
    }

    /// <summary>Ajoute des points à un savoir ; à son coût, il est découvert et annoncé.</summary>
    private static void AddProgress(Colony colony, Discovery discovery, float points, GameClock clock, string? source)
    {
        float progress = Progress(colony, discovery) + points;
        if (progress < CostFor(colony, discovery))
        {
            colony.ResearchProgress[discovery] = progress;
            return;
        }
        Discover(colony, discovery, clock, source);
    }

    /// <summary>Un savoir est découvert : la colonie l'annonce, et peut-être un nouvel âge.</summary>
    public static void Discover(Colony colony, Discovery discovery, GameClock clock, string? source = null)
    {
        if (Has(colony, discovery))
            return;
        Age before = AgeOf(colony);
        colony.Known[discovery] = clock.Ticks;
        colony.ResearchProgress.Remove(discovery);
        if (colony.Researching == discovery)
            colony.Researching = null;
        foreach (Colonist colonist in colony.PresentMembers)
            colonist.Needs.Faith = Math.Min(1f, colonist.Needs.Faith + DiscoveryFaithGain);
        DiscoveryInfo info = Info(discovery);
        ColonyBrain.Say(colony, clock, source is null
            ? $"✦ Nos sages découvrent un savoir : {info.Name}. {info.Effect}"
            : $"✦ Les marchands de {source} nous apprennent un savoir : {info.Name}. {info.Effect}");
        Age after = AgeOf(colony);
        if (after > before)
            ColonyBrain.Say(colony, clock, $"La colonie entre dans un nouvel âge : {AgeName(after)}.");
    }

    /// <summary>
    /// Une caravane arrive : chacun apprend un peu de ce que l'autre sait (une part du coût de chaque savoir qu'il peut aborder),
    /// deux fois plus entre alliés.
    /// </summary>
    public static void Share(WorldState world, Colony a, Colony b)
    {
        float share = TradeShare * (Diplomacy.AreAllied(world, a, b) ? 2f : 1f);
        Learn(a, b, share, world.Clock);
        Learn(b, a, share, world.Clock);
    }

    private static void Learn(Colony learner, Colony teacher, float share, GameClock clock)
    {
        foreach (Discovery discovery in teacher.Known.Keys.OrderBy(d => d).ToList())
            if (CanResearch(learner, discovery))
                AddProgress(learner, discovery, share * CostFor(learner, discovery), clock, teacher.Name);
    }
}
