using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class BalanceTests
{
    [Fact]
    public void Les_objets_ne_perdent_pas_la_valeur_des_intrants_jamais_produits_localement()
    {
        var monde = new WorldState(7, startingColonists: 8, migration: false, lifecycle: false);
        Colony colonie = monde.Colonies[0];
        Recipe recette = ExtendedIndustry.Recipes.Single(r => r.Output == ResourceType.Shoes);
        colonie.Stock.Add(ResourceType.Leather, 2);
        double valeur = Economy.Cost(colonie, ResourceType.Leather);
        Assert.Null(colonie.Labor.HoursPerUnit(ResourceType.Leather));
        Assert.True(ToolChain.TryTakeInputs(colonie, recette, out double heures, out Stockpile intrants));
        Assert.Equal(2 * valeur, heures, 6);
        Assert.Equal(2, intrants.Get(ResourceType.Leather));
        Assert.Equal(0, colonie.Stock.Get(ResourceType.Leather));

        Assert.True(Economy.BaselineCost(ResourceType.Shoes) > 2 * Economy.BaselineCost(ResourceType.Leather));
        Assert.True(Economy.BaselineCost(ResourceType.Carts) > Economy.BaselineCost(ResourceType.Iron) + 10 * Economy.BaselineCost(ResourceType.Wood));
        Assert.All(Enum.GetValues<ResourceType>(), bien =>
        {
            Assert.True(double.IsFinite(Economy.BaselineCost(bien)) && Economy.BaselineCost(bien) > 0);
            Assert.Equal(Economy.BaselineCost(bien), ResourceCatalog.ReferenceCost(bien));
        });
        Assert.Equal(1, Economy.BaselineCost(ResourceType.Coins));
    }

    [Fact]
    public void La_prime_suit_les_transformations_successives_sans_gonfler_le_travail_reel()
    {
        Assert.Equal(0, Economy.ProductionSteps(ResourceType.Wood));
        Assert.Equal(0, Economy.ProductionSteps(ResourceType.IronOre));
        Assert.Equal(0, Economy.ProductionSteps(ResourceType.Coins));
        Assert.Equal(1, Economy.ProductionSteps(ResourceType.Charcoal));
        Assert.Equal(1, Economy.ProductionSteps(ResourceType.SaltedMeat));
        Assert.Equal(2, Economy.ProductionSteps(ResourceType.Iron));
        Assert.Equal(2, Economy.ProductionSteps(ResourceType.Bread));
        Assert.Equal(2, Economy.ProductionSteps(ResourceType.Clothes));
        Assert.Equal(2, Economy.ProductionSteps(ResourceType.Shoes));
        Assert.Equal(3, Economy.ProductionSteps(ResourceType.Tools));
        Assert.Equal(3, Economy.ProductionSteps(ResourceType.Jewelry));
        Assert.Equal(3, Economy.ProductionSteps(ResourceType.Carts));

        var monde = new WorldState(7, startingColonists: 8, migration: false, lifecycle: false);
        Colony colonie = monde.Colonies[0];
        colonie.Labor.Record(ResourceType.Charcoal, 20, 10);
        colonie.Labor.Record(ResourceType.Iron, 50, 10);
        Assert.Equal(2, Economy.Cost(colonie, ResourceType.Charcoal));
        Assert.Equal(5, Economy.Cost(colonie, ResourceType.Iron));
        Recipe recette = ToolChain.RecipeFor(BuildingType.Forge);
        foreach (var intrant in recette.Inputs) colonie.Stock.Add(intrant.Type, intrant.Amount);
        double travailIntrants = recette.Inputs.Sum(i => i.Amount * Economy.Cost(colonie, i.Type));
        Assert.True(ToolChain.TryTakeInputs(colonie, recette, out double heures, out _));
        Assert.Equal(travailIntrants, heures, 6);
        Assert.Equal(5 * 1.16, Economy.Value(colonie, ResourceType.Iron) / Economy.Scarcity(colonie, ResourceType.Iron), 6);
        Assert.Equal(Economy.Cost(colonie, ResourceType.Coins), Economy.Value(colonie, ResourceType.Coins) / Economy.Scarcity(colonie, ResourceType.Coins), 6);
    }

    [Fact]
    public void Une_chaine_longue_ameliore_le_prix_negocie_et_la_specialisation_a_travail_egal()
    {
        var monde = new WorldState(12345, startingColonists: 8, colonyCount: 2, migration: false, lifecycle: false, trade: false);
        Colony vendeur = monde.Colonies[0], acheteur = monde.Colonies[1];
        foreach (Colony colonie in monde.Colonies)
            foreach (ResourceType bien in new[] { ResourceType.Wood, ResourceType.Tools })
            {
                Assert.True(colonie.Stock.TryTake(bien, colonie.Stock.Get(bien)));
                colonie.Labor.Record(bien, 10, 10);
            }
        vendeur.Stock.Add(ResourceType.Wood, 1000);
        vendeur.Stock.Add(ResourceType.Tools, 1000);
        Clearing bois = Economy.Clear(vendeur, acheteur, ResourceType.Wood, 1);
        Clearing outils = Economy.Clear(vendeur, acheteur, ResourceType.Tools, 1);
        Assert.Equal(1, bois.Units);
        Assert.Equal(1, outils.Units);
        Assert.Equal(bois.UnitPrice * 1.24, outils.UnitPrice, 6);
        Assert.Equal(bois.GainHours * 1.24, outils.GainHours, 6);
        Assert.True(ProductionPlanner.Score(monde, acheteur, ResourceType.Tools, []) > ProductionPlanner.Score(monde, acheteur, ResourceType.Wood, []));
        Assert.Equal(1, Economy.Cost(vendeur, ResourceType.Tools));
    }

    [Fact]
    public void La_natalite_suit_la_longevite_et_les_grossesses_reelles_suivent_le_rythme_du_peuple()
    {
        var monde = new WorldState(7, startingColonists: 8, colonyCount: 4, migration: false);
        foreach (Colony colonie in monde.Colonies)
        {
            Species espece = colonie.Species;
            Assert.Equal(1f, espece.Fertility * espece.LifespanScale, 5);
            colonie.Stock.Add(ResourceType.Food, 4000);
            foreach (Colonist habitant in colonie.Members)
                habitant.Needs.Food = habitant.Needs.Rest = habitant.Needs.Leisure = habitant.Needs.Social = habitant.Needs.Comfort = 1;
            Colonist mere = colonie.Members.First(c => c.Sex == Sex.Female);
            Colonist pere = colonie.Members.First(c => c.Sex == Sex.Male);
            mere.Partner = pere;
            pere.Partner = mere;
            for (int jour = 0; jour < 1000 && mere.PregnantUntilTicks is null; jour++) Lifecycle.Daily(monde, colonie);
            Assert.Equal(monde.Clock.Ticks + (long)(Lifecycle.PregnancyDays * espece.LifespanScale * TimeConstants.TicksPerDay), mere.PregnantUntilTicks);

            mere.PregnantUntilTicks = null;
            mere.LastBirthTicks = monde.Clock.Ticks;
            long intervalle = (long)(Lifecycle.MinBirthIntervalYears * espece.LifespanScale * TimeConstants.TicksPerYear);
            Assert.False(Lifecycle.CanConceive(mere, monde.Clock.Ticks + intervalle - 1));
            Assert.True(Lifecycle.CanConceive(mere, monde.Clock.Ticks + intervalle));
        }
        Species[] peuples = Species.All.OrderBy(e => e.LifespanScale).ToArray();
        for (int i = 1; i < peuples.Length; i++) Assert.True(peuples[i - 1].Fertility > peuples[i].Fertility);
    }

    [Fact]
    public void Les_besoins_d_intrants_suivent_les_rendements_et_la_taille_du_village()
    {
        var monde = new WorldState(7, startingColonists: 20, migration: false, lifecycle: false);
        Colony colonie = monde.Colonies[0];
        colonie.Labor.Record(ResourceType.Grain, 10, 20);
        Assert.Equal(9, ExtendedIndustry.Target(colonie, ResourceType.Clay)); // cinq poteries : trois fournées de deux
        Assert.Equal(60, ExtendedIndustry.Target(colonie, ResourceType.Flax)); // quarante toiles : vingt fournées de deux
        BreadDemand demande = FoodChain.Demand(colonie);
        Assert.True(demande.FlourTarget >= FoodChain.FlourForBread((int)Math.Ceiling(20 * ColonyBrain.MealsPerColonistPerDay)));
        int sansMoulin = FoodChain.GrainForBread(colonie, 8);
        colonie.Buildings.Add(new Building(BuildingType.Mill, 0, 0) { Progress = 1 });
        Assert.True(FoodChain.GrainForBread(colonie, 8) < sansMoulin);
        colonie.Stock.Add(ResourceType.Flour, 6);
        Assert.Equal(0, FoodChain.GrainForBread(colonie, 8));
        Assert.True(Economy.Need(colonie, ResourceType.Grain) >= Cuisine.BeerGrainReserve(colonie));
    }

    [Fact]
    public void Le_commerce_protege_les_charrettes_utilisees_et_prend_en_compte_les_herbes()
    {
        var monde = new WorldState(7, startingColonists: 8, migration: false, lifecycle: false);
        Colony colonie = monde.Colonies[0];
        colonie.Stock.Add(ResourceType.Carts, 2);
        colonie.Members[0].UsingCart = true;
        Assert.Equal(0, Economy.Surplus(colonie, ResourceType.Carts));
        colonie.Buildings.Add(new Building(BuildingType.Infirmary, 0, 0) { Progress = 1 });
        Assert.True(Economy.Shortage(colonie, ResourceType.Herbs) >= 2);
        Assert.Contains(ResourceType.Herbs, Economy.Tradable);
        Assert.Contains(ResourceType.Honey, Economy.Tradable);
        Assert.Contains(ResourceType.Carts, Economy.Tradable);
        Assert.DoesNotContain(ResourceType.Coins, Economy.Tradable);
    }

    [Fact]
    public void La_frappe_reste_proche_de_la_valeur_de_l_or_sans_depassement_du_quota()
    {
        Assert.InRange(MonetaryLedger.CoinsPerGold / Economy.BaselineCost(ResourceType.Gold), 0.75, 1.1);
        var monde = new WorldState(7, startingColonists: 8, migration: false, lifecycle: false);
        Colony colonie = monde.Colonies[0];
        long avant = monde.Money.RemainingFor(colonie);
        Assert.True(monde.Money.TryCommit(colonie, MonetaryLedger.CoinsPerGold));
        Assert.Equal(avant - MonetaryLedger.CoinsPerGold, monde.Money.RemainingFor(colonie));
        Assert.False(monde.Money.TryCommit(colonie, (int)avant));
        Assert.Equal(0, monde.Money.Imbalance(monde));
    }

    [Fact]
    public void L_expediteur_ne_compte_pas_le_benefice_du_voisin_pour_payer_son_voyage()
    {
        var monde = new WorldState(12345, startingColonists: 10, colonyCount: 2, migration: false, lifecycle: false, trade: false);
        Colony vendeur = monde.Colonies[0], acheteur = monde.Colonies[1];
        vendeur.Stock.Add(ResourceType.Tools, 30);
        acheteur.Stock.Add(ResourceType.Coins, 10000);
        Trade.ObserveMarket(monde, vendeur, acheteur);
        TradePlan plan = Assert.IsType<TradePlan>(Trade.Plan(monde, vendeur, acheteur));
        double gainPropre = 0;
        foreach (TradeLine ligne in plan.Lines)
            for (int i = 0; i < ligne.Units; i++)
                gainPropre += ligne.IsSale
                    ? ligne.UnitPrice - Economy.KeepValue(vendeur, ligne.Good, vendeur.Stock.Available(ligne.Good) - i)
                    : Economy.UseValue(vendeur, ligne.Good, vendeur.Stock.Available(ligne.Good) + i) - ligne.UnitPrice;
        Assert.Equal(gainPropre, plan.GainHours, 6);
        Assert.True(plan.GainHours >= plan.CostHours * Trade.RequiredGainOverCost);
    }
}
