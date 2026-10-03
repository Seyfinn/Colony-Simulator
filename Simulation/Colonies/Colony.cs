namespace GodColony.Simulation.Colonies;

public sealed class Colony
{
    public Colony(string name, int campX, int campY, List<(int X, int Y)> gatherSpots)
    {
        Name = name;
        CampX = campX;
        CampY = campY;
        GatherSpots = gatherSpots;
    }

    public string Name { get; }

    /// <summary>Le feu de camp, cœur de la colonie : on y mange, on s'y détend, on dort autour.</summary>
    public int CampX { get; }
    public int CampY { get; }

    /// <summary>Cases accessibles autour du feu, des plus proches aux plus lointaines.</summary>
    public IReadOnlyList<(int X, int Y)> GatherSpots { get; }

    /// <summary>Le point de la montagne où la colonie a ouvert sa carrière (null s'il n'y a pas de roche accessible).</summary>
    public (int X, int Y)? Quarry { get; internal set; }

    public List<Colonist> Members { get; } = [];
    public Stockpile Stock { get; } = new();

    /// <summary>Part de la main-d'œuvre consacrée à chaque secteur (la somme vaut 1). Le cerveau l'ajuste chaque heure.</summary>
    public Dictionary<WorkSector, float> WorkShares { get; } = new()
    {
        [WorkSector.Food] = 0.5f,
        [WorkSector.Wood] = 0.3f,
        [WorkSector.Stone] = 0.2f,
        [WorkSector.Free] = 0f,
    };

    /// <summary>Dernières mesures du cerveau de la colonie (null avant sa première réflexion).</summary>
    public ColonySensors? Sensors { get; internal set; }

    /// <summary>Ce que la colonie pense et décide, en langage clair, du plus ancien au plus récent.</summary>
    public List<Thought> Thoughts { get; } = [];

    /// <summary>Le feu brûle-t-il cette nuit ? Sans feu en saison froide, on dort mal.</summary>
    public bool FireLit { get; internal set; } = true;

    /// <summary>Dernier état annoncé pour chaque sujet, pour ne parler que lorsque la situation change.</summary>
    internal Dictionary<string, ColonyBrain.NarrationTopic> NarrationState { get; } = [];

    /// <summary>Cases déjà prises en charge par un colon (un buisson qu'il va cueillir, par exemple).</summary>
    internal HashSet<(int X, int Y)> Reserved { get; } = [];

    /// <summary>Chaque colon a sa place pour dormir autour du feu.</summary>
    public (int X, int Y) SleepSpot(Colonist colonist) =>
        GatherSpots[(1 + Members.IndexOf(colonist)) % GatherSpots.Count];

    public float AverageMood => Members.Count == 0 ? 0f : Members.Average(m => m.Needs.Mood);

    /// <summary>
    /// Affecte chaque colon à un secteur selon les parts voulues, en confiant chaque poste au plus compétent.
    /// Un colon garde de préférence son secteur actuel, pour éviter qu'il change de métier sans arrêt.
    /// </summary>
    public void AssignSectors()
    {
        Dictionary<WorkSector, int> quotas = ComputeQuotas(Members.Count);
        // Le temps libre revient à ceux qui restent une fois les postes productifs pourvus.
        var candidates =
            from colonist in Members
            from sector in WorkSectors.All
            let fit = sector.Fitness(colonist.Skills) + (colonist.Sector == sector ? 3f : 0f)
            orderby fit descending
            select (colonist, sector);

        var assigned = new HashSet<Colonist>();
        foreach ((Colonist colonist, WorkSector sector) in candidates.ToList())
        {
            if (assigned.Contains(colonist) || quotas[sector] <= 0)
                continue;
            colonist.Sector = sector;
            quotas[sector]--;
            assigned.Add(colonist);
        }
    }

    /// <summary>Nombre de colons par secteur ; les restes d'arrondi vont aux secteurs les plus proches du chiffre suivant.</summary>
    private Dictionary<WorkSector, int> ComputeQuotas(int workers)
    {
        var quotas = WorkSectors.All.ToDictionary(s => s, s => (int)(WorkShares[s] * workers));
        int remaining = workers - quotas.Values.Sum();
        foreach (WorkSector sector in WorkSectors.All.OrderByDescending(s => WorkShares[s] * workers % 1f).Take(remaining))
            quotas[sector]++;
        return quotas;
    }
}
