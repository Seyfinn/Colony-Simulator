using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Le comportement individuel d'un colon : il s'occupe d'abord de ses besoins (dormir, manger, se détendre),
/// puis travaille dans le secteur où la colonie l'a affecté, ou à défaut dans un autre.
/// </summary>
public static class ColonistAI
{
    // Le calendrier est compressé (une journée = 45 s), mais les gestes doivent rester lisibles à l'écran :
    // la marche et la durée des actions sont donc exprimées en secondes réelles à vitesse ×1.
    private const float WalkTilesPerSecond = 4f;
    private const float EatSeconds = 1.5f;
    private const float ForageSeconds = 1.5f;
    private const float FishSeconds = 3f;
    private const int FoodPerFish = 2;
    private const float ChopSeconds = 4f;
    private const float MineSeconds = 5f;
    /// <summary>Creuser une case de canal à la bêche : plus long que d'abattre un arbre.</summary>
    private const float DigSeconds = 4f;
    private const float DeliverSeconds = 0.5f;
    private const float RelaxSeconds = 6f;

    private const int ThinkIntervalTicks = 10;
    private const int ForageSearchRadius = 30;

    /// <summary>Nombre de cibles essayées avant d'abandonner (une cible peut être inaccessible).</summary>
    private const int TargetsToTry = 3;

    // Variation des besoins, par heure de jeu.
    private const float HungerPerHour = 0.04f;
    private const float FatiguePerHour = 0.045f;
    private const float SleepRecoveryPerHour = 0.12f;
    private const float BoredomPerHour = 0.03f;
    private const float RelaxRecoveryPerHour = 0.3f;
    private const float GriefFadePerHour = 0.2f / 24f;
    private const float SocialLossPerHour = 0.02f;
    private const float ComfortChangePerHour = 0.25f;

    // Conversations : on va rejoindre quelqu'un, on bavarde quelques secondes, chacun y gagne de la compagnie.
    private const float ChatSeconds = 3.5f;
    private const float ChatSearchRadius = 25f;
    private const float ChatMaxGap = 4f;
    private const float ChatSocialGain = 0.4f;
    private const float FriendChatBonus = 0.15f;
    private const float QuarrelSocialGain = 0.1f;

    private const float ColdSleepFactor = 0.6f;
    private const float ShelterSleepFactor = 1.2f;

    // Construction : on porte au plus 6 unités de bois par voyage, et chaque séance de travail fait avancer le chantier.
    private const int CarryCapacity = 6;
    private const float FetchSeconds = 0.5f;
    private const float BuildActionSeconds = 3f;
    private const float MealValue = 0.6f;
    private const float BerryValue = 0.2f;

    // Ce que rapporte une couche de roche minée.
    private const int StonePerLayer = 3;
    private const int IronOrePerLayer = 2;

    public static void Tick(Colonist colonist, WorldState world)
    {
        colonist.PrevX = colonist.X;
        colonist.PrevY = colonist.Y;
        UpdateNeeds(colonist, world);
        if (colonist.ChatCooldownTicks > 0)
            colonist.ChatCooldownTicks--;

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

    /// <summary>
    /// Un colon quitte la colonie : il laisse ce qu'il portait, libère sa place et marche jusqu'au bord de la carte.
    /// </summary>
    internal static void BeginDeparture(Colonist colonist, WorldState world)
    {
        DetachFromColony(colonist);

        // Le couple se défait, la grossesse aussi.
        if (colonist.Partner is { } partner)
        {
            partner.Partner = null;
            partner.Needs.Grief = Math.Max(partner.Needs.Grief, 0.5f);
            colonist.Partner = null;
        }
        colonist.PregnantUntilTicks = null;
        colonist.PregnancyFather = null;

        colonist.Transit = TransitState.Leaving;
        colonist.Colony.Transients.Add(colonist);
    }

    /// <summary>
    /// Un colon sort de la colonie (départ ou mort) : il laisse ce qu'il portait, libère sa place en hutte
    /// et n'est plus compté parmi les membres.
    /// </summary>
    internal static void DetachFromColony(Colonist colonist)
    {
        Colony colony = colonist.Colony;
        EndActivity(colonist);

        // Ce qu'il portait reste à la colonie ; un chantier qui l'attendait réclamera d'autres bras.
        if (colonist.Carrying is { } load)
        {
            if (colonist.CarryingTo is { } site)
                site.AddInTransit(load.Type, -load.Amount);
            colony.Stock.Add(load.Type, load.Amount);
            colonist.Carrying = null;
            colonist.CarryingTo = null;
        }
        colonist.WorkCycleStartTicks = -1;
        colonist.WorkCycleExtraHours = 0;

        colony.Members.Remove(colonist);
        if (colonist.Home is { } home)
        {
            home.Residents.Remove(colonist);
            colonist.Home = null;
        }
        colony.FillVacancies();
        colony.AssignSectors();
    }

    /// <summary>Fait marcher un voyageur vers le camp (arrivée) ou vers le bord de la carte (départ).</summary>
    internal static void TickTransient(Colonist colonist, WorldState world)
    {
        colonist.PrevX = colonist.X;
        colonist.PrevY = colonist.Y;

        if (colonist.Activity is null && !StartTransitWalk(colonist, world))
        {
            // Aucun chemin : l'arrivant apparaît directement au camp, le partant disparaît sur place.
            EndTransit(colonist, world);
            return;
        }

        if (colonist.PathIndex < colonist.Path.Count)
            Move(colonist, world);
        else
            EndTransit(colonist, world);
    }

    private static bool StartTransitWalk(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (colonist.Transit == TransitState.Arriving)
        {
            (int x, int y) = colony.GatherSpots[world.Random.Next(Math.Min(12, colony.GatherSpots.Count))];
            return TryStart(colonist, world, new Activity(ActivityKind.Arrive, x, y, 0));
        }
        return Migration.FindEdgePoint(world, colonist.TileX, colonist.TileY) is { } exit
               && TryStart(colonist, world, new Activity(ActivityKind.Depart, exit.X, exit.Y, 0));
    }

    private static void EndTransit(Colonist colonist, WorldState world)
    {
        EndActivity(colonist);
        if (colonist.Transit == TransitState.Arriving)
            Migration.Join(world, colonist);
        else
            colonist.Colony.Transients.Remove(colonist);
    }

    private static void UpdateNeeds(Colonist colonist, WorldState world)
    {
        Needs needs = colonist.Needs;
        const float hour = 1f / TimeConstants.TicksPerHour;
        needs.Food -= HungerPerHour * HungerFactor(colonist.Stage) * hour;
        needs.Grief -= GriefFadePerHour * hour;

        // On dort mieux à l'abri d'une hutte ; dehors, sans feu pendant la saison froide, le sommeil répare mal.
        bool sheltered = colonist.IsSleepingAtHome;
        bool cold = !sheltered && ColonyBrain.IsColdSeason(world.Clock.Season) && !colonist.Colony.FireLit;
        if (colonist.IsSleeping)
            needs.Rest += SleepRecoveryPerHour * (sheltered ? ShelterSleepFactor : cold ? ColdSleepFactor : 1f) * hour;
        else
            needs.Rest -= FatiguePerHour * hour;

        if (colonist.Activity is { Kind: ActivityKind.Relax, Started: true })
            needs.Leisure += RelaxRecoveryPerHour * hour;
        else if (!colonist.IsSleeping)
            needs.Leisure -= BoredomPerHour * colonist.Personality.BoredomFactor * hour;

        if (!colonist.IsSleeping)
            needs.Social -= SocialLossPerHour * colonist.Personality.LonelinessFactor * hour;
        needs.Comfort += (ComfortTarget(colonist, world) - needs.Comfort) * ComfortChangePerHour * hour;
        needs.Clamp();
    }

    /// <summary>Les enfants mangent moins, les adolescents un peu moins que les adultes.</summary>
    private static float HungerFactor(LifeStage stage) => stage switch
    {
        LifeStage.Child => 0.6f,
        LifeStage.Teen => 0.85f,
        _ => 1f,
    };

    /// <summary>L'âge change la vitesse de travail : les adolescents travaillent à mi-temps, les anciens à 70 %.</summary>
    private static float WorkFactorOf(LifeStage stage) => stage switch
    {
        LifeStage.Child => 0f,
        LifeStage.Teen => 0.5f,
        LifeStage.Elder => 0.7f,
        _ => 1f,
    };

    /// <summary>Les adolescents apprennent plus vite, les anciens un peu moins.</summary>
    private static float LearningFactorOf(LifeStage stage) => stage switch
    {
        LifeStage.Child => 0.5f,
        LifeStage.Teen => 1.5f,
        LifeStage.Elder => 0.7f,
        _ => 1f,
    };

    /// <summary>Le confort visé : un toit, de la chaleur (un feu allumé en saison froide), et un peu plus à l'abri l'hiver.</summary>
    private static float ComfortTarget(Colonist colonist, WorldState world)
    {
        bool cold = ColonyBrain.IsColdSeason(world.Clock.Season);
        bool warm = !cold || colonist.Colony.FireLit;
        bool housed = colonist.Home is not null;
        return 0.2f + (housed ? 0.4f : 0f) + (warm ? 0.3f : 0f) + (housed && cold ? 0.1f : 0f);
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
            if (colony.Stock.FoodUnits > 0 && StartNearCamp(colonist, world, ActivityKind.Eat, EatSeconds))
                return;
            if (TryForage(colonist, world, ActivityKind.ForageToEat, colonist.TileX, colonist.TileY))
                return;
        }

        if ((clock.IsNight && needs.Rest < 0.95f) || needs.Rest < 0.25f)
        {
            GoToSleep(colonist, world);
            return;
        }

        // Ce qu'on porte va d'abord à destination : au chantier s'il est encore ouvert, sinon au stock.
        if (colonist.CarryingTo is { IsComplete: true })
            colonist.CarryingTo = null;
        if (colonist.CarryingTo is { } site && TrySupplySite(colonist, world, site))
            return;
        if (colonist.Carrying is not null && colonist.CarryingTo is null
            && StartNearCamp(colonist, world, ActivityKind.Deliver, DeliverSeconds))
            return;

        // Besoin de compagnie : les sociables en cherchent plus tôt, les solitaires se contentent de peu.
        // Les conversations ont lieu le soir et pendant le temps libre, sauf si la solitude devient pesante.
        float socialThreshold = 0.75f - 0.2f * colonist.Personality[Axis.Sociabilite];
        bool leisureTime = colonist.Sector == WorkSector.Free || clock.Hour >= 17;
        if (colonist.Stage != LifeStage.Child && needs.Social < socialThreshold && (leisureTime || needs.Social < 0.2f) && !clock.IsNight
            && colonist.ChatCooldownTicks <= 0 && TryChat(colonist, world))
            return;

        if ((needs.Leisure < 0.3f || (clock.IsEvening && needs.Leisure < 0.8f))
            && StartNearCamp(colonist, world, ActivityKind.Relax, RelaxSeconds))
            return;

        // Temps libre : la colonie n'a pas besoin de ses bras pour l'instant, il se détend.
        if (colonist.Sector == WorkSector.Free || colonist.Stage == LifeStage.Child)
        {
            if (needs.Leisure < 0.95f && StartNearCamp(colonist, world, ActivityKind.Relax, RelaxSeconds))
                return;
            Wander(colonist, world);
            return;
        }

        // On ne part au travail que si on a de quoi tenir jusqu'au retour, et pas en fin de journée.
        bool fitForWork = needs.Food > 0.5f && needs.Rest > 0.4f && clock.Hour >= 6 && clock.Hour < 17;
        if (fitForWork && TryWork(colonist, world))
            return;

        Wander(colonist, world);
    }

    /// <summary>
    /// Va bavarder avec quelqu'un : on préfère ses amis et les gens proches, on évite ses rivaux.
    /// </summary>
    private static bool TryChat(Colonist colonist, WorldState world)
    {
        var candidates = new List<(Colonist Colonist, float Weight)>();
        foreach (Colonist other in colonist.Colony.Members)
        {
            if (other == colonist || other.IsSleeping || other.Transit != TransitState.None || other.Stage == LifeStage.Child)
                continue;
            float affinity = Relations.Affinity(colonist, other);
            float distance = MathF.Sqrt((other.X - colonist.X) * (other.X - colonist.X) + (other.Y - colonist.Y) * (other.Y - colonist.Y));
            if (distance > ChatSearchRadius || affinity <= Relations.RivalThreshold)
                continue;

            // On va plutôt vers ses amis, vers ceux dont le caractère nous plaît, et vers ceux qui sont proches.
            float attraction = 0.3f + 2f * MathF.Max(0f, Personality.Compatibility(colonist.Personality, other.Personality) - 0.3f);
            candidates.Add((other, MathF.Exp(affinity / 30f) * attraction / (1f + distance / 10f)));
        }

        if (candidates.Count > 0)
        {
            float roll = world.Random.NextSingle() * candidates.Sum(c => c.Weight);
            foreach ((Colonist other, float weight) in candidates)
            {
                roll -= weight;
                if (roll > 0f)
                    continue;
                if (TryStart(colonist, world, new Activity(ActivityKind.Chat, other.TileX, other.TileY, Ticks(ChatSeconds)) { Partner = other }))
                    return true;
                break;
            }
        }

        // Personne à qui parler : on réessaiera plus tard.
        colonist.ChatCooldownTicks = (int)TimeConstants.TicksPerHour;
        return false;
    }

    /// <summary>Travaille d'abord dans son secteur ; s'il n'y a rien à y faire, aide dans les autres secteurs actifs.</summary>
    private static bool TryWork(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        IEnumerable<WorkSector> order = WorkSectors.Productive
            .Where(s => s != colonist.Sector && colony.WorkShares[s] > 0.05f)
            .OrderByDescending(s => colony.WorkShares[s])
            .Prepend(colonist.Sector);

        foreach (WorkSector sector in order)
        {
            bool started = sector switch
            {
                WorkSector.Food => TryGatherFood(colonist, world),
                WorkSector.Farm => TryFarm(colonist, world),
                WorkSector.Wood => TryChop(colonist, world),
                WorkSector.Stone => TryMine(colonist, world),
                WorkSector.Craft => TryCraft(colonist, world),
                _ => TryConstruct(colonist, world),
            };
            if (started)
                return true;
        }
        return false;
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
        return TryStart(colonist, world, new Activity(kind, x, y, Ticks(seconds)));
    }

    /// <summary>Cherche le buisson chargé de baies le plus proche, que personne d'autre n'a réservé.</summary>
    private static bool TryForage(Colonist colonist, WorldState world, ActivityKind kind, int centerX, int centerY)
    {
        float seconds = ForageSeconds / WorkSpeed(colonist, SkillType.Foraging);
        return NearestBushes(world.Map, colonist.Colony, centerX, centerY)
            .Take(TargetsToTry)
            .Any(b => TryStart(colonist, world, new Activity(kind, b.X, b.Y, Ticks(seconds))));
    }

    /// <summary>
    /// Choisit la façon la plus rentable de nourrir la colonie : on compare la nourriture rapportée
    /// au temps passé (aller-retour compris) pour les buissons et les coins de pêche les plus proches.
    /// </summary>
    private static bool TryGatherFood(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        LocalMap map = world.Map;
        var options = new List<(Activity Activity, float Food, float WorkSeconds)>();

        float forageSeconds = ForageSeconds / WorkSpeed(colonist, SkillType.Foraging);
        foreach ((int x, int y) in NearestBushes(map, colony, colony.CampX, colony.CampY).Take(TargetsToTry))
            options.Add((new Activity(ActivityKind.Forage, x, y, Ticks(forageSeconds)), map.GetBerries(x, y), forageSeconds));

        float fishSeconds = FishSeconds / WorkSpeed(colonist, SkillType.Fishing);
        foreach ((int wx, int wy, int sx, int sy) in WorkSites.FishingSpots(map, colony).Take(TargetsToTry))
            options.Add((new Activity(ActivityKind.Fish, wx, wy, Ticks(fishSeconds)) { StandX = sx, StandY = sy }, FoodPerFish, fishSeconds));

        Activity? best = null;
        List<(int X, int Y)>? bestPath = null;
        int bestMaxStep = 1;
        float bestRate = 0f;
        foreach ((Activity activity, float food, float workSeconds) in options)
        {
            (List<(int X, int Y)>? path, int maxStep) = PlanPath(colonist, world, activity);
            if (path is null)
                continue;
            float roundTripSeconds = 2f * path.Count / WalkTilesPerSecond;
            float rate = food / (roundTripSeconds + workSeconds);
            if (rate > bestRate)
                (best, bestPath, bestMaxStep, bestRate) = (activity, path, maxStep, rate);
        }

        if (best is null || bestPath is null)
            return false;
        Commit(colonist, best, bestPath, bestMaxStep, world.Clock.Ticks);
        return true;
    }

    /// <summary>Moissonne une parcelle mûre en priorité ; au printemps, sinon, sème la parcelle libre la plus proche.</summary>
    private static bool TryFarm(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        float speed = WorkSpeed(colonist, SkillType.Farming);

        foreach (FieldPlot plot in NearestPlots(colony, colonist, CropStage.Ripe).Take(TargetsToTry))
            if (TryStart(colonist, world, new Activity(ActivityKind.Harvest, plot.X, plot.Y, Ticks(Farming.HarvestSeconds / speed))))
                return true;

        if (Farming.IsSowingSeason(world.Clock.Season))
            foreach (FieldPlot plot in NearestPlots(colony, colonist, CropStage.Fallow).Take(TargetsToTry))
                if (TryStart(colonist, world, new Activity(ActivityKind.Sow, plot.X, plot.Y, Ticks(Farming.SowSeconds / speed))))
                    return true;
        return false;
    }

    /// <summary>Vitesse de travail : l'habileté du métier, modulée par l'ardeur du colon.</summary>
    private static float WorkSpeed(Colonist colonist, SkillType skill) =>
        colonist.Skills.WorkSpeed(skill) * colonist.Personality.WorkFactor * MathF.Max(0.1f, WorkFactorOf(colonist.Stage))
        * ToolChain.SpeedFactor(colonist.Colony, skill);

    private static IEnumerable<FieldPlot> NearestPlots(Colony colony, Colonist colonist, CropStage stage) =>
        Farming.Plots(colony)
            .Where(p => p.Stage == stage && !colony.Reserved.Contains((p.X, p.Y)))
            .OrderBy(p => Math.Abs(p.X - colonist.TileX) + Math.Abs(p.Y - colonist.TileY));

    private static IEnumerable<(int X, int Y)> NearestBushes(LocalMap map, Colony colony, int centerX, int centerY)
    {
        var candidates = new List<(int X, int Y, int Distance)>();
        for (int dy = -ForageSearchRadius; dy <= ForageSearchRadius; dy++)
        for (int dx = -ForageSearchRadius; dx <= ForageSearchRadius; dx++)
        {
            int x = centerX + dx, y = centerY + dy;
            if (map.InBounds(x, y) && map.GetBerries(x, y) > 0 && !colony.Reserved.Contains((x, y)))
                candidates.Add((x, y, dx * dx + dy * dy));
        }
        return candidates.OrderBy(c => c.Distance).Select(c => (c.X, c.Y));
    }

    /// <summary>
    /// Travaille sur le chantier le plus proche : s'il manque du bois, on va le chercher au stock ;
    /// sinon, on bâtit.
    /// </summary>
    private static bool TryConstruct(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        Building? site = colony.ConstructionSites
            .OrderBy(b => Math.Abs(b.X - colonist.TileX) + Math.Abs(b.Y - colonist.TileY))
            .FirstOrDefault(b => b.HasAllMaterials || b.MaterialToFetch(colony.Stock) is not null);
        if (site is null)
            return TryDig(colonist, world);

        if (!site.HasAllMaterials)
        {
            IReadOnlyList<(int X, int Y)> spots = colony.GatherSpots;
            (int x, int y) = spots[world.Random.Next(Math.Min(12, spots.Count))];
            return TryStart(colonist, world, new Activity(ActivityKind.FetchMaterials, x, y, Ticks(FetchSeconds)) { Building = site });
        }

        (int bx, int by) = site.Tiles.OrderBy(t => Math.Abs(t.X - colonist.TileX) + Math.Abs(t.Y - colonist.TileY)).First();
        float seconds = BuildActionSeconds / WorkSpeed(colonist, SkillType.Construction);
        return TryStart(colonist, world, new Activity(ActivityKind.Build, bx, by, Ticks(seconds)) { Building = site });
    }

    /// <summary>Creuse la prochaine case d'un canal en chantier, en commençant par la source : l'eau avance à mesure.</summary>
    private static bool TryDig(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        float seconds = DigSeconds / WorkSpeed(colonist, SkillType.Construction);
        foreach (Canal canal in colony.CanalsInProgress)
            foreach ((int x, int y) in canal.TilesToDig().Where(t => !colony.Reserved.Contains(t)).Take(TargetsToTry))
                if (TryStart(colonist, world, new Activity(ActivityKind.Dig, x, y, Ticks(seconds))))
                    return true;
        return false;
    }

    private static bool TrySupplySite(Colonist colonist, WorldState world, Building site)
    {
        (int bx, int by) = site.Tiles.OrderBy(t => Math.Abs(t.X - colonist.TileX) + Math.Abs(t.Y - colonist.TileY)).First();
        return TryStart(colonist, world, new Activity(ActivityKind.SupplySite, bx, by, Ticks(DeliverSeconds)) { Building = site });
    }

    private static bool TryChop(Colonist colonist, WorldState world)
    {
        float seconds = ChopSeconds / WorkSpeed(colonist, SkillType.Woodcutting);
        return WorkSites.TreesToChop(world.Map, colonist.Colony)
            .Take(TargetsToTry)
            .Any(t => TryStart(colonist, world, new Activity(ActivityKind.Chop, t.X, t.Y, Ticks(seconds))));
    }

    /// <summary>Va travailler dans l'atelier que la chaîne du fer réclame en ce moment, s'il y en a un.</summary>
    private static bool TryCraft(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (ToolChain.PickJob(colony, (int)ColonyBrain.HeatingTarget(colony, world.Clock.Season)) is not { } workshop)
            return false;
        Recipe recipe = ToolChain.RecipeFor(workshop.Type);
        float seconds = recipe.Seconds / WorkSpeed(colonist, SkillType.Smithing);
        (int x, int y) = workshop.Tiles.OrderBy(t => Math.Abs(t.X - colonist.TileX) + Math.Abs(t.Y - colonist.TileY)).First();
        return TryStart(colonist, world, new Activity(ActivityKind.Craft, x, y, Ticks(seconds)) { Building = workshop });
    }

    private static bool TryMine(Colonist colonist, WorldState world)
    {
        float seconds = MineSeconds / WorkSpeed(colonist, SkillType.Mining);
        bool wantOre = colonist.Colony.Sensors?.Chain is { Active: true, OreMissing: > 0 };
        Colony colony = colonist.Colony;
        foreach ((int rockX, int rockY, int standX, int standY) in WorkSites.RocksToMine(world.Map, colony, wantOre)
                     .Where(r => !colony.UnreachableStands.Contains((r.StandX, r.StandY)))
                     .Take(TargetsToTry))
        {
            if (TryStart(colonist, world, new Activity(ActivityKind.Mine, rockX, rockY, Ticks(seconds)) { StandX = standX, StandY = standY }))
                return true;
            colony.UnreachableStands.Add((standX, standY));
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
            if (TryStart(colonist, world, new Activity(ActivityKind.Wander, x, y, Ticks(seconds))))
                return;
        }
    }

    /// <summary>Calcule le chemin vers l'endroit de l'action et la confie au colon. Renvoie false si l'endroit est inaccessible.</summary>
    private static bool TryStart(Colonist colonist, WorldState world, Activity activity)
    {
        (List<(int X, int Y)>? path, int maxStep) = PlanPath(colonist, world, activity);
        if (path is null)
            return false;
        Commit(colonist, activity, path, maxStep, world.Clock.Ticks);
        return true;
    }

    private static (List<(int X, int Y)>? Path, int MaxStep) PlanPath(Colonist colonist, WorldState world, Activity activity)
    {
        List<(int X, int Y)>? path = world.Pathfinder.FindPath(colonist.TileX, colonist.TileY, activity.StandX, activity.StandY);

        // Filet de sécurité : coincé dans un trou ou sur un plateau isolé par la carrière,
        // on escalade une marche de deux niveaux pour rentrer au camp.
        if (path is null && (IsStranded(colonist, world.Map) || IsGoingHome(activity)))
        {
            // D'abord une marche de deux niveaux ; si le colon est enfermé dans un trou plus profond, il escalade
            // la paroi à mains nues plutôt que d'y mourir de faim.
            foreach (int step in new[] { 2, LocalMap.MaxElevation })
                if (world.Pathfinder.FindPath(colonist.TileX, colonist.TileY, activity.StandX, activity.StandY, maxStep: step) is { } rescue)
                    return (rescue, step);
            return (null, 2);
        }
        return (path, 1);
    }

    private static bool IsGoingHome(Activity activity) =>
        activity.Kind is ActivityKind.Sleep or ActivityKind.Eat or ActivityKind.Deliver or ActivityKind.Relax;

    private static void Commit(Colonist colonist, Activity activity, List<(int X, int Y)> path, int maxStep, long now)
    {
        // Mesure du coût en travail : une récolte démarre un cycle qui se termine au dépôt au camp ;
        // toute autre occupation les mains vides (manger, dormir…) l'interrompt.
        if (activity.CommittedAtTicks == 0)
            activity.CommittedAtTicks = now;
        if (activity.IsHarvest && colonist.WorkCycleStartTicks < 0)
            colonist.WorkCycleStartTicks = now;
        else if (!activity.IsHarvest && colonist.Carrying is null)
        {
            colonist.WorkCycleStartTicks = -1;
            colonist.WorkCycleExtraHours = 0;
        }

        colonist.Activity = activity;
        colonist.Path = path;
        colonist.PathIndex = 0;
        colonist.PathMaxStep = maxStep;
        Reserve(colonist.Colony, activity);
    }

    private static bool IsStranded(Colonist colonist, LocalMap map)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if ((dx != 0 || dy != 0) && map.CanStep(colonist.TileX, colonist.TileY, colonist.TileX + dx, colonist.TileY + dy))
                return false;
        return true;
    }

    /// <summary>
    /// Réserve la cible (deux colons ne visent pas le même arbre) et, pour un mineur, la case où il se tiendra :
    /// personne ne doit creuser sous ses pieds.
    /// </summary>
    private static void Reserve(Colony colony, Activity activity)
    {
        if (!activity.ReservesTarget)
            return;
        colony.Reserved.Add((activity.TargetX, activity.TargetY));
        if (activity.Kind == ActivityKind.Mine)
            colony.Reserved.Add((activity.StandX, activity.StandY));
    }

    private static void Release(Colony colony, Activity activity)
    {
        if (!activity.ReservesTarget)
            return;
        colony.Reserved.Remove((activity.TargetX, activity.TargetY));
        if (activity.Kind == ActivityKind.Mine)
            colony.Reserved.Remove((activity.StandX, activity.StandY));
    }

    private static void Move(Colonist colonist, WorldState world)
    {
        LocalMap map = world.Map;
        (int nextX, int nextY) = colonist.Path[colonist.PathIndex];

        // Le terrain a pu changer depuis le calcul du chemin (une case minée, par exemple).
        if (!map.CanStep(colonist.TileX, colonist.TileY, nextX, nextY, colonist.PathMaxStep))
        {
            Activity activity = colonist.Activity!;
            Release(colonist.Colony, activity);
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
            // Quiconque revient au camp y dépose ce qu'il rapporte (sauf les matériaux destinés à un chantier).
            if (colonist.Carrying is { } load && colonist.CarryingTo is null && IsAtCamp(colonist))
            {
                colonist.Colony.Stock.Add(load.Type, load.Amount);
                colonist.Carrying = null;
                if (colonist.WorkCycleStartTicks >= 0)
                {
                    double hours = LaborLedger.TicksToHours(world.Clock.Ticks - colonist.WorkCycleStartTicks);
                    if (load.Type == ResourceType.Grain)
                        hours += colonist.Colony.SowHoursPerPlot;
                    // Un produit fabriqué porte aussi le travail de ses matières premières.
                    hours += colonist.WorkCycleExtraHours;
                    colonist.WorkCycleExtraHours = 0;
                    colonist.Colony.Labor.Record(load.Type, hours, load.Amount);
                    colonist.WorkCycleStartTicks = -1;
                }
            }

            if (!CanBegin(colonist, world, activity))
            {
                // L'interlocuteur est parti ailleurs : on réessaiera dans une heure.
                if (activity.Kind == ActivityKind.Chat)
                    colonist.ChatCooldownTicks = (int)TimeConstants.TicksPerHour;
                Cancel(colonist);
                return;
            }
            activity.Started = true;
        }

        activity.ElapsedTicks++;
        if (activity.Skill is { } skill)
            colonist.Skills.Practice(skill, colonist.Personality.LearningFactor * LearningFactorOf(colonist.Stage) / TimeConstants.TicksPerSecond);

        Needs needs = colonist.Needs;
        bool done = activity.Kind == ActivityKind.Sleep
            ? needs.Rest >= 1f
              || (!world.Clock.IsNight && needs.Rest >= 0.7f)
              || (needs.Food < 0.1f && needs.Rest > 0.15f) // la faim réveille
            : activity.ElapsedTicks >= activity.DurationTicks;
        if (done)
            Finish(colonist, world, activity);
    }

    /// <summary>Vérifie, à l'arrivée, que l'action est toujours possible (un autre a pu cueillir le buisson entre-temps).</summary>
    private static bool CanBegin(Colonist colonist, WorldState world, Activity activity) => activity.Kind switch
    {
        ActivityKind.Eat => colonist.Colony.Stock.TryTakeMeal(),
        ActivityKind.Chat => activity.Partner is { Transit: TransitState.None, IsSleeping: false } partner
                             && MathF.Abs(partner.X - colonist.X) + MathF.Abs(partner.Y - colonist.Y) <= ChatMaxGap,
        ActivityKind.Sow => Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Fallow }
                            && Farming.IsSowingSeason(world.Clock.Season),
        ActivityKind.Harvest => Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Ripe },
        ActivityKind.Forage or ActivityKind.ForageToEat => world.Map.GetBerries(activity.TargetX, activity.TargetY) > 0,
        ActivityKind.Fish => world.Map.GetFish(activity.TargetX, activity.TargetY) > 0,
        ActivityKind.Chop => world.Map.CanChop(activity.TargetX, activity.TargetY),
        ActivityKind.Mine => WorkSites.CanMineFrom(world.Map, colonist.TileX, colonist.TileY, activity.TargetX, activity.TargetY),
        ActivityKind.Dig => !world.Map.IsCanal(activity.TargetX, activity.TargetY),
        ActivityKind.Craft => activity.Building is { IsComplete: true } workshop && TakeCraftInputs(colonist, activity, workshop),
        ActivityKind.FetchMaterials => TakeMaterials(colonist, activity.Building!),
        ActivityKind.SupplySite => activity.Building is { IsComplete: false } site && colonist.CarryingTo == site,
        ActivityKind.Build => activity.Building is { IsComplete: false, HasAllMaterials: true },
        _ => true,
    };

    /// <summary>À l'arrivée à l'atelier, on prend au stock les matières de la recette ; elles manquent peut-être déjà.</summary>
    private static bool TakeCraftInputs(Colonist colonist, Activity activity, Building workshop)
    {
        if (!ToolChain.TryTakeInputs(colonist.Colony, ToolChain.RecipeFor(workshop.Type), out double inputHours))
            return false;
        activity.InputsTaken = true;
        activity.InputLaborHours = inputHours;
        colonist.WorkCycleExtraHours = inputHours;
        return true;
    }

    /// <summary>Prend au stock le matériau qui manque encore au chantier (bois d'abord, puis pierre), dans la limite de ce qu'on peut porter.</summary>
    private static bool TakeMaterials(Colonist colonist, Building site)
    {
        if (site.IsComplete || colonist.Carrying is not null || site.MaterialToFetch(colonist.Colony.Stock) is not { } type)
            return false;
        int amount = Math.Min(CarryCapacity, Math.Min(site.StillToBring(type), colonist.Colony.Stock.Get(type)));
        if (amount <= 0 || !colonist.Colony.Stock.TryTake(type, amount))
            return false;
        colonist.Carrying = (type, amount);
        colonist.CarryingTo = site;
        site.AddInTransit(type, amount);
        return true;
    }

    private static void Finish(Colonist colonist, WorldState world, Activity activity)
    {
        LocalMap map = world.Map;
        if (activity.Building is { } building)
            building.LaborTicks += world.Clock.Ticks - activity.CommittedAtTicks;
        if (activity.Skill is { } usedSkill)
            ToolChain.RecordUse(colonist.Colony, usedSkill);

        switch (activity.Kind)
        {
            case ActivityKind.Eat:
                colonist.Needs.Food += MealValue;
                break;
            case ActivityKind.Forage:
            {
                int berries = map.HarvestBerries(activity.TargetX, activity.TargetY);
                if (berries > 0)
                    colonist.Carrying = (ResourceType.Food, berries);
                break;
            }
            case ActivityKind.ForageToEat:
                colonist.Needs.Food += BerryValue * map.HarvestBerries(activity.TargetX, activity.TargetY);
                break;
            case ActivityKind.SupplySite when colonist.Carrying is { } load && activity.Building is { } site:
                site.Deliver(load.Type, load.Amount);
                colonist.Carrying = null;
                colonist.CarryingTo = null;
                break;
            case ActivityKind.Build when activity.Building is { IsComplete: false } site:
                site.Progress = MathF.Min(1f, site.Progress + BuildActionSeconds / site.WorkSeconds);
                if (site.IsComplete)
                    ColonyBrain.OnBuildingComplete(colonist.Colony, site, world.Clock);
                break;
            case ActivityKind.Sow when Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Fallow } plot:
                plot.Stage = CropStage.Growing;
                plot.Growth = 0f;
                colonist.Colony.RecordSowing(world.Clock.Ticks - activity.CommittedAtTicks);
                break;
            case ActivityKind.Harvest when Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Ripe } plot:
                plot.Stage = CropStage.Fallow;
                plot.Growth = 0f;
                colonist.Carrying = (ResourceType.Grain, Farming.YieldAt(map, activity.TargetX, activity.TargetY));
                break;
            case ActivityKind.Craft when activity.InputsTaken && activity.Building is { } workshop:
            {
                Recipe recipe = ToolChain.RecipeFor(workshop.Type);
                bool first = colonist.Colony.AnnouncedProducts.Add(recipe.Output);
                colonist.Carrying = (recipe.Output, recipe.OutputAmount);
                activity.InputsTaken = false;
                if (first)
                    ColonyBrain.OnFirstProduct(colonist.Colony, recipe.Output, world.Clock);
                break;
            }
            case ActivityKind.Dig when !map.IsCanal(activity.TargetX, activity.TargetY):
                FinishDig(colonist, world, activity);
                break;
            case ActivityKind.Chat when activity.Partner is { } partner:
            {
                Relations.Outcome outcome = Relations.Converse(colonist, partner, world.Random);
                float gain = outcome.Dispute ? QuarrelSocialGain
                    : ChatSocialGain + (Relations.Affinity(colonist, partner) >= Relations.FriendThreshold ? FriendChatBonus : 0f);
                colonist.Needs.Social += gain;
                partner.Needs.Social += gain;
                partner.Needs.Clamp();
                ColonyBrain.OnConversation(colonist.Colony, colonist, partner, outcome, world.Clock);
                break;
            }
            case ActivityKind.Fish when map.CatchFish(activity.TargetX, activity.TargetY):
                colonist.Carrying = (ResourceType.Food, FoodPerFish);
                break;
            case ActivityKind.Chop when map.CanChop(activity.TargetX, activity.TargetY):
                colonist.Carrying = (ResourceType.Wood, map.ChopTree(activity.TargetX, activity.TargetY));
                break;
            case ActivityKind.Mine when WorkSites.CanMineFrom(map, colonist.TileX, colonist.TileY, activity.TargetX, activity.TargetY):
                colonist.Carrying = map.Mine(activity.TargetX, activity.TargetY) == Material.IronOre
                    ? (ResourceType.IronOre, IronOrePerLayer)
                    : (ResourceType.Stone, StonePerLayer);
                break;
        }
        colonist.Needs.Clamp();
        EndActivity(colonist);
    }

    /// <summary>Une case de canal est creusée ; si le fossé est continu depuis la source, l'eau avance.</summary>
    private static void FinishDig(Colonist colonist, WorldState world, Activity activity)
    {
        Colony colony = colonist.Colony;
        LocalMap map = world.Map;
        map.DigCanal(activity.TargetX, activity.TargetY);
        colony.Labor.RecordCanalTile(LaborLedger.TicksToHours(world.Clock.Ticks - activity.CommittedAtTicks));

        Canal? canal = colony.Canals.FirstOrDefault(c => c.Contains(activity.TargetX, activity.TargetY));
        if (canal is null)
            return;
        foreach ((int x, int y) in canal.MarkDug(activity.TargetX, activity.TargetY))
            map.FillCanal(x, y);
        if (canal.IsComplete)
            ColonyBrain.OnCanalComplete(colony, canal, map, world.Clock);
    }

    private static void Cancel(Colonist colonist) => EndActivity(colonist);

    private static void EndActivity(Colonist colonist)
    {
        if (colonist.Activity is { } activity)
        {
            Release(colonist.Colony, activity);
            // Une fabrication interrompue rend les matières à la colonie.
            if (activity is { Kind: ActivityKind.Craft, InputsTaken: true, Building: { } workshop })
            {
                ToolChain.Refund(colonist.Colony, ToolChain.RecipeFor(workshop.Type));
                activity.InputsTaken = false;
                colonist.WorkCycleExtraHours = 0;
            }
        }
        colonist.Activity = null;
        colonist.Path = [];
        colonist.PathIndex = 0;
        colonist.PathMaxStep = 1;
        colonist.ThinkCooldown = 0;
    }

    private static float Ticks(float seconds) => seconds * TimeConstants.TicksPerSecond;
}
