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

    /// <summary>L'espèce qui peuple la colonie (celle des fondateurs et des voyageurs).</summary>
    public Species Species { get; internal set; } = Species.Human;

    /// <summary>La carte locale de la colonie : chaque colonie a la sienne, façonnée par son environnement.</summary>
    public GodColony.Simulation.Map.LocalMap Map { get; internal set; } = null!;

    public GodColony.Simulation.Pathfinding.Pathfinder Pathfinder { get; internal set; } = null!;

    /// <summary>L'horloge du monde, pour calculer les âges.</summary>
    internal GodColony.Simulation.Time.GameClock Clock { get; set; } = new(0);

    /// <summary>Ceux qui sont morts dans la colonie, avec leur tombe.</summary>
    public List<Grave> Graves { get; } = [];

    /// <summary>Les colons qui travaillent : tout le monde sauf les enfants.</summary>
    public IEnumerable<Colonist> Workers => Members.Where(m => m.Stage != LifeStage.Child);

    public int Children => Members.Count(m => m.Stage == LifeStage.Child);

    /// <summary>Le feu de camp, cœur de la colonie : on y mange, on s'y détend, on dort autour.</summary>
    public int CampX { get; }
    public int CampY { get; }

    /// <summary>Cases accessibles autour du feu, des plus proches aux plus lointaines.</summary>
    public IReadOnlyList<(int X, int Y)> GatherSpots { get; }

    /// <summary>Le point de la montagne où la colonie a ouvert sa carrière (null s'il n'y a pas de roche accessible).</summary>
    public (int X, int Y)? Quarry { get; internal set; }

    public List<Colonist> Members { get; } = [];
    public Stockpile Stock { get; } = new();

    /// <summary>Ce que coûte chaque ressource en heures de travail.</summary>
    public LaborLedger Labor { get; } = new();

    /// <summary>Part de la main-d'œuvre consacrée à chaque secteur (la somme vaut 1). Le cerveau l'ajuste chaque heure.</summary>
    public Dictionary<WorkSector, float> WorkShares { get; } = new()
    {
        [WorkSector.Food] = 0.5f,
        [WorkSector.Farm] = 0f,
        [WorkSector.Wood] = 0.3f,
        [WorkSector.Stone] = 0.2f,
        [WorkSector.Construction] = 0f,
        [WorkSector.Craft] = 0f,
        [WorkSector.Free] = 0f,
    };

    /// <summary>Les bâtiments de la colonie, achevés ou en chantier.</summary>
    public List<Building> Buildings { get; } = [];

    /// <summary>Les décisions que la colonie soumet au joueur.</summary>
    public PrayerBook Prayers => _prayers ??= new PrayerBook(this);
    private PrayerBook? _prayers;

    /// <summary>Les canaux d'irrigation, achevés ou en chantier.</summary>
    public List<Canal> Canals { get; } = [];

    /// <summary>Toutes les cases de canal prévues (creusées ou non) : on n'y bâtit rien et on n'y sème pas.</summary>
    internal HashSet<(int X, int Y)> CanalTiles { get; } = [];

    /// <summary>Les canaux qu'il reste à creuser.</summary>
    public IEnumerable<Canal> CanalsInProgress => Canals.Where(c => !c.IsComplete);

    /// <summary>Les ateliers achevés d'un type donné.</summary>
    public IEnumerable<Building> Workshops(BuildingType type) => Buildings.Where(b => b.Type == type && b.IsComplete);

    /// <summary>
    /// Des postes de mineur où aucun chemin n'a mené aujourd'hui (roche isolée sur un plateau) : on passe aux suivants
    /// au lieu de rester bloqué sur les plus proches. Vidé chaque jour, car la carrière change.
    /// </summary>
    internal HashSet<(int X, int Y)> UnreachableStands { get; } = [];

    /// <summary>Les produits dont la colonie a déjà annoncé la première fabrication.</summary>
    internal HashSet<ResourceType> AnnouncedProducts { get; } = [];

    /// <summary>Usure des outils : à chaque fois qu'elle atteint 1, un outil casse.</summary>
    internal float ToolWear { get; set; }

    /// <summary>Les champs de la colonie.</summary>
    public List<Field> Fields { get; } = [];

    /// <summary>Temps passé à semer, pour que le coût des céréales inclue les semailles.</summary>
    private long _sowTicks;
    private int _plotsSown;

    internal void RecordSowing(long ticks)
    {
        _sowTicks += ticks;
        _plotsSown++;
    }

    /// <summary>Heures de travail qu'il faut pour semer une parcelle (0 tant qu'on n'a rien semé).</summary>
    public double SowHoursPerPlot => _plotsSown == 0 ? 0 : LaborLedger.TicksToHours(_sowTicks) / _plotsSown;

    public IEnumerable<Building> ConstructionSites => Buildings.Where(b => !b.IsComplete);

    /// <summary>Colons qui n'ont pas de hutte et dorment à la belle étoile.</summary>
    public int Homeless => Members.Count(m => m.Home is null);

    /// <summary>
    /// Ceux qui arrivent ou s'en vont : ils marchent entre le bord de la carte et le camp,
    /// sans faire partie des membres (ni de leurs statistiques).
    /// </summary>
    public List<Colonist> Transients { get; } = [];

    /// <summary>Dernière fois qu'une amitié ou une rivalité a été notée dans les pensées.</summary>
    internal long LastSocialThoughtTicks { get; set; } = long.MinValue / 2;

    /// <summary>Dernier jour où l'on a dû refuser un voyageur (pour ne pas radoter dans les pensées).</summary>
    internal long LastRefusalDay { get; set; } = -100;

    /// <summary>Les places libres des huttes achevées vont aux colons qui dormaient dehors.</summary>
    internal void FillVacancies()
    {
        foreach (Building building in Buildings.Where(b => b.IsComplete && b.IsHut))
        foreach (Colonist colonist in Members.Where(m => m.Home is null).Take(Building.HutCapacity - building.Residents.Count).ToList())
        {
            colonist.Home = building;
            building.Residents.Add(colonist);
        }
    }

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

    /// <summary>Chaque colon a sa place pour dormir : dans sa hutte s'il en a une, sinon autour du feu.</summary>
    public (int X, int Y) SleepSpot(Colonist colonist) =>
        colonist.Home is { } home ? home.BedOf(colonist) : GatherSpots[(1 + Members.IndexOf(colonist)) % GatherSpots.Count];

    public float AverageMood => Members.Count == 0 ? 0f : Members.Average(m => m.Needs.Mood);

    /// <summary>
    /// Affecte chaque colon à un secteur selon les parts voulues, en confiant chaque poste au plus compétent.
    /// Un colon garde de préférence son secteur actuel, pour éviter qu'il change de métier sans arrêt.
    /// </summary>
    public void AssignSectors()
    {
        List<Colonist> workers = Workers.ToList();
        foreach (Colonist child in Members.Where(m => m.Stage == LifeStage.Child))
            child.Sector = WorkSector.Free;
        Dictionary<WorkSector, int> quotas = ComputeQuotas(workers.Count);
        // Le temps libre revient à ceux qui restent une fois les postes productifs pourvus.
        var candidates =
            from colonist in workers
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
