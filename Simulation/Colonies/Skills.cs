namespace GodColony.Simulation.Colonies;

public enum SkillType { Foraging, Fishing, Woodcutting, Mining, Construction, Farming, Smithing, Cooking }

/// <summary>
/// Les compétences d'un colon, de 0 à 20. Elles progressent par la pratique, plus ou moins vite
/// selon le talent inné de chacun : des spécialistes apparaissent ainsi naturellement.
/// </summary>
public sealed class Skills
{
    public const float MaxLevel = 20f;
    public static readonly SkillType[] All = Enum.GetValues<SkillType>();

    private readonly float[] _level = new float[All.Length];
    private readonly float[] _talent = new float[All.Length];

    /// <summary>Niveau de départ et talent tirés au hasard : chacun a ses points forts.</summary>
    public static Skills Random(Random random, Species? species = null)
    {
        var skills = new Skills();
        foreach (SkillType skill in All)
        {
            // Les talents de l'espèce déplacent le tirage : un nain est plus souvent doué pour la mine.
            float bias = species?.Talent(skill) ?? 1f;
            skills._talent[(int)skill] = Math.Clamp((0.5f + random.NextSingle()) * bias, 0.5f, 1.5f);
            skills._level[(int)skill] = random.NextSingle() * 6f;
        }
        return skills;
    }

    /// <summary>
    /// Un enfant : il naît sans expérience, mais son talent vient de ses parents (la moyenne des deux, à peu près).
    /// </summary>
    public static Skills Inherit(Random random, Skills mother, Skills father)
    {
        var skills = new Skills();
        foreach (SkillType skill in All)
        {
            float average = (mother._talent[(int)skill] + father._talent[(int)skill]) / 2f;
            skills._talent[(int)skill] = Math.Clamp(average + (random.NextSingle() - 0.5f) * 0.4f, 0.5f, 1.5f);
            skills._level[(int)skill] = 0f;
        }
        return skills;
    }

    /// <summary>Un voyageur expérimenté : un métier déjà maîtrisé (niveau 8 à 12), le reste tiré au hasard.</summary>
    public static Skills Veteran(Random random, SkillType specialty, Species? species = null)
    {
        Skills skills = Random(random, species);
        skills._level[(int)specialty] = 8f + 4f * random.NextSingle();
        skills._talent[(int)specialty] = MathF.Max(skills._talent[(int)specialty], 1f);
        return skills;
    }

    /// <summary>Le nom du métier, au masculin ou au féminin.</summary>
    public static string TradeName(SkillType skill, Sex sex)
    {
        bool f = sex == Sex.Female;
        return skill switch
        {
            SkillType.Foraging => f ? "cueilleuse" : "cueilleur",
            SkillType.Fishing => f ? "pêcheuse" : "pêcheur",
            SkillType.Woodcutting => f ? "bûcheronne" : "bûcheron",
            SkillType.Mining => f ? "mineuse" : "mineur",
            SkillType.Farming => f ? "agricultrice" : "agriculteur",
            SkillType.Smithing => f ? "forgeronne" : "forgeron",
            SkillType.Cooking => f ? "boulangère" : "boulanger",
            _ => f ? "bâtisseuse" : "bâtisseur",
        };
    }

    public float Level(SkillType skill) => _level[(int)skill];

    /// <summary>Multiplicateur d'apprentissage, de 0,5 (peu doué) à 1,5 (très doué).</summary>
    public float Talent(SkillType skill) => _talent[(int)skill];

    /// <summary>Vitesse de travail : ×0,6 pour un débutant, ×1 au niveau 10, ×1,4 pour un maître.</summary>
    public float WorkSpeed(SkillType skill) => 0.6f + Level(skill) * 0.04f;

    /// <summary>
    /// Apprentissage par la pratique : on progresse vite au début, puis de plus en plus lentement.
    /// Un colon moyen qui travaille tous les jours dans un métier atteint le niveau 15 en environ deux ans.
    /// </summary>
    public void Practice(SkillType skill, float seconds)
    {
        int i = (int)skill;
        float gain = 0.04f * seconds * _talent[i] / (1f + _level[i] / 5f);
        _level[i] = MathF.Min(MaxLevel, _level[i] + gain);
    }
}
