using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les bâtiments de la vie du village, au-delà des huttes et des ateliers du fer et du blé : l'enclos, le métier à tisser,
/// le marché, le puits, l'entrepôt, l'infirmerie, la taverne et l'école. Ici, la colonie décide lequel bâtir, et l'on trouve
/// ce que chacun apporte (réserves protégées de la pourriture, santé, moral, apprentissage).
/// </summary>
public static class Civic
{
    /// <summary>Capacité de stockage de base des vivres qui se gâtent, par habitant et en tout.</summary>
    private const int BaseStorage = 60;
    private const int StoragePerColonist = 8;
    private const int StoragePerStorehouse = 150;
    private const float SpoilRate = 0.15f;

    /// <summary>Vitesse d'apprentissage gagnée par une colonie qui a une école.</summary>
    public const float SchoolLearningBonus = 1.25f;

    public static bool Has(Colony colony, BuildingType type) => colony.Buildings.Any(b => b.Type == type && b.IsComplete);

    private static bool Planned(Colony colony, BuildingType type) => colony.Buildings.Any(b => b.Type == type);

    public static Building? Site(Colony colony, BuildingType type) => colony.Buildings.FirstOrDefault(b => b.Type == type && b.IsComplete);

    /// <summary>
    /// Le prochain bâtiment de la vie du village à bâtir, ou null. Un seul de chaque, un chantier à la fois. La colonie passe
    /// ceux qu'elle ne sait pas encore bâtir (voir <see cref="Knowledge"/>) : elle les étudie en attendant.
    /// </summary>
    public static BuildingType? NextToBuild(Colony colony)
    {
        // Un type que le terrain a déjà refusé (sans événement qui change la donne) ne bloque pas les suivants.
        foreach (BuildingType type in Candidates(colony))
            if (Knowledge.Allows(colony, type) && !SettlementPlanner.IsKnownImpossible(colony, type))
                return type;
        return null;
    }

    /// <summary>Les bâtiments du village que la colonie voudrait, du plus pressé au moins pressé, qu'elle sache les bâtir ou non.</summary>
    public static IEnumerable<BuildingType> Candidates(Colony colony)
    {
        int people = colony.PresentMembers.Count;
        if (people < 6)
            yield break;

        if (!Planned(colony, BuildingType.Pen) && (colony.Fields.Count > 0 || colony.Stock.Get(ResourceType.Grain) >= 10))
            yield return BuildingType.Pen;
        if (!Planned(colony, BuildingType.Well) && people >= 8)
            yield return BuildingType.Well;
        if (!Planned(colony, BuildingType.Loom) && Has(colony, BuildingType.Pen)
            && (colony.Stock.Get(ResourceType.Wool) >= 2 || colony.WoolReady >= 2f))
            yield return BuildingType.Loom;
        if (!Planned(colony, BuildingType.Storehouse) && (people >= 14 || Perishables(colony) >= StorageCapacity(colony) / 2))
            yield return BuildingType.Storehouse;
        if (!Planned(colony, BuildingType.Infirmary) && ((colony.IllnessCases >= 2 && people >= 7) || people >= 12))
            yield return BuildingType.Infirmary;
        if (!Planned(colony, BuildingType.Market) && people >= 8 && (colony.Trades.Count > 0 || people >= 14))
            yield return BuildingType.Market;
        if (!Planned(colony, BuildingType.Tavern) && people >= 12)
            yield return BuildingType.Tavern;
        if (Cuisine.WantsCask(colony))
            yield return BuildingType.Cask;
        if (!Planned(colony, BuildingType.School) && (colony.Children >= 3 || people >= 16))
            yield return BuildingType.School;
        if (Offerings.WantsShrine(colony))
            yield return BuildingType.Shrine;
        // Un village prospère agrandit son élevage : un enclos de plus quand les premiers débordent (voir Husbandry.WantsAnotherPen).
        if (Husbandry.WantsAnotherPen(colony))
            yield return BuildingType.Pen;
    }

    public static string Announcement(BuildingType type) => type switch
    {
        BuildingType.Pen => "Nous bâtissons un enclos pour élever des poules et des moutons.",
        BuildingType.Well => "Nous creusons un puits : de l'eau saine, contre les fièvres, le feu et la sécheresse.",
        BuildingType.Loom => "Nous avons de la laine : nous bâtissons un métier à tisser pour en faire des vêtements.",
        BuildingType.Storehouse => "Nos vivres commencent à se gâter faute de place : nous bâtissons un entrepôt.",
        BuildingType.Infirmary => "Les fièvres et les blessures nous coûtent cher : nous bâtissons une infirmerie.",
        BuildingType.Market => "Le commerce prospère : nous bâtissons un marché pour y troquer avec les nomades de la région.",
        BuildingType.Tavern => "Les habitants méritent de souffler : nous bâtissons une taverne.",
        BuildingType.Cask => "La taverne a soif : nous bâtissons un fût où fermenteront nos céréales en bière.",
        BuildingType.Shrine => "Nous voulons honorer les dieux : nous bâtissons un sanctuaire de pierre où préparer une offrande.",
        _ => "Les enfants grandissent : nous bâtissons une école.",
    };

    // --- Stockage ---

    /// <summary>Vivres qui se gâtent quand on les entasse.</summary>
    public static int Perishables(Colony colony) =>
        colony.Stock.Get(ResourceType.Grain) + colony.Stock.Get(ResourceType.Flour)
        + colony.Stock.Get(ResourceType.Bread) + colony.Stock.Get(ResourceType.Eggs) + colony.Stock.Get(ResourceType.Milk);

    public static int StorageCapacity(Colony colony)
    {
        int capacity = BaseStorage + StoragePerColonist * colony.PresentMembers.Count
            + StoragePerStorehouse * colony.Buildings.Count(b => b.Type == BuildingType.Storehouse && b.IsComplete);
        return Specialties.Salted(colony) ? capacity * 13 / 10 : capacity;
    }

    /// <summary>Chaque matin, ce qui dépasse la capacité de stockage pourrit en partie (grain d'abord, puis farine et pain).</summary>
    public static void Daily(Colony colony, GameClock clock)
    {
        PreserveMeat(colony, clock);
        int excess = Perishables(colony) - StorageCapacity(colony);
        if (excess <= 0)
            return;
        int rot = Math.Max(1, (int)MathF.Ceiling(excess * SpoilRate * (Specialties.Salted(colony) ? 0.5f : 1f)));
        int lost = 0;
        foreach (ResourceType good in new[] { ResourceType.Milk, ResourceType.Grain, ResourceType.Flour, ResourceType.Eggs, ResourceType.Bread })
        {
            int take = Math.Min(rot - lost, colony.Stock.Get(good));
            if (take > 0 && colony.Stock.TryTake(good, take, ResourceFlow.Loss))
                lost += take;
            if (lost >= rot)
                break;
        }
        if (lost > 0 && clock.TotalDays != colony.LastSpoilageThoughtDay)
        {
            colony.LastSpoilageThoughtDay = clock.TotalDays;
            ColonyBrain.Say(colony, clock, $"Faute de place, {lost} vivres pourrissent au grenier.");
        }
    }

    /// <summary>Viande que garde une unité de sel.</summary>
    public const int MeatPerSalt = 8;

    /// <summary>Part de la viande fraîche trop vieille qui se gâte chaque jour.</summary>
    private const float MeatSpoilRate = 0.5f;

    /// <summary>Jours que la viande fraîche se garde avant de commencer à se gâter : trois, ou sept avec un entrepôt.</summary>
    public static int MeatShelfDays(Colony colony) => (Has(colony, BuildingType.Storehouse) ? 7 : 3)
        + (colony.LocalSettlement.Equipment.PotteryExpiry.Count >= Math.Max(1, (colony.PresentMembers.Count + 3) / 4) ? 1 : 0);

    /// <summary>
    /// Chaque matin, la viande fraîche est mise au sel tant qu'il y en a (une unité de sel pour huit de viande) : la viande salée se garde
    /// indéfiniment. Ce qui reste vieillit d'un jour ; passé <see cref="MeatShelfDays"/> jours (3, ou 7 avec un entrepôt), la viande commence à se gâter, pour moitié
    /// chaque jour : une grosse bête abattue sans sel doit être mangée vite.
    /// </summary>
    public static void PreserveMeat(Colony colony, GameClock clock)
    {
        int meat = colony.Stock.Available(ResourceType.Meat);
        if (colony.Stock.Get(ResourceType.Meat) == 0)
            return;
        int salted = (int)Math.Min(meat, (long)colony.Stock.Available(ResourceType.Salt) * MeatPerSalt);
        if (salted > 0)
        {
            colony.Stock.TryTake(ResourceType.Salt, (salted + MeatPerSalt - 1) / MeatPerSalt);
            colony.Stock.TryTake(ResourceType.Meat, salted);
            colony.Stock.Add(ResourceType.SaltedMeat, salted);
        }
        colony.Stock.AgeMeat();
        int rotten = colony.Stock.SpoilMeat(MeatShelfDays(colony), MeatSpoilRate);
        if ((salted > 0 || rotten >= 6) && clock.TotalDays != colony.LastMeatThoughtDay)
        {
            colony.LastMeatThoughtDay = clock.TotalDays;
            ColonyBrain.Say(colony, clock, (salted > 0 ? $"{salted} viandes sont mises au sel pour les mauvais jours." : "")
                + (salted > 0 && rotten >= 6 ? " " : "") + (rotten >= 6 ? $"{rotten} repas de viande trop vieille se gâtent." : ""));
        }
    }

    // --- Effets ---

    /// <summary>L'école accélère l'apprentissage de tous.</summary>
    public static float LearningBonus(Colony colony) => Has(colony, BuildingType.School) ? SchoolLearningBonus : 1f;

    /// <summary>Les enfants vont à l'école de 9 h à 13 h : ils y exercent le métier où ils sont les plus doués.</summary>
    public static bool IsSchoolTime(GameClock clock) => clock.Hour is >= 9 and < 13;

    /// <summary>Confort ajouté par ce qui rend la vie agréable : les vêtements l'hiver, les épices à table.</summary>
    public static float ComfortBonus(Colony colony, bool cold) =>
        (cold ? 0.1f * Husbandry.ClothesCoverage(colony) : 0f) + (Specialties.Spiced(colony) ? 0.05f : 0f);

    /// <summary>
    /// Les travaux de l'élevage, du textile, du négoce et des soins réclament quelques bras : on les prend sur le temps libre,
    /// jamais sur les besoins vitaux.
    /// </summary>
    public static void ClaimIdleHands(Colony colony, Dictionary<WorkSector, float> target)
    {
        int workers = Math.Max(1, colony.Workers.Count());

        void Claim(WorkSector sector, int hands)
        {
            if (hands <= 0)
                return;
            float wanted = hands / (float)workers;
            float add = Math.Min(target[WorkSector.Free], Math.Max(0f, wanted - target[sector]));
            target[sector] += add;
            target[WorkSector.Free] -= add;
        }

        Claim(WorkSector.Farm, Husbandry.WorkPending(colony) || Husbandry.SlaughterPending(colony) ? 1 : 0);
        int craft = (Husbandry.PickLoomJob(colony) is not null ? 1 : 0) + (Specialties.PickMarketJob(colony) is not null ? 1 : 0)
            + (Has(colony, BuildingType.Infirmary) && Health.PatientCount(colony) > 0 ? 1 : 0) + (Cuisine.PickJob(colony) is not null ? 1 : 0);
        Claim(WorkSector.Craft, craft);
    }

    // --- Incendie ---

    /// <summary>Un bâtiment achevé brûle : la colonie en perd le toit ou l'atelier, et une part de son bois.</summary>
    public static void Burn(Colony colony, Building building, GameClock clock)
    {
        colony.RecordEvent(new(ColonyEventKind.Fire, building.X, building.Y, clock.Ticks, ColonyEventOutcome.Ruined, building.Type));
        colony.Buildings.Remove(building);
        SettlementPlanner.OnObjectRemoved(colony, building);
        foreach (Colonist resident in building.Residents.ToList())
            resident.Home = null;
        building.Residents.Clear();
        colony.FillVacancies();
        int wood = colony.Stock.Get(ResourceType.Wood);
        colony.Stock.TryTake(ResourceType.Wood, wood / 4, ResourceFlow.Loss);
        ColonyBrain.Say(colony, clock, $"Un incendie ravage {Building.WithArticle(building.Type)} : il est réduit en cendres !");
    }

    /// <summary>L'emplacement d'un bâtiment de la vie du village : le type précis compte (le fût près de sa taverne, l'entrepôt près du besoin qu'il sert).</summary>
    internal static (int X, int Y)? FindSite(LocalMap map, Colony colony, BuildingType type) => Urbanism.FindSite(map, colony, type);
}
