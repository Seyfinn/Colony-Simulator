using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;
using GodColony.Simulation.Time;

namespace GodColony.Simulation;

/// <summary>
/// L'état complet de la simulation. Godot ne fait que le lire pour l'afficher.
/// </summary>
public sealed class WorldState
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

    /// <summary>Écart entre les graines des cartes de deux colonies.</summary>
    private const int MapSeedStep = 7919;

    private static string ColonyName(int index, Species species) =>
        index == 0 ? "Première colonie" : $"Colonie {species.Adjective}";

    internal int NextColonistId() => _nextColonistId++;

    public GameClock Clock { get; }
    public List<Colony> Colonies { get; } = [];

    /// <summary>Où sont les colonies les unes par rapport aux autres.</summary>
    public WorldMap WorldMap { get; } = new();

    /// <summary>Les caravanes en route entre deux colonies.</summary>
    public List<Caravan> Caravans { get; } = [];

    /// <summary>Nombre de voyages de caravane menés à leur terme depuis le début de la partie.</summary>
    public int CompletedCaravans { get; internal set; }

    /// <summary>La carte de la première colonie (la seule, dans une partie à une colonie). Chaque colonie a la sienne.</summary>
    public LocalMap Map => Colonies.Count > 0 ? Colonies[0].Map : _emptyMap!;

    public Pathfinder Pathfinder => Colonies[0].Pathfinder;

    /// <summary>Tout le hasard de la simulation passe par ici : une même graine rejoue la même histoire.</summary>
    public Random Random { get; }

    /// <param name="startingColonists">Nombre de colons fondateurs (par colonie) ; tiré au hasard entre 5 et 10 si l'on n'en précise pas.</param>
    /// <param name="migration">Faux pour couper les arrivées de voyageurs et les départs.</param>
    /// <param name="trade">Faux pour que les colonies n'échangent rien.</param>
    /// <param name="colonyCount">
    /// Nombre de colonies. Chacune a son espèce (humains, puis nains, elfes, orcs) et sa propre carte locale,
    /// dont le relief convient à son peuple.
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
        Clock = new GameClock(StartTicks);
        if (colonyCount == 0)
            _emptyMap = GenerateColonyMap(Species.Human);
        for (int i = 0; i < colonyCount; i++)
        {
            Species species = Species.All[i % Species.All.Count];
            LocalMap map = MapGenerator.Generate(mapWidth, mapHeight, seed + i * MapSeedStep, species.Biome);
            int founders = startingColonists
                ?? Random.Next(ColonyFounder.MinStartingColonists, ColonyFounder.MaxStartingColonists + 1);
            Colonies.Add(ColonyFounder.Found(map, Random, ColonyName(i, species), founders, NextColonistId, Clock, species));
        }
        for (int i = 0; i < Colonies.Count; i++)
        {
            WorldMap.Place(Colonies[i], i, Colonies.Count);
            // Le même fleuve traverse les colonies dans l'ordre : chacune est en amont de la suivante.
            if (i > 0)
                Colonies[i - 1].Downstream = Colonies[i];
        }
        foreach (Colony colony in Colonies)
            ColonyBrain.Think(colony, colony.Map, Clock);
    }

    /// <summary>Prépare une région sans ajouter de colonie ni consommer le hasard de la vie des habitants.</summary>
    public LocalMap GenerateColonyMap(Species species) => MapGenerator.Generate(_mapWidth, _mapHeight,
        unchecked(Seed + Colonies.Count * MapSeedStep), species.Biome);

    /// <summary>Fonde une colonie en cours de partie ; une erreur ne modifie aucun état.</summary>
    public bool TryFoundColony(LocalMap map, int campX, int campY, string name, Species species,
        int founders, float worldX, float worldY, out Colony? colony, out string reason)
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
        else if (map.Width != _mapWidth || map.Height != _mapHeight || Colonies.Any(c => ReferenceEquals(c.Map, map)))
            reason = "Cette région est déjà occupée ou ne correspond pas à la taille du monde.";
        else if (!WorldMap.CanPlace(worldX, worldY, out reason) || !ColonyFounder.CanFoundAt(map, campX, campY, out reason))
            return false;
        else
        {
            colony = ColonyFounder.FoundAt(map, Random, name, founders, NextColonistId, Clock, species, campX, campY);
            WorldMap.PlaceAt(colony, worldX, worldY);
            if (Colonies.Count > 0)
                Colonies[^1].Downstream = colony;
            Colonies.Add(colony);
            ColonyBrain.Think(colony, map, Clock);
            reason = $"{colony.Name} a été fondée avec {founders} habitants.";
            return true;
        }
        return false;
    }

    /// <summary>Le joueur répond à une prière : accord ou refus.</summary>
    public void AnswerPrayer(Prayer prayer, bool approve) => prayer.Colony.Prayers.Answer(prayer, approve, Clock);

    /// <summary>Avance la simulation d'un tick.</summary>
    public void Step()
    {
        long day = Clock.TotalDays;
        int hour = Clock.Hour;
        Clock.Advance();
        if (Clock.TotalDays != day)
        {
            foreach (Colony colony in Colonies)
            {
                colony.Map.DailyUpdate(Clock.TotalDays, Clock.Season);
                ColonyBrain.OnDayStart(colony, Clock);
                if (_lifecycle)
                    Lifecycle.Daily(this, colony);
            }
        }

        if (Clock.Hour != hour)
        {
            foreach (Colony colony in Colonies)
            {
                ColonyBrain.Think(colony, colony.Map, Clock);
                if (Clock.Hour == FireLightingHour)
                    ColonyBrain.LightFire(colony, Clock);
                if (_lifecycle)
                    Lifecycle.Hourly(this, colony);
                if (Clock.Hour == Trade.PlanningHour && _trade)
                    Trade.Daily(this, colony);
                if (_migration)
                {
                    Migration.Hourly(this, colony);
                    if (Clock.Hour == Migration.ArrivalHour)
                        Migration.Daily(this, colony);
                }
            }
        }

        // Les caravanes déjà en route continuent même si l'on coupe le commerce pour de nouveaux départs.
        if (Clock.Hour != hour && Caravans.Count > 0)
            Trade.Hourly(this);

        foreach (Colony colony in Colonies)
        {
            foreach (Colonist colonist in colony.Members)
                ColonistAI.Tick(colonist, this);
            if (colony.Transients.Count > 0)
                foreach (Colonist traveler in colony.Transients.ToArray())
                    ColonistAI.TickTransient(traveler, this);
        }
    }

    /// <summary>On allume le feu pour la nuit à 20 h.</summary>
    private const int FireLightingHour = 20;
}
