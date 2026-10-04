namespace GodColony.Simulation.World;

/// <summary>Le milieu naturel d'une case du monde, déduit de sa température et de ses pluies (comme dans RimWorld).</summary>
public enum Biome : byte
{
    Ocean,
    IceSheet,
    Tundra,
    BorealForest,
    TemperateForest,
    Grassland,
    Steppe,
    Desert,
    Savanna,
    TropicalForest,
    Swamp,
}

/// <summary>Le relief d'une case du monde, indépendant du biome : une forêt tempérée peut être plate ou montagneuse.</summary>
public enum Relief : byte { Flat, Hills, Mountains, Impassable }

/// <summary>
/// La fiche d'un biome : son nom et ce qu'il donne à la carte locale d'une colonie qui s'y installe
/// (part de forêt, richesse du sol, sécheresse) et au voyage des caravanes qui le traversent.
/// </summary>
public sealed record BiomeInfo(
    string Name,
    bool Habitable,
    float ForestBias,
    float SoilRichness,
    float DryShare,
    float ExtraWater,
    float TravelCost)
{
    public static BiomeInfo Of(Biome biome) => Table[(int)biome];

    private static readonly BiomeInfo[] Table =
    [
        new("Océan", false, 0f, 0f, 0f, 0f, float.PositiveInfinity),
        new("Banquise", false, -0.2f, 0.2f, 0.6f, 0f, 3f),
        new("Toundra", true, -0.17f, 0.5f, 0.40f, 0f, 1.3f),
        new("Taïga", true, 0.08f, 0.7f, 0.15f, 0f, 1.4f),
        new("Forêt tempérée", true, 0.10f, 1.0f, 0.15f, 0f, 1.3f),
        new("Prairie", true, -0.03f, 1.1f, 0.20f, 0f, 1.0f),
        new("Steppe", true, -0.10f, 0.8f, 0.40f, -0.02f, 1.0f),
        new("Désert", true, -0.17f, 0.4f, 0.75f, -0.04f, 1.5f),
        new("Savane", true, -0.12f, 0.85f, 0.35f, -0.02f, 1.1f),
        new("Jungle", true, 0.20f, 0.9f, 0.05f, 0f, 1.8f),
        new("Marais", true, 0.05f, 0.8f, 0.05f, 0.10f, 2.0f),
    ];

    public static string NameOf(Relief relief) => relief switch
    {
        Relief.Flat => "plat",
        Relief.Hills => "collines",
        Relief.Mountains => "montagnes",
        _ => "sommets infranchissables",
    };

    /// <summary>Multiplie le temps de marche : on grimpe les collines, on peine en montagne.</summary>
    public static float TravelFactor(Relief relief) => relief switch
    {
        Relief.Flat => 1f,
        Relief.Hills => 1.5f,
        Relief.Mountains => 2.5f,
        _ => float.PositiveInfinity,
    };

    /// <summary>Part de montagne sur la carte locale selon le relief de la case du monde.</summary>
    public static float MountainShare(Relief relief) => relief switch
    {
        Relief.Flat => 0.10f,
        Relief.Hills => 0.22f,
        _ => 0.40f,
    };
}
