namespace GodColony.Simulation.Colonies;

public enum Sex { Female, Male }

/// <summary>Les âges de la vie : enfant (ne travaille pas), adolescent (apprenti, mi-temps), adulte, ancien.</summary>
public enum LifeStage { Child, Teen, Adult, Elder }

/// <summary>Un colon est « de passage » quand il marche vers la colonie pour la rejoindre, ou en sort pour toujours.</summary>
public enum TransitState { None, Arriving, Leaving }

public sealed class Colonist
{
    public Colonist(int id, string name, Sex sex, Colony colony, Skills skills, float x, float y)
    {
        Id = id;
        Name = name;
        Sex = sex;
        Colony = colony;
        HomeSettlementId = colony.LocalSettlement.Id;
        LocationSettlementId = colony.PrimarySettlementId;
        Skills = skills;
        X = PrevX = x;
        Y = PrevY = y;
    }

    public Skills Skills { get; }
    public int HomeSettlementId { get; internal set; }
    public int LocationSettlementId { get; internal set; }
    public int TravelId { get; internal set; }
    public bool HasShoes { get; internal set; }
    public float ShoeDistance { get; internal set; }
    public ColonistLocation Location => new(TravelId == 0 ? ColonistLocationKind.Settlement : ColonistLocationKind.Travel,
        TravelId == 0 ? LocationSettlementId : TravelId, X, Y);

    // --- Âge et famille ---

    public const float TeenAge = 3f;
    public const float AdultAge = 6f;
    public const float ElderAge = 16f;

    public string Surname { get; internal set; } = "";
    public string FullName => Surname.Length == 0 ? Name : $"{Name} {Surname}";

    /// <summary>Le moment de la naissance, en ticks (négatif pour ceux qui sont nés avant la fondation).</summary>
    public long BirthTicks { get; init; }

    /// <summary>Âge en années de jeu (une année = 20 jours).</summary>
    public float AgeYears => (Colony.Clock.Ticks - BirthTicks) / (float)Time.TimeConstants.TicksPerYear;

    public Species Species { get; init; } = Species.Human;

    /// <summary>
    /// L'âge qu'aurait un humain au même moment de sa vie : un nain de 12 ans a la maturité d'un humain de 6.
    /// C'est lui qui décide de l'enfance, de la vieillesse, de la fécondité.
    /// </summary>
    public float EquivalentAge => AgeYears / Species.LifespanScale;

    public LifeStage Stage => EquivalentAge switch
    {
        < TeenAge => LifeStage.Child,
        < AdultAge => LifeStage.Teen,
        < ElderAge => LifeStage.Adult,
        _ => LifeStage.Elder,
    };

    public Colonist? Mother { get; init; }
    public Colonist? Father { get; init; }

    /// <summary>Le conjoint (un adulte de l'autre sexe) ; null tant qu'on est seul.</summary>
    public Colonist? Partner { get; internal set; }

    public List<Colonist> Children { get; } = [];

    /// <summary>Jusqu'à quand elle porte son enfant (null si elle n'est pas enceinte), et de qui.</summary>
    public long? PregnantUntilTicks { get; internal set; }
    public Colonist? PregnancyFather { get; internal set; }

    /// <summary>Dernière naissance de cette mère : un couple attend un an entre deux enfants.</summary>
    public long LastBirthTicks { get; internal set; } = long.MinValue / 2;

    /// <summary>Heures de suite sans rien manger du tout ; au bout de deux jours, c'est la mort.</summary>
    internal int StarvedHours { get; set; }
    /// <summary>Dernière prise de nourriture effective ; distingue un réveil affamé d'un jeûne continu.</summary>
    public long LastMealTicks { get; internal set; } = -1;
    internal bool StarvationWarned { get; set; }

    /// <summary>Deux colons sont de la même famille proche : parent et enfant, ou frère et sœur.</summary>
    public static bool AreKin(Colonist a, Colonist b) =>
        a.Mother == b || a.Father == b || b.Mother == a || b.Father == a
        || (a.Mother is not null && a.Mother == b.Mother) || (a.Father is not null && a.Father == b.Father);

    public Personality Personality { get; init; } = Personality.Neutral;

    /// <summary>Affinité avec chaque autre colon (par identifiant), de -100 à +100.</summary>
    internal Dictionary<int, float> Affinities { get; } = [];
    internal HashSet<int> Friends { get; } = [];
    internal HashSet<int> Rivals { get; } = [];

    public IEnumerable<Colonist> FriendsIn(Colony colony) => colony.PresentMembers.Where(m => Friends.Contains(m.Id));
    public IEnumerable<Colonist> RivalsIn(Colony colony) => colony.PresentMembers.Where(m => Rivals.Contains(m.Id));

    /// <summary>Temps à attendre avant de chercher à nouveau de la compagnie, après une tentative ratée.</summary>
    internal int ChatCooldownTicks { get; set; }

    /// <summary>Le secteur auquel la colonie l'a affecté. Il y travaille en priorité, sans y être limité.</summary>
    public WorkSector Sector { get; internal set; }

    /// <summary>
    /// L'atelier (bâtiment principal) et le produit que ce colon pratique de préférence, depuis quand, et depuis quand ce travail lui est impossible (0 : jamais).
    /// Une préférence guide le choix à l'intérieur du secteur ; elle cède devant les besoins, la survie et les urgences (voir <see cref="SpecialistAssignments"/>).
    /// </summary>
    public int PreferredWorkshopId { get; internal set; }
    public ResourceType? PreferredProduct { get; internal set; }
    public long SpecialtyChosenTicks { get; internal set; }
    public long SpecialtyBlockedSinceTicks { get; internal set; }

    public int Id { get; }
    public string Name { get; private set; }
    public Sex Sex { get; }
    public Colony Colony { get; internal set; }

    public const int MaxNameLength = 40;

    /// <summary>Change l'identité affichée, en conservant le colon et tous ses liens familiaux et sociaux.</summary>
    public bool TryRename(string? name, string? surname)
    {
        string firstName = name?.Trim() ?? "";
        string familyName = surname?.Trim() ?? "";
        if (firstName.Length == 0 || firstName.Length > MaxNameLength || familyName.Length > MaxNameLength
            || firstName.Any(char.IsControl) || familyName.Any(char.IsControl))
            return false;
        Name = firstName;
        Surname = familyName;
        return true;
    }

    /// <summary>Position en cases (le centre d'une case est à +0,5).</summary>
    public float X { get; internal set; }
    public float Y { get; internal set; }

    /// <summary>Position au tick précédent, pour que l'affichage puisse lisser le mouvement.</summary>
    public float PrevX { get; internal set; }
    public float PrevY { get; internal set; }

    public int TileX => (int)X;
    public int TileY => (int)Y;

    /// <summary>Un marchand de retour de voyage : il marche du bord de la carte jusqu'au camp, sans que la colonie « accueille un voyageur ».</summary>
    internal bool ReturningTrader { get; set; }

    /// <summary>Tant qu'il n'est pas arrivé (ou une fois parti), il ne compte pas parmi les membres de la colonie.</summary>
    public TransitState Transit { get; internal set; }

    /// <summary>Heures de suite passées dans la déprime ; trop longtemps, et il quitte la colonie.</summary>
    internal int UnhappyHours { get; set; }

    public Needs Needs { get; } = new();

    /// <summary>Le mal dont souffre le colon (maladie ou blessure) ; il se repose jusqu'à la guérison.</summary>
    public Ailment Ailment { get; internal set; }

    /// <summary>Heures de convalescence restantes.</summary>
    internal int AilmentHours { get; set; }

    /// <summary>Un guérisseur est passé le soigner : il ne risque plus d'en mourir.</summary>
    internal bool Treated { get; set; }

    /// <summary>Jusqu'à quand un ragoût le rend plus vif au travail (voir <see cref="Cuisine.StewBoost"/>).</summary>
    internal long BoostUntilTicks { get; set; }

    public bool IsBoosted => Colony.Clock.Ticks < BoostUntilTicks;
    public Activity? Activity { get; internal set; }
    public bool IsSleeping => Activity is { Kind: ActivityKind.Sleep, Started: true };

    /// <summary>Ce que le colon transporte (null s'il a les mains vides).</summary>
    public (ResourceType Type, int Amount)? Carrying { get; internal set; }

    /// <summary>Le renom du colon : grandes chasses, victoires, ouvrages d'offrande, maîtrise d'un métier. Il pèse dans l'élection du chef (voir <see cref="Leadership"/>).</summary>
    public float Renown { get; internal set; }

    /// <summary>Le danger de mort propre à la blessure en cours (null : celui d'un accident de travail).</summary>
    internal float? AilmentDeathChance { get; set; }

    /// <summary>Le colon pousse une charrette de la colonie pour rapporter sa charge (voir <see cref="TransportView"/>).</summary>
    public bool UsingCart { get; internal set; }

    /// <summary>Le dernier geste de récolte, et la charge d'une prise : avec une charrette, le colon enchaîne les mêmes récoltes jusqu'à quatre charges avant de rentrer.</summary>
    internal ActivityKind? LastHarvest { get; set; }

    /// <summary>Une fabrication longue interrompue pour un repas : ses matières restent engagées, elle reprend là où elle s'est arrêtée (voir <see cref="ColonistAI"/>).</summary>
    internal Activity? PausedCraft { get; set; }

    /// <summary>Le cycle de travail (début et heures annexes) au moment de la pause, et le moment de la pause : la reprise décale le début du temps passé à manger ou à dormir.</summary>
    internal long PausedCycleStart { get; set; } = -1;
    internal long PausedAtTicks { get; set; }
    internal double PausedExtraHours { get; set; }
    internal int CartUnit { get; set; }

    /// <summary>Le chantier auquel est destiné ce qu'il transporte ; null s'il le rapporte au stock.</summary>
    public Building? CarryingTo { get; internal set; }

    /// <summary>La hutte où il dort, s'il en a une.</summary>
    public Building? Home { get; internal set; }

    public bool IsSleepingAtHome => IsSleeping && Home is { } home && home.Contains(TileX, TileY);

    /// <summary>Distance parcourue, utilisée pour animer la marche.</summary>
    public float DistanceWalked { get; internal set; }

    internal List<(int X, int Y)> Path { get; set; } = [];
    internal int PathIndex { get; set; }

    /// <summary>Hauteur de marche autorisée sur le chemin en cours (2 seulement pour escalader hors d'un trou).</summary>
    internal int PathMaxStep { get; set; } = 1;

    /// <summary>
    /// Le bâtiment qu'on quitte et celui où l'on entre (0 : aucun) : leurs emprises sont les seules que le chemin en cours traverse. La case de départ du chemin et la révision
    /// de l'occupation à son calcul permettent de revérifier le reste du trajet quand un bâtiment apparaît.
    /// </summary>
    internal int PathStartBuilding { get; set; }
    internal int PathGoalBuilding { get; set; }
    /// <summary>Quand le chemin en cours a été confié au colon : la durée d'un trajet terminé se mesure de là à l'arrivée (voir <see cref="RoadShortcuts"/>).</summary>
    internal long PathCommittedTicks { get; set; }
    internal int PathStartX { get; set; }
    internal int PathStartY { get; set; }
    internal int PathRevision { get; set; }

    /// <summary>Temps passé sur le pas en cours (en ticks) et d'où l'on est parti : le pas dure autant que le coût de son arête, le déplacement s'y interpole.</summary>
    internal float StepElapsedTicks { get; set; }
    internal float StepFromX { get; set; }
    internal float StepFromY { get; set; }
    internal int ThinkCooldown { get; set; }

    /// <summary>Moment où il est parti récolter ce qu'il rapporte, pour mesurer le coût en travail (-1 sinon).</summary>
    internal long WorkCycleStartTicks { get; set; } = -1;

    /// <summary>Heures de travail des matières premières d'une fabrication, à ajouter au coût de ce qu'il rapporte.</summary>
    internal double WorkCycleExtraHours { get; set; }
}
