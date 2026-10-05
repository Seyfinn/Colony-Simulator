namespace GodColony.Simulation.Colonies;

/// <summary>Des biens promis mais toujours présents dans leur stock ; le chargement les retire une seule fois.</summary>
public sealed class StockReservation
{
    internal StockReservation(int id, string owner, ResourceType resource, int amount, int priority, long expiresAtTicks)
    {
        Id = id;
        Owner = owner;
        Resource = resource;
        Amount = amount;
        Priority = priority;
        ExpiresAtTicks = expiresAtTicks;
    }

    public int Id { get; }
    public string Owner { get; }
    public ResourceType Resource { get; }
    public int Amount { get; }
    public int Priority { get; }
    public long ExpiresAtTicks { get; }
    public bool Active { get; internal set; } = true;
}
