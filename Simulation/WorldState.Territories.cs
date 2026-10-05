using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;

namespace GodColony.Simulation;

public sealed partial class WorldState
{
    internal bool TerritorialDevelopment => _trade;
    [NonSerialized] private HashSet<int>? _permanentIronRegions;
    private HashSet<int> PermanentIronRegions()
    {
        if (_permanentIronRegions is not null) return _permanentIronRegions;
        _permanentIronRegions = [];
        var visited = new HashSet<int>();
        foreach (var tile in WorldMap.Grid.Tiles)
        {
            if (tile.IsOcean || tile.Relief == World.Relief.Impassable || !visited.Add(tile.Index)) continue;
            var component = new List<int>(); var open = new Queue<int>(); open.Enqueue(tile.Index);
            while (open.TryDequeue(out int current))
            {
                if (WorldMap.Grid[current].Habitable) component.Add(current);
                foreach (int next in WorldMap.Grid.Neighbors(current))
                    if (!WorldMap.Grid[next].IsOcean && WorldMap.Grid[next].Relief != World.Relief.Impassable && visited.Add(next)) open.Enqueue(next);
            }
            if (component.Count >= 12)
            {
                component.Sort();
                int index = (int)((uint)unchecked(Seed * 397 ^ component[0] * 7919) % (uint)component.Count);
                _permanentIronRegions.Add(component[index]);
            }
        }
        return _permanentIronRegions;
    }
    internal RegionState VisitRegion(int tile)
    {
        _regions ??= [];
        if (!_regions.TryGetValue(tile, out RegionState? region))
        { region = new RegionState(tile, GenerateColonyMap(tile)); _regions.Add(tile, region); }
        if (region.Geology is null) region.Geology = GeologyGenerator.Generate(Seed, WorldMap.Grid[tile], region.Map, PermanentIronRegions().Contains(tile));
        return region;
    }

    internal Settlement FoundSettlement(Colony owner, RegionState region, int x, int y)
    {
        Settlement? previous = owner.Settlements.FirstOrDefault(s => s.RegionTileIndex == region.TileIndex && s.Status == SettlementStatus.Closed);
        if (previous is not null) { previous.Status = SettlementStatus.Active; previous.UnproductiveDays = 0; region.OwnerColonyId = owner.Id; UpdateRivers(); return previous; }
        var settlement = new Settlement(owner, x, y, ColonyFounder.FindGatherSpots(region.Map, x, y))
        { Id = _nextSettlementId++, RegionTileIndex = region.TileIndex, Map = region.Map, Kind = SettlementKind.Camp,
            Pathfinder = new Pathfinding.Pathfinder(region.Map) { Owner = owner } };
        owner.Settlements.Add(settlement); region.OwnerColonyId = owner.Id;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) region.Map.ClearFlora(x + dx, y + dy);
        using var scope = owner.UseSettlement(settlement);
        settlement.Quarry = WorkSites.FindQuarry(region.Map, x, y);
        _ = settlement.Layout;
        UpdateRivers();
        return settlement;
    }
}
