using GodColony.Simulation.Colonies;

namespace GodColony.Simulation;

public sealed partial class WorldState
{
    private int _nextColonyId = 1, _nextSettlementId = 1, _nextTripId = 1;
    private Dictionary<int, RegionState>? _regions;
    public IReadOnlyDictionary<int, RegionState> Regions => _regions ??= [];
    public IEnumerable<Settlement> Settlements => Colonies.SelectMany(c => c.Settlements);
    public Settlement? SettlementById(int id) => Settlements.FirstOrDefault(s => s.Id == id);
    public Colonist? ColonistById(int id) => Colonies.SelectMany(c => c.Citizens).FirstOrDefault(c => c.Id == id);

    internal void RegisterSettlement(Colony colony, int tile)
    {
        if (colony.Id == 0) colony.Id = _nextColonyId++;
        Settlement settlement = colony.PrimarySettlement;
        if (settlement.Id == 0) settlement.Id = _nextSettlementId++;
        colony.PrimarySettlementId = settlement.Id;
        settlement.RegionTileIndex = tile;
        _regions ??= [];
        if (_regions.TryGetValue(tile, out RegionState? region) && !ReferenceEquals(region.Map, settlement.Map))
            throw new InvalidOperationException("Une région possède déjà un terrain durable.");
        _regions[tile] = new RegionState(tile, settlement.Map) { OwnerColonyId = colony.Id };
        TerritorialTravel.Observe(this, colony, VisitRegion(tile));
        foreach (Colonist colonist in colony.Members.Concat(colony.Transients))
        {
            if (colonist.HomeSettlementId == 0) colonist.HomeSettlementId = settlement.Id;
            if (colonist.LocationSettlementId == 0) colonist.LocationSettlementId = settlement.Id;
        }
    }

    /// <summary>
    /// Installe une colonie issue d'un schisme dont le seul établissement existe déjà (carte, stocks et habitants conservés) : la région change
    /// de propriétaire sans être recréée.
    /// </summary>
    internal void AdoptColony(Colony daughter, Settlement place)
    {
        daughter.Id = _nextColonyId++;
        daughter.PrimarySettlementId = place.Id;
        place.Kind = SettlementKind.Village;
        WorldMap.PlaceAt(daughter, place.RegionTileIndex);
        daughter.Planning = Planning;
        daughter.Ledger = Money;
        Colonies.Add(daughter);
        if (_regions is not null && _regions.TryGetValue(place.RegionTileIndex, out RegionState? region)) region.OwnerColonyId = daughter.Id;
        foreach (PlanningJob job in Planning.Jobs.Where(j => j.SettlementId == place.Id)) job.Owner = daughter;
        UpdateRivers();
    }

    internal void RegisterTrip(Caravan trip)
    {
        if (trip.Id == 0) trip.Id = _nextTripId++;
        if (trip.FromSettlementId == 0) trip.FromSettlementId = trip.From.LocalSettlement.Id;
        if (trip.ToSettlementId == 0) trip.ToSettlementId = trip.To.LocalSettlement.Id;
        foreach (Colonist trader in trip.Traders)
        {
            trader.HomeSettlementId = trip.FromSettlementId;
            trader.TravelId = trip.Id;
        }
    }

    internal void RegisterWarTrip(WarParty trip)
    {
        if (trip.Id == 0) trip.Id = _nextTripId++;
        foreach (Colonist warrior in trip.Warriors)
        {
            warrior.HomeSettlementId = trip.From.PrimarySettlementId;
            warrior.TravelId = trip.Id;
        }
    }

    internal void ValidateTerritories()
    {
        int maxColony = Colonies.Select(c => c.Id).DefaultIfEmpty(0).Max();
        int maxSettlement = Settlements.Select(s => s.Id).DefaultIfEmpty(0).Max();
        int[] trips = Caravans.Select(t => t.Id).Concat(WarParties.Select(t => t.Id)).ToArray();
        if (_nextColonyId <= maxColony || _nextSettlementId <= maxSettlement
            || trips.Any(id => id <= 0) || trips.Distinct().Count() != trips.Length || _nextTripId <= trips.DefaultIfEmpty(0).Max())
            throw new InvalidDataException("Compteurs territoriaux invalides.");
        var citizens = Colonies.SelectMany(c => c.Members).ToArray();
        if (citizens.Select(c => c.Id).Distinct().Count() != citizens.Length
            || citizens.Any(c => c.Colony.Members.Count(m => m == c) != 1
                || SettlementById(c.HomeSettlementId)?.Owner != c.Colony
                || (c.TravelId != 0 && !trips.Contains(c.TravelId))
                || (c.TravelId == 0 && SettlementById(c.LocationSettlementId)?.Owner != c.Colony)))
            throw new InvalidDataException("Présence ou citoyenneté invalide.");
    }

    internal void RestoreSettlements(bool legacy)
    {
        Money ??= new MonetaryLedger();
        Territory ??= new TerritorialRules();
        foreach (Colony colony in Colonies) colony.Ledger = Money;
        if (legacy && Colonies.Any(c => c.Id == 0))
        {
            _nextColonyId = _nextSettlementId = _nextTripId = 1;
            foreach (Colony colony in Colonies) RegisterSettlement(colony, WorldMap.TileOf(colony));
            foreach (Caravan trip in Caravans)
            {
                foreach (Colonist traveler in trip.Traders)
                    if (!trip.From.Members.Contains(traveler)) trip.From.Members.Add(traveler);
                RegisterTrip(trip);
            }
            foreach (WarParty trip in WarParties)
            {
                foreach (Colonist traveler in trip.Warriors)
                    if (!trip.From.Members.Contains(traveler)) trip.From.Members.Add(traveler);
                RegisterWarTrip(trip);
            }
            foreach (Colony colony in Colonies)
                foreach (Colonist returned in colony.Transients.Where(c => c.ReturningTrader))
                    if (!colony.Members.Contains(returned)) colony.Members.Add(returned);
        }
        if (legacy && Colonies.Count > 0) UpdateRivers();
    }
}
