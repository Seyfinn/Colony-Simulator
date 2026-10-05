namespace GodColony.Simulation.Colonies;

/// <summary>Les modèles d'offrande. Les identifiants numériques sont stockés dans les sauvegardes : on n'en recycle aucun.</summary>
public enum OfferingModel { SimpleAltar = 0, HarvestStatue = 1, ChampionStatue = 2, GrandHarvestStatue = 3, CrownedChampionStatue = 4 }

public enum OfferingProjectState { Proposed = 0, Gathering = 1, Building = 2, Completed = 3, Suspended = 4, Cancelled = 5 }

/// <summary>
/// Un modèle d'offrande : ses matériaux réels, son travail et le souhait qu'il porte. Les quantités sont modestes mais les pierres rares
/// sont difficiles à réunir. Un projet fige sa liste au début : un nouveau fournisseur ne la change pas en cours de route.
/// </summary>
public sealed record OfferingTemplate(OfferingModel Model, string Name, (ResourceType Type, int Amount)[] Materials, float WorkSeconds, DivineWishKind Wish)
{
    /// <summary>Un modèle prestigieux demande au moins une pierre précieuse ; l'autel simple n'en demande aucune.</summary>
    /// <summary>Le nom du modèle est féminin en français (« une statue »), sauf l'autel.</summary>
    public bool Feminine => Model != OfferingModel.SimpleAltar;

    public bool Prestigious => Materials.Any(m => IsGem(m.Type));

    public static readonly OfferingTemplate[] All =
    [
        new(OfferingModel.SimpleAltar, "autel simple", [(ResourceType.Stone, 20), (ResourceType.Wood, 10)], 90f, DivineWishKind.CropBlessing),
        new(OfferingModel.HarvestStatue, "statue des récoltes",
            [(ResourceType.Stone, 24), (ResourceType.Gold, 2), (ResourceType.Emerald, 1), (ResourceType.Sapphire, 1)], 240f, DivineWishKind.CropBlessing),
        new(OfferingModel.ChampionStatue, "statue du champion",
            [(ResourceType.Stone, 24), (ResourceType.Gold, 2), (ResourceType.Ruby, 1), (ResourceType.Sapphire, 1)], 240f, DivineWishKind.ChampionBlessing),
        new(OfferingModel.GrandHarvestStatue, "grande statue des moissons",
            [(ResourceType.Stone, 36), (ResourceType.Gold, 3), (ResourceType.Ruby, 1), (ResourceType.Sapphire, 1), (ResourceType.Emerald, 1)], 420f, DivineWishKind.CropBlessing),
        new(OfferingModel.CrownedChampionStatue, "statue du champion couronnée",
            [(ResourceType.Stone, 24), (ResourceType.Gold, 2), (ResourceType.Ruby, 1), (ResourceType.Sapphire, 1), (ResourceType.Diamond, 1)], 300f, DivineWishKind.ChampionBlessing),
    ];

    public static OfferingTemplate Of(OfferingModel model) => All[(int)model];

    public static bool IsGem(ResourceType type) => type is ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond;
}

/// <summary>
/// Un projet collectif d'offrande d'un établissement : recette figée, matériaux réellement livrés (retirés du stock disponible, donc invendables),
/// travail accompli et état. Il ne crée aucun miracle : un monument achevé ne fait que porter un souhait (voir <see cref="DivineWish"/>).
/// </summary>
public sealed class OfferingProject
{
    public int Id { get; internal set; }
    public int SettlementId { get; internal set; }
    public OfferingModel Model { get; internal set; }
    public OfferingProjectState State { get; internal set; }

    /// <summary>L'état à retrouver quand une crise suspend le projet.</summary>
    public OfferingProjectState ResumeState { get; internal set; }

    /// <summary>Les matériaux exigés, figés à la création du projet.</summary>
    public Dictionary<ResourceType, int> Required { get; } = [];

    /// <summary>Ce qui a réellement été livré au projet : ces unités ne sont plus dans le stock.</summary>
    public Dictionary<ResourceType, int> Delivered { get; } = [];

    public float WorkSeconds { get; internal set; }
    public float WorkDone { get; internal set; }
    public long CreatedTicks { get; internal set; }
    public long StateTicks { get; internal set; }

    /// <summary>Pourquoi le projet est bloqué ou abandonné, en langage clair (null s'il avance).</summary>
    public string? BlockedReason { get; internal set; }

    /// <summary>Le monument né du projet une fois achevé (0 avant).</summary>
    public int MonumentId { get; internal set; }

    public bool IsActive => State is OfferingProjectState.Proposed or OfferingProjectState.Gathering or OfferingProjectState.Building or OfferingProjectState.Suspended;

    public int Missing(ResourceType type) => Math.Max(0, Required.GetValueOrDefault(type) - Delivered.GetValueOrDefault(type));
    public bool HasAllMaterials => Required.All(r => Delivered.GetValueOrDefault(r.Key) >= r.Value);
    public OfferingTemplate Template => OfferingTemplate.Of(Model);
}

/// <summary>Une offrande achevée : elle garde les matériaux investis et l'identité de son projet, sans rien produire d'elle-même.</summary>
public sealed class Monument
{
    public int Id { get; internal set; }
    public int SettlementId { get; internal set; }
    public int ProjectId { get; internal set; }
    public OfferingModel Model { get; internal set; }

    /// <summary>Les matériaux incorporés une seule fois ; ils ne reviennent jamais au stock.</summary>
    public Dictionary<ResourceType, int> Materials { get; } = [];

    public long CompletedTicks { get; internal set; }

    /// <summary>La case du sanctuaire où il se dresse.</summary>
    public int X { get; internal set; }
    public int Y { get; internal set; }

    /// <summary>Le souhait associé (0 tant qu'aucune cible valide n'existe).</summary>
    public int WishId { get; internal set; }
}
