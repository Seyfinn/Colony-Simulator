using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Logistique interne : besoins classés, charge réelle, vivres variés, rupture prolongée et évacuation.</summary>
public sealed class LogisticsPlannerTests
{
    private static (WorldState World, Colony Owner, Settlement Camp) WithCamp()
    {
        var world = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony owner = world.Colonies[0];
        owner.Stock.Add(ResourceType.Grain, 800); owner.Stock.Add(ResourceType.Bread, 800); owner.Stock.Add(ResourceType.Wood, 400); owner.Stock.Add(ResourceType.Stone, 200); owner.Stock.Add(ResourceType.Tools, 12);
        foreach (Colonist c in owner.Members) c.Sector = WorkSector.Free;
        int target = world.WorldMap.Grid.Neighbors(owner.PrimarySettlement.RegionTileIndex)
            .First(t => world.WorldMap.Grid[t].Habitable && world.WorldMap.TravelRoute(owner.PrimarySettlement.RegionTileIndex, t, new HashSet<int>()) is not null);
        Assert.NotNull(TerritorialTravel.Depart(world, owner.PrimarySettlement, target, TerritorialPurpose.Foundation,
            new Dictionary<ResourceType, int> { [ResourceType.Wood] = 24, [ResourceType.Grain] = 24 }, 4));
        Finish(world);
        return (world, owner, Assert.Single(owner.Settlements, s => s != owner.PrimarySettlement));
    }

    private static void Finish(WorldState world)
    {
        for (int hour = 0; hour < 24 * 30 && world.Caravans.Count > 0; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.Empty(world.Caravans);
    }

    private static void Empty(Settlement place, params ResourceType[] goods)
    {
        foreach (ResourceType good in goods) place.Stock.TryTake(good, place.Stock.Get(good), ResourceFlow.Loss);
    }

    [Fact]
    public void Les_besoins_sont_classes_la_survie_d_abord_avec_leur_echeance()
    {
        var (world, owner, camp) = WithCamp();
        Empty(camp, ResourceType.Grain, ResourceType.Food, ResourceType.Wood, ResourceType.Tools, ResourceType.Bread);
        List<SupplyNeed> needs = LogisticsPlanner.Needs(world, owner, camp);
        Assert.NotEmpty(needs);
        Assert.Equal(SupplyPriority.Survival, needs[0].Priority);
        Assert.Equal(needs.OrderBy(n => n.Priority).ThenBy(n => n.DueTicks).Select(n => n.Reason), needs.Select(n => n.Reason));
        Assert.Contains(needs, n => n.Good is null && n.Nutrition > 0);
        Assert.Contains(needs, n => n.Good == ResourceType.Wood && n.Priority == SupplyPriority.Survival);
        Assert.Contains(needs, n => n.Good == ResourceType.Tools && n.Priority == SupplyPriority.Tools);
        Assert.True(needs.Where(n => n.Priority == SupplyPriority.Survival).All(n => n.DueTicks <= world.Clock.Ticks + 2L * TimeConstants.TicksPerDay));
    }

    [Fact]
    public void Le_chargement_respecte_la_charge_les_reserves_du_village_et_varie_les_vivres()
    {
        var (world, owner, camp) = WithCamp();
        Empty(camp, ResourceType.Grain, ResourceType.Food, ResourceType.Wood, ResourceType.Tools);
        Settlement main = owner.PrimarySettlement;
        Empty(main, ResourceType.Bread);
        main.Stock.Add(ResourceType.Bread, 4);
        main.Stock.Add(ResourceType.SaltedMeat, 6);
        List<SupplyNeed> needs = LogisticsPlanner.Needs(world, owner, camp);
        int people = 2;
        var cargo = LogisticsPlanner.Cargo(world, owner, main, needs, people, routeDays: 2)!;
        Assert.NotNull(cargo);
        Assert.True(ResourceCatalog.WeightOf(cargo) <= 40 * people, "La charge ne dépasse jamais la capacité.");
        Assert.True(cargo.Keys.Count(k => ResourceCatalog.Nutrition(k) > 0) >= 2, "Les vivres sont variés quand le village en a plusieurs sortes.");
        foreach ((ResourceType good, int units) in cargo) Assert.True(units <= main.Stock.Available(good));
        // Le village garde au moins ses jours de réserve.
        decimal after = main.Stock.AvailableNutrition - cargo.Sum(p => p.Value * ResourceCatalog.Nutrition(p.Key));
        Assert.True(after >= main.Population.Count * (decimal)Trade.TravelerNutritionPerDay * LogisticsPlanner.SourceReserveDays);
    }

    [Fact]
    public void Le_ravitaillement_part_quand_le_village_a_du_surplus_et_apporte_le_plus_urgent()
    {
        var (world, owner, camp) = WithCamp();
        Empty(camp, ResourceType.Grain, ResourceType.Food, ResourceType.Bread);
        int nutritionBefore = (int)owner.PrimarySettlement.Stock.AvailableNutrition;
        Assert.True(LogisticsPlanner.PlanSupply(world, owner, owner.PrimarySettlement));
        Caravan trip = Assert.Single(world.Caravans);
        Assert.Equal(TerritorialPurpose.Supply, trip.Purpose);
        Assert.Equal(camp.Id, trip.ToSettlementId);
        Finish(world);
        Assert.True(camp.Stock.AvailableNutrition > 0, "La livraison est arrivée au camp.");
        Assert.True(owner.PrimarySettlement.Stock.AvailableNutrition < nutritionBefore);
    }

    [Fact]
    public void Un_village_sans_surplus_n_envoie_rien()
    {
        var (world, owner, camp) = WithCamp();
        Empty(camp, ResourceType.Grain, ResourceType.Food, ResourceType.Bread, ResourceType.Wood, ResourceType.Tools);
        Empty(owner.PrimarySettlement, ResourceType.Grain, ResourceType.Food, ResourceType.Bread, ResourceType.Wood, ResourceType.Tools, ResourceType.SaltedMeat);
        Assert.False(LogisticsPlanner.PlanSupply(world, owner, owner.PrimarySettlement));
        Assert.Empty(world.Caravans);
    }

    [Fact]
    public void Une_rupture_qui_dure_fait_evacuer_le_camp_sans_ravitaillement()
    {
        var (world, owner, camp) = WithCamp();
        Empty(camp, ResourceType.Grain, ResourceType.Food, ResourceType.Bread);
        camp.Stock.Add(ResourceType.Stone, 5);
        for (int day = 0; day < LogisticsPlanner.ProlongedShortageDays - 1; day++)
        {
            LogisticsPlanner.WatchShortage(world, camp);
            Assert.Empty(world.Caravans);
        }
        LogisticsPlanner.WatchShortage(world, camp);
        Caravan trip = Assert.Single(world.Caravans);
        Assert.Equal(TerritorialPurpose.Evacuation, trip.Purpose);
        int citizens = owner.Citizens.Count();
        Finish(world);
        Assert.Empty(camp.Population);
        Assert.Equal(citizens, owner.Citizens.Count());
        Assert.Equal(SettlementStatus.Closed, camp.Status);
    }
}
