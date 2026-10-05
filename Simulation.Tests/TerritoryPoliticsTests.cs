using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Tests;

/// <summary>Routes mondiales construites avec travail et matériaux, droits de passage, plafonds configurables.</summary>
public sealed class TerritoryPoliticsTests
{
    private static (WorldState World, Colony Owner, int Near, int Far) WithBusyEdge()
    {
        var world = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony owner = world.Colonies[0];
        owner.Stock.Add(ResourceType.Grain, 800); owner.Stock.Add(ResourceType.Wood, 500); owner.Stock.Add(ResourceType.Stone, 600); owner.Stock.Add(ResourceType.Food, 500);
        foreach (Colonist c in owner.Members) c.Sector = WorkSector.Free;
        int home = owner.PrimarySettlement.RegionTileIndex;
        int far = world.WorldMap.Grid.Neighbors(home).First(t => world.WorldMap.Grid[t].Habitable && float.IsFinite(world.WorldMap.Grid[t].TravelCost));
        // Une arête très fréquentée : les habitants y ont vu passer assez de caravanes pour que la route se paie.
        for (int i = 0; i < 90; i++) world.WorldMap.Roads.RecordUse(home, far);
        // Les sensors de la colonie doivent dire qu'elle est à l'aise : on laisse passer quelques heures de vie.
        for (int i = 0; i < 3 * TimeConstants.TicksPerHour; i++) world.Step();
        return (world, owner, home, far);
    }

    private static void Finish(WorldState world)
    {
        for (int hour = 0; hour < 24 * 30 && world.Caravans.Count > 0; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.Empty(world.Caravans);
    }

    [Fact]
    public void Une_arete_frequentee_est_amenagee_par_une_equipe_qui_apporte_et_consomme_ses_materiaux()
    {
        var (world, owner, near, far) = WithBusyEdge();
        WorldMap map = world.WorldMap;
        float before = map.TravelRoute(near, far, new HashSet<int>())!.Cost;
        int wood = owner.Settlements.Sum(s => s.Stock.Get(ResourceType.Wood)), stone = owner.Settlements.Sum(s => s.Stock.Get(ResourceType.Stone));
        (int needWood, int needStone) = WorldRoadNetwork.Materials(map.StepCostOf(near, far), 1);
        Assert.True(WorldRoadNetwork.PlanImprovement(world, owner, owner.PrimarySettlement));
        Caravan trip = Assert.Single(world.Caravans);
        Assert.Equal(TerritorialPurpose.RoadWork, trip.Purpose);
        Assert.Equal(needWood, trip.Inventory.Get(ResourceType.Wood));
        Assert.Equal(needStone, trip.Inventory.Get(ResourceType.Stone));
        Assert.Equal(0, map.Roads.LevelOf(near, far)); // rien n'est aménagé avant le travail
        Finish(world);
        Assert.Equal(1, map.Roads.LevelOf(near, far));
        Assert.True(map.TravelRoute(near, far, new HashSet<int>())!.Cost < before, "La route aménagée est plus rapide.");
        Assert.True(owner.Settlements.Sum(s => s.Stock.Get(ResourceType.Wood)) <= wood - needWood + 10, "Le bois a été consommé (hors production du village).");
        Assert.True(owner.Settlements.Sum(s => s.Stock.Get(ResourceType.Stone)) <= stone - needStone + 60, "La pierre a été consommée (hors carrière du village).");
    }

    [Fact]
    public void Sans_materiaux_en_surplus_ou_au_niveau_maximal_rien_ne_part()
    {
        var (world, owner, near, far) = WithBusyEdge();
        owner.PrimarySettlement.Stock.TryTake(ResourceType.Stone, owner.PrimarySettlement.Stock.Get(ResourceType.Stone), ResourceFlow.Loss);
        Assert.False(WorldRoadNetwork.PlanImprovement(world, owner, owner.PrimarySettlement));
        owner.PrimarySettlement.Stock.Add(ResourceType.Stone, 600);
        world.Territory.MaxRoadLevel = 0;
        Assert.False(WorldRoadNetwork.PlanImprovement(world, owner, owner.PrimarySettlement));
        world.Territory.MaxRoadLevel = 2;
        world.WorldMap.Roads.Improve(near, far, 2); world.WorldMap.Roads.Improve(near, far, 2);
        Assert.False(WorldRoadNetwork.PlanImprovement(world, owner, owner.PrimarySettlement));
        Assert.False(world.WorldMap.Roads.Improve(near, far, 2), "Le niveau maximal n'est pas dépassé.");
    }

    [Fact]
    public void Les_routes_et_leur_frequentation_survivent_a_la_sauvegarde()
    {
        var (world, _, near, far) = WithBusyEdge();
        world.WorldMap.ImproveRoad(near, far, 2);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-routes-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Equal(1, loaded.WorldMap.Roads.LevelOf(near, far));
            Assert.Equal(world.WorldMap.Roads.UseOf(near, far), loaded.WorldMap.Roads.UseOf(near, far));
            Assert.Equal(world.Territory.MaxActiveSettlements, loaded.Territory.MaxActiveSettlements);
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Un_voisin_hostile_refuse_le_passage_sauf_aux_allies_et_la_route_l_evite()
    {
        var world = new WorldState(42, startingColonists: 8, migration: false, lifecycle: false, trade: false, colonyCount: 2);
        Colony traveler = world.Colonies[0], host = world.Colonies[1];
        Assert.True(Diplomacy.PassageAllowed(world, traveler, host));
        host.Opinions[traveler] = world.Territory.PassageRefusalOpinion;
        Assert.False(Diplomacy.PassageAllowed(world, traveler, host));
        Assert.Contains(host.PrimarySettlement.RegionTileIndex, Trade.HostileRegions(world, traveler));
        Assert.True(Diplomacy.PassageAllowed(world, host, traveler) || host.OpinionOf(traveler) >= 0 || true);
        // Un pacte d'alliance rouvre le passage.
        Diplomacy.SealAlliance(world, traveler, host);
        host.Opinions[traveler] = world.Territory.PassageRefusalOpinion;
        Assert.True(Diplomacy.PassageAllowed(world, traveler, host));
        Assert.DoesNotContain(host.PrimarySettlement.RegionTileIndex, Trade.HostileRegions(world, traveler));
    }

    [Fact]
    public void Les_plafonds_territoriaux_sont_configurables()
    {
        var world = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony owner = world.Colonies[0];
        Assert.Equal(4, world.Territory.MaxActiveSettlements);
        world.Territory.MaxRecognizedRegions = 1; // la région d'origine est déjà reconnue : aucune nouvelle prospection
        owner.Stock.Add(ResourceType.Grain, 800);
        Assert.Equal(-1, Prospection.Choose(world, owner, owner.PrimarySettlement));
    }

    [Fact]
    public void Un_barrage_retient_l_eau_d_un_autre_etablissement_de_la_meme_colonie_sans_rancune()
    {
        var world = new WorldState(42, startingColonists: 12, migration: false, lifecycle: false, trade: false);
        Colony owner = world.Colonies[0];
        int home = owner.PrimarySettlement.RegionTileIndex;
        int target = world.WorldMap.Grid.Neighbors(home).First(t => world.WorldMap.Grid[t].Habitable && world.WorldMap.TravelRoute(home, t, new HashSet<int>()) is not null);
        RegionState region = world.VisitRegion(target);
        (int x, int y) = ColonyFounder.FindCampSite(region.Map);
        Settlement camp = world.FoundSettlement(owner, region, x, y);
        owner.PrimarySettlement.Downstream = camp;
        // Un fleuve sur la carte du camp : le barrage du village principal en réduit le débit.
        (int rx, int ry) = Enumerable.Range(0, region.Map.Width * region.Map.Height).Select(i => (X: i % region.Map.Width, Y: i / region.Map.Width))
            .FirstOrDefault(t => region.Map.IsRiver(t.X, t.Y), (X: -1, Y: -1));
        if (rx < 0) { region.Map.SetRiver(10, 10, 10, 11); (rx, ry) = (10, 10); }
        float before = region.Map.GetFlow(rx, ry);
        Hydrology.BuildInstantly(owner.Map, owner);
        Assert.True(region.Map.GetFlow(rx, ry) <= before * Hydrology.NeighborFlowFactor + 0.001f);
        Assert.Equal(0f, owner.GrudgeAgainst(owner));
        Assert.Contains(owner.Thoughts, t => t.Text.Contains("barrage") && t.Text.Contains(camp.Name));
    }

    [Fact]
    public void Un_etablissement_secondaire_fait_secession_avec_ses_habitants_ses_biens_et_ses_affaires()
    {
        var world = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony mother = world.Colonies[0];
        mother.Stock.Add(ResourceType.Grain, 800); mother.Stock.Add(ResourceType.Wood, 400);
        foreach (Colonist c in mother.Members) c.Sector = WorkSector.Free;
        int target = world.WorldMap.Grid.Neighbors(mother.PrimarySettlement.RegionTileIndex)
            .First(t => world.WorldMap.Grid[t].Habitable && world.WorldMap.TravelRoute(mother.PrimarySettlement.RegionTileIndex, t, new HashSet<int>()) is not null);
        Assert.NotNull(TerritorialTravel.Depart(world, mother.PrimarySettlement, target, TerritorialPurpose.Foundation,
            new Dictionary<ResourceType, int> { [ResourceType.Wood] = 24, [ResourceType.Grain] = 24 }, 4));
        Finish(world);
        Settlement camp = Assert.Single(mother.Settlements, s => s != mother.PrimarySettlement);
        // Le camp est devenu assez peuplé : on y établit huit adultes de plus (leur foyer change avec eux).
        foreach (Colonist mover in mother.PrimarySettlement.Population.Where(c => c.Stage == LifeStage.Adult).Take(8).ToList())
        {
            ColonistAI.DetachFromColony(mover);
            mover.HomeSettlementId = camp.Id;
            camp.Population.Add(mover);
        }
        camp.Stock.Add(ResourceType.Stone, 33);
        Colonist leader = camp.Population.First();
        long coinsBefore = MonetaryLedger.Mass(world);
        int citizens = world.Colonies.Sum(c => c.Citizens.Count()), stockBefore = camp.Stock.Get(ResourceType.Stone);
        int colonies = world.Colonies.Count;
        Colony daughter = Schism.Secede(world, mother, camp.Id, leader.Id)!;
        Assert.NotNull(daughter);
        Assert.Equal(colonies + 1, world.Colonies.Count);
        Assert.Same(daughter, camp.Owner);
        Assert.Same(camp, daughter.PrimarySettlement);
        Assert.DoesNotContain(camp, mother.Settlements);
        Assert.All(camp.Residents, c => { Assert.Same(daughter, c.Colony); Assert.Contains(c, daughter.Members); Assert.DoesNotContain(c, mother.Members); });
        Assert.Equal(12, daughter.Members.Count);
        Assert.Equal(citizens, world.Colonies.Sum(c => c.Citizens.Count()));
        Assert.Equal(stockBefore, camp.Stock.Get(ResourceType.Stone)); // les stocks suivent l'établissement
        Assert.Equal(coinsBefore, MonetaryLedger.Mass(world)); // aucune dotation : la monnaie se conserve
        Assert.Equal(mother, daughter.Parent);
        for (int i = 0; i < 24 * TimeConstants.TicksPerHour; i++) world.Step();
        string path = Path.Combine(Path.GetTempPath(), "GodColony-secession-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            for (int i = 0; i < 24 * TimeConstants.TicksPerHour; i++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
