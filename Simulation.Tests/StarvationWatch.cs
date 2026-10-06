using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Tests;

/// <summary>
/// Repère les vraies famines : un colon qui n'a presque rien à manger à deux observations espacées d'un jour
/// (on se réveille parfois affamé, mais on mange aussitôt ; rester un jour entier sur le fil, c'est mourir de faim).
/// </summary>
public sealed class StarvationWatch
{
    private const float Starving = 0.02f;
    private Dictionary<Colonist, long> _previous = [];

    /// <summary>Le premier colon trouvé affamé deux observations de suite, s'il y en a un.</summary>
    public Colonist? Victim { get; private set; }

    /// <summary>À appeler une fois par jour de jeu, à la même heure.</summary>
    public void Observe(Colony colony)
    {
        var now = colony.Members.Where(m => m.Needs.Food <= Starving).ToHashSet();
        Victim ??= now.FirstOrDefault(c => _previous.TryGetValue(c, out long meal) && c.LastMealTicks == meal);
        _previous = now.ToDictionary(c => c, c => c.LastMealTicks);
    }
}
