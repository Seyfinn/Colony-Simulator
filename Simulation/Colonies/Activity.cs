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
    /// <summary>Un voyageur marche jusqu'au camp pour se joindre à la colonie.</summary>
    Arrive,
    /// <summary>Un colon malheureux quitte la colonie pour de bon.</summary>
    Depart,
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

    /// <summary>Le chantier concerné, pour les activités de construction.</summary>
    public Building? Building { get; init; }

    /// <summary>Durée de l'action une fois sur place, en ticks (le sommeil, lui, dure jusqu'à être reposé).</summary>
    public float DurationTicks { get; } = durationTicks;

    public float ElapsedTicks { get; set; }

    /// <summary>Moment où le colon s'est engagé dans cette activité (départ compris).</summary>
    public long CommittedAtTicks { get; set; }

    /// <summary>Activité qui produit une ressource : on mesure son coût en travail.</summary>
    public bool IsHarvest => Kind is ActivityKind.Forage or ActivityKind.Fish or ActivityKind.Chop or ActivityKind.Mine;

    /// <summary>Vrai une fois que le colon est arrivé et a commencé l'action.</summary>
    public bool Started { get; set; }

    /// <summary>La compétence exercée pendant l'action, s'il s'agit d'un travail.</summary>
    public SkillType? Skill => Kind switch
    {
        ActivityKind.Forage => SkillType.Foraging,
        ActivityKind.Fish => SkillType.Fishing,
        ActivityKind.Chop => SkillType.Woodcutting,
        ActivityKind.Mine => SkillType.Mining,
        ActivityKind.Build => SkillType.Construction,
        _ => null,
    };

    /// <summary>Cette action réserve-t-elle sa case cible (pour éviter que deux colons visent le même arbre) ?</summary>
    public bool ReservesTarget =>
        Kind is ActivityKind.Forage or ActivityKind.ForageToEat or ActivityKind.Fish or ActivityKind.Chop or ActivityKind.Mine;
}
