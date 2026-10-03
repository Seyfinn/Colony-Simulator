using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Le registre des coûts de la colonie : combien d'heures de travail lui coûte une unité de chaque ressource,
/// trajets compris. C'est la base de l'économie : comparer ces coûts entre colonies révèle leurs avantages
/// absolus et comparatifs.
///
/// La valeur suit les conditions récentes (moyenne glissante) : quand la forêt proche est épuisée
/// et qu'il faut aller plus loin, le bois coûte plus cher.
/// </summary>
public sealed class LaborLedger
{
    /// <summary>Poids de chaque nouvelle mesure dans la moyenne glissante.</summary>
    private const double RecentWeight = 0.1;

    private readonly Dictionary<ResourceType, double> _hoursPerUnit = [];
    private readonly Dictionary<ResourceType, (double Hours, int Units)> _totals = [];
    private double? _hoursPerHut;

    /// <summary>Enregistre une récolte : tant d'heures de travail ont produit tant d'unités.</summary>
    public void Record(ResourceType type, double workerHours, int units)
    {
        if (units <= 0)
            return;
        double perUnit = workerHours / units;
        _hoursPerUnit[type] = _hoursPerUnit.TryGetValue(type, out double previous)
            ? previous + (perUnit - previous) * RecentWeight
            : perUnit;

        (double hours, int total) = _totals.GetValueOrDefault(type);
        _totals[type] = (hours + workerHours, total + units);
    }

    public void RecordHut(double workerHours) =>
        _hoursPerHut = _hoursPerHut is { } previous ? previous + (workerHours - previous) * 0.5 : workerHours;

    /// <summary>Heures de travail par unité, selon les conditions récentes (null tant que rien n'a été produit).</summary>
    public double? HoursPerUnit(ResourceType type) => _hoursPerUnit.TryGetValue(type, out double value) ? value : null;

    /// <summary>Quantité totale produite depuis la fondation.</summary>
    public int TotalProduced(ResourceType type) => _totals.GetValueOrDefault(type).Units;

    /// <summary>Heures de travail pour bâtir une hutte, approvisionnement compris.</summary>
    public double? HoursPerHut => _hoursPerHut;

    public static double TicksToHours(long ticks) => ticks / (double)TimeConstants.TicksPerHour;
}
