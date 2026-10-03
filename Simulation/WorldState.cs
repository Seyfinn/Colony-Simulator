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
    }

    /// <summary>Avance la simulation d'un tick.</summary>
    public void Step()
    {
        long day = Clock.TotalDays;
        Clock.Advance();
        if (Clock.TotalDays != day)
            Map.DailyUpdate();

        foreach (Colony colony in Colonies)
        foreach (Colonist colonist in colony.Members)
            ColonistAI.Tick(colonist, this);
    }
}
