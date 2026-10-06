namespace GodColony.Simulation.Colonies;

public sealed partial class Stockpile
{
    /// <summary>
    /// Le grain par jour de récolte (jour du compteur <see cref="_grainClock"/> → quantité) : tout ce qui entre le même jour forme un seul lot.
    /// On le consomme du plus vieux au plus récent, et seul un lot assez vieux peut pourrir (voir <c>Civic.Daily</c>). Le total reste dans <c>_amounts</c>.
    /// </summary>
    private Dictionary<int, int> _grainByDay = [];

    /// <summary>Jours écoulés pour ce stock : l'âge d'un lot est la différence avec son jour de récolte.</summary>
    private int _grainClock;

    /// <summary>Le grain vieillit d'un jour (un seul incrément, aucun lot n'est déplacé).</summary>
    internal void AgeGrain() => _grainClock++;

    private void AddGrain(int amount, int ageDays) =>
        _grainByDay[_grainClock - ageDays] = checked(_grainByDay.GetValueOrDefault(_grainClock - ageDays) + amount);

    /// <summary>Retire du grain, des lots les plus vieux aux plus jeunes. Un reste sans lot (stock rempli sans passer par <see cref="Add"/>) n'a rien à retirer.</summary>
    private void RemoveGrain(int amount)
    {
        foreach (int day in _grainByDay.Keys.OrderBy(d => d).ToList())
        {
            int take = Math.Min(amount, _grainByDay[day]);
            if (take == _grainByDay[day])
                _grainByDay.Remove(day);
            else
                _grainByDay[day] -= take;
            amount -= take;
            if (amount == 0)
                return;
        }
    }

    /// <summary>Le grain d'au moins <paramref name="age"/> jours.</summary>
    public int GrainAtLeast(int age) => _grainByDay.Where(p => _grainClock - p.Key >= age).Sum(p => p.Value);

    /// <summary>Les lots qui partiraient pour <paramref name="amount"/> grains (les plus vieux d'abord) : le reste sans lot part avec l'âge zéro.</summary>
    private List<CargoLot> GrainLots(int amount)
    {
        List<CargoLot> lots = [];
        foreach (int day in _grainByDay.Keys.OrderBy(d => d))
        {
            int take = Math.Min(amount, _grainByDay[day]);
            if (take > 0) lots.Add(new(ResourceType.Grain, take, _grainClock - day));
            amount -= take;
            if (amount == 0) break;
        }
        if (amount > 0) lots.Add(new(ResourceType.Grain, amount));
        return lots;
    }
}
