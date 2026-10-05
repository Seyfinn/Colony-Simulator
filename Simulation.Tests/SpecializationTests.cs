using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Tests;

/// <summary>Spécialisation : filières choisies par surplus par heure, lissées, exportations additionnées selon l'acheteur solvable, descriptions déduites.</summary>
public sealed class SpecializationTests
{
    private static (WorldState World, Colony Maker, Colony Buyer) Prepare()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, trade: false, colonyCount: 2);
        Colony maker = world.Colonies[0], buyer = world.Colonies[1];
        maker.Stock.Add(ResourceType.Food, 500);
        buyer.Stock.Add(ResourceType.Food, 500);
        maker.IronSeen = true;
        return (world, maker, buyer);
    }

    private static void Offer(WorldState world, Colony maker, Colony buyer, ResourceType good, int wanted, double buyPrice, int budget)
    {
        SupplierMemory memory = Trade.Suppliers(world, maker).FirstOrDefault(m => m.Supplier == buyer) ?? new SupplierMemory(maker, buyer);
        if (!world.SupplierMemories.Contains(memory)) world.SupplierMemories.Add(memory);
        memory.ObservedTicks = world.Clock.Ticks; memory.BuyingBudget = budget;
        memory.Offers.RemoveAll(o => o.Good == good);
        memory.Offers.Add(new MarketOffer(good, 0, wanted, SellPrice: buyPrice * 2, BuyPrice: buyPrice));
    }

    [Fact]
    public void Les_filieres_retenues_sont_celles_qui_rapportent_le_plus_par_heure_de_travail()
    {
        var (world, maker, buyer) = Prepare();
        maker.Labor.Record(ResourceType.Tools, workerHours: 100, units: 1);      // 100 h/unité
        maker.Labor.Record(ResourceType.Clothes, workerHours: 20, units: 1);     // 20 h/unité
        maker.Labor.Record(ResourceType.Pottery, workerHours: 10, units: 1);     // 10 h/unité
        Offer(world, maker, buyer, ResourceType.Tools, 4, buyPrice: 400, budget: 2000);   // (400−105)/100 ≈ 2,9
        Offer(world, maker, buyer, ResourceType.Clothes, 4, buyPrice: 22, budget: 2000);  // à peine rentable après transport
        Offer(world, maker, buyer, ResourceType.Pottery, 4, buyPrice: 60, budget: 2000);  // (60−10,5)/10 ≈ 4,9
        ProductionPlanner.Revise(world, maker, [buyer]);
        Assert.Equal(ProductionPlanner.FocusSize, maker.FocusGoods.Count);
        Assert.Contains(ResourceType.Pottery, maker.FocusGoods);
        Assert.Contains(ResourceType.Tools, maker.FocusGoods);
        Assert.DoesNotContain(ResourceType.Clothes, maker.FocusGoods);
    }

    [Fact]
    public void Un_bien_jamais_produit_n_est_pas_une_filiere_meme_tres_demande()
    {
        var (world, maker, buyer) = Prepare();
        Offer(world, maker, buyer, ResourceType.Jewelry, 4, buyPrice: 5000, budget: 50000);
        ProductionPlanner.Revise(world, maker, [buyer]);
        Assert.Empty(maker.FocusGoods);
    }

    [Fact]
    public void Un_challenger_plus_rentable_ne_remplace_une_filiere_qu_apres_trois_jours_et_quinze_pour_cent()
    {
        var (world, maker, buyer) = Prepare();
        maker.Labor.Record(ResourceType.Tools, workerHours: 100, units: 1);
        maker.Labor.Record(ResourceType.Pottery, workerHours: 10, units: 1);
        maker.Labor.Record(ResourceType.Clothes, workerHours: 20, units: 1);
        Offer(world, maker, buyer, ResourceType.Tools, 4, 400, 2000);
        Offer(world, maker, buyer, ResourceType.Pottery, 4, 60, 2000);
        Offer(world, maker, buyer, ResourceType.Clothes, 4, 80, 2000);   // (80−21)/20 ≈ 2,9 : moins que Tools (≈2,9 également) ou presque
        ProductionPlanner.Revise(world, maker, [buyer]);
        ResourceType weakest = maker.FocusGoods.OrderBy(g => ProductionPlanner.Score(world, maker, g, [buyer])).First();
        double weakestScore = ProductionPlanner.Score(world, maker, weakest, [buyer]);
        ResourceType challenger = new[] { ResourceType.Tools, ResourceType.Pottery, ResourceType.Clothes }.Single(g => !maker.FocusGoods.Contains(g));
        // Un peu meilleur (+5 %) : jamais assez pour changer.
        Offer(world, maker, buyer, challenger, 4, buyPrice: BuyPriceFor(world, maker, challenger, weakestScore * 1.05), 2000);
        for (int day = 0; day < 10; day++) ProductionPlanner.Revise(world, maker, [buyer]);
        Assert.DoesNotContain(challenger, maker.FocusGoods);
        // Nettement meilleur (+60 %) : il n'entre pas le premier jour, ni le deuxième, mais le troisième.
        Offer(world, maker, buyer, challenger, 4, buyPrice: BuyPriceFor(world, maker, challenger, weakestScore * 1.6), 2000);
        ProductionPlanner.Revise(world, maker, [buyer]); Assert.DoesNotContain(challenger, maker.FocusGoods);
        ProductionPlanner.Revise(world, maker, [buyer]); Assert.DoesNotContain(challenger, maker.FocusGoods);
        ProductionPlanner.Revise(world, maker, [buyer]); Assert.Contains(challenger, maker.FocusGoods);
        Assert.DoesNotContain(weakest, maker.FocusGoods);
    }

    /// <summary>Le prix d'achat qui donnerait au bien ce score pour ce producteur.</summary>
    private static double BuyPriceFor(WorldState world, Colony maker, ResourceType good, double score) =>
        Economy.Cost(maker, good) * (score * 1 + 1.05) + ProductionPlanner.TransportPerUnit(world, maker, world.Colonies[1], good);

    [Fact]
    public void Une_crise_vide_les_filieres_d_exportation()
    {
        var (world, maker, buyer) = Prepare();
        maker.Labor.Record(ResourceType.Pottery, workerHours: 10, units: 1);
        Offer(world, maker, buyer, ResourceType.Pottery, 4, 60, 2000);
        ProductionPlanner.Revise(world, maker, [buyer]);
        Assert.NotEmpty(maker.FocusGoods);
        maker.Stock.TryTake(ResourceType.Food, maker.Stock.Get(ResourceType.Food), ResourceFlow.Loss);
        foreach (ResourceType food in new[] { ResourceType.Grain, ResourceType.Bread, ResourceType.Fish }) maker.Stock.TryTake(food, maker.Stock.Get(food), ResourceFlow.Loss);
        ProductionPlanner.Revise(world, maker, [buyer]);
        Assert.Empty(maker.FocusGoods);
    }

    [Fact]
    public void L_objectif_d_exportation_additionne_les_acheteurs_solvables_et_plafonne_par_le_transport()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, trade: false, colonyCount: 3);
        Colony maker = world.Colonies[0], a = world.Colonies[1], b = world.Colonies[2];
        maker.Stock.Add(ResourceType.Food, 500); maker.IronSeen = true;
        maker.Labor.Record(ResourceType.Pottery, workerHours: 10, units: 1);
        maker.FocusGoods.Add(ResourceType.Pottery);
        Offer(world, maker, a, ResourceType.Pottery, 5, 60, budget: 2000);
        Offer(world, maker, b, ResourceType.Pottery, 3, 60, budget: 100);   // solvable pour 1 seule unité (100/60)
        var tradeDaily = typeof(Trade).GetMethod("UpdateExportInterest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        tradeDaily.Invoke(null, [world, maker, new List<Colony> { a, b }]);
        Assert.Equal(5 + 1, maker.ExportInterest[ResourceType.Pottery]); // 5 (A) + 1 (B, limité par sa bourse), pas le maximum des deux
        // Ce qui est déjà promis par une caravane ne compte pas deux fois.
        a.Stock.Add(ResourceType.Coins, 0);
        Offer(world, maker, a, ResourceType.Pottery, 50, 60, budget: 100000);
        tradeDaily.Invoke(null, [world, maker, new List<Colony> { a, b }]);
        Assert.InRange(maker.ExportInterest[ResourceType.Pottery], 1, 24); // plafonné par l'horizon
        // Hors des filières retenues, aucun objectif d'exportation.
        maker.FocusGoods.Clear();
        tradeDaily.Invoke(null, [world, maker, new List<Colony> { a, b }]);
        Assert.Equal(0, maker.ExportInterest[ResourceType.Pottery]);
    }

    [Fact]
    public void Le_profil_decrit_sans_rien_accorder()
    {
        var (_, maker, _) = Prepare();
        Assert.Empty(EconomicProfile.Of(maker).Labels);
        maker.Labor.Record(ResourceType.Grain, workerHours: 100, units: 60);
        maker.Labor.Record(ResourceType.Tools, workerHours: 400, units: 3);
        EconomicProfile profile = EconomicProfile.Of(maker);
        Assert.Contains("artisanale", profile.Labels);
        Assert.DoesNotContain("minière", profile.Labels);
        Assert.Empty(profile.Dependencies);
    }
}
