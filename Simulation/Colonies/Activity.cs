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
    Fish,
    Chop,
    Mine,
    Deliver,
    /// <summary>Prendre au stock les matériaux d'un chantier.</summary>
    FetchMaterials,
    /// <summary>Apporter les matériaux sur le chantier.</summary>
    SupplySite,
    Build,
    /// <summary>Semer une parcelle de champ.</summary>
    Sow,
    /// <summary>Moissonner une parcelle mûre.</summary>
    Harvest,
    /// <summary>Travailler dans un atelier : charbonnière, bas fourneau ou forge.</summary>
    Craft,
    /// <summary>Creuser une case de canal d'irrigation.</summary>
    Dig,
    /// <summary>Aller bavarder avec un autre colon.</summary>
    Chat,
    /// <summary>Un voyageur marche jusqu'au camp pour se joindre à la colonie.</summary>
    Arrive,
    /// <summary>Un colon malheureux quitte la colonie pour de bon.</summary>
    Depart,
    /// <summary>Soigner les animaux de l'enclos : ramasser les œufs, tondre les moutons.</summary>
    Tend,
    /// <summary>Soigner les malades et les blessés à l'infirmerie.</summary>
    Heal,
    /// <summary>Un enfant ou un adolescent apprend à l'école.</summary>
    Study,
    /// <summary>Abattre une bête à l'enclos, sur ordre de la colonie (besoin de nourriture ou surplus de bétail).</summary>
    Slaughter,
    /// <summary>Aménager une cellule d'un chemin en chemin de terre (travail facultatif de la compétence Construction).</summary>
    BuildRoad,
    /// <summary>Arracher un buisson sur le tracé d'un chemin à aménager (un arbre s'abat par l'abattage ordinaire).</summary>
    ClearAccess,
    Extract = 27,
    /// <summary>Travailler la pierre d'une offrande au sanctuaire (un chantier collectif d'offrande, voir <see cref="Offerings"/>).</summary>
    Sculpt = 28,
}

/// <summary>
/// Ce qu'un colon est en train de faire : aller quelque part, puis y agir un certain temps.
/// La cible (l'arbre, le buisson, la roche) peut différer de l'endroit où il se tient :
/// pour miner, on se place à côté de la roche.
/// </summary>
public sealed class Activity(ActivityKind kind, int targetX, int targetY, float durationTicks)
{
    public ActivityKind Kind { get; } = kind;
    public int TargetX { get; } = targetX;
    public int TargetY { get; } = targetY;

    /// <summary>Où le colon se tient pour agir. Par défaut, sur la cible elle-même.</summary>
    public int StandX { get; init; } = targetX;
    public int StandY { get; init; } = targetY;

    /// <summary>L'interlocuteur, pour une conversation.</summary>
    public Colonist? Partner { get; init; }

    /// <summary>Le chantier concerné, pour les activités de construction.</summary>
    public Building? Building { get; init; }

    /// <summary>Durée de l'action une fois sur place, en ticks (le sommeil, lui, dure jusqu'à être reposé).</summary>
    public float DurationTicks { get; } = durationTicks;

    public float ElapsedTicks { get; set; }

    /// <summary>Moment où le colon s'est engagé dans cette activité (départ compris).</summary>
    public long CommittedAtTicks { get; set; }

    /// <summary>Activité qui produit une ressource : on mesure son coût en travail.</summary>
    public bool IsHarvest => Kind is ActivityKind.Forage or ActivityKind.Fish or ActivityKind.Chop or ActivityKind.Mine or ActivityKind.Harvest
            or ActivityKind.Craft or ActivityKind.Tend or ActivityKind.Slaughter or ActivityKind.Extract;

    /// <summary>Pour un repas : ce qu'il redonne à celui qui mange (selon qu'on a pris du pain, du grain ou des baies).</summary>
    public float MealValue { get; set; } = Stockpile.GrainMealValue;

    /// <summary>Pour un repas : le plat de fête mangé (gâteau ou ragoût), s'il y en a un.</summary>
    public ResourceType? Meal { get; set; }

    /// <summary>Pour un travail de chemin : le tracé concerné (0 si l'activité n'est pas un travail routier).</summary>
    public int SegmentId { get; init; }
    public int DepositId { get; init; }

    /// <summary>Pour un lot de frappe : l'année du quota engagé et les pièces attendues (0 hors frappe ou une fois les pièces créées).</summary>
    public int MintYear { get; set; }
    public int MintCoins { get; set; }

    /// <summary>Pour un abattage : l'espèce de la bête.</summary>
    public ResourceType? Species { get; init; }

    /// <summary>Pour une fabrication : le produit visé, quand l'atelier a plusieurs recettes (les plats de fête) ; null pour la recette habituelle.</summary>
    public ResourceType? Product { get; init; }

    /// <summary>Pour une fabrication : les matières sont prises au stock, le produit n'est pas encore sorti.</summary>
    public bool InputsTaken { get; set; }

    /// <summary>Les intrants physiques engagés, avec leur type réel et leur âge.</summary>
    public Stockpile? InputsInventory { get; set; }

    /// <summary>La recette retenue au début du travail, conservée jusqu'à son achèvement.</summary>
    public Recipe? CommittedRecipe { get; set; }

    /// <summary>Pour une fabrication : le coût en heures de travail des matières premières consommées.</summary>
    public double InputLaborHours { get; set; }

    /// <summary>Vrai une fois que le colon est arrivé et a commencé l'action.</summary>
    public bool Started { get; set; }

    /// <summary>La compétence exercée pendant l'action, s'il s'agit d'un travail.</summary>
    public SkillType? Skill => Kind switch
    {
        ActivityKind.Forage => SkillType.Foraging,
        ActivityKind.Fish => SkillType.Fishing,
        ActivityKind.Chop => SkillType.Woodcutting,
        ActivityKind.Mine => SkillType.Mining,
        ActivityKind.Build or ActivityKind.Dig or ActivityKind.BuildRoad or ActivityKind.ClearAccess or ActivityKind.Sculpt => SkillType.Construction,
        ActivityKind.Craft => Building is { } workshop ? Crafting.SkillFor(workshop.Type) : SkillType.Smithing,
        ActivityKind.Tend or ActivityKind.Slaughter => SkillType.Husbandry,
        ActivityKind.Extract => SkillType.Mining,
        ActivityKind.Heal => SkillType.Medicine,
        ActivityKind.Sow or ActivityKind.Harvest => SkillType.Farming,
        _ => null,
    };

    /// <summary>Cette action réserve-t-elle sa case cible (pour éviter que deux colons visent le même arbre) ?</summary>
    public bool ReservesTarget =>
        Kind is ActivityKind.Forage or ActivityKind.ForageToEat or ActivityKind.Fish or ActivityKind.Chop or ActivityKind.Mine
            or ActivityKind.Sow or ActivityKind.Harvest or ActivityKind.Dig or ActivityKind.BuildRoad or ActivityKind.ClearAccess;
}
