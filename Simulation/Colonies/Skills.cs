namespace GodColony.Simulation.Colonies;

public enum SkillType { Foraging, Fishing, Woodcutting, Mining, Construction, Farming, Smithing, Cooking, Husbandry, Weaving, Trading, Medicine, Hunting }

/// <summary>
/// Les compétences d'un colon, de 0 à 20. Elles progressent par la pratique, plus ou moins vite
/// selon le talent inné de chacun : des spécialistes apparaissent ainsi naturellement.
/// </summary>
public sealed class Skills
{
    public const float MaxLevel = 20f;
    public static readonly SkillType[] All = Enum.GetValues<SkillType>();

    /// <summary>
    /// Les premiers métiers sont tirés au sort par le hasard de la partie ; les suivants (élevage, tissage, négoce, soins)
    /// en sont dérivés sans consommer de hasard : ajouter un métier ne décale donc pas les graines existantes.
    /// </summary>
    private const int DrawnSkills = 8;

    private static float Unit(float seed, int salt)
    {
        uint h = unchecked((uint)(int)(seed * 100003f) * 2654435761u + (uint)salt * 40503u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        h ^= h >> 13;
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>Le métier où le colon est le plus doué (ce que les enfants apprennent à l'école).</summary>
    public SkillType Favorite()
    {
        SkillType best = All[0];
        foreach (SkillType skill in All)
            if (_talent[(int)skill] > _talent[(int)best])
                best = skill;
        return best;
    }

    private float[] _level = new float[All.Length];
    private float[] _talent = new float[All.Length];

    /// <summary>Complète les tableaux d'un colon lu dans un ancien format avec les métiers apparus depuis (talent moyen, aucune expérience).</summary>
    internal void PadToCurrent()
    {
        int known = _level.Length;
        if (known >= All.Length)
            return;
        Array.Resize(ref _level, All.Length);
        Array.Resize(ref _talent, All.Length);
        for (int i = known; i < All.Length; i++)
            _talent[i] = 1f;
    }

    /// <summary>Niveau de départ et talent tirés au hasard : chacun a ses points forts.</summary>
    public static Skills Random(Random random, Species? species = null)
    {
        var skills = new Skills();
        float mix = 0f;
        foreach (SkillType skill in All)
        {
            // Les talents de l'espèce déplacent le tirage : un nain est plus souvent doué pour la mine.
            float bias = species?.Talent(skill) ?? 1f;
            float draw = (int)skill < DrawnSkills ? random.NextSingle() : Unit(mix, (int)skill);
            float level = (int)skill < DrawnSkills ? random.NextSingle() : Unit(mix, 100 + (int)skill);
            skills._talent[(int)skill] = Math.Clamp((0.5f + draw) * bias, 0.5f, 1.5f);
            skills._level[(int)skill] = level * 6f;
            mix += draw + level;
        }
        return skills;
    }

    /// <summary>
    /// Un enfant : il naît sans expérience, mais son talent vient de ses parents (la moyenne des deux, à peu près).
    /// </summary>
    public static Skills Inherit(Random random, Skills mother, Skills father)
    {
        var skills = new Skills();
        float mix = 0f;
        foreach (SkillType skill in All)
        {
            float average = (mother._talent[(int)skill] + father._talent[(int)skill]) / 2f;
            float draw = (int)skill < DrawnSkills ? random.NextSingle() : Unit(mix, (int)skill);
            mix += draw;
            skills._talent[(int)skill] = Math.Clamp(average + (draw - 0.5f) * 0.4f, 0.5f, 1.5f);
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
            SkillType.Husbandry => f ? "éleveuse" : "éleveur",
            SkillType.Weaving => f ? "tisserande" : "tisserand",
            SkillType.Trading => f ? "marchande" : "marchand",
            SkillType.Medicine => f ? "guérisseuse" : "guérisseur",
            SkillType.Hunting => f ? "chasseuse" : "chasseur",
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
