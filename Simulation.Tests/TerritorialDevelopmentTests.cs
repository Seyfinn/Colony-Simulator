using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class TerritorialDevelopmentTests
{
    private static (WorldState World, Colony Owner, int Target) Prepare()
    {
        var world = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony owner = world.Colonies[0];
        owner.Stock.Add(ResourceType.Grain, 500); owner.Stock.Add(ResourceType.Wood, 100); owner.Stock.Add(ResourceType.Tools, 10);
        foreach (Colonist c in owner.Members) c.Sector = WorkSector.Free;
        int target = world.WorldMap.Grid.Neighbors(owner.PrimarySettlement.RegionTileIndex)
            .First(t => world.WorldMap.Grid[t].Habitable && world.WorldMap.TravelRoute(owner.PrimarySettlement.RegionTileIndex, t, new HashSet<int>()) is not null);
        return (world, owner, target);
    }

    private static void Finish(WorldState world)
    {
        for (int hour = 0; hour < 24 * 20 && world.Caravans.Count > 0; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.Empty(world.Caravans);
    }

    [Fact]
    public void La_fondation_transfere_les_citoyens_et_les_biens_sans_dotation()
    {
        var (world, owner, target) = Prepare();
        int wood = owner.Stock.Get(ResourceType.Wood), people = owner.Citizens.Count();
        Caravan trip = Assert.IsType<Caravan>(TerritorialTravel.Depart(world, owner.PrimarySettlement, target,
            TerritorialPurpose.Foundation, new Dictionary<ResourceType,int> { [ResourceType.Wood] = 24, [ResourceType.Grain] = 24 }, 4));
        Assert.Equal(people, owner.Citizens.Count()); Assert.Equal(people - 4, owner.PresentMembers.Count);
        Assert.Equal(24, trip.Inventory.Get(ResourceType.Wood));
        Finish(world);
        Settlement camp = Assert.Single(owner.Settlements, s => s != owner.PrimarySettlement);
        Assert.Equal(4, camp.Population.Count); Assert.Equal(0, camp.Stock.Get(ResourceType.Coins));
        Assert.Empty(camp.Buildings); Assert.Equal(people, owner.Citizens.Count());
        Assert.Equal(wood, owner.Settlements.Sum(s => s.Stock.Get(ResourceType.Wood)));
        Assert.Same(camp.Map, world.GenerateColonyMap(target));
        using (owner.UseSettlement(camp)) Assert.Same(camp.Stock, owner.Stock);
        Assert.Same(owner.PrimarySettlement.Stock, owner.Stock);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".gcsave");
        try
        {
            WorldSave.Save(path,world,new SavedView(SettlementId:camp.Id)); var saved = WorldSave.Load(path);
            Assert.Equal(camp.Id,saved.Info.View.SettlementId); var loaded = saved.World;
            for (int i=0;i<100;i++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world,loaded));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Le_renseignement_arrive_avec_les_prospecteurs_et_le_debit_est_partage()
    {
        var (world, owner, target) = Prepare();
        var trip = Assert.IsType<Caravan>(TerritorialTravel.Depart(world, owner.PrimarySettlement,target,TerritorialPurpose.Prospection,new Dictionary<ResourceType,int>()));
        for (int hour=0;hour<100 && trip.State==CaravanState.Outbound;hour++)
        { for(int tick=0;tick<TimeConstants.TicksPerHour;tick++) world.Clock.Advance(); Trade.Hourly(world); }
        // Arrivés, les prospecteurs sondent pendant plusieurs jours : rien n'est rapporté avant leur retour.
        Assert.DoesNotContain(target, owner.VisitedRegions);
        Finish(world); Assert.Contains(target, owner.VisitedRegions);
        Assert.True(trip.WorkedTicks >= Prospection.StayDays * (long)TimeConstants.TicksPerDay - TimeConstants.TicksPerHour);
        var deposit = world.Regions[target].Deposits.First(d=>d.Mode==DepositMode.Finite && owner.DepositReports.Any(k=>k.SiteId==d.Id));
        int initial = deposit.RemainingReserve;
        int a=DepositExtraction.Extract(world,owner,deposit,1000), b=DepositExtraction.Extract(world,owner,deposit,1000);
        Assert.Equal(deposit.DailyLimit,a); Assert.Equal(0,b); Assert.Equal(initial-a,deposit.RemainingReserve);
        for(int day=0;day<1000 && deposit.RemainingReserve>0;day++)
        {
            for(int tick=0;tick<TimeConstants.TicksPerDay;tick++) world.Clock.Advance();
            DepositExtraction.Extract(world,owner,deposit,1000);
        }
        Assert.Equal(0,deposit.RemainingReserve);
        Assert.Equal(0,DepositExtraction.Extract(world,owner,deposit,1000));
        Assert.Equal(DepositObservation.Depleted,owner.DepositReports.Single(k=>k.SiteId==deposit.Id).State);
    }

    [Fact]
    public void Les_intrants_de_cuivre_et_le_combustible_reel_sont_remboursables()
    {
        var (world, owner, _) = Prepare();
        owner.Known[Discovery.Metallurgy] = world.Clock.Ticks;
        owner.Stock.Add(ResourceType.Coins,100);
        Assert.False(ToolChain.Demand(owner).Active);
        Urbanism.BuildInstantly(owner.Map,owner,BuildingType.Kiln);
        var fuel = ExtendedIndustry.PickJob(owner);
        Assert.NotNull(fuel); Assert.Equal(ResourceType.Charcoal,fuel.Value.Recipe.Output);
        Recipe recipe = ExtendedIndustry.Recipes.First(r=>r.Output==ResourceType.Copper);
        owner.Stock.Add(ResourceType.CopperOre,3); owner.Stock.Add(ResourceType.MineralCoal,2);
        Assert.True(ToolChain.TryTakeInputs(owner,recipe,out _,out var held));
        Assert.Equal(2, held.Get(ResourceType.MineralCoal)); Assert.Equal(0, held.Get(ResourceType.Charcoal));
        foreach(var item in held.Amounts.ToArray()) Assert.True(held.TryTransferTo(owner.Stock,item.Key,item.Value));
        Assert.Equal(3,owner.Stock.Get(ResourceType.CopperOre)); Assert.Equal(2,owner.Stock.Get(ResourceType.MineralCoal));
    }

    [Fact]
    public void Le_ravitaillement_repris_livre_au_camp_et_rapporte_son_surplus()
    {
        var (world, owner, target) = Prepare();
        Assert.NotNull(TerritorialTravel.Depart(world,owner.PrimarySettlement,target,TerritorialPurpose.Foundation,
            new Dictionary<ResourceType,int> { [ResourceType.Wood]=24, [ResourceType.Grain]=24 },4));
        Finish(world);
        Settlement camp = owner.Settlements.Last();
        camp.Stock.Add(ResourceType.IronOre,100);
        int wood = owner.Settlements.Sum(s=>s.Stock.Get(ResourceType.Wood));
        int ore = owner.Settlements.Sum(s=>s.Stock.Get(ResourceType.IronOre));
        Caravan trip = Assert.IsType<Caravan>(TerritorialTravel.Depart(world,owner.PrimarySettlement,target,TerritorialPurpose.Supply,
            new Dictionary<ResourceType,int> { [ResourceType.Wood]=10 },destination:camp));
        Assert.Equal(24,camp.Stock.Get(ResourceType.Wood));
        string path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".gcsave");
        try
        {
            WorldSave.Save(path,world); WorldState loaded = WorldSave.Load(path).World;
            Finish(world); Finish(loaded);
            Assert.Equal(34,camp.Stock.Get(ResourceType.Wood));
            Assert.Equal(wood,owner.Settlements.Sum(s=>s.Stock.Get(ResourceType.Wood)));
            Assert.Equal(ore,owner.Settlements.Sum(s=>s.Stock.Get(ResourceType.IronOre)));
            Assert.True(owner.Stock.Get(ResourceType.IronOre)>0);
            Assert.Equal(20,owner.PrimarySettlement.Population.Count+camp.Population.Count);
            Assert.Null(new WorldComparison().Difference(world,loaded));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Le_format_v6_reste_chargeable()
    {
        var world=WorldSave.Load(Path.Combine(AppContext.BaseDirectory,"Fixtures","territoires-v6.gcsave")).World;
        Assert.Equal(20,world.Colonies.Sum(c=>c.Citizens.Count()));
        Assert.All(world.Settlements,s=>Assert.NotEmpty(world.Regions[s.RegionTileIndex].Deposits));
        world.Step();
    }
}
