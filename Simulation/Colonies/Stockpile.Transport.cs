namespace GodColony.Simulation.Colonies;

public sealed partial class Stockpile
{
    public Stockpile() { }
    internal Stockpile(Dictionary<ResourceType, int> amounts) => _amounts = amounts;
    private List<StockReservation>? _reservations;
    private int _nextReservationId;

    public IReadOnlyList<StockReservation> Reservations => _reservations ?? (IReadOnlyList<StockReservation>)Array.Empty<StockReservation>();
    internal Dictionary<ResourceType, int> Amounts => _amounts;

    /// <summary>Les biens disponibles pour manger, travailler ou charger, hors promesses actives.</summary>
    public int Available(ResourceType resource) => Get(resource) - Reservations.Where(r => r.Active && r.Resource == resource).Sum(r => r.Amount);

    public decimal AvailableNutrition => _amounts.Keys.Sum(r => Available(r) * ResourceCatalog.Nutrition(r));

    public StockReservation? Reserve(string owner, ResourceType resource, int amount, int priority, long expiresAtTicks, long nowTicks)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 120 || !Enum.IsDefined(resource)
            || amount <= 0 || priority < 0 || nowTicks < 0 || expiresAtTicks <= nowTicks)
            throw new ArgumentException("Réservation de stock invalide.");
        ExpireReservations(nowTicks);
        if (Available(resource) < amount)
            return null;
        var reservation = new StockReservation(checked(++_nextReservationId), owner, resource, amount, priority, expiresAtTicks);
        (_reservations ??= []).Add(reservation);
        return reservation;
    }

    public bool CancelReservation(StockReservation reservation)
    {
        if (_reservations is null || !_reservations.Remove(reservation))
            return false;
        reservation.Active = false;
        return true;
    }

    public void ExpireReservations(long nowTicks)
    {
        foreach (StockReservation reservation in Reservations.Where(r => r.ExpiresAtTicks <= nowTicks).ToArray())
            CancelReservation(reservation);
    }

    /// <summary>La survie peut libérer les promesses de confort avant leur chargement.</summary>
    public void CancelReservationsBelow(int priority)
    {
        foreach (StockReservation reservation in Reservations.Where(r => r.Priority < priority).ToArray())
            CancelReservation(reservation);
    }

    /// <summary>Un chargement garde les âges et ne peut utiliser ni une promesse étrangère, ni deux fois la même promesse.</summary>
    public bool LoadReservation(StockReservation reservation, Stockpile destination, long nowTicks)
    {
        ExpireReservations(nowTicks);
        if (!_reservations?.Contains(reservation) ?? true)
            return false;
        if (ReferenceEquals(this, destination) || destination.Get(reservation.Resource) > int.MaxValue - reservation.Amount)
            return false;
        CancelReservation(reservation);
        return TryTransferTo(destination, reservation.Resource, reservation.Amount);
    }

    /// <summary>Transfert physique atomique ; les entrées ne rajeunissent pas les lots de viande.</summary>
    public bool TryTransferTo(Stockpile destination, ResourceType resource, int amount,
        ResourceFlow outgoing = ResourceFlow.Transfer, ResourceFlow incoming = ResourceFlow.Transfer)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (ReferenceEquals(this, destination) || Available(resource) < amount || destination.Get(resource) > int.MaxValue - amount)
            return false;
        if (amount == 0)
            return true;
        List<CargoLot> lots = [];
        if (resource == ResourceType.Meat)
        {
            int left = amount;
            foreach (int age in _meatByAge.Keys.OrderByDescending(a => a))
            {
                int take = Math.Min(left, _meatByAge[age]);
                if (take > 0) lots.Add(new(resource, take, age));
                left -= take;
                if (left == 0) break;
            }
            if (left != 0) throw new InvalidOperationException("Les lots de viande ne correspondent pas au stock.");
        }
        else
            lots.Add(new(resource, amount));
        if (!TryTake(resource, amount, outgoing)) return false;
        foreach (CargoLot lot in lots)
        {
            destination._amounts[resource] = checked(destination.Get(resource) + lot.Amount);
            if (resource == ResourceType.Meat)
                destination._meatByAge[lot.AgeDays] = checked(destination._meatByAge.GetValueOrDefault(lot.AgeDays) + lot.Amount);
        }
        ResourceAccounting.Record(destination, resource, incoming, amount);
        return true;
    }

    /// <summary>Une perte de biens annule les promesses les moins prioritaires ; elle ne les transforme pas en cargaison.</summary>
    private void ReconcileReservations(ResourceType resource)
    {
        foreach (StockReservation reservation in Reservations.Where(r => r.Resource == resource).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToArray())
        {
            if (Available(resource) >= 0) break;
            CancelReservation(reservation);
        }
    }

    /// <summary>Un règlement prépare ensemble les biens et les pièces ; un refus ne débite aucun des deux inventaires.</summary>
    internal bool TrySellTo(Stockpile buyer, ResourceType resource, int amount, int price,
        ResourceFlow outgoing = ResourceFlow.Sale, ResourceFlow incoming = ResourceFlow.Purchase)
    {
        if (amount <= 0 || price < 0 || resource == ResourceType.Coins || !Enum.IsDefined(resource))
            throw new ArgumentException("Échange invalide.");
        if (ReferenceEquals(this, buyer) || Available(resource) < amount || buyer.Available(ResourceType.Coins) < price
            || buyer.Get(resource) > int.MaxValue - amount || Get(ResourceType.Coins) > int.MaxValue - price)
            return false;
        if (!TryTransferTo(buyer, resource, amount, outgoing, incoming)) return false;
        if (price > 0)
        {
            buyer.TryTake(ResourceType.Coins, price, ResourceFlow.Transfer);
            Add(ResourceType.Coins, price, ResourceFlow.Transfer);
        }
        return true;
    }

    internal void ValidateInventory()
    {
        if (_nextReservationId < 0 || _amounts.Any(p => !Enum.IsDefined(p.Key) || p.Value < 0)
            || _meatByAge.Any(p => p.Key < 0 || p.Value <= 0) || _meatByAge.Sum(p => (long)p.Value) != Get(ResourceType.Meat)
            || _cakeSlices is < 0 or >= PortionsPerCake)
            throw new InvalidDataException("Inventaire ou lots périssables invalides.");
        var ids = new HashSet<int>();
        foreach (StockReservation reservation in Reservations)
            if (!ids.Add(reservation.Id) || reservation.Id <= 0 || reservation.Id > _nextReservationId
                || !reservation.Active || reservation.Amount <= 0 || !Enum.IsDefined(reservation.Resource)
                || reservation.Priority < 0 || string.IsNullOrWhiteSpace(reservation.Owner) || reservation.Owner.Length > 120
                || reservation.ExpiresAtTicks < 0 || Available(reservation.Resource) < 0)
                throw new InvalidDataException("Réservation de stock invalide.");
    }
}
