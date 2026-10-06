using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

public static partial class Trade
{
    private static double PlanScore(WorldState world, TradePlan plan)
    {
        SupplierMemory? memory = Suppliers(world, plan.From).FirstOrDefault(m => m.Supplier == plan.To);
        return plan.GainHours * (0.5 + 0.5 * (memory?.Confidence(world.Clock.Ticks) ?? 0.5)) - plan.CostHours;
    }

    private static int RoutingRevision(WorldState world, Colony owner)
    {
        int stamp = world.WorldMap.PassageRevision;
        foreach (int region in HostileRegions(world, owner).Order())
            stamp = unchecked(stamp * 31 + region + 1);
        return stamp;
    }

    /// <summary>
    /// Les régions où le peuple n'a pas le droit de passer : celles d'un ennemi, et celles d'un voisin qui lui refuse le passage
    /// (voir <see cref="Diplomacy.PassageAllowed"/>). Les établissements et les régions possédées comptent, même si aucun village n'y vit.
    /// </summary>
    internal static HashSet<int> HostileRegions(WorldState world, Colony owner)
    {
        var denied = new HashSet<int>();
        foreach (Colony host in world.Colonies.Where(c => c != owner && !Diplomacy.PassageAllowed(world, owner, c)))
        {
            foreach (Settlement place in host.Settlements.Where(s => s.Status != SettlementStatus.Closed)) denied.Add(place.RegionTileIndex);
            foreach (RegionState region in world.Regions.Values.Where(r => r.OwnerColonyId == host.Id)) denied.Add(region.TileIndex);
        }
        return denied;
    }

    private static WorldRoute? RouteForTrade(WorldState world, Colony owner, int start, int target) =>
        world.WorldMap.TravelRoute(start, target, HostileRegions(world, owner));

    private static WorldRoute? AccessibleRoute(WorldState world, Caravan trip, int start) =>
        RouteForTrade(world, trip.From, start,
            trip.State == CaravanState.Outbound && trip.Purpose != TerritorialPurpose.Commerce ? trip.TargetRegion
                : world.SettlementById(trip.State == CaravanState.Outbound ? trip.ToSettlementId : trip.FromSettlementId)?.RegionTileIndex
                    ?? world.WorldMap.TileOf(trip.State == CaravanState.Outbound ? trip.To : trip.From));

    private static void SetRoute(WorldState world, Caravan trip, WorldRoute route)
    {
        trip.Route = route; trip.RouteIndex = 0; trip.SegmentTravelCost = 0;
        trip.RouteRevision = RoutingRevision(world, trip.From);
        trip.BlockedReason = null;
        long travel = (long)Math.Ceiling(route.Cost / (WorldMap.CaravanTilesPerDay * SpeedOf(trip)) * TimeConstants.TicksPerDay);
        if (trip.State == CaravanState.Outbound)
        {
            trip.ArriveTicks = world.Clock.Ticks + travel;
            trip.ReturnTicks = trip.ArriveTicks + TimeConstants.TicksPerDay / 2 + travel;
        }
        else trip.ReturnTicks = Math.Max(world.Clock.Ticks, trip.RestUntilTicks) + travel;
    }

    /// <summary>Avance réellement de segment en segment ; une interruption ne ramène jamais les biens au stock.</summary>
    private static void AdvanceVoyage(WorldState world, Caravan trip)
    {
        long now = world.Clock.Ticks;
        double ticks = Math.Max(0, now - trip.LastRouteTicks);
        trip.LastRouteTicks = now;
        if (trip.State == CaravanState.Home) return;
        if (trip.Route is null)
        {
            // Les caravanes v1 reprennent leur position temporelle, sans recommencer le trajet.
            float oldPosition = trip.RoutePosition(now);
            WorldRoute? legacyRoute = world.WorldMap.Route(trip.From, trip.To);
            if (legacyRoute is null) { trip.BlockedReason = "Aucun passage praticable."; return; }
            if (trip.State == CaravanState.Returning)
                legacyRoute = new WorldRoute(legacyRoute.Tiles.Reverse().ToArray(), legacyRoute.Cumulative.Reverse().Select(c => legacyRoute.Cost - c).ToArray());
            trip.Route = legacyRoute;
            double traveled = legacyRoute.Cost * (trip.State == CaravanState.Outbound ? oldPosition : 1 - oldPosition);
            while (trip.RouteIndex + 1 < legacyRoute.Tiles.Count && legacyRoute.Cumulative[trip.RouteIndex + 1] <= traveled) trip.RouteIndex++;
            trip.SegmentTravelCost = traveled - legacyRoute.Cumulative[trip.RouteIndex];
            ticks = 0;
        }
        double costPerTick = WorldMap.CaravanTilesPerDay * SpeedOf(trip) / TimeConstants.TicksPerDay;
        while (true)
        {
            if (trip.State == CaravanState.Returning && trip.RestUntilTicks > now - ticks)
            {
                double rest = Math.Min(ticks, trip.RestUntilTicks - (now - ticks));
                if (trip.Purpose is TerritorialPurpose.Prospection or TerritorialPurpose.RoadWork && !trip.Aborted) trip.WorkedTicks += (long)rest; // l'équipe travaille pendant l'attente
                ticks -= rest;
                if (ticks <= 0) return;
            }
            WorldRoute route = trip.Route!;
            int node = route.Tiles[trip.RouteIndex];
            // On termine l'arête déjà empruntée avant de changer d'itinéraire.
            if (trip.SegmentTravelCost <= 0)
            {
                if (trip.State == CaravanState.Outbound && Diplomacy.AtWar(world, trip.From, trip.To))
                {
                    trip.Aborted = true; trip.State = CaravanState.Returning;
                    trip.RouteRevision = -1;
                }
                if (trip.RouteRevision != RoutingRevision(world, trip.From) || trip.BlockedReason is not null)
                {
                    WorldRoute? replacement = AccessibleRoute(world, trip, node);
                    if (replacement is null && trip.State == CaravanState.Outbound
                        && trip.Provisions.AvailableNutrition < trip.Traders.Count * (decimal)TravelerNutritionPerDay)
                    {
                        trip.Aborted = true; trip.State = CaravanState.Returning;
                        replacement = AccessibleRoute(world, trip, node);
                    }
                    if (replacement is null)
                    {
                        trip.BlockedReason = "Passage fermé : la caravane attend avec son chargement.";
                        trip.ReturnTicks += (long)ticks;
                        if (trip.State == CaravanState.Outbound) trip.ArriveTicks += (long)ticks;
                        return;
                    }
                    bool detour = !route.Tiles.Skip(trip.RouteIndex).SequenceEqual(replacement.Tiles);
                    SetRoute(world, trip, replacement);
                    if (detour) ColonyBrain.Say(trip.From, world.Clock, trip.Aborted
                        ? "Le voyage est interrompu : les marchands reprennent le chemin du retour."
                        : "La caravane emprunte un autre passage.");
                    route = replacement;
                }
            }
            if (trip.RouteIndex >= route.Tiles.Count - 1)
            {
                if (trip.State == CaravanState.Returning)
                {
                    ComeHome(world, trip);
                    return;
                }
                Settle(world, trip);
                if (trip.State == CaravanState.Home) return;
                long stay = !trip.Aborted && TerritorialTravel.StayDays(trip.Purpose) > 0 ? TerritorialTravel.StayDays(trip.Purpose) * (long)TimeConstants.TicksPerDay : TimeConstants.TicksPerDay / 2;
                trip.RestUntilTicks = (long)(now - ticks) + stay;
                WorldRoute? back = AccessibleRoute(world, trip, node);
                if (back is null)
                {
                    trip.BlockedReason = "Le retour est coupé : les marchands attendent chez leur hôte.";
                    return;
                }
                SetRoute(world, trip, back);
                continue;
            }
            if (ticks <= 0) return;
            double edge = route.Cumulative[trip.RouteIndex + 1] - route.Cumulative[trip.RouteIndex];
            double needed = Math.Max(0, edge - trip.SegmentTravelCost) / costPerTick;
            if (ticks + 0.00001 < needed)
            {
                trip.SegmentTravelCost += ticks * costPerTick;
                return;
            }
            ticks = Math.Max(0, ticks - needed);
            trip.RouteIndex++; trip.SegmentTravelCost = 0;
            world.WorldMap.Roads.RecordUse(route.Tiles[trip.RouteIndex - 1], route.Tiles[trip.RouteIndex]);
            CheckInterception(world, trip, route.Tiles[trip.RouteIndex - 1], route.Tiles[trip.RouteIndex], edge);
        }
    }
}
