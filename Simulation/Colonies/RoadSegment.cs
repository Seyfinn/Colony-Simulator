namespace GodColony.Simulation.Colonies;

/// <summary>
/// Un tracé accepté : des cellules contiguës, de l'entrée d'une parcelle jusqu'au réseau. Il peut contenir au début de la terre nue, des sentiers
/// et un gué, et rester <b>praticable</b> même si son aménagement n'est pas uniforme. La surface réalisée vit dans la couche routière de la carte ;
/// un tracé planifié n'est jamais une route achevée par le seul fait d'exister dans cette liste.
/// </summary>
public sealed class RoadSegment
{
    internal RoadSegment(int id, SegmentFunction function, RoadSurface targetSurface, int priority, long createdTicks)
    {
        Id = id;
        Function = function;
        TargetSurface = targetSurface;
        Priority = priority;
        CreatedTicks = createdTicks;
    }

    public int Id { get; }
    public SegmentFunction Function { get; }
    public RoadSurface TargetSurface { get; internal set; }
    public int Priority { get; internal set; }
    public long CreatedTicks { get; }

    /// <summary>Les cellules, dans l'ordre, chacune voisine de la précédente (index <c>y × largeur + x</c>).</summary>
    public List<int> Cells { get; } = [];

    /// <summary>Les parcelles durables qui doivent pouvoir sortir par ce tracé.</summary>
    public List<int> OwnerParcelIds { get; } = [];

    /// <summary>Le projet de travaux qui l'aménage en ce moment (-1 s'il n'y en a pas).</summary>
    public int ActiveProjectId { get; internal set; } = -1;
}
