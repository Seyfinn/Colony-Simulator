namespace GodColony.Simulation.Colonies;

/// <summary>
/// Nourriture sauvage (baies, poisson), céréales de la moisson, bois, pierre, minerai de fer,
/// et les produits de la chaîne du fer : charbon de bois, fer, outils.
/// </summary>
public enum ResourceType { Food, Grain, Wood, Stone, IronOre, Charcoal, Iron, Tools }

/// <summary>Le stock commun de la colonie : tout appartient à la colonie, rien aux colons.</summary>
public sealed class Stockpile
{
    private readonly Dictionary<ResourceType, int> _amounts = [];

    public int Get(ResourceType type) => _amounts.GetValueOrDefault(type);

    /// <summary>Tout ce qui se mange : nourriture sauvage et céréales.</summary>
    public int FoodUnits => Get(ResourceType.Food) + Get(ResourceType.Grain);

    /// <summary>Prend un repas : la nourriture sauvage d'abord (elle se garde mal), les céréales ensuite.</summary>
    public bool TryTakeMeal() => TryTake(ResourceType.Food, 1) || TryTake(ResourceType.Grain, 1);

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
