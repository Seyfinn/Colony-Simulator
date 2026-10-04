using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'artisanat de la colonie, toutes chaînes confondues (fer, blé) : quelle recette fait quoi, quel atelier bâtir
/// ensuite, et où aller travailler. Chaque chaîne décide de ses propres besoins ; ici on les arbitre.
/// </summary>
public static class Crafting
{
    public static Recipe RecipeFor(BuildingType workshop) => workshop == BuildingType.Loom ? Husbandry.Weaving
        : FoodChain.IsFoodWorkshop(workshop) ? FoodChain.RecipeFor(workshop) : ToolChain.RecipeFor(workshop);

    /// <summary>La recette d'un atelier de cette colonie : celle du marché dépend de la denrée que produit sa région.</summary>
    public static Recipe RecipeFor(Colony colony, BuildingType workshop) =>
        workshop == BuildingType.Market ? Specialties.RecipeFor(colony) : RecipeFor(workshop);

    /// <summary>La recette suivie : celle du produit visé s'il y en a un (les plats de fête), sinon la recette habituelle de l'atelier.</summary>
    public static Recipe RecipeFor(Colony colony, BuildingType workshop, ResourceType? product) =>
        Cuisine.RecipeFor(product) ?? RecipeFor(colony, workshop);

    /// <summary>La compétence qu'on exerce dans cet atelier : la boulangerie pour le moulin et le four, le tissage, le négoce, la forge pour le reste.</summary>
    public static SkillType SkillFor(BuildingType workshop) => workshop switch
    {
        BuildingType.Loom => SkillType.Weaving,
        BuildingType.Market => SkillType.Trading,
        BuildingType.Tavern or BuildingType.Cask => SkillType.Cooking,
        _ => FoodChain.IsFoodWorkshop(workshop) ? SkillType.Cooking : SkillType.Smithing,
    };

    /// <summary>Ce qui est déjà en train d'être fabriqué (les fabricants ont pris les matières mais n'ont pas fini).</summary>
    public static int Pending(Colony colony, ResourceType output) =>
        colony.Members.Count(m => m.Activity is { Kind: ActivityKind.Craft, Building: { } site } a && (a.Product ?? RecipeFor(colony, site.Type).Output) == output);

    /// <summary>
    /// Le prochain atelier à bâtir. Le pain passe avant le fer : manger mieux libère des bras pour tout le reste.
    /// Un atelier dont la colonie ignore le savoir attend qu'elle l'ait découvert (voir <see cref="Knowledge"/>).
    /// </summary>
    public static BuildingType? NextWorkshopToBuild(Colony colony, LocalMap map)
    {
        if (FoodChain.NextWorkshopToBuild(colony, map) is { } food && Knowledge.Allows(colony, food))
            return food;
        return ToolChain.NextWorkshopToBuild(colony) is { } iron && Knowledge.Allows(colony, iron) ? iron : null;
    }

    /// <summary>Où travailler maintenant, s'il y a quelque chose d'utile à fabriquer.</summary>
    public static Building? PickJob(Colony colony, int heatingReserve) =>
        FoodChain.PickJob(colony, heatingReserve) ?? ToolChain.PickJob(colony, heatingReserve)
        ?? Husbandry.PickLoomJob(colony) ?? Specialties.PickMarketJob(colony);
}
