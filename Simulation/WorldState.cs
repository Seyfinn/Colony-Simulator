using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation;

/// <summary>
/// L'état complet de la simulation. Godot ne fait que le lire pour l'afficher.
/// </summary>
public sealed class WorldState
{
    /// <summary>La partie commence à 8 h du matin, au premier jour du printemps.</summary>
    private const long StartTicks = TimeConstants.TicksPerDay * 8 / 24;

    public GameClock Clock { get; }
    public LocalMap Map { get; }

    public WorldState(int seed, int mapWidth = 160, int mapHeight = 160)
    {
        Clock = new GameClock(StartTicks);
        Map = MapGenerator.Generate(mapWidth, mapHeight, seed);
    }

    /// <summary>Avance la simulation d'un tick.</summary>
    public void Step()
    {
        Clock.Advance();
    }
}
