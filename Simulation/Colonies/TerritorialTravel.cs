using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

public enum TerritorialPurpose { Commerce = 0, Prospection = 1, Foundation = 2, Supply = 3, Evacuation = 4, Relocation = 5, RoadWork = 6 }

/// <summary>Les missions internes partagent la marche, les fermetures de route et les provisions des caravanes.</summary>
public static class TerritorialTravel
{
    internal static void Observe(WorldState world, Colony observer, RegionState region)
    {
        // Une visite ne révèle que les affleurements : le reste exige un sondage (voir Prospection).
        Prospection.Survey(world, observer, region, 0, "Visite");
    }

    public static Caravan? Depart(WorldState world, Settlement source, int targetRegion, TerritorialPurpose purpose,
        IReadOnlyDictionary<ResourceType, int> cargo, int people = 2, Settlement? destination = null, IReadOnlyList<Colonist>? chosen = null)
    {
        Colony owner = source.Owner;
        using var scope = owner.UseSettlement(source);
        if (!Enum.IsDefined(purpose) || purpose == TerritorialPurpose.Commerce || people < 1 || source.Status == SettlementStatus.Closed
            || targetRegion < 0 || targetRegion >= world.WorldMap.Grid.Tiles.Length
            || destination is not null && (destination.Owner != owner || destination.RegionTileIndex != targetRegion || destination.Status != SettlementStatus.Active)
            || purpose is TerritorialPurpose.Supply or TerritorialPurpose.Evacuation or TerritorialPurpose.Relocation && destination is null
            || purpose == TerritorialPurpose.Relocation && (chosen is null || chosen.Count != people || chosen.Any(c => !source.Population.Contains(c)))) return null;
        if (world.Regions.TryGetValue(targetRegion, out RegionState? existing) && existing.OwnerColonyId is int claimed && claimed != owner.Id) return null;
        var route = world.WorldMap.TravelRoute(source.RegionTileIndex, targetRegion,
            Trade.HostileRegions(world, owner));
        if (route is null || !world.WorldMap.Grid[targetRegion].Habitable) return null;
        var residents = new List<Colonist>();
        if (purpose == TerritorialPurpose.Evacuation) residents.AddRange(source.Population);
        else if (purpose == TerritorialPurpose.Relocation) residents.AddRange(chosen!); // une famille entière, choisie par la colonie
        else
        {
            foreach (Colonist person in source.Population.Where(c => c.Stage == LifeStage.Adult).OrderBy(c => c.Partner is not null).ThenBy(c => c.Id))
            {
                if (residents.Count >= people) break;
                if (person.Partner is not null && purpose == TerritorialPurpose.Foundation && source.Population.Count(c => c.Stage == LifeStage.Adult && c.Partner is null) >= people) continue;
                if (person.Sector is WorkSector.Food or WorkSector.Farm or WorkSector.Wood
                    && source.Population.Count(c => c.Sector == person.Sector && !residents.Contains(c)) <= 1) continue;
                residents.Add(person);
            }
        }
        if (purpose == TerritorialPurpose.Foundation)
        {
            foreach (Colonist person in residents.ToArray())
                if (person.Partner is { } partner && source.Population.Contains(partner) && !residents.Contains(partner)) residents.Add(partner);
            if (residents.Count > 6 || source.Population.Any(c => c.Stage == LifeStage.Child && (residents.Contains(c.Mother!) || residents.Contains(c.Father!)))) return null;
            foreach (WorkSector sector in new[] { WorkSector.Food,WorkSector.Farm,WorkSector.Wood })
                if (source.Population.Any(c => c.Sector == sector) && !source.Population.Any(c => c.Sector == sector && !residents.Contains(c))) return null;
        }
        if (residents.Count < people || source.Population.Count - residents.Count < (purpose == TerritorialPurpose.Evacuation ? 0 : purpose == TerritorialPurpose.Supply ? 2 : purpose == TerritorialPurpose.Relocation ? 8 : 5)) return null;
        people = residents.Count;
        // Des prospecteurs mangent aussi pendant leur séjour de sondage.
        double days = 2 * route.Cost / WorldMap.CaravanTilesPerDay + 0.5 + StayDays(purpose);
        // Des vivres variés (pain, grain, viande salée…), choisis parmi ce que le stock a de disponible hors chargement.
        List<(ResourceType Resource, int Amount)>? provisions = Trade.ProvisionLoad(owner, (decimal)((days + Trade.ProvisionMarginDays) * people * Trade.TravelerNutritionPerDay), cargo, partial: purpose == TerritorialPurpose.Evacuation);
        if (provisions is null) return null;
        var load = cargo.ToDictionary(p => p.Key, p => p.Value);
        foreach ((ResourceType food, int units) in provisions) load[food] = load.GetValueOrDefault(food) + units;
        if (load.Any(p => p.Value < 0 || source.Stock.Available(p.Key) < p.Value)
            || ResourceCatalog.WeightOf(load) > 40 * people
            || purpose != TerritorialPurpose.Evacuation && source.Stock.AvailableNutrition - load.Sum(p => p.Value * ResourceCatalog.Nutrition(p.Key))
                < Math.Max(1, source.Population.Count - people) * (decimal)Trade.TravelerNutritionPerDay * 2) return null;
        long now = world.Clock.Ticks;
        var trip = new Caravan(owner, owner, residents, [], 0, now,
            now + (long)(days / 2 * TimeConstants.TicksPerDay), now + (long)(days * TimeConstants.TicksPerDay))
        { Purpose = purpose, TargetRegion = targetRegion, FromSettlementId = source.Id, ToSettlementId = destination?.Id ?? 0,
            Route = route, RouteRevision = -1 };
        // Tous les intrants ont été vérifiés avant le premier débit ; aucune autre tâche ne s'intercale ici.
        foreach (var item in cargo)
            if (item.Value > 0 && !source.Stock.TryTransferTo(trip.Inventory, item.Key, item.Value)) throw new InvalidOperationException("Chargement interne indisponible.");
        foreach ((ResourceType food, int units) in provisions)
            if (!source.Stock.TryTransferTo(trip.Provisions, food, units)) throw new InvalidOperationException("Provisions internes indisponibles.");
        world.RegisterTrip(trip);
        foreach (Colonist person in residents)
        {
            ColonistAI.DetachFromColony(person);
            person.Transit = TransitState.None;
        }
        world.Caravans.Add(trip);
        owner.LastTerritorialTicks = now;
        ColonyBrain.Say(owner, world.Clock, $"{people} habitants partent : {Label(purpose)}.");
        return trip;
    }

    /// <summary>Jours qu'une équipe travaille sur place avant de rentrer : sondage ou aménagement de route.</summary>
    internal static int StayDays(TerritorialPurpose purpose) => purpose switch
    {
        TerritorialPurpose.Prospection => Prospection.StayDays,
        TerritorialPurpose.RoadWork => WorldRoadNetwork.WorkDays,
        _ => 0,
    };

    /// <summary>Une équipe part aménager une arête de route mondiale avec les matériaux qu'elle porte : bois et pierre prélevés sur les surplus du village.</summary>
    internal static Caravan? DepartRoadWork(WorldState world, Settlement source, int near, int far, int level, (int Wood, int Stone) materials)
    {
        Colony owner = source.Owner;
        using var scope = owner.UseSettlement(source);
        if (source.Stock.Available(ResourceType.Wood) - (int)ColonyBrain.HeatingTarget(owner, world.Clock.Season) - 10 < materials.Wood
            || source.Stock.Available(ResourceType.Stone) - ColonyBrain.StoneReserveTarget < materials.Stone) return null;
        Caravan? trip = Depart(world, source, near, TerritorialPurpose.RoadWork,
            new Dictionary<ResourceType, int> { [ResourceType.Wood] = materials.Wood, [ResourceType.Stone] = materials.Stone }, WorldRoadNetwork.Crew);
        if (trip is null) return null;
        trip.RoadEdgeA = near; trip.RoadEdgeB = far; trip.RoadLevel = level;
        return trip;
    }

    public static string Label(TerritorialPurpose purpose) => purpose switch
    { TerritorialPurpose.Prospection => "prospection", TerritorialPurpose.Foundation => "fondation d'un camp",
        TerritorialPurpose.Supply => "ravitaillement", TerritorialPurpose.Evacuation => "évacuation",
        TerritorialPurpose.Relocation => "déménagement d'une famille", TerritorialPurpose.RoadWork => "aménagement d'une route", _ => "commerce" };

    internal static void Arrive(WorldState world, Caravan trip)
    {
        trip.State = CaravanState.Returning;
        if (trip.Aborted) return;
        RegionState region = world.VisitRegion(trip.TargetRegion);
        if (trip.Purpose == TerritorialPurpose.RoadWork) { trip.Delivered = true; return; }
        if (trip.Purpose == TerritorialPurpose.Prospection)
        {
            // Les prospecteurs sondent pendant leur séjour (voir Trade.Routing) : leurs observations ne sont établies qu'à leur départ du site.
            trip.Delivered = true; return;
        }
        Observe(world, trip.From, region);
        Settlement? target = world.SettlementById(trip.ToSettlementId);
        if (trip.Purpose == TerritorialPurpose.Foundation)
        {
            if (world.Settlements.Any(s => s.RegionTileIndex == trip.TargetRegion && s.Status != SettlementStatus.Closed)
                || region.OwnerColonyId is int claimed && claimed != trip.From.Id) return;
            var place = ColonyFounder.FindCampSite(region.Map);
            if (!ColonyFounder.CanFoundAt(region.Map, place.X, place.Y, out _)) return;
            target = world.FoundSettlement(trip.From, region, place.X, place.Y);
            trip.ToSettlementId = target.Id;
        }
        if (target is null || target.Status != SettlementStatus.Active || target.Owner != trip.From) return;
        bool permanentMove = trip.Purpose is TerritorialPurpose.Foundation or TerritorialPurpose.Evacuation or TerritorialPurpose.Relocation;
        if (trip.Inventory.Amounts.Keys.Concat(permanentMove ? trip.Provisions.Amounts.Keys : Enumerable.Empty<ResourceType>()).Distinct()
            .Any(g => (long)target.Stock.Get(g) + trip.Inventory.Get(g) + (permanentMove ? trip.Provisions.Get(g) : 0) > int.MaxValue))
        { trip.BlockedReason = "Le dépôt destinataire est plein."; return; }
        foreach (var item in trip.Inventory.Amounts.ToArray())
            if (item.Value > 0 && !trip.Inventory.TryTransferTo(target.Stock, item.Key, item.Value)) throw new InvalidOperationException("Livraison interne indisponible.");
        trip.Delivered = true;
        if (trip.Purpose == TerritorialPurpose.Supply && target != trip.From.PrimarySettlement)
        {
            using var local = trip.From.UseSettlement(target);
            foreach (var item in target.Stock.Amounts.Where(p => ResourceCatalog.Nutrition(p.Key) == 0 && p.Key is not ResourceType.Coins and not ResourceType.Wood and not ResourceType.Tools).OrderBy(p => p.Key).ToArray())
            {
                int surplus = Math.Max(0, target.Stock.Available(item.Key) - (int)Math.Ceiling(Economy.Need(trip.From, item.Key)));
                int units = Math.Min(20, Math.Min(surplus, (int)Math.Max(0, (40 * trip.Traders.Count - trip.LoadWeight) / ResourceCatalog.Weight(item.Key))));
                if (units > 0) target.Stock.TryTransferTo(trip.Inventory, item.Key, units);
            }
        }
        if (permanentMove)
        {
            foreach (var item in trip.Provisions.Amounts.ToArray())
                if (item.Value > 0 && !trip.Provisions.TryTransferTo(target.Stock, item.Key, item.Value)) throw new InvalidOperationException("Provisions d’arrivée indisponibles.");
            foreach (Colonist person in trip.Traders) Relocate(world, person, target);
            if (trip.Purpose == TerritorialPurpose.Evacuation && world.SettlementById(trip.FromSettlementId) is { } old && !old.Residents.Any())
                old.Status = SettlementStatus.Closed;
            trip.State = CaravanState.Home; world.Caravans.Remove(trip);
        }
    }

    internal static void Return(WorldState world, Caravan trip)
    {
        Settlement source = world.SettlementById(trip.FromSettlementId)!;
        if (trip.Inventory.Amounts.Keys.Concat(trip.Provisions.Amounts.Keys).Distinct().Any(g => (long)source.Stock.Get(g) + trip.Inventory.Get(g) + trip.Provisions.Get(g) > int.MaxValue))
        { trip.BlockedReason = "Le stock de retour est plein."; return; }
        if (trip.Purpose == TerritorialPurpose.RoadWork && trip.Delivered)
        {
            // L'aménagement n'a lieu que si l'équipe a travaillé tout son séjour et porte encore ses matériaux : ils sont alors consommés une seule fois.
            (int wood, int stone) = WorldRoadNetwork.Materials(world.WorldMap.StepCostOf(trip.RoadEdgeA, trip.RoadEdgeB), trip.RoadLevel);
            bool complete = trip.WorkedTicks >= WorldRoadNetwork.WorkDays * (long)TimeConstants.TicksPerDay * 9 / 10
                && trip.Inventory.Get(ResourceType.Wood) >= wood && trip.Inventory.Get(ResourceType.Stone) >= stone
                && world.WorldMap.Roads.LevelOf(trip.RoadEdgeA, trip.RoadEdgeB) == trip.RoadLevel - 1;
            if (complete && trip.Inventory.TryTake(ResourceType.Wood, wood) && trip.Inventory.TryTake(ResourceType.Stone, stone)
                && world.WorldMap.ImproveRoad(trip.RoadEdgeA, trip.RoadEdgeB, world.Territory.MaxRoadLevel))
                ColonyBrain.Say(trip.From, world.Clock, $"Notre équipe a aménagé la route (niveau {trip.RoadLevel}) : les caravanes y gagneront du temps.");
            else
                ColonyBrain.Say(trip.From, world.Clock, "Les travaux de route n'ont pas pu s'achever : l'équipe rapporte ses matériaux.");
            trip.Delivered = false;
        }
        if (trip.Purpose == TerritorialPurpose.Prospection && trip.Delivered)
        {
            Prospection.Report(world, trip);
            if (!trip.From.VisitedRegions.Contains(trip.TargetRegion)) trip.From.VisitedRegions.Add(trip.TargetRegion);
            trip.From.RegionReach[trip.TargetRegion] = Math.Max(trip.From.RegionReach.GetValueOrDefault(trip.TargetRegion, -1), trip.SurveyReach);
            int fresh = trip.SurveyReports!.Count(report => Prospection.Learn(trip.From, report) && report.State != DepositObservation.Hint);
            ColonyBrain.Say(trip.From, world.Clock, trip.SurveyReports!.Count == 0
                ? "Nos prospecteurs rentrent : ils n'ont rien trouvé d'exploitable."
                : $"Nos prospecteurs rentrent : {trip.SurveyReports.Count} gisement{(trip.SurveyReports.Count > 1 ? "s" : "")} reconnu{(trip.SurveyReports.Count > 1 ? "s" : "")}"
                  + (fresh > 0 ? $", dont {fresh} confirmé{(fresh > 1 ? "s" : "")}" : "") + $" (sondage à {trip.SurveyReach} niveau{(trip.SurveyReach > 1 ? "x" : "")}).");
            trip.SurveyReports = null;
        }
        foreach (Stockpile inventory in new[] { trip.Inventory, trip.Provisions })
            foreach (var item in inventory.Amounts.ToArray())
                if (item.Value > 0 && !inventory.TryTransferTo(source.Stock, item.Key, item.Value))
                { trip.BlockedReason = "Le stock de retour est plein."; return; }
        foreach (Colonist person in trip.Traders) Relocate(world, person, source, preserveHome: true);
        trip.State = CaravanState.Home; world.Caravans.Remove(trip);
    }

    private static void Relocate(WorldState world, Colonist person, Settlement target, bool preserveHome = false)
    {
        foreach (Settlement place in world.Settlements) place.Transients.Remove(person);
        if (!preserveHome)
        {
            person.Home?.Residents.Remove(person); person.Home = null;
            person.HomeSettlementId = target.Id;
        }
        person.Activity = null; person.TravelId = 0; person.Transit = TransitState.None;
        person.LocationSettlementId = target.Id;
        person.X = person.PrevX = target.CampX + 0.5f; person.Y = person.PrevY = target.CampY + 0.5f;
        using var scope = target.Owner.UseSettlement(target);
        target.Owner.FillVacancies(); target.Owner.AssignSectors();
    }

    internal static void Daily(WorldState world, Settlement settlement)
    {
        Colony owner = settlement.Owner;
        ObserveLocal(world, settlement);
        Prospection.LocalDaily(world, settlement);
        ExpansionPlanner.WatchExhaustion(world, settlement);
        ExtendedIndustry.Daily(world, settlement);
        if (!world.TerritorialDevelopment || settlement.Population.Count == 0) return;
        if (settlement != owner.PrimarySettlement)
        {
            LogisticsPlanner.WatchShortage(world, settlement);
            if (settlement.Status != SettlementStatus.Active) return;
            bool viable = owner.DepositReports.Any(k => k.Region == settlement.RegionTileIndex && k.State != DepositObservation.Depleted
                && (k.Material is ResourceType.IronOre or ResourceType.CopperOre or ResourceType.MineralCoal
                    || ExtendedIndustry.Target(owner, k.Material) > 0));
            settlement.UnproductiveDays = viable ? 0 : settlement.UnproductiveDays + 1;
            if (settlement.UnproductiveDays >= 7 && settlement.Kind == SettlementKind.Camp)
            {
                // Une fermeture conserve le terrain et les ouvrages ; le transfert emporte seulement les biens chargeables.
                var goods = new Dictionary<ResourceType,int>();
                double room = Math.Max(0, settlement.Population.Count * 25);
                foreach (var item in settlement.Stock.Amounts.Where(p => p.Key != ResourceType.Grain).OrderBy(p => p.Key))
                { int units = Math.Min(item.Value, (int)(room / ResourceCatalog.Weight(item.Key))); if (units > 0) { goods[item.Key] = units; room -= units * ResourceCatalog.Weight(item.Key); } }
                Depart(world, settlement, owner.PrimarySettlement.RegionTileIndex, TerritorialPurpose.Evacuation, goods,
                    settlement.Population.Count, owner.PrimarySettlement);
            }
            // Le stade est une description : il change quand les conditions du suivant tiennent sept jours de suite (jamais de rétrogradation).
            SettlementKind deserved = ExpansionPlanner.StageOf(settlement);
            settlement.StableDays = ExpansionPlanner.Rank(deserved) > ExpansionPlanner.Rank(settlement.Kind) ? settlement.StableDays + 1 : 0;
            if (settlement.StableDays >= 7)
            {
                settlement.Kind = deserved; settlement.StableDays = 0;
                ColonyBrain.Say(owner, world.Clock, $"{settlement.Name} est devenu {(deserved == SettlementKind.Village ? "un village" : "un hameau")} : logements, services et activités tiennent.");
            }
            return;
        }
        if (world.Caravans.Any(t => t.From == owner && t.State != CaravanState.Home)) return;
        // Les besoins des camps sont classés (survie, vivres réguliers, outils, intrants, chantiers) et couverts dans la limite d'une charge réelle ;
        // un camp qui a du surplus utile au village principal le renvoie tous les trois jours.
        if (LogisticsPlanner.PlanSupply(world, owner, settlement) || world.Clock.TotalDays % 3 == 0 && LogisticsPlanner.PlanReturn(world, owner, settlement)) return;
        TerritorialRules rules = world.Territory;
        if (world.Clock.Ticks - owner.LastTerritorialTicks < rules.ExpeditionCooldownDays * TimeConstants.TicksPerDay) return;
        if (WorldRoadNetwork.PlanImprovement(world, owner, settlement)) return;
        if (ExpansionPlanner.TryRelocateFamily(world, owner, settlement)) return;
        DepositKnowledge? site = settlement.Population.Count >= rules.MinFoundingPopulation
            && owner.Settlements.Count(s => s.Status != SettlementStatus.Closed) < rules.MaxActiveSettlements
            ? ExpansionPlanner.BestCampSite(world, owner, settlement) : null;
        if (site is not null && Depart(world, settlement, site.Region, TerritorialPurpose.Foundation,
                new Dictionary<ResourceType,int> { [ResourceType.Wood] = 24, [ResourceType.Grain] = 24, [ResourceType.Tools] = 2 }, rules.FoundersPerCamp) is not null) return;
        int next = Prospection.Choose(world, owner, settlement);
        if (next >= 0) Depart(world, settlement, next, TerritorialPurpose.Prospection, new Dictionary<ResourceType,int>());
    }

    private static void ObserveLocal(WorldState world, Settlement settlement)
    {
        if (!settlement.Owner.VisitedRegions.Contains(settlement.RegionTileIndex)) Observe(world, settlement.Owner, world.VisitRegion(settlement.RegionTileIndex));
    }
}
