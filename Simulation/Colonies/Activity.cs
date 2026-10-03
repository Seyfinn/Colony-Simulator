namespace GodColony.Simulation.Colonies;

public enum ActivityKind
{
    Wander,
    Sleep,
    Eat,
    Relax,
    /// <summary>Cueillir des baies pour les rapporter au stock de la colonie.</summary>
    Forage,
    /// <summary>Cueillir des baies pour les manger sur place, faute de nourriture au stock.</summary>
    ForageToEat,
    Deliver,
}

/// <summary>Ce qu'un colon est en train de faire : aller quelque part, puis y agir un certain temps.</summary>
public sealed class Activity(ActivityKind kind, int targetX, int targetY, float durationTicks)
{
    public ActivityKind Kind { get; } = kind;
    public int TargetX { get; } = targetX;
    public int TargetY { get; } = targetY;

    /// <summary>Durée de l'action une fois sur place, en ticks (le sommeil, lui, dure jusqu'à être reposé).</summary>
    public float DurationTicks { get; } = durationTicks;

    public float ElapsedTicks { get; set; }

    /// <summary>Vrai une fois que le colon est arrivé et a commencé l'action.</summary>
    public bool Started { get; set; }
}
