using GodColony.Simulation.Pathfinding;

namespace GodColony.Simulation.Colonies;

/// <summary>Les étapes d'une recherche d'emplacement : candidats (balayage et contraintes), chemins d'accès des finalistes, puis un résultat.</summary>
public enum PlanStage { Candidates, Paths, Ready, Failed }

/// <summary>
/// Une recherche d'emplacement en cours : son état persistant (étape, curseurs de balayage, candidats, finalistes, frontière de l'A* inachevé). Une recherche
/// inachevée ne modifie jamais le monde ; son résultat est une <see cref="PlacementProposal"/> ou une impossibilité. Tout ce qui sert à décider
/// est sauvegardé : recommencer au chargement retarderait le résultat et changerait la partie.
/// </summary>
public sealed class PlanningJob
{
    internal PlanningJob(int id, Colony owner, PlanRequest request, long createdTicks, int layoutRevision, int terrainRevision)
    {
        Id = id;
        Owner = owner;
        SettlementId = owner.LocalSettlement.Id;
        RequestKey = request.Key;
        Kind = request.Kind;
        Type = request.Type;
        Priority = request.Priority;
        Urgent = request.Urgent;
        HintX = request.HintX;
        HintY = request.HintY;
        CreatedTicks = createdTicks;
        StartLayoutRevision = layoutRevision;
        StartTerrainRevision = terrainRevision;
    }

    public int Id { get; }
    public Colony Owner { get; internal set; }
    public int SettlementId { get; internal set; }
    public string RequestKey { get; }
    public DevelopmentKind Kind { get; }
    public BuildingType? Type { get; }
    public DevelopmentPriority Priority { get; }
    public bool Urgent { get; }
    public int HintX { get; }
    public int HintY { get; }
    public long CreatedTicks { get; }
    public int StartLayoutRevision { get; }
    public int StartTerrainRevision { get; }

    public PlanStage Stage { get; internal set; } = PlanStage.Candidates;

    // --- Les fronts : où chercher (un quartier existant, ou une nouvelle ancre) ---

    internal bool FrontsReady { get; set; }

    /// <summary>Pour chaque front : le quartier existant (-1 pour une nouvelle ancre), sa vocation, son centre et le quartier dont il serait issu.</summary>
    internal List<int> FrontDistrict { get; } = [];
    internal List<int> FrontKind { get; } = [];
    internal List<int> FrontCx { get; } = [];
    internal List<int> FrontCy { get; } = [];
    internal List<int> FrontParent { get; } = [];

    /// <summary>1 pour une ancre de nouveau noyau (une simple graine, scrutée de près), 0 pour la fenêtre d'un quartier ou d'un dépôt.</summary>
    internal List<int> FrontAnchor { get; } = [];

    /// <summary>Passe d'exploration (0 : la fenêtre locale ; puis 20, 35, 50, 70 cases) et curseur de balayage en anneaux.</summary>
    internal int Pass { get; set; }
    internal int ScanFront { get; set; }
    internal int ScanRing { get; set; }
    internal int ScanStep { get; set; }
    internal int ScannedCells { get; set; }

    /// <summary>
    /// Pour un logement urgent, les ancres d'un nouveau noyau ne se scrutent qu'après la fenêtre locale des quartiers existants : le choix viable le plus rapide prime sur l'objectif
    /// d'éviter une masse unique, et la densification passe avant l'extension.
    /// </summary>
    internal bool AnchorPhase { get; set; }
    internal bool AnchorPhaseDone { get; set; }

    /// <summary>Le balayage est allé au bout de toutes ses passes.</summary>
    internal bool ScanExhausted { get; set; }

    /// <summary>Tours de balayage déjà faits (un lot de finalistes tous refusés en rouvre un).</summary>
    internal int Round { get; set; }

    // --- Les candidats préfiltrés (24 au plus) ---

    internal List<int> CandCell { get; } = [];
    internal List<int> CandFront { get; } = [];
    internal List<int> CandDoor { get; } = [];
    internal List<float> CandScore { get; } = [];
    internal List<float> CandParts { get; } = [];
    internal List<float> CandServiceSeconds { get; } = [];

    // --- Les finalistes et l'A* en cours ---

    internal List<int> Finalists { get; } = [];
    internal int PathCursor { get; set; }
    internal PathSearchState? Search { get; set; }
    internal int SearchRestarts { get; set; }
    internal List<PlacementProposal> Completed { get; } = [];

    // --- Le résultat ---

    public PlacementProposal? Result { get; internal set; }
    public PlacementFailureKind? Failure { get; internal set; }
    public string FailureDetail { get; internal set; } = "";

    /// <summary>Les refus physiques rencontrés (pour dire pourquoi on n'a rien trouvé) : terrain, place, accès, trajet.</summary>
    internal int RejectedTerrain { get; set; }
    internal int RejectedSpace { get; set; }
    internal int RejectedAccess { get; set; }
    internal int RejectedTravel { get; set; }
    internal int RejectedDistrict { get; set; }

    public bool IsFinished => Stage is PlanStage.Ready or PlanStage.Failed;
}
