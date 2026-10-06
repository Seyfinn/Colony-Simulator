using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;
using Xunit;

namespace GodColony.Simulation.Tests;

/// <summary>Missions internes groupées : attente bornée sans réservation, urgences jamais retardées, besoins en route jamais comptés deux fois.</summary>
public sealed class SupplyBatchingTests
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
        for (int hour = 0; hour < 24 * 30 && world.Caravans.Count > 0; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.Empty(world.Caravans);
        return (world, owner, Assert.Single(owner.Settlements, s => s != owner.PrimarySettlement));
    }

    private static void Empty(Settlement place, params ResourceType[] goods)
    {
        foreach (ResourceType good in goods) place.Stock.TryTake(good, place.Stock.Get(good), ResourceFlow.Loss);
    }

    [Fact]
    public void Une_charge_a_moitie_pleine_attend_au_plus_un_jour_sans_rien_reserver()
    {
        var (world, owner, camp) = WithCamp();
        Settlement main = owner.PrimarySettlement;
        long now = world.Clock.Ticks;
        int bread = main.Stock.Get(ResourceType.Bread);

        Assert.False(SupplyBatching.ShouldDepart(main, camp, now, urgent: false, weight: 30, usefulCapacity: 100, minWorthwhile: 20, out string cause));
        Assert.Contains("remplie", cause);
        SupplyWaitState wait = Assert.Single(main.SupplyWaits);
        Assert.Equal((main.Id, camp.Id, now), (wait.SourceId, wait.DestinationId, wait.FirstTicks));
        Assert.Equal(bread, main.Stock.Get(ResourceType.Bread)); // l'attente ne prélève rien et ne promet rien
        Assert.Empty(main.Stock.Reservations);

        Assert.False(SupplyBatching.ShouldDepart(main, camp, now + TimeConstants.TicksPerDay / 2, false, 30, 100, 20, out _));
        Assert.Equal(now, Assert.Single(main.SupplyWaits).FirstTicks); // la première demande reste la date de référence
        Assert.True(SupplyBatching.ShouldDepart(main, camp, now + TimeConstants.TicksPerDay, false, 30, 100, 20, out _)); // un jour au plus
        SupplyBatching.Clear(main, camp);
        Assert.Empty(main.SupplyWaits);
    }

    [Fact]
    public void Soixante_dix_pour_cent_de_charge_ou_une_urgence_partent_sans_attendre_et_une_charge_trop_legere_ne_part_jamais()
    {
        var (world, owner, camp) = WithCamp();
        Settlement main = owner.PrimarySettlement;
        long now = world.Clock.Ticks;
        Assert.True(SupplyBatching.ShouldDepart(main, camp, now, false, weight: 70, usefulCapacity: 100, minWorthwhile: 20, out _));
        Assert.True(SupplyBatching.ShouldDepart(main, camp, now, urgent: true, weight: 1, usefulCapacity: 100, minWorthwhile: 20, out _));
        Assert.Empty(main.SupplyWaits);
        Assert.False(SupplyBatching.ShouldDepart(main, camp, now, false, weight: 10, usefulCapacity: 100, minWorthwhile: 20, out string cause));
        Assert.False(SupplyBatching.ShouldDepart(main, camp, now + 3L * TimeConstants.TicksPerDay, false, 10, 100, 20, out cause));
        Assert.Contains("légère", cause);
    }

    [Fact]
    public void Un_ravitaillement_de_survie_part_tout_de_suite_meme_peu_rempli()
    {
        var (world, owner, camp) = WithCamp();
        Empty(camp, ResourceType.Grain, ResourceType.Food, ResourceType.Bread);
        Assert.True(LogisticsPlanner.PlanSupply(world, owner, owner.PrimarySettlement));
        Assert.Single(world.Caravans);
        Assert.Empty(owner.PrimarySettlement.SupplyWaits);
    }

    [Fact]
    public void Ce_qui_roule_deja_vers_un_camp_n_est_pas_compte_deux_fois()
    {
        var (world, owner, camp) = WithCamp();
        Empty(camp, ResourceType.Grain, ResourceType.Food, ResourceType.Bread);
        SupplyNeed before = LogisticsPlanner.Needs(world, owner, camp).First(n => n.Good is null);
        Assert.True(LogisticsPlanner.PlanSupply(world, owner, owner.PrimarySettlement));
        Caravan trip = Assert.Single(world.Caravans);
        decimal carried = trip.Inventory.Amounts.Sum(p => p.Value * ResourceCatalog.Nutrition(p.Key));
        Assert.True(carried > 0);

        List<SupplyNeed> after = LogisticsPlanner.Needs(world, owner, camp);
        decimal remaining = after.FirstOrDefault(n => n.Good is null)?.Nutrition ?? 0;
        Assert.Equal(before.Nutrition, remaining + Math.Min(carried, before.Nutrition)); // la cargaison en route est retranchée du besoin
        // Un second chargement ne reprend jamais ce que le premier couvre déjà : il n'apporte au plus que le reste.
        if (LogisticsPlanner.PlanSupply(world, owner, owner.PrimarySettlement) && world.Caravans.Count == 2)
        {
            decimal second = world.Caravans[1].Inventory.Amounts.Sum(p => p.Value * ResourceCatalog.Nutrition(p.Key));
            Assert.True(second <= remaining + 1m);
        }
    }

    [Fact]
    public void Les_attentes_dont_le_besoin_a_disparu_s_effacent_et_survivent_a_la_sauvegarde_tant_qu_il_existe()
    {
        var (world, owner, camp) = WithCamp();
        Settlement main = owner.PrimarySettlement;
        main.SupplyWaits.Add(new SupplyWaitState { SourceId = main.Id, DestinationId = camp.Id, FirstTicks = world.Clock.Ticks, LastCause = "charge remplie à 40 %" });
        string path = Path.Combine(Path.GetTempPath(), $"GodColony-attente-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            SupplyWaitState restored = Assert.Single(loaded.Colonies[0].PrimarySettlement.SupplyWaits);
            Assert.Equal("charge remplie à 40 %", restored.LastCause);
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
        SupplyBatching.Prune(main, []);
        Assert.Empty(main.SupplyWaits);
    }

    [Fact]
    public void Les_charrettes_suivent_le_besoin_simultane_observe()
    {
        var world = new WorldState(12345, startingColonists: 16, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Colonist porter = colony.Members[0];
        porter.X = porter.PrevX = colony.CampX + 28.5f;
        porter.Y = porter.PrevY = colony.CampY + 0.5f;
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        Assert.True(Carts.DistanceToDepot(colony, porter.TileX, porter.TileY) > Carts.MinDistance);

        // Aucune charrette libre : un porteur qui en aurait pris une compte comme un refus.
        Assert.False(Carts.ShouldTake(colony, porter));
        Assert.Equal(1, ledger.Today.CartDenied);
        colony.Stock.Add(ResourceType.Carts, 1, ResourceFlow.Transfer);
        Assert.True(Carts.ShouldTake(colony, porter));
        Carts.Take(colony, porter);
        Assert.True(porter.UsingCart);
        Assert.Equal(1, ledger.Today.CartPeak);
    }
}
