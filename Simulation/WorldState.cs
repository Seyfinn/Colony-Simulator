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

    /// <summary>Les voyageurs et les départs sont-ils actifs ? (On peut les couper pour étudier une colonie fermée.)</summary>
    private readonly bool _migration;

    internal int NextColonistId() => _nextColonistId++;

    public GameClock Clock { get; }
    public LocalMap Map { get; }
    public Pathfinder Pathfinder { get; }
    public List<Colony> Colonies { get; } = [];

    /// <summary>Tout le hasard de la simulation passe par ici : une même graine rejoue la même histoire.</summary>
    public Random Random { get; }

    /// <param name="startingColonists">Nombre de colons fondateurs ; tiré au hasard entre 5 et 10 si l'on n'en précise pas.</param>
    /// <param name="migration">Faux pour couper les arrivées de voyageurs et les départs.</param>
    public WorldState(int seed, int mapWidth = 160, int mapHeight = 160, int? startingColonists = null, bool migration = true)
    {
        _migration = migration;
        Random = new Random(seed);
        Clock = new GameClock(StartTicks);
        Map = MapGenerator.Generate(mapWidth, mapHeight, seed);
        Pathfinder = new Pathfinder(Map);
        int founders = startingColonists
            ?? Random.Next(ColonyFounder.MinStartingColonists, ColonyFounder.MaxStartingColonists + 1);
        Colonies.Add(ColonyFounder.Found(Map, Random, "Première colonie", founders, NextColonistId));
        foreach (Colony colony in Colonies)
            ColonyBrain.Think(colony, Map, Clock);
    }

    /// <summary>Avance la simulation d'un tick.</summary>
    public void Step()
    {
        long day = Clock.TotalDays;
        int hour = Clock.Hour;
        Clock.Advance();
        if (Clock.TotalDays != day)
            Map.DailyUpdate(Clock.TotalDays, Clock.Season);

        if (Clock.Hour != hour)
        {
            foreach (Colony colony in Colonies)
            {
                ColonyBrain.Think(colony, Map, Clock);
                if (Clock.Hour == FireLightingHour)
                    ColonyBrain.LightFire(colony, Clock);
                if (_migration)
                {
                    Migration.Hourly(this, colony);
                    if (Clock.Hour == Migration.ArrivalHour)
                        Migration.Daily(this, colony);
                }
            }
        }

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
