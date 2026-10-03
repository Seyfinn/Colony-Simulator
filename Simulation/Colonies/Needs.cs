namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les besoins de base d'un colon, de 0 (au plus bas) à 1 (pleinement satisfait).
/// La foi viendra avec les pouvoirs divins.
/// </summary>
public sealed class Needs
{
    public float Food { get; set; } = 1f;
    public float Rest { get; set; } = 1f;
    public float Leisure { get; set; } = 1f;

    /// <summary>Le besoin des autres : il baisse avec le temps, une conversation le comble.</summary>
    public float Social { get; set; } = 1f;

    /// <summary>L'abri et la chaleur : une hutte, un feu allumé, de quoi dormir au chaud.</summary>
    public float Comfort { get; set; } = 1f;

    /// <summary>Humeur de 0 à 1, calculée à partir des besoins. Un besoin au plus bas pèse lourd.</summary>
    public float Mood
    {
        get
        {
            float mood = Food * 0.3f + Rest * 0.25f + Leisure * 0.15f + Social * 0.15f + Comfort * 0.15f;
            if (Food < 0.15f) mood -= 0.2f;
            if (Rest < 0.15f) mood -= 0.15f;
            return Math.Clamp(mood, 0f, 1f);
        }
    }

    public void Clamp()
    {
        Food = Math.Clamp(Food, 0f, 1f);
        Rest = Math.Clamp(Rest, 0f, 1f);
        Leisure = Math.Clamp(Leisure, 0f, 1f);
        Social = Math.Clamp(Social, 0f, 1f);
        Comfort = Math.Clamp(Comfort, 0f, 1f);
    }
}
