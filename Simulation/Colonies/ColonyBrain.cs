using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Ce que la colonie mesure d'elle-même à un instant donné. Le chauffage ne compte que le bois à brûler ;
/// le bois total inclut aussi celui qu'attendent les chantiers.
/// </summary>
/// <param name="FoodDays">Jours de repas en réserve (nourriture sauvage, grain et pain).</param>
/// <param name="HasConstructionSite">Une hutte ou un atelier est en chantier.</param>
/// <param name="StonePressure">Manque de pierre, réserve et chantiers de fours compris.</param>
/// <param name="FarmShare">Part des bras que réclament les semailles et la moisson.</param>
/// <param name="Chain">Ce que réclame la chaîne du fer (outils, fer, charbon, minerai).</param>
/// <param name="HasWorkshopSite">Un chantier qui n'est pas une hutte (atelier, barrage, moulin…).</param>
/// <param name="WorkshopsReady">Un atelier de la chaîne du fer est achevé.</param>
/// <param name="OrePressure">Urgence de trouver du minerai pour la chaîne du fer.</param>
/// <param name="CanalWork">Un canal d'irrigation est en cours de creusement.</param>
/// <param name="Bread">Ce que réclame la chaîne du blé (pain, farine, grain en surplus).</param>
/// <param name="FoodWorkshopsReady">Le moulin ou le four est achevé.</param>
/// <param name="Prospecting">On cherche encore du fer en creusant la roche, dans la limite d'un budget.</param>
public sealed record ColonySensors(
    float FoodDays, float FoodPressure,
    float HeatingPressure,
    float WoodTarget, float WoodPressure,
    int Homeless, float HousingPressure, bool HasConstructionSite,
    float StonePressure,
    float FarmShare,
    ChainDemand Chain,
    bool HasWorkshopSite,
    bool WorkshopsReady,
    float OrePressure,
    bool CanalWork,
    BreadDemand Bread,
    bool FoodWorkshopsReady,
    bool Prospecting)
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
///    survie (nourriture, chauffage), puis logement, puis réserves de pierre et recherche du fer, puis prospérité
///    (ateliers du fer et du blé, canaux, demandes au joueur comme le barrage) ;
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
    internal const int StoneReserveTarget = 60;

    // Part maximale de la main-d'œuvre que chaque secteur peut mobiliser.
    private const float MaxFoodShare = 0.9f;
    private const float MaxWoodShare = 0.5f;
    private const float MaxStoneShare = 0.4f;
    private const float MaxConstructionShare = 0.3f;
    private const float MaxFarmShare = 0.6f;
    private const float MaxCraftShare = 0.25f;

    /// <summary>Tant qu'on n'a pas trouvé de fer, on veut bien creuser jusqu'à cette quantité de pierre pour en chercher.</summary>
    private const int ProspectBudget = 180;
    private const float ProspectShare = 0.15f;
    private const float BreadCraftShare = 0.12f;
    /// <summary>Grain en surplus qui dort au grenier : on met deux fois plus de bras au moulin et au four.</summary>
    private const int LargeGrainSurplus = 60;
    /// <summary>Part des bras qu'on envoie fouiller la roche pour y trouver du minerai (plus modeste que la carrière).</summary>
    private const float MaxOreShare = 0.25f;

    /// <summary>On ouvre au plus ce nombre de champs d'un coup (la colonie réfléchit chaque heure).</summary>
    private const int MaxFieldsPerThought = 2;

    /// <summary>Nombre de chantiers ouverts en même temps : un seul, deux si beaucoup de colons dorment dehors.</summary>
    private static int MaxConstructionSites(int homeless) => homeless > 8 ? 2 : 1;

    /// <summary>Les parts évoluent progressivement d'une heure à l'autre, pour éviter les revirements brutaux.</summary>
    private const float ShareSmoothing = 0.5f;

    private const int MaxThoughts = 30;

    /// <summary>Part de rancune qui s'estompe chaque jour : un barrage est pardonné en un an de jeu environ (20 jours).</summary>
    private const float GrudgeFadePerDay = 0.05f;

    /// <summary>Part de foi qui revient chaque jour vers le niveau naturel du colon.</summary>
    private const float FaithRecoveryPerDay = 0.02f;

    /// <summary>Bois brûlé par colon et par nuit pour se chauffer, selon la saison.</summary>
    public static float FirewoodPerColonist(Season season) => season switch
    {
        Season.Printemps => 0.1f,
        Season.Ete => 0.05f,
        Season.Automne => 0.2f,
        _ => 0.4f,
    };

    /// <summary>Bois brûlé par colon et par nuit dans cette colonie : plus en pays froid, davantage encore pendant une vague de froid.</summary>
    public static float FirewoodPerColonist(Colony colony, Season season) => FirewoodPerColonist(season) * Climate.WoodFactor(colony, season);

    /// <summary>
    /// Le bois qu'on garde pour se chauffer : une réserve de base plus trois nuits, et en automne tout l'hiver.
    /// Ce qui dépasse peut servir à autre chose (charbon de bois).
    /// </summary>
    public static float HeatingTarget(Colony colony, Season season)
    {
        int population = Math.Max(1, colony.PresentMembers.Count);
        float target = WoodBaseReserve + 3 * population * FirewoodPerColonist(colony, season);
        if (season == Season.Automne)
            target += TimeConstants.DaysPerSeason * population * FirewoodPerColonist(colony, Season.Hiver);
        return target;
    }

    public static bool IsColdSeason(Season season) => season is Season.Automne or Season.Hiver;

    public static void Think(Colony colony, LocalMap map, GameClock clock)
    {
        SettlementPlanner.Sync(colony);
        RelocateQuarryIfExhausted(colony, map, clock);
        Cuisine.TickCasks(colony, clock);
        if (!colony.IronSeen && WorkSites.OreVisibleNearQuarry(map, colony))
        {
            colony.IronSeen = true;
            Say(colony, clock, "Les mineurs ont repéré un filon de fer à la carrière.");
        }
        PlanFields(colony, map, clock);
        ColonySensors sensors = Sense(colony, clock);
        if (PlanConstruction(colony, map, sensors, clock) || PlanSpareRoom(colony, map, sensors, clock)
            || PlanWorkshop(colony, map, sensors, clock)
            || PlanCanal(colony, map, sensors, clock)
            || PlanCivic(colony, map, sensors, clock))
            sensors = Sense(colony, clock);
        AskForDam(colony, map, sensors, clock);
        colony.Sensors = sensors;

        Dictionary<WorkSector, float> target = DecideShares(sensors);
        Civic.ClaimIdleHands(colony, target);
        if (sensors.SurvivalAssured && sensors.HousingPressure <= ComfortHousingLimit)
        {
            if (ExtendedIndustry.PickJob(colony) is not null || Offerings.PickSculptJob(colony) is not null) target[WorkSector.Craft] = Math.Max(target[WorkSector.Craft], .1f);
            bool extraction = colony.DepositReports.Any(k => k.Region == colony.LocalSettlement.RegionTileIndex && k.State != DepositObservation.Depleted
                && (ExtendedIndustry.Target(colony, k.Material) > colony.Stock.Get(k.Material) || colony.LocalSettlement.Kind == SettlementKind.Camp && colony.Stock.Get(k.Material) < 20));
            if (extraction) target[WorkSector.Stone] = Math.Max(target[WorkSector.Stone], .15f);
            float sum = target.Values.Sum();
            if (sum > 1) foreach (WorkSector sector in WorkSectors.All) target[sector] /= sum;
        }
        foreach (WorkSector sector in WorkSectors.All)
            colony.WorkShares[sector] += (target[sector] - colony.WorkShares[sector]) * ShareSmoothing;
        colony.AssignSectors();

        Narrate(colony, sensors, clock);
    }

    /// <summary>Jours à attendre avant de chercher une nouvelle carrière, pour ne pas en changer sans cesse.</summary>
    private const int QuarryMoveCooldownDays = 8;

    /// <summary>La carrière n'offre plus assez de roche : la colonie ouvre un nouveau front de taille, ailleurs.</summary>
    private static void RelocateQuarryIfExhausted(Colony colony, LocalMap map, GameClock clock)
    {
        if (colony.Quarry is not { } quarry || clock.Hour != 12
            || clock.Ticks - colony.LastQuarryMoveTicks < QuarryMoveCooldownDays * TimeConstants.TicksPerDay)
            return;

        // Le fer manque, la pierre déborde et aucun filon n'est à portée de la carrière : les mineurs
        // resteraient les bras croisés. On ouvre un front de taille là où le minerai se voit.
        bool wantOre = colony.Sensors?.Chain is { Active: true, OreMissing: > 0 };
        if (wantOre && !WorkSites.OreWithinReach(map, colony))
        {
            colony.LastQuarryMoveTicks = clock.Ticks;
            if (colony.IsExhausted(SearchKind.OreQuarry))
                return;
            if (WorkSites.FindOreQuarry(map, colony.CampX, colony.CampY) is { } oreQuarry && oreQuarry != quarry)
            {
                colony.Quarry = oreQuarry;
                colony.ForgetQuarrySearches();
                Say(colony, clock, "Aucun filon près de la carrière : nous ouvrons un front de taille vers le minerai de fer.");
            }
            else
                colony.MarkExhausted(SearchKind.OreQuarry); // aucun filon visible nulle part : on revérifiera dans un an
            return;
        }

        if (WorkSites.RocksLeft(map, quarry) >= WorkSites.ExhaustedQuarryRocks)
            return;
        colony.LastQuarryMoveTicks = clock.Ticks;
        if (WorkSites.FindQuarry(map, colony.CampX, colony.CampY) is not { } next || next == quarry
            || WorkSites.RocksLeft(map, next) <= WorkSites.RocksLeft(map, quarry))
            return;
        colony.Quarry = next;
        colony.ForgetQuarrySearches();
        Say(colony, clock, "La carrière est presque épuisée : nous ouvrons un nouveau front de taille.");
    }

    /// <summary>
    /// Au printemps, la colonie défriche assez de champs pour nourrir une bonne part de ses habitants.
    /// Plus la nourriture sauvage lui coûte d'heures de travail, plus elle cultive.
    /// </summary>
    private static void PlanFields(Colony colony, LocalMap map, GameClock clock)
    {
        if (!Farming.IsSowingSeason(clock.Season))
            return;

        // Des greniers qui débordent : inutile d'agrandir les champs.
        float foodDays = colony.Stock.FoodUnits / (Math.Max(1, colony.PresentMembers.Count) * MealsPerColonistPerDay);
        if (foodDays >= 2 * FoodTargetDays)
            return;

        int target = Farming.TargetPlots(colony);
        if (colony.Fields.Count * Field.Size * Field.Size >= target)
            return;

        // Un champ est une demande persistante : le site se cherche par petits lots hors de la pensée, et la proposition prête s'admet ici (deux champs au plus par pensée).
        PlanRequest request = SettlementPlanner.RequestFor(colony, DevelopmentKind.Field, null, DevelopmentPriority.Production);
        int opened = 0;
        while (opened < MaxFieldsPerThought && colony.Fields.Count * Field.Size * Field.Size < target
               && SettlementPlanner.Poll(colony, request) == PlanningOutcome.Ready && SettlementPlanner.TryCommit(colony, request).Success)
            opened++;
        if (opened == 0)
        {
            AskForDepot(colony, request, clock);
            return;
        }

        int plots = colony.Fields.Count * Field.Size * Field.Size;
        string fields = opened == 1 ? "un champ" : $"{opened} champs";
        Say(colony, clock, colony.Labor.HoursPerUnit(ResourceType.Food) is { } wildCost
            ? $"Baies et poisson nous coûtent {wildCost:0.0} h par unité : nous ouvrons {fields} ({plots} parcelles en tout)."
            : $"Pour assurer la nourriture de l'année, nous ouvrons {fields} ({plots} parcelles en tout).");
    }

    /// <summary>
    /// Les champs ne trouvent de place qu'à plus de six secondes aller-retour du dépôt : un entrepôt de proximité les rapprochera (jamais de dépôt circulaire : ses matériaux
    /// viennent d'un point déjà opérationnel). Une seule demande à la fois.
    /// </summary>
    private static void AskForDepot(Colony colony, PlanRequest fields, GameClock clock)
    {
        if (fields is not { State: PlanningOutcome.WaitingForChange, Failure: PlacementFailureKind.TravelBudgetExceeded }
            || !Knowledge.Allows(colony, BuildingType.Storehouse) || colony.Buildings.Any(b => b.Type == BuildingType.Storehouse && !b.IsComplete)
            || !SensorsAllowComfort(colony))
            return;
        District? farm = colony.Layout.Districts.LastOrDefault(d => d.Kind == DistrictKind.Agricultural);
        (int hx, int hy) = farm is null ? (colony.CampX, colony.CampY) : DistrictPlanner.CenterOf(colony, farm);
        PlanRequest depot = SettlementPlanner.RequestFor(colony, DevelopmentKind.Logistics, BuildingType.Storehouse, DevelopmentPriority.Production, hintX: hx, hintY: hy);
        if (SettlementPlanner.Poll(colony, depot) == PlanningOutcome.Ready && SettlementPlanner.TryCommit(colony, depot).Success)
            Say(colony, clock, "Nos champs sont trop loin du dépôt : nous bâtissons un entrepôt de proximité.");
    }

    private static bool SensorsAllowComfort(Colony colony) => colony.Sensors is { SurvivalAssured: true } s && s.FoodDays >= SettlementRules.ComfortFoodDays;

    /// <summary>
    /// Une amitié ou une rivalité vient de naître : la colonie le remarque (mais pas plus d'une fois par demi-journée,
    /// pour ne pas noyer le reste de ses pensées).
    /// </summary>
    internal static void OnConversation(Colony colony, Colonist a, Colonist b, Relations.Outcome outcome, GameClock clock) =>
        OnRelationChange(colony, a, b, outcome.Change, clock);

    internal static void OnRelationChange(Colony colony, Colonist a, Colonist b, Relations.Change change, GameClock clock)
    {
        if (change == Relations.Change.None || clock.Ticks - colony.LastSocialThoughtTicks < SocialThoughtIntervalTicks)
            return;
        colony.LastSocialThoughtTicks = clock.Ticks;
        Say(colony, clock, change == Relations.Change.BecameFriends
            ? $"{a.Name} et {b.Name} sont devenus amis."
            : $"{a.Name} et {b.Name} ne peuvent plus se voir : une rivalité est née.");
    }

    private static readonly long SocialThoughtIntervalTicks = (long)(12 * TimeConstants.TicksPerHour);

    /// <summary>Le gel de l'hiver détruit ce qui n'a pas été moissonné.</summary>
    public static void OnDayStart(Colony colony, GameClock clock)
    {
        colony.UnreachableStands.Clear();
        if (clock.DayOfSeason == 1)
            SettlementPlanner.Notify(colony, RetryEvents.Season);
        colony.AgeExhaustedSearches();
        foreach (Colony other in colony.Grudges.Keys.ToList())
        {
            colony.Grudges[other] = Math.Max(0f, colony.Grudges[other] - GrudgeFadePerDay);
            if (colony.Grudges[other] == 0f)
                colony.Grudges.Remove(other);
        }
        Husbandry.Daily(colony, clock);
        Civic.Daily(colony, clock);
        Specialties.Daily(colony);
        Milestones.Daily(colony, clock);
        foreach (Colonist colonist in colony.PresentMembers)
        {
            // La foi revient doucement vers le tempérament du colon.
            float baseline = Needs.NeutralFaith + 0.2f * colonist.Personality[Axis.Piete];
            colonist.Needs.Faith += Math.Clamp(baseline - colonist.Needs.Faith, -FaithRecoveryPerDay, FaithRecoveryPerDay);
        }
        Relations.FadeDaily(colony);
        foreach ((Colonist a, Colonist c, Relations.Change change) in Relations.Cohabit(colony))
            OnRelationChange(colony, a, c, change, clock);
        int lost = Farming.DailyUpdate(colony, clock);
        if (lost > 0)
            Say(colony, clock, $"Le gel a détruit {lost} parcelles de céréales qui n'avaient pas été moissonnées !");
    }

    /// <summary>
    /// Étage 2 de la pyramide : une fois la survie assurée, si des colons dorment dehors,
    /// la colonie ouvre un chantier de hutte. Renvoie true si un chantier a été ouvert.
    /// </summary>
    private static bool PlanConstruction(Colony colony, LocalMap map, ColonySensors sensors, GameClock clock)
    {
        if (!sensors.SurvivalAssured || sensors.Homeless == 0)
            return false;
        int sites = colony.ConstructionSites.Count(b => b.IsHut);
        // Les places des huttes en chantier comptent déjà comme des toits à venir.
        int stillUnplanned = sensors.Homeless - sites * Building.HutCapacity;
        if (stillUnplanned <= 0 || sites >= MaxConstructionSites(sensors.Homeless))
            return false;
        PlanRequest request = SettlementPlanner.RequestFor(colony, DevelopmentKind.Housing, BuildingType.Hut, DevelopmentPriority.Housing,
            urgent: sensors.Homeless >= Building.HutCapacity);
        if (SettlementPlanner.Poll(colony, request) != PlanningOutcome.Ready || !SettlementPlanner.TryCommit(colony, request).Success)
            return false;
        Say(colony, clock, colony.Buildings.Count(b => b.IsHut) == 1
            ? $"{sensors.Homeless} colons dorment à la belle étoile : nous décidons de bâtir notre première hutte."
            : $"Encore {sensors.Homeless} {(sensors.Homeless > 1 ? "colons sans toit" : "colon sans toit")} : nous ouvrons le chantier d'une nouvelle hutte.");
        return true;
    }

    /// <summary>Lits libres qu'une colonie prospère veut toujours avoir d'avance : elle bâtit avant d'être à l'étroit.</summary>
    private const int SpareBedsWanted = 3;

    /// <summary>
    /// Une colonie prospère anticipe sa croissance : tant que tout le monde est logé et qu'il reste moins de trois lits libres
    /// (les huttes en chantier comptent), elle ouvre le chantier d'une hutte de plus, au lieu d'attendre que des colons dorment dehors.
    /// Sans cela, la population plafonne à la capacité des huttes.
    /// </summary>
    private static bool PlanSpareRoom(Colony colony, LocalMap map, ColonySensors sensors, GameClock clock)
    {
        if (!sensors.SurvivalAssured || sensors.Homeless > 0 || colony.ConstructionSites.Any() || colony.AverageMood < 0.5f
            || sensors.FoodDays < Migration.MinFoodDaysToWelcome)
            return false;
        int beds = colony.Buildings.Count(b => b.IsHut) * Building.HutCapacity;
        if (beds - colony.PresentMembers.Count >= SpareBedsWanted)
            return false;

        PlanRequest request = SettlementPlanner.RequestFor(colony, DevelopmentKind.Housing, BuildingType.Hut, DevelopmentPriority.Comfort);
        if (SettlementPlanner.Poll(colony, request) != PlanningOutcome.Ready || !SettlementPlanner.TryCommit(colony, request).Success)
            return false;
        Say(colony, clock, "La colonie grandit : nous préparons une hutte de plus avant d'être à l'étroit.");
        return true;
    }

    /// <summary>
    /// Étage 4 de la pyramide : tout le monde est à l'abri ; si la colonie a vu du fer et manque d'outils,
    /// elle bâtit l'atelier suivant de la chaîne (un seul chantier à la fois).
    /// </summary>
    private static bool PlanWorkshop(Colony colony, LocalMap map, ColonySensors sensors, GameClock clock)
    {
        if (!sensors.SurvivalAssured || sensors.HousingPressure > ComfortHousingLimit || colony.ConstructionSites.Any())
            return false;
        if (Crafting.NextWorkshopToBuild(colony, map) is not { } type)
            return false;
        PlanRequest request = SettlementPlanner.RequestFor(colony, DevelopmentKind.Workshop, type, DevelopmentPriority.Production);
        if (SettlementPlanner.Poll(colony, request) != PlanningOutcome.Ready || !SettlementPlanner.TryCommit(colony, request).Success)
            return false;
        Say(colony, clock, type switch
        {
            BuildingType.Kiln => "Nous avons trouvé du fer, mais pas d'outils pour le travailler : il nous faut d'abord du charbon de bois. Nous bâtissons une charbonnière.",
            BuildingType.Bloomery => "Le charbon de bois est là : nous bâtissons un bas fourneau pour tirer le fer du minerai.",
            BuildingType.Forge => "Nous aurons du fer : nous bâtissons une forge pour en faire des outils.",
            BuildingType.Mill => "Nos greniers débordent de grain : nous bâtissons un moulin sur la rivière pour le moudre.",
            BuildingType.MineDepot => "Nous aménageons une grande mine : chevalement, dépôts et aire de tri pour exploiter les métaux, le charbon et les pierres précieuses.",
            _ => "Nous avons de la farine : nous bâtissons un four pour en faire du pain.",
        });
        return true;
    }

    private const float ComfortHousingLimit = 25f;

    /// <summary>
    /// Une fois la survie et le logement assurés, la colonie bâtit ce qui rend la vie meilleure : enclos, puits, métier à tisser,
    /// entrepôt, infirmerie, marché, taverne, école (un seul chantier à la fois, voir <see cref="Civic.NextToBuild"/>).
    /// </summary>
    private static bool PlanCivic(Colony colony, LocalMap map, ColonySensors sensors, GameClock clock)
    {
        if (!sensors.SurvivalAssured || sensors.HousingPressure > ComfortHousingLimit || sensors.WoodPressure > 70f || colony.ConstructionSites.Any())
            return false;
        // Les ateliers du fer et du blé passent d'abord : on n'occupe pas l'unique chantier avec un confort.
        if (Crafting.NextWorkshopToBuild(colony, map) is not null)
            return false;
        BuildingType? next = Civic.NextToBuild(colony);
        if ((next == BuildingType.Pen || next is null && Husbandry.WantsAnotherPen(colony)) && Civic.Has(colony, BuildingType.Pen)
            && SettlementPlanner.PlanExtension(colony, BuildingType.Pen) is not null)
        {
            Say(colony, clock, "L'enclos déborde : nous ajoutons une parcelle clôturée pour accueillir les jeunes bêtes.");
            return true;
        }
        if (next is null)
        {
            if (!SettlementPlanner.WantsMarketExtension(colony) || SettlementPlanner.PlanExtension(colony, BuildingType.Market) is null)
                return false;
            Say(colony, clock, "Le commerce grandit : nous ajoutons des étals au marché pour préparer davantage de marchandises par voyage.");
            return true;
        }
        BuildingType type = next.Value;
        PlanRequest request = SettlementPlanner.RequestFor(colony, Urbanism.KindOf(type), type,
            type == BuildingType.Storehouse ? DevelopmentPriority.Production : DevelopmentPriority.Comfort);
        if (SettlementPlanner.Poll(colony, request) != PlanningOutcome.Ready || !SettlementPlanner.TryCommit(colony, request).Success)
            return false;
        Say(colony, clock, type == BuildingType.Pen && colony.Buildings.Any(b => b.Type == BuildingType.Pen)
            ? "Nos enclos sont pleins de bêtes qu'on ne peut loger : nous en bâtissons un de plus."
            : Civic.Announcement(type));
        return true;
    }

    /// <summary>Jours sans reposer la question d'un barrage refusé : c'est un gros ouvrage, on n'insiste pas.</summary>
    private const int DamRefusalCooldownDays = 20;

    /// <summary>
    /// Quand des champs manquent d'eau, la colonie envisage un barrage. Ce n'est pas elle qui décide : elle adresse
    /// une prière au joueur (une seule à la fois) et, si elle est exaucée, ouvre le chantier.
    /// </summary>
    private static void AskForDam(Colony colony, LocalMap map, ColonySensors sensors, GameClock clock)
    {
        if (!sensors.SurvivalAssured || sensors.HousingPressure > ComfortHousingLimit || clock.Season == Season.Hiver
            || colony.Fields.Count == 0 || colony.ConstructionSites.Any() || colony.Buildings.Any(b => b.IsDam)
            || colony.Prayers.IsQuiet(DecisionKind.Dam, clock) || !Knowledge.Allows(colony, BuildingType.Dam))
            return;
        if (!colony.Fields.Any(f => Irrigation.IrrigatedShare(map, f) < 0.5f))
            return;
        if (Hydrology.FindSite(map, colony) is not { } site)
            return;

        int x = site.X, y = site.Y;
        int tiles = site.Reservoir.Tiles.Count;
        Building ouvrage = Hydrology.DamAt(map, x, y);
        colony.Prayers.Ask(DecisionKind.Dam, $"{x},{y}",
            "Construire un barrage sur la rivière ?",
            $"Nos champs manquent d'eau. Un barrage en ({x}, {y}) formerait en amont un lac de {tiles} cases : des poissons, des berges fertiles et de l'eau à portée de nos champs. " +
            $"Ce grand ouvrage demande {ouvrage.WoodRequired} bois, {ouvrage.StoneRequired} pierres et {ouvrage.WorkSeconds:0} secondes de travail. La rivière coulerait moins fort en aval.",
            () => ApplyDamDecision(colony, map, clock, x, y),
            clock, DamRefusalCooldownDays);
    }

    internal static void ApplyDamDecision(Colony colony, LocalMap map, GameClock clock, int x, int y)
    {
        if (Hydrology.FindReservoir(map, colony, x, y) is null) return;
        Urbanism.PlanBuilding(map, colony, BuildingType.Dam, x, y);
        Say(colony, clock, "Nous bâtissons un barrage sur la rivière.");
    }

    /// <summary>
    /// Étage 4 aussi : une fois tout le monde logé, la colonie creuse un canal vers un champ trop sec
    /// quand une rivière (ou un canal en eau) est plus haute que lui. Pas en hiver : le sol est gelé.
    /// </summary>
    private static bool PlanCanal(Colony colony, LocalMap map, ColonySensors sensors, GameClock clock)
    {
        if (!sensors.SurvivalAssured || sensors.HousingPressure > ComfortHousingLimit || clock.Season == Season.Hiver
            || colony.Fields.Count == 0 || colony.CanalsInProgress.Any() || !Knowledge.Has(colony, Discovery.Irrigation))
            return false;
        if (Irrigation.PlanBest(map, colony) is not { } canal)
            return false;

        colony.Canals.Add(canal);
        foreach ((int x, int y) in canal.Tiles)
            colony.CanalTiles.Add((x, y));
        Say(colony, clock, $"Nos champs manquent d'eau et une rivière coule plus haut : nous creusons un canal de {canal.Tiles.Count} cases.");
        return true;
    }

    /// <summary>Le dernier tronçon est creusé : l'eau arrive au champ.</summary>
    internal static void OnCanalComplete(Colony colony, Canal canal, LocalMap map, GameClock clock)
    {
        int irrigated = canal.Target.Plots.Count(p => map.IsIrrigated(p.X, p.Y));
        Say(colony, clock, irrigated > 0
            ? $"Le canal est achevé : l'eau arrive au champ, {irrigated} parcelles sont irriguées."
            : "Le canal est achevé, mais l'eau n'atteint pas les parcelles.");
    }

    /// <summary>Premier charbon, premier fer, premier outil : la colonie le remarque.</summary>
    internal static void OnFirstProduct(Colony colony, ResourceType product, GameClock clock) =>
        Say(colony, clock, product switch
        {
            ResourceType.Charcoal => "Notre premier charbon de bois sort de la charbonnière : de quoi chauffer la forge.",
            ResourceType.Iron => "Un premier lingot de fer sort du bas fourneau !",
            ResourceType.Flour => "Le moulin tourne : notre première farine est moulue.",
            ResourceType.Bread => "Notre premier pain sort du four : il nourrit bien mieux que le grain cru.",
            ResourceType.Clothes => "Notre premier vêtement de laine est tissé : l'hiver sera moins rude.",
            ResourceType.Cake => "Un premier gâteau sort du four : une fête pour toute la colonie !",
            ResourceType.Beer => "La taverne brasse sa première bière : de quoi garder le moral longtemps.",
            ResourceType.Coins => "L'atelier de frappe sort ses premières pièces : l'or devient monnaie, dans la limite du quota annuel.",
            ResourceType.Stew => "Un premier ragoût mijote : de quoi réchauffer le corps et le moral, et donner du cœur à l'ouvrage.",
            ResourceType.Salt or ResourceType.Spices or ResourceType.Hardwood =>
                $"Le marché troque sa première denrée de la région : {Specialties.Name(product)}.",
            _ => "Notre premier outil de fer est forgé : le travail ira plus vite.",
        });

    /// <summary>Un bâtiment vient d'être achevé : des colons s'installent dans une hutte, un atelier se met au travail.</summary>
    public static void OnBuildingComplete(Colony colony, Building building, LocalMap map, GameClock clock)
    {
        SettlementPlanner.OnObjectCompleted(colony, building);
        if (building.IsExtension)
        {
            Say(colony, clock, building.Type == BuildingType.Pen
                ? $"L'enclos est agrandi : il peut accueillir {Husbandry.Capacity(colony)} poules ou moutons par espèce et {Husbandry.CapacityOf(colony, ResourceType.Cows)} vaches."
                : "Le marché est agrandi : ses nouveaux étals permettent de préparer de plus grandes caravanes.");
            return;
        }
        if (building.IsDam)
        {
            Reservoir? lake = Hydrology.CompleteDam(map, colony, building);
            Say(colony, clock, lake is null
                ? "Le barrage est achevé, mais l'eau ne monte pas : le terrain a changé."
                : $"Le barrage est achevé : un lac de {lake.Tiles.Count} cases se forme en amont.");
            return;
        }
        if (building.Type == BuildingType.MineDepot)
        {
            Say(colony, clock, "La mine est achevée : ses installations permettent d'exploiter les métaux, le charbon minéral et les pierres précieuses.");
            return;
        }
        if (building.IsWorkshop || building.IsCivic)
        {
            string name = Building.NameOf(building.Type);
            Say(colony, clock, building.Type == BuildingType.Bloomery
                ? "Le bas fourneau est achevé : on peut y fondre le minerai."
                : $"{char.ToUpper(Building.Definite(building.Type)[0])}{Building.Definite(building.Type)[1..]} est {(Building.IsFeminine(building.Type) ? "achevée" : "achevé")} : on peut s'y mettre.");
            if (building.Type == BuildingType.Pen)
                Husbandry.OnPenBuilt(colony, clock);
            return;
        }
        colony.Labor.RecordHut(LaborLedger.TicksToHours(building.LaborTicks));
        colony.FillVacancies();
        int homeless = colony.Homeless;
        Say(colony, clock, homeless > 0
            ? $"Une hutte est achevée : {(building.Residents.Count == 1 ? "1 colon y dort" : $"{building.Residents.Count} colons y dorment")} désormais à l'abri ({homeless} encore dehors)."
            : "Une hutte est achevée : tout le monde dort désormais à l'abri !");
    }

    /// <summary>Allume le feu à la tombée de la nuit, s'il reste assez de bois.</summary>
    public static void LightFire(Colony colony, GameClock clock)
    {
        int needed = (int)MathF.Ceiling(colony.PresentMembers.Count * FirewoodPerColonist(colony, clock.Season));
        // Le bois dur brûle longtemps : une bûche en vaut trois.
        int hardwood = Math.Min(colony.Stock.Get(ResourceType.Hardwood), (needed + Specialties.WoodPerHardwood - 1) / Specialties.WoodPerHardwood);
        colony.Stock.TryTake(ResourceType.Hardwood, hardwood);
        needed = Math.Max(0, needed - hardwood * Specialties.WoodPerHardwood);
        colony.FireLit = colony.Stock.TryTake(ResourceType.Wood, needed);
        if (!colony.FireLit && IsColdSeason(clock.Season))
            Say(colony, clock, "Le feu s'est éteint faute de bois : la nuit sera froide et le sommeil mauvais.");
    }

    public static ColonySensors Sense(Colony colony, GameClock clock)
    {
        int population = Math.Max(1, colony.PresentMembers.Count);

        float dailyMeals = population * MealsPerColonistPerDay;
        float foodDays = (float)(colony.Stock.AvailableNutrition / (population * (decimal)Trade.TravelerNutritionPerDay));
        float foodTarget = clock.Season == Season.Automne ? AutumnFoodTargetDays : FoodTargetDays;
        float foodPressure = Pressure(foodTarget - foodDays, foodTarget - FoodCrisisDays);

        // Chauffage : trois nuits d'avance, plus tout l'hiver si l'on est en automne (anticipation).
        int wood = colony.Stock.Get(ResourceType.Wood);
        float heatingTarget = HeatingTarget(colony, clock.Season);
        float heatingPressure = Pressure(heatingTarget - wood, heatingTarget);

        // Bois total : le chauffage, ce que les chantiers attendent encore, et celui que la charbonnière va brûler.
        // Sans la métallurgie, le fer ne sert à rien : on ne brûle pas de charbon et l'on ne cherche pas le minerai pour lui.
        ChainDemand chain = Knowledge.Has(colony, Discovery.Metallurgy) ? ToolChain.Demand(colony)
            : ChainDemand.None with { ToolsWanted = ToolChain.ToolsWanted(colony) };
        float woodTarget = heatingTarget + colony.ConstructionSites.Sum(b => b.WoodStillToBring) + chain.WoodForCharcoal;
        float woodPressure = Pressure(woodTarget - wood, woodTarget);

        int homeless = colony.Homeless;
        float housingPressure = homeless * 100f / population;
        bool hasSite = colony.ConstructionSites.Any();

        // Pierre : la réserve, plus ce que les chantiers de fours attendent encore.
        float stoneTarget = StoneReserveTarget + colony.ConstructionSites.Sum(b => b.StoneStillToBring);
        float stonePressure = Pressure(stoneTarget - colony.Stock.Get(ResourceType.Stone), stoneTarget);
        float orePressure = chain.Active ? Math.Min(1f, chain.OreMissing / 6f) * 100f : 0f;

        // Les champs réclament des bras au moment des semailles et de la moisson.
        float farmShare = Math.Min(MaxFarmShare, Farming.WorkersNeeded(colony, clock) / (float)population);

        return new ColonySensors(foodDays, foodPressure, heatingPressure, woodTarget, woodPressure,
            homeless, housingPressure, hasSite, stonePressure, farmShare,
            chain, colony.ConstructionSites.Any(b => !b.IsHut), colony.Buildings.Any(b => b.IsComplete && b.Type is BuildingType.Kiln or BuildingType.Bloomery or BuildingType.Forge), orePressure,
            colony.CanalsInProgress.Any(), FoodChain.Demand(colony), colony.Buildings.Any(b => b.IsComplete && FoodChain.IsFoodWorkshop(b.Type)),
            !ToolChain.IronDiscovered(colony) && colony.Labor.TotalProduced(ResourceType.Stone) < ProspectBudget);
    }

    /// <summary>
    /// La pyramide : la survie (nourriture, chauffage) d'abord ; le logement une fois la survie assurée ;
    /// les réserves de pierre quand presque tout le monde est logé ; le temps libre pour le reste.
    /// </summary>
    public static Dictionary<WorkSector, float> DecideShares(ColonySensors sensors)
    {
        float food = sensors.FoodPressure / 100f * MaxFoodShare;
        float farm = sensors.FarmShare;
        float wood = sensors.WoodPressure / 100f * MaxWoodShare;
        // Un chantier d'atelier ou de canal mobilise des bras même quand tout le monde est déjà logé.
        float construction = sensors.SurvivalAssured && (sensors.HasConstructionSite || sensors.CanalWork)
            ? Math.Max(sensors.HasWorkshopSite || sensors.CanalWork || sensors.Homeless == 0 ? 0.5f : 0.1f, sensors.HousingPressure / 100f) * MaxConstructionShare
            : 0f;
        bool comfortAssured = sensors.SurvivalAssured && sensors.HousingPressure <= ComfortHousingLimit;
        float stone = comfortAssured ? sensors.StonePressure / 100f * MaxStoneShare : 0f;

        // Pas encore de fer en vue : un ou deux mineurs creusent la roche à sa recherche (dans la limite d'un budget).
        if (comfortAssured && sensors.Prospecting)
            stone = Math.Max(stone, ProspectShare);

        // La chaîne du fer : on fouille la roche pour le minerai manquant, et l'on travaille dans les ateliers.
        if (comfortAssured && sensors.Chain.Active)
            stone = Math.Max(stone, sensors.OrePressure / 100f * MaxOreShare);
        float ironCraft = comfortAssured && sensors.Chain.Active && sensors.WorkshopsReady
            ? Math.Max(0.1f, MaxCraftShare * sensors.Chain.ToolShortfall / Math.Max(1, sensors.Chain.ToolsWanted))
            : 0f;
        // Le moulin et le four : un ou deux colons y travaillent tant qu'il y a du grain en surplus ou de la farine à cuire.
        float breadCraft = comfortAssured && sensors.Bread.Active && sensors.FoodWorkshopsReady
            ? BreadCraftShare * (sensors.Bread.GrainSurplus >= LargeGrainSurplus ? 2f : 1f)
            : 0f;
        float craft = Math.Min(MaxCraftShare, ironCraft + breadCraft);

        // Survie menacée : pas de temps libre, tous les bras disponibles vont aux besoins vitaux.
        float vital = food + wood;
        if (!sensors.SurvivalAssured && vital > 0f)
        {
            float spare = 1f - (food + farm + wood + construction + stone + craft);
            if (spare > 0f)
            {
                food += spare * food / vital;
                wood += spare * wood / vital;
            }
        }

        float total = food + farm + wood + construction + stone + craft;
        if (total > 1f)
        {
            food /= total;
            farm /= total;
            wood /= total;
            construction /= total;
            stone /= total;
            craft /= total;
            total = 1f;
        }
        return new Dictionary<WorkSector, float>
        {
            [WorkSector.Food] = food,
            [WorkSector.Farm] = farm,
            [WorkSector.Wood] = wood,
            [WorkSector.Stone] = stone,
            [WorkSector.Construction] = construction,
            [WorkSector.Craft] = craft,
            [WorkSector.Free] = 1f - total,
        };
    }

    /// <summary>Convertit un manque en pression : 0 quand rien ne manque, 100 quand le manque atteint <paramref name="full"/>.</summary>
    private static float Pressure(float shortfall, float full) => Math.Clamp(shortfall / full, 0f, 1f) * 100f;

    // --- Les pensées de la colonie ---

    private static void Narrate(Colony colony, ColonySensors sensors, GameClock clock)
    {
        int Workers(WorkSector sector) => colony.Workers.Count(m => m.Sector == sector);
        string People(WorkSector sector) => Workers(sector) == 1 ? "1 colon" : $"{Workers(sector)} colons";

        string foodBand = sensors.FoodDays < 2 ? "crise"
            : sensors.FoodDays < 3.5f ? "basse"
            : sensors.FoodDays > 4.5f ? "ok"
            : Announced(colony, "nourriture");
        if (Changed(colony, "nourriture", foodBand))
        {
            Say(colony, clock, foodBand switch
            {
                "crise" => $"Il ne reste que {sensors.FoodDays:0.#} jours de nourriture ! {People(WorkSector.Food)} à la cueillette.",
                "basse" => $"Les réserves de nourriture baissent ({sensors.FoodDays:0} jours) : {People(WorkSector.Food)} à la cueillette.",
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
                    $"L'hiver approche et le bois manque : {People(WorkSector.Wood)} en forêt pour le bois de chauffage.",
                "manque" => $"Nous manquons de bois : {People(WorkSector.Wood)} en forêt.",
                "besoin-automne" =>
                    $"L'hiver approche : nous faisons des réserves de bois ({colony.Stock.Get(ResourceType.Wood)} sur {sensors.WoodTarget:0}).",
                "besoin" => $"Il nous faudra un peu plus de bois : {People(WorkSector.Wood)} en forêt.",
                _ => "Nous avons assez de bois pour nous chauffer.",
            });
        }

        // Zones neutres : entre deux situations, on garde le dernier avis plutôt que d'en changer sans cesse.
        int miners = Workers(WorkSector.Stone);
        string stoneBand = miners >= 2 ? "carrière" : miners == 0 ? "arrêt" : Announced(colony, "pierre");
        if (Changed(colony, "pierre", stoneBand))
        {
            Say(colony, clock, stoneBand == "carrière"
                ? $"Nos besoins vitaux sont couverts : {People(WorkSector.Stone)} à la carrière."
                : "La carrière attendra : les besoins vitaux passent d'abord.");
        }

        // Les couples et les enfants : en temps de crise, on renonce à agrandir la famille.
        if (colony.PresentMembers.Any(m => m.Sex == Sex.Female && m.Partner is not null))
        {
            float prosperity = Lifecycle.Prosperity(colony, clock);
            string birthBand = prosperity < 0.25f ? "freinée" : prosperity > 0.5f ? "normale" : Announced(colony, "natalité");
            if (birthBand.Length == 0)
                birthBand = "normale";
            if (Changed(colony, "natalité", birthBand))
            {
                Say(colony, clock, birthBand == "freinée"
                    ? "Les temps sont durs : les couples renoncent à avoir des enfants pour l'instant."
                    : "Les couples songent à agrandir leur famille.");
            }
        }

        // Les champs : semailles, pousse, moisson, repos hivernal.
        if (colony.Fields.Count > 0)
        {
            var plots = Farming.Plots(colony).ToList();
            int fallow = plots.Count(p => p.Stage == CropStage.Fallow);
            int ripe = plots.Count(p => p.Stage == CropStage.Ripe);
            string fieldBand = clock.Season == Season.Hiver ? "repos"
                : ripe > 0 ? "moisson"
                : fallow > 0 && Farming.IsSowingSeason(clock.Season) ? "semis"
                : plots.Any(p => p.Stage == CropStage.Growing) ? "pousse"
                : "repos";
            if (Changed(colony, "champs", fieldBand))
            {
                Say(colony, clock, fieldBand switch
                {
                    "semis" => $"C'est le temps des semailles : {fallow} parcelles à ensemencer.",
                    "pousse" => "Les semailles sont faites : les céréales poussent.",
                    "moisson" => $"Les céréales sont mûres : c'est la moisson ({ripe} parcelles prêtes).",
                    _ => "Les champs se reposent.",
                });
            }
        }

        // L'équipement : les outils font gagner du temps, mais s'usent.
        float coverage = ToolChain.Coverage(colony);
        string toolBand = coverage >= 0.9f ? "équipés"
            : coverage > 0f ? "quelques"
            : colony.Labor.TotalProduced(ResourceType.Tools) > 0 ? "usés"
            : "";
        if (toolBand.Length > 0 && Changed(colony, "outils", toolBand))
        {
            Say(colony, clock, toolBand switch
            {
                "équipés" => $"Nous sommes bien équipés : {colony.Stock.Get(ResourceType.Tools)} outils de fer pour {colony.Workers.Count()} travailleurs.",
                "quelques" => $"Nous avons {colony.Stock.Get(ResourceType.Tools)} outils de fer, mais pas pour tout le monde.",
                _ => "Tous nos outils sont usés : il faut en forger de nouveaux.",
            });
        }

        // Bilan de saison : ce que coûte chaque ressource en heures de travail.
        if (Changed(colony, "saison", clock.Season.ToString()) && colony.Labor.HoursPerUnit(ResourceType.Food) is not null)
            Say(colony, clock, "Bilan de saison, en heures de travail par unité : " + CostSummary(colony.Labor) + ".");

        int free = Workers(WorkSector.Free);
        float freeRatio = free / (float)Math.Max(1, colony.Workers.Count());
        string freeBand = free == 0 ? "aucun"
            : freeRatio < 0.4f ? "quelques"
            : freeRatio > 0.6f ? "beaucoup"
            : Announced(colony, "temps libre");
        if (Changed(colony, "temps libre", freeBand) && free > 0)
            Say(colony, clock, free == 1
                ? "Tout va bien : 1 colon profite de son temps libre."
                : $"Tout va bien : {free} colons profitent de leur temps libre.");
    }

    public static string CostSummary(LaborLedger labor)
    {
        var parts = new List<string>();
        void Add(ResourceType type, string name)
        {
            if (labor.HoursPerUnit(type) is { } hours)
                parts.Add($"{name} {hours:0.0} h");
        }
        Add(ResourceType.Food, "nourriture sauvage");
        Add(ResourceType.Grain, "céréales");
        Add(ResourceType.Wood, "bois");
        Add(ResourceType.Stone, "pierre");
        Add(ResourceType.IronOre, "minerai de fer");
        Add(ResourceType.Charcoal, "charbon de bois");
        Add(ResourceType.Iron, "fer");
        Add(ResourceType.Tools, "outil");
        Add(ResourceType.Flour, "farine");
        Add(ResourceType.Bread, "pain");
        Add(ResourceType.Eggs, "œufs");
        Add(ResourceType.Meat, "viande");
        Add(ResourceType.Wool, "laine");
        Add(ResourceType.Clothes, "vêtement");
        Add(ResourceType.Salt, "sel");
        Add(ResourceType.Spices, "épices");
        Add(ResourceType.Hardwood, "bois dur");
        if (labor.HoursPerHut is { } hut)
            parts.Add($"hutte {hut:0} h");
        if (labor.HoursPerCanalTile is { } canal)
            parts.Add($"canal {canal:0.0} h par case");
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

    internal static void Say(Colony colony, GameClock clock, string text)
    {
        colony.Thoughts.Add(new Thought(clock.Ticks, text));
        if (colony.Thoughts.Count > MaxThoughts)
            colony.Thoughts.RemoveAt(0);
    }
}
