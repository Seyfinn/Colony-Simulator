namespace GodColony.Simulation.Colonies;

/// <summary>Caractéristiques physiques partagées par les stocks, les repas et les chargements.</summary>
public static class ResourceCatalog
{
    /// <summary>Poids en unités de portage ; une pièce pèse un centième, les minerais et les bêtes davantage.</summary>
    public static double Weight(ResourceType resource) => resource switch
    {
        ResourceType.Coins => 0.01,
        ResourceType.Stone or ResourceType.IronOre or ResourceType.Iron or ResourceType.Clay or ResourceType.CopperOre or ResourceType.Copper or ResourceType.GoldOre or ResourceType.Gold => 2,
        ResourceType.Carts => 8,
        ResourceType.Chickens => 2,
        ResourceType.Sheep => 8,
        ResourceType.Cows => 16,
        ResourceType.Horses => 16,
        ResourceType.Oxen => 20,
        ResourceType.Dogs => 2,
        _ => 1,
    };

    public static string Name(ResourceType resource) => resource switch
    {
        ResourceType.MineralCoal => "charbon minéral", ResourceType.Clay => "argile", ResourceType.Pottery => "poterie",
        ResourceType.CopperOre => "minerai de cuivre", ResourceType.Copper => "cuivre", ResourceType.Copperware => "objets de cuivre",
        ResourceType.Flax => "lin", ResourceType.Linen => "toile de lin", ResourceType.Hides => "peaux", ResourceType.Leather => "cuir",
        ResourceType.Shoes => "chaussures", ResourceType.Grapes => "raisin", ResourceType.Wine => "vin",
        ResourceType.GoldOre => "minerai d’or", ResourceType.Gold => "or", ResourceType.Ruby => "rubis",
        ResourceType.Sapphire => "saphir", ResourceType.Emerald => "émeraude", ResourceType.Diamond => "diamant", ResourceType.Jewelry => "bijoux",
        _ => Trade.GoodName(resource),
    };

    /// <summary>Coût commun aux affichages et au commerce ; les produits suivent leurs recettes actuelles.</summary>
    public static double ReferenceCost(ResourceType resource) => Economy.BaselineCost(resource);

    /// <summary>Référence des ressources brutes, avant les transformations et les coûts locaux mesurés.</summary>
    internal static double CoutBrutReference(ResourceType resource) => resource switch
    {
        ResourceType.Coins => 1,
        ResourceType.Food or ResourceType.Fish or ResourceType.Mushrooms => 2,
        ResourceType.Grain or ResourceType.Flax or ResourceType.Grapes => 1.5,
        ResourceType.Wood => 0.5,
        ResourceType.Stone or ResourceType.Clay => 3.5,
        ResourceType.IronOre => 5.5,
        ResourceType.CopperOre => 6,
        ResourceType.GoldOre => 8,
        ResourceType.MineralCoal or ResourceType.Wool or ResourceType.Milk or ResourceType.Herbs => 5,
        ResourceType.Eggs or ResourceType.Salt or ResourceType.Hides => 4,
        ResourceType.Meat => 3,
        ResourceType.SaltedMeat => 3 + 4d / Civic.MeatPerSalt,
        ResourceType.Spices => 6,
        ResourceType.Hardwood => 5,
        ResourceType.Chickens => 80,
        ResourceType.Sheep => 180,
        ResourceType.Cows or ResourceType.Oxen => 360,
        ResourceType.Horses => 420,
        ResourceType.Dogs => 90,
        ResourceType.Honey => 3,
        ResourceType.Wax => 4,
        ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald => 24,
        ResourceType.Diamond => 36,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, "Ce produit doit avoir une recette de référence."),
    };

    /// <summary>Les rubriques de présentation des stocks : la classification vient de la simulation, l'affichage ne l'invente pas.</summary>
    public enum Group { Food, Crops, Materials, Minerals, Processed }

    /// <summary>Les minéraux, dans l'ordre d'affichage : roches et sels, minerais bruts, métaux, puis pierres précieuses.</summary>
    public static readonly ResourceType[] Minerals =
    [
        ResourceType.Stone, ResourceType.Clay, ResourceType.Salt, ResourceType.MineralCoal,
        ResourceType.IronOre, ResourceType.CopperOre, ResourceType.GoldOre,
        ResourceType.Iron, ResourceType.Copper, ResourceType.Gold,
        ResourceType.Ruby, ResourceType.Sapphire, ResourceType.Emerald, ResourceType.Diamond,
    ];

    /// <summary>Les matières premières agricoles : les céréales ne se mangent qu'en dernier recours (voir <see cref="Stockpile.GrainMealValue"/>).</summary>
    public static readonly ResourceType[] Crops = [ResourceType.Grain, ResourceType.Flour, ResourceType.Flax];

    private static readonly ResourceType[] RawMaterials =
        [ResourceType.Wood, ResourceType.Hardwood, ResourceType.Charcoal, ResourceType.Hides, ResourceType.Wool, ResourceType.Wax];

    public static Group GroupOf(ResourceType resource) =>
        Minerals.Contains(resource) ? Group.Minerals : Crops.Contains(resource) ? Group.Crops
        : Nutrition(resource) > 0 ? Group.Food : RawMaterials.Contains(resource) ? Group.Materials : Group.Processed;

    public static string Category(ResourceType resource) => GroupOf(resource) switch
    {
        Group.Food => "Vivres",
        Group.Crops => "Cultures",
        Group.Minerals => "Minéraux",
        Group.Materials => "Matériaux",
        _ => "Produits transformés",
    };

    public static string Source(ResourceType resource) => resource switch
    {
        ResourceType.Flax or ResourceType.Grapes or ResourceType.Grain => "Cultures",
        ResourceType.Hides => "Abattage d’une bête réelle",
        ResourceType.Salt or ResourceType.Spices or ResourceType.Hardwood => "Site environnemental",
        _ => ExtendedIndustry.Recipes.Any(r => r.Output == resource) ? "Atelier et intrants physiques" : "Extraction ou récolte",
    };

    public static decimal Nutrition(ResourceType resource) => Stockpile.NutritionPerItem(resource);

    /// <summary>Les vivres transportables qui peuvent aussi ravitailler les voyageurs, dans un ordre stable : les céréales crues, presque sans valeur nutritive, ferment la marche.</summary>
    public static readonly ResourceType[] TravelFood =
        [ResourceType.Bread, ResourceType.SaltedMeat, ResourceType.Eggs, ResourceType.Milk, ResourceType.Grain];

    public static double WeightOf(IEnumerable<KeyValuePair<ResourceType, int>> inventory) =>
        inventory.Sum(p => Weight(p.Key) * p.Value);
}
