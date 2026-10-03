namespace GodColony.Simulation.Colonies;

public enum Sex { Female, Male }

/// <summary>Un colon est « de passage » quand il marche vers la colonie pour la rejoindre, ou en sort pour toujours.</summary>
public enum TransitState { None, Arriving, Leaving }

public sealed class Colonist
{
    public Colonist(int id, string name, Sex sex, Colony colony, Skills skills, float x, float y)
    {
        Id = id;
        Name = name;
        Sex = sex;
        Colony = colony;
        Skills = skills;
        X = PrevX = x;
        Y = PrevY = y;
    }

    public Skills Skills { get; }

    /// <summary>Le secteur auquel la colonie l'a affecté. Il y travaille en priorité, sans y être limité.</summary>
    public WorkSector Sector { get; internal set; }

    public int Id { get; }
    public string Name { get; }
    public Sex Sex { get; }
    public Colony Colony { get; }

    /// <summary>Position en cases (le centre d'une case est à +0,5).</summary>
    public float X { get; internal set; }
    public float Y { get; internal set; }

    /// <summary>Position au tick précédent, pour que l'affichage puisse lisser le mouvement.</summary>
    public float PrevX { get; internal set; }
    public float PrevY { get; internal set; }

    public int TileX => (int)X;
    public int TileY => (int)Y;

    /// <summary>Tant qu'il n'est pas arrivé (ou une fois parti), il ne compte pas parmi les membres de la colonie.</summary>
    public TransitState Transit { get; internal set; }

    /// <summary>Heures de suite passées dans la déprime ; trop longtemps, et il quitte la colonie.</summary>
    internal int UnhappyHours { get; set; }

    public Needs Needs { get; } = new();
    public Activity? Activity { get; internal set; }
    public bool IsSleeping => Activity is { Kind: ActivityKind.Sleep, Started: true };

    /// <summary>Ce que le colon transporte (null s'il a les mains vides).</summary>
    public (ResourceType Type, int Amount)? Carrying { get; internal set; }

    /// <summary>Le chantier auquel est destiné ce qu'il transporte ; null s'il le rapporte au stock.</summary>
    public Building? CarryingTo { get; internal set; }

    /// <summary>La hutte où il dort, s'il en a une.</summary>
    public Building? Home { get; internal set; }

    public bool IsSleepingAtHome => IsSleeping && Home is { } home && home.Contains(TileX, TileY);

    /// <summary>Distance parcourue, utilisée pour animer la marche.</summary>
    public float DistanceWalked { get; internal set; }

    internal List<(int X, int Y)> Path { get; set; } = [];
    internal int PathIndex { get; set; }

    /// <summary>Hauteur de marche autorisée sur le chemin en cours (2 seulement pour escalader hors d'un trou).</summary>
    internal int PathMaxStep { get; set; } = 1;
    internal int ThinkCooldown { get; set; }

    /// <summary>Moment où il est parti récolter ce qu'il rapporte, pour mesurer le coût en travail (-1 sinon).</summary>
    internal long WorkCycleStartTicks { get; set; } = -1;
}
