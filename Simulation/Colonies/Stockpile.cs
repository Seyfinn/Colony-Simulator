namespace GodColony.Simulation.Colonies;

/// <summary>
/// Nourriture sauvage (baies, poisson), céréales de la moisson, bois, pierre, minerai de fer,
/// les produits de la chaîne du fer (charbon de bois, fer, outils), ceux de la chaîne du blé (farine, pain)
/// la monnaie commune à toutes les colonies, les œufs, la laine et les vêtements de l'élevage,
/// trois denrées de négoce propres à chaque biome (sel, épices, bois dur), le lait des vaches
/// les bêtes vivantes (poules, moutons, vaches) qu'on garde en réserve ou qu'on vend, en attendant de les mettre à l'enclos,
/// la viande (fraîche, qui vieillit puis se gâte, ou salée, qui se garde) et trois produits de fête : le gâteau, le ragoût et la bière.
/// </summary>
public enum ResourceType { Food, Grain, Wood, Stone, IronOre, Charcoal, Iron, Tools, Flour, Bread, Coins, Fish, Eggs, Wool, Clothes, Salt, Spices, Hardwood, Milk, Chickens, Sheep, Cows, Meat, SaltedMeat, Cake, Stew, Beer }

/// <summary>Le stock commun de la colonie : tout appartient à la colonie, rien aux colons.</summary>
public sealed class Stockpile
{
    private readonly Dictionary<ResourceType, int> _amounts = [];

    /// <summary>La viande fraîche par âge (jours écoulés depuis l'abattage → quantité) : elle se gâte une fois trop vieille. Le total reste dans <c>_amounts</c>.</summary>
    private readonly Dictionary<int, int> _meatByAge = [];

    public int Get(ResourceType type) => _amounts.GetValueOrDefault(type);

    /// <summary>Ce que redonne un repas de baies ou de poisson, de céréales, ou de pain (le pain nourrit mieux).</summary>
    public const float WildMealValue = 0.6f, GrainMealValue = 0.6f, BreadMealValue = 0.85f, MilkMealValue = 0.7f;

    /// <summary>Une part de viande, un bol de ragoût, une part de gâteau (celle-ci rassasie entièrement) ; un gâteau fait six parts.</summary>
    public const float MeatMealValue = 0.9f, StewMealValue = 1f, CakeMealValue = 1f;
    public const int PortionsPerCake = 6;

    /// <summary>Parts du gâteau entamé qu'il reste à manger.</summary>
    private int _cakeSlices;

    /// <summary>Tout ce qui se mange : nourriture sauvage, céréales et pain (un repas chacun). La farine ne se mange pas crue.</summary>
    public int FoodUnits => Get(ResourceType.Food) + Get(ResourceType.Fish) + Get(ResourceType.Eggs) + Get(ResourceType.Milk) + Get(ResourceType.Grain) + Get(ResourceType.Bread)
        + Get(ResourceType.Meat) + Get(ResourceType.SaltedMeat) + Get(ResourceType.Stew) + Get(ResourceType.Cake) * PortionsPerCake + _cakeSlices;

    /// <summary>Valeur du stock comestible : 100 points de faim correspondent à une unité de nourriture.</summary>
    public decimal FoodNutrition => Nutrition(ResourceType.Food) + Nutrition(ResourceType.Fish) + Nutrition(ResourceType.Eggs)
        + Nutrition(ResourceType.Milk) + Nutrition(ResourceType.Grain) + Nutrition(ResourceType.Bread)
        + Nutrition(ResourceType.Meat) + Nutrition(ResourceType.SaltedMeat) + Nutrition(ResourceType.Stew) + Nutrition(ResourceType.Cake)
        + _cakeSlices * (decimal)CakeMealValue;

    public static decimal NutritionPerItem(ResourceType type) => type switch
    {
        ResourceType.Food or ResourceType.Fish or ResourceType.Eggs => (decimal)WildMealValue,
        ResourceType.Milk => (decimal)MilkMealValue,
        ResourceType.Grain => (decimal)GrainMealValue,
        ResourceType.Bread => (decimal)BreadMealValue,
        ResourceType.Meat or ResourceType.SaltedMeat => (decimal)MeatMealValue,
        ResourceType.Stew => (decimal)StewMealValue,
        ResourceType.Cake => PortionsPerCake * (decimal)CakeMealValue,
        _ => 0m,
    };

    public decimal Nutrition(ResourceType type) => Get(type) * NutritionPerItem(type);

    /// <summary>
    /// Prend un repas : la nourriture sauvage d'abord (elle se garde mal), puis le pain, puis les céréales, puis le lait et les œufs
    /// (les produits de l'élevage sont gardés pour la vente, et ne se mangent que quand le reste manque).
    /// Renvoie ce que le repas redonne à celui qui mange.
    /// </summary>
    public bool TryTakeMeal(out float value) => TryTakeMeal(out value, out _);

    /// <summary>
    /// Comme ci-dessus, mais renvoie aussi le plat de fête mangé, le cas échéant (gâteau ou ragoût, qui passent avant tout le reste).
    /// Ensuite : la nourriture sauvage, la viande fraîche (qui se gâte vite), le pain, les céréales, la viande salée (la réserve de longue durée),
    /// le lait et les œufs.
    /// </summary>
    public bool TryTakeMeal(out float value, out ResourceType? dish)
    {
        value = 0f;
        dish = null;
        if (_cakeSlices == 0 && TryTake(ResourceType.Cake, 1))
            _cakeSlices = PortionsPerCake;
        if (_cakeSlices > 0)
        {
            _cakeSlices--;
            (value, dish) = (CakeMealValue, ResourceType.Cake);
        }
        else if (TryTake(ResourceType.Stew, 1)) (value, dish) = (StewMealValue, ResourceType.Stew);
        else if (TryTake(ResourceType.Food, 1)) value = WildMealValue;
        else if (TryTake(ResourceType.Fish, 1)) value = WildMealValue;
        else if (TryTake(ResourceType.Meat, 1)) value = MeatMealValue;
        else if (TryTake(ResourceType.Bread, 1)) value = BreadMealValue;
        else if (TryTake(ResourceType.Grain, 1)) value = GrainMealValue;
        else if (TryTake(ResourceType.SaltedMeat, 1)) value = MeatMealValue;
        else if (TryTake(ResourceType.Milk, 1)) value = MilkMealValue;
        else if (TryTake(ResourceType.Eggs, 1)) value = WildMealValue;
        return value > 0f;
    }

    public bool TryTakeMeal() => TryTakeMeal(out _);

    public void Add(ResourceType type, int amount, ResourceFlow flow = ResourceFlow.Production)
    {
        _amounts[type] = Get(type) + amount;
        ResourceAccounting.Record(this, type, flow, amount);
        if (type == ResourceType.Meat && amount > 0)
            _meatByAge[0] = _meatByAge.GetValueOrDefault(0) + amount;
    }

    /// <summary>Retire une quantité si elle est disponible (la viande la plus vieille d'abord). Renvoie false sinon, sans rien retirer.</summary>
    public bool TryTake(ResourceType type, int amount, ResourceFlow flow = ResourceFlow.Usage)
    {
        if (Get(type) < amount)
            return false;
        _amounts[type] = Get(type) - amount;
        ResourceAccounting.Record(this, type, flow, amount);
        if (type == ResourceType.Meat)
            RemoveMeat(amount, 0);
        return true;
    }

    /// <summary>Retire de la viande fraîche, des lots les plus vieux (au moins <paramref name="fromAge"/> jours) aux plus jeunes.</summary>
    private void RemoveMeat(int amount, int fromAge)
    {
        foreach (int age in _meatByAge.Keys.Where(a => a >= fromAge).OrderByDescending(a => a).ToList())
        {
            int take = Math.Min(amount, _meatByAge[age]);
            if (take == _meatByAge[age])
                _meatByAge.Remove(age);
            else
                _meatByAge[age] -= take;
            amount -= take;
            if (amount == 0)
                return;
        }
    }

    /// <summary>La viande fraîche vieillit d'un jour.</summary>
    internal void AgeMeat()
    {
        var aged = _meatByAge.ToDictionary(p => p.Key + 1, p => p.Value);
        _meatByAge.Clear();
        foreach ((int age, int amount) in aged)
            _meatByAge[age] = amount;
    }

    /// <summary>La viande fraîche d'au moins <paramref name="age"/> jours.</summary>
    public int MeatAtLeast(int age) => _meatByAge.Where(p => p.Key >= age).Sum(p => p.Value);

    /// <summary>Fait gâter la part <paramref name="share"/> (arrondie au-dessus) de la viande d'au moins <paramref name="fromAge"/> jours. Renvoie la quantité perdue.</summary>
    internal int SpoilMeat(int fromAge, float share)
    {
        int lost = 0;
        foreach (int age in _meatByAge.Keys.Where(a => a >= fromAge).ToList())
        {
            int rot = Math.Min(_meatByAge[age], Math.Max(1, (int)MathF.Ceiling(_meatByAge[age] * share)));
            if (rot == _meatByAge[age])
                _meatByAge.Remove(age);
            else
                _meatByAge[age] -= rot;
            lost += rot;
        }
        _amounts[ResourceType.Meat] = Get(ResourceType.Meat) - lost;
        ResourceAccounting.Record(this, ResourceType.Meat, ResourceFlow.Loss, lost);
        return lost;
    }
}
