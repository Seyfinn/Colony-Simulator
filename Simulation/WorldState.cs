using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation;

/// <summary>
/// L'état complet de la simulation. Godot ne fait que le lire pour l'afficher.
/// </summary>
public sealed partial class WorldState
{
    /// <summary>La partie commence à 8 h du matin, au premier jour du printemps.</summary>
    private const long StartTicks = TimeConstants.TicksPerDay * 8 / 24;

    private int _nextColonistId = 1;
    private readonly int _mapWidth, _mapHeight;
    private readonly LocalMap? _emptyMap;

    public int Seed { get; }
    public const int MaxPlayerColonies = 16;

    /// <summary>Les voyageurs et les départs sont-ils actifs ? (On peut les couper pour étudier une colonie fermée.)</summary>
    private readonly bool _migration;

    /// <summary>Les âges, les couples, les naissances et la mort sont-ils actifs ?</summary>
    private readonly bool _lifecycle;

    /// <summary>Le commerce entre colonies est-il actif ? (On peut le couper pour étudier des colonies isolées.)</summary>
    private readonly bool _trade;

    /// <summary>Écart entre les graines des cartes locales de deux cases du monde.</summary>
    private const int MapSeedStep = 7919;

    /// <summary>La graine de la carte locale d'une case : une même case donne toujours la même région.</summary>
    private int RegionSeed(int tile) => unchecked(Seed + (tile + 1) * MapSeedStep);

    private static string ColonyName(int index, Species species) =>
        index == 0 ? "Première colonie" : $"Colonie {species.Adjective}";

    internal int NextColonistId() => _nextColonistId++;

    public GameClock Clock { get; }
    public List<Colony> Colonies { get; } = [];

    /// <summary>La carte du monde (cases, biomes, fleuves) et la case de chaque colonie.</summary>
    public WorldMap WorldMap { get; }

    /// <summary>
    /// Les recherches d'emplacement en cours de toutes les colonies, avec leur budget partagé (voir <see cref="SettlementPlanningScheduler"/>).
    /// Le monde ne connaît pas la vitesse d'affichage : les mêmes ticks donnent les mêmes décisions à ×1 comme à ×200.
    /// </summary>
    public SettlementPlanningState Planning { get; internal set; } = new();

    /// <summary>Les caravanes en route entre deux colonies.</summary>
    public List<Caravan> Caravans { get; } = [];

    private List<SupplierMemory>? _supplierMemories = [];
    public List<SupplierMemory> SupplierMemories => _supplierMemories ??= [];

    /// <summary>Les alliances, guerres et trêves entre colonies (voir <see cref="Diplomacy"/>).</summary>
    public List<Pact> Pacts { get; } = [];

    /// <summary>Les bandes de guerriers en marche vers une colonie ennemie, ou qui en reviennent.</summary>
    public List<WarParty> WarParties { get; } = [];

    /// <summary>Pièces qui ont quitté le monde par les événements (pillards, colporteurs) : la monnaie ne se perd pas autrement.</summary>
    public int CoinsLostToEvents { get; internal set; }

    /// <summary>Le bilan monétaire : dotations, frappe et quota annuel partagé (voir <see cref="MonetaryLedger"/>).</summary>
    public MonetaryLedger Money { get; private set; } = new();

    /// <summary>Les plafonds et seuils territoriaux de cette partie (voir <see cref="TerritorialRules"/>).</summary>
    public TerritorialRules Territory { get; private set; } = new();

    /// <summary>Nombre de voyages de caravane menés à leur terme depuis le début de la partie.</summary>
    public int CompletedCaravans { get; internal set; }

    /// <summary>La carte de la première colonie (la seule, dans une partie à une colonie). Chaque colonie a la sienne.</summary>
    public LocalMap Map => Colonies.Count > 0 ? Colonies[0].Map : _emptyMap!;

    public Pathfinder Pathfinder => Colonies[0].Pathfinder;

    /// <summary>Tout le hasard de la simulation passe par ici : une même graine rejoue la même histoire.</summary>
    public Random Random { get; }

    /// <summary>
    /// Le hasard des maladies, du climat et des événements, à part de <see cref="Random"/> : ajouter ou régler ces aléas
    /// ne décale pas le reste de l'histoire d'une graine.
    /// </summary>
    public Random Chance { get; }

    /// <summary>Le hasard de la politique (querelles, présents, batailles, schismes), à part lui aussi.</summary>
    public Random Politics { get; }

    /// <param name="startingColonists">Nombre de colons fondateurs (par colonie) ; tiré au hasard entre 5 et 10 si l'on n'en précise pas.</param>
    /// <param name="migration">Faux pour couper les arrivées de voyageurs et les départs.</param>
    /// <param name="trade">Faux pour que les colonies n'échangent rien.</param>
    /// <param name="colonyCount">
    /// Nombre de colonies. Chacune a son espèce (humains, puis nains, elfes, orcs) et s'installe sur la case du monde
    /// qui plaît le plus à son peuple ; sa carte locale est générée à partir de cette case (biome, relief, fleuve, côte).
    /// </param>
    public WorldState(int seed, int mapWidth = MapGenerator.DefaultSize, int mapHeight = MapGenerator.DefaultSize, int? startingColonists = null, bool migration = true, bool lifecycle = true, int colonyCount = 1, bool trade = true)
    {
        Seed = seed;
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _trade = trade;
        _migration = migration;
        _lifecycle = lifecycle;
        Random = new Random(seed);
        Chance = new Random(unchecked(seed * 31 + 0x5EED));
        Politics = new Random(unchecked(seed * 47 + 0x7EA7));
        Clock = new GameClock(StartTicks);
        WorldMap = new WorldMap(WorldGenerator.Generate(seed));
        if (colonyCount == 0)
            _emptyMap = GenerateColonyMap(WorldMap.SuggestTile(Species.Human));
        var peoples = Enumerable.Range(0, colonyCount).Select(i => Species.All[i % Species.All.Count]).ToList();
        List<int> tiles = WorldMap.ChooseStartingTiles(peoples);
        for (int i = 0; i < colonyCount; i++)
        {
            Species species = peoples[i];
            LocalMap map = GenerateColonyMap(tiles[i]);
            int founders = startingColonists
                ?? Random.Next(ColonyFounder.MinStartingColonists, ColonyFounder.MaxStartingColonists + 1);
            Colony colony = ColonyFounder.Found(map, Random, ColonyName(i, species), founders, NextColonistId, Clock, species);
            Money.Dotations += ColonyFounder.StartingCoins;
            WorldMap.PlaceAt(colony, tiles[i]);
            colony.Planning = Planning;
            colony.Ledger = Money;
            Colonies.Add(colony);
            RegisterSettlement(colony, tiles[i]);
        }
        UpdateRivers();
        foreach (Colony colony in Colonies)
            FoundingThink(colony);
        Money.StartYear(this);
    }

    /// <summary>
    /// La première pensée d'une colonie fondée : son plan initial (premiers champs, première hutte) se résout sur place, sans attendre les rendez-vous du planificateur,
    /// comme le faisait la fondation avant les quartiers. Ensuite, tout passe par la file de recherches partagée.
    /// </summary>
    private void FoundingThink(Colony colony)
    {
        colony.Planning = null;
        ColonyBrain.Think(colony, colony.Map, Clock);
        colony.Planning = Planning;
    }

    /// <summary>
    /// La carte locale d'une case du monde : son terrain suit le biome, le relief, le fleuve et la côte de la case.
    /// Ne modifie rien et ne consomme pas le hasard de la vie des habitants.
    /// </summary>
    public LocalMap GenerateColonyMap(int tile) => Regions.TryGetValue(tile, out RegionState? region) ? region.Map
        : MapGenerator.Generate(_mapWidth, _mapHeight, RegionSeed(tile), MapStyle.For(WorldMap.Grid[tile]));

    /// <summary>Qui est en aval de qui : suit les fleuves de la carte du monde.</summary>
    internal void UpdateRivers()
    {
        foreach (Colony colony in Colonies)
            colony.Downstream = WorldMap.DownstreamOf(colony);
        // L'aval est territorial : chaque établissement a son voisin le plus proche en aval, même au sein d'une seule colonie.
        foreach (Settlement place in Settlements)
            place.Downstream = place.Status == SettlementStatus.Closed || place.RegionTileIndex < 0 ? null
                : WorldMap.DownstreamTile(place.RegionTileIndex, tile => Settlements.Any(s => s.RegionTileIndex == tile && s.Status != SettlementStatus.Closed && s != place)) is { } tile
                    ? Settlements.First(s => s.RegionTileIndex == tile && s.Status != SettlementStatus.Closed && s != place) : null;
    }

    /// <summary>Fonde une colonie en cours de partie sur la case <paramref name="tile"/> du monde ; une erreur ne modifie aucun état.</summary>
    public bool TryFoundColony(LocalMap map, int campX, int campY, string name, Species species,
        int founders, int tile, out Colony? colony, out string reason)
    {
        colony = null;
        name = name.Trim();
        if (Colonies.Count >= MaxPlayerColonies)
            reason = $"Le monde accueille au maximum {MaxPlayerColonies} colonies.";
        else if (name.Length is < 1 or > 40 || name.Any(char.IsControl))
            reason = "Donnez un nom de 1 à 40 caractères à la colonie.";
        else if (Colonies.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            reason = "Une colonie porte déjà ce nom.";
        else if (!Species.All.Contains(species))
            reason = "Choisissez un peuple disponible.";
        else if (founders < ColonyFounder.MinStartingColonists || founders > ColonyFounder.MaxPlayerFounders)
            reason = $"Choisissez de {ColonyFounder.MinStartingColonists} à {ColonyFounder.MaxPlayerFounders} fondateurs.";
        else if (map.Width != _mapWidth || map.Height != _mapHeight || Settlements.Any(s => ReferenceEquals(s.Map, map))
            || Regions.TryGetValue(tile, out RegionState? claimedRegion) && claimedRegion.OwnerColonyId is not null)
            reason = "Cette région est déjà occupée ou ne correspond pas à la taille du monde.";
        else if (!WorldMap.CanSettle(tile, out reason) || !ColonyFounder.CanFoundAt(map, campX, campY, out reason))
            return false;
        else
        {
            colony = ColonyFounder.FoundAt(map, Random, name, founders, NextColonistId, Clock, species, campX, campY);
            Money.Dotations += ColonyFounder.StartingCoins;
            AddColony(colony, tile);
            reason = $"{colony.Name} a été fondée avec {founders} habitants.";
            return true;
        }
        return false;
    }

    /// <summary>Installe une colonie déjà peuplée sur sa case du monde (une fondation, un schisme).</summary>
    internal void AddColony(Colony colony, int tile)
    {
        WorldMap.PlaceAt(colony, tile);
        colony.Planning = Planning;
        colony.Ledger = Money;
        Colonies.Add(colony);
        RegisterSettlement(colony, tile);
        UpdateRivers();
        FoundingThink(colony);
    }

    /// <summary>Le joueur répond à une prière : accord ou refus.</summary>
    public void AnswerPrayer(Prayer prayer, bool approve) => prayer.Colony.Prayers.Answer(prayer, approve, Clock);

    /// <summary>Avance la simulation d'un tick.</summary>
    public void Step()
    {
        long day = Clock.TotalDays;
        int hour = Clock.Hour;
        Clock.Advance();
        SettlementPlanningScheduler.Tick(this);
        if (Clock.TotalDays != day)
        {
            if (Clock.Year != Money.Year) Money.StartYear(this);
            foreach (Settlement settlement in Settlements.Where(s => s.Status == SettlementStatus.Active).ToArray())
            {
                Colony colony = settlement.Owner;
                using var scope = colony.UseSettlement(settlement);
                colony.Map.DailyUpdate(Clock.TotalDays, Clock.Season);
                RoadDevelopment.OnDayStart(colony);
                ColonyBrain.OnDayStart(colony, Clock);
                TerritorialTravel.Daily(this, settlement);
                ColonistAI.AgeCraftInputs(colony);
                if (settlement == colony.PrimarySettlement) { Knowledge.Daily(this, colony); Offerings.Daily(colony); }
                if (_lifecycle)
                {
                    Lifecycle.Daily(this, colony);
                    Climate.Daily(this, colony);
                    Health.Daily(this, colony);
                    Events.Daily(this, colony);
                }
            }
        }

        if (Clock.Hour != hour)
        {
            foreach (Settlement settlement in Settlements.Where(s => s.Status == SettlementStatus.Active).ToArray())
            {
                Colony colony = settlement.Owner;
                using var scope = colony.UseSettlement(settlement);
                colony.Stock.ExpireReservations(Clock.Ticks);
                ColonyBrain.Think(colony, colony.Map, Clock);
                Offerings.Hourly(this, colony);
                if (Clock.Hour == FireLightingHour)
                    ColonyBrain.LightFire(colony, Clock);
                if (_lifecycle)
                {
                    Lifecycle.Hourly(this, colony);
                    Health.Hourly(this, colony);
                }
                if (Clock.Hour == Trade.PlanningHour && _trade && settlement == colony.PrimarySettlement)
                    Trade.Daily(this, colony);
                if (_migration && settlement == colony.PrimarySettlement)
                {
                    Migration.Hourly(this, colony);
                    if (Clock.Hour == Migration.ArrivalHour)
                        Migration.Daily(this, colony);
                }
            }
            if (Clock.Hour == Diplomacy.PlanningHour)
            {
                Diplomacy.Daily(this);
                if (_migration)
                    foreach (Colony colony in Colonies.ToList())
                        Schism.Daily(this, colony);
            }
            if (WarParties.Count > 0)
                Warfare.Hourly(this);
        }

        // Les caravanes déjà en route continuent même si l'on coupe le commerce pour de nouveaux départs.
        if (Clock.Hour != hour && Caravans.Count > 0)
            Trade.Hourly(this);

        foreach (Settlement settlement in Settlements.Where(s => s.Status == SettlementStatus.Active).ToArray())
        {
            Colony colony = settlement.Owner;
            using var scope = colony.UseSettlement(settlement);
            foreach (Colonist colonist in colony.PresentMembers.ToArray())
                ColonistAI.Tick(colonist, this);
            if (colony.Transients.Count > 0)
                foreach (Colonist traveler in colony.Transients.ToArray())
                    ColonistAI.TickTransient(traveler, this);
        }
    }

    /// <summary>On allume le feu pour la nuit à 20 h.</summary>
    private const int FireLightingHour = 20;
}
