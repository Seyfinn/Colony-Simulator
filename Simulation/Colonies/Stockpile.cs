namespace GodColony.Simulation.Colonies;

/// <summary>
/// Nourriture sauvage (baies, poisson), céréales de la moisson, bois, pierre, minerai de fer,
/// les produits de la chaîne du fer (charbon de bois, fer, outils), ceux de la chaîne du blé (farine, pain)
/// et la monnaie commune à toutes les colonies.
/// </summary>
public enum ResourceType { Food, Grain, Wood, Stone, IronOre, Charcoal, Iron, Tools, Flour, Bread, Coins }

/// <summary>Le stock commun de la colonie : tout appartient à la colonie, rien aux colons.</summary>
public sealed class Stockpile
{
    private readonly Dictionary<ResourceType, int> _amounts = [];

    public int Get(ResourceType type) => _amounts.GetValueOrDefault(type);

    /// <summary>Ce que redonne un repas de baies ou de poisson, de céréales, ou de pain (le pain nourrit mieux).</summary>
    public const float WildMealValue = 0.6f, GrainMealValue = 0.6f, BreadMealValue = 0.85f;

    /// <summary>Tout ce qui se mange : nourriture sauvage, céréales et pain (un repas chacun). La farine ne se mange pas crue.</summary>
    public int FoodUnits => Get(ResourceType.Food) + Get(ResourceType.Grain) + Get(ResourceType.Bread);

    /// <summary>
    /// Prend un repas : la nourriture sauvage d'abord (elle se garde mal), puis le pain, puis les céréales.
    /// Renvoie ce que le repas redonne à celui qui mange.
    /// </summary>
    public bool TryTakeMeal(out float value)
    {
        value = 0f;
        if (TryTake(ResourceType.Food, 1)) value = WildMealValue;
        else if (TryTake(ResourceType.Bread, 1)) value = BreadMealValue;
        else if (TryTake(ResourceType.Grain, 1)) value = GrainMealValue;
        return value > 0f;
    }

    public bool TryTakeMeal() => TryTakeMeal(out _);

    public void Add(ResourceType type, int amount) => _amounts[type] = Get(type) + amount;

    /// <summary>Retire une quantité si elle est disponible. Renvoie false sinon, sans rien retirer.</summary>
    public bool TryTake(ResourceType type, int amount)
    {
        if (Get(type) < amount)
            return false;
        _amounts[type] = Get(type) - amount;
        return true;
    }
}
