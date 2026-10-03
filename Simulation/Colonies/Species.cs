namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les proportions du terrain qui conviennent à une espèce (les nains veulent des montagnes, les humains des plaines).
/// </summary>
public sealed record MapStyle(float MountainShare, float WaterShare)
{
    public static readonly MapStyle Temperate = new(0.22f, 0.08f);
    public static readonly MapStyle Highlands = new(0.40f, 0.05f);
    public static readonly MapStyle Woodlands = new(0.12f, 0.10f);
    public static readonly MapStyle Steppe = new(0.18f, 0.04f);
}

/// <summary>
/// Une espèce n'a aucune règle spéciale : c'est une fiche de réglages appliquée aux systèmes existants
/// (durée de vie, fécondité, talents innés, tendances de caractère, terrain préféré).
/// </summary>
public sealed class Species
{
    private Species(string name, string plural, string adjective, float lifespanScale, float fertility, MapStyle biome,
        Dictionary<SkillType, float> talents, Dictionary<Axis, float> tendencies)
    {
        Name = name;
        Plural = plural;
        Adjective = adjective;
        LifespanScale = lifespanScale;
        Fertility = fertility;
        Biome = biome;
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

    public MapStyle Biome { get; }

    /// <summary>Multiplie le talent inné dans chaque métier (1 par défaut).</summary>
    public IReadOnlyDictionary<SkillType, float> TalentBias { get; }

    /// <summary>S'ajoute aux axes de personnalité tirés au hasard (0 par défaut).</summary>
    public IReadOnlyDictionary<Axis, float> PersonalityBias { get; }

    public float Talent(SkillType skill) => TalentBias.GetValueOrDefault(skill, 1f);
    public float Tendency(Axis axis) => PersonalityBias.GetValueOrDefault(axis);

    public static readonly Species Human = new("Humain", "Humains", "humaine", 1f, 1f, MapStyle.Temperate, [], []);

    public static readonly Species Dwarf = new("Nain", "Nains", "naine", 2f, 0.6f, MapStyle.Highlands,
        new()
        {
            [SkillType.Mining] = 1.3f, [SkillType.Smithing] = 1.3f, [SkillType.Construction] = 1.15f, [SkillType.Farming] = 0.8f,
        },
        new()
        {
            [Axis.Ardeur] = 0.25f, [Axis.Attachement] = 0.35f, [Axis.Audace] = -0.25f,
        });

    public static readonly Species Elf = new("Elfe", "Elfes", "elfe", 4f, 0.4f, MapStyle.Woodlands,
        new()
        {
            [SkillType.Foraging] = 1.3f, [SkillType.Woodcutting] = 1.1f, [SkillType.Cooking] = 1.15f, [SkillType.Mining] = 0.8f,
        },
        new()
        {
            [Axis.Curiosite] = 0.3f, [Axis.Piete] = 0.35f, [Axis.Temperament] = -0.3f,
        });

    public static readonly Species Orc = new("Orque", "Orques", "orque", 0.6f, 1.5f, MapStyle.Steppe,
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
