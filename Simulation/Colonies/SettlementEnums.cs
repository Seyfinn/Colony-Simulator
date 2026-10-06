namespace GodColony.Simulation.Colonies;

/// <summary>La vocation dominante d'un quartier : pas une zone peinte, un pôle de développement.</summary>
public enum DistrictKind { Civic, Residential, Industrial, Agricultural }

/// <summary>L'absence d'activité ne détruit pas l'identité d'un quartier.</summary>
public enum DistrictStatus { Emerging, Active, Dormant }

public enum ParcelKind { Building, Field, PublicSpace, AccessCorridor }

public enum ReservationState { Reserved, Occupied, Released }

public enum DevelopmentKind { Housing, Field, Workshop, Civic, Logistics, RoadImprovement, AccessRepair, RoadShortcut, WorkshopExtension }

/// <summary>Ordre explicite de la pyramide : la survie d'abord, le confort en dernier.</summary>
public enum DevelopmentPriority { Survival, Housing, Production, Comfort }

public enum ProjectState { Accepted, Working, Paused, Completed, Cancelled, Blocked }

/// <summary>Surface réalisée d'une cellule : un sentier naît des passages, un chemin de terre d'un aménagement. Pavage réservé à plus tard.</summary>
public enum RoadSurface : byte { None, Trail, DirtRoad, Bridge }

/// <summary>Une impossibilité physique. L'épuisement du budget de recherche n'en est jamais une (voir <see cref="PlanningOutcome"/>).</summary>
public enum PlacementFailureKind { NoSuitableTerrain, NoAccess, TravelBudgetExceeded, NoCompatibleDistrict, NoSpace, SearchBudgetExceeded, PrerequisiteMissing }

/// <summary>Ce que sait dire une demande de développement à celui qui la consulte (la pensée horaire, par exemple).</summary>
public enum PlanningOutcome { Idle, Pending, Ready, WaitingForChange }

/// <summary>Le rôle d'un tronçon de chemin.</summary>
public enum SegmentFunction { Access, Link, Improvement }

/// <summary>Les événements qui justifient de retenter une demande restée sans suite (masque de bits).</summary>
[Flags]
public enum RetryEvents
{
    None = 0,
    /// <summary>Un bâtiment ou un champ achevé, détruit ou une parcelle libérée.</summary>
    Occupancy = 1,
    /// <summary>Un nouveau service (entrepôt, taverne, espace public).</summary>
    Service = 2,
    /// <summary>Une découverte, un seuil économique franchi.</summary>
    Knowledge = 4,
    /// <summary>Le terrain a changé dans la zone utile (retenue, mine, canal).</summary>
    Terrain = 8,
    /// <summary>Début d'une saison pertinente.</summary>
    Season = 16,
    /// <summary>Reprise quotidienne espacée.</summary>
    Daily = 32,
    /// <summary>Un chemin a changé de surface.</summary>
    Road = 64,
}
