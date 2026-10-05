namespace GodColony.Simulation.Colonies;

public sealed class Colony
{
    public Colony(string name, int campX, int campY, List<(int X, int Y)> gatherSpots)
    {
        Name = name;
        Settlements.Add(new Settlement(this, campX, campY, gatherSpots));
    }

    public string Name { get; }
    public int Id { get; internal set; }
    private List<Settlement>? _settlements;
    public List<Settlement> Settlements => _settlements ??= [];
    public int PrimarySettlementId { get; internal set; }
    private List<GodColony.Simulation.Map.DepositKnowledge>? _depositReports;
    public List<GodColony.Simulation.Map.DepositKnowledge> DepositReports => _depositReports ??= [];
    private List<ResourceType>? _focusGoods = [];

    /// <summary>Les filières d'exportation que la colonie entretient en ce moment (voir <see cref="ProductionPlanner"/>) ; vide en crise.</summary>
    public List<ResourceType> FocusGoods => _focusGoods ??= [];

    /// <summary>Le bien qui voudrait entrer dans la liste (−1 : aucun) et depuis combien de jours il vaut nettement mieux que le plus faible.</summary>
    internal int FocusChallenger { get; set; } = -1;
    internal int FocusChallengerDays { get; set; }
    private Dictionary<int, int>? _regionReach;

    /// <summary>Profondeur de sondage atteinte par la colonie dans chaque région connue (0 : simple visite).</summary>
    public Dictionary<int, int> RegionReach => _regionReach ??= [];
    private List<int>? _visitedRegions;
    public List<int> VisitedRegions => _visitedRegions ??= [];
    internal long LastTerritorialTicks { get; set; }
    /// <summary>Les adaptateurs physiques de Colony désignent seulement cet établissement.</summary>
    public Settlement PrimarySettlement
    {
        get
        {
            if (Settlements.Count == 0) Settlements.Add(new Settlement(this, 0, 0, []));
            return PrimarySettlementId == 0 ? Settlements[0] : Settlements.Single(s => s.Id == PrimarySettlementId);
        }
    }
    /// <summary>Citoyens locaux et citoyens en mission, sans compter deux fois un participant.</summary>
    public IEnumerable<Colonist> Citizens => Members;
    /// <summary>Les citoyens physiquement présents dans l'établissement principal.</summary>
    [NonSerialized] private Settlement? _localSettlement;
    internal Settlement LocalSettlement => _localSettlement ?? PrimarySettlement;
    public Settlement CurrentSettlement => LocalSettlement;
    internal LocalScope UseSettlement(Settlement settlement) => new(this, settlement);
    internal readonly struct LocalScope : IDisposable
    {
        private readonly Colony _owner;
        private readonly Settlement? _previous;
        internal LocalScope(Colony owner, Settlement settlement)
        {
            if (settlement.Owner != owner) throw new ArgumentException("Établissement d'un autre peuple.");
            _owner = owner; _previous = owner._localSettlement; owner._localSettlement = settlement;
        }
        public void Dispose() => _owner._localSettlement = _previous;
    }
    public PresentPopulation PresentMembers => LocalSettlement.Population;

    /// <summary>Les 24 derniers événements, avec position et issue exactes ; effets visuels temporaires.</summary>
    public IReadOnlyList<RecentEvent> RecentEvents => RecentEventHistory.For(this);
    internal void RecordEvent(RecentEvent report)
    {
        List<RecentEvent> reports = RecentEventHistory.For(this);
        reports.Add(report);
        if (reports.Count > 24) reports.RemoveAt(0);
    }

    /// <summary>L'espèce qui peuple la colonie (celle des fondateurs et des voyageurs).</summary>
    public Species Species { get; internal set; } = Species.Human;

    /// <summary>La carte locale de la colonie : chaque colonie a la sienne, façonnée par son environnement.</summary>
    public GodColony.Simulation.Map.LocalMap Map { get => LocalSettlement.Map; internal set => LocalSettlement.Map = value; }

    public GodColony.Simulation.Pathfinding.Pathfinder Pathfinder { get => LocalSettlement.Pathfinder; internal set => LocalSettlement.Pathfinder = value; }

    /// <summary>
    /// Le plan du village : ses quartiers, ses parcelles, ses projets et ses tracés (voir <see cref="SettlementLayout"/>). Il est créé à
    /// la fondation et à la migration d'une ancienne sauvegarde ; il n'est jamais nul sur une colonie vivante.
    /// </summary>
    public SettlementLayout Layout
    {
        get => _layout ??= SettlementPlanner.InitializeLayout(this, Map);
        internal set => _layout = value;
    }
    private SettlementLayout? _layout { get => LocalSettlement.LayoutState; set => LocalSettlement.LayoutState = value; }

    /// <summary>
    /// La file de recherches d'emplacement du monde, partagée par toutes les colonies (null pour une colonie bâtie à la main hors d'un monde : ses demandes se résolvent alors sur place).
    /// </summary>
    internal SettlementPlanningState? Planning { get; set; }

    /// <summary>Le registre monétaire du monde, partagé par toutes les colonies (null hors d'un monde : la frappe est alors impossible).</summary>
    internal MonetaryLedger? Ledger { get; set; }

    // Créées dès la construction : une lecture (validation du chargement) ne doit pas allouer et faire diverger deux mondes identiques.
    private List<OfferingProject>? _offerings = [];
    private List<Monument>? _monuments = [];
    private List<DivineWish>? _wishes = [];
    private int _nextOfferingId = 1, _nextMonumentId = 1, _nextWishId = 1;

    /// <summary>Les projets d'offrande de la colonie (tous établissements), y compris achevés ou abandonnés.</summary>
    public List<OfferingProject> Offerings => _offerings ??= [];

    /// <summary>Les offrandes achevées : des monuments qui gardent les matériaux investis.</summary>
    public List<Monument> Monuments => _monuments ??= [];

    /// <summary>Les souhaits adressés au joueur, avec leur état de réponse.</summary>
    public List<DivineWish> Wishes => _wishes ??= [];

    /// <summary>Moment où la dernière offrande s'est achevée ou a été abandonnée : la colonie n'en lance pas une autre aussitôt.</summary>
    public long LastOfferingTicks { get; internal set; } = long.MinValue / 2;
    /// <summary>Un établissement transféré par un schisme apporte ses projets, monuments et souhaits : les identifiants restent uniques dans la colonie d'accueil.</summary>
    internal void AdoptOfferings(IEnumerable<OfferingProject> projects, IEnumerable<Monument> monuments, IEnumerable<DivineWish> wishes)
    {
        Offerings.AddRange(projects); Monuments.AddRange(monuments); Wishes.AddRange(wishes);
        _nextOfferingId = Math.Max(_nextOfferingId, Offerings.Select(p => p.Id).DefaultIfEmpty(0).Max() + 1);
        _nextMonumentId = Math.Max(_nextMonumentId, Monuments.Select(m => m.Id).DefaultIfEmpty(0).Max() + 1);
        _nextWishId = Math.Max(_nextWishId, Wishes.Select(w => w.Id).DefaultIfEmpty(0).Max() + 1);
    }

    internal int NextOfferingId() => _nextOfferingId++;
    internal int NextMonumentId() => _nextMonumentId++;
    internal int NextWishId() => _nextWishId++;

    /// <summary>
    /// L'occupation de la grille (bâtiments, champs, tombes, canaux, parcelles, accès), reconstruite au besoin depuis les objets de la
    /// simulation : un cache, jamais sauvegardé. Il se remet à jour tout seul quand un objet est ajouté ou retiré.
    /// </summary>
    public GodColony.Simulation.Map.LocalSpatialIndex Spatial
    {
        get
        {
            _spatial ??= new GodColony.Simulation.Map.LocalSpatialIndex(Map.Width, Map.Height);
            _spatial.Refresh(this);
            return _spatial;
        }
    }
    private GodColony.Simulation.Map.LocalSpatialIndex? _spatial { get => LocalSettlement.SpatialCache; set => LocalSettlement.SpatialCache = value; }

    /// <summary>Les lieux de repas, de rencontre et de dépôt, dérivés des bâtiments achevés (un cache, jamais sauvegardé).</summary>
    internal ServiceCache? ServiceCache { get => LocalSettlement.ServiceCache; set => LocalSettlement.ServiceCache = value; }

    public Building? BuildingById(int id)
    {
        foreach (Building building in Buildings)
            if (building.Id == id)
                return building;
        return null;
    }

    public Field? FieldById(int id)
    {
        foreach (Field field in Fields)
            if (field.Id == id)
                return field;
        return null;
    }

    /// <summary>L'horloge du monde, pour calculer les âges.</summary>
    internal GodColony.Simulation.Time.GameClock Clock { get; set; } = new(0);

    /// <summary>Ceux qui sont morts dans la colonie, avec leur tombe.</summary>
    public List<Grave> Graves { get => LocalSettlement.Graves; }

    /// <summary>Les colons qui travaillent : tout le monde sauf les enfants.</summary>
    public IEnumerable<Colonist> Workers => PresentMembers.Where(m => m.Stage != LifeStage.Child);

    public int Children => Members.Count(m => m.Stage == LifeStage.Child);

    /// <summary>Le feu de camp, cœur de la colonie : on y mange, on s'y détend, on dort autour.</summary>
    public int CampX { get => LocalSettlement.CampX; }
    public int CampY { get => LocalSettlement.CampY; }

    /// <summary>Cases accessibles autour du feu, des plus proches aux plus lointaines.</summary>
    public IReadOnlyList<(int X, int Y)> GatherSpots { get => LocalSettlement.GatherSpots; }

    /// <summary>Le point de la montagne où la colonie a ouvert sa carrière (null s'il n'y a pas de roche accessible).</summary>
    public (int X, int Y)? Quarry { get => LocalSettlement.Quarry; internal set => LocalSettlement.Quarry = value; }

    public List<Colonist> Members { get; } = [];
    public Stockpile Stock { get => LocalSettlement.Stock; }

    /// <summary>Ce que coûte chaque ressource en heures de travail.</summary>
    public LaborLedger Labor { get => LocalSettlement.Labor; }

    /// <summary>Part de la main-d'œuvre consacrée à chaque secteur (la somme vaut 1). Le cerveau l'ajuste chaque heure.</summary>
    public Dictionary<WorkSector, float> WorkShares => LocalSettlement.WorkShares;

    /// <summary>Les bâtiments de la colonie, achevés ou en chantier.</summary>
    public List<Building> Buildings { get => LocalSettlement.Buildings; }

    /// <summary>
    /// Ce que la colonie fabriquerait volontiers en plus pour ses voisines (des outils, par exemple) : c'est le commerce
    /// qui oriente sa production, donc sa spécialisation.
    /// </summary>
    public Dictionary<ResourceType, int> ExportInterest { get; } = [];

    /// <summary>Les derniers voyages de caravane, envoyés ou reçus, du plus ancien au plus récent.</summary>
    public List<TradeRecord> Trades { get; } = [];

    /// <summary>Travail épargné au total grâce au commerce, en heures (somme des gains attendus des voyages réussis).</summary>
    public double LifetimeTradeGainHours { get; internal set; }

    /// <summary>Dernier départ d'une de ses caravanes.</summary>
    internal long LastCaravanTicks { get; set; } = long.MinValue / 2;

    /// <summary>La colonie située en aval, sur le même fleuve : ce que celle-ci retient lui manque (null s'il n'y en a pas).</summary>
    public Colony? Downstream { get; internal set; }

    /// <summary>
    /// La rancune envers d'autres colonies, de 0 (rien) à 3 : un barrage qui assèche notre rivière en fait naître.
    /// Elle s'estompe lentement et rend les échanges moins attrayants.
    /// </summary>
    public Dictionary<Colony, float> Grudges { get; } = [];

    public float GrudgeAgainst(Colony other) => Grudges.GetValueOrDefault(other);

    /// <summary>Les décisions que la colonie soumet au joueur.</summary>
    public PrayerBook Prayers => _prayers ??= new PrayerBook(this);
    private PrayerBook? _prayers;

    /// <summary>Les canaux d'irrigation, achevés ou en chantier.</summary>
    public List<Canal> Canals { get => LocalSettlement.Canals; }

    /// <summary>Toutes les cases de canal prévues (creusées ou non) : on n'y bâtit rien et on n'y sème pas.</summary>
    internal HashSet<(int X, int Y)> CanalTiles { get => LocalSettlement.CanalTiles; }

    /// <summary>Les canaux qu'il reste à creuser.</summary>
    public IEnumerable<Canal> CanalsInProgress => Canals.Where(c => !c.IsComplete);

    /// <summary>Les ateliers achevés d'un type donné.</summary>
    public IEnumerable<Building> Workshops(BuildingType type) => Buildings.Where(b => b.Type == type && b.IsComplete);

    /// <summary>
    /// Des postes de mineur où aucun chemin n'a mené aujourd'hui (roche isolée sur un plateau) : on passe aux suivants
    /// au lieu de rester bloqué sur les plus proches. Vidé chaque jour, car la carrière change.
    /// </summary>
    internal HashSet<(int X, int Y)> UnreachableStands { get => LocalSettlement.UnreachableStands; }

    /// <summary>Recherches qui n'ont rien donné, avec le nombre de jours avant de les retenter (une année).</summary>
    private Dictionary<SearchKind, int> _exhaustedDaysLeft => LocalSettlement.ExhaustedDaysLeft;

    internal bool IsExhausted(SearchKind kind) => _exhaustedDaysLeft.ContainsKey(kind);

    internal void MarkExhausted(SearchKind kind) => _exhaustedDaysLeft[kind] = Time.TimeConstants.DaysPerYear;

    /// <summary>Oublie les échecs liés à la carrière (elle vient de changer de place).</summary>
    internal void ForgetQuarrySearches()
    {
        _exhaustedDaysLeft.Remove(SearchKind.Rocks);
        _exhaustedDaysLeft.Remove(SearchKind.Ore);
    }

    /// <summary>Un jour de plus : les recherches infructueuses vieillissent et sont retentées au bout d'un an.</summary>
    internal void AgeExhaustedSearches()
    {
        foreach (SearchKind kind in _exhaustedDaysLeft.Keys.ToList())
            if (--_exhaustedDaysLeft[kind] <= 0)
                _exhaustedDaysLeft.Remove(kind);
    }

    /// <summary>Dernière fois que la carrière a été déplacée (ou jugée épuisée).</summary>
    internal long LastQuarryMoveTicks { get => LocalSettlement.LastQuarryMoveTicks; set => LocalSettlement.LastQuarryMoveTicks = value; }

    /// <summary>Un filon de fer a été aperçu à la carrière : la colonie sait qu'il y a du minerai à portée.</summary>
    public bool IronSeen { get => LocalSettlement.IronSeen; internal set => LocalSettlement.IronSeen = value; }

    /// <summary>Les produits dont la colonie a déjà annoncé la première fabrication.</summary>
    internal HashSet<ResourceType> AnnouncedProducts { get; } = [];

    /// <summary>Usure des outils : à chaque fois qu'elle atteint 1, un outil casse.</summary>
    internal float ToolWear { get => LocalSettlement.ToolWear; set => LocalSettlement.ToolWear = value; }

    /// <summary>Les champs de la colonie.</summary>
    public List<Field> Fields { get => LocalSettlement.Fields; }

    // --- Élevage et textile ---

    public int Chickens { get => LocalSettlement.Chickens; internal set => LocalSettlement.Chickens = value; }
    public int Sheep { get => LocalSettlement.Sheep; internal set => LocalSettlement.Sheep = value; }
    public int Cows { get => LocalSettlement.Cows; internal set => LocalSettlement.Cows = value; }

    /// <summary>Œufs, laine et lait qui attendent à l'enclos qu'on vienne les ramasser.</summary>
    public float EggsReady { get => LocalSettlement.EggsReady; internal set => LocalSettlement.EggsReady = value; }
    public float WoolReady { get => LocalSettlement.WoolReady; internal set => LocalSettlement.WoolReady = value; }
    public float MilkReady { get => LocalSettlement.MilkReady; internal set => LocalSettlement.MilkReady = value; }

    internal float ChickenGrowth { get => LocalSettlement.ChickenGrowth; set => LocalSettlement.ChickenGrowth = value; }
    internal float SheepGrowth { get => LocalSettlement.SheepGrowth; set => LocalSettlement.SheepGrowth = value; }
    internal float CowGrowth { get => LocalSettlement.CowGrowth; set => LocalSettlement.CowGrowth = value; }

    /// <summary>Les bêtes que la colonie a décidé d'abattre aujourd'hui (espèce → nombre), voir <see cref="Husbandry.PlanSlaughter"/>.</summary>
    public Dictionary<ResourceType, int> SlaughterOrders { get => LocalSettlement.SlaughterOrders; }

    internal long LastMeatThoughtDay { get => LocalSettlement.LastMeatThoughtDay; set => LocalSettlement.LastMeatThoughtDay = value; }

    /// <summary>Usure des vêtements : à chaque fois qu'elle atteint 1, un vêtement est perdu.</summary>
    internal float ClothesWear { get => LocalSettlement.ClothesWear; set => LocalSettlement.ClothesWear = value; }

    // --- Denrées de négoce ---

    internal float SaltUse { get => LocalSettlement.SaltUse; set => LocalSettlement.SaltUse = value; }
    internal float SpiceUse { get => LocalSettlement.SpiceUse; set => LocalSettlement.SpiceUse = value; }

    // --- Santé, saisons, événements ---

    /// <summary>Nombre de fièvres depuis la fondation : l'infirmerie devient une urgence.</summary>
    public int IllnessCases { get => LocalSettlement.IllnessCases; internal set => LocalSettlement.IllnessCases = value; }

    /// <summary>Jours restants d'une vague de froid (hiver) ou d'une sécheresse (été), 0 s'il n'y en a pas.</summary>
    public int ColdSnapDaysLeft { get => LocalSettlement.ColdSnapDaysLeft; internal set => LocalSettlement.ColdSnapDaysLeft = value; }
    public int DroughtDaysLeft { get => LocalSettlement.DroughtDaysLeft; internal set => LocalSettlement.DroughtDaysLeft = value; }

    internal long LastHealthThoughtDay { get => LocalSettlement.LastHealthThoughtDay; set => LocalSettlement.LastHealthThoughtDay = value; }
    internal long LastSpoilageThoughtDay { get => LocalSettlement.LastSpoilageThoughtDay; set => LocalSettlement.LastSpoilageThoughtDay = value; }

    /// <summary>Les jalons atteints (identifiant → moment), voir <see cref="Milestones"/>.</summary>
    public Dictionary<string, long> Achievements { get; } = [];

    // --- Savoirs (voir Knowledge) ---

    /// <summary>Les savoirs connus, avec le moment de leur découverte.</summary>
    public Dictionary<Discovery, long> Known { get; } = [];

    /// <summary>Points de savoir déjà accumulés sur les savoirs entamés.</summary>
    public Dictionary<Discovery, float> ResearchProgress { get; } = [];

    /// <summary>Le savoir que la colonie étudie en ce moment (null si elle sait tout ce qu'elle peut aborder).</summary>
    public Discovery? Researching { get; internal set; }

    // --- Relations avec les autres colonies (voir Diplomacy) ---

    /// <summary>Ce que la colonie pense de chacune des autres, de -100 (haine) à +100 (amitié).</summary>
    public Dictionary<Colony, float> Opinions { get; } = [];

    public float OpinionOf(Colony other) => Opinions.GetValueOrDefault(other);

    /// <summary>La colonie dont celle-ci est issue par un schisme (null pour une fondation).</summary>
    public Colony? Parent { get; internal set; }

    /// <summary>Lassitude de la guerre : elle monte avec les jours de guerre et les morts, et retombe en paix.</summary>
    public float WarWeariness { get; internal set; }

    /// <summary>Batailles gagnées et perdues dans la guerre en cours (remises à zéro à la paix).</summary>
    public int BattlesWon { get; internal set; }
    public int BattlesLost { get; internal set; }

    /// <summary>Dernier départ d'une bande de guerriers, dernier cadeau envoyé à chaque voisine.</summary>
    internal long LastWarPartyTicks { get; set; } = long.MinValue / 2;
    internal Dictionary<Colony, long> LastGiftTicks { get; } = [];

    /// <summary>Temps passé à semer, pour que le coût des céréales inclue les semailles.</summary>
    private long _sowTicks { get => LocalSettlement._sowTicks; set => LocalSettlement._sowTicks = value; }
    private int _plotsSown { get => LocalSettlement._plotsSown; set => LocalSettlement._plotsSown = value; }

    internal void RecordSowing(long ticks)
    {
        _sowTicks += ticks;
        _plotsSown++;
    }

    /// <summary>Heures de travail qu'il faut pour semer une parcelle (0 tant qu'on n'a rien semé).</summary>
    public double SowHoursPerPlot => _plotsSown == 0 ? 0 : LaborLedger.TicksToHours(_sowTicks) / _plotsSown;

    public IEnumerable<Building> ConstructionSites => Buildings.Where(b => !b.IsComplete);

    /// <summary>Colons qui n'ont pas de hutte et dorment à la belle étoile.</summary>
    public int Homeless => PresentMembers.Count(m => m.Home is null);

    /// <summary>
    /// Ceux qui arrivent ou s'en vont : ils marchent entre le bord de la carte et le camp,
    /// sans faire partie des membres (ni de leurs statistiques).
    /// </summary>
    public List<Colonist> Transients { get => LocalSettlement.Transients; }

    /// <summary>Dernière fois qu'une amitié ou une rivalité a été notée dans les pensées.</summary>
    internal long LastSocialThoughtTicks { get => LocalSettlement.LastSocialThoughtTicks; set => LocalSettlement.LastSocialThoughtTicks = value; }

    /// <summary>Dernier jour où l'on a dû refuser un voyageur (pour ne pas radoter dans les pensées).</summary>
    internal long LastRefusalDay { get => LocalSettlement.LastRefusalDay; set => LocalSettlement.LastRefusalDay = value; }

    /// <summary>Les places libres des huttes achevées vont aux colons qui dormaient dehors.</summary>
    internal void FillVacancies()
    {
        foreach (Building building in Buildings.Where(b => b.IsComplete && b.IsHut))
        foreach (Colonist colonist in PresentMembers.Where(m => m.Home is null).Take(Building.HutCapacity - building.Residents.Count).ToList())
        {
            colonist.Home = building;
            building.Residents.Add(colonist);
        }
    }

    /// <summary>Dernières mesures du cerveau de la colonie (null avant sa première réflexion).</summary>
    public ColonySensors? Sensors { get => LocalSettlement.Sensors; internal set => LocalSettlement.Sensors = value; }

    /// <summary>Ce que la colonie pense et décide, en langage clair, du plus ancien au plus récent.</summary>
    public List<Thought> Thoughts { get; } = [];

    /// <summary>Le feu brûle-t-il cette nuit ? Sans feu en saison froide, on dort mal.</summary>
    public bool FireLit { get => LocalSettlement.FireLit; internal set => LocalSettlement.FireLit = value; }

    /// <summary>Dernier état annoncé pour chaque sujet, pour ne parler que lorsque la situation change.</summary>
    internal Dictionary<string, ColonyBrain.NarrationTopic> NarrationState { get => LocalSettlement.NarrationState; }

    /// <summary>Cases déjà prises en charge par un colon (un buisson qu'il va cueillir, par exemple).</summary>
    internal HashSet<(int X, int Y)> Reserved { get => LocalSettlement.Reserved; }

    /// <summary>Chaque colon a sa place pour dormir : dans sa hutte s'il en a une, sinon autour du feu.</summary>
    public (int X, int Y) SleepSpot(Colonist colonist) =>
        colonist.Home is { } home ? home.BedOf(colonist) : GatherSpots[(1 + PresentMembers.IndexOf(colonist)) % GatherSpots.Count];

    public float AverageMood => Members.Count == 0 ? 0f : Members.Average(m => m.Needs.Mood);

    /// <summary>
    /// Affecte chaque colon à un secteur selon les parts voulues, en confiant chaque poste au plus compétent.
    /// Un colon garde de préférence son secteur actuel, pour éviter qu'il change de métier sans arrêt.
    /// </summary>
    public void AssignSectors()
    {
        var workers = new List<Colonist>(PresentMembers.Count);
        foreach (Colonist member in PresentMembers)
        {
            if (member.Stage == LifeStage.Child)
                member.Sector = WorkSector.Free;
            else
                workers.Add(member);
        }
        Dictionary<WorkSector, int> quotas = ComputeQuotas(workers.Count);

        // Chaque couple (colon, secteur), du plus apte au moins apte ; à aptitude égale, dans l'ordre des colons puis des secteurs.
        // Le temps libre revient à ceux qui restent une fois les postes productifs pourvus.
        WorkSector[] sectors = WorkSectors.All;
        var candidates = new (float Fit, int Index)[workers.Count * sectors.Length];
        for (int w = 0; w < workers.Count; w++)
        for (int s = 0; s < sectors.Length; s++)
        {
            Colonist colonist = workers[w];
            candidates[w * sectors.Length + s] =
                (sectors[s].Fitness(colonist.Skills) + (colonist.Sector == sectors[s] ? 3f : 0f), w * sectors.Length + s);
        }
        Array.Sort(candidates, (a, b) => b.Fit.CompareTo(a.Fit) is var byFit and not 0 ? byFit : a.Index.CompareTo(b.Index));

        var assigned = new bool[workers.Count];
        foreach ((float _, int index) in candidates)
        {
            int w = index / sectors.Length;
            WorkSector sector = sectors[index % sectors.Length];
            if (assigned[w] || quotas[sector] <= 0)
                continue;
            workers[w].Sector = sector;
            quotas[sector]--;
            assigned[w] = true;
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
