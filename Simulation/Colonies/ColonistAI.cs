using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Le comportement individuel d'un colon : il s'occupe d'abord de ses besoins (dormir, manger, se détendre),
/// puis cueille des baies pour la colonie. Le cerveau de la colonie viendra ensuite répartir le travail.
/// </summary>
public static class ColonistAI
{
    // Le calendrier est compressé (une journée = 45 s), mais les gestes doivent rester lisibles à l'écran :
    // la marche et la durée des actions sont donc exprimées en secondes réelles à vitesse ×1.
    private const float WalkTilesPerSecond = 4f;
    private const float EatSeconds = 1.5f;
    private const float ForageSeconds = 1.5f;
    private const float DeliverSeconds = 0.5f;
    private const float RelaxSeconds = 6f;

    private const int ThinkIntervalTicks = 10;
    private const int ForageSearchRadius = 30;

    // Variation des besoins, par heure de jeu.
    private const float HungerPerHour = 0.04f;
    private const float FatiguePerHour = 0.045f;
    private const float SleepRecoveryPerHour = 0.12f;
    private const float BoredomPerHour = 0.03f;
    private const float RelaxRecoveryPerHour = 0.3f;

    private const float MealValue = 0.6f;
    private const float BerryValue = 0.2f;

    public static void Tick(Colonist colonist, WorldState world)
    {
        colonist.PrevX = colonist.X;
        colonist.PrevY = colonist.Y;
        UpdateNeeds(colonist);

        // À bout de forces, il s'endort là où il est.
        if (colonist.Needs.Rest <= 0f && colonist.Activity?.Kind != ActivityKind.Sleep)
        {
            Cancel(colonist);
            TryStart(colonist, world, new Activity(ActivityKind.Sleep, colonist.TileX, colonist.TileY, 0));
        }

        if (colonist.Activity is null)
        {
            if (--colonist.ThinkCooldown > 0)
                return;
            colonist.ThinkCooldown = ThinkIntervalTicks;
            Choose(colonist, world);
            if (colonist.Activity is null)
                return;
        }

        if (colonist.PathIndex < colonist.Path.Count)
            Move(colonist, world);
        else
            Act(colonist, world);
    }

    private static void UpdateNeeds(Colonist colonist)
    {
        Needs needs = colonist.Needs;
        const float hour = 1f / TimeConstants.TicksPerHour;
        needs.Food -= HungerPerHour * hour;
        if (colonist.IsSleeping)
            needs.Rest += SleepRecoveryPerHour * hour;
        else
            needs.Rest -= FatiguePerHour * hour;

        if (colonist.Activity is { Kind: ActivityKind.Relax, Started: true })
            needs.Leisure += RelaxRecoveryPerHour * hour;
        else if (!colonist.IsSleeping)
            needs.Leisure -= BoredomPerHour * hour;
        needs.Clamp();
    }

    /// <summary>Choisit la prochaine activité, du besoin le plus pressant au travail.</summary>
    private static void Choose(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        Needs needs = colonist.Needs;
        var clock = world.Clock;

        // Épuisement : dormir passe avant tout.
        if (needs.Rest < 0.15f)
        {
            GoToSleep(colonist, world);
            return;
        }

        // On mange quand on a faim, et on dîne avant d'aller se coucher pour ne pas se réveiller affamé.
        bool bedtime = clock.IsNight || clock.IsEvening;
        if (needs.Food < 0.4f || (bedtime && needs.Food < 0.65f))
        {
            if (colony.Stock.Get(ResourceType.Food) > 0 && StartNearCamp(colonist, world, ActivityKind.Eat, EatSeconds))
                return;
            if (TryForage(colonist, world, ActivityKind.ForageToEat, colonist.TileX, colonist.TileY))
                return;
        }

        if ((clock.IsNight && needs.Rest < 0.95f) || needs.Rest < 0.25f)
        {
            GoToSleep(colonist, world);
            return;
        }

        if (colonist.Carrying is not null && StartNearCamp(colonist, world, ActivityKind.Deliver, DeliverSeconds))
            return;

        if ((needs.Leisure < 0.3f || (clock.IsEvening && needs.Leisure < 0.8f))
            && StartNearCamp(colonist, world, ActivityKind.Relax, RelaxSeconds))
            return;

        // On ne part au travail que si on a de quoi tenir jusqu'au retour, et pas en fin de journée.
        bool fitForWork = needs.Food > 0.5f && needs.Rest > 0.4f && clock.Hour >= 6 && clock.Hour < 17;
        if (fitForWork && TryForage(colonist, world, ActivityKind.Forage, colony.CampX, colony.CampY))
            return;

        Wander(colonist, world);
    }

    private static bool IsAtCamp(Colonist colonist) =>
        Math.Max(Math.Abs(colonist.TileX - colonist.Colony.CampX), Math.Abs(colonist.TileY - colonist.Colony.CampY)) <= 6;

    private static void GoToSleep(Colonist colonist, WorldState world)
    {
        (int x, int y) = colonist.Colony.SleepSpot(colonist);
        if (!TryStart(colonist, world, new Activity(ActivityKind.Sleep, x, y, 0)))
            TryStart(colonist, world, new Activity(ActivityKind.Sleep, colonist.TileX, colonist.TileY, 0));
    }

    private static bool StartNearCamp(Colonist colonist, WorldState world, ActivityKind kind, float seconds)
    {
        IReadOnlyList<(int X, int Y)> spots = colonist.Colony.GatherSpots;
        (int x, int y) = spots[world.Random.Next(Math.Min(12, spots.Count))];
        return TryStart(colonist, world, new Activity(kind, x, y, seconds * TimeConstants.TicksPerSecond));
    }

    /// <summary>Cherche le buisson chargé de baies le plus proche, que personne d'autre n'a réservé.</summary>
    private static bool TryForage(Colonist colonist, WorldState world, ActivityKind kind, int centerX, int centerY)
    {
        LocalMap map = world.Map;
        var candidates = new List<(int X, int Y, int Distance)>();
        for (int dy = -ForageSearchRadius; dy <= ForageSearchRadius; dy++)
        for (int dx = -ForageSearchRadius; dx <= ForageSearchRadius; dx++)
        {
            int x = centerX + dx, y = centerY + dy;
            if (map.InBounds(x, y) && map.GetBerries(x, y) > 0 && !colonist.Colony.Reserved.Contains((x, y)))
                candidates.Add((x, y, dx * dx + dy * dy));
        }

        foreach ((int x, int y, _) in candidates.OrderBy(c => c.Distance).Take(3))
        {
            if (TryStart(colonist, world, new Activity(kind, x, y, ForageSeconds * TimeConstants.TicksPerSecond)))
            {
                colonist.Colony.Reserved.Add((x, y));
                return true;
            }
        }
        return false;
    }

    private static void Wander(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int x = colony.CampX + world.Random.Next(-7, 8);
            int y = colony.CampY + world.Random.Next(-7, 8);
            float seconds = 1.5f + 2f * world.Random.NextSingle();
            if (TryStart(colonist, world, new Activity(ActivityKind.Wander, x, y, seconds * TimeConstants.TicksPerSecond)))
                return;
        }
    }

    private static bool TryStart(Colonist colonist, WorldState world, Activity activity)
    {
        List<(int X, int Y)>? path = world.Pathfinder.FindPath(colonist.TileX, colonist.TileY, activity.TargetX, activity.TargetY);
        if (path is null)
            return false;
        colonist.Activity = activity;
        colonist.Path = path;
        colonist.PathIndex = 0;
        return true;
    }

    private static void Move(Colonist colonist, WorldState world)
    {
        LocalMap map = world.Map;
        (int nextX, int nextY) = colonist.Path[colonist.PathIndex];

        // Le terrain a pu changer depuis le calcul du chemin (une case minée, par exemple).
        if (!map.CanStep(colonist.TileX, colonist.TileY, nextX, nextY))
        {
            Activity activity = colonist.Activity!;
            if (!TryStart(colonist, world, activity))
                Cancel(colonist);
            return;
        }

        float speed = WalkTilesPerSecond / TimeConstants.TicksPerSecond / map.MoveCost(colonist.TileX, colonist.TileY);
        float targetX = nextX + 0.5f, targetY = nextY + 0.5f;
        float dx = targetX - colonist.X, dy = targetY - colonist.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= speed)
        {
            colonist.X = targetX;
            colonist.Y = targetY;
            colonist.PathIndex++;
        }
        else
        {
            colonist.X += dx / distance * speed;
            colonist.Y += dy / distance * speed;
        }
        colonist.DistanceWalked += Math.Min(distance, speed);
    }

    private static void Act(Colonist colonist, WorldState world)
    {
        Activity activity = colonist.Activity!;
        if (!activity.Started)
        {
            if (!CanBegin(colonist, world, activity))
            {
                Cancel(colonist);
                return;
            }
            activity.Started = true;

            // Quiconque revient au camp y dépose ce qu'il rapporte.
            if (colonist.Carrying is { } load && IsAtCamp(colonist))
            {
                colonist.Colony.Stock.Add(load.Type, load.Amount);
                colonist.Carrying = null;
            }
        }

        activity.ElapsedTicks++;
        Needs needs = colonist.Needs;
        bool done = activity.Kind == ActivityKind.Sleep
            ? needs.Rest >= 1f
              || (!world.Clock.IsNight && needs.Rest >= 0.7f)
              || (needs.Food < 0.1f && needs.Rest > 0.15f) // la faim réveille
            : activity.ElapsedTicks >= activity.DurationTicks;
        if (done)
            Finish(colonist, world, activity);
    }

    private static bool CanBegin(Colonist colonist, WorldState world, Activity activity) => activity.Kind switch
    {
        ActivityKind.Eat => colonist.Colony.Stock.TryTake(ResourceType.Food, 1),
        ActivityKind.Forage or ActivityKind.ForageToEat => world.Map.GetBerries(activity.TargetX, activity.TargetY) > 0,
        _ => true,
    };

    private static void Finish(Colonist colonist, WorldState world, Activity activity)
    {
        Colony colony = colonist.Colony;
        switch (activity.Kind)
        {
            case ActivityKind.Eat:
                colonist.Needs.Food += MealValue;
                break;
            case ActivityKind.Forage:
            {
                int berries = world.Map.HarvestBerries(activity.TargetX, activity.TargetY);
                if (berries > 0)
                    colonist.Carrying = (ResourceType.Food, berries);
                break;
            }
            case ActivityKind.ForageToEat:
                colonist.Needs.Food += BerryValue * world.Map.HarvestBerries(activity.TargetX, activity.TargetY);
                break;
        }
        colonist.Needs.Clamp();
        EndActivity(colonist);
    }

    private static void Cancel(Colonist colonist) => EndActivity(colonist);

    private static void EndActivity(Colonist colonist)
    {
        if (colonist.Activity is { Kind: ActivityKind.Forage or ActivityKind.ForageToEat } activity)
            colonist.Colony.Reserved.Remove((activity.TargetX, activity.TargetY));
        colonist.Activity = null;
        colonist.Path = [];
        colonist.PathIndex = 0;
        colonist.ThinkCooldown = 0;
    }
}
