namespace GodColony.Simulation.Colonies;

public enum Sex { Female, Male }

public sealed class Colonist
{
    public Colonist(int id, string name, Sex sex, Colony colony, float x, float y)
    {
        Id = id;
        Name = name;
        Sex = sex;
        Colony = colony;
        X = PrevX = x;
        Y = PrevY = y;
    }

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

    public Needs Needs { get; } = new();
    public Activity? Activity { get; internal set; }
    public bool IsSleeping => Activity is { Kind: ActivityKind.Sleep, Started: true };

    /// <summary>Ce que le colon transporte vers le stock (null s'il a les mains vides).</summary>
    public (ResourceType Type, int Amount)? Carrying { get; internal set; }

    /// <summary>Distance parcourue, utilisée pour animer la marche.</summary>
    public float DistanceWalked { get; internal set; }

    internal List<(int X, int Y)> Path { get; set; } = [];
    internal int PathIndex { get; set; }
    internal int ThinkCooldown { get; set; }
}
