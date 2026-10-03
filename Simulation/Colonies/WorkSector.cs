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
        WorkSector.Food => [SkillType.Foraging, SkillType.Fishing],
        WorkSector.Farm => [SkillType.Farming],
        WorkSector.Wood => [SkillType.Woodcutting],
        WorkSector.Stone => [SkillType.Mining],
        WorkSector.Construction => [SkillType.Construction],
        WorkSector.Craft => [SkillType.Smithing],
        _ => [],
    };

    /// <summary>Aptitude d'un colon pour un secteur : son niveau et son talent dans la meilleure compétence utile.</summary>
    public static float Fitness(this WorkSector sector, Skills skills) =>
        sector.Skills().Select(s => skills.Level(s) + skills.Talent(s) * 4f).DefaultIfEmpty(0f).Max();
}
