using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Map;

/// <summary>
/// La couche routière d'une carte : la surface réalisée de chaque cellule, indépendante du sol (on ne remplace pas son type). Tableaux plats
/// indexés <c>y × largeur + x</c>, mêmes dimensions que la carte. Le trafic est compté en virgule fixe entière (1 passage = <see cref="Pass"/>),
/// avec le jour de la dernière lecture, pour que l'usure soit une fonction de la valeur et de la date, jamais de la fréquence à laquelle on la lit.
/// </summary>
public sealed class RoadLayer
{
    /// <summary>Un passage, en virgule fixe (Q8).</summary>
    public const int Pass = 256;

    /// <summary>Plafond d'usure d'une cellule : au-delà, le trafic n'apporte plus rien.</summary>
    public const int MaxWear = 200 * Pass;

    /// <summary>Travail d'aménagement d'une cellule, de 0 à <see cref="WorkDone"/> (un octet).</summary>
    public const byte WorkDone = 255;

    private readonly byte[] _surface;
    private readonly int[] _wear;
    private readonly int[] _wearDay;
    private readonly byte[] _work;

    /// <summary>Cellules qui ont reçu des passages depuis le début de la journée (le bilan quotidien ne parcourt pas toute la grille).</summary>
    internal HashSet<int> TouchedToday { get; } = [];

    /// <summary>Les sentiers non aménagés, par jour d'expiration prévu (agenda déterministe, vérifié paresseusement à l'échéance).</summary>
    internal Dictionary<long, List<int>> ExpiryAgenda { get; } = [];

    /// <summary>Le nombre de changements de surface depuis le début de la partie : les caches de coût s'y rattachent.</summary>
    public int SurfaceRevision { get; internal set; }

    internal RoadLayer(int cells)
    {
        _surface = new byte[cells];
        _wear = new int[cells];
        _wearDay = new int[cells];
        _work = new byte[cells];
    }

    public int Length => _surface.Length;

    public RoadSurface SurfaceAt(int cell) => (RoadSurface)_surface[cell];

    /// <summary>L'usure brute de la cellule et le jour où elle a été mise à jour (pour la lire à une autre date, voir <see cref="RoadDevelopment"/>).</summary>
    internal int RawWear(int cell) => _wear[cell];
    internal int WearDay(int cell) => _wearDay[cell];
    internal void SetWear(int cell, int wear, int day)
    {
        _wear[cell] = wear;
        _wearDay[cell] = day;
    }

    internal void SetSurface(int cell, RoadSurface surface)
    {
        RoadSurface before = (RoadSurface)_surface[cell];
        if (before == surface)
            return;
        if (before == RoadSurface.Trail) Trails--;
        else if (before == RoadSurface.DirtRoad) DirtRoads--;
        if (surface == RoadSurface.Trail) Trails++;
        else if (surface == RoadSurface.DirtRoad) DirtRoads++;
        _surface[cell] = (byte)surface;
        SurfaceRevision++;
    }

    /// <summary>Nombre de cellules de sentier et de chemin de terre : la plus petite durée de pas de la carte en dépend (voir <see cref="MinFactor"/>).</summary>
    public int Trails { get; private set; }
    public int DirtRoads { get; private set; }

    /// <summary>Le multiplicateur de coût le plus bas que la carte offre : 1 sans aucune route, 0,85 avec des sentiers, 0,65 avec un chemin de terre. L'heuristique d'un A* s'y tient pour rester admissible.</summary>
    public float MinFactor => DirtRoads > 0 ? SettlementRules.DirtRoadCost : Trails > 0 ? SettlementRules.TrailCost : 1f;

    public byte WorkAt(int cell) => _work[cell];
    internal void SetWork(int cell, byte work) => _work[cell] = work;

    /// <summary>Le multiplicateur de coût d'un pas qui entre dans cette cellule (1 hors route).</summary>
    public float CostFactor(int cell) => (RoadSurface)_surface[cell] switch
    {
        RoadSurface.Trail => SettlementRules.TrailCost,
        RoadSurface.DirtRoad => SettlementRules.DirtRoadCost,
        _ => 1f,
    };
}
