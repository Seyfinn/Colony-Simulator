namespace GodColony.Simulation.Colonies;

public enum ResourceType { Food, Wood, Stone, IronOre }

/// <summary>Le stock commun de la colonie : tout appartient à la colonie, rien aux colons.</summary>
public sealed class Stockpile
{
    private readonly Dictionary<ResourceType, int> _amounts = [];

    public int Get(ResourceType type) => _amounts.GetValueOrDefault(type);

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
