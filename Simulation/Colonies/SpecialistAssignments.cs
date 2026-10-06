using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les spécialistes : un artisan garde de préférence l'atelier qu'il pratique, tant que la demande et l'approvisionnement y sont réguliers. La préférence guide le choix à l'intérieur
/// du secteur d'artisanat (les quotas <see cref="Settlement.WorkShares"/> et les compétences restent la vérité) ; elle ne réserve aucun métier, cède devant la survie, la faim,
/// l'épuisement et les urgences, et ne s'accroche jamais à un travail impossible. Aucun tirage : les départages se font par compétence puis par identifiant.
/// </summary>
public static class SpecialistAssignments
{
    /// <summary>Jours de demande regardés pour répartir les artisans, et jours pendant lesquels un choix tient sauf impossibilité.</summary>
    public const int HorizonDays = 3;
    public const int MinHoldDays = 3;

    /// <summary>Gain de travail estimé au-delà duquel un artisan change d'atelier, et jours d'impossibilité qui lui font abandonner le sien.</summary>
    public const double ChangeGain = 0.15;
    public const int BlockedDaysToDrop = 1;

    private static readonly BuildingType[] Crafts =
    [
        BuildingType.Kiln, BuildingType.Bloomery, BuildingType.Forge, BuildingType.Mill, BuildingType.Oven,
        BuildingType.Loom, BuildingType.PotteryKiln, BuildingType.Tannery, BuildingType.Goldsmith,
    ];

    /// <summary>L'atelier principal préféré de ce colon, s'il existe encore dans ce lieu (sinon null).</summary>
    public static Building? PreferredWorkshop(Colony colony, Colonist colonist) =>
        colonist.PreferredWorkshopId != 0 && colony.BuildingById(colonist.PreferredWorkshopId) is { IsComplete: true, IsExtension: false } shop ? shop : null;

    /// <summary>Les préférences ne guident le choix qu'en temps ordinaire : jamais quand la survie ou la nourriture sont en jeu.</summary>
    public static bool Applies(Colony colony) =>
        colony.Sensors is { SurvivalAssured: true } sensors && sensors.FoodDays >= SettlementRules.ComfortFoodDays;

    /// <summary>
    /// Parmi les travaux recevables, dans l'ordre de priorité de la colonie, celui que ce colon préfère s'il est recevable ; sinon le premier. Un travail préféré
    /// impossible ouvre (une fois) la mesure de son blocage ; le retrouver la referme.
    /// </summary>
    internal static CraftCandidate? Choose(Colony colony, Colonist colonist, IReadOnlyList<CraftCandidate> candidates, long now)
    {
        if (PreferredWorkshop(colony, colonist) is not { } preferred)
            return candidates.Count == 0 ? null : candidates[0];
        foreach (CraftCandidate job in candidates)
            if (job.Workshop == preferred && !job.Sculpt && (colonist.PreferredProduct is null || job.Product is null || job.Product == colonist.PreferredProduct))
            {
                colonist.SpecialtyBlockedSinceTicks = 0;
                return job;
            }
        if (colonist.SpecialtyBlockedSinceTicks == 0)
            colonist.SpecialtyBlockedSinceTicks = now;
        return candidates.Count == 0 ? null : candidates[0];
    }

    /// <summary>Le colon quitte (déménagement, départ, mort) : sa préférence locale n'a plus de sens.</summary>
    internal static void Invalidate(Colonist colonist)
    {
        colonist.PreferredWorkshopId = 0;
        colonist.PreferredProduct = null;
        colonist.SpecialtyChosenTicks = 0;
        colonist.SpecialtyBlockedSinceTicks = 0;
    }

    /// <summary>Heures de travail que la demande des trois prochains jours réclame à cet atelier (au plus ce que ses postes peuvent fournir).</summary>
    private static double Workload(Colony colony, Building shop)
    {
        Recipe recipe = Crafting.RecipeFor(colony, shop.Type);
        int wanted = (int)MathF.Ceiling(Economy.Need(colony, recipe.Output)) + colony.ExportInterest.GetValueOrDefault(recipe.Output);
        int net = Math.Max(0, wanted - colony.Stock.Available(recipe.Output) - Crafting.Expected(colony, recipe.Output));
        double needed = Math.Ceiling(net / (double)recipe.OutputAmount) * recipe.Seconds * ScaleRules.HoursPerSecond;
        double capacity = HorizonDays * Trade.WorkHoursPerDay * (BatchProduction.IsEligible(shop.Type) ? WorkshopCapacity.Slots(colony, shop) : 1);
        return Math.Min(needed, capacity);
    }

    /// <summary>
    /// Chaque jour : répartit les artisans sur les ateliers qui ont du travail (demande des trois jours, un travailleur de plus seulement là où il reste le plus à faire).
    /// Un choix tient trois jours sauf impossibilité, départ ou blocage d'un jour ; un gain estimé au-delà de 15 % le change.
    /// </summary>
    internal static void RefreshDaily(Colony colony, GameClock clock)
    {
        long now = clock.Ticks;
        foreach (Colonist colonist in colony.PresentMembers)
            if (colonist.PreferredWorkshopId != 0 && (PreferredWorkshop(colony, colonist) is null || colonist.Stage != LifeStage.Adult))
                Invalidate(colonist);

        var loads = new List<(Building Shop, double Hours)>();
        foreach (Building shop in colony.Buildings.Where(b => b.IsComplete && !b.IsExtension && Array.IndexOf(Crafts, b.Type) >= 0).OrderBy(b => b.Id))
            if (Workload(colony, shop) is var hours and > 0)
                loads.Add((shop, hours));
        double HoursOf(Building shop) => loads.FirstOrDefault(l => l.Shop == shop).Hours;

        List<Colonist> crafters = colony.PresentMembers
            .Where(c => c.Stage == LifeStage.Adult && c.Sector == WorkSector.Craft && c.Transit == TransitState.None).OrderBy(c => c.Id).ToList();
        var assigned = new Dictionary<Building, int>();
        void Count(Building shop, int delta) => assigned[shop] = assigned.GetValueOrDefault(shop) + delta;

        // Ceux qui ont déjà un atelier le gardent, sauf impossibilité, travail disparu ou gain net d'un changement.
        foreach (Colonist colonist in crafters.Where(c => c.PreferredWorkshopId != 0).ToList())
        {
            Building shop = PreferredWorkshop(colony, colonist)!;
            bool blocked = colonist.SpecialtyBlockedSinceTicks != 0 && now - colonist.SpecialtyBlockedSinceTicks >= BlockedDaysToDrop * TimeConstants.TicksPerDay;
            if (blocked || HoursOf(shop) <= 0)
            {
                Invalidate(colonist);
                continue;
            }
            Count(shop, 1);
        }
        foreach (Colonist colonist in crafters.Where(c => c.PreferredWorkshopId != 0 && now - c.SpecialtyChosenTicks >= MinHoldDays * TimeConstants.TicksPerDay).ToList())
        {
            Building shop = PreferredWorkshop(colony, colonist)!;
            double current = HoursOf(shop) / Math.Max(1, assigned.GetValueOrDefault(shop));
            Building? better = loads.Where(l => l.Shop != shop).OrderByDescending(l => l.Hours / (assigned.GetValueOrDefault(l.Shop) + 1)).ThenBy(l => l.Shop.Id)
                .Select(l => l.Shop).FirstOrDefault();
            if (better is null || HoursOf(better) / (assigned.GetValueOrDefault(better) + 1) <= current * (1 + ChangeGain))
                continue;
            Count(shop, -1); Count(better, 1);
            Assign(colonist, better, now);
        }

        // Les autres : l'atelier qui a le plus de travail par artisan, au meilleur de la compétence (puis de l'identifiant).
        List<Colonist> free = crafters.Where(c => c.PreferredWorkshopId == 0).ToList();
        while (free.Count > 0 && loads.Count > 0)
        {
            Building target = loads.OrderByDescending(l => l.Hours / (assigned.GetValueOrDefault(l.Shop) + 1)).ThenBy(l => l.Shop.Id).First().Shop;
            Colonist best = free.OrderByDescending(c => c.Skills.Level(Crafting.SkillFor(target.Type))).ThenBy(c => c.Id).First();
            free.Remove(best);
            Count(target, 1);
            Assign(best, target, now);
        }
    }

    private static void Assign(Colonist colonist, Building shop, long now)
    {
        colonist.PreferredWorkshopId = shop.Id;
        colonist.PreferredProduct = null;
        colonist.SpecialtyChosenTicks = now;
        colonist.SpecialtyBlockedSinceTicks = 0;
    }
}

/// <summary>Un travail d'atelier recevable : l'atelier, le produit visé s'il y en a un, ou le chantier d'une offrande.</summary>
internal readonly record struct CraftCandidate(Building Workshop, ResourceType? Product, bool Sculpt = false);
