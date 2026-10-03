namespace GodColony.Simulation.Colonies;

public sealed class Colony
{
    public Colony(string name, int campX, int campY, List<(int X, int Y)> gatherSpots)
    {
        Name = name;
        CampX = campX;
        CampY = campY;
        GatherSpots = gatherSpots;
    }

    public string Name { get; }

    /// <summary>Le feu de camp, cœur de la colonie : on y mange, on s'y détend, on dort autour.</summary>
    public int CampX { get; }
    public int CampY { get; }

    /// <summary>Cases accessibles autour du feu, des plus proches aux plus lointaines.</summary>
    public IReadOnlyList<(int X, int Y)> GatherSpots { get; }

    public List<Colonist> Members { get; } = [];
    public Stockpile Stock { get; } = new();

    /// <summary>Cases déjà prises en charge par un colon (un buisson qu'il va cueillir, par exemple).</summary>
    internal HashSet<(int X, int Y)> Reserved { get; } = [];

    /// <summary>Chaque colon a sa place pour dormir autour du feu.</summary>
    public (int X, int Y) SleepSpot(Colonist colonist) =>
        GatherSpots[(1 + Members.IndexOf(colonist)) % GatherSpots.Count];

    public float AverageMood => Members.Count == 0 ? 0f : Members.Average(m => m.Needs.Mood);
}
