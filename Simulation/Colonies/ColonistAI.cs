using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;
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
    private const float WalkTilesPerSecond = 10f;
    private const float EatSeconds = 1.5f;
    private const float CriticalFoodThreshold = 0.1f;
    private const float ForageSeconds = 1.5f;
    private const float FishSeconds = 3f;
    private const int FoodPerFish = 2;
    private const float ChopSeconds = 4f;
    private const float MineSeconds = 5f;

    /// <summary>Au-delà de ce multiple de la réserve de pierre, on ne creuse plus la roche stérile pour trouver du minerai.</summary>
    private const int StoneGlutFactor = 3;
    /// <summary>Creuser une case de canal à la bêche : plus long que d'abattre un arbre.</summary>
    private const float DigSeconds = 4f;
    private const float DeliverSeconds = 0.5f;
    private const float RelaxSeconds = 6f;
    private const float TendSeconds = 3f;
    private const float SlaughterSeconds = 5f;
    /// <summary>Un artisan qui n'a presque plus rien dans l'estomac interrompt sa fabrication (longue : jusqu'à dix-sept heures) pour manger, puis la reprend.</summary>
    private const float MealBreakFood = 0.15f;
    private const float TameSeconds = 3f;
    private const float CaptureSeconds = 5f;
    private const float GreatHuntSeconds = 4f;

    /// <summary>La gaieté d'un bon repas retombe en deux jours.</summary>
    private const float CheerFadePerHour = 1f / 48f;
    private const float HealSeconds = 5f;
    private const float StudySeconds = 6f;

    /// <summary>À la taverne on se détend plus vite, et l'on y trouve de la compagnie.</summary>
    private const float TavernRelaxFactor = 1.5f;
    private const float TavernSocialPerHour = 0.15f;

    internal const int ThinkIntervalTicks = 10;
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
    internal const int CarryCapacity = 6;
    private const float FetchSeconds = 0.5f;
    private const float BuildActionSeconds = 3f;
    private const float BerryValue = 0.2f;

    /// <summary>On cueille des plantes médicinales tant qu'on en a moins que cela en réserve ; leur valeur compte comme celle de quelques repas.</summary>
    private const int HerbReserve = 4;
    private const float HerbValue = 1.5f;

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

        // À bout de forces, il s'endort là où il est, après avoir terminé un repas vital (trajet compris).
        bool urgentMeal = colonist.Needs.Food < CriticalFoodThreshold
            && colonist.Activity?.Kind is ActivityKind.Eat or ActivityKind.ForageToEat;
        if (colonist.Needs.Rest <= 0f && colonist.Activity?.Kind != ActivityKind.Sleep && !urgentMeal)
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
        if (colonist.PausedCraft is { } paused)
        {
            RefundCraft(colonist, paused);
            colonist.PausedCraft = null;
        }

        // Ce qu'il portait reste à la colonie ; un chantier qui l'attendait réclamera d'autres bras.
        if (colonist.Carrying is { } load)
        {
            if (colonist.CarryingTo is { } site)
                site.AddInTransit(load.Type, -load.Amount);
            colony.Stock.Add(load.Type, load.Amount, colonist.WorkCycleStartTicks >= 0 ? ResourceFlow.Production : ResourceFlow.Transfer);
            colonist.Carrying = null;
            colonist.CarryingTo = null;
        }
        colonist.WorkCycleStartTicks = -1;
        colonist.WorkCycleExtraHours = 0;
        SpecialistAssignments.Invalidate(colonist);

        colony.PresentMembers.Remove(colonist);
        if (colonist.TravelId == 0 && colonist.Home is { } home)
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
        return Migration.FindEdgePoint(world, colonist.Colony, colonist.TileX, colonist.TileY) is { } exit
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
        needs.Cheer -= CheerFadePerHour * hour;
        needs.BeerCheer -= hour / (Cuisine.BeerDays * 24f);

        // On dort mieux à l'abri d'une hutte ; dehors, sans feu pendant la saison froide, le sommeil répare mal.
        bool sheltered = colonist.IsSleepingAtHome;
        bool cold = !sheltered && ColonyBrain.IsColdSeason(world.Clock.Season) && !colonist.Colony.FireLit;
        if (colonist.IsSleeping)
            needs.Rest += SleepRecoveryPerHour * (sheltered ? ShelterSleepFactor : cold ? ColdSleepFactor : 1f) * hour;
        else
            needs.Rest -= FatiguePerHour * hour;

        if (colonist.Activity is { Kind: ActivityKind.Relax, Started: true } relaxing)
        {
            bool tavern = relaxing.Building?.Type == BuildingType.Tavern;
            needs.Leisure += RelaxRecoveryPerHour * (tavern ? TavernRelaxFactor : 1f) * hour;
            if (tavern)
                needs.Social += TavernSocialPerHour * hour;
        }
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
        return 0.2f + (housed ? 0.4f : 0f) + (warm ? 0.3f : 0f) + (housed && cold ? 0.1f : 0f) + Civic.ComfortBonus(colonist.Colony, cold)
            + (colonist.HasShoes ? .03f : 0) + (colonist.Colony.LocalSettlement.Equipment.CopperwareExpiry.Count > 0 ? .02f : 0);
    }

    /// <summary>Choisit la prochaine activité, du besoin le plus pressant au travail.</summary>
    private static void Choose(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        Needs needs = colonist.Needs;
        var clock = world.Clock;

        // Épuisement : dormir passe avant les besoins ordinaires, mais une faim critique doit pouvoir être rassasiée.
        if (needs.Rest < 0.15f && needs.Food >= CriticalFoodThreshold)
        {
            GoToSleep(colonist, world);
            return;
        }

        // On mange quand on a faim, et on dîne avant d'aller se coucher pour ne pas se réveiller affamé.
        bool bedtime = clock.IsNight || clock.IsEvening;
        if (needs.Food < 0.4f || (bedtime && needs.Food < 0.65f))
        {
            if (colony.Stock.FoodUnits > 0 && StartAtService(colonist, world, ActivityKind.Eat, EatSeconds, ServiceUse.Meal))
                return;
            // Du pain attend à la sortie d'un four : on le récupère avant de déclarer que la colonie n'a rien à manger.
            if (colony.Stock.FoodUnits == 0 && BatchProduction.BufferedFood(colony) > 0
                && (colonist.Carrying is { Type: ResourceType.Bread } && colonist.CarryingTo is null
                    ? StartAtService(colonist, world, ActivityKind.Deliver, DeliverSeconds, ServiceUse.Stock) : TryCollectOutput(colonist, world)))
                return;
            if (TryForage(colonist, world, ActivityKind.ForageToEat, colonist.TileX, colonist.TileY))
                return;
            // Dernier recours : les céréales crues, presque sans valeur nutritive, seulement quand rien d'autre ne se mange ni ne se cueille.
            if (colony.Stock.HasAnyMeal && StartAtService(colonist, world, ActivityKind.Eat, EatSeconds, ServiceUse.Meal))
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
            && (TryHaulMore(colonist, world) || StartAtService(colonist, world, ActivityKind.Deliver, DeliverSeconds, ServiceUse.Stock)))
            return;

        // Les produits finis qui attendent à la sortie d'un atelier partent vers le dépôt (le pain avant tout).
        if (needs.Food >= 0.3f && needs.Rest > 0.2f && clock.Hour >= 6 && clock.Hour < 17 && colonist.Ailment == Ailment.None && TryCollectOutput(colonist, world))
            return;

        // Malade ou blessé : on se repose (à l'infirmerie, s'il y en a une) jusqu'à la guérison.
        if (colonist.Ailment != Ailment.None)
        {
            if (!TryRecover(colonist, world))
                Wander(colonist, world);
            return;
        }

        // Une fabrication interrompue pour un repas reprend dès qu'on a mangé et un peu dormi.
        if (colonist.PausedCraft is not null && needs.Food >= 0.5f && needs.Rest > 0.2f && TryResumeCraft(colonist, world))
            return;

        // Besoin de compagnie : les sociables en cherchent plus tôt, les solitaires se contentent de peu.
        // Les conversations ont lieu le soir et pendant le temps libre, sauf si la solitude devient pesante.
        float socialThreshold = 0.75f - 0.2f * colonist.Personality[Axis.Sociabilite];
        bool leisureTime = colonist.Sector == WorkSector.Free || clock.Hour >= 17;
        if (colonist.Stage != LifeStage.Child && needs.Social < socialThreshold && (leisureTime || needs.Social < 0.2f) && !clock.IsNight
            && colonist.ChatCooldownTicks <= 0 && TryChat(colonist, world))
            return;

        if ((needs.Leisure < 0.3f || (clock.IsEvening && needs.Leisure < 0.8f))
            && StartRelax(colonist, world))
            return;

        // Un chasseur désigné pour la grande chasse y va même s'il était en temps libre.
        if (colonist.Stage == LifeStage.Adult && needs.Food > 0.5f && needs.Rest > 0.4f && clock.Hour >= 6 && clock.Hour < 17 && TryGreatHunt(colonist, world))
            return;

        // Temps libre : la colonie n'a pas besoin de ses bras pour l'instant, il se détend.
        if (colonist.Sector == WorkSector.Free || colonist.Stage == LifeStage.Child)
        {
            // Les enfants vont à l'école, quand il y en a une.
            if (colonist.Stage == LifeStage.Child && Civic.IsSchoolTime(clock) && TryStudy(colonist, world))
                return;
            // Un vrai temps libre peut servir à aménager un chemin très fréquenté (facultatif, jamais en crise).
            if (colonist.Stage != LifeStage.Child && needs.Leisure >= 0.6f && clock.Hour >= 6 && clock.Hour < 17 && TryRoadWork(colonist, world, idle: true))
                return;
            // Des bras libres en journée, besoins et loisirs satisfaits : on met de côté des surplus utiles (réserves, produits d'atelier pour le commerce).
            if (colonist.Stage == LifeStage.Adult && needs.Leisure >= 0.5f && needs.Food > 0.5f && needs.Rest > 0.4f && clock.Hour >= 6 && clock.Hour < 17
                && TrySurplusWork(colonist, world))
                return;
            if (needs.Leisure < 0.95f && StartRelax(colonist, world))
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
        foreach (Colonist other in colonist.Colony.PresentMembers)
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
                // Une bête abattue pour un besoin de vivres passe avant la cueillette : c'est la nourriture la plus rapide à obtenir.
                WorkSector.Food => TrySlaughter(colonist, world) || TryGatherFood(colonist, world),
                WorkSector.Farm => TryFarm(colonist, world) || TryTend(colonist, world) || TryTame(colonist, world) || TryCapture(colonist, world) || TrySlaughter(colonist, world),
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

    /// <summary>Réserve de bois (en multiple du besoin de chauffage) au-delà de laquelle le temps libre ne sert plus à abattre.</summary>
    private const float SurplusWoodFactor = 2f;

    /// <summary>
    /// Le travail facultatif d'un adulte du temps libre, une fois la survie assurée : un produit d'atelier recherché, une moisson mûre, puis du bois et de la pierre
    /// tant que les réserves restent sous leur plafond (pas d'accumulation sans fin). Les denrées périssables n'en font pas partie.
    /// </summary>
    private static bool TrySurplusWork(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (colony.Sensors is not { SurvivalAssured: true })
            return false;
        if (TryCraft(colonist, world) || TryFarm(colonist, world))
            return true;
        if (colony.Stock.Get(ResourceType.Wood) < ColonyBrain.HeatingTarget(colony, world.Clock.Season) * SurplusWoodFactor + 40 && TryChop(colonist, world))
            return true;
        return colony.Stock.Get(ResourceType.Stone) < ColonyBrain.StoneReserveTarget * StoneGlutFactor && TryMine(colonist, world);
    }

    /// <summary>Se détend à la taverne si la colonie en a une, sinon près du feu.</summary>
    private static bool StartRelax(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        // Une taverne n'accueille que ceux qui peuvent l'atteindre et y trouver une place (huit au plus, en route comprises) ; sinon on se dÃ©tend prÃ¨s du feu.
        foreach (Building tavern in CivicServices.Candidates(colony, colonist, CivicUse.Relax))
        {
            (int x, int y) = StandAt(colonist, tavern);
            if (!TryStart(colonist, world, new Activity(ActivityKind.Relax, x, y, Ticks(RelaxSeconds)) { Building = tavern }))
                continue;
            CivicServices.Note(colony, CivicUse.Relax, served: true);
            return true;
        }
        if (CivicServices.Sites(colony, CivicUse.Relax).Any())
            CivicServices.Note(colony, CivicUse.Relax, served: false);
        return StartAtService(colonist, world, ActivityKind.Relax, RelaxSeconds, ServiceUse.Meet);
    }

    /// <summary>Un malade garde le lit : à l'infirmerie s'il y a de la place (quatre lits), sinon près du feu. Sans hasard, pour ne pas décaler les graines.</summary>
    private static bool TryRecover(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        // Les lits de l'infirmerie (quatre par bÃ¢timent, en route comprises) ne sont offerts qu'Ã  qui peut y aller ; sans service accessible, le repos de base se fait prÃ¨s du feu.
        foreach (Building infirmary in CivicServices.Candidates(colony, colonist, CivicUse.Recover))
        {
            (int x, int y) = StandAt(colonist, infirmary);
            if (!TryStart(colonist, world, new Activity(ActivityKind.Relax, x, y, Ticks(RelaxSeconds * 2)) { Building = infirmary }))
                continue;
            CivicServices.Note(colony, CivicUse.Recover, served: true);
            return true;
        }
        if (CivicServices.Sites(colony, CivicUse.Recover).Any())
            CivicServices.Note(colony, CivicUse.Recover, served: false);
        IReadOnlyList<(int X, int Y)> spots = colony.GatherSpots;
        (int cx, int cy) = spots[colonist.Id % Math.Min(12, spots.Count)];
        return TryStart(colonist, world, new Activity(ActivityKind.Relax, cx, cy, Ticks(RelaxSeconds * 2)));
    }

    /// <summary>À l'école, un enfant s'exerce au métier où il est le plus doué.</summary>
    private static bool TryStudy(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        // Huit places par Ã©cole : l'Ã©lÃ¨ve qui n'a pas de place ou habite trop loin n'y va pas, et son refus se compte.
        foreach (Building school in CivicServices.Candidates(colony, colonist, CivicUse.Study))
        {
            (int x, int y) = StandAt(colonist, school);
            if (!TryStart(colonist, world, new Activity(ActivityKind.Study, x, y, Ticks(StudySeconds)) { Building = school }))
                continue;
            CivicServices.Note(colony, CivicUse.Study, served: true);
            return true;
        }
        if (CivicServices.Sites(colony, CivicUse.Study).Any())
            CivicServices.Note(colony, CivicUse.Study, served: false);
        return false;
    }

    /// <summary>Soigne les animaux de l'enclos : il y a des œufs à ramasser ou de la laine à tondre.</summary>
    private static bool TryTend(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (!Husbandry.WorkPending(colony) || Husbandry.NearestPen(colony, colonist.TileX, colonist.TileY) is not { } pen)
            return false;
        float seconds = TendSeconds / WorkSpeed(colonist, SkillType.Husbandry);
        (int x, int y) = StandAt(colonist, pen);
        return TryStart(colonist, world, new Activity(ActivityKind.Tend, x, y, Ticks(seconds)) { Building = pen });
    }

    /// <summary>Soigne et nourrit les bêtes capturées à l'enclos, pour qu'elles s'apprivoisent (voir <see cref="Nature.Taming"/>).</summary>
    private static bool TryTame(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (!Nature.Taming.WorkPending(colony) || Husbandry.NearestPen(colony, colonist.TileX, colonist.TileY) is not { } pen)
            return false;
        float seconds = TameSeconds / WorkSpeed(colonist, SkillType.Husbandry);
        (int x, int y) = StandAt(colonist, pen);
        return TryStart(colonist, world, new Activity(ActivityKind.Tame, x, y, Ticks(seconds)) { Building = pen });
    }

    /// <summary>Capture une bête sauvage domestiquable (un jeune de préférence) pour l'apprivoiser à l'enclos.</summary>
    private static bool TryCapture(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (!Nature.Taming.Opportunity(colony))
            return false;
        float seconds = CaptureSeconds / WorkSpeed(colonist, SkillType.Husbandry);
        foreach (Nature.WildHerd herd in Nature.Taming.Targets(colony, colonist).Take(TargetsToTry))
            if (TryStart(colonist, world, new Activity(ActivityKind.Capture, herd.TileX, herd.TileY, Ticks(seconds)) { HerdId = herd.Id }))
                return true;
        return false;
    }

    /// <summary>Un chasseur désigné pour la grande chasse rejoint l'alpha (voir <see cref="Nature.Hunting.PlanGreatHunt"/>).</summary>
    private static bool TryGreatHunt(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (colonist.Stage != LifeStage.Adult || !Nature.Hunting.IsSummoned(colony, colonist) || Nature.Hunting.GreatHuntTarget(colony) is not { } alpha)
            return false;
        return TryStart(colonist, world, new Activity(ActivityKind.GreatHunt, alpha.TileX, alpha.TileY, Ticks(GreatHuntSeconds)) { HerdId = alpha.Id });
    }

    /// <summary>Abat une bête à l'enclos, si la colonie en a donné l'ordre (voir <see cref="Husbandry.PlanSlaughter"/>).</summary>
    private static bool TrySlaughter(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (Husbandry.NextSlaughter(colony) is not { } species || Husbandry.NearestPen(colony, colonist.TileX, colonist.TileY) is not { } pen)
            return false;
        float seconds = SlaughterSeconds / WorkSpeed(colonist, SkillType.Husbandry);
        (int x, int y) = StandAt(colonist, pen);
        return TryStart(colonist, world, new Activity(ActivityKind.Slaughter, x, y, Ticks(seconds)) { Building = pen, Species = species });
    }

    /// <summary>Un guérisseur passe à l'infirmerie s'il y a des malades et que personne n'y soigne déjà.</summary>
    private static bool TryHeal(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        // Un seul soignant actif par infirmerie, et seulement s'il y a des patients rÃ©ellement installÃ©s : les soins ne profitent qu'Ã  ceux qui sont lÃ .
        float seconds = HealSeconds / WorkSpeed(colonist, SkillType.Medicine);
        foreach (Building infirmary in CivicServices.Candidates(colony, colonist, CivicUse.Heal).Where(b => CivicServices.PatientsAt(colony, b).Any()))
        {
            (int x, int y) = StandAt(colonist, infirmary);
            if (TryStart(colonist, world, new Activity(ActivityKind.Heal, x, y, Ticks(seconds)) { Building = infirmary }))
                return true;
        }
        return false;
    }

    /// <summary>
    /// La case où l'on se tient pour travailler dans un bâtiment ou le servir : sa cellule de travail accessible, hors de son emprise (généralement à l'entrée), la plus proche
    /// du colon. Un bâtiment sans porte (parcelle ancienne) se travaille dans son emprise, comme avant.
    /// </summary>
    private static (int X, int Y) StandAt(Colonist colonist, Building building)
    {
        SettlementLayout layout = colonist.Colony.Layout;
        int best = -1;
        int bestDistance = int.MaxValue;
        foreach (int cell in SettlementServices.WorkCells(colonist.Colony, building))
        {
            (int cx, int cy) = layout.Decode(cell);
            int distance = Math.Abs(cx - colonist.TileX) + Math.Abs(cy - colonist.TileY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = cell;
            }
        }
        return layout.Decode(best);
    }

    private static bool IsAtCamp(Colonist colonist) =>
        Math.Max(Math.Abs(colonist.TileX - colonist.Colony.CampX), Math.Abs(colonist.TileY - colonist.Colony.CampY)) <= 6;

    private static void GoToSleep(Colonist colonist, WorldState world)
    {
        (int x, int y) = colonist.Colony.SleepSpot(colonist);
        if (!TryStart(colonist, world, new Activity(ActivityKind.Sleep, x, y, 0)))
            TryStart(colonist, world, new Activity(ActivityKind.Sleep, colonist.TileX, colonist.TileY, 0));
    }

    /// <summary>
    /// Va à un lieu de service (repas, rencontre, dépôt) : parmi les lieux disponibles, celui dont le trajet réel est le plus court (les deux plus proches à vol d'oiseau
    /// sont mesurés), en tenant compte de la suite (le chantier où l'on portera des matériaux). Le camp reste le repli. Le choix de la case est stable (selon le colon),
    /// jamais un tirage de <c>WorldState.Random</c>.
    /// </summary>
    private static bool StartAtService(Colonist colonist, WorldState world, ActivityKind kind, float seconds, ServiceUse use, (int X, int Y)? next = null, Building? building = null)
    {
        Colony colony = colonist.Colony;
        SettlementLayout layout = colony.Layout;
        var options = new List<(ServicePoint Point, int X, int Y, float Estimate)>();
        foreach (ServicePoint point in SettlementServices.Points(colony))
        {
            if (!point.Provides(use))
                continue;
            int cell = point.Cells.Length == 0 ? -1 : PickServiceCell(colonist, layout, point);
            if (cell < 0)
                continue;
            (int cx, int cy) = layout.Decode(cell);
            float estimate = TraversalCost.Octile(cx - colonist.TileX, cy - colonist.TileY)
                + (next is { } n ? TraversalCost.Octile(cx - n.X, cy - n.Y) : 0f);
            options.Add((point, cx, cy, estimate));
        }
        options.Sort((a, b) => a.Estimate != b.Estimate ? a.Estimate.CompareTo(b.Estimate) : a.Point.Id.CompareTo(b.Point.Id));

        Activity? best = null;
        NavPath? bestPath = null;
        int bestMaxStep = 1;
        float bestSeconds = float.MaxValue;
        foreach ((ServicePoint _, int x, int y, float _) in options.Take(2))
        {
            var activity = new Activity(kind, x, y, Ticks(seconds)) { Building = building };
            (NavPath? path, int maxStep) = PlanPath(colonist, world, activity);
            if (path is null)
                continue;
            float total = path.Seconds + (next is { } n ? TraversalCost.Octile(x - n.X, y - n.Y) / SettlementRules.WalkTilesPerSecond : 0f);
            if (total < bestSeconds)
                (best, bestPath, bestMaxStep, bestSeconds) = (activity, path, maxStep, total);
        }
        if (best is null || bestPath is null)
            return building is null ? StartNearCamp(colonist, world, kind, seconds)
                : TryStart(colonist, world, new Activity(kind, colony.CampX, colony.CampY, Ticks(seconds)) { Building = building });
        Commit(colonist, best, bestPath, bestMaxStep, world.Clock.Ticks);
        return true;
    }

    /// <summary>La case de service d'un point : l'une des trois plus proches du colon, choisie selon son identifiant pour qu'on ne s'empile pas sur la même.</summary>
    private static int PickServiceCell(Colonist colonist, SettlementLayout layout, ServicePoint point)
    {
        var cells = point.Cells.Select(c =>
        {
            (int x, int y) = layout.Decode(c);
            return (Cell: c, Distance: TraversalCost.Octile(x - colonist.TileX, y - colonist.TileY));
        }).OrderBy(c => c.Distance).ThenBy(c => c.Cell).Take(3).ToList();
        return cells[colonist.Id % cells.Count].Cell;
    }

    /// <summary>Le colon est à un point de dépôt : le camp, ou la porte d'un entrepôt achevé.</summary>
    private static bool IsAtStockAccess(Colonist colonist)
    {
        if (IsAtCamp(colonist))
            return true;
        SettlementLayout layout = colonist.Colony.Layout;
        foreach (ServicePoint point in SettlementServices.Points(colonist.Colony))
            if (point.Provides(ServiceUse.Stock))
                foreach (int cell in point.Cells)
                {
                    (int x, int y) = layout.Decode(cell);
                    if (Math.Max(Math.Abs(x - colonist.TileX), Math.Abs(y - colonist.TileY)) <= 1)
                        return true;
                }
        return false;
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
        return NearestBushes(colonist.Colony.Map, colonist.Colony, centerX, centerY)
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
        LocalMap map = colonist.Colony.Map;
        var options = new List<(Activity Activity, float Food, float WorkSeconds)>();

        float forageSeconds = ForageSeconds / WorkSpeed(colonist, SkillType.Foraging);
        foreach ((int x, int y) in NearestBushes(map, colony, colony.CampX, colony.CampY).Take(TargetsToTry))
            options.Add((new Activity(ActivityKind.Forage, x, y, Ticks(forageSeconds)), map.GetBerries(x, y), forageSeconds));

        float fishSeconds = FishSeconds / WorkSpeed(colonist, SkillType.Fishing);
        foreach ((int wx, int wy, int sx, int sy) in WorkSites.FishingSpots(map, colony).Take(TargetsToTry))
            options.Add((new Activity(ActivityKind.Fish, wx, wy, Ticks(fishSeconds)) { StandX = sx, StandY = sy }, FoodPerFish, fishSeconds));

        // La chasse : la viande qu'on espère (chance de réussite comprise) contre le temps passé, comme la cueillette et la pêche.
        foreach (Nature.WildHerd herd in Nature.Hunting.Candidates(colony, colonist).Take(TargetsToTry))
        {
            float huntSeconds = Nature.WildSpeciesInfo.HuntSeconds(herd.Species) / WorkSpeed(colonist, SkillType.Hunting);
            options.Add((new Activity(ActivityKind.Hunt, herd.TileX, herd.TileY, Ticks(huntSeconds)) { HerdId = herd.Id },
                Nature.Hunting.ExpectedMeals(colony, colonist, herd), huntSeconds));
        }

        // Les ressources sauvages : miel et champignons se mangent ; les plantes médicinales ne valent qu'en petite réserve.
        foreach (ResourceType kind in Nature.WildResources.Kinds)
        {
            if (kind == ResourceType.Herbs && colony.Stock.Get(ResourceType.Herbs) >= HerbReserve)
                continue;
            float gatherSeconds = Nature.WildResources.GatherSeconds(kind) / WorkSpeed(colonist, SkillType.Foraging);
            float value = kind == ResourceType.Herbs ? HerbValue : Nature.WildResources.Yield(kind);
            foreach ((int x, int y, int _) in Nature.WildResources.Near(colony, kind, colony.CampX, colony.CampY, ForageSearchRadius).Take(TargetsToTry))
                options.Add((new Activity(ActivityKind.Gather, x, y, Ticks(gatherSeconds)) { Product = kind }, value, gatherSeconds));
        }

        Activity? best = null;
        NavPath? bestPath = null;
        int bestMaxStep = 1;
        float bestRate = 0f;
        foreach ((Activity activity, float food, float workSeconds) in options)
        {
            (NavPath? path, int maxStep) = PlanPath(colonist, world, activity);
            if (path is null)
                continue;
            float roundTripSeconds = 2f * path.Seconds;
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
        * ToolChain.SpeedFactor(colonist.Colony, skill) * (skill == SkillType.Farming ? Husbandry.PloughFactor(colonist.Colony) : 1f)
        * Cuisine.BoostFactor(colonist);

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
            return TryDig(colonist, world) || TryRoadWork(colonist, world, idle: false);

        if (!site.HasAllMaterials)
        {
            // Au dépôt le plus utile au trajet vers le chantier (le camp, ou un entrepôt achevé).
            (int sx, int sy) = site.HasDoor ? (site.AccessX, site.AccessY) : (site.X, site.Y);
            return StartAtService(colonist, world, ActivityKind.FetchMaterials, FetchSeconds, ServiceUse.Stock, (sx, sy), site);
        }

        (int bx, int by) = StandAt(colonist, site);
        float seconds = BuildActionSeconds / WorkSpeed(colonist, SkillType.Construction);
        return TryStart(colonist, world, new Activity(ActivityKind.Build, bx, by, Ticks(seconds)) { Building = site });
    }

    /// <summary>
    /// Une charrette : quand le dépôt est loin (voir <see cref="Carts"/>), le colon qui vient d'abattre ou de moissonner enchaîne la même récolte jusqu'à porter quatre charges,
    /// plutôt que de refaire le trajet à chaque fois.
    /// </summary>
    private static bool TryHaulMore(Colonist colonist, WorldState world)
    {
        if (colonist.Carrying is not { Type: ResourceType.Wood or ResourceType.Grain } load || colonist.LastHarvest is not { } kind
            || kind is not (ActivityKind.Chop or ActivityKind.Harvest) || colonist.Stage == LifeStage.Child)
            return false;
        if (!colonist.UsingCart)
        {
            if (!Carts.ShouldTake(colonist.Colony, colonist))
                return false;
            Carts.Take(colonist.Colony, colonist);
            colonist.CartUnit = Math.Max(1, load.Amount);
        }
        if (load.Amount >= Carts.CapacityFactor * colonist.CartUnit)
            return false;
        if (kind == ActivityKind.Chop)
            return TryChop(colonist, world);
        float speed = WorkSpeed(colonist, SkillType.Farming);
        foreach (FieldPlot plot in NearestPlots(colonist.Colony, colonist, CropStage.Ripe).Where(p => p.Crop == CropKind.Grain).Take(TargetsToTry))
            if (TryStart(colonist, world, new Activity(ActivityKind.Harvest, plot.X, plot.Y, Ticks(Farming.HarvestSeconds / speed))))
                return true;
        return false;
    }

    /// <summary>Ajoute une prise à ce que le colon porte (la charrette rapporte plusieurs récoltes d'un coup).</summary>
    private static void AddLoad(Colonist colonist, ResourceType type, int amount) =>
        colonist.Carrying = colonist.Carrying is { } held && held.Type == type ? (type, held.Amount + amount) : (type, amount);

    /// <summary>Un aménagement de chemin facultatif : une cellule d'un axe très fréquenté (voir <see cref="RoadWorks"/>).</summary>
    private static bool TryRoadWork(Colonist colonist, WorldState world, bool idle)
    {
        float speed = WorkSpeed(colonist, SkillType.Construction);
        Activity? activity = Bridges.NextActivity(colonist, speed) ?? RoadWorks.NextActivity(colonist, speed, idle);
        return activity is not null && TryStart(colonist, world, activity);
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
        (int bx, int by) = StandAt(colonist, site);
        return TryStart(colonist, world, new Activity(ActivityKind.SupplySite, bx, by, Ticks(DeliverSeconds)) { Building = site });
    }

    private static bool TryChop(Colonist colonist, WorldState world)
    {
        float seconds = ChopSeconds / WorkSpeed(colonist, SkillType.Woodcutting);
        return WorkSites.TreesToChop(colonist.Colony.Map, colonist.Colony)
            .Take(TargetsToTry)
            .Any(t => TryStart(colonist, world, new Activity(ActivityKind.Chop, t.X, t.Y, Ticks(seconds))));
    }

    /// <summary>Va travailler dans l'atelier que la chaîne du fer réclame en ce moment, s'il y en a un.</summary>
    private static bool TryCraft(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        if (TryHeal(colonist, world))
            return true;
        // Le gâteau passe avant le pain (sinon la farine part toute en pain), et le ragoût et la bière avant les chaînes du fer, du blé, du textile et du négoce
        // tant que leur stock est presque vide (sinon ces chaînes occuperaient les artisans sans cesse) ; sinon ils passent après, pour compléter le stock.
        // Le choix du travail suit la priorité de la colonie (voir Crafting.Candidates) ; un artisan qui pratique un atelier le retrouve s'il est recevable, en temps ordinaire.
        CraftCandidate? pick = null;
        IEnumerable<CraftCandidate> jobs = Crafting.Candidates(colony, (int)ColonyBrain.HeatingTarget(colony, world.Clock.Season));
        if (colonist.PreferredWorkshopId != 0 && SpecialistAssignments.Applies(colony))
            pick = SpecialistAssignments.Choose(colony, colonist, jobs.Take(6).ToList(), world.Clock.Ticks);
        else
            foreach (CraftCandidate job in jobs) { pick = job; break; }
        if (pick is not { } chosen)
            return false;
        // Une offrande en chantier occupe au plus un dixième des artisans (jamais en crise) : elle avance même quand une chaîne cherche sans cesse du travail.
        if (chosen.Sculpt)
        {
            (int sx, int sy) = StandAt(colonist, chosen.Workshop);
            return TryStart(colonist, world, new Activity(ActivityKind.Sculpt, sx, sy, Ticks(Offerings.ChunkSeconds / WorkSpeed(colonist, SkillType.Construction))) { Building = chosen.Workshop });
        }
        Building workshop = chosen.Workshop;
        ResourceType? product = chosen.Product;
        if (workshop.Type == BuildingType.Market)
        {
            var source = world.VisitRegion(colony.LocalSettlement.RegionTileIndex).Deposits.FirstOrDefault(d => d.Material == Specialties.NativeOf(colony));
            if (source is null || colony.PresentMembers.Any(c => c.Activity?.DepositId == source.Id)) return false;
            if (source.Material == ResourceType.Hardwood && !colony.Map.CanChop(source.X,source.Y))
            {
                var tree = WorkSites.TreesToChop(colony.Map,colony).Take(1).ToArray();
                if (tree.Length == 0) return false;
                source.X = tree[0].X; source.Y = tree[0].Y;
            }
            if (source.Material == ResourceType.Spices && colony.Map.GetBerries(source.X,source.Y) == 0) return false;
            return TryStart(colonist,world,new Activity(ActivityKind.Extract,source.X,source.Y,
                Ticks(10f / WorkSpeed(colonist,SkillType.Trading))) { DepositId = source.Id });
        }
        Recipe recipe = Crafting.RecipeFor(colony, workshop.Type, product);
        // Un atelier à lots : on choisit le lot (répétitions, recette concrète, poste) ; la compétence et le débit s'appliquent ensuite, une seule fois.
        BatchChoice? batch = null;
        if (product is null && BatchProduction.IsEligible(workshop.Type))
        {
            batch = BatchProduction.Choose(colony, workshop, recipe, (int)ColonyBrain.HeatingTarget(colony, world.Clock.Season));
            if (batch is null)
                return false;
            recipe = batch.Recipe;
        }
        float seconds = recipe.Seconds / WorkSpeed(colonist, Crafting.SkillFor(workshop.Type));
        // Un moulin tourne au rythme de la rivière : un barrage en amont le ralentit.
        if (workshop.Type == BuildingType.Mill)
            seconds /= MathF.Max(0.25f, Hydrology.MillFlow(colonist.Colony.Map, workshop));
        (int x, int y) = batch is null ? StandAt(colonist, workshop) : StandAtSlot(colonist, workshop, batch.SlotId);
        return TryStart(colonist, world, new Activity(ActivityKind.Craft, x, y, Ticks(seconds))
        {
            Building = workshop, Product = product,
            BatchCount = batch?.Count ?? 1, WorkshopSlotId = batch?.SlotId ?? -1, PlannedRecipe = batch?.Recipe,
        });
    }

    /// <summary>La case de travail d'un poste : avec deux postes, chacun a la sienne (la première, la deuxième) ; avec un seul, la plus proche comme pour tout atelier.</summary>
    private static (int X, int Y) StandAtSlot(Colonist colonist, Building workshop, int slot)
    {
        if (WorkshopCapacity.Slots(colonist.Colony, workshop) < 2)
            return StandAt(colonist, workshop);
        int[] cells = SettlementServices.WorkCells(colonist.Colony, workshop);
        return colonist.Colony.Layout.Decode(cells[Math.Min(slot, cells.Length - 1)]);
    }

    /// <summary>Va chercher à la sortie d'un atelier les produits finis qui y attendent, s'il y en a et que ce colon doit s'en charger.</summary>
    private static bool TryCollectOutput(Colonist colonist, WorldState world)
    {
        if (BatchProduction.PickCollection(colonist.Colony, colonist) is not { } workshop)
            return false;
        (int x, int y) = StandAt(colonist, workshop);
        return TryStart(colonist, world, new Activity(ActivityKind.CollectWorkshopOutput, x, y, Ticks(DeliverSeconds)) { Building = workshop });
    }

    /// <summary>
    /// Annule les fabrications engagées (en cours ou en pause) que désigne le filtre : les intrants encore présents retournent au stock, rien d'autre n'est recréé.
    /// Utilisé quand un atelier ou un poste disparaît.
    /// </summary>
    internal static void AbandonCrafts(Colony colony, Func<Activity, bool> doomed)
    {
        foreach (Colonist colonist in colony.PresentMembers.ToArray())
        {
            if (colonist.Activity is { Kind: ActivityKind.Craft } active && doomed(active))
                Cancel(colonist);
            if (colonist.PausedCraft is { Kind: ActivityKind.Craft } paused && doomed(paused))
            {
                RefundCraft(colonist, paused);
                colonist.PausedCraft = null;
            }
        }
    }

    private static bool TryMine(Colonist colonist, WorldState world)
    {
        float seconds = MineSeconds / WorkSpeed(colonist, SkillType.Mining);
        bool wantOre = colonist.Colony.Sensors?.Chain is { Active: true, OreMissing: > 0 };
        Colony colony = colonist.Colony;
        foreach (var deposit in ExtendedIndustry.ExtractionJobs(world, colony).Take(TargetsToTry))
            if (!colony.PresentMembers.Any(c => c.Activity?.DepositId == deposit.Id)
                && TryStart(colonist, world, new Activity(ActivityKind.Extract, deposit.X, deposit.Y, Ticks(seconds * deposit.Difficulty)) { DepositId = deposit.Id })) return true;
        foreach ((int rockX, int rockY, int standX, int standY) in WorkSites.RocksToMine(colonist.Colony.Map, colony, wantOre)
                     .Where(r => !colony.UnreachableStands.Contains((r.StandX, r.StandY)))
                     .Take(TargetsToTry))
        {
            // On cherche du minerai, mais aucun filon n'est proche : inutile d'entasser de la pierre dont on n'a que faire.
            if (wantOre && colony.Stock.Get(ResourceType.Stone) >= ColonyBrain.StoneReserveTarget * StoneGlutFactor
                && colony.Map.DepthToOre(rockX, rockY, WorkSites.OreProspectDepth) == int.MaxValue)
                return false;
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
            if (colony.Map.InBounds(x, y) && colony.Spatial.BlocksWalking(x, y))
                continue; // on ne flâne pas à l'intérieur d'une maison
            if (TryStart(colonist, world, new Activity(ActivityKind.Wander, x, y, Ticks(seconds))))
                return;
        }
    }

    /// <summary>Calcule le chemin vers l'endroit de l'action et la confie au colon. Renvoie false si l'endroit est inaccessible.</summary>
    private static bool TryStart(Colonist colonist, WorldState world, Activity activity)
    {
        (NavPath? path, int maxStep) = PlanPath(colonist, world, activity);
        if (path is null)
            return false;
        Commit(colonist, activity, path, maxStep, world.Clock.Ticks);
        return true;
    }

    private static (NavPath? Path, int MaxStep) PlanPath(Colonist colonist, WorldState world, Activity activity)
    {
        NavPath? path = LocalNavigation.FindPath(colonist.Colony, colonist.TileX, colonist.TileY, activity.StandX, activity.StandY);

        // Filet de sécurité : coincé dans un trou ou sur un plateau isolé par la carrière,
        // on escalade une marche de deux niveaux pour rentrer au camp.
        if (path is null && (IsStranded(colonist, colonist.Colony.Map) || IsGoingHome(activity)))
        {
            // D'abord une marche de deux niveaux ; si le colon est enfermé dans un trou plus profond, il escalade
            // la paroi à mains nues plutôt que d'y mourir de faim.
            foreach (int step in new[] { 2, LocalMap.MaxElevation })
                if (LocalNavigation.FindPath(colonist.Colony, colonist.TileX, colonist.TileY, activity.StandX, activity.StandY, step) is { } rescue)
                    return (rescue, step);
            return (null, 2);
        }
        return (path, 1);
    }

    private static bool IsGoingHome(Activity activity) =>
        activity.Kind is ActivityKind.Sleep or ActivityKind.Eat or ActivityKind.Deliver or ActivityKind.Relax;

    private static void Commit(Colonist colonist, Activity activity, NavPath path, int maxStep, long now)
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
        colonist.Path = path.Cells;
        colonist.PathIndex = 0;
        colonist.PathMaxStep = maxStep;
        colonist.PathStartBuilding = path.StartBuildingId;
        colonist.PathGoalBuilding = path.GoalBuildingId;
        colonist.PathCommittedTicks = now;
        colonist.PathStartX = colonist.TileX;
        colonist.PathStartY = colonist.TileY;
        colonist.PathRevision = colonist.Colony.Spatial.Revision;
        colonist.StepElapsedTicks = 0f;
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

    /// <summary>
    /// Fait avancer le colon le long de son chemin pendant un tick. Un pas dure autant que son arête (<see cref="TraversalCost"/> : longueur, terrain, route,
    /// montée) : la même durée que celle qu'utilisent le pathfinder et les budgets du planificateur. Le temps du tick se consomme sur le pas courant et déborde sur
    /// le suivant ; chaque pas terminé compte comme un passage (voir <see cref="RoadDevelopment"/>). Le prochain pas est vérifié avec le terrain et l'occupation actuels.
    /// </summary>
    private static void Move(Colonist colonist, WorldState world)
    {
        Colony colony = colonist.Colony;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;

        // Un bâtiment a pu apparaître sur la route depuis le calcul du chemin : on revérifie le reste du trajet, sans tout recalculer.
        if (colonist.PathRevision != index.Revision)
        {
            colonist.PathRevision = index.Revision;
            if (!LocalNavigation.StillValid(index, colonist.Path, colonist.PathIndex, colonist.PathStartBuilding, colonist.PathGoalBuilding))
            {
                Replan(colonist, world);
                return;
            }
        }

        float budget = 1f;
        while (budget > 0f && colonist.PathIndex < colonist.Path.Count)
        {
            (int nextX, int nextY) = colonist.Path[colonist.PathIndex];
            (int fromX, int fromY) = colonist.PathIndex == 0
                ? (colonist.PathStartX, colonist.PathStartY)
                : colonist.Path[colonist.PathIndex - 1];

            if (colonist.StepElapsedTicks == 0f)
            {
                // Le terrain a pu changer depuis le calcul du chemin (une case minée, par exemple) : on ne s'engage que sur un pas permis maintenant.
                if (!map.CanStep(fromX, fromY, nextX, nextY, colonist.PathMaxStep))
                {
                    Replan(colonist, world);
                    return;
                }
                colonist.StepFromX = colonist.X;
                colonist.StepFromY = colonist.Y;
            }

            float duration = TraversalCost.StepTicks(map, fromX, fromY, nextX, nextY);
            // Une charrette ne roule bien que sur un chemin : à travers champs, elle ralentit.
            if (colonist.UsingCart && map.Roads.SurfaceAt(nextY * map.Width + nextX) == RoadSurface.None)
                duration /= Carts.OffRoadSpeed;
            float remaining = duration - colonist.StepElapsedTicks;
            float targetX = nextX + 0.5f, targetY = nextY + 0.5f;
            float beforeX = colonist.X, beforeY = colonist.Y;
            if (budget >= remaining)
            {
                budget -= remaining;
                colonist.X = targetX;
                colonist.Y = targetY;
                colonist.PathIndex++;
                colonist.StepElapsedTicks = 0f;
                float walked = MathF.Sqrt((targetX - beforeX) * (targetX - beforeX) + (targetY - beforeY) * (targetY - beforeY));
                colonist.DistanceWalked += walked; ExtendedIndustry.Walk(colonist, walked);
                RoadDevelopment.OnStepCompleted(colony, nextX, nextY);
                if (colonist.PathIndex == colonist.Path.Count)
                    RoadShortcuts.RecordCompletedTrip(colony, colonist, world.Clock.Ticks); // un trajet terminé (jamais un trajet abandonné)
            }
            else
            {
                colonist.StepElapsedTicks += budget;
                budget = 0f;
                float t = colonist.StepElapsedTicks / duration;
                colonist.X = colonist.StepFromX + (targetX - colonist.StepFromX) * t;
                colonist.Y = colonist.StepFromY + (targetY - colonist.StepFromY) * t;
                float walked = MathF.Sqrt((colonist.X - beforeX) * (colonist.X - beforeX) + (colonist.Y - beforeY) * (colonist.Y - beforeY));
                colonist.DistanceWalked += walked; ExtendedIndustry.Walk(colonist, walked);
            }
        }
    }

    /// <summary>Le chemin n'est plus praticable : on cherche un autre trajet vers la même cible, sinon on renonce.</summary>
    private static void Replan(Colonist colonist, WorldState world)
    {
        Activity activity = colonist.Activity!;
        Release(colonist.Colony, activity);
        if (!TryStart(colonist, world, activity))
            Cancel(colonist);
    }

    private static void Act(Colonist colonist, WorldState world)
    {
        Activity activity = colonist.Activity!;
        if (!activity.Started)
        {
            // Quiconque revient au camp y dépose ce qu'il rapporte (sauf les matériaux destinés à un chantier).
            if (colonist.Carrying is { } load && colonist.CarryingTo is null && IsAtStockAccess(colonist))
            {
                colonist.Colony.Stock.Add(load.Type, load.Amount, colonist.WorkCycleStartTicks >= 0 ? ResourceFlow.Production : ResourceFlow.Transfer);
                colonist.Carrying = null;
                Carts.Return(colonist);
                if (colonist.WorkCycleStartTicks >= 0)
                {
                    double hours = LaborLedger.TicksToHours(world.Clock.Ticks - colonist.WorkCycleStartTicks);
                    if (load.Type == ResourceType.Grain)
                        hours += colonist.Colony.SowHoursPerPlot;
                    // Un produit fabriqué porte aussi le travail de ses matières premières.
                    hours += colonist.WorkCycleExtraHours;
                    colonist.WorkCycleExtraHours = 0;
                    colonist.Colony.Labor.Record(load.Type == ResourceType.Fish ? ResourceType.Food : load.Type, hours, load.Amount);
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

        if (activity is { Kind: ActivityKind.Craft, Started: true } && colonist.Needs.Food < MealBreakFood && colonist.PausedCraft is null && colonist.Colony.Stock.FoodUnits > 0)
        {
            PauseForMeal(colonist, activity, world.Clock.Ticks);
            return;
        }
        activity.ElapsedTicks++;
        if (activity is { Kind: ActivityKind.Craft, Building: { } batchShop } && (activity.PlannedRecipe is not null || batchShop.Type == BuildingType.Mint))
            colonist.Colony.LocalSettlement.ScaleLedger.AddWork(batchShop); // le travail effectif d'un lot, hors trajet et repas
        if (activity.Skill is { } skill)
            colonist.Skills.Practice(skill, colonist.Personality.LearningFactor * LearningFactorOf(colonist.Stage) * Civic.LearningBonus(colonist.Colony, activity) / TimeConstants.TicksPerSecond);

        Needs needs = colonist.Needs;
        bool done = activity.Kind == ActivityKind.Sleep
            ? needs.Rest >= 1f
              || (!world.Clock.IsNight && needs.Rest >= 0.7f)
              || (needs.Food < CriticalFoodThreshold && needs.Rest > 0.15f) // la faim réveille
            : activity.ElapsedTicks >= activity.DurationTicks;
        if (done)
            Finish(colonist, world, activity);
    }

    /// <summary>Vérifie, à l'arrivée, que l'action est toujours possible (un autre a pu cueillir le buisson entre-temps).</summary>
    private static bool CanBegin(Colonist colonist, WorldState world, Activity activity) => activity.Kind switch
    {
        ActivityKind.Eat => colonist.Colony.Stock.TryTakeMeal(out float meal, out ResourceType? dish) && SetMeal(activity, meal, dish),
        ActivityKind.Slaughter => activity.Species is { } species && Husbandry.CanSlaughter(colonist.Colony, species),
        ActivityKind.Chat => activity.Partner is { Transit: TransitState.None, IsSleeping: false } partner
                             && MathF.Abs(partner.X - colonist.X) + MathF.Abs(partner.Y - colonist.Y) <= ChatMaxGap,
        ActivityKind.Sow => Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Fallow }
                            && Farming.IsSowingSeason(world.Clock.Season),
        ActivityKind.Harvest => Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Ripe },
        ActivityKind.Forage or ActivityKind.ForageToEat => colonist.Colony.Map.GetBerries(activity.TargetX, activity.TargetY) > 0,
        ActivityKind.Fish => colonist.Colony.Map.GetFish(activity.TargetX, activity.TargetY) > 0,
        ActivityKind.Chop => colonist.Colony.Map.CanChop(activity.TargetX, activity.TargetY),
        ActivityKind.Extract => colonist.Colony.Map.IsWalkable(activity.TargetX, activity.TargetY),
        ActivityKind.Mine => WorkSites.CanMineFrom(colonist.Colony.Map, colonist.TileX, colonist.TileY, activity.TargetX, activity.TargetY),
        ActivityKind.Dig => !colonist.Colony.Map.IsCanal(activity.TargetX, activity.TargetY),
        ActivityKind.BuildRoad when activity.SegmentId < 0 => Bridges.CanBegin(colonist.Colony, activity),
        ActivityKind.BuildRoad => colonist.Colony.Map.Roads.SurfaceAt(activity.TargetY * colonist.Colony.Map.Width + activity.TargetX) != RoadSurface.DirtRoad
                                  && RoadWorks.IsAllowed(colonist.Colony),
        ActivityKind.ClearAccess => colonist.Colony.Map.GetFlora(activity.TargetX, activity.TargetY) == FloraType.Bush && RoadWorks.IsAllowed(colonist.Colony),
        ActivityKind.Craft => activity.Building is { IsComplete: true } workshop && TakeCraftInputs(colonist, activity, workshop),
        ActivityKind.Tend => Husbandry.PenSite(colonist.Colony) is not null && Husbandry.WorkPending(colonist.Colony),
        ActivityKind.Tame => Husbandry.PenSite(colonist.Colony) is not null && Nature.Taming.WorkPending(colonist.Colony),
        ActivityKind.Hunt or ActivityKind.GreatHunt => Nature.Hunting.CanBegin(colonist.Colony, colonist, activity),
        ActivityKind.Capture => Nature.Taming.CanBegin(colonist.Colony, colonist, activity),
        ActivityKind.Gather => activity.Product is { } wild && Nature.WildResources.Left(colonist.Colony.LocalSettlement, wild, activity.TargetX, activity.TargetY) > 0,
        ActivityKind.Heal => activity.Building is { } clinic && CivicServices.PatientsAt(colonist.Colony, clinic).Any(),
        ActivityKind.FetchMaterials => TakeMaterials(colonist, activity.Building!),
        ActivityKind.SupplySite => activity.Building is { IsComplete: false } site && colonist.CarryingTo == site,
        ActivityKind.Build => activity.Building is { IsComplete: false, HasAllMaterials: true },
        ActivityKind.Sculpt => activity.Building is { Type: BuildingType.Shrine, IsComplete: true } && Offerings.CanSculpt(colonist.Colony),
        ActivityKind.CollectWorkshopOutput => activity.Building is { IsComplete: true } source && BatchProduction.CollectInto(colonist, source, world.Clock.Ticks) > 0,
        _ => true,
    };

    private static bool SetMeal(Activity activity, float value, ResourceType? dish)
    {
        activity.MealValue = value;
        activity.Meal = dish;
        return true;
    }

    /// <summary>À l'arrivée à l'atelier, on prend au stock les matières de la recette ; elles manquent peut-être déjà.</summary>
    private static bool TakeCraftInputs(Colonist colonist, Activity activity, Building workshop)
    {
        Recipe recipe = Crafting.RecipeFor(colonist.Colony, workshop.Type, activity.Product);
        // Un lot proposé pendant le trajet se revalide à l'arrivée (recette, poste, demande) : s'il n'est plus recevable, il est abandonné sans rien débiter.
        if (activity.PlannedRecipe is not null)
        {
            if (BatchProduction.Revalidate(colonist.Colony, workshop, activity, recipe) is not { } batchRecipe)
                return false;
            recipe = batchRecipe;
        }
        // La frappe engage d'abord son quota annuel ; si les matières manquent ensuite, l'engagement est rendu.
        if (workshop.Type == BuildingType.Mint && !Minting.TryCommit(colonist.Colony, activity, recipe))
            return false;
        if (!ToolChain.TryTakeInputs(colonist.Colony, recipe, out double inputHours, out Stockpile inventory))
        {
            Minting.Cancel(colonist.Colony, activity);
            return false;
        }
        if (activity.PlannedRecipe is not null && recipe.Fuel is { } fuel)
            colonist.Colony.LocalSettlement.ScaleLedger.AddFuel(fuel, recipe.Inputs.Where(i => i.Type == fuel).Sum(i => i.Amount));
        activity.InputsInventory = inventory;
        activity.CommittedRecipe = recipe;
        activity.InputsTaken = true;
        activity.InputLaborHours = inputHours;
        colonist.WorkCycleExtraHours = inputHours;
        return true;
    }

    /// <summary>La viande engagée vieillit aussi à l'atelier ; une perte interrompt la recette.</summary>
    internal static void AgeCraftInputs(Colony colony)
    {
        foreach (Colonist colonist in colony.PresentMembers.Concat(colony.Transients))
        {
            foreach (Activity? craft in new[] { colonist.Activity, colonist.PausedCraft })
            {
                if (craft is not { Kind: ActivityKind.Craft, InputsTaken: true, InputsInventory: { } inventory }
                    || inventory.Get(ResourceType.Meat) == 0) continue;
                inventory.AgeMeat();
                int lost = inventory.SpoilMeat(3, 0.5f);
                if (lost == 0) continue;
                ResourceAccounting.Record(colony.Stock, ResourceType.Meat, ResourceFlow.Loss, lost);
                if (craft == colonist.PausedCraft)
                {
                    RefundCraft(colonist, craft);
                    colonist.PausedCraft = null;
                }
                else Cancel(colonist);
            }
        }
    }

    /// <summary>Prend au stock le matériau qui manque encore au chantier (bois d'abord, puis pierre), dans la limite de ce qu'on peut porter.</summary>
    private static bool TakeMaterials(Colonist colonist, Building site)
    {
        if (site.IsComplete || colonist.Carrying is not null || site.MaterialToFetch(colonist.Colony.Stock) is not { } type)
            return false;
        // Un chantier loin du dépôt et gourmand en matériaux : une charrette libre porte quatre fois plus.
        if (!colonist.UsingCart && site.StillToBring(type) > CarryCapacity && Carts.ShouldTake(colonist.Colony, colonist))
            Carts.Take(colonist.Colony, colonist);
        int amount = Math.Min(CarryCapacity * (colonist.UsingCart ? Carts.CapacityFactor : 1), Math.Min(site.StillToBring(type), colonist.Colony.Stock.Get(type)));
        if (amount <= 0 || !colonist.Colony.Stock.TryTake(type, amount))
            return false;
        colonist.Carrying = (type, amount);
        colonist.CarryingTo = site;
        site.AddInTransit(type, amount);
        return true;
    }

    private static void Finish(Colonist colonist, WorldState world, Activity activity)
    {
        LocalMap map = colonist.Colony.Map;
        if (activity.Building is { } building)
            building.LaborTicks += world.Clock.Ticks - activity.CommittedAtTicks;
        if (activity.Skill is { } usedSkill)
        {
            ToolChain.RecordUse(colonist.Colony, usedSkill);
            // Les gestes de force et d'outil peuvent blesser.
            if (activity.Kind is ActivityKind.Chop or ActivityKind.Mine or ActivityKind.Build or ActivityKind.Dig)
                Health.MaybeInjure(world, colonist, usedSkill);
        }

        switch (activity.Kind)
        {
            case ActivityKind.Eat:
                colonist.Needs.Food += activity.MealValue;
                if (activity.MealValue > 0) colonist.LastMealTicks = world.Clock.Ticks;
                if (activity.Meal is { } dishEaten)
                    Cuisine.Savor(colonist, dishEaten, world.Clock);
                break;
            case ActivityKind.Slaughter when activity.Species is { } slaughtered && Husbandry.Slaughter(colonist.Colony, slaughtered) is var meat and > 0:
                colonist.Carrying = (ResourceType.Meat, meat);
                break;
            case ActivityKind.Relax when activity.Building is { Type: BuildingType.Tavern }:
                Cuisine.Drink(colonist, world.Clock);
                break;
            case ActivityKind.Forage:
            {
                int berries = map.HarvestBerries(activity.TargetX, activity.TargetY);
                if (berries > 0)
                    colonist.Carrying = (ResourceType.Food, berries);
                break;
            }
            case ActivityKind.ForageToEat:
                int eatenBerries = map.HarvestBerries(activity.TargetX, activity.TargetY);
                colonist.Needs.Food += BerryValue * eatenBerries;
                if (eatenBerries > 0) colonist.LastMealTicks = world.Clock.Ticks;
                break;
            case ActivityKind.SupplySite when colonist.Carrying is { } load && activity.Building is { } site:
                site.Deliver(load.Type, load.Amount);
                colonist.Carrying = null;
                Carts.Return(colonist);
                colonist.CarryingTo = null;
                break;
            case ActivityKind.Sculpt when activity.Building is { Type: BuildingType.Shrine }:
                Offerings.AddWork(colonist.Colony);
                colonist.Renown += 0.5f; // le travail d'une offrande fait honneur à qui y met la main
                break;
            case ActivityKind.Build when activity.Building is { IsComplete: false } site:
                site.Progress = MathF.Min(1f, site.Progress + BuildActionSeconds / site.WorkSeconds);
                if (site.IsComplete)
                    ColonyBrain.OnBuildingComplete(colonist.Colony, site, map, world.Clock);
                break;
            case ActivityKind.Sow when Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Fallow } plot:
                if (plot.Crop == CropKind.Grain && !ExtendedIndustry.Crisis(colonist.Colony))
                {
                    int specialtyPlots = Farming.Plots(colonist.Colony).Count(p => p.Crop != CropKind.Grain);
                    if (specialtyPlots < Farming.Plots(colonist.Colony).Count() / 10)
                    {
                        if (Knowledge.Has(colonist.Colony, Discovery.Weaving) && ExtendedIndustry.Target(colonist.Colony, ResourceType.Flax) > 0)
                            plot.Crop = CropKind.Flax;
                        else if (Knowledge.Has(colonist.Colony, Discovery.Brewing) && colonist.Colony.Map.SoilRichness >= .7f
                            && Farming.VineSuitability(colonist.Colony.Map, plot.X, plot.Y) >= Farming.MinVineSuitability
                            && colonist.Colony.Stock.TryTake(ResourceType.Wood, 1)) plot.Crop = CropKind.Grapes;
                    }
                }
                plot.PlantedTicks = world.Clock.Ticks;
                plot.Stage = CropStage.Growing;
                plot.Growth = 0f;
                plot.Trampled = 0f;
                colonist.Colony.RecordSowing(world.Clock.Ticks - activity.CommittedAtTicks);
                break;
            case ActivityKind.Harvest when Farming.PlotAt(colonist.Colony, activity.TargetX, activity.TargetY) is { Stage: CropStage.Ripe } plot:
                long cyclePlanted = plot.PlantedTicks, cycleHarvest = plot.LastHarvestTicks; // le cycle bÃ©ni se lit avant d'Ãªtre remis Ã  zÃ©ro
                plot.LastHarvestTicks = world.Clock.Ticks;
                plot.Stage = plot.Crop == CropKind.Grapes ? CropStage.Growing : CropStage.Fallow;
                plot.Growth = 0f;
                colonist.LastHarvest = ActivityKind.Harvest;
                (ResourceType, int) harvested = plot.Crop switch
                {
                    CropKind.Flax => (ResourceType.Flax, Math.Clamp((int)MathF.Round(3 * map.SoilRichness + (map.IsIrrigated(plot.X,plot.Y) ? 1 : 0)), 1, 5)),
                    CropKind.Grapes => (ResourceType.Grapes, Math.Clamp((int)MathF.Round(4 * map.SoilRichness * Farming.VineSuitability(map, plot.X, plot.Y)), 1, 6)),
                    _ => (ResourceType.Grain, Farming.YieldAt(colonist.Colony, map, activity.TargetX, activity.TargetY)),
                };
                // Les herbivores qui ont piétiné la parcelle en ont emporté une part.
                if (plot.Trampled > 0f)
                    harvested = (harvested.Item1, Math.Max(1, (int)MathF.Round(harvested.Item2 * (1f - plot.Trampled))));
                plot.Trampled = 0f;
                harvested.Item2 += DivinePowers.HarvestBonus(colonist.Colony, plot.X, plot.Y, harvested.Item2, cyclePlanted, cycleHarvest);
                AddLoad(colonist, harvested.Item1, harvested.Item2);
                break;
            case ActivityKind.Craft when activity.InputsTaken && activity.Building is { Type: BuildingType.Cask } cask:
                // Les céréales sont versées dans le fût : rien à rapporter, la bière fermente et sera tirée dans cinq jours.
                cask.BrewProduct = activity.CommittedRecipe?.Output ?? ResourceType.Beer;
                Cuisine.StartBrewing(colonist.Colony, cask, activity.InputLaborHours + LaborLedger.TicksToHours(world.Clock.Ticks - activity.CommittedAtTicks), world.Clock);
                if (activity.InputsInventory is { } brewingInputs)
                    ToolChain.ConsumeInputs(colonist.Colony, brewingInputs);
                activity.InputsTaken = false;
                activity.InputsInventory = null;
                activity.CommittedRecipe = null;
                colonist.WorkCycleStartTicks = -1;
                colonist.WorkCycleExtraHours = 0;
                break;
            case ActivityKind.Craft when activity.InputsTaken && activity.Building is { } workshop:
            {
                Recipe recipe = activity.CommittedRecipe ?? Crafting.RecipeFor(colonist.Colony, workshop.Type, activity.Product);
                bool first = colonist.Colony.AnnouncedProducts.Add(recipe.Output);
                int output = recipe.OutputAmount;
                if (workshop.Type == BuildingType.Market)
                {
                    var source = world.VisitRegion(colonist.Colony.LocalSettlement.RegionTileIndex).Deposits.FirstOrDefault(d => d.Material == recipe.Output);
                    output = source is null ? 0 : DepositExtraction.Extract(world, colonist.Colony, source, output);
                    if (source is not null && output > 0 && recipe.Output == ResourceType.Hardwood)
                    {
                        if (!map.CanChop(source.X,source.Y)) output = 0;
                        else map.ChopTree(source.X,source.Y);
                    }
                    if (source is not null && output > 0 && recipe.Output == ResourceType.Spices)
                        output = Math.Min(output, map.HarvestBerries(source.X,source.Y));
                }
                if (workshop.Type == BuildingType.Mint)
                    Minting.Complete(colonist.Colony, activity);
                else if (output > 0 && activity.PlannedRecipe is not null)
                    BatchProduction.Deposit(colonist, workshop, recipe, world.Clock.Ticks); // sortie physique de l'atelier, puis portage vers le dépôt
                else if (output > 0)
                    colonist.Carrying = (recipe.Output, output);
                if (activity.InputsInventory is { } usedInputs)
                    ToolChain.ConsumeInputs(colonist.Colony, usedInputs);
                activity.InputsTaken = false;
                activity.InputsInventory = null;
                activity.CommittedRecipe = null;
                if (first)
                    ColonyBrain.OnFirstProduct(colonist.Colony, recipe.Output, world.Clock);
                break;
            }
            case ActivityKind.Tend when Husbandry.Collect(colonist.Colony) is { } haul:
                colonist.Carrying = haul;
                break;
            case ActivityKind.Tame:
                Nature.Taming.Care(colonist.Colony);
                break;
            case ActivityKind.Hunt:
                Nature.Hunting.Resolve(world, colonist, activity);
                break;
            case ActivityKind.GreatHunt:
                Nature.Hunting.Arrived(world, colonist);
                break;
            case ActivityKind.Capture:
                Nature.Taming.ResolveCapture(world, colonist, activity);
                break;
            case ActivityKind.Gather when activity.Product is { } gathered && Nature.WildResources.Harvest(colonist.Colony, gathered, activity.TargetX, activity.TargetY) is { } picked:
                colonist.Carrying = picked;
                break;
            case ActivityKind.Heal:
                if (activity.Building is { } clinic)
                    Health.Treat(colonist.Colony, CivicServices.PatientsAt(colonist.Colony, clinic));
                break;
            case ActivityKind.Study:
                colonist.Skills.Practice(colonist.Skills.Favorite(),
                    StudySeconds * 3f * colonist.Personality.LearningFactor * LearningFactorOf(colonist.Stage) * Civic.LearningBonus(colonist.Colony, activity));
                break;
            case ActivityKind.Dig when !map.IsCanal(activity.TargetX, activity.TargetY):
                FinishDig(colonist, world, activity);
                break;
            case ActivityKind.BuildRoad when activity.SegmentId < 0:
                Bridges.CompleteCell(colonist.Colony, activity.TargetX, activity.TargetY, -activity.SegmentId);
                break;
            case ActivityKind.BuildRoad when map.Roads.SurfaceAt(activity.TargetY * map.Width + activity.TargetX) != RoadSurface.DirtRoad:
                RoadWorks.CompleteCell(colonist.Colony, activity.TargetX, activity.TargetY, activity.SegmentId);
                break;
            case ActivityKind.ClearAccess when map.GetFlora(activity.TargetX, activity.TargetY) == FloraType.Bush:
                map.ClearFlora(activity.TargetX, activity.TargetY);
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
                colonist.Carrying = (ResourceType.Fish, FoodPerFish);
                break;
            case ActivityKind.Chop when map.CanChop(activity.TargetX, activity.TargetY):
                AddLoad(colonist, ResourceType.Wood, map.ChopTree(activity.TargetX, activity.TargetY));
                colonist.LastHarvest = ActivityKind.Chop;
                break;
            case ActivityKind.Extract:
            {
                var deposit = world.VisitRegion(colonist.Colony.LocalSettlement.RegionTileIndex).Deposits.FirstOrDefault(d => d.Id == activity.DepositId);
                if (deposit is not null)
                {
                    int units = DepositExtraction.Extract(world, colonist.Colony, deposit, 2);
                    if (units > 0 && deposit.Material == ResourceType.Hardwood)
                    { if (!map.CanChop(deposit.X,deposit.Y)) units = 0; else map.ChopTree(deposit.X,deposit.Y); }
                    if (units > 0 && deposit.Material == ResourceType.Spices) units = Math.Min(units,map.HarvestBerries(deposit.X,deposit.Y));
                    if (units > 0) colonist.Carrying = (deposit.Material, units);
                }
                break;
            }
            case ActivityKind.Mine when WorkSites.CanMineFrom(map, colonist.TileX, colonist.TileY, activity.TargetX, activity.TargetY):
            {
                Material mined = map.Mine(activity.TargetX, activity.TargetY);
                int units = StonePerLayer;
                if (mined == Material.IronOre)
                {
                    var iron = world.VisitRegion(colonist.Colony.LocalSettlement.RegionTileIndex).Deposits.FirstOrDefault(d => d.Material == ResourceType.IronOre);
                    units = iron is null ? 0 : DepositExtraction.Extract(world, colonist.Colony, iron, IronOrePerLayer);
                }
                if (units > 0) colonist.Carrying = (mined == Material.IronOre ? ResourceType.IronOre : ResourceType.Stone, units);
                break;
            }
        }
        colonist.Needs.Clamp();
        EndActivity(colonist);
    }

    /// <summary>Une case de canal est creusée ; si le fossé est continu depuis la source, l'eau avance.</summary>
    private static void FinishDig(Colonist colonist, WorldState world, Activity activity)
    {
        Colony colony = colonist.Colony;
        LocalMap map = colonist.Colony.Map;
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

    /// <summary>Une fabrication abandonnée rend ses matières engagées à la colonie.</summary>
    private static void RefundCraft(Colonist colonist, Activity activity)
    {
        if (activity is not { Kind: ActivityKind.Craft, InputsTaken: true, Building: { } workshop })
            return;
        Minting.Cancel(colonist.Colony, activity);
        if (activity.InputsInventory is { } inventory)
            ToolChain.Refund(colonist.Colony, inventory);
        else
            ToolChain.Refund(colonist.Colony, Crafting.RecipeFor(colonist.Colony, workshop.Type, activity.Product));
        activity.InputsTaken = false;
        activity.InputsInventory = null;
        activity.CommittedRecipe = null;
        colonist.WorkCycleExtraHours = 0;
    }

    /// <summary>L'artisan lâche son ouvrage pour manger : ses matières restent engagées et la progression est gardée.</summary>
    private static void PauseForMeal(Colonist colonist, Activity activity, long now)
    {
        colonist.PausedCraft = activity;
        (colonist.PausedCycleStart, colonist.PausedAtTicks, colonist.PausedExtraHours) = (colonist.WorkCycleStartTicks, now, colonist.WorkCycleExtraHours);
        colonist.Activity = null;
        colonist.Path = [];
        colonist.PathIndex = 0;
        colonist.PathMaxStep = 1;
        colonist.PathStartBuilding = colonist.PathGoalBuilding = 0;
        colonist.StepElapsedTicks = 0f;
        colonist.ThinkCooldown = 0;
    }

    /// <summary>Il retourne à l'atelier achever ce qu'il avait commencé ; si l'atelier a disparu, les matières sont rendues.</summary>
    private static bool TryResumeCraft(Colonist colonist, WorldState world)
    {
        Activity paused = colonist.PausedCraft!;
        colonist.PausedCraft = null;
        if (paused.Building is { IsComplete: true } workshop && colonist.Colony.Buildings.Contains(workshop) && TryStart(colonist, world, paused))
        {
            // Le temps passé à manger ou à dormir ne compte pas dans le travail de l'ouvrage.
            colonist.WorkCycleStartTicks = colonist.PausedCycleStart < 0 ? -1 : colonist.PausedCycleStart + (world.Clock.Ticks - colonist.PausedAtTicks);
            colonist.WorkCycleExtraHours = colonist.PausedExtraHours;
            return true;
        }
        RefundCraft(colonist, paused);
        return false;
    }

    private static void EndActivity(Colonist colonist)
    {
        if (colonist.Activity is { } activity)
        {
            Release(colonist.Colony, activity);
            if (activity.HerdId != 0)
                Nature.Hunting.Release(colonist.Colony, colonist, activity);
            // Une fabrication interrompue rend les matières à la colonie.
            RefundCraft(colonist, activity);
        }
        colonist.Activity = null;
        colonist.Path = [];
        colonist.PathIndex = 0;
        colonist.PathMaxStep = 1;
        colonist.PathStartBuilding = colonist.PathGoalBuilding = 0;
        colonist.StepElapsedTicks = 0f;
        colonist.ThinkCooldown = 0;
    }

    private static float Ticks(float seconds) => seconds * TimeConstants.TicksPerSecond;
}
