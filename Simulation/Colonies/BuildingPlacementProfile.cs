namespace GodColony.Simulation.Colonies;

/// <summary>Le service dont dépend avant tout l'emplacement d'un bâtiment.</summary>
public enum ServiceNeed
{
    /// <summary>Aucun trajet régulier vers un service (un barrage, un puits).</summary>
    None,
    /// <summary>Lieu de repas et de rencontre : les logements, les services de la vie du village.</summary>
    Meal,
    /// <summary>Accès au stock (dépôt) : ateliers, champs, enclos.</summary>
    Depot,
    /// <summary>Une taverne réelle : le fût se tient près d'elle.</summary>
    Tavern,
}

/// <summary>
/// Ce qu'un type de bâtiment demande à son emplacement : sa vocation préférée et celles qu'il accepte, son emprise, ses besoins de service, ses
/// nuisances et ses affinités avec d'autres bâtiments. Les profils guident le choix ; aucun secteur angulaire fixe n'est autorisé.
/// </summary>
public sealed class BuildingPlacementProfile
{
    private BuildingPlacementProfile(BuildingType type, DistrictKind preferred, DistrictKind[] compatible, ServiceNeed service,
        bool heavy = false, bool sensitive = false, bool needsFlow = false, bool needsDoor = true,
        BuildingType[]? partners = null, bool followsNeighbourhood = false)
    {
        Type = type;
        Preferred = preferred;
        Compatible = compatible;
        Service = service;
        Heavy = heavy;
        Sensitive = sensitive;
        NeedsFlow = needsFlow;
        NeedsDoor = needsDoor;
        Partners = partners ?? [];
        FollowsNeighbourhood = followsNeighbourhood;
    }

    public BuildingType Type { get; }
    public DistrictKind Preferred { get; }
    public DistrictKind[] Compatible { get; }
    public ServiceNeed Service { get; }

    /// <summary>Industrie lourde (charbonnière, bas fourneau, forge) : on la tient à l'écart des logements.</summary>
    public bool Heavy { get; }

    /// <summary>Un bâtiment qui cherche le calme : logements, infirmerie, école, taverne.</summary>
    public bool Sensitive { get; }

    /// <summary>Le moulin conserve son obligation de débit hydraulique.</summary>
    public bool NeedsFlow { get; }

    public bool NeedsDoor { get; }

    /// <summary>Les bâtiments avec lesquels l'atelier travaille : on se tient près d'eux.</summary>
    public BuildingType[] Partners { get; }

    /// <summary>Le quartier d'un entrepôt dépend du besoin qu'il sert : il suit le quartier qui le réclame.</summary>
    public bool FollowsNeighbourhood { get; }

    public int Width => Building.FootprintOf(Type).Width;
    public int Height => Building.FootprintOf(Type).Height;

    /// <summary>La vocation convient-elle (préférée ou acceptée) ?</summary>
    public bool Accepts(DistrictKind kind) => kind == Preferred || Array.IndexOf(Compatible, kind) >= 0;

    private static readonly BuildingPlacementProfile[] Profiles =
    [
        new(BuildingType.Hut, DistrictKind.Residential, [DistrictKind.Civic, DistrictKind.Agricultural], ServiceNeed.Meal, sensitive: true),
        new(BuildingType.Kiln, DistrictKind.Industrial, [], ServiceNeed.Depot, heavy: true, partners: [BuildingType.Bloomery, BuildingType.Forge]),
        new(BuildingType.Bloomery, DistrictKind.Industrial, [], ServiceNeed.Depot, heavy: true, partners: [BuildingType.Kiln, BuildingType.Forge]),
        new(BuildingType.Forge, DistrictKind.Industrial, [], ServiceNeed.Depot, heavy: true, partners: [BuildingType.Bloomery, BuildingType.Kiln]),
        new(BuildingType.Dam, DistrictKind.Civic, [DistrictKind.Industrial, DistrictKind.Agricultural, DistrictKind.Residential], ServiceNeed.None, needsDoor: false),
        new(BuildingType.Mill, DistrictKind.Industrial, [DistrictKind.Agricultural], ServiceNeed.Depot, needsFlow: true, partners: [BuildingType.Oven]),
        new(BuildingType.Oven, DistrictKind.Industrial, [DistrictKind.Civic, DistrictKind.Residential], ServiceNeed.Depot, partners: [BuildingType.Mill]),
        new(BuildingType.Pen, DistrictKind.Agricultural, [], ServiceNeed.Depot, partners: [BuildingType.Loom]),
        new(BuildingType.Loom, DistrictKind.Industrial, [DistrictKind.Residential, DistrictKind.Agricultural], ServiceNeed.Depot, partners: [BuildingType.Pen]),
        new(BuildingType.Market, DistrictKind.Civic, [], ServiceNeed.Depot),
        new(BuildingType.Infirmary, DistrictKind.Civic, [DistrictKind.Residential], ServiceNeed.Meal, sensitive: true),
        new(BuildingType.Storehouse, DistrictKind.Civic, [DistrictKind.Agricultural, DistrictKind.Industrial, DistrictKind.Residential], ServiceNeed.Depot, followsNeighbourhood: true),
        new(BuildingType.Well, DistrictKind.Civic, [DistrictKind.Residential], ServiceNeed.None),
        new(BuildingType.Tavern, DistrictKind.Civic, [DistrictKind.Residential], ServiceNeed.Meal, sensitive: true),
        new(BuildingType.School, DistrictKind.Civic, [DistrictKind.Residential], ServiceNeed.Meal, sensitive: true),
        new(BuildingType.Cask, DistrictKind.Civic, [DistrictKind.Residential], ServiceNeed.Tavern, partners: [BuildingType.Tavern], followsNeighbourhood: true),
        new(BuildingType.MineDepot, DistrictKind.Industrial, [], ServiceNeed.Depot, heavy: true, partners: [BuildingType.Bloomery]),
        new(BuildingType.PotteryKiln, DistrictKind.Industrial, [], ServiceNeed.Depot, heavy: true),
        new(BuildingType.Tannery, DistrictKind.Industrial, [DistrictKind.Agricultural], ServiceNeed.Depot, heavy: true),
        new(BuildingType.Goldsmith, DistrictKind.Industrial, [DistrictKind.Civic], ServiceNeed.Depot),
        new(BuildingType.Mint, DistrictKind.Civic, [DistrictKind.Industrial], ServiceNeed.Depot),
        new(BuildingType.Shrine, DistrictKind.Civic, [DistrictKind.Residential], ServiceNeed.None),
    ];

    private static readonly Dictionary<BuildingType, BuildingPlacementProfile> ByType = Profiles.ToDictionary(p => p.Type);

    /// <summary>Un type que personne n'a encore décrit : un bâtiment civique ordinaire, qui cherche un lieu de repas.</summary>
    private static readonly BuildingPlacementProfile Fallback = new(BuildingType.Hut, DistrictKind.Civic, [DistrictKind.Residential], ServiceNeed.Meal);

    /// <summary>Le profil d'un type de bâtiment (il y en a un pour chaque valeur de <see cref="BuildingType"/>).</summary>
    public static BuildingPlacementProfile For(BuildingType type) => ByType.GetValueOrDefault(type, Fallback);
}
