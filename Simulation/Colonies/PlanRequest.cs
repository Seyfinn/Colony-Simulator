namespace GodColony.Simulation.Colonies;

/// <summary>
/// Un besoin persistant, identifié par sa fonction (« une hutte », « un champ », « un moulin »), jamais par une coordonnée : il décrit
/// <b>ce qui manque</b>. Il n'est pas recréé à chaque heure ; il se réveille quand un événement utile survient (voir <see cref="RetryEvents"/>)
/// et garde son motif d'attente quand le terrain ne permet rien pour l'instant.
/// </summary>
public sealed class PlanRequest
{
    internal PlanRequest(string key, DevelopmentKind kind, BuildingType? type, long createdTicks)
    {
        Key = key;
        Kind = kind;
        Type = type;
        CreatedTicks = createdTicks;
    }

    /// <summary>L'identité de la fonction demandée (« hut », « field », « building:Mill »…).</summary>
    public string Key { get; }

    public DevelopmentKind Kind { get; }
    public BuildingType? Type { get; }
    public long CreatedTicks { get; }

    public DevelopmentPriority Priority { get; internal set; } = DevelopmentPriority.Comfort;

    /// <summary>Un logement urgent assouplit les préférences (jamais l'eau, les collisions, l'entrée utilisable) et prend le premier site sûr.</summary>
    public bool Urgent { get; internal set; }

    /// <summary>Un point que le site doit approcher (l'entrepôt près d'un champ trop loin du dépôt, le fût près de sa taverne) ; -1 s'il n'y en a pas.</summary>
    public int HintX { get; internal set; } = -1;
    public int HintY { get; internal set; } = -1;

    public PlanningOutcome State { get; internal set; } = PlanningOutcome.Idle;

    /// <summary>La recherche en cours dans la file du monde (-1 s'il n'y en a pas).</summary>
    public int JobId { get; internal set; } = -1;

    /// <summary>Pour une impossibilité connue : pourquoi, et quels événements autorisent un nouvel essai.</summary>
    public PlacementFailureKind? Failure { get; internal set; }
    public RetryEvents RetryOn { get; internal set; }
    public long LastSearchTicks { get; internal set; } = long.MinValue / 2;

    /// <summary>Événements survenus depuis la dernière recherche (voir <see cref="SettlementPlanner.Notify"/>).</summary>
    public RetryEvents Seen { get; internal set; }

    /// <summary>La proposition prête à être acceptée, avec la révision spatiale qu'elle a évaluée (null sinon).</summary>
    public PlacementProposal? Proposal { get; internal set; }
}
