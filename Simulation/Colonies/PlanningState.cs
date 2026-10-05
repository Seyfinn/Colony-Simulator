namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'état de planification du monde : la file des recherches d'emplacement en cours, leur curseur d'équité et des compteurs d'opérations.
/// Il appartient au monde (le budget est partagé entre toutes les colonies) ; chaque recherche garde sa colonie propriétaire. Tout ce qui
/// influence les décisions suivantes est sauvegardé : recommencer les recherches au chargement en retarderait les résultats et changerait la partie.
/// </summary>
public sealed class SettlementPlanningState
{
    /// <summary>Les recherches en file, dans l'ordre de leur création.</summary>
    public List<PlanningJob> Jobs { get; } = [];

    internal int NextJobId { get; set; } = 1;

    /// <summary>L'indice de la colonie qui a la main à égalité de priorité : il tourne, pour qu'aucune colonie ne monopolise le budget.</summary>
    internal int FairnessCursor { get; set; }

    /// <summary>Nombre de rendez-vous du planificateur depuis le début de la partie.</summary>
    public long Rendezvous { get; internal set; }

    /// <summary>Compteurs d'opérations (jamais de millisecondes) : base des mesures de performance.</summary>
    public long Expansions { get; internal set; }
    public long Prefilters { get; internal set; }
    public long ValidationCells { get; internal set; }
    public long CompletedSearches { get; internal set; }
}
