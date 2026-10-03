using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'économie vue par une colonie : ce que lui coûte chaque bien en heures de travail, ce qu'elle en a de trop ou
/// de pas assez, et donc ce qu'il vaut pour elle. Deux colonies qui n'estiment pas un bien de la même façon ont
/// intérêt à l'échanger : c'est la base du commerce.
///
/// La monnaie commune est ancrée sur le travail : une pièce vaut à peu près une heure de travail d'un colon.
/// </summary>
public static class Economy
{
    /// <summary>Les biens qu'on échange (la nourriture sauvage pourrit en route ; la monnaie n'est pas un bien).</summary>
    public static readonly ResourceType[] Tradable =
    [
        ResourceType.Grain, ResourceType.Bread, ResourceType.Flour, ResourceType.Wood, ResourceType.Stone,
        ResourceType.IronOre, ResourceType.Charcoal, ResourceType.Iron, ResourceType.Tools,
    ];

    /// <summary>Pièces par heure de travail : l'ancre de toute la monnaie.</summary>
    public const double CoinsPerHour = 1.0;

    /// <summary>Un bien qu'on n'a jamais produit coûterait plus cher que la normale à qui s'y mettrait : on l'apprend sur le tas.</summary>
    private const double UnprovenFactor = 1.5;

    /// <summary>Jours de repas de céréales qu'on veut garder en grenier avant de les trouver abondantes.</summary>
    private const float GrainDays = 6f;

    /// <summary>Un bien moins rare que ce multiple du besoin ne vaut plus que le plancher du prix.</summary>
    private const double GlutFloor = 0.6;

    /// <summary>Coût de référence (heures par unité) d'un bien qu'une colonie n'a jamais produit : ce que coûtent les autres colonies en moyenne.</summary>
    public static double BaselineCost(ResourceType good) => good switch
    {
        ResourceType.Grain => 1.5,
        ResourceType.Bread => 4.5,
        ResourceType.Flour => 3.4,
        ResourceType.Wood => 0.5,
        ResourceType.Stone => 3.5,
        ResourceType.IronOre => 5.5,
        ResourceType.Charcoal => 3.2,
        ResourceType.Iron => 30.0,
        ResourceType.Tools => 70.0,
        _ => 1.0,
    };

    /// <summary>Heures de travail que coûte une unité à cette colonie : son registre, ou à défaut la référence majorée.</summary>
    public static double Cost(Colony colony, ResourceType good) =>
        colony.Labor.HoursPerUnit(good) ?? BaselineCost(good) * UnprovenFactor;

    /// <summary>La quantité que la colonie voudrait avoir en réserve pour ses propres besoins (0 si le bien ne lui sert à rien).</summary>
    public static float Need(Colony colony, ResourceType good)
    {
        float daily = Math.Max(1, colony.Members.Count) * ColonyBrain.MealsPerColonistPerDay;
        ChainDemand iron = ToolChain.Demand(colony);
        return good switch
        {
            ResourceType.Grain => daily * GrainDays,
            ResourceType.Bread => colony.Buildings.Any(b => b.Type == BuildingType.Oven) ? FoodChain.Demand(colony).BreadTarget : 0f,
            ResourceType.Flour => colony.Buildings.Any(b => b.Type == BuildingType.Mill) ? FoodChain.Demand(colony).FlourTarget : 0f,
            ResourceType.Wood => ColonyBrain.HeatingTarget(colony, colony.Clock.Season) + 10f,
            ResourceType.Stone => ColonyBrain.StoneReserveTarget,
            // Pour le minerai, le fer et le charbon : ce qu'il manque à la chaîne des outils, en plus de ce qu'on a déjà.
            ResourceType.IronOre => iron.Active ? colony.Stock.Get(ResourceType.IronOre) + iron.OreMissing : 0f,
            ResourceType.Charcoal => iron.Active ? iron.CharcoalTarget : 0f,
            ResourceType.Iron => iron.Active ? iron.IronTarget : 0f,
            ResourceType.Tools => ToolChain.ToolsWanted(colony),
            _ => 0f,
        };
    }

    /// <summary>
    /// Rareté du bien pour la colonie : 2 quand elle n'en a pas du tout alors qu'elle en a besoin, 1 quand elle a juste
    /// ce qu'il faut, et jusqu'à 0,6 quand elle en regorge (ou n'en a pas l'usage).
    /// </summary>
    public static double Scarcity(Colony colony, ResourceType good)
    {
        float need = Need(colony, good);
        int stock = colony.Stock.Get(good);
        if (need <= 0f)
            return stock > 0 ? GlutFloor : 1.0;
        double ratio = stock / (double)need;
        return ratio < 1 ? 1 + (1 - ratio) : Math.Max(GlutFloor, 1 - 0.2 * (ratio - 1));
    }

    /// <summary>Ce que vaut une unité du bien pour cette colonie, en heures de travail (donc en pièces) : son coût fois sa rareté.</summary>
    public static double Value(Colony colony, ResourceType good) => Cost(colony, good) * Scarcity(colony, good);

    /// <summary>Ce que la colonie peut vendre sans se priver : le stock au-delà d'un quart de plus que ses besoins.</summary>
    public static int Surplus(Colony colony, ResourceType good) =>
        (int)Math.Max(0f, colony.Stock.Get(good) - Need(colony, good) * 1.25f);

    /// <summary>Ce qui lui manque pour ses besoins.</summary>
    public static int Shortage(Colony colony, ResourceType good) =>
        (int)Math.Max(0f, MathF.Ceiling(Need(colony, good) - colony.Stock.Get(good)));
}
