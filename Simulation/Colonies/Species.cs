using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les proportions du terrain d'une carte locale : part de montagne, d'eau, de forêt, de terre sèche, richesse du sol,
/// nombre de rivières. Elles viennent de la case du monde où la colonie s'installe (<see cref="For"/>).
/// </summary>
public sealed record MapStyle(float MountainShare, float WaterShare, float ForestBias = 0f, float SoilRichness = 1f,
    float DryShare = 0.20f, int RiverCount = 2, Biome Biome = Biome.TemperateForest)
{
    /// <summary>Part de la carte couverte par les zones de forêt : un tiers en terrain tempéré, la moitié en pays de forêts.</summary>
    public float ForestShare => Math.Clamp(0.32f + 1.6f * ForestBias, 0.05f, 0.7f);

    /// <summary>
    /// Le terrain local d'une case du monde : le relief fixe la part de montagne, le biome la forêt, le sol et la sécheresse,
    /// la côte et les marais ajoutent de l'eau, et la rivière du monde se retrouve sur la carte locale.
    /// </summary>
    public static MapStyle For(WorldTile tile)
    {
        BiomeInfo info = tile.Info;
        float water = Math.Max(0.03f, 0.06f + info.ExtraWater + (tile.Coastal ? 0.12f : 0f));
        // Un grand fleuve du monde donne deux cours d'eau à la région, une rivière un seul ; sans rivière, un ruisseau
        // naît quand même dans les régions humides.
        int rivers = tile.River >= 2 ? 2 : tile.River == 1 || tile.Rainfall > 0.3f ? 1 : 0;
        return new MapStyle(BiomeInfo.MountainShare(tile.Relief), water, info.ForestBias, info.SoilRichness,
            info.DryShare, rivers, tile.Biome);
    }

    public static readonly MapStyle Temperate = new(0.22f, 0.08f);

    /// <summary>Hautes terres : beaucoup de roche, peu de forêt, un sol maigre (3 céréales par parcelle au lieu de 4).</summary>
    public static readonly MapStyle Highlands = new(0.40f, 0.05f, ForestBias: -0.12f, SoilRichness: 0.75f);

    /// <summary>Forêts : peu de montagnes, des arbres partout.</summary>
    public static readonly MapStyle Woodlands = new(0.12f, 0.10f, ForestBias: 0.1f);

    public static readonly MapStyle Steppe = new(0.18f, 0.04f, ForestBias: -0.08f);
}

/// <summary>
/// Une espèce n'a aucune règle spéciale : c'est une fiche de réglages appliquée aux systèmes existants
/// (durée de vie, fécondité, talents innés, tendances de caractère, terrain préféré).
/// </summary>
public sealed class Species
{
    private Species(string name, string plural, string adjective, float lifespanScale, float fertility, MapStyle biome,
        Biome[] homeBiomes, Relief homeRelief, Dictionary<SkillType, float> talents, Dictionary<Axis, float> tendencies)
    {
        Name = name;
        Plural = plural;
        Adjective = adjective;
        LifespanScale = lifespanScale;
        Fertility = fertility;
        Biome = biome;
        _homeBiomes = homeBiomes;
        HomeRelief = homeRelief;
        TalentBias = talents;
        PersonalityBias = tendencies;
    }

    public string Name { get; }
    public string Plural { get; }

    /// <summary>« humaine », « naine » : pour nommer une colonie.</summary>
    public string Adjective { get; }

    /// <summary>
    /// Durée de vie relative à celle d'un humain (20 ans de jeu) : 2 pour un nain, qui vit deux fois plus longtemps.
    /// Les âges de la vie (enfant, adolescent, adulte, ancien) sont étirés dans la même proportion.
    /// </summary>
    public float LifespanScale { get; }

    /// <summary>Multiplie la chance de concevoir : les peuples qui vivent longtemps ont moins d'enfants.</summary>
    public float Fertility { get; }

    /// <summary>Terrain local typique du peuple (pour une carte sans case du monde, dans les tests).</summary>
    public MapStyle Biome { get; }

    /// <summary>Les biomes où ce peuple aime s'installer, le préféré d'abord.</summary>
    public IReadOnlyList<Biome> HomeBiomes => _homeBiomes;
    private readonly Biome[] _homeBiomes;

    /// <summary>Le relief qu'il préfère (les nains, la montagne).</summary>
    public Relief HomeRelief { get; }

    /// <summary>
    /// Combien une case du monde plaît à ce peuple : son biome, son relief, une rivière, la mer.
    /// Négatif si l'on ne peut pas y vivre.
    /// </summary>
    public float Appeal(WorldTile tile)
    {
        if (!tile.Habitable)
            return -100f;
        int rank = Array.IndexOf(_homeBiomes, tile.Biome);
        float score = rank < 0 ? 0f : 4f - rank;
        score += tile.Relief == HomeRelief ? 3f : Math.Abs((int)tile.Relief - (int)HomeRelief) == 1 ? 1f : 0f;
        score += tile.River switch { 2 => 2f, 1 => 1.5f, _ => 0f };
        if (tile.Coastal) score += 0.5f;
        return score;
    }

    /// <summary>Multiplie le talent inné dans chaque métier (1 par défaut).</summary>
    public IReadOnlyDictionary<SkillType, float> TalentBias { get; }

    /// <summary>S'ajoute aux axes de personnalité tirés au hasard (0 par défaut).</summary>
    public IReadOnlyDictionary<Axis, float> PersonalityBias { get; }

    public float Talent(SkillType skill) => TalentBias.GetValueOrDefault(skill, 1f);
    public float Tendency(Axis axis) => PersonalityBias.GetValueOrDefault(axis);

    public static readonly Species Human = new("Humain", "Humains", "humaine", 1f, 1f, MapStyle.Temperate,
        [World.Biome.Grassland, World.Biome.TemperateForest], Relief.Flat, [], []);

    public static readonly Species Dwarf = new("Nain", "Nains", "naine", 2f, 0.6f, MapStyle.Highlands,
        [World.Biome.TemperateForest, World.Biome.BorealForest, World.Biome.Grassland, World.Biome.Steppe], Relief.Mountains,
        new()
        {
            [SkillType.Mining] = 1.3f, [SkillType.Smithing] = 1.3f, [SkillType.Construction] = 1.15f, [SkillType.Farming] = 0.8f,
        },
        new()
        {
            [Axis.Ardeur] = 0.25f, [Axis.Attachement] = 0.35f, [Axis.Audace] = -0.25f,
        });

    public static readonly Species Elf = new("Elfe", "Elfes", "elfe", 4f, 0.4f, MapStyle.Woodlands,
        [World.Biome.TemperateForest, World.Biome.BorealForest, World.Biome.TropicalForest], Relief.Flat,
        new()
        {
            [SkillType.Foraging] = 1.3f, [SkillType.Woodcutting] = 1.1f, [SkillType.Cooking] = 1.15f, [SkillType.Mining] = 0.8f,
        },
        new()
        {
            [Axis.Curiosite] = 0.3f, [Axis.Piete] = 0.35f, [Axis.Temperament] = -0.3f,
        });

    public static readonly Species Orc = new("Orque", "Orques", "orque", 0.6f, 1.5f, MapStyle.Steppe,
        [World.Biome.Steppe, World.Biome.Savanna, World.Biome.Grassland], Relief.Hills,
        new()
        {
            [SkillType.Foraging] = 1.2f, [SkillType.Mining] = 1.1f, [SkillType.Woodcutting] = 1.1f, [SkillType.Construction] = 0.85f,
        },
        new()
        {
            [Axis.Temperament] = 0.4f, [Axis.Audace] = 0.35f, [Axis.Ambition] = 0.3f,
        });

    public static readonly IReadOnlyList<Species> All = [Human, Dwarf, Elf, Orc];
}
