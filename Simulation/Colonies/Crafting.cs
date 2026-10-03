using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'artisanat de la colonie, toutes chaînes confondues (fer, blé) : quelle recette fait quoi, quel atelier bâtir
/// ensuite, et où aller travailler. Chaque chaîne décide de ses propres besoins ; ici on les arbitre.
/// </summary>
public static class Crafting
{
    public static Recipe RecipeFor(BuildingType workshop) =>
        FoodChain.IsFoodWorkshop(workshop) ? FoodChain.RecipeFor(workshop) : ToolChain.RecipeFor(workshop);

    /// <summary>La compétence qu'on exerce dans cet atelier : la boulangerie pour le moulin et le four, la forge pour le reste.</summary>
    public static SkillType SkillFor(BuildingType workshop) => FoodChain.IsFoodWorkshop(workshop) ? SkillType.Cooking : SkillType.Smithing;

    /// <summary>Ce qui est déjà en train d'être fabriqué (les fabricants ont pris les matières mais n'ont pas fini).</summary>
    public static int Pending(Colony colony, ResourceType output) =>
        colony.Members.Count(m => m.Activity is { Kind: ActivityKind.Craft, Building: { } site } && RecipeFor(site.Type).Output == output);

    /// <summary>
    /// Le prochain atelier à bâtir. Le pain passe avant le fer : manger mieux libère des bras pour tout le reste.
    /// </summary>
    public static BuildingType? NextWorkshopToBuild(Colony colony, LocalMap map) =>
        FoodChain.NextWorkshopToBuild(colony, map) ?? ToolChain.NextWorkshopToBuild(colony);

    /// <summary>Où travailler maintenant, s'il y a quelque chose d'utile à fabriquer.</summary>
    public static Building? PickJob(Colony colony, int heatingReserve) =>
        FoodChain.PickJob(colony, heatingReserve) ?? ToolChain.PickJob(colony, heatingReserve);
}
