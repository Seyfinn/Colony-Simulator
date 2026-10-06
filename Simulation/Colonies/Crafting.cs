using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'artisanat de la colonie, toutes chaînes confondues (fer, blé) : quelle recette fait quoi, quel atelier bâtir
/// ensuite, et où aller travailler. Chaque chaîne décide de ses propres besoins ; ici on les arbitre.
/// </summary>
public static class Crafting
{
    public static Recipe RecipeFor(BuildingType workshop) => workshop is BuildingType.PotteryKiln or BuildingType.Tannery or BuildingType.Goldsmith or BuildingType.Mint
        ? ExtendedIndustry.Recipes.First(r => r.Workshop == workshop) : workshop == BuildingType.Loom ? Husbandry.Weaving
        : FoodChain.IsFoodWorkshop(workshop) ? FoodChain.RecipeFor(workshop) : ToolChain.RecipeFor(workshop);

    /// <summary>La recette d'un atelier de cette colonie : celle du marché dépend de la denrée que produit sa région.</summary>
    public static Recipe RecipeFor(Colony colony, BuildingType workshop) =>
        workshop == BuildingType.Market ? Specialties.RecipeFor(colony) : FoodChain.IsFoodWorkshop(workshop) ? FoodChain.RecipeFor(colony, workshop) : RecipeFor(workshop);

    /// <summary>La recette suivie : celle du produit visé s'il y en a un (les plats de fête), sinon la recette habituelle de l'atelier.</summary>
    public static Recipe RecipeFor(Colony colony, BuildingType workshop, ResourceType? product) =>
        (product is null ? null : ExtendedIndustry.RecipeFor(colony, workshop, product)) ?? Cuisine.RecipeFor(product) ?? RecipeFor(colony, workshop);

    /// <summary>La compétence qu'on exerce dans cet atelier : la boulangerie pour le moulin et le four, le tissage, le négoce, la forge pour le reste.</summary>
    public static SkillType SkillFor(BuildingType workshop) => workshop switch
    {
        BuildingType.Loom => SkillType.Weaving,
        BuildingType.Market => SkillType.Trading,
        BuildingType.Tavern or BuildingType.Cask => SkillType.Cooking,
        _ => FoodChain.IsFoodWorkshop(workshop) ? SkillType.Cooking : SkillType.Smithing,
    };

    /// <summary>Ce qui est déjà en train d'être fabriqué, en nombre d'activités (voir <see cref="PendingUnits"/> pour les unités exactes).</summary>
    public static int Pending(Colony colony, ResourceType output) =>
        colony.PresentMembers.Sum(m => new[] { m.Activity, m.PausedCraft }.Count(a => a is { Kind: ActivityKind.Craft, Building: { } site }
            && (a.Product ?? RecipeFor(colony, site.Type).Output) == output));

    /// <summary>
    /// Les unités exactes de ce produit que les fabrications engagées ou en route vont donner : la recette engagée si les intrants sont pris, sinon la recette proposée, sinon
    /// la recette habituelle. Une activité en pause ne se compte qu'une fois ; <paramref name="ignoring"/> est celle qu'on revalide.
    /// </summary>
    public static int PendingUnits(Colony colony, ResourceType output, Activity? ignoring = null)
    {
        int units = 0;
        foreach (Colonist colonist in colony.PresentMembers)
            foreach (Activity? craft in new[] { colonist.Activity, colonist.PausedCraft }.Distinct())
                if (craft is { Kind: ActivityKind.Craft, Building: { } site } && craft != ignoring
                    && (craft.Product ?? RecipeFor(colony, site.Type).Output) == output)
                    units += (craft.CommittedRecipe ?? craft.PlannedRecipe ?? RecipeFor(colony, site.Type, craft.Product)).OutputAmount;
        return units;
    }

    /// <summary>Les produits portés vers un dépôt : ils ne sont ni au stock ni encore à fabriquer.</summary>
    public static int CarriedUnits(Colony colony, ResourceType output) =>
        colony.PresentMembers.Sum(m => m.Carrying is { } load && m.CarryingTo is null && load.Type == output ? load.Amount : 0);

    /// <summary>
    /// Tout ce qui arrivera au stock sans nouveau travail : fabrications engagées, sorties qui attendent à l'atelier et produits portés. Centralisé ici pour que
    /// les chaînes, les exportations et les extensions ne réclament pas chacune les mêmes unités.
    /// </summary>
    public static int Expected(Colony colony, ResourceType output, Activity? ignoring = null) =>
        PendingUnits(colony, output, ignoring) + BatchProduction.BufferedUnits(colony, output) + CarriedUnits(colony, output);

    /// <summary>
    /// Le prochain atelier à bâtir. Le pain passe avant le fer : manger mieux libère des bras pour tout le reste.
    /// Un atelier dont la colonie ignore le savoir attend qu'elle l'ait découvert (voir <see cref="Knowledge"/>).
    /// </summary>
    public static BuildingType? NextWorkshopToBuild(Colony colony, LocalMap map)
    {
        if (FoodChain.NextWorkshopToBuild(colony, map) is { } food && Knowledge.Allows(colony, food))
            return food;
        return ToolChain.NextWorkshopToBuild(colony) is { } iron && Knowledge.Allows(colony, iron) ? iron : ExtendedIndustry.NextWorkshop(colony);
    }

    /// <summary>
    /// Les travaux d'atelier recevables, dans l'ordre de priorité de la colonie (le premier est celui qu'on choisit sans préférence). Évalués à la demande : un colon sans préférence
    /// n'en regarde jamais plus d'un. Le gâteau, le ragoût et la bière urgents passent avant tout ; une offrande en chantier ensuite ; puis la frappe et l'affinage de l'or, les
    /// chaînes (blé, fer, textile, négoce), les plats de fête restants et les autres industries.
    /// </summary>
    internal static IEnumerable<CraftCandidate> Candidates(Colony colony, int heatingReserve)
    {
        (Building Workshop, Recipe Recipe)? urgent = Cuisine.PickCake(colony) ?? Cuisine.PickStew(colony, Cuisine.UrgentFill) ?? Cuisine.PickBrew(colony, Cuisine.UrgentFill);
        if (urgent is { } first)
            yield return new CraftCandidate(first.Workshop, first.Recipe.Output);
        else if (Offerings.PickSculptJob(colony) is { } shrine)
            yield return new CraftCandidate(shrine, null, Sculpt: true);
        if (urgent is null && ExtendedIndustry.PickMonetaryJob(colony) is { } monetary)
            yield return new CraftCandidate(monetary.Workshop, monetary.Recipe.Output);
        if (urgent is null && PickJob(colony, heatingReserve) is { } chain)
            yield return new CraftCandidate(chain, null);
        if ((Cuisine.PickStew(colony) ?? Cuisine.PickBrew(colony)) is { } dish)
            yield return new CraftCandidate(dish.Workshop, dish.Recipe.Output);
        if (ExtendedIndustry.PickJob(colony) is { } industry)
            yield return new CraftCandidate(industry.Workshop, industry.Recipe.Output);
    }

    /// <summary>Où travailler maintenant, s'il y a quelque chose d'utile à fabriquer.</summary>
    public static Building? PickJob(Colony colony, int heatingReserve) =>
        FoodChain.PickJob(colony, heatingReserve) ?? ToolChain.PickJob(colony, heatingReserve)
        ?? Husbandry.PickLoomJob(colony) ?? Specialties.PickMarketJob(colony);
}
