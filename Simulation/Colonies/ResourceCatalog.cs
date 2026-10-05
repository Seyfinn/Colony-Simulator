namespace GodColony.Simulation.Colonies;

/// <summary>Caractéristiques physiques partagées par les stocks, les repas et les chargements.</summary>
public static class ResourceCatalog
{
    /// <summary>Poids en unités de portage ; une pièce pèse un centième, les minerais et les bêtes davantage.</summary>
    public static double Weight(ResourceType resource) => resource switch
    {
        ResourceType.Coins => 0.01,
        ResourceType.Stone or ResourceType.IronOre or ResourceType.Iron or ResourceType.Clay or ResourceType.CopperOre or ResourceType.Copper or ResourceType.GoldOre or ResourceType.Gold => 2,
        ResourceType.Chickens => 2,
        ResourceType.Sheep => 8,
        ResourceType.Cows => 16,
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

    public static double ReferenceCost(ResourceType resource) => resource switch
    {
        ResourceType.Gold => 72, ResourceType.Jewelry => 110,
        ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond => 32,
        ResourceType.Copper => 40, ResourceType.Copperware => 95, ResourceType.Shoes => 25,
        ResourceType.Pottery or ResourceType.Leather or ResourceType.Linen => 12,
        ResourceType.CopperOre or ResourceType.GoldOre => 7,
        ResourceType.MineralCoal => 5, ResourceType.Wine => 9, _ => 3,
    };

    public static string Category(ResourceType resource) => Nutrition(resource) > 0 ? "Vivres"
        : resource is ResourceType.IronOre or ResourceType.CopperOre or ResourceType.GoldOre or ResourceType.Clay or ResourceType.MineralCoal ? "Sous-sol"
        : (int)resource >= 27 ? "Filières et équipement" : "Ressources du village";

    public static string Source(ResourceType resource) => resource switch
    {
        ResourceType.Flax or ResourceType.Grapes or ResourceType.Grain => "Cultures",
        ResourceType.Hides => "Abattage d’une bête réelle",
        ResourceType.Salt or ResourceType.Spices or ResourceType.Hardwood => "Site environnemental",
        _ => ExtendedIndustry.Recipes.Any(r => r.Output == resource) ? "Atelier et intrants physiques" : "Extraction ou récolte",
    };

    public static decimal Nutrition(ResourceType resource) => Stockpile.NutritionPerItem(resource);

    /// <summary>Les vivres transportables qui peuvent aussi ravitailler les voyageurs, dans un ordre stable.</summary>
    public static readonly ResourceType[] TravelFood =
        [ResourceType.Bread, ResourceType.Grain, ResourceType.SaltedMeat, ResourceType.Eggs, ResourceType.Milk];

    public static double WeightOf(IEnumerable<KeyValuePair<ResourceType, int>> inventory) =>
        inventory.Sum(p => Weight(p.Key) * p.Value);
}
