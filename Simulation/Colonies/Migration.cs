using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les allées et venues : des voyageurs viennent de nulle part frapper à la porte de la colonie,
/// qui décide de les accueillir ou non ; des colons malheureux depuis trop longtemps s'en vont.
/// La population peut ainsi croître comme décroître sans passer par les naissances.
/// </summary>
public static class Migration
{
    /// <summary>Heure de la journée où un voyageur peut se présenter.</summary>
    public const int ArrivalHour = 7;

    /// <summary>
    /// Chance maximale, par jour, qu'un voyageur (ou un petit groupe) se présente : celle d'une colonie
    /// au sommet de son attrait. Une colonie ordinaire en reçoit bien moins, une colonie pauvre presque aucun.
    /// </summary>
    public const float MaxTravelerChancePerDay = 0.15f;
    private const float PairChance = 0.25f;

    /// <summary>On voyage peu en hiver.</summary>
    private const float WinterTravelFactor = 0.5f;

    // Ce qui fait l'attrait d'une colonie, et son poids dans la note finale.
    private const float MoodWeight = 0.4f;
    private const float FoodWeight = 0.3f;
    private const float HousingWeight = 0.15f;
    private const float WealthWeight = 0.15f;

    /// <summary>Réserves de nourriture (en jours) à partir desquelles la colonie est « bien nourrie » aux yeux d'un voyageur.</summary>
    private const float PlentifulFoodDays = 8f;

    /// <summary>Matières (bois, pierre, minerai) par colon à partir desquelles la colonie est « riche ».</summary>
    private const float WealthyMaterialsPerColonist = 15f;

    /// <summary>Sous ce stock de nourriture (en jours de réserve), la colonie n'ose pas prendre une bouche de plus.</summary>
    public const float MinFoodDaysToWelcome = 3f;

    /// <summary>Les voyageurs ne s'arrêtent pas dans une colonie où l'on est malheureux.</summary>
    public const float MinMoodToAttract = 0.4f;

    /// <summary>Humeur en dessous de laquelle un colon déprime.</summary>
    public const float UnhappyMood = 0.35f;

    /// <summary>Trois jours de déprime d'affilée, et il part.</summary>
    public const int UnhappyHoursBeforeLeaving = 3 * 24;

    /// <summary>Une colonie plus petite ne se vide pas davantage.</summary>
    public const int MinPopulationToLeave = 3;

    private const int EntryAttempts = 12;

    /// <summary>
    /// L'attrait de la colonie aux yeux d'un voyageur, de 0 à 1 : humeur de ses habitants, nourriture en réserve,
    /// toits disponibles et richesse en matériaux.
    /// </summary>
    public static float Attractiveness(Colony colony, GameClock clock)
    {
        ColonySensors sensors = ColonyBrain.Sense(colony, clock);
        int population = Math.Max(1, colony.Members.Count);

        float mood = Math.Clamp((colony.AverageMood - MinMoodToAttract) / 0.4f, 0f, 1f);
        float food = Math.Clamp((sensors.FoodDays - MinFoodDaysToWelcome) / (PlentifulFoodDays - MinFoodDaysToWelcome), 0f, 1f);
        float housing = 1f - Math.Clamp(sensors.Homeless / (float)Building.HutCapacity, 0f, 1f);
        float materials = colony.Stock.Get(ResourceType.Wood) + colony.Stock.Get(ResourceType.Stone) + 2f * colony.Stock.Get(ResourceType.IronOre);
        float wealth = Math.Clamp(materials / population / WealthyMaterialsPerColonist, 0f, 1f);

        return MoodWeight * mood + FoodWeight * food + HousingWeight * housing + WealthWeight * wealth;
    }

    /// <summary>La chance qu'un voyageur se présente ce jour-là : elle croît avec l'attrait de la colonie.</summary>
    public static float TravelerChancePerDay(Colony colony, GameClock clock)
    {
        float seasonFactor = clock.Season == Season.Hiver ? WinterTravelFactor : 1f;
        float attractiveness = Attractiveness(colony, clock);
        // Au carré : un attrait moyen n'attire qu'un voyageur de temps en temps, seuls les sommets font venir du monde.
        return MaxTravelerChancePerDay * attractiveness * attractiveness * seasonFactor;
    }

    /// <summary>Chaque matin, un voyageur peut se présenter à la colonie.</summary>
    public static void Daily(WorldState world, Colony colony)
    {
        if (world.Random.NextSingle() >= TravelerChancePerDay(colony, world.Clock))
            return;
        int size = world.Random.NextSingle() < PairChance ? 2 : 1;
        Welcome(world, colony, size);
    }

    /// <summary>
    /// Des voyageurs se présentent ; la colonie les accueille si elle a de quoi les nourrir et les loger.
    /// Renvoie le nombre de voyageurs acceptés.
    /// </summary>
    public static int Welcome(WorldState world, Colony colony, int size)
    {
        // Une colonie où l'on est malheureux n'attire personne : le voyageur passe son chemin, sans bruit.
        if (colony.AverageMood < MinMoodToAttract)
            return 0;

        bool group = size > 1;
        string asks = group ? "Des voyageurs demandent" : "Un voyageur demande";
        ColonySensors sensors = ColonyBrain.Sense(colony, world.Clock);
        string? refusal = sensors.FoodDays < MinFoodDaysToWelcome
            ? $"{asks} asile, mais nos réserves de nourriture sont trop maigres : nous {(group ? "les" : "le")} renvoyons."
            : sensors.Homeless > Building.HutCapacity
                ? $"{asks} asile, mais nous n'avons pas de toit à offrir : {(group ? "ils repartent" : "il repart")}."
                : null;
        if (refusal is not null)
        {
            if (world.Clock.TotalDays - colony.LastRefusalDay >= 3)
            {
                ColonyBrain.Say(colony, world.Clock, refusal);
                colony.LastRefusalDay = world.Clock.TotalDays;
            }
            return 0;
        }

        if (FindEdgePoint(world, colony.CampX, colony.CampY) is not { } entry)
            return 0;

        var taken = colony.Members.Concat(colony.Transients).Select(m => m.Name).ToList();
        SkillType specialty = Skills.All[world.Random.Next(Skills.All.Length)];
        for (int i = 0; i < size; i++)
        {
            Sex sex = world.Random.Next(2) == 0 ? Sex.Female : Sex.Male;
            string name = Names.Pick(sex, world.Random, taken);
            taken.Add(name);
            var traveler = new Colonist(world.NextColonistId(), name, sex, colony,
                Skills.Veteran(world.Random, i == 0 ? specialty : Skills.All[world.Random.Next(Skills.All.Length)]),
                entry.X + 0.5f, entry.Y + 0.5f)
            {
                Transit = TransitState.Arriving,
                Personality = Personality.Random(world.Random),
                BirthTicks = world.Clock.Ticks - (long)((Colonist.AdultAge + 6f * world.Random.NextSingle()) * TimeConstants.TicksPerYear),
                Surname = Names.PickSurname(world.Random, colony.Members.Concat(colony.Transients).Select(t => t.Surname)),
            };
            // Il arrive après une longue marche : fatigué et un peu affamé.
            traveler.Needs.Food = 0.5f + 0.15f * world.Random.NextSingle();
            traveler.Needs.Rest = 0.5f + 0.2f * world.Random.NextSingle();
            traveler.Needs.Leisure = 0.6f + 0.3f * world.Random.NextSingle();
            traveler.Needs.Social = 0.4f + 0.3f * world.Random.NextSingle();
            traveler.Needs.Comfort = 0.4f;
            colony.Transients.Add(traveler);
        }
        return size;
    }

    /// <summary>Le voyageur est arrivé au camp : il se joint à la colonie, qui se réorganise.</summary>
    internal static void Join(WorldState world, Colonist colonist)
    {
        Colony colony = colonist.Colony;
        colony.Transients.Remove(colonist);
        colonist.Transit = TransitState.None;
        colonist.Activity = null;
        colony.Members.Add(colonist);
        colony.FillVacancies();
        colony.AssignSectors();

        SkillType best = Skills.All.OrderByDescending(colonist.Skills.Level).First();
        string experienced = colonist.Sex == Sex.Female ? "expérimentée" : "expérimenté";
        ColonyBrain.Say(colony, world.Clock,
            $"{colonist.Name}, {Skills.TradeName(best, colonist.Sex)} {experienced}, rejoint la colonie ({colony.Members.Count} colons).");
    }

    /// <summary>Chaque heure : on mesure la déprime de chacun, et un colon qui n'en peut plus s'en va.</summary>
    public static void Hourly(WorldState world, Colony colony)
    {
        foreach (Colonist colonist in colony.Members)
        {
            if (colonist.Needs.Mood < UnhappyMood)
                colonist.UnhappyHours++;
            else
                colonist.UnhappyHours = Math.Max(0, colonist.UnhappyHours - 3);
        }

        if (colony.Members.Count <= MinPopulationToLeave)
            return;
        // Les enracinés tiennent plus longtemps, les nomades partent plus vite.
        Colonist? leaver = colony.Members.FirstOrDefault(m => m.Stage is LifeStage.Adult or LifeStage.Elder
            && m.UnhappyHours >= UnhappyHoursBeforeLeaving * m.Personality.PatienceFactor);
        if (leaver is null)
            return;
        ColonyBrain.Say(colony, world.Clock, $"{leaver.Name} n'en peut plus et quitte la colonie.");
        ColonistAI.BeginDeparture(leaver, world);
    }

    /// <summary>
    /// Une case marchable au bord de la carte, d'où l'on peut rejoindre (ou à laquelle on peut se rendre depuis)
    /// la case donnée. Null si aucune n'est accessible.
    /// </summary>
    internal static (int X, int Y)? FindEdgePoint(WorldState world, int fromX, int fromY)
    {
        LocalMap map = world.Map;
        for (int attempt = 0; attempt < EntryAttempts; attempt++)
        {
            int side = world.Random.Next(4);
            int along = world.Random.Next(side < 2 ? map.Width : map.Height);
            (int x, int y, int dx, int dy) = side switch
            {
                0 => (along, 0, 0, 1),
                1 => (along, map.Height - 1, 0, -1),
                2 => (0, along, 1, 0),
                _ => (map.Width - 1, along, -1, 0),
            };
            // Si le bord est de l'eau ou de la roche, on avance de quelques cases vers l'intérieur.
            for (int step = 0; step < 6 && !map.IsWalkable(x, y); step++)
            {
                x += dx;
                y += dy;
            }
            if (map.IsWalkable(x, y) && world.Pathfinder.FindPath(fromX, fromY, x, y) is not null)
                return (x, y);
        }
        return null;
    }
}
