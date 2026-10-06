namespace GodColony.Simulation.Colonies;

/// <summary>
/// Un besoin non urgent différé pour mieux remplir une charrette : d'où, vers où, depuis quand, et pourquoi on attend encore. Aucune cargaison n'est réservée pendant l'attente ;
/// tout se revalide au départ.
/// </summary>
public sealed class SupplyWaitState
{
    public int SourceId { get; internal set; }
    public int DestinationId { get; internal set; }

    /// <summary>La date de la première demande qui a motivé l'attente.</summary>
    public long FirstTicks { get; internal set; }
    public string LastCause { get; internal set; } = "";
}

/// <summary>Le regroupement des missions internes non urgentes : on attend au plus un jour, ou l'on part dès que la charge utile est remplie à 70 %.</summary>
public static class SupplyBatching
{
    public const double FillToDepart = 0.70;
    public const int MaxWaitDays = 1;

    /// <summary>
    /// Faut-il partir maintenant ? Une urgence part sans seuil. Sinon : départ si la charge utile est assez remplie, ou si l'attente a duré un jour et que la charge vaut le voyage.
    /// Une attente commence à la première demande et ne la dépasse jamais ; elle ne réserve rien.
    /// </summary>
    internal static bool ShouldDepart(Settlement source, Settlement destination, long now, bool urgent, double weight, double usefulCapacity, double minWorthwhile, out string cause)
    {
        cause = "";
        if (urgent)
            return true;
        SupplyWaitState? wait = source.SupplyWaits.FirstOrDefault(w => w.DestinationId == destination.Id);
        double fill = usefulCapacity <= 0 ? 1 : weight / usefulCapacity;
        if (fill >= FillToDepart)
            return true;
        if (wait is null)
            source.SupplyWaits.Add(wait = new SupplyWaitState { SourceId = source.Id, DestinationId = destination.Id, FirstTicks = now });
        if (now - wait.FirstTicks >= MaxWaitDays * Time.TimeConstants.TicksPerDay && weight >= minWorthwhile)
            return true;
        wait.LastCause = weight < minWorthwhile ? "charge trop légère pour le voyage" : $"charge remplie à {fill:P0} : attente d'un meilleur remplissage";
        cause = wait.LastCause;
        return false;
    }

    /// <summary>Le départ a eu lieu, ou le besoin a disparu : l'attente s'efface.</summary>
    internal static void Clear(Settlement source, Settlement destination) =>
        source.SupplyWaits.RemoveAll(w => w.DestinationId == destination.Id);

    /// <summary>Les attentes dont le besoin n'existe plus ne sont pas conservées.</summary>
    internal static void Prune(Settlement source, IEnumerable<int> destinationsWithNeeds)
    {
        var live = destinationsWithNeeds.ToHashSet();
        source.SupplyWaits.RemoveAll(w => !live.Contains(w.DestinationId));
    }
}
