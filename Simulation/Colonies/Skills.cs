namespace GodColony.Simulation.Colonies;

public enum SkillType { Foraging, Fishing, Woodcutting, Mining, Construction }

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
    public static Skills Random(Random random)
    {
        var skills = new Skills();
        foreach (SkillType skill in All)
        {
            skills._talent[(int)skill] = 0.5f + random.NextSingle();
            skills._level[(int)skill] = random.NextSingle() * 6f;
        }
        return skills;
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
