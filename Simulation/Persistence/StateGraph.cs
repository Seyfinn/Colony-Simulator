using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Persistence;

/// <summary>
/// Instantané des données de simulation, y compris leurs références partagées et leurs cycles.
/// Seuls les types de données explicitement autorisés sont restaurés ; aucun nom CLR externe ni délégué n'est chargé.
/// Les champs privés évitent de perdre les réservations, chemins, délais et compteurs des travaux en cours.
/// Le format est lié à son schéma : une modification de ces champs exige une migration ou un nouveau format.
/// </summary>
internal static class StateGraph
{
    private static readonly Type[] DataTypes =
    [
        typeof(WorldState), typeof(WorldMap), typeof(LocalMap), typeof(GameClock), typeof(Colony), typeof(Colonist),
        typeof(Needs), typeof(Skills), typeof(Personality), typeof(Stockpile), typeof(LaborLedger), typeof(Building),
        typeof(Field), typeof(FieldPlot), typeof(Canal), typeof(Activity), typeof(Grave), typeof(Thought),
        typeof(ColonySensors), typeof(ChainDemand), typeof(BreadDemand), typeof(Caravan), typeof(TradeLine),
        typeof(TradeRecord), typeof(Prayer), typeof(PrayerBook), typeof(ColonyBrain.NarrationTopic),
        typeof(WorldGrid), typeof(WorldTile), typeof(WorldRoute), typeof(Pact), typeof(WarParty),
        // Format v2 : le plan du village, ses recherches en cours et la couche routière.
        typeof(SettlementLayout), typeof(District), typeof(PlotReservation), typeof(DevelopmentProject), typeof(RoadSegment),
        typeof(PlanRequest), typeof(PlacementProposal), typeof(RoadLayer), typeof(SettlementPlanningState), typeof(PlanningJob),
        typeof(Pathfinding.PathSearchState), typeof(StockReservation), typeof(Recipe), typeof(SupplierMemory), typeof(MarketOffer), typeof(Settlement), typeof(RegionState), typeof(Deposit), typeof(DepositKnowledge), typeof(HouseholdEquipment), typeof(MonetaryLedger), typeof(TerritorialRules), typeof(WorldRoadNetwork), typeof(OfferingProject), typeof(Monument), typeof(DivineWish),
    ];
    private static readonly Dictionary<string, Type> KnownTypes = DataTypes
        .Concat(typeof(WorldState).Assembly.GetTypes().Where(t => t.IsEnum))
        .Concat(new[] { typeof(int), typeof(long), typeof(uint), typeof(byte), typeof(bool), typeof(float), typeof(double), typeof(string) })
        .ToDictionary(t => t.FullName!, StringComparer.Ordinal);
    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = DataTypes.ToDictionary(t => t, Fields);
    private const int MaxItems = 262144;
    private const int MaxObjects = 250000;
    private const int MaxDepth = 512;

    internal static string Schema => string.Join("\n", DataTypes.OrderBy(t => t.FullName).Select(t =>
        t.FullName + ":" + string.Join(",", Fields(t).Select(f => f.Name + "=" + f.FieldType)))) + "\n" + RandomState.Schema;

    /// <summary>
    /// Les champs persistés d'un type, triés par nom. Les caches (marqués <see cref="NonSerializedAttribute"/>), les délégués et les pathfinders
    /// sont exclus : ils se reconstruisent au chargement.
    /// </summary>
    private static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(f => !typeof(Delegate).IsAssignableFrom(f.FieldType)
            && f.GetCustomAttribute<NonSerializedAttribute>() is null
            && !(type == typeof(LocalMap) && f.Name == "_scratch")
            && f.FieldType != typeof(Pathfinding.Pathfinder))
        .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

    internal static void Write(BinaryWriter output, WorldState world) => new Writer(output).Value(world, 0);

    /// <summary>Lit un monde au format courant, ou un format antérieur figé : voir <see cref="LegacySchema"/>.</summary>
    internal static WorldState Read(BinaryReader input, LegacySchema? legacy = null, bool migrateSettlement = true, bool restoreOldVoyage = true, bool resetPredictedGain = true)
    {
        var world = new Reader(input, legacy).Value(0) as WorldState ?? throw new InvalidDataException("Le monde est absent de la sauvegarde.");
        if (legacy is not null && migrateSettlement)
            SettlementMigration.MigrateV1(world);
        if (legacy is not null && resetPredictedGain)
            foreach (Colony colony in world.Colonies)
                colony.LifetimeTradeGainHours = 0; // Les anciens gains prévisionnels ne sont pas des bilans réalisés.
        if (legacy is not null && restoreOldVoyage)
            foreach (Caravan trip in world.Caravans)
            {
                trip.PlannedReturnTicks = trip.ReturnTicks;
                trip.ReportTicks = -1;
                trip.OutboundOffers = []; trip.ReturnOffers = [];
                trip.LastRouteTicks = world.Clock.Ticks;
                trip.RouteRevision = -1;
            }
        world.RestoreSettlements(legacy is not null);
        foreach (PlanningJob job in world.Planning.Jobs)
            if (job.SettlementId == 0) job.SettlementId = job.Owner.PrimarySettlementId;
        foreach (Settlement place in world.Settlements)
            if (legacy is not null) TerritorialTravel.Observe(world, place.Owner, world.VisitRegion(place.RegionTileIndex));
        Validate(world);
        foreach (Colony colony in world.Colonies)
        {
            foreach (Settlement place in colony.Settlements) place.Pathfinder = new Pathfinding.Pathfinder(place.Map) { Owner = colony };
            if (FieldCache[typeof(Colony)].Single(f => f.Name == "_prayers").GetValue(colony) is PrayerBook book)
                foreach (Prayer prayer in book.All) prayer.RestoreAction(world);
        }
        return world;
    }

    /// <summary>
    /// Le schéma du format v1, figé avant la refonte du village : pour chaque type, ses champs dans l'ordre où le flux v1 les écrit. Un flux v1 se lit
    /// avec ces listes ; chaque valeur est rangée dans le champ de même nom du type actuel, ou ignorée s'il a disparu. Les champs nés depuis gardent leur
    /// valeur par défaut (la migration les complète). Aucun autre schéma n'est accepté.
    /// </summary>
    internal sealed class LegacySchema
    {
        private readonly Dictionary<Type, string[]> _names = [];
        internal string Text { get; }

        internal LegacySchema(string text)
        {
            Text = text;
            foreach (string line in text.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || !KnownTypes.TryGetValue(line[..colon], out Type? type))
                    continue;
                var names = new List<string>();
                int depth = 0, start = colon + 1;
                for (int i = colon + 1; i <= line.Length; i++)
                {
                    char c = i < line.Length ? line[i] : ',';
                    if (c == '[') depth++;
                    else if (c == ']') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        if (i > start)
                            names.Add(line[start..line.IndexOf('=', start)]);
                        start = i + 1;
                    }
                }
                _names[type] = names.ToArray();
            }
        }

        internal string[]? NamesOf(Type type) => _names.GetValueOrDefault(type);
    }

    private static void Validate(WorldState world)
    {
        if (world.Colonies.Count > WorldState.MaxPlayerColonies || world.Clock.Ticks < 0)
            throw new InvalidDataException("Le monde sauvegardé est invalide.");
        world.ValidateTerritories();
        if (world.Colonies.Select(c => c.Id).Distinct().Count() != world.Colonies.Count
            || world.Colonies.Any(c => c.Id <= 0 || c.Settlements.Count < 1 || c.PrimarySettlement.Owner != c))
            throw new InvalidDataException("Identité d'établissement invalide.");
        var settlements = world.Settlements.ToArray();
        if (settlements.Select(s => s.Id).Distinct().Count() != settlements.Length
            || settlements.Any(s => s.Id <= 0 || !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Status)
                || !world.Regions.TryGetValue(s.RegionTileIndex, out RegionState? region) || !ReferenceEquals(s.Map, region.Map)))
            throw new InvalidDataException("Région d'établissement invalide.");
        LocalMap[] maps = world.Colonies.Count == 0 ? [world.Map] : world.Regions.Values.Select(r => r.Map).ToArray();
        foreach (LocalMap map in maps)
        {
            if (map.Width is < 32 or > 512 || map.Height is < 32 or > 512)
                throw new InvalidDataException("Dimensions du terrain invalides.");
            foreach (FieldInfo field in Fields(typeof(LocalMap)))
                if (field.Name != "_terrainStamp" && field.GetValue(map) is Array array && array.Length != map.Width * map.Height)
                    throw new InvalidDataException("Données du terrain incomplètes.");
            if (map.Roads is null || map.Roads.Length != map.Width * map.Height || !map.HasValidTerrainStamps)
                throw new InvalidDataException("Couche routière absente ou incomplète.");
        }
        foreach (Settlement place in world.Settlements)
        {
            Colony colony = place.Owner;
            using var scope = colony.UseSettlement(place);
            if (colony.GatherSpots.Count == 0 || !colony.Map.InBounds(colony.CampX, colony.CampY) || colony.Clock != world.Clock)
                throw new InvalidDataException("Données de colonie invalides.");
            _ = world.WorldMap.PositionOf(colony);
            SettlementMigration.ValidateLayout(colony);
            foreach (Building building in colony.Buildings)
            {
                if (building.Width is < 1 or > 6 || building.Height is < 1 or > 5
                    || !colony.Map.InBounds(building.X, building.Y)
                    || !colony.Map.InBounds(building.X + building.Width - 1, building.Y + building.Height - 1))
                    throw new InvalidDataException("Emprise de bâtiment invalide.");
                if (building.ExtensionOfId < 0 || building.IsExtension && (building.Type is not (BuildingType.Pen or BuildingType.Market)
                    || building.Width != 2 || building.Height != 3 || building.ExtensionOfId == building.Id
                    || building.ExtensionOfId >= colony.Layout.NextObjectId))
                    throw new InvalidDataException("Extension de bâtiment invalide.");
                // Un incendie peut avoir détruit le bâtiment principal sans détruire toutes ses parcelles.
                if (building.IsExtension && colony.BuildingById(building.ExtensionOfId) is { } principal
                    && (principal.IsExtension || principal.Type != building.Type))
                    throw new InvalidDataException("Bâtiment principal d'extension invalide.");
            }
            colony.Stock.ValidateInventory();
        }
        var inventories = world.Settlements.Select(s => s.Stock).ToHashSet();
        if (inventories.Count != world.Settlements.Count())
            throw new InvalidDataException("Un stock est partagé entre plusieurs lieux.");
        var inhabitants = world.Colonies.SelectMany(c => c.Members).Concat(world.Settlements.SelectMany(s => s.Transients))
            .Concat(world.Caravans.SelectMany(c => c.Traders)).Distinct();
        foreach (Colonist colonist in inhabitants)
        {
            if (colonist.Activity is not { InputsInventory: { } inventory } activity)
                continue;
            if (!inventories.Add(inventory) || !activity.InputsTaken || activity.Kind != ActivityKind.Craft
                || activity.CommittedRecipe is not { } recipe || activity.Building?.Type != recipe.Workshop
                || !Enum.IsDefined(recipe.Output) || recipe.OutputAmount <= 0
                || !float.IsFinite(recipe.Seconds) || recipe.Seconds <= 0
                || recipe.Inputs is null || recipe.Inputs.Any(i => !Enum.IsDefined(i.Type) || i.Amount <= 0))
                throw new InvalidDataException("Fabrication sauvegardée invalide.");
            inventory.ValidateInventory();
        }
        foreach (Caravan caravan in world.Caravans)
        {
            if (!inventories.Add(caravan.Inventory) || !inventories.Add(caravan.Provisions)
                || !ReferenceEquals(caravan.Cargo, caravan.Inventory.Amounts)
                || !world.Colonies.Contains(caravan.From) || !world.Colonies.Contains(caravan.To)
                || !Enum.IsDefined(caravan.State) || caravan.LastNeedsTicks > world.Clock.Ticks)
                throw new InvalidDataException("Voyage sauvegardé invalide.");
            if (caravan.DepartureValues is { } values && (values.Select(l => l.Good).Distinct().Count() != values.Count
                || values.Any(l => !Enum.IsDefined(l.Good) || l.Good == ResourceType.Coins || l.Units < 0
                    || !double.IsFinite(l.UnitPrice) || l.UnitPrice < 0)))
                throw new InvalidDataException("Coûts de départ invalides.");
            caravan.Inventory.ValidateInventory();
            caravan.Provisions.ValidateInventory();
        }
        foreach (TradeRecord record in world.Colonies.SelectMany(c => c.Trades))
            if ((record.GainHours is { } gain && !double.IsFinite(gain))
                || (record.CostHours is { } cost && (!double.IsFinite(cost) || cost < 0))
                || (record.ExpectedGainHours is { } expected && !double.IsFinite(expected)))
                throw new InvalidDataException("Bilan commercial invalide.");
        if (world.WorldMap.ClosedPassages.Any(t => t < 0 || t >= world.WorldMap.Grid.Tiles.Length))
            throw new InvalidDataException("Passage mondial invalide.");
        var suppliers = new HashSet<(Colony, Colony)>();
        foreach (SupplierMemory memory in world.SupplierMemories)
        {
            if (!world.Colonies.Contains(memory.Observer) || !world.Colonies.Contains(memory.Supplier)
                || memory.Observer == memory.Supplier || !suppliers.Add((memory.Observer, memory.Supplier))
                || memory.ObservedTicks < 0 || memory.ObservedTicks > world.Clock.Ticks
                || memory.BuyingBudget < 0 || memory.Deliveries < 0 || memory.Refusals < 0 || memory.Delays < 0
                || !double.IsFinite(memory.LastTripDays) || memory.LastTripDays < 0
                || !double.IsFinite(memory.LastCostHours) || memory.LastCostHours < 0)
                throw new InvalidDataException("Mémoire commerciale invalide.");
            ValidateOffers(memory.Offers);
        }
        foreach (Caravan trip in world.Caravans)
        {
            ValidateOffers(trip.OutboundOffers); ValidateOffers(trip.ReturnOffers);
            if (trip.LastRouteTicks > world.Clock.Ticks || trip.ReportTicks > world.Clock.Ticks
                || !double.IsFinite(trip.SegmentTravelCost) || trip.SegmentTravelCost < 0
                || !double.IsFinite(trip.ProvisionCostHours) || trip.ProvisionCostHours < 0)
                throw new InvalidDataException("Progression commerciale invalide.");
            if (trip.Route is { } route && (route.Tiles.Count == 0 || route.Tiles.Count != route.Cumulative.Count
                || trip.RouteIndex < 0 || trip.RouteIndex >= route.Tiles.Count
                || route.Tiles.Any(t => t < 0 || t >= world.WorldMap.Grid.Tiles.Length)
                || route.Cumulative.Any(c => !float.IsFinite(c) || c < 0)
                || route.Cumulative.Zip(route.Cumulative.Skip(1)).Any(p => p.First > p.Second)
                || (trip.RouteIndex + 1 < route.Tiles.Count && trip.SegmentTravelCost > route.Cumulative[trip.RouteIndex + 1] - route.Cumulative[trip.RouteIndex] + 0.001)))
                throw new InvalidDataException("Itinéraire sauvegardé invalide.");
        }
        foreach (Colony colony in world.Colonies)
        {
            ValidateOfferings(colony);
            if (colony.DepositReports.Select(k => k.SiteId).Distinct().Count() != colony.DepositReports.Count
                || colony.DepositReports.Any(k => !Enum.IsDefined(k.State) || !Enum.IsDefined(k.Material) || !float.IsFinite(k.Confidence) || k.Confidence is < 0 or > 1
                    || k.EstimateMin < 0 || k.EstimateMax < k.EstimateMin || k.SurveyedDepth is < -1 or > Prospection.MaxReach))
                throw new InvalidDataException("Renseignement de gisement invalide.");
        }
        if (world.Planning is null)
            throw new InvalidDataException("L'état de planification est absent.");
        if (world.Planning.Jobs.Any(j => world.SettlementById(j.SettlementId)?.Owner != j.Owner))
            throw new InvalidDataException("Recherche d'établissement invalide.");
        foreach (RegionState region in world.Regions.Values)
        {
            if (region.TileIndex < 0 || region.TileIndex >= world.WorldMap.Grid.Tiles.Length
                || region.OwnerColonyId is int owner && !world.Colonies.Any(c => c.Id == owner))
                throw new InvalidDataException("Territoire invalide.");
            if (region.Deposits.Any(d => d.Region != region.TileIndex || d.Id <= 0 || !Enum.IsDefined(d.Material)
                || !Enum.IsDefined(d.Mode) || d.InitialReserve < 0 || d.RemainingReserve < 0 || d.RemainingReserve > d.InitialReserve
                || d.Depth is < 0 or > Prospection.MaxReach || d.DailyLimit < 1 || d.ExtractedToday < 0 || d.ExtractedToday > d.DailyLimit || !float.IsFinite(d.Difficulty) || d.Difficulty <= 0
                || !region.Map.InBounds(d.X,d.Y))) throw new InvalidDataException("Gisement invalide.");
        }
    }

    /// <summary>Offrandes, monuments et souhaits : états connus, matériaux livrés dans la limite de la recette figée, références existantes.</summary>
    private static void ValidateOfferings(Colony colony)
    {
        var settlements = colony.Settlements.Select(s => s.Id).ToHashSet();
        if (colony.Offerings.Select(p => p.Id).Distinct().Count() != colony.Offerings.Count
            || colony.Monuments.Select(m => m.Id).Distinct().Count() != colony.Monuments.Count
            || colony.Wishes.Select(w => w.Id).Distinct().Count() != colony.Wishes.Count
            || colony.Offerings.Where(p => p.IsActive).GroupBy(p => p.SettlementId).Any(g => g.Count() > 1))
            throw new InvalidDataException("Identifiants d'offrande invalides.");
        foreach (OfferingProject project in colony.Offerings)
            if (project.Id <= 0 || !settlements.Contains(project.SettlementId) || !Enum.IsDefined(project.Model) || !Enum.IsDefined(project.State)
                || !Enum.IsDefined(project.ResumeState) || !float.IsFinite(project.WorkDone) || !float.IsFinite(project.WorkSeconds)
                || project.WorkDone < 0 || project.WorkDone > project.WorkSeconds
                || project.Required.Any(r => !Enum.IsDefined(r.Key) || r.Value <= 0)
                || project.Delivered.Any(d => d.Value < 0 || d.Value > project.Required.GetValueOrDefault(d.Key))
                || (project.State == OfferingProjectState.Completed && !colony.Monuments.Any(m => m.Id == project.MonumentId && m.ProjectId == project.Id))
                || (project.State == OfferingProjectState.Building && !project.HasAllMaterials))
                throw new InvalidDataException("Projet d'offrande invalide.");
        foreach (Monument monument in colony.Monuments)
            if (monument.Id <= 0 || !settlements.Contains(monument.SettlementId) || !Enum.IsDefined(monument.Model)
                || monument.Materials.Any(m => !Enum.IsDefined(m.Key) || m.Value <= 0)
                || (monument.WishId != 0 && !colony.Wishes.Any(w => w.Id == monument.WishId)))
                throw new InvalidDataException("Monument invalide.");
        foreach (DivineWish wish in colony.Wishes)
            if (wish.Id <= 0 || !settlements.Contains(wish.SettlementId) || !Enum.IsDefined(wish.Kind) || !Enum.IsDefined(wish.TargetKind)
                || !Enum.IsDefined(wish.Status))
                throw new InvalidDataException("Souhait invalide.");
    }

    private static void ValidateOffers(IReadOnlyList<MarketOffer> offers)
    {
        if (offers is null || offers.Select(o => o.Good).Distinct().Count() != offers.Count
            || offers.Any(o => !Enum.IsDefined(o.Good) || o.Available < 0 || o.Wanted < 0
                || !double.IsFinite(o.SellPrice) || o.SellPrice < 0 || !double.IsFinite(o.BuyPrice) || o.BuyPrice < 0))
            throw new InvalidDataException("Offres commerciales invalides.");
    }

    // Les identifiants de types sont structurels pour les collections ; le reste passe par la liste fermée ci-dessus.
    private static void WriteType(BinaryWriter output, Type type)
    {
        if (KnownTypes.ContainsKey(type.FullName ?? "")) { output.Write((byte)0); output.Write(type.FullName!); return; }
        if (type.IsArray && type.GetArrayRank() == 1) { output.Write((byte)1); WriteType(output, type.GetElementType()!); return; }
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            byte kind = definition == typeof(List<>) ? (byte)2 : definition == typeof(Dictionary<,>) ? (byte)3
                : definition == typeof(HashSet<>) ? (byte)4 : definition == typeof(ValueTuple<,>) ? (byte)5
                : throw new InvalidDataException($"Collection non prise en charge : {type.Name}.");
            output.Write(kind);
            foreach (Type arg in type.GetGenericArguments()) WriteType(output, arg);
            return;
        }
        throw new InvalidDataException($"Type non pris en charge : {type.Name}.");
    }

    private static Type ReadType(BinaryReader input, int depth = 0)
    {
        if (depth > 12) throw new InvalidDataException("Type de sauvegarde trop complexe.");
        byte kind = input.ReadByte();
        if (kind == 0)
        {
            string name = input.ReadString();
            return KnownTypes.TryGetValue(name, out Type? type) ? type : throw new InvalidDataException("Type de sauvegarde inconnu.");
        }
        Type first = ReadType(input, depth + 1);
        return kind switch
        {
            1 => first.MakeArrayType(),
            2 => typeof(List<>).MakeGenericType(first),
            3 => typeof(Dictionary<,>).MakeGenericType(first, ReadType(input, depth + 1)),
            4 => typeof(HashSet<>).MakeGenericType(first),
            5 => typeof(ValueTuple<,>).MakeGenericType(first, ReadType(input, depth + 1)),
            _ => throw new InvalidDataException("Type de collection inconnu."),
        };
    }

    private sealed class Writer(BinaryWriter output)
    {
        private readonly Dictionary<object, int> _objects = new(ReferenceEqualityComparer.Instance);
        internal void Value(object? value, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("Le monde comporte trop de références imbriquées.");
            if (value is null) { output.Write((byte)0); return; }
            if (value is Species species) { output.Write((byte)2); output.Write(Species.All.ToList().IndexOf(species)); return; }
            Type type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
            {
                output.Write((byte)3); WriteType(output, type); Scalar(value, type); return;
            }
            if (!type.IsValueType)
            {
                if (_objects.TryGetValue(value, out int id)) { output.Write((byte)1); output.Write(id); return; }
                if (_objects.Count >= MaxObjects) throw new InvalidDataException("Le monde est trop volumineux.");
                _objects[value] = _objects.Count;
            }
            if (value is Random random)
            {
                output.Write((byte)4); RandomState.Write(output, random); return;
            }
            output.Write((byte)5); WriteType(output, type);
            if (value is Array array)
            {
                output.Write(array.Length);
                Type element = type.GetElementType()!;
                if (element.IsPrimitive)
                {
                    byte[] bytes = new byte[Buffer.ByteLength(array)];
                    Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length); output.Write(bytes);
                }
                else if (element.IsEnum)
                    foreach (object item in array) Scalar(item, element);
                else foreach (object? item in array) Value(item, depth + 1);
            }
            else if (value is IDictionary dictionary)
            {
                output.Write(dictionary.Count);
                foreach (DictionaryEntry entry in dictionary) { Value(entry.Key, depth + 1); Value(entry.Value, depth + 1); }
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(List<>) || definition == typeof(HashSet<>)))
            {
                var items = ((IEnumerable)value).Cast<object?>().ToArray();
                output.Write(items.Length); foreach (object? item in items) Value(item, depth + 1);
            }
            else
            {
                FieldInfo[] fields = FieldCache.GetValueOrDefault(type) ?? Fields(type);
                output.Write(fields.Length);
                foreach (FieldInfo field in fields) Value(field.GetValue(value), depth + 1);
            }
        }

        private void Scalar(object value, Type type)
        {
            if (type.IsEnum) { output.Write(Convert.ToInt64(value)); return; }
            switch (value)
            {
                case string text: output.Write(text); break;
                case int number: output.Write(number); break;
                case long number: output.Write(number); break;
                case uint number: output.Write(number); break;
                case byte number: output.Write(number); break;
                case bool flag: output.Write(flag); break;
                case float number: output.Write(number); break;
                case double number: output.Write(number); break;
                default: throw new InvalidDataException("Valeur non prise en charge.");
            }
        }
    }

    private sealed class Reader(BinaryReader input, LegacySchema? legacy = null)
    {
        private readonly List<object> _objects = [];
        private int Count()
        {
            int count = input.ReadInt32();
            return count is >= 0 and <= MaxItems ? count : throw new InvalidDataException("Taille de collection invalide.");
        }
        private void Register(object value)
        {
            if (_objects.Count >= MaxObjects) throw new InvalidDataException("Trop d'objets dans la sauvegarde.");
            _objects.Add(value);
        }
        internal object? Value(int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("Sauvegarde trop imbriquée.");
            byte tag = input.ReadByte();
            if (tag == 0) return null;
            if (tag == 1)
            {
                int id = input.ReadInt32();
                return id >= 0 && id < _objects.Count ? _objects[id] : throw new InvalidDataException("Référence de sauvegarde invalide.");
            }
            if (tag == 2)
            {
                int index = input.ReadInt32();
                return index >= 0 && index < Species.All.Count ? Species.All[index] : throw new InvalidDataException("Peuple inconnu.");
            }
            if (tag == 3) return Scalar(ReadType(input));
            if (tag == 4) { Random random = RandomState.Read(input); Register(random); return random; }
            if (tag != 5) throw new InvalidDataException("Élément de sauvegarde inconnu.");
            Type type = ReadType(input);
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                throw new InvalidDataException("Type d'objet de sauvegarde invalide.");
            if (type.IsArray)
            {
                int count = Count(); Type element = type.GetElementType()!;
                Array array = Array.CreateInstance(element, count); Register(array);
                if (element.IsPrimitive)
                {
                    int size = Buffer.ByteLength(array); byte[] bytes = input.ReadBytes(size);
                    if (bytes.Length != size) throw new EndOfStreamException();
                    Buffer.BlockCopy(bytes, 0, array, 0, size);
                }
                else for (int i = 0; i < count; i++) array.SetValue(element.IsEnum ? Scalar(element) : Value(depth + 1), i);
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(List<>) || definition == typeof(Dictionary<,>) || definition == typeof(HashSet<>)))
            {
                object collection = Activator.CreateInstance(type)!; Register(collection);
                int count = Count();
                if (collection is IDictionary dictionary)
                    for (int i = 0; i < count; i++) dictionary.Add(Value(depth + 1)!, Value(depth + 1));
                else
                {
                    MethodInfo add = type.GetMethod("Add")!;
                    for (int i = 0; i < count; i++) add.Invoke(collection, [Value(depth + 1)]);
                }
                return collection;
            }
            object instance = RuntimeHelpers.GetUninitializedObject(type);
            if (!type.IsValueType) Register(instance);
            FieldInfo[] fields = FieldCache.GetValueOrDefault(type) ?? Fields(type);
            if (legacy?.NamesOf(type) is { } legacyNames)
            {
                ReadLegacyFields(instance, type, legacyNames, fields, depth);
                return instance;
            }
            if (Count() != fields.Length) throw new InvalidDataException("Le schéma de la sauvegarde ne correspond pas à cette version du jeu.");
            foreach (FieldInfo field in fields) field.SetValue(instance, Value(depth + 1));
            return instance;
        }

        /// <summary>
        /// Lit les champs d'un objet v1 : l'ordre et le nombre viennent du schéma figé, la valeur va au champ de même nom du type actuel
        /// (ignorée s'il n'existe plus). Un type incompatible reste une erreur : on ne devine jamais.
        /// </summary>
        private void ReadLegacyFields(object instance, Type type, string[] names, FieldInfo[] current, int depth)
        {
            if (Count() != names.Length) throw new InvalidDataException("Le schéma de la sauvegarde ne correspond pas au format v1.");
            foreach (string name in names)
            {
                object? value = Value(depth + 1);
                FieldInfo? field = Array.Find(current, f => f.Name == name);
                object targetInstance = instance;
                if (field is null && instance is Colony colony)
                {
                    string localName = name == "_exhaustedDaysLeft" ? "<ExhaustedDaysLeft>k__BackingField"
                        : name == "_layout" ? "LayoutState" : name;
                    field = Array.Find(FieldCache[typeof(Settlement)], f => f.Name == localName);
                    targetInstance = colony.PrimarySettlement;
                }
                if (field is null) continue;
                Type target = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                if (value is null ? field.FieldType.IsValueType && Nullable.GetUnderlyingType(field.FieldType) is null : !target.IsInstanceOfType(value))
                    throw new InvalidDataException($"Le champ {type.Name}.{name} a changé de type depuis le format v1.");
                field.SetValue(targetInstance, value);
            }
        }
        private object Scalar(Type type)
        {
            if (type.IsEnum) return Enum.ToObject(type, input.ReadInt64());
            if (type == typeof(string)) return input.ReadString();
            if (type == typeof(int)) return input.ReadInt32();
            if (type == typeof(long)) return input.ReadInt64();
            if (type == typeof(uint)) return input.ReadUInt32();
            if (type == typeof(byte)) return input.ReadByte();
            if (type == typeof(bool)) return input.ReadBoolean();
            if (type == typeof(float)) return input.ReadSingle();
            if (type == typeof(double)) return input.ReadDouble();
            throw new InvalidDataException("Valeur de sauvegarde invalide.");
        }
    }

    /// <summary>
    /// État du générateur à graine de .NET 8. Le schéma vérifie explicitement l'implémentation avant toute restauration.
    /// Cela garde les mêmes tirages après chargement, sans rejouer toute la partie ni changer les graines existantes.
    /// </summary>
    private static class RandomState
    {
        private static readonly FieldInfo Impl = typeof(Random).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly Type Implementation = new Random(1).GetType() == typeof(Random)
            ? Impl.GetValue(new Random(1))!.GetType() : throw new NotSupportedException();
        private static readonly FieldInfo Prng = Implementation.GetField("_prng", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo[] Parts = Prng.FieldType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(f => f.Name).ToArray();
        internal static string Schema => Implementation.FullName + ":" + string.Join(",", Parts.Select(f => f.Name + "=" + f.FieldType));
        internal static void Write(BinaryWriter output, Random random)
        {
            object impl = Impl.GetValue(random)!;
            if (impl.GetType() != Implementation || Parts.Length != 3) throw new InvalidDataException("Générateur aléatoire incompatible.");
            object state = Prng.GetValue(impl)!;
            foreach (FieldInfo field in Parts)
                if (field.GetValue(state) is int[] array)
                {
                    output.Write(array.Length); foreach (int value in array) output.Write(value);
                }
                else output.Write((int)field.GetValue(state)!);
        }
        internal static Random Read(BinaryReader input)
        {
            var random = new Random(1); object impl = Impl.GetValue(random)!; object state = Prng.GetValue(impl)!;
            foreach (FieldInfo field in Parts)
                if (field.FieldType == typeof(int[]))
                {
                    if (input.ReadInt32() != 56) throw new InvalidDataException("État aléatoire invalide.");
                    var array = new int[56]; for (int i = 0; i < array.Length; i++) array[i] = input.ReadInt32();
                    field.SetValue(state, array);
                }
                else
                {
                    int value = input.ReadInt32();
                    if (value is < 0 or > 55) throw new InvalidDataException("État aléatoire invalide.");
                    field.SetValue(state, value);
                }
            Prng.SetValue(impl, state); return random;
        }
    }
}
