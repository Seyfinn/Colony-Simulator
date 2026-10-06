using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Pourquoi une extension d'atelier est (ou n'est pas) décidée : un motif structuré que les vues et les tests lisent.</summary>
public enum ExtensionVerdict { None, Wanted, TooLittleUse, NoDemand, NoInputs, SurvivalFirst, PaybackTooLong, MaxReached, NoSite, UnderConstruction }

/// <summary>Les mesures d'une journée d'un établissement : travail productif par atelier, production et combustible. Des compteurs d'observation que des décisions relisent.</summary>
public sealed class ScaleDay
{
    public long Day { get; internal set; }

    /// <summary>Ticks de travail effectif (hors trajet, repas et attente) par bâtiment principal.</summary>
    public Dictionary<int, int> WorkTicks { get; } = [];

    /// <summary>Unités déposées à la sortie des ateliers à lots, et combustible déclaré consommé par leurs recettes.</summary>
    public Dictionary<ResourceType, int> Produced { get; } = [];
    public Dictionary<ResourceType, int> FuelUsed { get; } = [];

    /// <summary>Fois où un artisan s'est présenté devant un atelier sans poste libre alors que la demande et les intrants y étaient : du temps productif perdu, par bâtiment principal.</summary>
    public Dictionary<int, int> SlotRefusals { get; } = [];

    /// <summary>Charrettes en service au plus fort de la journée, et fois où un porteur en aurait pris une sans qu aucune soit libre : le besoin simultané réellement observé.</summary>
    /// <summary>Usagers servis et refusés (service plein ou trop loin) par type de service.</summary>
    public Dictionary<CivicUse, int> ServiceServed { get; } = [];
    public Dictionary<CivicUse, int> ServiceDenied { get; } = [];

    public int CartPeak { get; internal set; }
    public int CartDenied { get; internal set; }

    /// <summary>Lots achevés par bâtiment principal.</summary>
    public Dictionary<int, int> Batches { get; } = [];
}

/// <summary>
/// Le registre d'observation d'un établissement : un anneau de 30 jours clos, l'état de ses ateliers à lots et les verdicts d'extension. Toute décision qui utilise cette fenêtre
/// retrouve les mêmes valeurs après un rechargement : tout est sauvegardé.
/// </summary>
public sealed class SettlementScaleLedger
{
    public const int RingDays = 30;

    /// <summary>La journée en cours (non close).</summary>
    public ScaleDay Today { get; internal set; } = new();

    /// <summary>Les journées closes, de la plus ancienne à la plus récente (au plus <see cref="RingDays"/>).</summary>
    public List<ScaleDay> Days { get; } = [];

    /// <summary>Pour chaque atelier à lots : le jour où sa capacité actuelle a été vue pour la première fois (la fenêtre d'utilisation ne remonte pas avant).</summary>
    public Dictionary<int, long> CapacitySince { get; } = [];
    public Dictionary<int, int> CapacityKey { get; } = [];

    /// <summary>Le dernier verdict d'extension par bâtiment principal, et le dernier jour où l'on a cherché un site.</summary>
    public Dictionary<int, ExtensionVerdict> Verdicts { get; } = [];
    public long LastSiteSearchDay { get; internal set; } = -1;

    /// <summary>Ferme la journée qui s'achève et en ouvre une : appelée une seule fois par jour, après les changements quotidiens.</summary>
    internal void Close(long newDay)
    {
        if (Today.Day == newDay && Days.Count > 0)
            return;
        Days.Add(Today);
        if (Days.Count > RingDays)
            Days.RemoveRange(0, Days.Count - RingDays);
        Today = new ScaleDay { Day = newDay };
    }

    internal void AddWork(Building workshop) =>
        Today.WorkTicks[workshop.Id] = Today.WorkTicks.GetValueOrDefault(workshop.Id) + 1;

    internal void AddProduction(Building workshop, ResourceType output, int units)
    {
        Today.Produced[output] = Today.Produced.GetValueOrDefault(output) + units;
        Today.Batches[workshop.Id] = Today.Batches.GetValueOrDefault(workshop.Id) + 1;
    }

    internal void AddRefusal(Building workshop) =>
        Today.SlotRefusals[workshop.Id] = Today.SlotRefusals.GetValueOrDefault(workshop.Id) + 1;

    internal void AddFuel(ResourceType fuel, int units) =>
        Today.FuelUsed[fuel] = Today.FuelUsed.GetValueOrDefault(fuel) + units;

    /// <summary>Les <paramref name="days"/> dernières journées closes (moins s'il n'y en a pas assez).</summary>
    public IEnumerable<ScaleDay> Last(int days) => Days.Skip(Math.Max(0, Days.Count - days));
}
