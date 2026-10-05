using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les plats de fête. Le <b>gâteau</b> (œufs, lait et farine, cuit au four) se partage : un gâteau fait six parts, chacune rassasie entièrement
/// celui qui la mange et lui remonte le moral pour deux jours. Le <b>ragoût</b> (viande, œufs et céréales, mijoté au four ou à la taverne) donne
/// quatre bols qui rassasient, remontent le moral et, surtout, donnent un coup de fouet : <see cref="StewBoost"/> de vitesse de travail
/// pendant <see cref="StewBoostDays"/> jours. Les deux se mangent avant tout le reste (voir <see cref="Stockpile.TryTakeMeal(out float, out ResourceType?)"/>).
/// La <b>bière</b> se brasse dans un <b>fût</b> (un petit bâtiment de bois, près de la taverne) : on y verse 50 céréales, qui fermentent cinq jours et donnent 40 chopes
/// livrées à la taverne ; une chope bue à la taverne donne de l'entrain pour longtemps (<see cref="BeerDays"/> jours).
/// Ils ne se cuisinent que quand la survie est assurée et qu'il reste des ingrédients : ni les œufs ni le lait ne sont affamés pour un gâteau.
/// </summary>
public static class Cuisine
{
    public static readonly Recipe CakeRecipe = new(BuildingType.Oven,
        [(ResourceType.Eggs, 2), (ResourceType.Milk, 2), (ResourceType.Flour, 2)], ResourceType.Cake, 1, 10f);

    public static readonly Recipe StewRecipe = new(BuildingType.Tavern,
        [(ResourceType.Meat, 2), (ResourceType.Eggs, 1), (ResourceType.Grain, 2)], ResourceType.Stew, 4, 8f);

    /// <summary>La bière : on verse 50 céréales dans un fût (6 s de travail) ; cinq jours plus tard, 40 chopes sont prêtes.</summary>
    public static readonly Recipe BeerRecipe = new(BuildingType.Cask, [(ResourceType.Grain, 50)], ResourceType.Beer, 40, 6f);

    /// <summary>Jours de fermentation d'un fût.</summary>
    public const int BrewDays = 5;

    /// <summary>Un fût donne une fournée de 40 chopes tous les cinq jours, soit 8 par jour : de quoi abreuver une vingtaine de colons.</summary>
    public const int ColonistsPerCask = 20, MaxCasks = 4;

    /// <summary>
    /// Jours de repas en céréales qu'on garde intacts avant de brasser : une chope coûte deux céréales, que les habitants ne mangeraient plus,
    /// et la bière ne doit jamais entamer les vivres.
    /// </summary>
    public const float BeerGrainReserveDays = 3f;

    /// <summary>Céréales à garder en réserve avant de brasser : trois jours de repas pour la colonie, ou la réserve ordinaire si elle est plus petite.</summary>
    public static int BeerGrainReserve(Colony colony) =>
        Math.Max(GrainKept, (int)MathF.Ceiling(colony.PresentMembers.Count * ColonyBrain.MealsPerColonistPerDay * BeerGrainReserveDays));

    /// <summary>Durée de l'entrain que donne une chope : cinq jours, une saison entière. On reprend une chope quand il est retombé sous la moitié.</summary>
    public const int BeerDays = 5;
    public const float BeerRedrinkBelow = 0.5f;

    /// <summary>Gain de vitesse de travail que donne un bol de ragoût.</summary>
    public const float StewBoost = 0.25f;

    /// <summary>Durée de l'effet d'un ragoût : deux jours, soit deux journées de travail.</summary>
    public const int StewBoostDays = 2;

    /// <summary>Gaieté laissée par un gâteau et par un ragoût (elle s'estompe en deux jours).</summary>
    public const float CakeCheer = 1f, StewCheer = 0.6f;

    /// <summary>Céréales qu'on garde en réserve avant de s'en servir pour un ragoût.</summary>
    private const int GrainKept = 10;

    public static bool IsDish(ResourceType good) => good is ResourceType.Cake or ResourceType.Stew;

    /// <summary>Un gâteau pour huit habitants, plus un.</summary>
    public static int CakeTarget(Colony colony) => 1 + colony.PresentMembers.Count / 8;

    /// <summary>Un bol de ragoût pour chacun.</summary>
    public static int StewTarget(Colony colony) => colony.PresentMembers.Count;

    /// <summary>Viande, fraîche ou salée : le ragoût accepte l'une et l'autre.</summary>
    public static int MeatOnHand(Colony colony) => colony.Stock.Get(ResourceType.Meat) + colony.Stock.Get(ResourceType.SaltedMeat);

    public static Recipe? RecipeFor(ResourceType? product) => product switch
    {
        ResourceType.Cake => CakeRecipe,
        ResourceType.Stew => StewRecipe,
        ResourceType.Beer => BeerRecipe,
        _ => null,
    };

    /// <summary>
    /// Que cuisiner maintenant, et où ? Le gâteau au four ; le ragoût à la taverne si elle existe, sinon au four.
    /// Renvoie null si la survie n'est pas assurée, s'il n'y a pas de cuisine ou pas assez d'ingrédients.
    /// </summary>
    public static (Building Workshop, Recipe Recipe)? PickJob(Colony colony) => PickCake(colony) ?? PickStew(colony) ?? PickBrew(colony);

    /// <summary>Cinq jours de consommation, soit le temps qu'il faut à un fût pour fermenter : deux chopes par habitant.</summary>
    public static int BeerTarget(Colony colony) => 2 * colony.PresentMembers.Count;

    /// <summary>Un fût pour vingt colons (au moins un, quatre au plus).</summary>
    public static int CasksWanted(Colony colony) => Math.Clamp((colony.PresentMembers.Count + ColonistsPerCask - 1) / ColonistsPerCask, 1, MaxCasks);

    /// <summary>La colonie bâtit un fût (ou un fût de plus) dès qu'elle a une taverne et que ses fûts ne suffisent plus à ses habitants.</summary>
    public static bool WantsCask(Colony colony) =>
        Civic.Has(colony, BuildingType.Tavern) && colony.Buildings.Count(b => b.Type == BuildingType.Cask) < CasksWanted(colony);

    /// <summary>Chopes en cours de fermentation (ou prêtes, en attente d'être tirées) dans les fûts de la colonie.</summary>
    public static int MugsInCasks(Colony colony) => colony.Buildings.Count(b => b.Type == BuildingType.Cask && b.IsComplete && b.IsBrewing) * BeerRecipe.OutputAmount;

    /// <summary>
    /// Le gâteau : au four, quand il y a de quoi (œufs, lait, farine). Il passe avant le pain, dont il emprunterait sinon toute la farine :
    /// on ne le cuisine de toute façon que si la survie est assurée.
    /// </summary>
    public static (Building Workshop, Recipe Recipe)? PickCake(Colony colony)
    {
        Stockpile stock = colony.Stock;
        if (!(colony.Sensors?.SurvivalAssured ?? true) || colony.Workshops(BuildingType.Oven).FirstOrDefault() is not { } oven)
            return null;
        return stock.Get(ResourceType.Eggs) >= 2 && stock.Get(ResourceType.Milk) >= 2 && stock.Get(ResourceType.Flour) >= 2
            && stock.Get(ResourceType.Cake) + Crafting.Pending(colony, ResourceType.Cake) < CakeTarget(colony)
            ? (oven, CakeRecipe) : null;
    }

    /// <summary>Fraction de l'objectif sous laquelle le ragoût et la bière passent <b>avant</b> les autres chaînes d'artisanat (sinon elles les occuperaient sans cesse).</summary>
    public const float UrgentFill = 1f / 3f;

    /// <summary>
    /// Le ragoût : à la taverne si elle existe, sinon au four. <paramref name="fill"/> est la part de l'objectif qu'on veut atteindre
    /// (1 pour compléter le stock, <see cref="UrgentFill"/> pour s'y mettre en priorité quand il est presque vide).
    /// </summary>
    public static (Building Workshop, Recipe Recipe)? PickStew(Colony colony, float fill = 1f)
    {
        Stockpile stock = colony.Stock;
        if (!(colony.Sensors?.SurvivalAssured ?? true))
            return null;
        Building? kitchen = colony.Workshops(BuildingType.Tavern).FirstOrDefault() ?? colony.Workshops(BuildingType.Oven).FirstOrDefault();
        return kitchen is not null && MeatOnHand(colony) >= 2 && stock.Get(ResourceType.Eggs) >= 1 && stock.Get(ResourceType.Grain) >= 2 + GrainKept
            && stock.Get(ResourceType.Stew) + StewRecipe.OutputAmount * Crafting.Pending(colony, ResourceType.Stew) < StewTarget(colony) * fill
            ? (kitchen, StewRecipe) : null;
    }

    /// <summary>
    /// La bière : on remplit un fût vide (qu'aucun colon n'est déjà en train de remplir) quand la taverne existe, que le stock, les fûts en fermentation compris,
    /// est sous l'objectif (voir <paramref name="fill"/> pour le ragoût) et qu'il reste, au-delà de la réserve, de quoi verser 50 céréales.
    /// </summary>
    public static (Building Workshop, Recipe Recipe)? PickBrew(Colony colony, float fill = 1f)
    {
        Stockpile stock = colony.Stock;
        if (!(colony.Sensors?.SurvivalAssured ?? true) || !Civic.Has(colony, BuildingType.Tavern))
            return null;
        Building? cask = colony.Workshops(BuildingType.Cask).FirstOrDefault(c => !c.IsBrewing
            && !colony.PresentMembers.Any(m => m.Activity is { Kind: ActivityKind.Craft, Product: ResourceType.Beer } filling && filling.Building == c));
        return cask is not null && stock.Get(ResourceType.Grain) >= BeerRecipe.Inputs[0].Amount + BeerGrainReserve(colony)
            && stock.Get(ResourceType.Beer) + MugsInCasks(colony) + BeerRecipe.OutputAmount * Crafting.Pending(colony, ResourceType.Beer) < BeerTarget(colony) * fill
            ? (cask, BeerRecipe) : null;
    }

    /// <summary>Un colon vient de verser les céréales : le fût fermente cinq jours. <paramref name="hours"/> est ce que la fournée a coûté en travail.</summary>
    public static void StartBrewing(Colony colony, Building cask, double hours, GameClock clock)
    {
        cask.BrewReadyTicks = clock.Ticks + (cask.BrewProduct == ResourceType.Wine ? 3 : BrewDays) * TimeConstants.TicksPerDay;
        cask.BrewCostHours = hours;
        ColonyBrain.Say(colony, clock, $"On met un fût en perce : {BeerRecipe.Inputs[0].Amount} céréales fermenteront {BrewDays} jours pour donner {BeerRecipe.OutputAmount} chopes.");
    }

    /// <summary>
    /// Chaque heure : un fût dont la bière est prête est tiré, et ses chopes deviennent disponibles à la taverne (sans taverne, elles attendent dans le fût).
    /// Le coût de la fournée entre dans le registre de la colonie.
    /// </summary>
    public static void TickCasks(Colony colony, GameClock clock)
    {
        if (!Civic.Has(colony, BuildingType.Tavern))
            return;
        foreach (Building cask in colony.Buildings.Where(b => b.Type == BuildingType.Cask && b.IsComplete && b.IsBrewing && clock.Ticks >= b.BrewReadyTicks))
        {
            ResourceType product = cask.BrewProduct ?? ResourceType.Beer;
            int units = product == ResourceType.Wine ? 2 : BeerRecipe.OutputAmount;
            colony.Stock.Add(product, units);
            colony.Labor.Record(product, cask.BrewCostHours, units);
            cask.BrewReadyTicks = 0;
            cask.BrewCostHours = 0;
            cask.BrewProduct = null;
            ColonyBrain.Say(colony, clock, $"Le fût est tiré : {BeerRecipe.OutputAmount} chopes de bière sont disponibles à la taverne.");
        }
    }

    /// <summary>
    /// Un colon qui se détend à la taverne y boit une chope (si elle en a et si l'effet de la précédente est retombé sous la moitié) :
    /// son humeur monte pour <see cref="BeerDays"/> jours. Les enfants n'en boivent pas.
    /// </summary>
    public static void Drink(Colonist colonist, GameClock clock)
    {
        if (colonist.Stage == LifeStage.Child || colonist.Needs.BeerCheer >= BeerRedrinkBelow || !colonist.Colony.Stock.TryTake(ResourceType.Beer, 1))
            return;
        colonist.Needs.BeerCheer = 1f;
    }

    /// <summary>Un colon mange un plat : le moral monte, et le ragoût donne son coup de fouet.</summary>
    public static void Savor(Colonist colonist, ResourceType dish, GameClock clock)
    {
        if (dish == ResourceType.Cake)
            colonist.Needs.Cheer = Math.Max(colonist.Needs.Cheer, CakeCheer);
        else if (dish == ResourceType.Stew)
        {
            colonist.Needs.Cheer = Math.Max(colonist.Needs.Cheer, StewCheer);
            colonist.BoostUntilTicks = clock.Ticks + StewBoostDays * TimeConstants.TicksPerDay;
        }
    }

    /// <summary>Le multiplicateur de vitesse de travail d'un colon revigoré par un ragoût.</summary>
    public static float BoostFactor(Colonist colonist) => colonist.IsBoosted ? 1f + StewBoost : 1f;
}
