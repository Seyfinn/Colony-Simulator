namespace GodColony.Simulation.Colonies;

/// <summary>
/// Une recette : dans un atelier, des matières deviennent un produit. <c>Seconds</c> est la durée du geste à vitesse ×1
/// pour un ouvrier moyen : plus la tâche est difficile, plus elle est longue (moudre, brûler du charbon, cuire, tisser, fondre, forger).
/// Les coûts de référence d'<see cref="Economy.BaselineCost"/> suivent ces durées.
/// </summary>
public sealed record Recipe(BuildingType Workshop, (ResourceType Type, int Amount)[] Inputs, ResourceType Output, int OutputAmount, float Seconds);

/// <summary>
/// Ce que la colonie veut produire pour s'équiper, en remontant la chaîne : outils → fer → charbon de bois,
/// minerai et bois. Tout est nul tant que la colonie n'a pas découvert le fer ou qu'elle est assez équipée.
/// </summary>
public sealed record ChainDemand(
    int ToolsWanted, int ToolShortfall,
    int IronTarget, int CharcoalTarget,
    int OreMissing, int WoodForCharcoal,
    bool Active)
{
    public static readonly ChainDemand None = new(0, 0, 0, 0, 0, 0, false);
}

/// <summary>
/// La chaîne du fer : le bois devient charbon de bois (charbonnière), le minerai et le charbon deviennent du fer
/// (bas fourneau), le fer et le charbon deviennent des outils (forge). Les outils accélèrent le bûcheronnage,
/// le minage, l'agriculture et la construction, puis s'usent à l'ouvrage.
/// </summary>
public static class ToolChain
{
    /// <summary>Un outil pour deux travailleurs suffit à équiper toute la colonie (on se les passe).</summary>
    public const float ToolsPerWorker = 0.5f;

    /// <summary>Vitesse gagnée par une colonie entièrement équipée.</summary>
    public const float ToolSpeedBonus = 0.35f;

    /// <summary>Nombre de gestes de travail qu'un outil supporte avant de casser.</summary>
    public const float ToolLifeUses = 150f;

    /// <summary>On ne lance pas plus de trois outils d'un coup : la colonie ne s'endette pas en matières premières.</summary>
    private const int MaxBatchTools = 3;

    private const int WoodPerBatch = 6;
    private const int CharcoalPerBatch = 3;
    private const int OrePerIron = 3;
    private const int CharcoalPerIron = 2;
    private const int IronPerTool = 2;
    private const int CharcoalPerTool = 1;

    private static readonly Recipe Charring = new(BuildingType.Kiln, [(ResourceType.Wood, WoodPerBatch)], ResourceType.Charcoal, CharcoalPerBatch, 12f);
    private static readonly Recipe Smelting = new(BuildingType.Bloomery, [(ResourceType.IronOre, OrePerIron), (ResourceType.Charcoal, CharcoalPerIron)], ResourceType.Iron, 1, 24f);
    private static readonly Recipe Forging = new(BuildingType.Forge, [(ResourceType.Iron, IronPerTool), (ResourceType.Charcoal, CharcoalPerTool)], ResourceType.Tools, 1, 32f);

    public static Recipe RecipeFor(BuildingType workshop) => workshop switch
    {
        BuildingType.Kiln => Charring,
        BuildingType.Bloomery => Smelting,
        BuildingType.Forge => Forging,
        _ => throw new ArgumentException("Une hutte ne fabrique rien.", nameof(workshop)),
    };

    /// <summary>Les métiers qu'un outil aide.</summary>
    public static bool UsesTools(SkillType skill) =>
        skill is SkillType.Woodcutting or SkillType.Mining or SkillType.Farming or SkillType.Construction;

    /// <summary>Ce qu'il faut d'outils à la colonie pour ses propres travailleurs.</summary>
    public static int ToolsWanted(Colony colony) => (int)MathF.Ceiling(colony.Workers.Count() * ToolsPerWorker);

    /// <summary>Ce que la colonie veut en stock : ses propres outils, plus ceux que ses voisines lui achèteraient.</summary>
    public static int ToolsTarget(Colony colony) => ToolsWanted(colony) + colony.ExportInterest.GetValueOrDefault(ResourceType.Tools);

    /// <summary>La part des travailleurs équipée d'un outil, de 0 à 1.</summary>
    public static float Coverage(Colony colony)
    {
        int tools = colony.Stock.Get(ResourceType.Tools);
        int wanted = ToolsWanted(colony);
        return tools == 0 || wanted == 0 ? 0f : MathF.Min(1f, tools / (float)wanted);
    }

    /// <summary>Multiplicateur de vitesse que donne l'équipement de la colonie à ce métier.</summary>
    public static float SpeedFactor(Colony colony, SkillType skill) =>
        UsesTools(skill) ? 1f + ToolSpeedBonus * Coverage(colony) : 1f;

    /// <summary>Un geste de travail use les outils ; renvoie true si l'un d'eux vient de casser.</summary>
    public static bool RecordUse(Colony colony, SkillType skill)
    {
        if (!UsesTools(skill) || colony.Stock.Get(ResourceType.Tools) == 0)
            return false;
        colony.ToolWear += Coverage(colony) / ToolLifeUses;
        if (colony.ToolWear < 1f)
            return false;
        colony.ToolWear -= 1f;
        return colony.Stock.TryTake(ResourceType.Tools, 1);
    }

    /// <summary>La colonie a-t-elle déjà vu du minerai de fer ? Sans cette découverte, elle ne pense pas aux outils de fer.</summary>
    public static bool IronDiscovered(Colony colony) =>
        colony.IronSeen || colony.Labor.TotalProduced(ResourceType.IronOre) > 0 || colony.Stock.Get(ResourceType.IronOre) > 0
        || colony.Stock.Get(ResourceType.Iron) > 0 || colony.Stock.Get(ResourceType.Tools) > 0;

    public static ChainDemand Demand(Colony colony)
    {
        int wanted = ToolsTarget(colony);
        int shortfall = Math.Max(0, wanted - colony.Stock.Get(ResourceType.Tools));
        if (shortfall == 0 || !IronDiscovered(colony))
            return ChainDemand.None with { ToolsWanted = wanted };

        int batch = Math.Min(shortfall, MaxBatchTools);
        int ironTarget = batch * IronPerTool;
        int ironMissing = Math.Max(0, ironTarget - colony.Stock.Get(ResourceType.Iron));
        int charcoalTarget = batch * CharcoalPerTool + ironMissing * CharcoalPerIron;
        int charcoalMissing = Math.Max(0, charcoalTarget - colony.Stock.Get(ResourceType.Charcoal));
        int oreMissing = Math.Max(0, ironMissing * OrePerIron - colony.Stock.Get(ResourceType.IronOre));
        int woodForCharcoal = (int)MathF.Ceiling(charcoalMissing / (float)CharcoalPerBatch) * WoodPerBatch;
        return new ChainDemand(wanted, shortfall, ironTarget, charcoalTarget, oreMissing, woodForCharcoal, true);
    }

    /// <summary>Le prochain atelier à bâtir, dans l'ordre de la chaîne : charbonnière, bas fourneau, forge.</summary>
    public static BuildingType? NextWorkshopToBuild(Colony colony)
    {
        if (!Demand(colony).Active)
            return null;
        foreach (BuildingType type in new[] { BuildingType.Kiln, BuildingType.Bloomery, BuildingType.Forge })
            if (!colony.Buildings.Any(b => b.Type == type))
                return type;
        return null;
    }

    /// <summary>
    /// Que fabriquer maintenant ? On finit par le bout de la chaîne : forger si l'on a de quoi, sinon fondre,
    /// sinon brûler du charbon (sans entamer le bois de chauffage). Renvoie l'atelier où aller travailler.
    /// </summary>
    public static Building? PickJob(Colony colony, int heatingReserve)
    {
        ChainDemand demand = Demand(colony);
        if (!demand.Active)
            return null;
        Stockpile stock = colony.Stock;

        bool Has((ResourceType Type, int Amount)[] inputs) => inputs.All(i => stock.Get(i.Type) >= i.Amount);

        if (colony.Workshops(BuildingType.Forge).FirstOrDefault() is { } forge
            && Has(Forging.Inputs) && demand.ToolShortfall > Crafting.Pending(colony, ResourceType.Tools))
            return forge;
        if (colony.Workshops(BuildingType.Bloomery).FirstOrDefault() is { } bloomery
            && Has(Smelting.Inputs) && stock.Get(ResourceType.Iron) + Crafting.Pending(colony, ResourceType.Iron) < demand.IronTarget)
            return bloomery;
        if (colony.Workshops(BuildingType.Kiln).FirstOrDefault() is { } kiln
            && stock.Get(ResourceType.Wood) >= WoodPerBatch + heatingReserve
            && stock.Get(ResourceType.Charcoal) + CharcoalPerBatch * Crafting.Pending(colony, ResourceType.Charcoal) < demand.CharcoalTarget)
            return kiln;
        return null;
    }

    /// <summary>Prend au stock les matières de la recette. Renvoie le coût en heures de travail de ce qu'on a pris.</summary>
    internal static bool TryTakeInputs(Colony colony, Recipe recipe, out double inputHours)
    {
        inputHours = 0;
        if (!recipe.Inputs.All(i => colony.Stock.Get(Source(colony, i.Type, i.Amount)) >= i.Amount))
            return false;
        foreach ((ResourceType type, int amount) in recipe.Inputs)
        {
            colony.Stock.TryTake(Source(colony, type, amount), amount);
            inputHours += amount * (colony.Labor.HoursPerUnit(type) ?? 0.0);
        }
        return true;
    }

    /// <summary>La viande salée remplace la viande fraîche dans une recette quand celle-ci manque.</summary>
    private static ResourceType Source(Colony colony, ResourceType type, int amount) =>
        type == ResourceType.Meat && colony.Stock.Get(ResourceType.Meat) < amount ? ResourceType.SaltedMeat : type;

    internal static void Refund(Colony colony, Recipe recipe)
    {
        foreach ((ResourceType type, int amount) in recipe.Inputs)
            colony.Stock.Add(type, amount, ResourceFlow.Transfer);
    }
}
