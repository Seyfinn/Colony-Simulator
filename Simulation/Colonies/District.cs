namespace GodColony.Simulation.Colonies;

/// <summary>
/// Un quartier : un pôle de développement à vocation dominante, pas un rectangle peint sur la carte. Sa vocation et son ancre sont stables ;
/// son emprise et sa charge se calculent depuis les parcelles qui lui sont rattachées (voir <see cref="SettlementLayout.EnvelopeOf"/>).
/// Un quartier n'existe officiellement qu'après l'acceptation de son premier projet.
/// </summary>
public sealed class District
{
    internal District(int id, DistrictKind kind, int anchorX, int anchorY, long createdTicks, int parentDistrictId)
    {
        Id = id;
        Kind = kind;
        AnchorX = anchorX;
        AnchorY = anchorY;
        CreatedTicks = createdTicks;
        LastExpandedTicks = createdTicks;
        ParentDistrictId = parentDistrictId;
        Status = DistrictStatus.Emerging;
    }

    public int Id { get; }
    public DistrictKind Kind { get; }
    public int AnchorX { get; }
    public int AnchorY { get; }
    public long CreatedTicks { get; }
    public long LastExpandedTicks { get; internal set; }

    /// <summary>Le quartier dont celui-ci est issu (-1 pour le cœur civique).</summary>
    public int ParentDistrictId { get; }

    public DistrictStatus Status { get; internal set; }
}
