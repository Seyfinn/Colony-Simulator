using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Tests;

/// <summary>Élevage et textile, négoce, santé et saisons, bâtiments de village, événements et objectifs : un test par grande idée.</summary>
public class VillageLifeTests
{
    private static (WorldState World, Colony Colony) Village(int colonists = 10)
    {
        var world = new WorldState(777, startingColonists: colonists, migration: false, lifecycle: false);
        return (world, world.Colonies[0]);
    }

    private static Building Build(WorldState world, Colony colony, BuildingType type) => Urbanism.BuildInstantly(world.Map, colony, type)!;

    private static GameClock ClockAtDay(int dayOfYear) => new((long)dayOfYear * TimeConstants.TicksPerDay);

    // ---------- 1. Élevage et textile ----------

    [Fact]
    public void L_enclos_donne_des_oeufs_et_de_la_laine_et_le_metier_tisse_des_vetements()
    {
        (WorldState world, Colony colony) = Village();
        Build(world, colony, BuildingType.Pen);
        Husbandry.OnPenBuilt(colony, world.Clock);
        Assert.Equal(Husbandry.StartingHerd(colony.Map.Biome, ResourceType.Chickens), colony.Chickens);
        Assert.True(Husbandry.Animals(colony) > 0);

        GameClock spring = ClockAtDay(2);
        for (int day = 0; day < 8; day++)
            Husbandry.Daily(colony, spring);
        Assert.True(Husbandry.WorkPending(colony));
        var products = new List<ResourceType>();
        while (Husbandry.Collect(colony) is { } haul)
            products.Add(haul.Type);
        Assert.Contains(ResourceType.Wool, products);

        Build(world, colony, BuildingType.Loom);
        colony.Stock.Add(ResourceType.Wool, 4);
        Assert.NotNull(Husbandry.PickLoomJob(colony));
        Assert.True(ToolChain.TryTakeInputs(colony, Husbandry.Weaving, out _));
        Assert.Equal(2, colony.Stock.Get(ResourceType.Wool));
        Assert.Equal(ResourceType.Clothes, Crafting.RecipeFor(BuildingType.Loom).Output);
        Assert.Equal(SkillType.Weaving, Crafting.SkillFor(BuildingType.Loom));
    }

    [Fact]
    public void L_hiver_sans_grain_les_betes_deperissent_et_les_oeufs_se_mangent()
    {
        (WorldState world, Colony colony) = Village();
        Build(world, colony, BuildingType.Pen);
        Husbandry.OnPenBuilt(colony, world.Clock);
        GameClock winter = ClockAtDay(16);
        Assert.Equal(Season.Hiver, winter.Season);

        int animals = Husbandry.Animals(colony);
        Husbandry.Daily(colony, winter);
        Assert.Equal(animals - 1, Husbandry.Animals(colony));

        // La bête qu'on ne peut plus nourrir est abattue : sa viande est sauvée.
        Assert.True(colony.Stock.Get(ResourceType.Meat) > 0);
        colony.Stock.Add(ResourceType.Eggs, 3);
        Assert.Equal(3, colony.Stock.FoodUnits - colony.Stock.Get(ResourceType.Food) - colony.Stock.Get(ResourceType.Meat));
        Assert.True(colony.Stock.TryTakeMeal(out float meal) && meal > 0f);
    }

    [Fact]
    public void Les_betes_sont_rares_ou_abondantes_selon_la_region_et_s_echangent_le_lait_nourrit_et_les_vaches_tirent_la_charrue()
    {
        Assert.True(Husbandry.Abundance(Biome.Grassland, ResourceType.Cows) > 1f);
        Assert.True(Husbandry.Abundance(Biome.Desert, ResourceType.Cows) < 0.3f);
        Assert.Equal(0, Husbandry.StartingHerd(Biome.Desert, ResourceType.Cows));
        Assert.True(Husbandry.StartingHerd(Biome.Grassland, ResourceType.Cows) >= 3);
        Assert.True(Husbandry.CostFactor(Biome.Desert, ResourceType.Cows) > Husbandry.CostFactor(Biome.Grassland, ResourceType.Cows));

        var world = new WorldState(777, startingColonists: 10, migration: false, lifecycle: false, colonyCount: 2);
        Colony rich = world.Colonies[0], poor = world.Colonies[1];
        rich.Map.Biome = Biome.Grassland;
        poor.Map.Biome = Biome.Desert;
        foreach (Colony colony in new[] { rich, poor })
        {
            Build(world, colony, BuildingType.Pen);
            Husbandry.OnPenBuilt(colony, world.Clock);
        }
        Assert.Equal(0, poor.Cows);
        Assert.True(rich.Cows >= 3);

        // La colonie d'herbages a de jeunes bêtes à vendre, mais une vache vaut une fortune : celle du désert, avec ses 400 pièces, n'y songe pas...
        rich.Stock.Add(ResourceType.Cows, 3);
        Assert.Equal(0, Economy.Clear(rich, poor, ResourceType.Cows, 4).Units);
        Assert.True(Economy.Cost(poor, ResourceType.Cows) > 400);

        // ... mais une colonie très prospère l'achète, à prix d'or, et la met à l'enclos le lendemain.
        poor.Stock.Add(ResourceType.Coins, 10_000);
        Clearing sale = Economy.Clear(rich, poor, ResourceType.Cows, 4);
        Assert.True(sale.Units > 0 && sale.UnitPrice > 300, $"prix {sale.UnitPrice:0}");
        poor.Stock.Add(ResourceType.Cows, 2);
        Husbandry.Daily(poor, ClockAtDay(2));
        Assert.Equal((2, 0), (poor.Cows, poor.Stock.Get(ResourceType.Cows)));

        // Ce sont surtout les produits qui s'échangent : le lait de la colonie qui a des vaches, acheté par celle qui n'en a pas.
        for (int day = 0; day < 5; day++)
            Husbandry.Daily(rich, ClockAtDay(2));
        Assert.Equal(ResourceType.Milk, Husbandry.Collect(rich)!.Value.Type);
        Assert.Contains(ResourceType.Milk, Economy.Tradable);
        Assert.Contains(ResourceType.Eggs, Economy.Tradable);
        rich.Stock.Add(ResourceType.Milk, 20);
        Clearing milk = Economy.Clear(rich, poor, ResourceType.Milk, 36);
        Assert.True(milk.Units > 0 && milk.UnitPrice < Economy.Cost(poor, ResourceType.Cows) / 10);

        // Le lait et les œufs ne se mangent qu'en dernier recours (ils restent à vendre) ; les vaches accélèrent les champs.
        rich.Stock.Add(ResourceType.Grain, 1);
        rich.Stock.TryTake(ResourceType.Food, rich.Stock.Get(ResourceType.Food));
        Assert.True(rich.Stock.TryTakeMeal() && rich.Stock.Get(ResourceType.Grain) == 0 && rich.Stock.Get(ResourceType.Milk) == 20);
        Assert.True(Husbandry.PloughFactor(rich) > 1f && Husbandry.PloughFactor(poor) > 1f);
    }

    [Fact]
    public void L_abattage_ne_se_decide_que_pour_un_vrai_besoin_ou_un_surplus_et_protege_la_paire_de_reproducteurs()
    {
        (WorldState world, Colony colony) = Village(10);
        Build(world, colony, BuildingType.Pen);
        colony.Chickens = 6; colony.Sheep = 4; colony.Cows = 2;
        colony.Stock.Add(ResourceType.Grain, 200);
        GameClock spring = ClockAtDay(2);

        // Ni besoin ni surplus : on ne touche pas aux bêtes.
        Husbandry.PlanSlaughter(colony, spring);
        Assert.False(Husbandry.SlaughterPending(colony));

        // Surplus : enclos plein et quatre petits en réserve que personne n'achète. On abat un petit, pas un reproducteur.
        colony.Chickens = 8;
        colony.Stock.Add(ResourceType.Chickens, 4);
        Husbandry.PlanSlaughter(colony, spring);
        Assert.Equal(1, colony.SlaughterOrders.GetValueOrDefault(ResourceType.Chickens));
        Assert.Equal(6, Husbandry.Slaughter(colony, ResourceType.Chickens));
        Assert.Equal((8, 3), (colony.Chickens, colony.Stock.Get(ResourceType.Chickens)));
        Assert.False(Husbandry.SlaughterPending(colony));

        // Sans sel, la viande d'une vache (60 repas) ne servirait à rien pour dix colons (16 repas par jour) : on ne l'abat pas pour un surplus.
        colony.Cows = 4;
        colony.Stock.Add(ResourceType.Cows, 4);
        Husbandry.PlanSlaughter(colony, spring);
        Assert.Equal(0, colony.SlaughterOrders.GetValueOrDefault(ResourceType.Cows));

        // Vrai besoin : plus de vivres du tout. Les bêtes au-delà de la paire passent à la casserole, jamais la paire elle-même.
        colony.Stock.TryTake(ResourceType.Grain, colony.Stock.Get(ResourceType.Grain));
        colony.Stock.TryTake(ResourceType.Food, colony.Stock.Get(ResourceType.Food));
        colony.Stock.TryTake(ResourceType.Chickens, colony.Stock.Get(ResourceType.Chickens));
        colony.Stock.TryTake(ResourceType.Cows, colony.Stock.Get(ResourceType.Cows));
        colony.Chickens = 3; colony.Sheep = 3; colony.Cows = 2;
        Husbandry.PlanSlaughter(colony, spring);
        Assert.True(Husbandry.SlaughterPending(colony));
        Assert.DoesNotContain(ResourceType.Cows, colony.SlaughterOrders.Keys);
        Assert.All(colony.SlaughterOrders, order => Assert.True(order.Value <= Husbandry.Available(colony, order.Key)));
        Assert.Equal(0, Husbandry.Slaughter(colony, ResourceType.Cows));
        Assert.Equal(2, colony.Cows);
        Assert.True(Husbandry.MeatYield(ResourceType.Cows) >= 60);
    }

    [Fact]
    public void Le_sel_garde_la_viande_indefiniment_sinon_elle_pourrit_apres_trois_jours_et_sept_avec_un_entrepot()
    {
        (WorldState world, Colony colony) = Village();
        colony.Stock.Add(ResourceType.Meat, 60);
        colony.Stock.Add(ResourceType.Salt, 4);
        Civic.PreserveMeat(colony, ClockAtDay(0));   // 32 viandes salées, 28 fraîches (1 jour)
        Assert.Equal(32, colony.Stock.Get(ResourceType.SaltedMeat));
        Assert.Equal(0, colony.Stock.Get(ResourceType.Salt));
        Assert.Equal(28, colony.Stock.Get(ResourceType.Meat));

        Civic.PreserveMeat(colony, ClockAtDay(1));   // 2 jours : intacte
        Assert.Equal(28, colony.Stock.Get(ResourceType.Meat));
        Civic.PreserveMeat(colony, ClockAtDay(2));   // 3 jours : elle commence à pourrir
        Assert.Equal(14, colony.Stock.Get(ResourceType.Meat));
        for (int day = 3; day < 13; day++)
            Civic.PreserveMeat(colony, ClockAtDay(day));
        Assert.Equal(0, colony.Stock.Get(ResourceType.Meat));
        Assert.Equal(32, colony.Stock.Get(ResourceType.SaltedMeat));   // la viande salée, elle, ne se gâte jamais
        Assert.Contains(ResourceType.SaltedMeat, Economy.Tradable);

        // Avec un entrepôt, elle se garde sept jours ; et une viande plus jeune est mangée après la plus vieille.
        Build(world, colony, BuildingType.Storehouse);
        Assert.Equal(7, Civic.MeatShelfDays(colony));
        colony.Stock.Add(ResourceType.Meat, 20);
        for (int day = 0; day < 6; day++)
            Civic.PreserveMeat(colony, ClockAtDay(day));
        Assert.Equal(20, colony.Stock.Get(ResourceType.Meat));
        Civic.PreserveMeat(colony, ClockAtDay(6));
        Assert.Equal(10, colony.Stock.Get(ResourceType.Meat));

        colony.Stock.Add(ResourceType.Meat, 5);   // un lot tout frais
        Assert.True(colony.Stock.TryTake(ResourceType.Meat, 10));   // on mange d'abord le vieux lot
        Assert.Equal(0, colony.Stock.MeatAtLeast(1));
        Assert.Equal(5, colony.Stock.Get(ResourceType.Meat));
    }

    [Fact]
    public void Une_colonie_peut_avoir_plusieurs_enclos_quand_le_troupeau_deborde()
    {
        (WorldState world, Colony colony) = Village(16);
        // L'emplacement des bâtiments n'importe pas ici : on les pose achevés où l'on veut.
        void Finished(BuildingType type) => colony.Buildings.Add(new Building(type, 2, 2 + 3 * colony.Buildings.Count) { Progress = 1f });
        foreach (BuildingType type in new[] { BuildingType.Pen, BuildingType.Well, BuildingType.Loom, BuildingType.Storehouse,
                     BuildingType.Infirmary, BuildingType.Market, BuildingType.Tavern, BuildingType.Cask, BuildingType.School })
            Finished(type);
        colony.Sensors = null;
        colony.Stock.Add(ResourceType.Grain, 200);
        Husbandry.OnPenBuilt(colony, world.Clock);
        Assert.Null(Civic.NextToBuild(colony));   // le troupeau n'a pas besoin de place

        colony.Chickens = Husbandry.PenCapacity;
        colony.Stock.Add(ResourceType.Chickens, Husbandry.MaxSpareAnimals);
        Assert.True(Husbandry.Saturated(colony));
        Assert.Equal(BuildingType.Pen, Civic.NextToBuild(colony));

        Finished(BuildingType.Pen);
        Assert.Equal(2, Husbandry.Pens(colony));
        Assert.Equal((16, 8), (Husbandry.CapacityOf(colony, ResourceType.Chickens), Husbandry.CapacityOf(colony, ResourceType.Cows)));
        Husbandry.Daily(colony, ClockAtDay(2));   // les petits en réserve entrent dans le nouvel enclos
        Assert.True(colony.Chickens >= Husbandry.PenCapacity + Husbandry.MaxSpareAnimals);
        Assert.NotEqual(BuildingType.Pen, Civic.NextToBuild(colony));   // 16 habitants : deux enclos suffisent
        Assert.NotNull(Husbandry.NearestPen(colony, colony.CampX, colony.CampY));
    }

    [Fact]
    public void La_biere_fermente_cinq_jours_dans_un_fut_de_cinquante_cereales_et_donne_quarante_chopes_a_la_taverne()
    {
        (WorldState world, Colony colony) = Village();
        colony.Sensors = null;
        void Finished(BuildingType type) => colony.Buildings.Add(new Building(type, 2, 2 + 3 * colony.Buildings.Count) { Progress = 1f });
        Assert.Null(Cuisine.PickBrew(colony));   // ni taverne ni fût
        Assert.Equal((6, 0), (new Building(BuildingType.Cask, 0, 0).WoodRequired, new Building(BuildingType.Cask, 0, 0).StoneRequired));   // un peu de bois, pas de pierre

        Finished(BuildingType.Tavern);
        Assert.Equal(1, Cuisine.CasksWanted(colony));
        Assert.True(Cuisine.WantsCask(colony));
        Finished(BuildingType.Cask);
        Assert.False(Cuisine.WantsCask(colony));

        // 50 céréales par fût, et l'on garde trois jours de repas intacts : 30 ne suffisent pas, 120 si.
        colony.Stock.Add(ResourceType.Grain, 30);
        Assert.Null(Cuisine.PickBrew(colony));
        colony.Stock.Add(ResourceType.Grain, 120);
        var brew = Cuisine.PickBrew(colony)!.Value;
        Assert.Equal(BuildingType.Cask, brew.Workshop.Type);
        Assert.Equal((ResourceType.Beer, 40, 50), (brew.Recipe.Output, brew.Recipe.OutputAmount, brew.Recipe.Inputs[0].Amount));
        Assert.True(ToolChain.TryTakeInputs(colony, brew.Recipe, out _));
        Assert.Equal(100, colony.Stock.Get(ResourceType.Grain));

        // Le fût fermente : rien pendant cinq jours, puis 40 chopes à la taverne, et le coût de la fournée entre dans le registre.
        Cuisine.StartBrewing(colony, brew.Workshop, 60.0, ClockAtDay(0));
        Assert.True(brew.Workshop.IsBrewing);
        Assert.Null(Cuisine.PickBrew(colony));   // plus de fût vide, et 40 chopes en route
        Cuisine.TickCasks(colony, ClockAtDay(4));
        Assert.Equal(0, colony.Stock.Get(ResourceType.Beer));
        Cuisine.TickCasks(colony, ClockAtDay(5));
        Assert.Equal(40, colony.Stock.Get(ResourceType.Beer));
        Assert.False(brew.Workshop.IsBrewing);
        Assert.Equal(1.5, colony.Labor.HoursPerUnit(ResourceType.Beer)!.Value);   // 60 h pour 40 chopes
        Assert.Equal((50 * 1.5 + 2.0) / 40, Economy.BaselineCost(ResourceType.Beer));
        Assert.Contains(ResourceType.Beer, Economy.Tradable);

        // L'entrain d'une chope dure cinq jours ; on n'en reprend une que quand il est retombé sous la moitié.
        Colonist drinker = colony.Members[0];
        drinker.Needs.Food = 0.5f;
        float mood = drinker.Needs.Mood;
        Cuisine.Drink(drinker, world.Clock);
        Assert.Equal((1f, 39), (drinker.Needs.BeerCheer, colony.Stock.Get(ResourceType.Beer)));
        Assert.True(drinker.Needs.Mood > mood + 0.1f);
        Assert.True(Cuisine.BrewDays >= 5 && Cuisine.BeerDays >= 5);
        Cuisine.Drink(drinker, world.Clock);
        Assert.Equal(39, colony.Stock.Get(ResourceType.Beer));
        drinker.Needs.BeerCheer = 0.4f;
        Cuisine.Drink(drinker, world.Clock);
        Assert.Equal((1f, 38), (drinker.Needs.BeerCheer, colony.Stock.Get(ResourceType.Beer)));
    }

    [Fact]
    public void Le_gateau_se_partage_et_remonte_le_moral_le_ragout_donne_un_coup_de_fouet()
    {
        (WorldState world, Colony colony) = Village();
        var stock = new Stockpile();
        stock.Add(ResourceType.Cake, 1);
        stock.Add(ResourceType.Grain, 10);
        for (int slice = 0; slice < Stockpile.PortionsPerCake; slice++)
        {
            Assert.True(stock.TryTakeMeal(out float value, out ResourceType? dish));
            Assert.Equal((1f, ResourceType.Cake), (value, dish!.Value));
        }
        Assert.True(stock.TryTakeMeal(out _, out ResourceType? plain) && plain is null);

        Colonist eater = colony.Members[0];
        eater.Needs.Food = 0.5f;
        float moodBefore = eater.Needs.Mood;
        Cuisine.Savor(eater, ResourceType.Cake, world.Clock);
        Assert.True(eater.Needs.Mood > moodBefore + 0.1f && !eater.IsBoosted);
        Cuisine.Savor(eater, ResourceType.Stew, world.Clock);
        Assert.True(eater.IsBoosted);
        Assert.Equal(1f + Cuisine.StewBoost, Cuisine.BoostFactor(eater));

        // Les recettes : le gâteau au four, le ragoût avec de la viande salée à défaut de fraîche.
        colony.Sensors = null;
        Build(world, colony, BuildingType.Oven);
        colony.Stock.Add(ResourceType.Eggs, 4);
        colony.Stock.Add(ResourceType.Milk, 4);
        colony.Stock.Add(ResourceType.Flour, 4);
        Assert.Equal(ResourceType.Cake, Cuisine.PickCake(colony)!.Value.Recipe.Output);
        colony.Stock.Add(ResourceType.SaltedMeat, 4);
        colony.Stock.Add(ResourceType.Grain, 20);
        var stew = Cuisine.PickStew(colony)!.Value;
        Assert.Equal(ResourceType.Stew, stew.Recipe.Output);
        Assert.True(ToolChain.TryTakeInputs(colony, stew.Recipe, out _));
        Assert.Equal(2, colony.Stock.Get(ResourceType.SaltedMeat));
    }

    // ---------- 2. Commerce ----------

    [Fact]
    public void Chaque_region_produit_sa_denree_le_marche_la_troque_et_le_colporteur_vend_les_autres()
    {
        Assert.Equal(ResourceType.Salt, Specialties.NativeOf(Biome.Desert));
        Assert.Equal(ResourceType.Spices, Specialties.NativeOf(Biome.Savanna));
        Assert.Equal(ResourceType.Hardwood, Specialties.NativeOf(Biome.TropicalForest));

        (WorldState world, Colony colony) = Village();
        Assert.Null(Specialties.PickMarketJob(colony));
        Build(world, colony, BuildingType.Market);
        Assert.NotNull(Specialties.PickMarketJob(colony));
        Assert.Equal(Specialties.NativeOf(colony), Crafting.RecipeFor(colony, BuildingType.Market).Output);

        int coins = colony.Stock.Get(ResourceType.Coins);
        Events.Peddler(world, colony);
        Assert.True(colony.Stock.Get(ResourceType.Coins) < coins);
        Assert.Equal(coins - colony.Stock.Get(ResourceType.Coins), world.CoinsLostToEvents);
        Assert.Contains(Specialties.Goods, g => g != Specialties.NativeOf(colony) && colony.Stock.Get(g) > 0);
    }

    // ---------- 3. Santé et saisons ----------

    [Fact]
    public void Un_malade_guerit_et_un_soin_ecarte_tout_danger_de_mort()
    {
        (WorldState world, Colony colony) = Village();
        Colonist patient = colony.Members[0];
        Health.Fall(colony, patient, Ailment.Sick, 20, world.Clock, "malade");
        Assert.Equal(1, Health.PatientCount(colony));
        Assert.True(patient.Needs.Mood < 0.95f);

        Health.Treat(colony);
        for (int hour = 0; hour < 24; hour++)
            Health.Hourly(world, colony);
        Assert.Equal(Ailment.None, patient.Ailment);
        Assert.Contains(patient, colony.Members);
    }

    [Fact]
    public void Les_regions_froides_brulent_plus_de_bois_et_la_secheresse_gele_les_champs_non_irrigues()
    {
        (WorldState world, Colony colony) = Village();
        Assert.Equal(1f, Climate.WoodFactor(colony, Season.Printemps));
        colony.Map.Biome = Biome.Tundra;
        Assert.True(Climate.WoodFactor(colony, Season.Hiver) > 1.4f);
        colony.ColdSnapDaysLeft = 2;
        Assert.True(Climate.WoodFactor(colony, Season.Hiver) > 2.5f);

        colony.Map.Biome = Biome.TemperateForest;
        Farming.PlanField(colony.Map, colony, 40, 40);
        FieldPlot plot = Farming.Plots(colony).First();
        plot.Stage = CropStage.Growing;
        colony.DroughtDaysLeft = 3;
        Farming.DailyUpdate(colony, ClockAtDay(8));
        if (!colony.Map.IsIrrigated(plot.X, plot.Y))
            Assert.Equal(0f, plot.Growth);
    }

    // ---------- 4. Bâtiments de village ----------

    [Fact]
    public void Le_village_bati_dans_l_ordre_et_l_entrepot_protege_les_vivres()
    {
        (WorldState world, Colony colony) = Village(10);
        Farming.PlanField(colony.Map, colony, 40, 40);
        Assert.Equal(BuildingType.Pen, Civic.NextToBuild(colony));
        Build(world, colony, BuildingType.Pen);
        Assert.Equal(BuildingType.Well, Civic.NextToBuild(colony));

        colony.Stock.Add(ResourceType.Grain, 400);
        int before = colony.Stock.Get(ResourceType.Grain);
        Civic.Daily(colony, world.Clock);
        Assert.True(colony.Stock.Get(ResourceType.Grain) < before, "Au-delà de la capacité, le grain pourrit.");

        int capacity = Civic.StorageCapacity(colony);
        Build(world, colony, BuildingType.Storehouse);
        Assert.True(Civic.StorageCapacity(colony) >= capacity + 100);
        Assert.Equal(Civic.SchoolLearningBonus, Civic.LearningBonus(BuiltSchool(world, colony)));
    }

    private static Colony BuiltSchool(WorldState world, Colony colony)
    {
        Build(world, colony, BuildingType.School);
        return colony;
    }

    // ---------- 5. Événements et objectifs ----------

    [Fact]
    public void Un_puits_eteint_l_incendie_et_les_jalons_sont_celebres_une_seule_fois()
    {
        (WorldState world, Colony colony) = Village(10);
        Build(world, colony, BuildingType.Tavern);
        Build(world, colony, BuildingType.Well);
        int buildings = colony.Buildings.Count;
        Events.Fire(world, colony);
        Assert.Equal(buildings, colony.Buildings.Count);

        colony.Buildings.RemoveAll(b => b.Type == BuildingType.Well);
        buildings = colony.Buildings.Count;
        Events.Fire(world, colony);
        Assert.Equal(buildings - 1, colony.Buildings.Count);

        Milestones.Daily(colony, world.Clock);
        Assert.True(colony.Achievements.ContainsKey("pop10"));
        int thoughts = colony.Thoughts.Count(t => t.Text.Contains("Dix habitants"));
        Milestones.Daily(colony, world.Clock);
        Assert.Equal(thoughts, colony.Thoughts.Count(t => t.Text.Contains("Dix habitants")));
    }

    [Fact]
    public void Une_petite_colonie_vit_deux_ans_avec_tous_les_nouveaux_systemes_sans_famine_ni_incoherence()
    {
        var world = new WorldState(2027, startingColonists: 12, colonyCount: 2);
        var watches = world.Colonies.ToDictionary(c => c, _ => new StarvationWatch());
        for (long i = 0; i < 2 * TimeConstants.TicksPerYear; i++)
        {
            world.Step();
            if (i % TimeConstants.TicksPerDay != 0)
                continue;
            foreach (Colony colony in world.Colonies)
            {
                watches[colony].Observe(colony);
                Assert.All(colony.Members, m => Assert.True(m.Needs.Mood is >= 0f and <= 1f));
                Assert.InRange(colony.Chickens, 0, Husbandry.Capacity(colony) + 1);
            }
        }
        Assert.All(world.Colonies, c => Assert.Contains(c.Buildings, b => b.IsCivic || b.IsHut));
    }
}
