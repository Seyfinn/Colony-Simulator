using GodColony.Simulation.Map;

namespace GodColony.Simulation;

/// <summary>Terrain durable d'une région, conservé même lorsqu'un établissement ferme.</summary>
public sealed class RegionState(int tileIndex, LocalMap map)
{
    public int TileIndex { get; } = tileIndex;
    public LocalMap Map { get; } = map;
    public int? OwnerColonyId { get; internal set; }
    internal List<Deposit>? Geology;
    public IReadOnlyList<Deposit> Deposits => Geology ?? [];
}
