namespace GodColony.Simulation.Colonies;

/// <summary>
/// Un emplacement complet, calculé sans rien modifier au monde : l'emprise, l'entrée, le tracé de raccordement, les durées de trajet, ce qu'il faut
/// défricher, le détail du score et la révision spatiale évaluée. <see cref="SettlementPlanner.TryCommit"/> l'accepte, ou le renvoie au planificateur
/// si le monde a changé là où il compte.
/// </summary>
public sealed class PlacementProposal
{
    internal PlacementProposal(string requestKey, DevelopmentKind kind, BuildingType? type, int x, int y, int width, int height)
    {
        RequestKey = requestKey;
        Kind = kind;
        Type = type;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>La demande d'origine.</summary>
    public string RequestKey { get; }
    public DevelopmentKind Kind { get; }
    public BuildingType? Type { get; }

    /// <summary>Le quartier existant qui l'accueille (-1 si la proposition ouvre un nouveau quartier).</summary>
    public int DistrictId { get; internal set; } = -1;

    /// <summary>Pour un nouveau quartier : sa vocation, son ancre et son parent.</summary>
    public DistrictKind NewKind { get; internal set; }
    public int NewAnchorX { get; internal set; }
    public int NewAnchorY { get; internal set; }
    public int ParentDistrictId { get; internal set; } = -1;
    /// <summary>Une ancre de nouveau noyau : la proposition ouvre toujours un quartier neuf, sans se ranger dans un voisin compatible.</summary>
    public bool ForceNewDistrict { get; internal set; }

    public bool OpensDistrict => DistrictId < 0;

    /// <summary>L'emprise exacte.</summary>
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Margin { get; internal set; } = 1;

    /// <summary>La case de l'emprise qui touche la porte, et la case extérieure d'où l'on y accède.</summary>
    public int EntryX { get; internal set; } = -1;
    public int EntryY { get; internal set; } = -1;
    public int AccessX { get; internal set; } = -1;
    public int AccessY { get; internal set; } = -1;

    /// <summary>Le tracé de raccordement : de la case d'accès jusqu'à la première case du réseau (comprise), dans l'ordre (index de cellule).</summary>
    public List<int> PathCells { get; } = [];

    /// <summary>Les arbres et buissons du tracé : praticables, mais à dégager pour aménager une route.</summary>
    public List<int> ClearCells { get; } = [];

    /// <summary>Pour un quartier résidentiel qui s'ouvre : l'espace public modeste (rencontre et repas) qui l'accompagne ; Width = 0 si aucun.</summary>
    public int PublicX { get; internal set; }
    public int PublicY { get; internal set; }
    public int PublicWidth { get; internal set; }
    public int PublicHeight { get; internal set; }

    /// <summary>Les durées estimées, en secondes de simulation à vitesse ×1 : vers le service principal, et l'aller-retour vers le dépôt.</summary>
    public float ServiceSeconds { get; internal set; }
    public float DepotRoundTripSeconds { get; internal set; }

    /// <summary>Un trajet qui dépasse la cible mais reste sous le plafond, ou une préférence assouplie par l'urgence : le motif.</summary>
    public string Degradation { get; internal set; } = "";

    public float Score { get; internal set; }

    /// <summary>Le détail du score, dans l'ordre de <see cref="SitePlanner.ScoreNames"/>.</summary>
    public float[] ScoreParts { get; internal set; } = [];

    /// <summary>La révision spatiale du plan au moment de l'évaluation.</summary>
    public int EvaluatedRevision { get; internal set; }

    /// <summary>La révision de terrain (carte) au moment de l'évaluation : le moindre changement du relief ou de l'eau sur la zone utilisée la périme.</summary>
    public int EvaluatedTerrainRevision { get; internal set; }

    public bool HasPublicSpace => PublicWidth > 0;
}
