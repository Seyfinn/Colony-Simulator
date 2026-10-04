namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les besoins de base d'un colon, de 0 (au plus bas) à 1 (pleinement satisfait).
/// La foi est celle qu'on porte au joueur-dieu : elle baisse quand ses prières sont refusées.
/// </summary>
public sealed class Needs
{
    public float Food { get; set; } = 1f;
    public float Rest { get; set; } = 1f;
    public float Leisure { get; set; } = 1f;

    /// <summary>Le besoin des autres : il baisse avec le temps, une conversation le comble.</summary>
    public float Social { get; set; } = 1f;

    /// <summary>Le chagrin d'un deuil, de 0 à 1 ; il s'estompe en quelques jours.</summary>
    public float Grief { get; set; }

    /// <summary>L'abri et la chaleur : une hutte, un feu allumé, de quoi dormir au chaud.</summary>
    public float Comfort { get; set; } = 1f;

    /// <summary>
    /// La confiance dans le joueur-dieu, de 0 à 1 (0,6 par défaut, ce qui ne change rien à l'humeur).
    /// Elle revient peu à peu vers le tempérament du colon (les pieux croient plus fort).
    /// </summary>
    public float Faith { get; set; } = NeutralFaith;

    public const float NeutralFaith = 0.6f;

    /// <summary>La gravité du mal dont souffre le colon, de 0 (en bonne santé) à 1 : maladie et blessure pèsent sur l'humeur.</summary>
    public float Illness { get; set; }

    /// <summary>La gaieté que laisse un bon repas (gâteau, ragoût), de 0 à 1 ; elle s'estompe en deux jours et remonte l'humeur.</summary>
    public float Cheer { get; set; }

    /// <summary>L'entrain que donne une bière à la taverne, de 0 à 1 ; il dure longtemps (cinq jours, voir <see cref="Cuisine.BeerDays"/>).</summary>
    public float BeerCheer { get; set; }

    /// <summary>Humeur de 0 à 1, calculée à partir des besoins. Un besoin au plus bas pèse lourd.</summary>
    public float Mood
    {
        get
        {
            float mood = Food * 0.3f + Rest * 0.25f + Leisure * 0.15f + Social * 0.15f + Comfort * 0.15f;
            mood -= 0.25f * Grief;
            mood -= 0.2f * Illness;
            mood += 0.2f * Cheer;
            mood += 0.15f * BeerCheer;
            mood += (Faith - NeutralFaith) * 0.15f;
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
        Grief = Math.Clamp(Grief, 0f, 1f);
        Faith = Math.Clamp(Faith, 0f, 1f);
        Illness = Math.Clamp(Illness, 0f, 1f);
        Cheer = Math.Clamp(Cheer, 0f, 1f);
        BeerCheer = Math.Clamp(BeerCheer, 0f, 1f);
    }
}
