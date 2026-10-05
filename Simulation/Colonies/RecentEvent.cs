namespace GodColony.Simulation.Colonies;

/// <summary>Compte rendu factuel pour l'affichage. N'influence ni les décisions ni le hasard.</summary>
public enum ColonyEventKind { Fire, Raid, Peddler }
public enum ColonyEventOutcome { Extinguished, Ruined, Repelled, Pillaged, Traded, Unaffordable }
public sealed record RecentEvent(ColonyEventKind Kind, int X, int Y, long Ticks, ColonyEventOutcome Outcome, BuildingType? Building = null);

/// <summary>Effets temporaires, exclus des sauvegardes pour préserver leur schéma et ne pas rejouer un feu à la reprise.</summary>
internal static class RecentEventHistory
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Settlement, List<RecentEvent>> Reports = new();
    internal static List<RecentEvent> For(Colony colony) => Reports.GetOrCreateValue(colony.LocalSettlement);
}
