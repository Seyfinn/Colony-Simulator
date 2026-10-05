using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Map;

public enum DepositMode { Finite, Permanent }
public enum DepositObservation { Hint, Surveyed, Working, Depleted }

/// <summary>Réserve physique unique, indépendante des renseignements de ses observateurs.</summary>
public sealed class Deposit
{
    public int Id { get; internal set; }
    public int Region { get; internal set; }
    public ResourceType Material { get; internal set; }
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public DepositMode Mode { get; internal set; }
    public int InitialReserve { get; internal set; }
    public int RemainingReserve { get; internal set; }
    public int DailyLimit { get; internal set; }
    public float Difficulty { get; internal set; } = 1;

    /// <summary>Profondeur du gîte : 0 affleure à la surface, 3 exige une prospection poussée. La vérité reste ici, jamais dans les renseignements.</summary>
    public int Depth { get; internal set; }
    internal long BudgetDay { get; set; } = -1;
    internal int ExtractedToday { get; set; }

    internal int Extract(int requested, long day)
    {
        if (BudgetDay != day) { BudgetDay = day; ExtractedToday = 0; }
        int amount = Math.Max(0, Math.Min(requested, DailyLimit - ExtractedToday));
        if (Mode == DepositMode.Finite) amount = Math.Min(amount, RemainingReserve);
        ExtractedToday += amount;
        if (Mode == DepositMode.Finite) RemainingReserve -= amount;
        return amount;
    }
}

public sealed class DepositKnowledge
{
    public int SiteId { get; internal set; }
    public int Region { get; internal set; }
    public ResourceType Material { get; internal set; }
    public DepositObservation State { get; internal set; }
    public int EstimateMin { get; internal set; }
    public int EstimateMax { get; internal set; }

    /// <summary>Confiance dans le renseignement, de 0 (rumeur) à 1 : elle monte avec la profondeur sondée et le travail réel sur le gîte.</summary>
    public float Confidence { get; internal set; }

    /// <summary>La colonie a déjà été prévenue que ce gîte s'épuise.</summary>
    public bool Alerted { get; internal set; }

    /// <summary>Profondeur de sondage atteinte quand le renseignement a été établi (−1 : simple affleurement aperçu).</summary>
    public int SurveyedDepth { get; internal set; } = -1;
    public long ObservedTicks { get; internal set; }
    public string Source { get; internal set; } = "Prospection sur place";
}

public static class DepositExtraction
{
    public static int Extract(WorldState world, Colony observer, Deposit deposit, int requested)
    {
        if (ExtendedIndustry.NeedsMine(deposit.Material) && !Civic.Has(observer, BuildingType.MineDepot)) return 0;
        if (!observer.DepositReports.Any(k => k.SiteId == deposit.Id && k.State != DepositObservation.Depleted)) return 0;
        int amount = deposit.Extract(requested, world.Clock.TotalDays);
        foreach (DepositKnowledge known in observer.DepositReports.Where(k => k.SiteId == deposit.Id))
        {
            known.ObservedTicks = world.Clock.Ticks;
            known.State = deposit.Mode == DepositMode.Finite && deposit.RemainingReserve == 0
                ? DepositObservation.Depleted : DepositObservation.Working;
            if (deposit.Mode == DepositMode.Permanent || amount == 0) continue;
            // Le travail réel resserre l'estimation : chaque quart de la réserve initiale extrait ajoute 60 % de confiance.
            known.Confidence = Math.Min(1f, known.Confidence + 0.6f * amount / Math.Max(4f, 0.25f * deposit.InitialReserve));
            float spread = 1f - known.Confidence;
            known.EstimateMin = (int)(deposit.RemainingReserve * (1 - 0.8f * spread));
            known.EstimateMax = (int)Math.Ceiling(deposit.RemainingReserve * (1 + 1.2f * spread));
        }
        return amount;
    }
}
