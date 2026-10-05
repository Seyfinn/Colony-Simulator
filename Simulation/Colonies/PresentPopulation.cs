using System.Collections;

namespace GodColony.Simulation.Colonies;

/// <summary>Vue locale des citoyens ; seule Colony.Members possède la population politique.</summary>
public sealed class PresentPopulation(Colony owner, Settlement settlement) : IReadOnlyList<Colonist>
{
    private bool IsPresent(Colonist colonist) => colonist.TravelId == 0 && colonist.Transit == TransitState.None
        && (colonist.LocationSettlementId == settlement.Id || (colonist.LocationSettlementId == 0 && settlement.Id == 0));
    public int Count => owner.Members.Count(IsPresent);
    public Colonist this[int index] => owner.Members.Where(IsPresent).ElementAt(index);
    public IEnumerator<Colonist> GetEnumerator() => owner.Members.Where(IsPresent).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(Colonist colonist)
    {
        int index = 0;
        foreach (Colonist present in this) { if (present == colonist) return index; index++; }
        return -1;
    }
    internal void Add(Colonist colonist)
    {
        bool newcomer = !owner.Members.Contains(colonist);
        if (newcomer) owner.Members.Add(colonist);
        colonist.Colony = owner;
        if (newcomer || colonist.HomeSettlementId == 0) colonist.HomeSettlementId = settlement.Id;
        colonist.LocationSettlementId = settlement.Id;
        colonist.TravelId = 0;
    }
    internal bool Remove(Colonist colonist) => colonist.TravelId == 0 && owner.Members.Remove(colonist);
}
