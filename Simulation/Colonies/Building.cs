namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les bâtiments de la colonie : des huttes pour dormir, puis les ateliers de la chaîne du fer.
/// La charbonnière brûle du bois en charbon de bois, le bas fourneau tire du fer du minerai,
/// la forge fait des outils de ce fer.
/// </summary>
public enum BuildingType { Hut, Kiln, Bloomery, Forge, Dam }

/// <summary>
/// Un bâtiment de la colonie, du chantier à l'achèvement. Tous font 2 × 2 cases ; ils coûtent du bois
/// (et de la pierre pour les ateliers qui montent en température) et de la peine.
/// Une hutte abrite 4 colons, un par case.
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

    public bool IsHut => Type == BuildingType.Hut;
    public bool IsDam => Type == BuildingType.Dam;
    public bool IsWorkshop => Type is BuildingType.Kiln or BuildingType.Bloomery or BuildingType.Forge;

    /// <summary>Case en haut à gauche du bâtiment.</summary>
    public int X { get; }
    public int Y { get; }
    /// <summary>Un barrage ne tient que sur une case de rivière ; les autres bâtiments font 2 × 2.</summary>
    public int Width => IsDam ? 1 : 2;
    public int Height => IsDam ? 1 : 2;

    public int WoodRequired => Type switch
    {
        BuildingType.Kiln => 8,
        BuildingType.Bloomery => 6,
        BuildingType.Forge => 10,
        BuildingType.Dam => 16,
        _ => HutWood,
    };

    /// <summary>Un four de pierre tient la chaleur : le bas fourneau et la forge réclament de la pierre.</summary>
    public int StoneRequired => Type switch
    {
        BuildingType.Bloomery => 24,
        BuildingType.Forge => 12,
        BuildingType.Dam => 30,
        _ => 0,
    };

    public float WorkSeconds => Type switch
    {
        BuildingType.Kiln => 14f,
        BuildingType.Bloomery => 26f,
        BuildingType.Forge => 24f,
        BuildingType.Dam => 40f,
        _ => HutWorkSeconds,
    };

    public int WoodDelivered { get; internal set; }
    public int StoneDelivered { get; internal set; }

    /// <summary>Matériaux en route vers le chantier, pour ne pas en faire apporter plus que nécessaire.</summary>
    public int WoodInTransit { get; internal set; }
    public int StoneInTransit { get; internal set; }

    public int WoodStillToBring => Math.Max(0, WoodRequired - WoodDelivered - WoodInTransit);
    public int StoneStillToBring => Math.Max(0, StoneRequired - StoneDelivered - StoneInTransit);
    public bool HasAllMaterials => WoodDelivered >= WoodRequired && StoneDelivered >= StoneRequired;

    /// <summary>Ce qu'il faut encore apporter de ce matériau.</summary>
    public int StillToBring(ResourceType type) => type switch
    {
        ResourceType.Wood => WoodStillToBring,
        ResourceType.Stone => StoneStillToBring,
        _ => 0,
    };

    /// <summary>Le premier matériau qui manque et dont la colonie a en stock, ou null.</summary>
    internal ResourceType? MaterialToFetch(Stockpile stock)
    {
        foreach (ResourceType type in new[] { ResourceType.Wood, ResourceType.Stone })
            if (StillToBring(type) > 0 && stock.Get(type) > 0)
                return type;
        return null;
    }

    /// <summary>Un colon part avec ce matériau vers le chantier (ou y renonce) : on le compte « en route ».</summary>
    internal void AddInTransit(ResourceType type, int amount)
    {
        if (type == ResourceType.Wood)
            WoodInTransit += amount;
        else if (type == ResourceType.Stone)
            StoneInTransit += amount;
    }

    /// <summary>Le matériau est arrivé sur le chantier.</summary>
    internal void Deliver(ResourceType type, int amount)
    {
        AddInTransit(type, -amount);
        if (type == ResourceType.Wood)
            WoodDelivered += amount;
        else if (type == ResourceType.Stone)
            StoneDelivered += amount;
    }

    /// <summary>Avancement des travaux, de 0 à 1.</summary>
    public float Progress { get; internal set; }
    public bool IsComplete => Progress >= 1f;

    public List<Colonist> Residents { get; } = [];

    /// <summary>Temps de travail cumulé sur ce chantier (allers-retours compris), en ticks.</summary>
    public long LaborTicks { get; internal set; }

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

    public static string NameOf(BuildingType type) => type switch
    {
        BuildingType.Kiln => "charbonnière",
        BuildingType.Bloomery => "bas fourneau",
        BuildingType.Forge => "forge",
        BuildingType.Dam => "barrage",
        _ => "hutte",
    };
}
