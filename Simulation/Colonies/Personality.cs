namespace GodColony.Simulation.Colonies;

/// <summary>Les huit axes de personnalité. Chacun va d'un extrême (-1) à l'autre (+1), 0 étant le juste milieu.</summary>
public enum Axis { Piete, Sociabilite, Ardeur, Audace, Ambition, Curiosite, Attachement, Temperament }

/// <summary>
/// La personnalité d'un colon : huit axes chiffrés, tirés au hasard (la plupart des gens sont modérés,
/// peu sont extrêmes). Elle oriente ses envies et ses rapports aux autres.
/// </summary>
public sealed class Personality
{
    public static readonly Axis[] All = Enum.GetValues<Axis>();

    private readonly float[] _value = new float[All.Length];

    public static Personality Neutral => new();

    public static Personality Random(Random random)
    {
        var personality = new Personality();
        foreach (Axis axis in All)
            personality._value[(int)axis] = random.NextSingle() + random.NextSingle() - 1f;
        return personality;
    }

    public float this[Axis axis] => _value[(int)axis];

    /// <summary>Les travailleurs acharnés vont plus vite, les paresseux moins (de -15 % à +15 %).</summary>
    public float WorkFactor => 1f + 0.15f * this[Axis.Ardeur];

    /// <summary>Les curieux apprennent plus vite (de -25 % à +25 %).</summary>
    public float LearningFactor => 1f + 0.25f * this[Axis.Curiosite];

    /// <summary>Les travailleurs s'ennuient moins vite que les paresseux (de -30 % à +30 % d'ennui).</summary>
    public float BoredomFactor => 1f - 0.3f * this[Axis.Ardeur];

    /// <summary>Les sociables ont plus vite besoin des autres (de -60 % à +60 %).</summary>
    public float LonelinessFactor => 1f + 0.6f * this[Axis.Sociabilite];

    /// <summary>Les enracinés supportent plus longtemps la déprime avant de partir (de -50 % à +50 %).</summary>
    public float PatienceFactor => 1f + 0.5f * this[Axis.Attachement];

    /// <summary>Les traits marquants (au moins 0,35 d'un côté ou de l'autre), en toutes lettres.</summary>
    public IEnumerable<string> NotableTraits(Sex sex, float threshold = 0.35f)
    {
        foreach (Axis axis in All)
        {
            float value = this[axis];
            if (MathF.Abs(value) >= threshold)
                yield return Word(axis, value > 0, sex == Sex.Female);
        }
    }

    private static string Word(Axis axis, bool high, bool female) => (axis, high) switch
    {
        (Axis.Piete, false) => "sceptique",
        (Axis.Piete, true) => female ? "pieuse" : "pieux",
        (Axis.Sociabilite, false) => "solitaire",
        (Axis.Sociabilite, true) => "sociable",
        (Axis.Ardeur, false) => female ? "paresseuse" : "paresseux",
        (Axis.Ardeur, true) => female ? "travailleuse" : "travailleur",
        (Axis.Audace, false) => female ? "prudente" : "prudent",
        (Axis.Audace, true) => "téméraire",
        (Axis.Ambition, false) => "humble",
        (Axis.Ambition, true) => female ? "ambitieuse" : "ambitieux",
        (Axis.Curiosite, false) => female ? "routinière" : "routinier",
        (Axis.Curiosite, true) => female ? "curieuse" : "curieux",
        (Axis.Attachement, false) => "nomade",
        (Axis.Attachement, true) => female ? "enracinée" : "enraciné",
        (Axis.Temperament, false) => "pacifique",
        _ => female ? "belliqueuse" : "belliqueux",
    };

    public static string AxisName(Axis axis) => axis switch
    {
        Axis.Piete => "Piété",
        Axis.Sociabilite => "Sociabilité",
        Axis.Ardeur => "Ardeur",
        Axis.Audace => "Audace",
        Axis.Ambition => "Ambition",
        Axis.Curiosite => "Curiosité",
        Axis.Attachement => "Attachement",
        _ => "Tempérament",
    };

    // Plus deux colons se ressemblent sur ces axes, mieux ils s'entendent. Le tempérament et la sociabilité comptent le plus.
    private static readonly float[] CompatibilityWeight = [1f, 1.2f, 0.7f, 0.3f, 0.5f, 0.5f, 0.3f, 1.5f];

    /// <summary>L'entente naturelle de deux personnalités, de -1 (tout les oppose) à +1 (jumeaux).</summary>
    public static float Compatibility(Personality a, Personality b)
    {
        float difference = 0f, total = 0f;
        foreach (Axis axis in All)
        {
            float weight = CompatibilityWeight[(int)axis];
            difference += weight * MathF.Abs(a[axis] - b[axis]);
            total += weight;
        }
        return 1f - difference / total;
    }
}
