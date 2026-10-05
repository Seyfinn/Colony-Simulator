namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'état de coordination d'un développement accepté : sa parcelle, ses accès, ce qu'il faut défricher. Ce n'est pas une seconde progression :
/// l'avancement d'un bâtiment reste <see cref="Building.Progress"/>, celui d'une route est la somme du travail de ses cellules.
/// </summary>
public sealed class DevelopmentProject
{
    internal DevelopmentProject(int id, DevelopmentKind kind, DevelopmentPriority priority, int parcelId, long createdTicks, string reason)
    {
        Id = id;
        Kind = kind;
        Priority = priority;
        ParcelId = parcelId;
        CreatedTicks = createdTicks;
        Reason = reason;
        State = ProjectState.Accepted;
    }

    public int Id { get; }
    public DevelopmentKind Kind { get; }
    public DevelopmentPriority Priority { get; internal set; }

    /// <summary>La parcelle concernée (-1 pour un aménagement de route pur).</summary>
    public int ParcelId { get; }

    /// <summary>Le bâtiment ou le champ créé (-1 pour une route).</summary>
    public int OccupantId { get; internal set; } = -1;

    /// <summary>Les tronçons d'accès à aménager ou à garder praticables.</summary>
    public List<int> SegmentIds { get; } = [];

    /// <summary>Les cellules d'arbres ou de buissons à dégager avant de pouvoir aménager l'accès (index de cellule).</summary>
    public List<int> ClearCells { get; } = [];

    public ProjectState State { get; internal set; }
    public long CreatedTicks { get; }

    /// <summary>Le motif en quelques mots (clé de la demande), pour le récit et le diagnostic.</summary>
    public string Reason { get; }
}
