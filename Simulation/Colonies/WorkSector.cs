namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les grands secteurs de travail entre lesquels la colonie répartit sa main-d'œuvre.
/// « Libre » regroupe ceux dont la colonie n'a pas besoin pour l'instant : ils se reposent.
/// </summary>
public enum WorkSector { Food, Farm, Wood, Stone, Construction, Craft, Free }

public static class WorkSectors
{
    /// <summary>Les secteurs où l'on travaille vraiment (tous sauf « libre »).</summary>
    public static readonly WorkSector[] Productive = [WorkSector.Food, WorkSector.Farm, WorkSector.Wood, WorkSector.Stone, WorkSector.Construction, WorkSector.Craft];

    public static readonly WorkSector[] All = Enum.GetValues<WorkSector>();

    /// <summary>Les compétences utiles dans un secteur (la nourriture sauvage vient de la cueillette ou de la pêche, les champs de l'agriculture).</summary>
    public static SkillType[] Skills(this WorkSector sector) => sector switch
    {
        WorkSector.Food => [SkillType.Foraging, SkillType.Fishing, SkillType.Hunting],
        WorkSector.Farm => [SkillType.Farming, SkillType.Husbandry],
        WorkSector.Wood => [SkillType.Woodcutting],
        WorkSector.Stone => [SkillType.Mining],
        WorkSector.Construction => [SkillType.Construction],
        WorkSector.Craft => [SkillType.Smithing, SkillType.Cooking, SkillType.Weaving, SkillType.Trading, SkillType.Medicine],
        _ => [],
    };

    /// <summary>Les compétences de chaque secteur, dans l'ordre de <see cref="WorkSector"/> (pour ne pas les recréer à chaque calcul).</summary>
    private static readonly SkillType[][] SkillsBySector = All.Select(s => s.Skills()).ToArray();

    /// <summary>Les métiers de niche (élevage, tissage, négoce, soins) pèsent moins : ils ne détournent pas les forgerons et les agriculteurs.</summary>
    private const float NicheWeight = 0.7f;

    private static float Aptitude(Skills skills, SkillType skill) =>
        (skills.Level(skill) + skills.Talent(skill) * 4f) * (skill >= SkillType.Husbandry ? NicheWeight : 1f);

    /// <summary>Aptitude d'un colon pour un secteur : son niveau et son talent dans la meilleure compétence utile (0 s'il n'y en a pas).</summary>
    public static float Fitness(this WorkSector sector, Skills skills)
    {
        SkillType[] useful = SkillsBySector[(int)sector];
        if (useful.Length == 0)
            return 0f;
        float best = Aptitude(skills, useful[0]);
        for (int i = 1; i < useful.Length; i++)
        {
            float fit = Aptitude(skills, useful[i]);
            if (fit > best)
                best = fit;
        }
        return best;
    }
}
