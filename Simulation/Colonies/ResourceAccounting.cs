using System.Runtime.CompilerServices;

namespace GodColony.Simulation.Colonies;

/// <summary>Origine d'une entrée ou destination d'une sortie du stock.</summary>
public enum ResourceFlow { Production, Usage, Purchase, Sale, Transfer, Loss }

/// <summary>
/// Compteurs d'observation, indépendants des sauvegardes : les graphiques repartent au chargement.
/// Les transferts (cargaisons, remboursements, provisions initiales) ne sont pas des productions.
/// </summary>
public static class ResourceAccounting
{
    private static readonly ConditionalWeakTable<Stockpile, long[][]> Totals = new();

    private static long[][] Of(Stockpile stock) => Totals.GetValue(stock,
        _ => Enum.GetValues<ResourceType>().Select(_ => new long[6]).ToArray());

    public static long Total(Stockpile stock, ResourceType good, ResourceFlow flow) => Of(stock)[(int)good][(int)flow];

    public static void Record(Stockpile stock, ResourceType good, ResourceFlow flow, int units)
    {
        if (units > 0 && flow != ResourceFlow.Transfer)
            Of(stock)[(int)good][(int)flow] += units;
    }
}
