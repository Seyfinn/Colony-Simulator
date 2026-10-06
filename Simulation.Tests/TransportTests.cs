using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Lot B : visibilité du transport, charrettes locales, gués et ponts, équipement des caravanes, interception sur la route.</summary>
public class TransportTests
{
    private static WorldState TwoPeoples(int colonists = 10, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false, colonyCount: 2, trade: false);

    private static void Set(Colony colony, ResourceType type, int amount)
    {
        colony.Stock.TryTake(type, colony.Stock.Get(type));
        colony.Stock.Add(type, amount);
    }

    /// <summary>Des capteurs sereins : la survie est assurée, les vivres abondent.</summary>
    private static ColonySensors Safe(Colony colony, WorldState world) =>
        ColonyBrain.Sense(colony, world.Clock) with { FoodDays = 20f, FoodPressure = 0f, HeatingPressure = 0f };

    private static TradePlan? Plan(WorldState world, Colony from, Colony to)
    {
        Trade.ObserveMarket(world, from, to);
        return Trade.Plan(world, from, to);
    }

    // ---------- B.1 Libellés ----------

    [Fact]
    public void Un_porteur_et_une_caravane_disent_leur_contenu_et_leur_destination()
    {
        WorldState world = TwoPeoples();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Colonist porter = from.PresentMembers.First();
        Assert.Equal("", TransportView.TransportLabel(porter));
        porter.Carrying = (ResourceType.Wood, 6);
        Assert.Equal("6 bois → camp", TransportView.TransportLabel(porter));
        porter.UsingCart = true;
        Assert.Equal("6 bois → camp (en charrette)", TransportView.TransportLabel(porter));

        // Un chantier : on le nomme.
        Building site = Urbanism.BuildInstantly(from.Map, from, BuildingType.Oven)!;
        porter.UsingCart = false;
        porter.CarryingTo = site;
        Assert.Equal($"6 bois → chantier : {Building.NameOf(BuildingType.Oven)}", TransportView.TransportLabel(porter));

        Set(from, ResourceType.Salt, 20);
        Set(to, ResourceType.Salt, 0);
        var caravan = new Caravan(from, to, [], [], 0, 0, 10, 20);
        caravan.Cargo[ResourceType.Salt] = 12;
        caravan.Coins = 30;
        Assert.Equal($"12 sel, 30 pièces → {to.Name}", TransportView.TransportLabel(caravan));
        caravan.State = CaravanState.Returning;
        caravan.Cargo[ResourceType.Salt] = 0;
        caravan.Cargo[ResourceType.Grain] = 8;
        Assert.Equal($"8 céréales, 30 pièces → {from.Name} (retour)", TransportView.TransportLabel(caravan));
        caravan.Gear = CaravanGear.IronCart;
        Assert.EndsWith("(charrette renforcée)", TransportView.TransportLabel(caravan));
        caravan.Cargo.Clear();
        Assert.StartsWith("à vide", TransportView.TransportLabel(caravan));
    }

    // ---------- B.2 Charrettes ----------

    [Fact]
    public void Une_charrette_se_fabrique_a_la_forge_et_se_prend_au_stock_quand_le_depot_est_loin()
    {
        WorldState world = TwoPeoples();
        Colony colony = world.Colonies[0];
        Assert.Contains(ExtendedIndustry.Recipes, r => r.Output == ResourceType.Carts && r.Workshop == BuildingType.Forge
            && r.Inputs.Any(i => i.Type == ResourceType.Wood && i.Amount == 10) && r.Inputs.Any(i => i.Type == ResourceType.Iron && i.Amount == 1));
        Colonist porter = colony.PresentMembers.First();
        porter.X = colony.CampX + 40.5f; porter.Y = colony.CampY + 0.5f;
        Assert.False(Carts.ShouldTake(colony, porter)); // pas de charrette au stock
        colony.Stock.Add(ResourceType.Carts, 1);
        int goods = colony.Stock.Get(ResourceType.Wood);
        Assert.True(Carts.ShouldTake(colony, porter));
        porter.UsingCart = true;
        Assert.Equal(0, Carts.Free(colony));
        Assert.Equal(1, Carts.InService(colony));
        Carts.Return(porter);
        Assert.Equal(1, Carts.Free(colony));
        Assert.Equal(goods, colony.Stock.Get(ResourceType.Wood)); // aucun bien n'est créé ni détruit

        // Près du dépôt, la charrette ne vaut pas la peine.
        porter.X = colony.CampX + 3.5f;
        Assert.False(Carts.ShouldTake(colony, porter));
        Assert.Equal(4, Carts.CapacityFactor);
    }

    [Fact]
    public void Un_colon_avec_une_charrette_enchaine_les_recoltes_et_rend_la_charrette_au_depot()
    {
        var world = new WorldState(777, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        colony.Stock.Add(ResourceType.Carts, 1);
        // Une partie entière : aucun colon ne perd ni ne crée de bois, et la charrette finit toujours rendue.
        for (int i = 0; i < 12 * TimeConstants.TicksPerDay; i++)
        {
            world.Step();
            Assert.True(Carts.Free(colony) >= 0);
        }
        Assert.Equal(1, colony.Stock.Get(ResourceType.Carts));
        Assert.True(colony.PresentMembers.Count(c => c.UsingCart) <= 1);
    }

    [Fact]
    public void La_colonie_veut_une_charrette_quand_le_depot_est_trop_loin_et_aucune_n_est_libre()
    {
        var world = new WorldState(777, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        // Sans champ, l'aller-retour est nul : aucune charrette demandée.
        colony.Fields.Clear();
        Assert.Equal(0, Carts.Wanted(colony));
        Assert.Equal(0, ExtendedIndustry.Target(colony, ResourceType.Carts));
    }

    // ---------- B.3 Gués et ponts ----------

    private static (WorldState World, Colony Colony, int X, int Y) RiverWorld()
    {
        for (int seed = 1; seed < 80; seed++)
        {
            var world = new WorldState(seed, startingColonists: 10, migration: false, lifecycle: false);
            Colony colony = world.Colonies[0];
            LocalMap map = colony.Map;
            for (int y = 2; y < map.Height - 2; y++)
            for (int x = 2; x < map.Width - 2; x++)
                if (map.IsRiver(x, y) && !map.IsWideRiver(x, y) && map.IsWalkable(x - 1, y) && map.IsWalkable(x + 1, y))
                    return (world, colony, x, y);
        }
        throw new InvalidOperationException("Aucune rivière trouvée.");
    }

    [Fact]
    public void Le_gue_ralentit_et_un_pont_acheve_supprime_le_ralentissement()
    {
        (WorldState world, Colony colony, int x, int y) = RiverWorld();
        LocalMap map = colony.Map;
        Assert.Equal(LocalMap.RiverMoveCost, map.MoveCost(x, y));
        Assert.True(LocalMap.WideRiverMoveCost > LocalMap.RiverMoveCost);
        float ford = GodColony.Simulation.Pathfinding.TraversalCost.StepSeconds(map, x - 1, y, x, y);
        int cell = y * map.Width + x;
        map.Roads.SetSurface(cell, RoadSurface.Bridge);
        Assert.Equal(1f, map.MoveCost(x, y));
        float bridge = GodColony.Simulation.Pathfinding.TraversalCost.StepSeconds(map, x - 1, y, x, y);
        Assert.True(bridge < ford / 2f, $"{bridge} contre {ford}");
    }

    [Fact]
    public void Un_passage_frequent_ouvre_un_chantier_de_pont_qui_consomme_ses_materiaux_puis_devient_un_pont()
    {
        (WorldState world, Colony colony, int x, int y) = RiverWorld();
        LocalMap map = colony.Map;
        Settlement place = colony.PrimarySettlement;
        int cell = y * map.Width + x;
        colony.Stock.Add(ResourceType.Wood, 200);
        colony.Stock.Add(ResourceType.Stone, 100);
        colony.Sensors = Safe(colony, world);
        // Beaucoup de passages sur la rivière.
        for (int pass = 0; pass < 8; pass++)
            Bridges.OnRiverStep(colony, map, x, y);
        int wood = colony.Stock.Get(ResourceType.Wood), stone = colony.Stock.Get(ResourceType.Stone);
        Bridges.OnDayStart(colony);
        BridgeSite site = Assert.Single(place.BridgeSites);
        Assert.Contains(cell, site.Cells);
        Assert.Equal(wood - Bridges.WoodPerCell * site.Cells.Count, colony.Stock.Get(ResourceType.Wood));
        Assert.Equal(stone - Bridges.StonePerCell * site.Cells.Count, colony.Stock.Get(ResourceType.Stone));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("pont"));

        // Un seul chantier à la fois.
        for (int pass = 0; pass < 8; pass++)
            Bridges.OnRiverStep(colony, map, x, y);
        Bridges.OnDayStart(colony);
        Assert.Single(place.BridgeSites);

        // Les habitants le bâtissent case après case.
        foreach (int target in site.Cells.ToList())
            Bridges.CompleteCell(colony, target % map.Width, target / map.Width, site.Id);
        Assert.True(site.IsComplete);
        Assert.All(site.Cells, c => Assert.Equal(RoadSurface.Bridge, map.Roads.SurfaceAt(c)));
        Assert.Equal(1f, map.MoveCost(x, y));
        Assert.Null(Bridges.ActiveSite(colony));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("pont est achevé"));
    }

    [Fact]
    public void Sans_materiaux_en_surplus_ou_en_crise_aucun_pont_ne_s_ouvre()
    {
        (WorldState world, Colony colony, int x, int y) = RiverWorld();
        LocalMap map = colony.Map;
        colony.Stock.TryTake(ResourceType.Wood, colony.Stock.Get(ResourceType.Wood));
        for (int pass = 0; pass < 8; pass++)
            Bridges.OnRiverStep(colony, map, x, y);
        Bridges.OnDayStart(colony);
        Assert.Empty(colony.PrimarySettlement.BridgeSites);
    }

    [Fact]
    public void Un_grand_fleuve_majore_la_marche_de_l_arete_jusqu_au_niveau_du_pont()
    {
        var world = new WorldState(777, startingColonists: 6, migration: false, lifecycle: false);
        WorldMap map = world.WorldMap;
        int river = -1, bank = -1;
        foreach (WorldTileInfo info in Enumerate(map))
            if (info.Tile.River >= 2 && !info.Tile.IsOcean)
                foreach (int n in map.Grid.Neighbors(info.Tile.Index))
                    if (map.Grid[n].River == 0 && !map.Grid[n].IsOcean && float.IsFinite(map.Grid[n].TravelCost) && float.IsFinite(info.Tile.TravelCost))
                    { river = info.Tile.Index; bank = n; break; }
        Assert.True(river >= 0, "un grand fleuve doit exister");
        Assert.True(map.CrossesRiver(bank, river));
        float plain = 0.5f * (map.Grid[bank].TravelCost + map.Grid[river].TravelCost);
        Assert.Equal(plain * WorldMap.RiverCrossingFactor, map.StepCostOf(bank, river), 3);
        // Les matériaux du pont (niveau 2) suivent la peine du terrain : plus lourds pour une traversée.
        (int wood, int stone) = GodColony.Simulation.World.WorldRoadNetwork.Materials(map.StepCostOf(bank, river), 2);
        (int plainWood, int plainStone) = GodColony.Simulation.World.WorldRoadNetwork.Materials(plain, 2);
        Assert.True(wood >= plainWood && stone >= plainStone);
    }

    private readonly record struct WorldTileInfo(GodColony.Simulation.World.WorldTile Tile);

    private static IEnumerable<WorldTileInfo> Enumerate(WorldMap map) => map.Grid.Tiles.Select(t => new WorldTileInfo(t));

    // ---------- B.4 Équipement des caravanes ----------

    [Fact]
    public void La_capacite_et_la_vitesse_suivent_l_equipement()
    {
        Assert.Equal([1, 2, 3, 4], new[] { CaravanGear.Porters, CaravanGear.WoodCart, CaravanGear.IronCart, CaravanGear.Draft }.Select(Trade.GearCapacityFactor));
        Assert.Equal(0.9f, Trade.GearSpeedFactor(CaravanGear.WoodCart));
        Assert.Equal(1f, Trade.GearSpeedFactor(CaravanGear.IronCart));
        Assert.Equal(1.2f, Trade.GearSpeedFactor(CaravanGear.Draft, ResourceType.Oxen));
        Assert.Equal(1.5f, Trade.GearSpeedFactor(CaravanGear.Draft, ResourceType.Horses));

        WorldState world = TwoPeoples();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        int baseCapacity = Trade.CapacityOf(from, to, CaravanGear.Porters);
        Assert.Equal(3 * baseCapacity, Trade.CapacityOf(from, to, CaravanGear.IronCart));
        from.CaravanGear = CaravanGear.WoodCart;
        Assert.Equal(2 * baseCapacity, Trade.CapacityOf(from, to));
    }

    private static void GiveGoodTrips(Colony colony, int count, bool limited)
    {
        for (int i = 0; i < count; i++)
            colony.Trades.Add(new TradeRecord(i, "Voisine", [new TradeLine(ResourceType.Tools, 1, 10, true)], 10, WeSent: true)
            { GainHours = 500, CostHours = 20, CapacityLimited = limited });
    }

    [Fact]
    public void Monter_en_gamme_consomme_le_cout_et_exige_le_besoin_les_moyens_et_la_survie()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false, colonyCount: 2, trade: false);
        Colony colony = world.Colonies[0];
        colony.Sensors = Safe(colony, world);
        Settlement home = colony.PrimarySettlement;
        // Pas de besoin : rien ne change.
        Assert.False(Trade.ConsiderGearUpgrade(world, colony));
        GiveGoodTrips(colony, 3, limited: false);
        Assert.False(Trade.ConsiderGearUpgrade(world, colony));
        colony.Trades.Clear();
        GiveGoodTrips(colony, 3, limited: true);

        // Le besoin est là, mais pas les moyens.
        home.Stock.TryTake(ResourceType.Wood, home.Stock.Get(ResourceType.Wood));
        Assert.False(Trade.ConsiderGearUpgrade(world, colony));
        Assert.Equal(CaravanGear.Porters, colony.CaravanGear);

        // Charrette de bois : 30 bois (au-delà des réserves de chauffage) et un bon bâtisseur.
        colony.PresentMembers.First().Skills.Practice(SkillType.Construction, 5000f);
        home.Stock.Add(ResourceType.Wood, 400);
        int wood = home.Stock.Get(ResourceType.Wood);
        Assert.True(Trade.ConsiderGearUpgrade(world, colony));
        Assert.Equal(CaravanGear.WoodCart, colony.CaravanGear);
        Assert.Equal(wood - 30, home.Stock.Get(ResourceType.Wood));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("équipe ses caravanes"));

        // Charrette de fer : 10 fer et 2 outils.
        Assert.False(Trade.ConsiderGearUpgrade(world, colony));
        home.Stock.Add(ResourceType.Iron, 12);
        home.Stock.Add(ResourceType.Tools, 3);
        colony.Trades.Clear();
        for (int i = 0; i < 3; i++)
            colony.Trades.Add(new TradeRecord(i, "Voisine", [], 0, WeSent: true) { GainHours = 800, CostHours = 20, CapacityLimited = true });
        Assert.True(Trade.ConsiderGearUpgrade(world, colony));
        Assert.Equal(CaravanGear.IronCart, colony.CaravanGear);
        Assert.Equal((2, 1), (home.Stock.Get(ResourceType.Iron), home.Stock.Get(ResourceType.Tools)));

        // Bêtes de trait : il faut deux bœufs ou deux chevaux apprivoisés ; ils ne sont pas consommés.
        for (int i = 0; i < 3; i++)
            colony.Trades.Add(new TradeRecord(i, "Voisine", [], 0, WeSent: true) { GainHours = 800, CostHours = 20, CapacityLimited = true });
        Assert.False(Trade.ConsiderGearUpgrade(world, colony));
        home.Oxen = 2;
        Assert.True(Trade.ConsiderGearUpgrade(world, colony));
        Assert.Equal(CaravanGear.Draft, colony.CaravanGear);
        Assert.Equal(2, home.Oxen);
        Assert.False(Trade.ConsiderGearUpgrade(world, colony)); // niveau maximal
    }

    [Fact]
    public void Une_caravane_equipee_porte_plus_part_avec_ses_betes_les_nourrit_et_les_ramene()
    {
        WorldState world = TwoPeoples();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Settlement home = from.PrimarySettlement;
        Set(from, ResourceType.Tools, 60);
        Set(to, ResourceType.Tools, 0);
        Set(to, ResourceType.Coins, 3000);
        from.CaravanGear = CaravanGear.Draft;
        home.Oxen = 2;
        // Sans le grain de leur ration, l'attelage ne part pas : une charrette renforcée de fer.
        Set(from, ResourceType.Grain, 2);
        Assert.Equal(CaravanGear.IronCart, Trade.EffectiveGear(from));
        Set(from, ResourceType.Grain, 80);
        Assert.Equal(CaravanGear.Draft, Trade.EffectiveGear(from));
        Assert.Equal(4 * Trade.CapacityOf(from, to, CaravanGear.Porters), Trade.CapacityOf(from, to));

        TradePlan plan = Plan(world, from, to)!;
        Assert.NotNull(plan);
        int grain = from.Stock.Get(ResourceType.Grain);
        Caravan caravan = Trade.Depart(world, plan)!;
        Assert.Equal(CaravanGear.Draft, caravan.Gear);
        Assert.Equal((ResourceType.Oxen, 2), (caravan.DraftSpecies, caravan.DraftCount));
        Assert.Equal(0, home.Oxen);
        Assert.True(grain - from.Stock.Get(ResourceType.Grain) >= Trade.DraftFeed(plan.TripDays) - 0.001, "la ration des bêtes est chargée");
        Assert.Contains("attelage", TransportView.TransportLabel(caravan));

        for (int hour = 0; hour < 24 * 60 && world.Caravans.Contains(caravan); hour++)
        {
            for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance();
            Trade.Hourly(world);
        }
        Assert.DoesNotContain(caravan, world.Caravans);
        Assert.Equal(caravan.DraftCount, home.Oxen + from.Stock.Get(ResourceType.Oxen));
    }

    [Fact]
    public void Un_attelage_voyage_plus_vite_qu_une_caravane_de_porteurs()
    {
        WorldState a = TwoPeoples(), b = TwoPeoples();
        foreach (WorldState world in new[] { a, b })
        {
            Colony from = world.Colonies[0], to = world.Colonies[1];
            Set(from, ResourceType.Tools, 30);
            Set(to, ResourceType.Tools, 0);
            Set(to, ResourceType.Coins, 800);
            Set(from, ResourceType.Grain, 80);
        }
        b.Colonies[0].CaravanGear = CaravanGear.Draft;
        b.Colonies[0].PrimarySettlement.Horses = 2;
        Caravan slow = Trade.Depart(a, Plan(a, a.Colonies[0], a.Colonies[1])!)!;
        Caravan fast = Trade.Depart(b, Plan(b, b.Colonies[0], b.Colonies[1])!)!;
        Assert.True(fast.ArriveTicks - fast.DepartTicks < slow.ArriveTicks - slow.DepartTicks);
    }

    // ---------- B.5 Interception ----------

    [Fact]
    public void Le_risque_baisse_pres_des_colonies_et_avec_les_routes_et_monte_avec_les_predateurs()
    {
        WorldState world = TwoPeoples();
        Colony colony = world.Colonies[0];
        int home = colony.PrimarySettlement.RegionTileIndex;
        int near = world.WorldMap.Grid.Neighbors(home).First(n => !world.WorldMap.Grid[n].IsOcean);
        double nearRisk = Trade.RouteRisk(world, home, near);
        // Loin de tout établissement, la route est plus risquée.
        int far = world.WorldMap.Grid.Tiles.Where(t => !t.IsOcean && world.Settlements.All(s => world.WorldMap.Grid.Distance(s.RegionTileIndex, t.Index) > 4))
            .Select(t => t.Index).First();
        int farNeighbor = world.WorldMap.Grid.Neighbors(far).First();
        double farRisk = Trade.RouteRisk(world, far, farNeighbor);
        Assert.True(farRisk > nearRisk, $"{farRisk} > {nearRisk}");
        Assert.InRange(farRisk, Trade.BaseRisk * 0.3, Trade.BaseRisk * 3);

        // Une route aménagée est plus sûre.
        world.WorldMap.ImproveRoad(far, farNeighbor, 2);
        Assert.True(Trade.RouteRisk(world, far, farNeighbor) < farRisk);
        world.WorldMap.ImproveRoad(far, farNeighbor, 2);
        Assert.Equal(farRisk * 0.5, Trade.RouteRisk(world, far, farNeighbor), 6);
    }

    [Fact]
    public void Une_interception_perd_une_part_comptee_de_la_cargaison_et_la_caravane_continue()
    {
        WorldState world = TwoPeoples();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Set(from, ResourceType.Tools, 30);
        Set(to, ResourceType.Tools, 0);
        Set(to, ResourceType.Coins, 800);
        Caravan caravan = Trade.Depart(world, Plan(world, from, to)!)!;
        int tools = caravan.Cargo.GetValueOrDefault(ResourceType.Tools), coins = caravan.Coins;
        int worldTools = world.Colonies.Sum(c => c.Stock.Get(ResourceType.Tools)) + caravan.Cargo.GetValueOrDefault(ResourceType.Tools);
        int worldCoins = world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)) + caravan.Coins + world.CoinsLostToEvents;
        Assert.True(tools > 0);
        int a = caravan.Route!.Tiles[0], b = caravan.Route.Tiles[1];
        bool hit = false;
        for (int attempt = 0; attempt < 100_000 && !hit; attempt++)
            hit = Trade.CheckInterception(world, caravan, a, b, edgeCost: 60f); // un très long trajet : le tirage finit par tomber
        Assert.True(hit);
        Assert.InRange(caravan.Cargo.GetValueOrDefault(ResourceType.Tools), 1, tools - 1);
        Assert.True(caravan.Cargo.GetValueOrDefault(ResourceType.Tools) >= (int)(tools * 0.55));
        Assert.Equal(1, caravan.Interceptions);
        // Conservation : les marchandises manquantes sont perdues, comptées, jamais remises au stock ; la bourse reste intacte.
        Assert.Equal(coins, caravan.Coins);
        int nowTools = world.Colonies.Sum(c => c.Stock.Get(ResourceType.Tools)) + caravan.Cargo.GetValueOrDefault(ResourceType.Tools);
        Assert.True(nowTools < worldTools);
        int nowCoins = world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)) + caravan.Coins + world.CoinsLostToEvents;
        Assert.Equal(worldCoins, nowCoins);
        Assert.Contains(from.Thoughts, t => t.Text.Contains("attaqué la caravane"));
        Assert.Contains(caravan.Traders, t => t.Ailment == Ailment.Injured);
        Assert.Contains(world.Caravans, c => c == caravan); // elle continue
    }

    [Fact]
    public void Moins_de_deux_pour_cent_des_voyages_sont_interceptes()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, colonyCount: 2, trade: false);
        int trips = 0, hits = 0;
        var caravan = new Caravan(world.Colonies[0], world.Colonies[1], [], [], 0, 0, 10, 20) { Route = world.WorldMap.TravelRoute(world.Colonies[0].PrimarySettlement.RegionTileIndex, world.Colonies[1].PrimarySettlement.RegionTileIndex, null) };
        Assert.NotNull(caravan.Route);
        WorldRoute route = caravan.Route!;
        for (int trip = 0; trip < 20_000; trip++)
        {
            trips++;
            bool intercepted = false;
            // Aller et retour sur toute la route.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < route.Tiles.Count; i++)
                {
                    caravan.Inventory.Add(ResourceType.Tools, 3, ResourceFlow.Transfer);
                    intercepted |= Trade.CheckInterception(world, caravan, route.Tiles[i - 1], route.Tiles[i], route.Cumulative[i] - route.Cumulative[i - 1]);
                    caravan.Inventory.TryTake(ResourceType.Tools, caravan.Inventory.Get(ResourceType.Tools), ResourceFlow.Transfer);
                }
            if (intercepted) hits++;
        }
        Assert.True(hits < 0.02 * trips, $"{hits} interceptions sur {trips} voyages");
        Assert.True(hits > 0, "elles restent possibles");
    }
}

public sealed class BridgeAndMillTests
{
    [Fact]
    public void Un_pont_relie_deux_berges_en_ligne_droite_et_raccorde_les_chemins()
    {
        var world = new WorldState(1, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        LocalMap map = colony.Map;
        for (int y = 2; y < map.Height - 2; y++)
        for (int x = 2; x < map.Width - 2; x++)
            if (map.IsRiver(x, y) && Bridges.Crossing(map, y * map.Width + x, Bridges.MaxCells) is { } c)
            {
                Assert.All(c.Cells, cell => Assert.True(map.IsRiver(cell % map.Width, cell / map.Width)));
                Assert.All(c.Landings, l => Assert.True(map.IsWalkable(l % map.Width, l / map.Width) && !map.IsRiver(l % map.Width, l / map.Width)));
                int step = c.Horizontal ? 1 : map.Width;
                Assert.Equal(c.Cells[0] - step, c.Landings[0]);
                Assert.Equal(c.Cells[^1] + step, c.Landings[1]);
                var site = new BridgeSite { Id = 1, Cells = c.Cells, Landings = c.Landings, Horizontal = c.Horizontal };
                colony.PrimarySettlement.BridgeSites.Add(site);
                foreach (int cell in c.Cells) Bridges.CompleteCell(colony, cell % map.Width, cell / map.Width, 1);
                Assert.All(c.Landings, l => Assert.NotEqual(RoadSurface.None, map.Roads.SurfaceAt(l)));
                return;
            }
        throw new InvalidOperationException("Aucun franchissement trouvé.");
    }

    [Fact]
    public void La_roue_du_moulin_se_pose_du_cote_de_l_eau_et_sans_eau_il_n_y_a_ni_cote_ni_debit()
    {
        var world = new WorldState(1, startingColonists: 10, migration: false, lifecycle: false);
        LocalMap map = world.Colonies[0].Map;
        for (int y = 3; y < map.Height - 3; y++)
        for (int x = 3; x < map.Width - 3; x++)
            if (map.IsRiver(x, y) && map.GetFlow(x, y) > 0f)
            {
                var mill = new Building(BuildingType.Mill, x + 1, y);
                (float flow, MillSide? side) = Hydrology.MillWater(map, mill);
                Assert.True(flow > 0f && side is not null);
                var dry = new Building(BuildingType.Mill, 0, 0);
                if (Hydrology.MillFlow(map, dry) == 0f) Assert.Null(Hydrology.MillWater(map, dry).Side);
                return;
            }
        throw new InvalidOperationException("Aucune rivière trouvée.");
    }
}
