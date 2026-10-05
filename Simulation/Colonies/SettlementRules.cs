using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les paramètres de l'urbanisme : une première configuration, à équilibrer par simulation. Rien ici n'est tiré au hasard et rien ne
/// dépend de la vitesse d'affichage. Un changement de ces valeurs change la version des règles (voir <see cref="Version"/>), car des décisions
/// déjà prises (parcelles, tracés) ont été calculées avec les anciennes.
/// </summary>
public static class SettlementRules
{
    /// <summary>Version des règles et de la fonction de mélange spatiale ; sauvegardée avec chaque plan de village.</summary>
    public const int Version = 1;

    // --- Distances esthétiques (en cases, pour une carte de 200 × 200 ; voir Scale) ---

    /// <summary>Un groupe résidentiel compte de 3 à 6 huttes.</summary>
    public const int GroupMinHuts = 3;
    public const int GroupMaxHuts = 6;

    /// <summary>Place laissée entre deux emprises de la même ruelle : une case au moins, trois au plus.</summary>
    public const int MinGap = 1;
    public const int MaxGap = 3;

    /// <summary>Entre habitat et industrie lourde : six cases libres de préférence, trois au plancher.</summary>
    public const int HeavySeparation = 6;
    public const int HeavySeparationFloor = 3;

    /// <summary>Une ancre résidentielle secondaire se cherche à 18–30 cases du noyau parent.</summary>
    public const int SecondaryAnchorMin = 18;
    public const int SecondaryAnchorMax = 30;

    /// <summary>Fenêtre locale de recherche autour d'une ancre.</summary>
    public const int WindowMin = 8;
    public const int WindowMax = 14;

    /// <summary>Passes d'exploration autour des pôles quand rien ne convient dans la fenêtre locale.</summary>
    public static readonly int[] ExplorationPasses = [20, 35, 50, 70];

    /// <summary>Jours de jeu entre deux nouveaux quartiers de confort (urgences et activités imposées par le terrain exemptées).</summary>
    public const int ComfortCooldownDays = 2;

    /// <summary>Jours de nourriture qu'il faut avoir pour agrandir le confort ou la logistique.</summary>
    public const float ComfortFoodDays = 4f;

    // --- Trajets, en secondes de simulation à vitesse ×1 ---

    public const float WalkTilesPerSecond = 10f;
    public const float HomeToMealSeconds = 1.5f;
    public const float HomeToDepotSeconds = 3f;
    public const float PlotRoundTripSeconds = 4f;
    public const float PlotRoundTripCeilingSeconds = 6f;

    // --- Surfaces ---

    /// <summary>Coût d'un pas sur un sentier et sur un chemin de terre, par rapport au terrain nu sec.</summary>
    public const float TrailCost = 0.85f;
    public const float DirtRoadCost = 0.65f;

    /// <summary>Passages équivalents (au bout d'un jour d'usure) pour qu'un sentier apparaisse.</summary>
    public const int TrailThreshold = 8;

    /// <summary>Part de l'usure d'une cellule non aménagée qui s'estompe chaque jour.</summary>
    public const float WearLossPerDay = 0.15f;

    /// <summary>Temps de travail pour aménager une cellule de terre nue, avant le facteur de compétence.</summary>
    public const float RoadWorkSecondsPerCell = 0.35f;

    /// <summary>Part des travailleurs que les aménagements facultatifs peuvent mobiliser (arrondi inférieur).</summary>
    public const float RoadWorkerShare = 0.10f;

    /// <summary>Cellules aménagées d'un seul lot, sur un axe accepté.</summary>
    public const int RoadBatchCells = 8;

    // --- Recherche : limites par demande ---

    public const int MaxCandidates = 24;
    public const int MaxFinalists = 6;

    /// <summary>Nouveaux projets par pensée : un bâtiment (deux huttes si beaucoup de sans-abri), deux champs.</summary>
    public const int MaxFieldsPerThought = 2;

    // --- Budgets partagés par tout le monde (voir SettlementPlanningScheduler) ---

    public const int RendezvousTicks = 5;
    public const int PrefilterBudget = 8;
    public const int AStarExpansionBudget = 128;
    public const int ValidationCellBudget = 256;
    public const int MaxActiveSearches = 2;

    /// <summary>Plafond d'expansions d'un seul A* de planification : au-delà, le trajet est déclaré trop coûteux, pas introuvable.</summary>
    public const int MaxSearchExpansions = 6000;

    // --- Pondérations du score d'un site (voir SitePlanner) ---

    public const float WTerrain = 3f, WCohesion = 2f, WChain = 2f, WServices = 3f, WNetwork = 2f, WNewCore = 2f,
        WWalking = 4f, WNuisance = 3f, WClearing = 2f, WGoodSoil = 2f, WVariation = 0.15f;

    /// <summary>Facteur d'échelle des distances esthétiques : min(largeur, hauteur) / 200, entre 0,65 et 1,3. Les tailles de bâtiments et les budgets de trajet ne changent pas.</summary>
    public static float Scale(LocalMap map) => Math.Clamp(Math.Min(map.Width, map.Height) / 200f, 0.65f, 1.3f);

    public static int Scaled(LocalMap map, int cells) => Math.Max(1, (int)MathF.Round(cells * Scale(map)));

    // --- Fonction de mélange entière, stable et versionnée (jamais string.GetHashCode, jamais l'heure) ---

    /// <summary>Mélange entier de 32 bits (finaliseur de MurmurHash3) : mêmes entrées, mêmes sorties, sur toute machine.</summary>
    public static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }

    public static uint Mix(uint seed, int a, int b = 0, int c = 0, int d = 0)
    {
        uint h = Mix(seed ^ 0x9E3779B9u);
        h = Mix(h + (uint)a * 0x27D4EB2Fu);
        h = Mix(h + (uint)b * 0x165667B1u);
        h = Mix(h + (uint)c * 0x85EBCA77u);
        return Mix(h + (uint)d * 0xC2B2AE3Du);
    }

    /// <summary>Une empreinte stable d'une chaîne (FNV-1a) : l'identité d'une demande, jamais <c>string.GetHashCode</c>, qui change d'un processus à l'autre.</summary>
    public static int HashKey(string key)
    {
        uint hash = 2166136261u;
        foreach (char c in key)
            hash = (hash ^ c) * 16777619u;
        return (int)hash;
    }

    /// <summary>Une variation stable entre 0 et 1, fonction de la graine, des coordonnées, de la vocation et de l'identité de la demande.</summary>
    public static float Taste(uint seed, int x, int y, int kind, int request) => (Mix(seed, x, y, kind, request) >> 8) / (float)(1 << 24);

    /// <summary>Durée d'une journée en ticks et vitesse de marche par tick, pour convertir les secondes en cases.</summary>
    public const float WalkTilesPerTick = WalkTilesPerSecond / TimeConstants.TicksPerSecond;
}
