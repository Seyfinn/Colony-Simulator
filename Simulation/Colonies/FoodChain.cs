using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Ce que la colonie veut produire pour mieux se nourrir : pain, farine, et le grain qu'il faut moudre.</summary>
public sealed record BreadDemand(int BreadTarget, int FlourTarget, int GrainSurplus, bool Active)
{
    public static readonly BreadDemand None = new(0, 0, 0, false);
}

/// <summary>
/// La chaîne du blé : le grain cru ne nourrit presque pas (voir <see cref="Stockpile.GrainMealValue"/>), il faut le transformer. Le moulin à eau fait de la farine avec
/// le grain, le four à pain fait du pain avec la farine et un peu de bois : trois mesures de grain donnent quatre pains.
/// Le moulin a besoin d'eau vive : sa vitesse suit le débit de la rivière (un barrage en amont le ralentit). Sans moulin, le four moud le grain à bras
/// (la meule à bras), à un rendement moindre : deux pains pour trois mesures de grain, de quoi survivre loin de tout cours d'eau.
/// </summary>
public static class FoodChain
{
    private const int GrainPerBatch = 3;
    private const int FlourPerBatch = 3;
    private const int BreadPerBatch = 4;

    /// <summary>Jours de repas qu'on veut garder en pain d'avance : la réserve alimentaire visée par la colonie (voir <c>ColonyBrain.FoodTargetDays</c>), le grain cru ne comptant presque pas.</summary>
    private const float BreadTargetDays = 6f;

    /// <summary>Jours de grain qu'on garde cru : aucun, le grain cru ne nourrit pas ; seule la quantité de sécurité des semailles et du bétail reste au grenier.</summary>
    private const float GrainReserveDays = 0.5f;

    /// <summary>Grain en surplus à partir duquel la colonie se décide à bâtir un moulin (une bonne moisson).</summary>
    private const int SurplusToBuildMill = 12;

    /// <summary>Elle ne cherche à se faire des réserves de pain qu'après avoir récolté quelques fois.</summary>
    private const int GrainHarvestedBeforeThinkingOfBread = 6;

    /// <summary>Le moulin doit être à moins de cette distance (en cases) du camp.</summary>
    public const int MillSearchRadius = 20;

    private static readonly Recipe Milling = new(BuildingType.Mill, [(ResourceType.Grain, GrainPerBatch)], ResourceType.Flour, FlourPerBatch, 10f);
    private static readonly Recipe Baking = new(BuildingType.Oven, [(ResourceType.Flour, FlourPerBatch), (ResourceType.Wood, 1)], ResourceType.Bread, BreadPerBatch, 12f, ResourceType.Wood);

    /// <summary>La boulange sans moulin : le grain est moulu à bras puis cuit, pour deux pains seulement et un peu plus de travail.</summary>
    private static readonly Recipe HandBaking = new(BuildingType.Oven, [(ResourceType.Grain, GrainPerBatch), (ResourceType.Wood, 1)], ResourceType.Bread, 2, 16f, ResourceType.Wood);

    /// <summary>Le four moud-il lui-même ? Oui tant que la colonie n'a pas de moulin achevé.</summary>
    public static bool HandMills(Colony colony) => !colony.Buildings.Any(b => b.Type == BuildingType.Mill && b.IsComplete);

    /// <summary>La recette du four de cette colonie (à la farine, ou au grain moulu à bras faute de moulin) ; celle du moulin est unique.</summary>
    public static Recipe RecipeFor(Colony colony, BuildingType workshop) =>
        workshop == BuildingType.Oven && HandMills(colony) ? HandBaking : RecipeFor(workshop);

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
        return new BreadDemand((int)MathF.Ceiling(daily * BreadTargetDays), Math.Max(2 * FlourPerBatch, FlourForBread((int)MathF.Ceiling(daily))), surplus, true);
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
        // Le four d'abord : il suffit à faire du pain (à la meule à bras) ; le moulin viendra améliorer le rendement quand le grain s'accumule.
        if (!hasOven)
            return BuildingType.Oven;
        return !hasMill && demand.GrainSurplus >= SurplusToBuildMill && !SettlementPlanner.IsKnownImpossible(colony, BuildingType.Mill) ? BuildingType.Mill : null;
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

        // Un atelier n'est proposé que s'il a un poste libre et de la place de sortie ; la demande est la demande nette en unités exactes (voir BatchProduction.NetDemand).
        Recipe baking = RecipeFor(colony, BuildingType.Oven);
        bool bakeable = stock.Available(ResourceType.Wood) >= 1 + heatingReserve
            && (HandMills(colony) ? demand.GrainSurplus >= GrainPerBatch : stock.Available(ResourceType.Flour) >= FlourPerBatch)
            && BatchProduction.NetDemand(colony, ResourceType.Bread) > 0;
        if (bakeable && colony.Workshops(BuildingType.Oven).FirstOrDefault(o => WorkshopCapacity.CanStartBatch(colony, o, baking)) is { } oven)
            return oven;
        NoteRefusal(colony, BuildingType.Oven, bakeable);

        bool millable = demand.GrainSurplus >= GrainPerBatch && BatchProduction.NetDemand(colony, ResourceType.Flour) > 0;
        if (millable && colony.Workshops(BuildingType.Mill).FirstOrDefault(m => WorkshopCapacity.CanStartBatch(colony, m, Milling)) is { } mill)
            return mill;
        NoteRefusal(colony, BuildingType.Mill, millable);
        return null;
    }

    /// <summary>Du travail utile attendait, mais tous les postes de l'atelier étaient pris : une attente mesurée, qui plaide pour une extension.</summary>
    private static void NoteRefusal(Colony colony, BuildingType type, bool workWaiting)
    {
        if (workWaiting && colony.Workshops(type).FirstOrDefault() is { } shop && WorkshopCapacity.FindFreeSlot(colony, shop) < 0)
            colony.LocalSettlement.ScaleLedger.AddRefusal(shop);
    }

    /// <summary>La farine qu'il faut au four pour cuire ce nombre de pains (en fournées entières).</summary>
    public static int FlourForBread(int bread) => (bread + BreadPerBatch - 1) / BreadPerBatch * FlourPerBatch;

    /// <summary>Grain à réserver pour le pain manquant ; le moulin utilise d'abord la farine déjà disponible.</summary>
    public static int GrainForBread(Colony colony, int bread) => HandMills(colony)
        ? (bread + HandBaking.OutputAmount - 1) / HandBaking.OutputAmount * GrainPerBatch
        : Math.Max(0, FlourForBread(bread) - colony.Stock.Available(ResourceType.Flour));
}
