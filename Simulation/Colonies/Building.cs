namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les bâtiments de la colonie : des huttes pour dormir, puis les ateliers de la chaîne du fer.
/// La charbonnière brûle du bois en charbon de bois, le bas fourneau tire du fer du minerai,
/// la forge fait des outils de ce fer. Le moulin à eau moud le grain en farine, le four à pain en fait du pain.
/// L'enclos abrite les bêtes, le métier à tisser fait des vêtements de laine, le marché troque la denrée de la région.
/// L'infirmerie soigne, l'entrepôt protège les vivres, le puits donne de l'eau saine, la taverne délasse, l'école instruit.
/// </summary>
public enum BuildingType { Hut, Kiln, Bloomery, Forge, Dam, Mill, Oven, Pen, Loom, Market, Infirmary, Storehouse, Well, Tavern, School, Cask, MineDepot = 16, PotteryKiln = 17, Tannery = 18, Goldsmith = 19, Mint = 20, Shrine = 21 }

/// <summary>
/// Un bâtiment de la colonie, du chantier à l'achèvement. Son emprise et son coût dépendent de sa fonction :
/// les petits équipements côtoient les ateliers et les grands ouvrages de pierre.
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
        (_width, _height) = FootprintOf(type);
    }

    public BuildingType Type { get; }

    /// <summary>Identifiant local, stable et déterministe (0 tant que le bâtiment n'est pas enregistré dans le plan du village).</summary>
    public int Id { get; internal set; }

    /// <summary>Le bâtiment principal auquel cette extension est accolée (0 pour un bâtiment indépendant).</summary>
    public int ExtensionOfId { get; internal set; }
    public bool IsExtension => ExtensionOfId > 0;

    /// <summary>Le quartier et la parcelle auxquels il appartient (-1 : aucun, par exemple un barrage).</summary>
    public int DistrictId { get; internal set; } = -1;
    public int ParcelId { get; internal set; } = -1;

    /// <summary>
    /// La porte : la case de l'emprise qui la touche, et la case extérieure d'où l'on y accède (-1 : aucune, le bâtiment se traverse comme avant).
    /// Pour un barrage, la case d'accès est une cellule de travail sur la berge et il n'y a pas d'entrée intérieure.
    /// </summary>
    public int EntryX { get; internal set; } = -1;
    public int EntryY { get; internal set; } = -1;
    public int AccessX { get; internal set; } = -1;
    public int AccessY { get; internal set; } = -1;

    /// <summary>Le bâtiment a une porte : les chemins extérieurs le contournent et l'on y entre par là.</summary>
    public bool HasDoor => AccessX >= 0;

    public bool IsHut => Type == BuildingType.Hut;
    public bool IsDam => Type == BuildingType.Dam;
    public bool IsWorkshop => Type is BuildingType.Kiln or BuildingType.Bloomery or BuildingType.Forge or BuildingType.Mill or BuildingType.Oven
        or BuildingType.Loom or BuildingType.Market or BuildingType.PotteryKiln or BuildingType.Tannery or BuildingType.Goldsmith or BuildingType.Mint or BuildingType.Cask;

    /// <summary>Les bâtiments de la vie du village (hors ateliers) : enclos, infirmerie, entrepôt, puits, taverne, école.</summary>
    public bool IsCivic => Type is BuildingType.Pen or BuildingType.Infirmary or BuildingType.Storehouse or BuildingType.Well
        or BuildingType.Tavern or BuildingType.School or BuildingType.Cask or BuildingType.Shrine;

    /// <summary>Case en haut à gauche du bâtiment.</summary>
    public int X { get; }
    public int Y { get; }
    // L'orientation du barrage est fixée à sa fondation ; les autres bâtiments suivent l'emprise de leur type.
    private int _width, _height;
    public int Width { get => _width == 0 ? FootprintOf(Type).Width : _width; internal set => _width = value; }
    public int Height { get => _height == 0 ? FootprintOf(Type).Height : _height; internal set => _height = value; }

    /// <summary>La rivière passe au centre de l'ouvrage, entre ses deux culées.</summary>
    public int RiverX => X + Width / 2;
    public int RiverY => Y + Height / 2;

    /// <summary>Emprise des nouvelles constructions, partagée avec le choix des parcelles.</summary>
    public static (int Width, int Height) FootprintOf(BuildingType type) => type switch
    {
        BuildingType.Well or BuildingType.Cask => (1, 1),
        BuildingType.Forge or BuildingType.Infirmary or BuildingType.Tavern or BuildingType.School => (3, 2),
        BuildingType.Mill or BuildingType.Shrine => (3, 3),
        BuildingType.Pen or BuildingType.Storehouse or BuildingType.Market => (4, 3),
        BuildingType.MineDepot => (6, 4),
        BuildingType.Dam => (5, 3),
        _ => (2, 2),
    };

    /// <summary>Matériaux et travail suivent la surface des ateliers et des bâtiments de service.</summary>
    private float CostScale => Type switch
    {
        BuildingType.Forge or BuildingType.Infirmary or BuildingType.Tavern or BuildingType.School => 1.5f,
        BuildingType.Mill => 2.25f,
        BuildingType.Pen or BuildingType.Market => Width * Height / 4f,
        BuildingType.Storehouse => 3f,
        _ => 1f,
    };

    public int WoodRequired => (int)MathF.Ceiling(CostScale * (Type switch
    {
        BuildingType.Kiln => 8,
        BuildingType.Bloomery => 6,
        BuildingType.Forge => 10,
        BuildingType.Dam => 100,
        BuildingType.MineDepot => 120,
        BuildingType.Mill => 14,
        BuildingType.Mint => 10,
        BuildingType.Shrine => 6,
        BuildingType.Oven => 6,
        BuildingType.Pen => 10,
        BuildingType.Loom => 8,
        BuildingType.Market => 14,
        BuildingType.Infirmary => 12,
        BuildingType.Storehouse => 18,
        BuildingType.Well => 4,
        BuildingType.Tavern => 16,
        BuildingType.School => 14,
        BuildingType.Cask => 6,
        _ => HutWood,
    }));

    /// <summary>Un four de pierre tient la chaleur : le bas fourneau et la forge réclament de la pierre.</summary>
    public int StoneRequired => (int)MathF.Ceiling(CostScale * (Type switch
    {
        BuildingType.Bloomery => 24,
        BuildingType.Forge => 12,
        BuildingType.Dam => 240,
        BuildingType.MineDepot => 100,
        BuildingType.Mill => 20,
        BuildingType.Mint => 16,
        BuildingType.Shrine => 30,
        BuildingType.Oven => 16,
        BuildingType.Market => 6,
        BuildingType.Infirmary => 8,
        BuildingType.Storehouse => 10,
        BuildingType.Well => 14,
        BuildingType.Tavern => 4,
        BuildingType.School => 6,
        _ => 0,
    }));

    public float WorkSeconds => CostScale * (Type switch
    {
        BuildingType.Kiln => 14f,
        BuildingType.Bloomery => 26f,
        BuildingType.Forge => 24f,
        BuildingType.Dam => 480f,
        BuildingType.MineDepot => 260f,
        BuildingType.Mint => 26f,
        BuildingType.Shrine => 30f,
        BuildingType.Mill => 26f,
        BuildingType.Oven => 18f,
        BuildingType.Pen => 14f,
        BuildingType.Loom => 14f,
        BuildingType.Market => 24f,
        BuildingType.Infirmary => 24f,
        BuildingType.Storehouse => 28f,
        BuildingType.Well => 20f,
        BuildingType.Tavern => 26f,
        BuildingType.School => 24f,
        BuildingType.Cask => 8f,
        _ => HutWorkSeconds,
    });

    /// <summary>
    /// Pour un fût : le moment où sa bière sera prête (0 s'il est vide). Il fermente cinq jours (voir <see cref="Cuisine.BrewDays"/>) puis livre ses chopes à la taverne.
    /// </summary>
    public long BrewReadyTicks { get; internal set; }
    public ResourceType? BrewProduct { get; internal set; }

    /// <summary>Pour un fût : ce qu'a coûté sa fournée en heures de travail (céréales comprises), pour fixer le prix de la bière.</summary>
    public double BrewCostHours { get; internal set; }

    /// <summary>Le fût contient une fournée, qu'elle fermente encore ou qu'elle attende d'être tirée.</summary>
    public bool IsBrewing => BrewReadyTicks > 0;

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

    /// <summary>Le nom est féminin en français (« une forge »).</summary>
    public static bool IsFeminine(BuildingType type) =>
        type is BuildingType.Hut or BuildingType.Kiln or BuildingType.Forge or BuildingType.Infirmary or BuildingType.Tavern or BuildingType.School or BuildingType.MineDepot;

    /// <summary>« la forge », « le moulin », « l'infirmerie » (élision devant une voyelle ; le h de « hutte » est aspiré).</summary>
    public static string Definite(BuildingType type)
    {
        string name = NameOf(type);
        return "aeiouyé".Contains(name[0]) ? "l'" + name : (IsFeminine(type) ? "la " : "le ") + name;
    }

    /// <summary>« à la forge », « au moulin », « à l'infirmerie ».</summary>
    public static string AtThe(BuildingType type) =>
        Definite(type).StartsWith("l'") ? "à " + Definite(type) : (IsFeminine(type) ? "à la " : "au ") + NameOf(type);

    /// <summary>« un moulin », « une forge ».</summary>
    public static string WithArticle(BuildingType type) => (IsFeminine(type) ? "une " : "un ") + NameOf(type);

    public static string NameOf(BuildingType type) => type switch
    {
        BuildingType.Kiln => "charbonnière",
        BuildingType.Bloomery => "bas fourneau",
        BuildingType.Forge => "forge",
        BuildingType.Dam => "barrage",
        BuildingType.Mill => "moulin",
        BuildingType.Oven => "four à pain",
        BuildingType.Pen => "enclos",
        BuildingType.Loom => "métier à tisser",
        BuildingType.Market => "marché",
        BuildingType.Infirmary => "infirmerie",
        BuildingType.Storehouse => "entrepôt",
        BuildingType.Well => "puits",
        BuildingType.Tavern => "taverne",
        BuildingType.School => "école",
        BuildingType.Cask => "fût",
        BuildingType.MineDepot => "mine", BuildingType.PotteryKiln => "four de poterie",
        BuildingType.Tannery => "tannerie", BuildingType.Goldsmith => "orfèvrerie", BuildingType.Mint => "atelier de frappe", BuildingType.Shrine => "sanctuaire",
        _ => "hutte",
    };
}
