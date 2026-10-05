using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Expansion économique : camp, exploitation locale ou importation comparés ; familles relocalisées ; stades ; fin de gisement signalée.</summary>
public sealed class ExpansionTests
{
    private static (WorldState World, Colony Owner, int Target, Deposit Site) Prepare()
    {
        var world = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony owner = world.Colonies[0];
        owner.Stock.Add(ResourceType.Grain, 500); owner.Stock.Add(ResourceType.Wood, 100);
        foreach (Colonist c in owner.Members) c.Sector = WorkSector.Free;
        int target = world.WorldMap.Grid.Neighbors(owner.PrimarySettlement.RegionTileIndex)
            .First(t => world.WorldMap.Grid[t].Habitable && world.WorldMap.TravelRoute(owner.PrimarySettlement.RegionTileIndex, t, new HashSet<int>()) is not null);
        RegionState region = world.VisitRegion(target);
        (int x, int y) = ColonyFounder.FindCampSite(region.Map);
        var site = new Deposit { Id = 910001, Region = target, Material = ResourceType.IronOre, X = x, Y = y, Mode = DepositMode.Finite,
            InitialReserve = 900, RemainingReserve = 900, DailyLimit = 16, Depth = 0 };
        region.Geology = region.Geology!.Where(d => d.Material != ResourceType.IronOre).Append(site).ToList();
        // La région du village n'a plus de fer connu : le besoin de minerai ne peut venir que d'ailleurs.
        foreach (Deposit local in world.VisitRegion(owner.PrimarySettlement.RegionTileIndex).Deposits.Where(d => d.Material == ResourceType.IronOre))
            local.RemainingReserve = 0;
        owner.DepositReports.RemoveAll(k => k.Material == ResourceType.IronOre);
        Prospection.Survey(world, owner, region, 1, "Test");
        owner.Stock.Add(ResourceType.IronOre, 0);
        owner.IronSeen = true;
        return (world, owner, target, site);
    }

    private static void Finish(WorldState world)
    {
        for (int hour = 0; hour < 24 * 30 && world.Caravans.Count > 0; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.Empty(world.Caravans);
    }

    [Fact]
    public void Sans_autre_solution_connue_un_camp_au_gisement_reconnu_est_choisi()
    {
        var (world, owner, _, site) = Prepare();
        Assert.True(ExpansionPlanner.DailyDemand(owner, ResourceType.IronOre) > 0);
        DepositKnowledge? choice = ExpansionPlanner.BestCampSite(world, owner, owner.PrimarySettlement);
        Assert.NotNull(choice);
        Assert.Equal(site.Id, choice!.SiteId);
        ExpansionOption camp = ExpansionPlanner.CampOption(world, owner, owner.PrimarySettlement, choice, ExpansionPlanner.DailyDemand(owner, ResourceType.IronOre))!;
        Assert.True(camp.UnitCostHours > Economy.Cost(owner, ResourceType.IronOre), "Le camp coûte plus cher par unité que le travail d'extraction seul (installation, transport, entretien).");
    }

    [Fact]
    public void Un_fournisseur_connu_et_bon_marche_rend_le_camp_inutile()
    {
        // Un voisin du même monde propose du minerai à bas prix.
        var world2 = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false, colonyCount: 2);
        Colony owner2 = world2.Colonies[0], supplier = world2.Colonies[1];
        owner2.DepositReports.RemoveAll(k => k.Material == ResourceType.IronOre);
        foreach (Deposit local in world2.VisitRegion(owner2.PrimarySettlement.RegionTileIndex).Deposits.Where(d => d.Material == ResourceType.IronOre))
            local.RemainingReserve = 0;
        owner2.IronSeen = true;
        int target = world2.WorldMap.Grid.Neighbors(owner2.PrimarySettlement.RegionTileIndex)
            .First(t => world2.WorldMap.Grid[t].Habitable && world2.Regions.GetValueOrDefault(t)?.OwnerColonyId is null && world2.WorldMap.TravelRoute(owner2.PrimarySettlement.RegionTileIndex, t, new HashSet<int>()) is not null);
        RegionState region = world2.VisitRegion(target);
        (int x, int y) = ColonyFounder.FindCampSite(region.Map);
        region.Geology = region.Geology!.Where(d => d.Material != ResourceType.IronOre)
            .Append(new Deposit { Id = 910002, Region = target, Material = ResourceType.IronOre, X = x, Y = y, Mode = DepositMode.Finite, InitialReserve = 900, RemainingReserve = 900, DailyLimit = 16 }).ToList();
        Prospection.Survey(world2, owner2, region, 1, "Test");
        Assert.NotNull(ExpansionPlanner.BestCampSite(world2, owner2, owner2.PrimarySettlement));
        var memory = new SupplierMemory(owner2, supplier) { ObservedTicks = world2.Clock.Ticks };
        memory.Offers.Add(new MarketOffer(ResourceType.IronOre, 200, 0, SellPrice: 0.5, BuyPrice: 0.1));
        world2.SupplierMemories.Add(memory);
        Assert.Null(ExpansionPlanner.BestCampSite(world2, owner2, owner2.PrimarySettlement));
    }

    [Fact]
    public void Un_gisement_local_suffisant_evite_le_camp()
    {
        var (world, owner, _, _) = Prepare();
        Settlement home = owner.PrimarySettlement;
        RegionState region = world.VisitRegion(home.RegionTileIndex);
        (int x, int y) = ColonyFounder.FindCampSite(region.Map);
        var local = new Deposit { Id = 910003, Region = home.RegionTileIndex, Material = ResourceType.IronOre, X = x, Y = y, Mode = DepositMode.Finite,
            InitialReserve = 900, RemainingReserve = 900, DailyLimit = 16 };
        region.Geology = region.Geology!.Append(local).ToList();
        Prospection.Survey(world, owner, region, 1, "Test");
        Assert.Null(ExpansionPlanner.BestCampSite(world, owner, home));
        ExpansionOption option = ExpansionPlanner.LocalOption(world, owner, home, ResourceType.IronOre, ExpansionPlanner.DailyDemand(owner, ResourceType.IronOre))!;
        Assert.Equal(ExpansionKind.Local, option.Kind);
    }

    [Fact]
    public void Un_gisement_presque_epuise_est_signale_une_seule_fois()
    {
        var (world, owner, _, _) = Prepare();
        Settlement home = owner.PrimarySettlement;
        var known = new DepositKnowledge { SiteId = 910004, Region = home.RegionTileIndex, Material = ResourceType.CopperOre, State = DepositObservation.Working,
            EstimateMin = 10, EstimateMax = 30, Confidence = 0.9f };
        owner.DepositReports.Add(known);
        int before = owner.Thoughts.Count(t => t.Text.Contains("touche à sa fin"));
        ExpansionPlanner.WatchExhaustion(world, home);
        ExpansionPlanner.WatchExhaustion(world, home);
        Assert.Equal(before + 1, owner.Thoughts.Count(t => t.Text.Contains("touche à sa fin")));
        Assert.True(known.Alerted);
    }

    [Fact]
    public void Le_stade_suit_les_logements_les_services_et_la_nourriture()
    {
        var (_, owner, _, _) = Prepare();
        Settlement home = owner.PrimarySettlement;
        foreach (Colonist c in home.Population) c.Sector = WorkSector.Free;
        // Vingt habitants nourris et logés mais sans aucun service : un camp, quelle que soit leur nombre.
        owner.Stock.Add(ResourceType.Grain, 2000);
        while (owner.Homeless > 0)
        {
            (int hx, int hy) = Urbanism.FindHutSite(home.Map, owner)!.Value;
            Urbanism.PlanHut(home.Map, owner, hx, hy).Progress = 1f;
            owner.FillVacancies();
        }
        foreach (BuildingType service in new[] { BuildingType.Well, BuildingType.Storehouse, BuildingType.Infirmary, BuildingType.Tavern, BuildingType.School, BuildingType.Market, BuildingType.Pen })
            foreach (Building built in owner.Buildings.Where(b => b.Type == service).ToList()) owner.Buildings.Remove(built);
        Assert.Equal(SettlementKind.Camp, ExpansionPlanner.StageOf(home));
        Urbanism.BuildInstantly(home.Map, owner, BuildingType.Well);
        Assert.Equal(SettlementKind.Hamlet, ExpansionPlanner.StageOf(home)); // moins de vingt-quatre habitants : pas encore un village
        // Sans nourriture suffisante, il redevient un camp.
        owner.Stock.TryTake(ResourceType.Grain, owner.Stock.Get(ResourceType.Grain), ResourceFlow.Loss);
        owner.Stock.TryTake(ResourceType.Food, owner.Stock.Get(ResourceType.Food), ResourceFlow.Loss);
        Assert.Equal(SettlementKind.Camp, ExpansionPlanner.StageOf(home));
    }

    [Fact]
    public void Une_famille_entiere_est_relocalisee_avec_son_trajet_et_ses_lits()
    {
        var (world, owner, target, _) = Prepare();
        Assert.NotNull(TerritorialTravel.Depart(world, owner.PrimarySettlement, target, TerritorialPurpose.Foundation,
            new Dictionary<ResourceType, int> { [ResourceType.Wood] = 24, [ResourceType.Grain] = 24 }, 4));
        Finish(world);
        Settlement camp = Assert.Single(owner.Settlements, s => s != owner.PrimarySettlement);
        camp.Stock.Add(ResourceType.Grain, 200);
        using (owner.UseSettlement(camp)) { Urbanism.BuildInstantly(camp.Map, owner, BuildingType.Hut); Urbanism.BuildInstantly(camp.Map, owner, BuildingType.Hut); }
        Settlement home = owner.PrimarySettlement;
        while (owner.Homeless > 0)
        {
            (int hx, int hy) = Urbanism.FindHutSite(home.Map, owner)!.Value;
            Urbanism.PlanHut(home.Map, owner, hx, hy).Progress = 1f;
            owner.FillVacancies();
        }
        var adults = home.Population.Where(c => c.Stage == LifeStage.Adult).OrderBy(c => c.Id).ToList();
        Colonist mother = adults[0], father = adults[1], child = adults[2];
        mother.Partner = father; father.Partner = mother;
        typeof(Colonist).GetProperty(nameof(Colonist.Mother))!.SetValue(child, mother);
        typeof(Colonist).GetProperty(nameof(Colonist.Father))!.SetValue(child, father);
        typeof(Colonist).GetProperty(nameof(Colonist.BirthTicks))!.SetValue(child, world.Clock.Ticks - 1L * TimeConstants.TicksPerYear);
        foreach (Colonist c in home.Population.Where(c => c.Stage == LifeStage.Adult && c != mother && c != father)) c.Sector = WorkSector.Food;
        int citizens = owner.Citizens.Count();
        Assert.Equal(LifeStage.Child, child.Stage);
        Assert.True(ExpansionPlanner.TryRelocateFamily(world, owner, home, minSource: 10));
        Assert.Equal(citizens, owner.Citizens.Count()); // la citoyenneté suit la famille en marche
        Assert.DoesNotContain(mother, home.Population); Assert.DoesNotContain(child, home.Population);
        Finish(world);
        foreach (Colonist member in new[] { mother, father, child })
        {
            Assert.Equal(camp.Id, member.HomeSettlementId);
            Assert.Contains(member, camp.Population);
        }
        Assert.Equal(citizens, owner.Citizens.Count());
    }
}
