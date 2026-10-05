using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Ce que la colonie veut produire pour mieux se nourrir : pain, farine, et le grain qu'il faut moudre.</summary>
public sealed record BreadDemand(int BreadTarget, int FlourTarget, int GrainSurplus, bool Active)
{
    public static readonly BreadDemand None = new(0, 0, 0, false);
}

/// <summary>
/// La chaîne du blé : le moulin à eau fait de la farine avec le grain, le four à pain fait du pain avec la farine
/// et un peu de bois. Le pain nourrit presque une fois et demie mieux que le grain cru, et trois mesures de grain
/// donnent quatre pains : la colonie tire plus de repas de ses champs, donc en cultive moins.
/// Le moulin a besoin d'eau vive : sa vitesse suit le débit de la rivière (un barrage en amont le ralentit).
/// </summary>
public static class FoodChain
{
    private const int GrainPerBatch = 3;
    private const int FlourPerBatch = 3;
    private const int BreadPerBatch = 4;

    /// <summary>Jours de repas qu'on veut garder en pain d'avance.</summary>
    private const float BreadTargetDays = 2f;

    /// <summary>Jours de grain qu'on garde cru pour ne pas affamer la colonie : on ne moud que le surplus.</summary>
    private const float GrainReserveDays = 3f;

    /// <summary>Grain en surplus à partir duquel la colonie se décide à bâtir un moulin (une bonne moisson).</summary>
    private const int SurplusToBuildMill = 12;

    /// <summary>Elle ne cherche à se faire des réserves de pain qu'après avoir récolté quelques fois.</summary>
    private const int GrainHarvestedBeforeThinkingOfBread = 16;

    /// <summary>Le moulin doit être à moins de cette distance (en cases) du camp.</summary>
    public const int MillSearchRadius = 20;

    private static readonly Recipe Milling = new(BuildingType.Mill, [(ResourceType.Grain, GrainPerBatch)], ResourceType.Flour, FlourPerBatch, 10f);
    private static readonly Recipe Baking = new(BuildingType.Oven, [(ResourceType.Flour, FlourPerBatch), (ResourceType.Wood, 1)], ResourceType.Bread, BreadPerBatch, 14f);

    public static bool IsFoodWorkshop(BuildingType type) => type is BuildingType.Mill or BuildingType.Oven;

    public static Recipe RecipeFor(BuildingType workshop) => workshop switch
    {
        BuildingType.Mill => Milling,
        BuildingType.Oven => Baking,
        _ => throw new ArgumentException("Ce bâtiment ne fait ni farine ni pain.", nameof(workshop)),
    };

    private static float DailyMeals(Colony colony) => Math.Max(1, colony.PresentMembers.Count) * ColonyBrain.MealsPerColonistPerDay;

    public static BreadDemand Demand(Colony colony)
    {
        if (colony.Labor.TotalProduced(ResourceType.Grain) < GrainHarvestedBeforeThinkingOfBread)
            return BreadDemand.None;
        float daily = DailyMeals(colony);
        int grainReserve = (int)MathF.Ceiling(daily * GrainReserveDays);
        int surplus = Math.Max(0, colony.Stock.Get(ResourceType.Grain) - grainReserve);
        return new BreadDemand((int)MathF.Ceiling(daily * BreadTargetDays), 2 * FlourPerBatch, surplus, true);
    }

    /// <summary>
    /// Le prochain atelier à bâtir : le moulin d'abord (si l'on a un grain en surplus et un cours d'eau où le poser),
    /// puis le four. Renvoie null s'il n'y a rien à bâtir pour l'instant.
    /// </summary>
    public static BuildingType? NextWorkshopToBuild(Colony colony, LocalMap map)
    {
        BreadDemand demand = Demand(colony);
        if (!demand.Active)
            return null;
        bool hasMill = colony.Buildings.Any(b => b.Type == BuildingType.Mill);
        bool hasOven = colony.Buildings.Any(b => b.Type == BuildingType.Oven);
        if (!hasMill)
            return demand.GrainSurplus >= SurplusToBuildMill && !SettlementPlanner.IsKnownImpossible(colony, BuildingType.Mill) ? BuildingType.Mill : null;
        return hasOven ? null : BuildingType.Oven;
    }

    /// <summary>
    /// Que fabriquer maintenant ? Du pain si l'on a de la farine, sinon de la farine avec le grain en surplus.
    /// Le bois du four ne doit pas entamer le bois de chauffage.
    /// </summary>
    public static Building? PickJob(Colony colony, int heatingReserve)
    {
        BreadDemand demand = Demand(colony);
        if (!demand.Active)
            return null;
        Stockpile stock = colony.Stock;

        if (colony.Workshops(BuildingType.Oven).FirstOrDefault() is { } oven
            && stock.Get(ResourceType.Flour) >= FlourPerBatch && stock.Get(ResourceType.Wood) >= 1 + heatingReserve
            && stock.Get(ResourceType.Bread) + BreadPerBatch * Crafting.Pending(colony, ResourceType.Bread) < demand.BreadTarget)
            return oven;

        if (colony.Workshops(BuildingType.Mill).FirstOrDefault() is { } mill
            && demand.GrainSurplus >= GrainPerBatch
            && stock.Get(ResourceType.Flour) + FlourPerBatch * Crafting.Pending(colony, ResourceType.Flour) < demand.FlourTarget)
            return mill;
        return null;
    }
}
