namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les plafonds et seuils territoriaux d'un monde, réglables sans toucher au code des décisions. Ce sont des valeurs initiales d'équilibrage, enregistrées
/// avec la partie : une partie reprise garde ses règles. Les limites protègent aussi les performances (cartes actives, expéditions simultanées).
/// </summary>
public sealed class TerritorialRules
{
    /// <summary>Établissements actifs d'un même peuple (le village principal compris).</summary>
    public int MaxActiveSettlements { get; set; } = 4;

    /// <summary>Régions qu'un peuple reconnaît au plus (visitées ou sondées).</summary>
    public int MaxRecognizedRegions { get; set; } = 8;

    /// <summary>Jours entre deux départs de prospection ou de fondation d'un même peuple.</summary>
    public int ExpeditionCooldownDays { get; set; } = 10;

    /// <summary>Habitants présents au village principal pour qu'il fonde un camp.</summary>
    public int MinFoundingPopulation { get; set; } = 28;

    /// <summary>Habitants présents pour qu'il envoie des prospecteurs.</summary>
    public int MinProspectingPopulation { get; set; } = 14;

    /// <summary>Adultes qui partent fonder un camp.</summary>
    public int FoundersPerCamp { get; set; } = 4;

    /// <summary>Un voisin refuse le passage à ceux dont il pense au plus cette opinion (sauf alliés) : voir <see cref="Diplomacy.HostileOpinion"/>.</summary>
    public float PassageRefusalOpinion { get; set; } = Diplomacy.HostileOpinion;

    /// <summary>Niveau le plus élevé d'une route mondiale (0 : sentier naturel).</summary>
    public int MaxRoadLevel { get; set; } = 2;

    /// <summary>Passages cumulés sur une arête avant que les habitants songent à l'aménager.</summary>
    public int RoadUseThreshold { get; set; } = 6;
}
