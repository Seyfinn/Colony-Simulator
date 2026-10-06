namespace GodColony.Simulation.Colonies;

public enum SettlementKind { Village = 0, Camp = 1, Hamlet = 2 }
public enum SettlementStatus { Active = 0, Evacuating = 1, Closed = 2 }

/// <summary>Autorité des biens, du terrain exploité et du travail d'un établissement physique.</summary>
public sealed class Settlement
{
    internal Settlement(Colony owner, int campX, int campY, List<(int X, int Y)> gatherSpots)
    {
        Owner = owner; CampX = campX; CampY = campY; GatherSpots = gatherSpots;
    }
    public int Id { get; internal set; }
    public Colony Owner { get; private set; }

    /// <summary>Un schisme transfère l'établissement entier à une colonie sœur : ses stocks, ouvrages, champs et habitants le suivent ; seuls le propriétaire et les caches liés changent.</summary>
    internal void AdoptBy(Colony newOwner)
    {
        Owner = newOwner;
        _population = null;
        ServiceCache = null;
        if (Pathfinder is not null) Pathfinder.Owner = newOwner;
    }
    public int RegionTileIndex { get; internal set; } = -1;
    public SettlementKind Kind { get; internal set; }
    public SettlementStatus Status { get; internal set; }
    /// <summary>Les observations de croissance de l'établissement (voir <see cref="GrowthPolicy"/>).</summary>
    public SettlementGrowthState GrowthState { get; } = new();

    /// <summary>Les mesures quotidiennes de l'établissement : utilisation des ateliers, production et verdicts d'extension (voir <see cref="SettlementScaleLedger"/>).</summary>
    public SettlementScaleLedger ScaleLedger { get; } = new();

    /// <summary>Les ravitaillements non urgents différés pour grouper leur charge (voir <see cref="SupplyBatching"/>).</summary>
    public List<SupplyWaitState> SupplyWaits { get; } = [];

    /// <summary>Les flux de déplacements récents (fenêtre de 14 jours, 256 couples au plus) qui nourrissent les raccourcis (voir <see cref="RoadShortcuts"/>).</summary>
    public List<TravelFlow> TravelFlows { get; } = [];
    internal int StableDays { get; set; }
    internal int UnproductiveDays { get; set; }

    /// <summary>Travail de sondage accumulé par les habitants autour de leur établissement (voir <see cref="Prospection"/>).</summary>
    internal float SurveyWork { get; set; }

    /// <summary>Jours consécutifs où la nourriture disponible n'atteint pas une journée de repas (voir <see cref="LogisticsPlanner"/>).</summary>
    internal int ShortageDays { get; set; }

    /// <summary>Dernière livraison de ravitaillement partie vers cet établissement (une livraison de survie ignore ce délai).</summary>
    internal long LastSupplyTicks { get; set; } = long.MinValue / 2;

    /// <summary>L'établissement actif le plus proche en aval sur le même fleuve (de la même colonie ou d'une autre) : ce que retient un barrage ici lui manque.</summary>
    public Settlement? Downstream { get; internal set; }
    private HouseholdEquipment? _equipment;
    public HouseholdEquipment Equipment => _equipment ??= new();
    public string Name => Owner.PrimarySettlementId == Id ? Owner.Name
        : $"{(Kind == SettlementKind.Camp ? "Camp" : Kind == SettlementKind.Hamlet ? "Hameau" : "Village")} {Id} de {Owner.Name}";
    internal SettlementLayout? LayoutState;
    public SettlementLayout Layout
    {
        get { using var scope = Owner.UseSettlement(this); return LayoutState ??= SettlementPlanner.InitializeLayout(Owner, Map); }
    }
    [NonSerialized] internal GodColony.Simulation.Map.LocalSpatialIndex? SpatialCache;
    [NonSerialized] internal ServiceCache? ServiceCache;
    internal Dictionary<SearchKind, int> ExhaustedDaysLeft { get; } = [];
    internal long _sowTicks;
    internal int _plotsSown;
    public IEnumerable<Colonist> Residents => Owner.Citizens.Where(c => c.HomeSettlementId == Id);
    [NonSerialized] private PresentPopulation? _population;
    public PresentPopulation Population => _population ??= new PresentPopulation(Owner, this);
    public IEnumerable<Colonist> PresentColonists => Population;
    /// <summary>Lecture locale des anciens adaptateurs ; fermer ce contexte avant d'avancer le monde.</summary>
    public IDisposable Observe() => Owner.UseSettlement(this);
    public GodColony.Simulation.Map.LocalMap Map { get; internal set; } = null!;
    public GodColony.Simulation.Pathfinding.Pathfinder Pathfinder { get; internal set; } = null!;
    public int CampX { get; internal set; }
    public int CampY { get; internal set; }
    public IReadOnlyList<(int X, int Y)> GatherSpots { get; internal set; } = [];
    public (int X, int Y)? Quarry { get; internal set; }
    public Stockpile Stock { get; } = new();
    public LaborLedger Labor { get; } = new();
    public List<Death> Deaths { get; } = [];
    public List<Building> Buildings { get; } = [];
    public List<Canal> Canals { get; } = [];
    internal HashSet<(int X, int Y)> CanalTiles { get; } = [];
    internal HashSet<(int X, int Y)> UnreachableStands { get; } = [];
    internal long LastQuarryMoveTicks { get; set; } = long.MinValue / 2;
    public bool IronSeen { get; internal set; }
    internal float ToolWear { get; set; }
    public List<Field> Fields { get; } = [];
    public int Chickens { get; internal set; }
    public int Sheep { get; internal set; }
    public int Cows { get; internal set; }
    /// <summary>Bêtes de trait de l'enclos : elles ne donnent ni œufs ni viande, mais tirent les caravanes (voir <see cref="Husbandry.DraftSpecies"/>).</summary>
    public int Horses { get; internal set; }
    public int Oxen { get; internal set; }
    internal float HorseGrowth { get; set; }
    /// <summary>Les hardes sauvages visibles autour de l'établissement (leurs bêtes sont empruntées à la population de la région).</summary>
    /// <summary>Les chantiers de pont de l'établissement (voir <see cref="Bridges"/>).</summary>
    public List<BridgeSite> BridgeSites => _bridgeSites ??= [];
    private List<BridgeSite>? _bridgeSites;
    public List<Nature.WildHerd> Herds => _herds ??= [];
    private List<Nature.WildHerd>? _herds;
    /// <summary>Les bêtes capturées qu'on apprivoise à l'enclos.</summary>
    public List<Nature.TamingAnimal> Taming => _taming ??= [];
    private List<Nature.TamingAnimal>? _taming;
    /// <summary>Les lignées d'élevage de la colonie : ses bêtes nées en enclos deviennent plus dociles et plus productives.</summary>
    public Dictionary<ResourceType, Nature.LivestockLine> Lines => _lines ??= [];
    private Dictionary<ResourceType, Nature.LivestockLine>? _lines;
    /// <summary>L'enclos est renforcé : les prédateurs y entrent bien moins souvent.</summary>
    public bool PenReinforced { get; internal set; }
    internal int NextHerdId { get; set; }
    /// <summary>Les bêtes soignées aujourd'hui (au moins un colon est venu apprivoiser).</summary>
    internal bool TamedToday { get; set; }
    /// <summary>La nature sauvage de la région de l'établissement (la même instance que celle de <see cref="RegionState.Wildlife"/>), posée par la simulation chaque jour.</summary>
    internal Nature.RegionWildlife? Wildlife { get; set; }
    /// <summary>Dernière grande chasse (réussie ou non) : on ne repart pas aussitôt contre l'alpha.</summary>
    internal long LastGreatHuntTicks { get; set; } = long.MinValue / 2;
    /// <summary>La grande chasse en cours contre un alpha, ou null.</summary>
    internal Nature.GreatHuntState? GreatHunt { get; set; }
    public float EggsReady { get; internal set; }
    public float WoolReady { get; internal set; }
    public float MilkReady { get; internal set; }
    internal float ChickenGrowth { get; set; }
    internal float SheepGrowth { get; set; }
    internal float CowGrowth { get; set; }
    public Dictionary<ResourceType, int> SlaughterOrders { get; } = [];
    internal long LastMeatThoughtDay { get; set; } = -100;
    internal float ClothesWear { get; set; }
    internal float SaltUse { get; set; }
    internal float SpiceUse { get; set; }
    public int IllnessCases { get; internal set; }
    public int ColdSnapDaysLeft { get; internal set; }
    public int DroughtDaysLeft { get; internal set; }
    internal long LastHealthThoughtDay { get; set; } = -100;
    internal long LastSpoilageThoughtDay { get; set; } = -100;
    public List<Colonist> Transients { get; } = [];
    internal long LastSocialThoughtTicks { get; set; } = long.MinValue / 2;
    internal long LastRefusalDay { get; set; } = -100;
    public ColonySensors? Sensors { get; internal set; }
    public bool FireLit { get; internal set; } = true;
    internal Dictionary<string, ColonyBrain.NarrationTopic> NarrationState { get; } = [];
    internal HashSet<(int X, int Y)> Reserved { get; } = [];
    public Dictionary<WorkSector, float> WorkShares { get; } = new()
    {
        [WorkSector.Food] = 0.5f,
        [WorkSector.Farm] = 0f,
        [WorkSector.Wood] = 0.3f,
        [WorkSector.Stone] = 0.2f,
        [WorkSector.Construction] = 0f,
        [WorkSector.Craft] = 0f,
        [WorkSector.Free] = 0f,
    };
}
