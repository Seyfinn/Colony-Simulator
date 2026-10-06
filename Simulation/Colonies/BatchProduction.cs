namespace GodColony.Simulation.Colonies;

/// <summary>Le lot retenu pour un poste : le nombre de répétitions, la recette concrète et le poste réservé. Un résultat temporaire, jamais sauvegardé.</summary>
public sealed record BatchChoice(int Count, Recipe Recipe, int SlotId);

/// <summary>
/// La production par lots du four à pain et du moulin : plusieurs répétitions d'une recette de référence partagent leur préparation et une part de leur combustible. Un lot répond à
/// la demande nette (il ne crée ni matières ni débouchés), occupe un poste dès l'engagement, garde recette et intrants pendant une pause, et dépose ses produits sur la sortie
/// physique de l'atelier, d'où l'on les porte au dépôt. Aucun gain ne dépend du nombre d'habitants : seul l'équipement réellement utilisé compte.
/// </summary>
public static class BatchProduction
{
    /// <summary>Les ateliers dont les recettes se répètent en lots (le premier déploiement ; la frappe, les fûts et les offrandes ont leurs propres contrats).</summary>
    public static bool IsEligible(BuildingType type) => type is BuildingType.Oven or BuildingType.Mill;

    /// <summary>
    /// <c>k</c> répétitions de la recette de référence : matières transformées et sortie proportionnelles, préparation (30 % du temps) partagée, combustible déclaré
    /// à moitié fixe. Pour <c>k = 1</c>, la recette actuelle exacte ; les intrants de même type sont fusionnés avant tout débit.
    /// </summary>
    public static Recipe BuildRecipe(Recipe reference, int count)
    {
        if (count <= 1)
            return reference;
        double fixedShare = ScaleRules.BatchFuelFixedShare;
        var inputs = reference.Inputs
            .Select(i => (i.Type, Amount: i.Type == reference.Fuel
                ? (int)Math.Ceiling(fixedShare * i.Amount + count * (1 - fixedShare) * i.Amount - 1e-9)
                : checked(i.Amount * count)))
            .GroupBy(i => i.Type).Select(g => (Type: g.Key, Amount: g.Sum(i => i.Amount))).ToArray();
        float seconds = reference.Seconds * (ScaleRules.BatchPreparationShare + count * (1 - ScaleRules.BatchPreparationShare));
        return reference with { Inputs = inputs, OutputAmount = checked(reference.OutputAmount * count), Seconds = seconds };
    }

    /// <summary>Deux recettes sont-elles les mêmes (type d'atelier, intrants, sortie et durée) ?</summary>
    public static bool SameRecipe(Recipe a, Recipe b) =>
        a.Workshop == b.Workshop && a.Output == b.Output && a.OutputAmount == b.OutputAmount && a.Seconds.Equals(b.Seconds) && a.Fuel == b.Fuel
        && a.Inputs.OrderBy(i => i.Type).SequenceEqual(b.Inputs.OrderBy(i => i.Type));

    // --- La demande ---

    /// <summary>Ce que la colonie veut avoir en stock de ce produit (0 hors pain et farine). La farine suit aussi le pain qu'il reste à cuire.</summary>
    private static int Target(Colony colony, ResourceType output, Activity? ignoring)
    {
        BreadDemand demand = FoodChain.Demand(colony);
        if (!demand.Active)
            return 0;
        if (output == ResourceType.Bread)
            return demand.BreadTarget;
        if (output != ResourceType.Flour)
            return 0;
        int breadGap = Math.Max(0, demand.BreadTarget - colony.Stock.Available(ResourceType.Bread) - Crafting.Expected(colony, ResourceType.Bread, ignoring));
        return Math.Max(demand.FlourTarget, FoodChain.FlourForBread(breadGap));
    }

    /// <summary>
    /// La demande nette d'un produit : l'objectif moins le stock libre, les sorties d'atelier, les fabrications engagées et les produits portés vers le dépôt. Jamais négative.
    /// <paramref name="ignoring"/> : l'activité qu'on revalide ne se compte pas elle-même.
    /// </summary>
    public static int NetDemand(Colony colony, ResourceType output, Activity? ignoring = null) =>
        Math.Max(0, Target(colony, output, ignoring) - colony.Stock.Available(output) - Crafting.Expected(colony, output, ignoring));

    /// <summary>Unités de sortie déjà engagées (en route, en cours ou en pause) dans cet atelier.</summary>
    public static int CommittedOutput(Colony colony, Building workshop, Activity? ignoring = null) =>
        WorkshopCapacity.Crafts(colony, workshop).Where(a => a != ignoring).Sum(a => (a.CommittedRecipe ?? a.PlannedRecipe)?.OutputAmount ?? 0);

    // --- Choisir un lot ---

    /// <summary>
    /// Le plus grand lot recevable, de la capacité de l'atelier jusqu'à 1 : demande nette (seul l'excédent indivisible d'une recette de base dépasse la demande), place de sortie,
    /// intrants libres une fois les engagements et les réserves vitales déduits. Null s'il n'y a aucun poste libre ni de lot possible.
    /// </summary>
    public static BatchChoice? Choose(Colony colony, Building workshop, Recipe reference, int heatingReserve, Activity? ignoring = null)
    {
        int slot = WorkshopCapacity.FindFreeSlot(colony, workshop, ignoring);
        int net = NetDemand(colony, reference.Output, ignoring);
        if (slot < 0 || net <= 0)
            return null;
        int room = WorkshopCapacity.OutputCapacity(colony, workshop, reference.OutputAmount) - CommittedOutput(colony, workshop, ignoring) - workshop.OutputUnits(reference.Output);
        int most = Math.Min(Math.Min(WorkshopCapacity.MaxBatch(colony, workshop), Math.Max(1, net / reference.OutputAmount)), room / reference.OutputAmount);
        for (int count = most; count >= 1; count--)
        {
            Recipe recipe = BuildRecipe(reference, count);
            if (HasInputs(colony, recipe, heatingReserve))
                return new BatchChoice(count, recipe, slot);
        }
        return null;
    }

    /// <summary>Les intrants libres suffisent-ils, sans entamer le chauffage ni le grain gardé en réserve ?</summary>
    private static bool HasInputs(Colony colony, Recipe recipe, int heatingReserve)
    {
        if (ToolChain.MissingInputs(colony, recipe).Count > 0)
            return false;
        foreach ((ResourceType type, int amount) in recipe.Inputs)
        {
            if (type == ResourceType.Wood && colony.Stock.Available(ResourceType.Wood) < amount + heatingReserve)
                return false;
            if (type == ResourceType.Grain && FoodChain.Demand(colony).GrainSurplus < amount)
                return false;
        }
        return true;
    }

    /// <summary>
    /// À l'arrivée : la recette de départ est-elle toujours la bonne, le poste toujours là et la demande toujours présente ? Sinon le lot est abandonné sans rien débiter.
    /// </summary>
    public static Recipe? Revalidate(Colony colony, Building workshop, Activity activity, Recipe reference)
    {
        if (activity.PlannedRecipe is not { } planned)
            return null;
        Recipe recipe = BuildRecipe(reference, activity.BatchCount);
        if (!SameRecipe(planned, recipe) || activity.WorkshopSlotId >= WorkshopCapacity.Slots(colony, workshop)
            || WorkshopCapacity.Crafts(colony, workshop).Any(a => a != activity && a.WorkshopSlotId == activity.WorkshopSlotId)
            || NetDemand(colony, reference.Output, activity) <= 0)
            return null;
        return recipe;
    }

    // --- Les sorties physiques ---

    /// <summary>Un lot achevé dépose ses produits et leur travail à la sortie de l'atelier ; l'artisan emporte ensuite ce qu'il peut porter.</summary>
    internal static void Deposit(Colonist colonist, Building workshop, Recipe recipe, long now)
    {
        double hours = colonist.WorkCycleExtraHours + (colonist.WorkCycleStartTicks >= 0 ? LaborLedger.TicksToHours(now - colonist.WorkCycleStartTicks) : 0);
        workshop.StoreOutput(recipe.Output, recipe.OutputAmount, hours);
        colonist.Colony.LocalSettlement.ScaleLedger.AddProduction(workshop, recipe.Output, recipe.OutputAmount);
        colonist.WorkCycleStartTicks = -1;
        colonist.WorkCycleExtraHours = 0;
        CollectInto(colonist, workshop, now);
    }

    /// <summary>
    /// Le colon, sur place, retire à la sortie ce qu'il peut porter : la quantité, les heures proportionnelles et le produit passent en bloc à ce qu'il porte, avec le début
    /// du trajet de livraison. Renvoie les unités prises (0 si les mains sont prises ou la sortie vide).
    /// </summary>
    internal static int CollectInto(Colonist colonist, Building workshop, long now)
    {
        if (colonist.Carrying is not null || workshop.OutputStock is not { } output)
            return 0;
        ResourceType? type = output.Amounts.Where(p => p.Value > 0).Select(p => (ResourceType?)p.Key).OrderBy(t => t == ResourceType.Bread ? 0 : 1).ThenBy(t => t).FirstOrDefault();
        if (type is not { } good)
            return 0;
        double hours = workshop.TakeOutput(good, ColonistAI.CarryCapacity, out int taken);
        if (taken == 0)
            return 0;
        colonist.Carrying = (good, taken);
        colonist.WorkCycleStartTicks = now;
        colonist.WorkCycleExtraHours = hours;
        return taken;
    }

    /// <summary>Les produits qui attendent à la sortie des ateliers de la colonie (tous établissements locaux), toujours comptés dans l'inventaire physique.</summary>
    public static int BufferedUnits(Colony colony, ResourceType output) =>
        colony.Buildings.Sum(b => b.OutputUnits(output));

    /// <summary>Nourriture récupérable à la sortie des ateliers (pain) : elle compte pour la survie avant même d'être livrée.</summary>
    public static int BufferedFood(Colony colony) => BufferedUnits(colony, ResourceType.Bread);

    /// <summary>
    /// L'atelier où un colon libre devrait aller chercher des produits finis : les artisans et ceux qui n'ont pas de travail s'en chargent ; si le dépôt n'a plus de vivres, n'importe quel
    /// adulte vient chercher le pain. Le plus proche d'abord, le pain avant le reste, puis l'identifiant.
    /// </summary>
    public static Building? PickCollection(Colony colony, Colonist colonist)
    {
        if (colonist.Carrying is not null || colonist.Stage != LifeStage.Adult)
            return null;
        bool starving = colony.Stock.FoodUnits < Math.Max(1, colony.PresentMembers.Count) * ColonyBrain.MealsPerColonistPerDay;
        bool volunteer = colonist.Sector is WorkSector.Craft or WorkSector.Free;
        return colony.Buildings
            .Where(b => b.IsComplete && b.OutputStock is { } stock && stock.Amounts.Any(p => p.Value > 0)
                && (volunteer || starving && b.OutputUnits(ResourceType.Bread) > 0))
            .OrderBy(b => b.OutputUnits(ResourceType.Bread) > 0 ? 0 : 1)
            .ThenBy(b => Math.Abs(b.X - colonist.TileX) + Math.Abs(b.Y - colonist.TileY)).ThenBy(b => b.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Un bâtiment disparaît : ses fabrications engagées sont annulées (les intrants encore présents retournent au stock, les pertes anciennes ne sont pas recréées) et sa sortie
    /// devient une perte physique, enregistrée une seule fois. Une extension détruite ferme les postes qui n'existent plus.
    /// </summary>
    internal static void OnBuildingDestroyed(Colony colony, Building building)
    {
        ColonistAI.AbandonCrafts(colony, a => a.Building == building);
        if (building.OutputStock is { } output)
        {
            foreach ((ResourceType type, int amount) in output.Amounts.Where(p => p.Value > 0).ToArray())
            {
                output.TryTake(type, amount, ResourceFlow.Loss);
                ResourceAccounting.Record(colony.Stock, type, ResourceFlow.Loss, amount);
            }
            building.OutputHours?.Clear();
        }
        if (building.IsExtension && colony.BuildingById(building.ExtensionOfId) is { } principal)
        {
            int slots = WorkshopCapacity.Slots(colony, principal);
            ColonistAI.AbandonCrafts(colony, a => a.Building == principal && a.WorkshopSlotId >= slots);
        }
    }
}
