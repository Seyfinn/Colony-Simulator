using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Ce que la colonie mesure d'elle-même à un instant donné. Le chauffage ne compte que le bois à brûler ;
/// le bois total inclut aussi celui qu'attendent les chantiers.
/// </summary>
public sealed record ColonySensors(
    float FoodDays, float FoodPressure,
    float HeatingPressure,
    float WoodTarget, float WoodPressure,
    int Homeless, float HousingPressure, bool HasConstructionSite,
    float StonePressure)
{
    /// <summary>Étage 1 de la pyramide : nourriture et chauffage.</summary>
    public bool SurvivalAssured => Math.Max(FoodPressure, HeatingPressure) <= 60f;
}

/// <summary>Une pensée de la colonie, datée, en langage clair.</summary>
public sealed record Thought(long Ticks, string Text);

/// <summary>
/// Le cerveau de la colonie, consulté chaque heure de jeu :
/// 1. les capteurs mesurent les réserves et les transforment en pressions de 0 à 100 ;
/// 2. la pyramide des priorités décide quels secteurs ont le droit d'avoir des bras :
///    survie (nourriture, chauffage), puis logement, puis réserves de pierre ;
/// 3. la main-d'œuvre est répartie en pourcentages, puis chaque poste va au plus doué ;
/// 4. la colonie explique ses décisions dans ses pensées.
/// </summary>
public static class ColonyBrain
{
    /// <summary>Repas consommés par colon et par jour (voir la faim dans ColonistAI).</summary>
    public const float MealsPerColonistPerDay = 1.6f;

    private const float FoodTargetDays = 6f;
    /// <summary>En automne, on vise de plus grosses réserves : les baies ne repoussent pas l'hiver.</summary>
    private const float AutumnFoodTargetDays = 10f;
    private const float FoodCrisisDays = 1f;
    private const int WoodBaseReserve = 20;
    private const int StoneReserveTarget = 60;

    // Part maximale de la main-d'œuvre que chaque secteur peut mobiliser.
    private const float MaxFoodShare = 0.9f;
    private const float MaxWoodShare = 0.5f;
    private const float MaxStoneShare = 0.4f;
    private const float MaxConstructionShare = 0.3f;

    /// <summary>Nombre de chantiers ouverts en même temps : un seul, deux si beaucoup de colons dorment dehors.</summary>
    private static int MaxConstructionSites(int homeless) => homeless > 8 ? 2 : 1;

    /// <summary>Les parts évoluent progressivement d'une heure à l'autre, pour éviter les revirements brutaux.</summary>
    private const float ShareSmoothing = 0.5f;

    private const int MaxThoughts = 30;

    /// <summary>Bois brûlé par colon et par nuit pour se chauffer, selon la saison.</summary>
    public static float FirewoodPerColonist(Season season) => season switch
    {
        Season.Printemps => 0.1f,
        Season.Ete => 0.05f,
        Season.Automne => 0.2f,
        _ => 0.4f,
    };

    public static bool IsColdSeason(Season season) => season is Season.Automne or Season.Hiver;

    public static void Think(Colony colony, LocalMap map, GameClock clock)
    {
        ColonySensors sensors = Sense(colony, clock);
        if (PlanConstruction(colony, map, sensors, clock))
            sensors = Sense(colony, clock);
        colony.Sensors = sensors;

        Dictionary<WorkSector, float> target = DecideShares(sensors);
        foreach (WorkSector sector in WorkSectors.All)
            colony.WorkShares[sector] += (target[sector] - colony.WorkShares[sector]) * ShareSmoothing;
        colony.AssignSectors();

        Narrate(colony, sensors, clock);
    }

    /// <summary>
    /// Étage 2 de la pyramide : une fois la survie assurée, si des colons dorment dehors,
    /// la colonie ouvre un chantier de hutte. Renvoie true si un chantier a été ouvert.
    /// </summary>
    private static bool PlanConstruction(Colony colony, LocalMap map, ColonySensors sensors, GameClock clock)
    {
        if (!sensors.SurvivalAssured || sensors.Homeless == 0)
            return false;
        int sites = colony.ConstructionSites.Count();
        // Les places des huttes en chantier comptent déjà comme des toits à venir.
        int stillUnplanned = sensors.Homeless - sites * Building.HutCapacity;
        if (stillUnplanned <= 0 || sites >= MaxConstructionSites(sensors.Homeless))
            return false;
        if (Urbanism.FindHutSite(map, colony) is not { } site)
            return false;

        Urbanism.PlanHut(map, colony, site.X, site.Y);
        Say(colony, clock, colony.Buildings.Count == 1
            ? $"{sensors.Homeless} colons dorment à la belle étoile : nous décidons de bâtir notre première hutte."
            : $"Encore {sensors.Homeless} colons sans toit : nous ouvrons le chantier d'une nouvelle hutte.");
        return true;
    }

    /// <summary>Une hutte vient d'être achevée : des colons s'y installent.</summary>
    public static void OnBuildingComplete(Colony colony, Building building, GameClock clock)
    {
        colony.Labor.RecordHut(LaborLedger.TicksToHours(building.LaborTicks));
        colony.MoveIn(building);
        int homeless = colony.Homeless;
        Say(colony, clock, homeless > 0
            ? $"Une hutte est achevée : {building.Residents.Count} colons y dorment désormais à l'abri ({homeless} encore dehors)."
            : "Une hutte est achevée : tout le monde dort désormais à l'abri !");
    }

    /// <summary>Allume le feu à la tombée de la nuit, s'il reste assez de bois.</summary>
    public static void LightFire(Colony colony, GameClock clock)
    {
        int needed = (int)MathF.Ceiling(colony.Members.Count * FirewoodPerColonist(clock.Season));
        colony.FireLit = colony.Stock.TryTake(ResourceType.Wood, needed);
        if (!colony.FireLit && IsColdSeason(clock.Season))
            Say(colony, clock, "Le feu s'est éteint faute de bois : la nuit sera froide et le sommeil mauvais.");
    }

    public static ColonySensors Sense(Colony colony, GameClock clock)
    {
        int population = Math.Max(1, colony.Members.Count);

        float dailyMeals = population * MealsPerColonistPerDay;
        float foodDays = colony.Stock.Get(ResourceType.Food) / dailyMeals;
        float foodTarget = clock.Season == Season.Automne ? AutumnFoodTargetDays : FoodTargetDays;
        float foodPressure = Pressure(foodTarget - foodDays, foodTarget - FoodCrisisDays);

        // Chauffage : trois nuits d'avance, plus tout l'hiver si l'on est en automne (anticipation).
        int wood = colony.Stock.Get(ResourceType.Wood);
        float nightly = population * FirewoodPerColonist(clock.Season);
        float heatingTarget = WoodBaseReserve + 3 * nightly;
        if (clock.Season == Season.Automne)
            heatingTarget += TimeConstants.DaysPerSeason * population * FirewoodPerColonist(Season.Hiver);
        float heatingPressure = Pressure(heatingTarget - wood, heatingTarget);

        // Bois total : le chauffage plus ce que les chantiers attendent encore.
        float woodTarget = heatingTarget + colony.ConstructionSites.Sum(b => b.WoodStillToBring);
        float woodPressure = Pressure(woodTarget - wood, woodTarget);

        int homeless = colony.Homeless;
        float housingPressure = homeless * 100f / population;
        bool hasSite = colony.ConstructionSites.Any();

        float stonePressure = Pressure(StoneReserveTarget - colony.Stock.Get(ResourceType.Stone), StoneReserveTarget);

        return new ColonySensors(foodDays, foodPressure, heatingPressure, woodTarget, woodPressure,
            homeless, housingPressure, hasSite, stonePressure);
    }

    /// <summary>
    /// La pyramide : la survie (nourriture, chauffage) d'abord ; le logement une fois la survie assurée ;
    /// les réserves de pierre quand presque tout le monde est logé ; le temps libre pour le reste.
    /// </summary>
    public static Dictionary<WorkSector, float> DecideShares(ColonySensors sensors)
    {
        float food = sensors.FoodPressure / 100f * MaxFoodShare;
        float wood = sensors.WoodPressure / 100f * MaxWoodShare;
        float construction = sensors.SurvivalAssured && sensors.HasConstructionSite
            ? Math.Max(0.1f, sensors.HousingPressure / 100f) * MaxConstructionShare
            : 0f;
        bool comfortAssured = sensors.SurvivalAssured && sensors.HousingPressure <= 25f;
        float stone = comfortAssured ? sensors.StonePressure / 100f * MaxStoneShare : 0f;

        // Survie menacée : pas de temps libre, tous les bras disponibles vont aux besoins vitaux.
        float vital = food + wood;
        if (!sensors.SurvivalAssured && vital > 0f)
        {
            float spare = 1f - (food + wood + construction + stone);
            if (spare > 0f)
            {
                food += spare * food / vital;
                wood += spare * wood / vital;
            }
        }

        float total = food + wood + construction + stone;
        if (total > 1f)
        {
            food /= total;
            wood /= total;
            construction /= total;
            stone /= total;
            total = 1f;
        }
        return new Dictionary<WorkSector, float>
        {
            [WorkSector.Food] = food,
            [WorkSector.Wood] = wood,
            [WorkSector.Stone] = stone,
            [WorkSector.Construction] = construction,
            [WorkSector.Free] = 1f - total,
        };
    }

    /// <summary>Convertit un manque en pression : 0 quand rien ne manque, 100 quand le manque atteint <paramref name="full"/>.</summary>
    private static float Pressure(float shortfall, float full) => Math.Clamp(shortfall / full, 0f, 1f) * 100f;

    // --- Les pensées de la colonie ---

    private static void Narrate(Colony colony, ColonySensors sensors, GameClock clock)
    {
        int Workers(WorkSector sector) => colony.Members.Count(m => m.Sector == sector);

        string foodBand = sensors.FoodDays < 2 ? "crise"
            : sensors.FoodDays < 3.5f ? "basse"
            : sensors.FoodDays > 4.5f ? "ok"
            : Announced(colony, "nourriture");
        if (Changed(colony, "nourriture", foodBand))
        {
            Say(colony, clock, foodBand switch
            {
                "crise" => $"Il ne reste que {sensors.FoodDays:0.#} jours de nourriture ! {Workers(WorkSector.Food)} colons partent cueillir.",
                "basse" => $"Les réserves de nourriture baissent ({sensors.FoodDays:0} jours) : {Workers(WorkSector.Food)} colons à la cueillette.",
                _ => $"Nos réserves de nourriture sont confortables ({sensors.FoodDays:0} jours).",
            });
        }

        // L'arrivée de l'automne change le discours sur le bois : on pense à l'hiver.
        string woodBand = sensors.WoodPressure > 60 ? "manque" : sensors.WoodPressure > 20 ? "besoin" : "ok";
        if (woodBand != "ok" && clock.Season == Season.Automne)
            woodBand += "-automne";
        if (Changed(colony, "bois", woodBand))
        {
            Say(colony, clock, woodBand switch
            {
                "manque-automne" =>
                    $"L'hiver approche et le bois manque : {Workers(WorkSector.Wood)} colons coupent du bois de chauffage.",
                "manque" => $"Nous manquons de bois : {Workers(WorkSector.Wood)} colons partent en forêt.",
                "besoin-automne" =>
                    $"L'hiver approche : nous faisons des réserves de bois ({colony.Stock.Get(ResourceType.Wood)} sur {sensors.WoodTarget:0}).",
                "besoin" => $"Il nous faudra un peu plus de bois : {Workers(WorkSector.Wood)} colons y travaillent.",
                _ => "Nous avons assez de bois pour nous chauffer.",
            });
        }

        // Zones neutres : entre deux situations, on garde le dernier avis plutôt que d'en changer sans cesse.
        int miners = Workers(WorkSector.Stone);
        string stoneBand = miners >= 2 ? "carrière" : miners == 0 ? "arrêt" : Announced(colony, "pierre");
        if (Changed(colony, "pierre", stoneBand))
        {
            Say(colony, clock, stoneBand == "carrière"
                ? $"Nos besoins vitaux sont couverts : {Workers(WorkSector.Stone)} colons travaillent à la carrière."
                : "La carrière attendra : les besoins vitaux passent d'abord.");
        }

        // Bilan de saison : ce que coûte chaque ressource en heures de travail.
        if (Changed(colony, "saison", clock.Season.ToString()) && colony.Labor.HoursPerUnit(ResourceType.Food) is not null)
            Say(colony, clock, "Bilan de saison, en heures de travail par unité : " + CostSummary(colony.Labor) + ".");

        int free = Workers(WorkSector.Free);
        float freeRatio = free / (float)Math.Max(1, colony.Members.Count);
        string freeBand = free == 0 ? "aucun"
            : freeRatio < 0.4f ? "quelques"
            : freeRatio > 0.6f ? "beaucoup"
            : Announced(colony, "temps libre");
        if (Changed(colony, "temps libre", freeBand) && free > 0)
            Say(colony, clock, $"Tout va bien : {free} colons profitent de leur temps libre.");
    }

    public static string CostSummary(LaborLedger labor)
    {
        var parts = new List<string>();
        void Add(ResourceType type, string name)
        {
            if (labor.HoursPerUnit(type) is { } hours)
                parts.Add($"{name} {hours:0.0} h");
        }
        Add(ResourceType.Food, "nourriture");
        Add(ResourceType.Wood, "bois");
        Add(ResourceType.Stone, "pierre");
        Add(ResourceType.IronOre, "minerai de fer");
        if (labor.HoursPerHut is { } hut)
            parts.Add($"hutte {hut:0} h");
        return parts.Count == 0 ? "pas encore mesuré" : string.Join(", ", parts);
    }

    private static string Announced(Colony colony, string topic) =>
        colony.NarrationState.TryGetValue(topic, out NarrationTopic? state) ? state.Announced : "";

    /// <summary>
    /// Une nouvelle situation n'est annoncée que si elle dure depuis quelques heures, pour éviter de radoter
    /// à chaque petite variation. Les urgences (famine, manque de bois) sont annoncées tout de suite.
    /// </summary>
    private static bool Changed(Colony colony, string topic, string band)
    {
        if (!colony.NarrationState.TryGetValue(topic, out NarrationTopic? state))
        {
            colony.NarrationState[topic] = new NarrationTopic { Announced = band, Candidate = band };
            return true;
        }
        if (band == state.Announced)
        {
            state.Candidate = band;
            state.Hours = 0;
            return false;
        }
        if (band == state.Candidate)
            state.Hours++;
        else
        {
            state.Candidate = band;
            state.Hours = 1;
        }

        bool urgent = band is "crise" or "manque" or "manque-automne";
        if (!urgent && state.Hours < HoursBeforeAnnouncing)
            return false;
        state.Announced = band;
        state.Hours = 0;
        return true;
    }

    private const int HoursBeforeAnnouncing = 3;

    internal sealed class NarrationTopic
    {
        public string Announced { get; set; } = "";
        public string Candidate { get; set; } = "";
        public int Hours { get; set; }
    }

    private static void Say(Colony colony, GameClock clock, string text)
    {
        colony.Thoughts.Add(new Thought(clock.Ticks, text));
        if (colony.Thoughts.Count > MaxThoughts)
            colony.Thoughts.RemoveAt(0);
    }
}
