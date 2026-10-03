namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les besoins de base d'un colon, de 0 (au plus bas) à 1 (pleinement satisfait).
/// La foi, le social et le confort viendront plus tard.
/// </summary>
public sealed class Needs
{
    public float Food { get; set; } = 1f;
    public float Rest { get; set; } = 1f;
    public float Leisure { get; set; } = 1f;

    /// <summary>Humeur de 0 à 1, calculée à partir des besoins. Un besoin au plus bas pèse lourd.</summary>
    public float Mood
    {
        get
        {
            float mood = Food * 0.4f + Rest * 0.35f + Leisure * 0.25f;
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
    }
}
