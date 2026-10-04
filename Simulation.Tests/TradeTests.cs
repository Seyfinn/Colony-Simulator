using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class TradeTests(ITestOutputHelper output)
{
    private static WorldState TwoPeoples(int colonists = 10, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false, colonyCount: 2, trade: false);

    private static void Set(Colony colony, ResourceType type, int amount)
    {
        colony.Stock.TryTake(type, colony.Stock.Get(type));
        colony.Stock.Add(type, amount);
    }

    private static int CoinsInTheWorld(WorldState world) =>
        world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)) + world.Caravans.Sum(c => c.Coins);

    private static int GoodInTheWorld(WorldState world, ResourceType good) =>
        world.Colonies.Sum(c => c.Stock.Get(good)) + world.Caravans.Sum(c => c.Cargo.GetValueOrDefault(good));

    private static void RunHours(WorldState world, int hours)
    {
        for (long i = 0; i < (long)Math.Ceiling(hours * (double)TimeConstants.TicksPerHour); i++)
            world.Step();
    }

    [Fact]
    public void Chaque_unite_de_plus_vaut_moins_a_celui_qui_en_a_deja()
    {
        WorldState world = TwoPeoples();
        Colony colony = world.Colonies[0];

        double none = Economy.UseValue(colony, ResourceType.Tools, 0);
        double enough = Economy.UseValue(colony, ResourceType.Tools, ToolChain.ToolsWanted(colony));
        double glut = Economy.UseValue(colony, ResourceType.Tools, 5 * ToolChain.ToolsWanted(colony));
        Assert.True(none > enough && enough > glut, $"{none:0} > {enough:0} > {glut:0}");

        // Céder sa dernière unité coûte plus cher que céder une unité parmi beaucoup.
        Assert.True(Economy.KeepValue(colony, ResourceType.Tools, 1) > Economy.KeepValue(colony, ResourceType.Tools, 20));

        // Ce dont on n'a pas l'usage ne vaut rien à l'achat.
        Set(colony, ResourceType.IronOre, 0);
        colony.IronSeen = false;
        Assert.Equal(0.0, Economy.UseValue(colony, ResourceType.IronOre, 0));
    }

    [Fact]
    public void L_echange_se_conclut_entre_les_deux_valeurs_et_chaque_unite_rapporte_aux_deux()
    {
        WorldState world = TwoPeoples();
        Colony rich = world.Colonies[0], poor = world.Colonies[1];
        Set(rich, ResourceType.Tools, 30);
        Set(poor, ResourceType.Tools, 0);

        Clearing deal = Economy.Clear(rich, poor, ResourceType.Tools, maxUnits: 50);

        Assert.True(deal.Units > 0);
        Assert.True(deal.GainHours > 0);
        // Le prix reste entre ce que cela coûte au vendeur de céder la dernière unité et ce qu'elle vaut pour l'acheteur.
        Assert.True(deal.UnitPrice >= Economy.KeepValue(rich, ResourceType.Tools, 30 - deal.Units + 1) - 0.01);
        Assert.True(deal.UnitPrice <= Economy.UseValue(poor, ResourceType.Tools, deal.Units - 1) + 0.01);

        // Deux colonies qui estiment pareil n'ont rien à échanger.
        Set(rich, ResourceType.Tools, 0);
        Assert.Equal(0, Economy.Clear(rich, poor, ResourceType.Tools, 50).Units);
    }

    [Fact]
    public void On_ne_paie_que_ce_que_la_bourse_contient()
    {
        WorldState world = TwoPeoples();
        Colony seller = world.Colonies[0], buyer = world.Colonies[1];
        Set(seller, ResourceType.Tools, 30);
        Set(buyer, ResourceType.Tools, 0);
        Set(buyer, ResourceType.Coins, 500);
        TradePlan? unlimited = Trade.Plan(world, seller, buyer);

        Set(buyer, ResourceType.Coins, 260);
        TradePlan? plan = Trade.Plan(world, seller, buyer);

        Assert.NotNull(unlimited);
        Assert.NotNull(plan);
        double spentByBuyer = plan!.Lines.Where(l => l.IsSale).Sum(l => l.Total);
        Assert.True(spentByBuyer <= 260 + 0.01, $"L'acheteur ne peut pas dépenser {spentByBuyer:0} avec 260 pièces.");
        Assert.True(plan.Lines.Sum(l => l.Units) < unlimited!.Lines.Sum(l => l.Units), "Une bourse plus vide réduit l'échange.");
        Assert.True(plan.GainHours >= plan.CostHours * Trade.RequiredGainOverCost);
    }

    [Fact]
    public void Une_caravane_part_echange_et_rentre_sans_creer_ni_perdre_de_marchandises_ni_de_pieces()
    {
        WorldState world = TwoPeoples();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Set(from, ResourceType.Tools, 30);
        Set(to, ResourceType.Tools, 0);
        Set(to, ResourceType.Coins, 800);

        int coins = CoinsInTheWorld(world);
        int tools = GoodInTheWorld(world, ResourceType.Tools);
        int membersBefore = from.Members.Count;
        int fromCoinsBefore = from.Stock.Get(ResourceType.Coins);

        TradePlan plan = Trade.Plan(world, from, to)!;
        Assert.NotNull(plan);
        Caravan caravan = Trade.Depart(world, plan)!;
        Assert.NotNull(caravan);

        // En route : les colons ont quitté la colonie, les marchandises aussi, les pièces voyagent.
        Assert.Equal(membersBefore - Trade.TradersPerCaravan, from.Members.Count);
        Assert.All(caravan.Traders, t => Assert.DoesNotContain(t, from.Members));
        Assert.All(caravan.Traders, t => Assert.Equal(TransitState.Leaving, t.Transit)); // ils marchent jusqu'au bord de la carte
        Assert.Equal(coins, CoinsInTheWorld(world));
        Assert.Equal(tools, GoodInTheWorld(world, ResourceType.Tools));
        Assert.True(caravan.Cargo.GetValueOrDefault(ResourceType.Tools) > 0);

        // Arrivée : la colonie hôte a reçu les outils et payé en pièces.
        long untilArrival = caravan.ArriveTicks - world.Clock.Ticks;
        RunHours(world, (int)(untilArrival / TimeConstants.TicksPerHour) + 2);
        Assert.Equal(CaravanState.Returning, caravan.State);
        Assert.True(to.Stock.Get(ResourceType.Tools) > 0);
        Assert.True(to.Stock.Get(ResourceType.Coins) < 800);
        Assert.Equal(coins, CoinsInTheWorld(world));
        Assert.Equal(tools, GoodInTheWorld(world, ResourceType.Tools));

        // Retour : les colons rentrent avec le produit de la vente.
        long untilHome = caravan.ReturnTicks - world.Clock.Ticks;
        RunHours(world, (int)(untilHome / TimeConstants.TicksPerHour) + 2);
        Assert.Equal(CaravanState.Home, caravan.State);
        Assert.Empty(world.Caravans);
        Assert.Equal(1, world.CompletedCaravans);

        // Les marchands reviennent par le bord de la carte et marchent jusqu'au camp : on les voit arriver.
        Assert.All(caravan.Traders, t => Assert.Contains(t, from.Transients));
        Assert.All(caravan.Traders, t => Assert.Equal(TransitState.Arriving, t.Transit));
        // La carte est vaste : de l'orée de la carte au camp, la marche peut prendre plus d'un jour.
        for (int hours = 0; hours < 72 && from.Transients.Count > 0; hours++)
            RunHours(world, 1);
        Assert.Equal(membersBefore, from.Members.Count);
        Assert.Empty(from.Transients);
        Assert.Equal(coins, CoinsInTheWorld(world));
        Assert.Equal(tools, GoodInTheWorld(world, ResourceType.Tools));
        Assert.True(from.Stock.Get(ResourceType.Coins) > fromCoinsBefore, "L'expéditeur a encaissé la vente.");
        Assert.Single(from.Trades);
        Assert.Single(to.Trades);
        Assert.True(from.Trades[0].NetCoins > 0);
        Assert.Equal(-from.Trades[0].NetCoins, to.Trades[0].NetCoins);
        Assert.True(from.LifetimeTradeGainHours > 0);
    }

    [Fact]
    public void Une_petite_colonie_n_envoie_pas_ses_colons_en_voyage_et_on_n_en_envoie_pas_deux_a_la_fois()
    {
        var world = new WorldState(12345, startingColonists: 5, migration: false, lifecycle: false, colonyCount: 2);
        Set(world.Colonies[0], ResourceType.Tools, 30);
        Set(world.Colonies[1], ResourceType.Tools, 0);
        RunHours(world, 24 * 3);
        Assert.Empty(world.Caravans);
        Assert.Equal(0, world.CompletedCaravans);

        var big = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false, colonyCount: 2);
        Set(big.Colonies[0], ResourceType.Tools, 30);
        Set(big.Colonies[1], ResourceType.Tools, 0);
        Set(big.Colonies[1], ResourceType.Coins, 800);
        for (int day = 0; day < 3; day++)
        {
            RunHours(big, 24);
            Assert.True(big.Caravans.Count(c => c.From == big.Colonies[0] && c.State != CaravanState.Home) <= 1);
        }
    }

    [Fact]
    public void Avec_une_seule_colonie_il_n_y_a_pas_de_commerce()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false);
        RunHours(world, 24 * 10);
        Assert.Empty(world.Caravans);
        Assert.Equal(0, world.CompletedCaravans);
    }

    [Fact]
    public void Ce_que_le_voisin_paierait_cher_la_colonie_se_met_a_le_fabriquer()
    {
        WorldState world = TwoPeoples();
        Colony maker = world.Colonies[0], customer = world.Colonies[1];
        maker.Labor.Record(ResourceType.Tools, workerHours: 60, units: 1);
        Set(customer, ResourceType.Tools, 0);
        maker.IronSeen = true;

        int before = ToolChain.ToolsTarget(maker);
        Trade.Daily(world, maker); // met à jour ce que le voisin lui achèterait
        Assert.True(maker.ExportInterest.GetValueOrDefault(ResourceType.Tools) > 0);
        Assert.True(ToolChain.ToolsTarget(maker) > before);

        // Les outils pour les voisins ne comptent pas dans l'équipement de ses propres travailleurs.
        Assert.Equal((int)Math.Ceiling(maker.Workers.Count() * ToolChain.ToolsPerWorker), ToolChain.ToolsWanted(maker));
    }

    [Fact]
    public void Deux_peuples_font_du_commerce_pendant_trois_ans_sans_rien_perdre_ni_mourir_de_faim()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: true, lifecycle: true, colonyCount: 2);
        int coinsAtStart = CoinsInTheWorld(world);
        var watches = world.Colonies.ToDictionary(c => c, _ => new StarvationWatch());

        for (long i = 0; i < 3 * TimeConstants.TicksPerYear; i++)
        {
            world.Step();
            if (i % TimeConstants.TicksPerDay == 0)
            {
                foreach (Colony c in world.Colonies)
                    watches[c].Observe(c);
                Assert.Equal(coinsAtStart, CoinsInTheWorld(world)); // la monnaie ne se crée ni ne se perd
            }
        }

        foreach (Colony colony in world.Colonies)
        {
            output.WriteLine($"{colony.Name} : {colony.Members.Count} colons, {colony.Stock.Get(ResourceType.Coins)} pièces, {colony.Trades.Count} voyages, " +
                             $"travail épargné {colony.LifetimeTradeGainHours:0} h");
            foreach (TradeRecord r in colony.Trades)
                output.WriteLine($"  J{r.Ticks / TimeConstants.TicksPerDay + 1} {(r.WeSent ? "→" : "←")} {r.Partner} : " +
                                 string.Join(", ", r.Lines.Select(l => $"{(l.IsSale ? "vend" : "achète")} {l.Units} {l.Good} à {l.UnitPrice:0.0}")) + $" · net {r.NetCoins}");
            Assert.Null(watches[colony].Victim);
        }
        // Selon la carte, deux peuples font un à quatre voyages en trois ans : la mécanique fine est couverte par les autres tests.
        Assert.True(world.CompletedCaravans >= 1, $"Au moins un voyage : {world.CompletedCaravans}.");
        Assert.True(world.Colonies.Sum(c => c.LifetimeTradeGainHours) > 0);
    }

    [Fact]
    public void La_caravane_avance_sur_la_route_attend_chez_l_hote_puis_revient()
    {
        WorldState world = TwoPeoples();
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Set(from, ResourceType.Tools, 30);
        Set(to, ResourceType.Tools, 0);
        Set(to, ResourceType.Coins, 800);
        Caravan caravan = Trade.Depart(world, Trade.Plan(world, from, to)!)!;

        Assert.Equal(0f, caravan.RoutePosition(caravan.DepartTicks), 3);
        float midway = caravan.RoutePosition((caravan.DepartTicks + caravan.ArriveTicks) / 2);
        Assert.InRange(midway, 0.45f, 0.55f);
        Assert.Equal(1f, caravan.RoutePosition(caravan.ArriveTicks), 3);
        Assert.Equal(1f, caravan.RoutePosition(caravan.ArriveTicks + 10), 3); // elle échange chez l'hôte

        long backStart = caravan.ReturnTicks - (caravan.ArriveTicks - caravan.DepartTicks);
        float onTheWayBack = caravan.RoutePosition((backStart + caravan.ReturnTicks) / 2);
        Assert.InRange(onTheWayBack, 0.45f, 0.55f);
        Assert.Equal(0f, caravan.RoutePosition(caravan.ReturnTicks), 3);
    }
}
