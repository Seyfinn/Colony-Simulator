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

    private const int StartingColonists = 20;

    private int _nextColonistId = 1;

    public GameClock Clock { get; }
    public LocalMap Map { get; }
    public Pathfinder Pathfinder { get; }
    public List<Colony> Colonies { get; } = [];

    /// <summary>Tout le hasard de la simulation passe par ici : une même graine rejoue la même histoire.</summary>
    public Random Random { get; }

    public WorldState(int seed, int mapWidth = 160, int mapHeight = 160)
    {
        Random = new Random(seed);
        Clock = new GameClock(StartTicks);
        Map = MapGenerator.Generate(mapWidth, mapHeight, seed);
        Pathfinder = new Pathfinder(Map);
        Colonies.Add(ColonyFounder.Found(Map, Random, "Première colonie", StartingColonists, () => _nextColonistId++));
        foreach (Colony colony in Colonies)
            ColonyBrain.Think(colony, Clock);
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
                ColonyBrain.Think(colony, Clock);
                if (Clock.Hour == FireLightingHour)
                    ColonyBrain.LightFire(colony, Clock);
            }
        }

        foreach (Colony colony in Colonies)
        foreach (Colonist colonist in colony.Members)
            ColonistAI.Tick(colonist, this);
    }

    /// <summary>On allume le feu pour la nuit à 20 h.</summary>
    private const int FireLightingHour = 20;
}
