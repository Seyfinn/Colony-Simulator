namespace GodColony.Simulation.Colonies;

/// <summary>
/// Fait avancer par petits lots, à cadence fixe de simulation, les recherches d'emplacement de toutes les colonies. Le budget est <b>partagé</b> : seize colonies ne
/// multiplient pas seize fois le travail maximal d'un tick. Les recherches urgentes passent avant le confort ; à priorité égale, les colonies tournent dans un ordre
/// stable pour qu'aucune ne monopolise le budget. On compte des opérations, jamais des millisecondes : le backend ne connaît pas la vitesse d'affichage, les mêmes
/// ticks donnent les mêmes décisions à ×1 comme à ×200.
/// </summary>
public static class SettlementPlanningScheduler
{
    /// <summary>
    /// Termine d'un coup toutes les recherches en file, sans budget : l'outil des tests et du développement (jamais le chemin normal du jeu, qui répartit le travail
    /// sur les rendez-vous suivants).
    /// </summary>
    internal static void Drain(WorldState world)
    {
        foreach (PlanningJob job in world.Planning.Jobs.ToList())
        {
            using var scope = job.Owner.UseSettlement(world.SettlementById(job.SettlementId) ?? job.Owner.PrimarySettlement);
            SitePlanner.Advance(job, SitePlanner.Budget.Unlimited());
            if (job.Search is not null)
                Pathfinding.IncrementalPathSearch.Release(job.Search);
            SettlementPlanner.Deliver(job);
            world.Planning.Jobs.Remove(job);
            world.Planning.CompletedSearches++;
        }
    }

    /// <summary>
    /// Un rendez-vous : appelé à chaque tick par le monde, il ne fait quelque chose que tous les <see cref="SettlementRules.RendezvousTicks"/> ticks, et rien du tout
    /// s'il n'y a aucune recherche en file.
    /// </summary>
    internal static void Tick(WorldState world)
    {
        if (world.Clock.Ticks % SettlementRules.RendezvousTicks != 0)
            return;
        SettlementPlanningState planning = world.Planning;
        planning.Rendezvous++;
        if (planning.Jobs.Count == 0)
            return;

        var budget = new SitePlanner.Budget
        {
            Prefilters = SettlementRules.PrefilterBudget,
            Expansions = SettlementRules.AStarExpansionBudget,
            Cells = SettlementRules.ValidationCellBudget,
            MaxActiveSearches = SettlementRules.MaxActiveSearches,
            ActiveSearches = planning.Jobs.Count(j => j.Search is not null),
        };

        int colonies = Math.Max(1, world.Colonies.Count);
        int cursor = planning.FairnessCursor % colonies;
        List<PlanningJob> queue = planning.Jobs.Where(j => !j.IsFinished).ToList();
        queue.Sort((a, b) =>
        {
            int byPriority = a.Priority.CompareTo(b.Priority);
            if (byPriority != 0)
                return byPriority;
            int rankA = (world.Colonies.IndexOf(a.Owner) - cursor + colonies) % colonies, rankB = (world.Colonies.IndexOf(b.Owner) - cursor + colonies) % colonies;
            return rankA != rankB ? rankA.CompareTo(rankB) : a.Id.CompareTo(b.Id);
        });

        int lastServed = -1;
        foreach (PlanningJob job in queue)
        {
            if (budget.Cells <= 0 && budget.Prefilters <= 0 && budget.Expansions <= 0)
                break;
            using var scope = job.Owner.UseSettlement(world.SettlementById(job.SettlementId) ?? job.Owner.PrimarySettlement);
            SitePlanner.Advance(job, budget);
            lastServed = world.Colonies.IndexOf(job.Owner);
            if (job.IsFinished)
            {
                SettlementPlanner.Deliver(job);
                planning.Jobs.Remove(job);
                planning.CompletedSearches++;
            }
        }
        if (lastServed >= 0)
            planning.FairnessCursor = (lastServed + 1) % colonies;
        planning.Expansions += budget.SpentExpansions;
        planning.Prefilters += budget.SpentPrefilters;
        planning.ValidationCells += budget.SpentCells;
    }
}
