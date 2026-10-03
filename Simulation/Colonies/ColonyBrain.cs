using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Ce que la colonie mesure d'elle-même à un instant donné.</summary>
public sealed record ColonySensors(
    float FoodDays, float FoodPressure,
    float WoodTarget, float WoodPressure,
    float StonePressure);

/// <summary>Une pensée de la colonie, datée, en langage clair.</summary>
public sealed record Thought(long Ticks, string Text);

/// <summary>
/// Le cerveau de la colonie, consulté chaque heure de jeu :
/// 1. les capteurs mesurent les réserves et les transforment en pressions de 0 à 100 ;
/// 2. la pyramide des priorités décide quels secteurs ont le droit d'avoir des bras
///    (pas de carrière tant que la survie n'est pas assurée) ;
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

    /// <summary>Au-delà de cette pression sur un besoin vital, la colonie n'investit pas dans les étages supérieurs.</summary>
    private const float SurvivalFirstThreshold = 60f;

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

    public static void Think(Colony colony, GameClock clock)
    {
        ColonySensors sensors = Sense(colony, clock);
        colony.Sensors = sensors;

        Dictionary<WorkSector, float> target = DecideShares(sensors);
        foreach (WorkSector sector in WorkSectors.All)
            colony.WorkShares[sector] += (target[sector] - colony.WorkShares[sector]) * ShareSmoothing;
        colony.AssignSectors();

        Narrate(colony, sensors, clock);
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

        // Bois : trois nuits de chauffage d'avance, plus tout l'hiver si l'on est en automne (anticipation).
        float nightly = population * FirewoodPerColonist(clock.Season);
        float woodTarget = WoodBaseReserve + 3 * nightly;
        if (clock.Season == Season.Automne)
            woodTarget += TimeConstants.DaysPerSeason * population * FirewoodPerColonist(Season.Hiver);
        float woodPressure = Pressure(woodTarget - colony.Stock.Get(ResourceType.Wood), woodTarget);

        float stonePressure = Pressure(StoneReserveTarget - colony.Stock.Get(ResourceType.Stone), StoneReserveTarget);

        return new ColonySensors(foodDays, foodPressure, woodTarget, woodPressure, stonePressure);
    }

    /// <summary>La pyramide : la survie (nourriture, chauffage) d'abord, les réserves de pierre ensuite, le temps libre pour le reste.</summary>
    public static Dictionary<WorkSector, float> DecideShares(ColonySensors sensors)
    {
        float food = sensors.FoodPressure / 100f * MaxFoodShare;
        float wood = sensors.WoodPressure / 100f * MaxWoodShare;
        bool survivalAssured = Math.Max(sensors.FoodPressure, sensors.WoodPressure) <= SurvivalFirstThreshold;
        float stone = survivalAssured ? sensors.StonePressure / 100f * MaxStoneShare : 0f;

        float total = food + wood + stone;
        if (total > 1f)
        {
            food /= total;
            wood /= total;
            stone /= total;
            total = 1f;
        }
        return new Dictionary<WorkSector, float>
        {
            [WorkSector.Food] = food,
            [WorkSector.Wood] = wood,
            [WorkSector.Stone] = stone,
            [WorkSector.Free] = 1f - total,
        };
    }

    /// <summary>Convertit un manque en pression : 0 quand rien ne manque, 100 quand le manque atteint <paramref name="full"/>.</summary>
    private static float Pressure(float shortfall, float full) => Math.Clamp(shortfall / full, 0f, 1f) * 100f;

    // --- Les pensées de la colonie ---

    private static void Narrate(Colony colony, ColonySensors sensors, GameClock clock)
    {
        int Workers(WorkSector sector) => colony.Members.Count(m => m.Sector == sector);

        string foodBand = sensors.FoodDays < 2 ? "crise" : sensors.FoodDays < 4 ? "basse" : "ok";
        if (Changed(colony, "nourriture", foodBand))
        {
            Say(colony, clock, foodBand switch
            {
                "crise" => $"Il ne reste que {sensors.FoodDays:0.#} jours de nourriture ! {Workers(WorkSector.Food)} colons partent cueillir.",
                "basse" => $"Les réserves de nourriture baissent ({sensors.FoodDays:0} jours) : {Workers(WorkSector.Food)} colons à la cueillette.",
                _ => $"Nos réserves de nourriture sont confortables ({sensors.FoodDays:0} jours).",
            });
        }

        string woodBand = sensors.WoodPressure > 60 ? "manque" : sensors.WoodPressure > 20 ? "besoin" : "ok";
        if (Changed(colony, "bois", woodBand))
        {
            Say(colony, clock, woodBand switch
            {
                "manque" when clock.Season == Season.Automne =>
                    $"L'hiver approche et le bois manque : {Workers(WorkSector.Wood)} colons coupent du bois de chauffage.",
                "manque" => $"Nous manquons de bois : {Workers(WorkSector.Wood)} colons partent en forêt.",
                "besoin" when clock.Season == Season.Automne =>
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

        int free = Workers(WorkSector.Free);
        float freeRatio = free / (float)Math.Max(1, colony.Members.Count);
        string freeBand = free == 0 ? "aucun"
            : freeRatio < 0.4f ? "quelques"
            : freeRatio > 0.6f ? "beaucoup"
            : Announced(colony, "temps libre");
        if (Changed(colony, "temps libre", freeBand) && free > 0)
            Say(colony, clock, $"Tout va bien : {free} colons profitent de leur temps libre.");
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

        bool urgent = band is "crise" or "manque";
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
