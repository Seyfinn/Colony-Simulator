namespace GodColony.Simulation.Colonies;

public enum BuildingType { Hut }

/// <summary>
/// Un bâtiment de la colonie, du chantier à l'achèvement. Pour l'instant, seulement des huttes
/// de 2 × 2 cases : elles coûtent du bois et abritent 4 colons, un par case.
/// </summary>
public sealed class Building
{
    public const int HutCapacity = 4;
    public const int HutWood = 12;

    /// <summary>Temps de travail total pour bâtir une hutte, en secondes à vitesse ×1 pour un bâtisseur moyen.</summary>
    public const float HutWorkSeconds = 20f;

    public Building(BuildingType type, int x, int y)
    {
        Type = type;
        X = x;
        Y = y;
    }

    public BuildingType Type { get; }

    /// <summary>Case en haut à gauche du bâtiment.</summary>
    public int X { get; }
    public int Y { get; }
    public int Width => 2;
    public int Height => 2;

    public int WoodRequired => HutWood;
    public int WoodDelivered { get; internal set; }

    /// <summary>Bois en route vers le chantier, pour ne pas en faire apporter plus que nécessaire.</summary>
    public int WoodInTransit { get; internal set; }

    public int WoodStillToBring => Math.Max(0, WoodRequired - WoodDelivered - WoodInTransit);
    public bool HasAllMaterials => WoodDelivered >= WoodRequired;

    /// <summary>Avancement des travaux, de 0 à 1.</summary>
    public float Progress { get; internal set; }
    public bool IsComplete => Progress >= 1f;

    public List<Colonist> Residents { get; } = [];

    public IEnumerable<(int X, int Y)> Tiles
    {
        get
        {
            for (int dy = 0; dy < Height; dy++)
            for (int dx = 0; dx < Width; dx++)
                yield return (X + dx, Y + dy);
        }
    }

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    /// <summary>La case où dort un résident : chacun a la sienne.</summary>
    public (int X, int Y) BedOf(Colonist colonist) => Tiles.ElementAt(Math.Max(0, Residents.IndexOf(colonist)) % HutCapacity);
}
