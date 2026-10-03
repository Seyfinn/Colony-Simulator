namespace GodColony.Simulation.Colonies;

/// <summary>Les grands secteurs de travail entre lesquels la colonie répartit sa main-d'œuvre.</summary>
public enum WorkSector { Food, Wood, Stone }

public static class WorkSectors
{
    public static readonly WorkSector[] All = Enum.GetValues<WorkSector>();

    public static SkillType Skill(this WorkSector sector) => sector switch
    {
        WorkSector.Food => SkillType.Foraging,
        WorkSector.Wood => SkillType.Woodcutting,
        _ => SkillType.Mining,
    };
}
