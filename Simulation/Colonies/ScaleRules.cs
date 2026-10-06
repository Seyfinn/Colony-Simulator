using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les réglages initiaux des grandes colonies : croissance en quartiers, lots, spécialistes, extensions, services et transport groupé.
/// Ce sont des hypothèses de départ à mesurer, pas des résultats validés ; aucune règle de jeu ne dépend d'un palier « petit » ou « grand » village.
/// </summary>
public static class ScaleRules
{
    // --- Croissance et schismes (voir GrowthPolicy) ---

    /// <summary>Pression de logement : au moins ce nombre de sans-abri sans toit prévu pendant ce nombre de jours.</summary>
    public const int HousingPressureHomeless = 4;
    public const int HousingPressureDays = 5;

    /// <summary>Mécontentement : humeur moyenne sous ce seuil pendant ce nombre de jours ; le retour au calme demande le seuil haut pendant les jours de calme.</summary>
    public const float DiscontentMood = 0.45f;
    public const float CalmMood = 0.55f;
    public const int DiscontentDays = 10;
    public const int CalmDays = 3;

    /// <summary>Saturation locale : observations quotidiennes qui confirment l'impossibilité de loger sur place, sur une même révision.</summary>
    public const int BlockedDays = 5;

    /// <summary>Une installation extérieure doit coûter au plus cette part de l'alternative locale, par habitant déplacé.</summary>
    public const double OpportunityAdvantage = 0.85;

    /// <summary>Distance (en cases) des huttes au feu au-delà de laquelle chaque trajet quotidien au cœur du village a un coût.</summary>
    public const float ComfortRadius = 14f;

    /// <summary>Le village garde au moins ce nombre d'habitants présents après un départ.</summary>
    public const int MinRemaining = 10;

    // --- Lots d'atelier (voir BatchProduction et WorkshopCapacity) ---

    /// <summary>Part du temps d'une recette de référence consacrée à la préparation, partagée par tout le lot ; le reste se répète à chaque unité.</summary>
    public const float BatchPreparationShare = 0.30f;

    /// <summary>Part fixe du combustible déclaré par une recette ; l'autre moitié croît avec le nombre de répétitions.</summary>
    public const float BatchFuelFixedShare = 0.50f;

    /// <summary>Répétitions maximales d'un atelier sans extension, avec la première extension, et postes de travail avec la deuxième.</summary>
    public const int InitialMaxBatch = 2;
    public const int ExtendedMaxBatch = 4;
    public const int InitialSlots = 1;
    public const int ExtendedSlots = 2;

    /// <summary>Lots maximaux que la sortie d'un poste peut garder en attente de livraison.</summary>
    public const int StoredOutputBatches = 2;

    // --- Services partagés (voir CivicServices) ---

    /// <summary>Places simultanées : huit élèves par école, quatre patients et un soignant actif par infirmerie, huit clients par taverne.</summary>
    public const int SchoolSeats = 8;
    public const int InfirmaryBeds = 4;
    public const int HealersPerInfirmary = 1;
    public const int TavernSeats = 8;

    /// <summary>Trajet maximal, en secondes de marche, d un habitant à un service (le plafond du profil de placement des bâtiments de service).</summary>
    public const float ServiceTravelCapSeconds = SettlementRules.HomeToMealSeconds * 2f;

    /// <summary>Une saturation s observe sur cinq jours ; au moins ce nombre de refus, et cette part des demandes, la prouvent. Une couverture se juge sur ce nombre de logements au moins.</summary>
    public const int ServiceDays = 5;
    public const int ServiceMinDenials = 5;
    public const double ServiceDenialShare = 0.30;
    public const int ServiceMinHomes = 4;
    public const double ServiceMinReach = 0.70;

    /// <summary>Heures de jeu qu'une seconde de simulation représente (une journée dure <see cref="TimeConstants.SecondsPerDay"/> secondes).</summary>
    public const double HoursPerSecond = 24.0 / TimeConstants.SecondsPerDay;
}
