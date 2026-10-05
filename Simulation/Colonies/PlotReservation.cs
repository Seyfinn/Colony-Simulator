namespace GodColony.Simulation.Colonies;

/// <summary>
/// Une parcelle : le terrain réservé à un bâtiment, un champ ou un espace public. Elle distingue l'emprise bâtie, la cour (une marge
/// libre autour), l'espace public et les accès (des tronçons partageables, voir <see cref="RoadSegment"/>). La parcelle est durable : elle
/// survit à son projet, qui sera purgé après achèvement. Une demande jamais acceptée ne réserve aucun terrain.
/// </summary>
public sealed class PlotReservation
{
    internal PlotReservation(int id, int districtId, ParcelKind kind, int x, int y, int width, int height, long createdTicks)
    {
        Id = id;
        DistrictId = districtId;
        Kind = kind;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        CreatedTicks = createdTicks;
        State = ReservationState.Reserved;
    }

    public int Id { get; }
    public int DistrictId { get; internal set; }
    public ParcelKind Kind { get; }

    /// <summary>Le projet de développement qui l'a fait naître (-1 une fois purgé).</summary>
    public int ProjectId { get; internal set; } = -1;

    /// <summary>L'identifiant du bâtiment ou du champ qui l'occupe (-1 tant que rien n'est posé).</summary>
    public int OccupantId { get; internal set; } = -1;

    /// <summary>Pour une parcelle de bâtiment : son type.</summary>
    public BuildingType? BuildingType { get; internal set; }

    public ReservationState State { get; internal set; }

    /// <summary>L'emprise exacte (cases, coin en haut à gauche).</summary>
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>La cour : nombre de cases laissées libres tout autour de l'emprise (1 à 3).</summary>
    public int Margin { get; internal set; } = 1;

    /// <summary>La case de l'emprise qui touche la porte (-1 si la parcelle n'a pas d'entrée).</summary>
    public int EntryX { get; internal set; } = -1;
    public int EntryY { get; internal set; } = -1;

    /// <summary>La case, hors emprise, où l'on se tient pour entrer, travailler ou servir (-1 si la parcelle n'a pas d'accès).</summary>
    public int AccessX { get; internal set; } = -1;
    public int AccessY { get; internal set; } = -1;

    /// <summary>Les tronçons d'accès qu'elle possède ou partage.</summary>
    public List<int> SegmentIds { get; } = [];

    public long CreatedTicks { get; }

    /// <summary>Une parcelle ancienne (sauvegarde v1) dont l'accès ne respecte pas les règles actuelles : on la traverse encore, comme avant.</summary>
    public bool LegacyOpen { get; internal set; }

    public bool HasAccess => AccessX >= 0;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    public IEnumerable<(int X, int Y)> Tiles
    {
        get
        {
            for (int dy = 0; dy < Height; dy++)
            for (int dx = 0; dx < Width; dx++)
                yield return (X + dx, Y + dy);
        }
    }
}
