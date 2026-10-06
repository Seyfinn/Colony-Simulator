using GodColony.Simulation.Map;

namespace GodColony.Simulation;

/// <summary>Terrain durable d'une région, conservé même lorsqu'un établissement ferme.</summary>
public sealed class RegionState(int tileIndex, LocalMap map)
{
    public int TileIndex { get; } = tileIndex;
    public LocalMap Map { get; } = map;
    public int? OwnerColonyId { get; internal set; }

    /// <summary>La nature sauvage de la région : gibier, prédateurs, ruches et plantes (voir <see cref="Nature.Wildlife"/>).</summary>
    public Nature.RegionWildlife Wildlife { get => _wildlife ??= Nature.RegionWildlife.Create(Map); internal set => _wildlife = value; }
    private Nature.RegionWildlife? _wildlife;
    internal List<Deposit>? Geology;
    public IReadOnlyList<Deposit> Deposits => Geology ?? [];
}
