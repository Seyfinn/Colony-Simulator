namespace GodColony.Simulation.Colonies;

/// <summary>
/// Un royaume : des colonies réunies par filiation (une colonie fille naît dans le royaume de sa mère) ou par conquête. Il n'impose ni tribut ni stock partagé :
/// ses membres sont alliés, peuvent traverser les terres des uns des autres, et élisent un roi parmi leurs chefs (voir <see cref="Realms"/>).
/// </summary>
public sealed class Realm
{
    public int Id { get; internal set; }
    public string Name { get; internal set; } = "";

    /// <summary>La couleur du royaume sur la carte du monde, stable pour toute la partie.</summary>
    public int ColorIndex { get; internal set; }

    public int CapitalColonyId { get; internal set; }

    /// <summary>Le roi : le chef d'une colonie membre (0 : pas encore élu).</summary>
    public int KingColonistId { get; internal set; }

    /// <summary>Les colonies membres, dans l'ordre d'entrée (la capitale comprise).</summary>
    public List<int> MemberColonyIds { get; internal set; } = [];

    public long FoundedTicks { get; internal set; }
    public long NextKingElectionTicks { get; internal set; }

    /// <summary>L'entretien du royaume a-t-il été payé à la dernière échéance ? La loyauté des membres en dépend (voir <see cref="Realms.LoyaltyTarget"/>).</summary>
    public bool UpkeepPaid { get; internal set; } = true;
}
