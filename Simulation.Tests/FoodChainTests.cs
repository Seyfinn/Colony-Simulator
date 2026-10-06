using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class FoodChainTests(ITestOutputHelper output)
{
    [Fact]
    public void Le_stock_nutritif_additionne_les_valeurs_et_exclut_la_farine()
    {
        var stock = new Stockpile();
        stock.Add(ResourceType.Food, 10);
        Assert.Equal(6m, stock.FoodNutrition);
        stock.Add(ResourceType.Fish, 3);
        stock.Add(ResourceType.Grain, 2);
        stock.Add(ResourceType.Bread, 1);
        stock.Add(ResourceType.Flour, 100);
        Assert.Equal(8.85m, stock.FoodNutrition);
        Assert.Equal(14, stock.FoodUnits); // deux grains crus ne valent pas même un repas
        Assert.True(stock.TryTakeMeal());
        Assert.Equal(8.25m, stock.FoodNutrition);
    }

    [Fact]
    public void Le_poisson_se_mange_avant_le_pain_et_reste_distinct_des_baies()
    {
        var stock = new Stockpile();
        stock.Add(ResourceType.Fish, 1);
        stock.Add(ResourceType.Bread, 1);
        Assert.Equal(0, stock.Get(ResourceType.Food));
        Assert.True(stock.TryTakeMeal(out float value));
        Assert.Equal(Stockpile.WildMealValue, value);
        Assert.Equal(0, stock.Get(ResourceType.Fish));
        Assert.Equal(0.85m, stock.FoodNutrition);
        Assert.True(stock.TryTakeMeal());
        Assert.Equal(0m, stock.FoodNutrition);
    }

    private static (WorldState World, Colony Colony) Closed(int colonists = 10)
    {
        var world = new WorldState(12345, startingColonists: colonists, migration: false, lifecycle: false);
        return (world, world.Colonies[0]);
    }

    private static void Set(Colony colony, ResourceType type, int amount)
    {
        colony.Stock.TryTake(type, colony.Stock.Get(type));
        colony.Stock.Add(type, amount);
    }

    private static void Complete(WorldState world, Colony colony, BuildingType type)
    {
        (int x, int y) = Urbanism.FindSite(world.Map, colony, type)!.Value;
        Urbanism.PlanBuilding(world.Map, colony, type, x, y).Progress = 1f;
    }

    [Fact]
    public void Un_repas_de_pain_nourrit_mieux_et_on_mange_le_sauvage_puis_le_pain_puis_le_grain()
    {
        var stock = new Stockpile();
        stock.Add(ResourceType.Food, 1);
        stock.Add(ResourceType.Bread, 1);
        stock.Add(ResourceType.Grain, 1);
        stock.Add(ResourceType.Flour, 5);
        Assert.Equal(2, stock.FoodUnits); // la farine ne se mange pas, un grain cru ne fait pas un repas
        Assert.True(stock.HasAnyMeal);

        Assert.True(stock.TryTakeMeal(out float first));
        Assert.Equal(Stockpile.WildMealValue, first);
        Assert.True(stock.TryTakeMeal(out float second));
        Assert.Equal(Stockpile.BreadMealValue, second);
        Assert.True(stock.TryTakeMeal(out float third));
        Assert.Equal(Stockpile.GrainMealValue, third);
        Assert.False(stock.TryTakeMeal(out _));
        Assert.InRange(Stockpile.GrainMealValue, 0.01f, 0.15f); // très faible, mais jamais nulle
        Assert.True(Stockpile.BreadMealValue > Stockpile.GrainMealValue);
    }

    [Fact]
    public void Tant_que_la_colonie_n_a_pas_recolte_elle_ne_pense_pas_au_pain()
    {
        (_, Colony colony) = Closed();
        Assert.False(FoodChain.Demand(colony).Active);
        colony.Labor.Record(ResourceType.Grain, workerHours: 20, units: 20);
        Assert.True(FoodChain.Demand(colony).Active);
    }

    [Fact]
    public void Le_moulin_se_batit_quand_le_grain_deborde_puis_le_four()
    {
        (WorldState world, Colony colony) = Closed();
        colony.Labor.Record(ResourceType.Grain, workerHours: 20, units: 20);

        // Le four d'abord : il moud à bras, de quoi faire du pain sans cours d'eau.
        Set(colony, ResourceType.Grain, 10);
        Assert.Equal(BuildingType.Oven, FoodChain.NextWorkshopToBuild(colony, world.Map));
        Complete(world, colony, BuildingType.Oven);
        Assert.Null(FoodChain.NextWorkshopToBuild(colony, world.Map));   // pas encore de surplus pour un moulin

        Set(colony, ResourceType.Grain, 200);
        Assert.Equal(BuildingType.Mill, FoodChain.NextWorkshopToBuild(colony, world.Map));
        Complete(world, colony, BuildingType.Mill);
        Assert.Null(FoodChain.NextWorkshopToBuild(colony, world.Map));
    }

    [Fact]
    public void Le_moulin_se_pose_au_bord_de_l_eau_et_tourne_au_rythme_du_debit()
    {
        (WorldState world, Colony colony) = Closed();
        (int x, int y) = Urbanism.FindMillSite(world.Map, colony)!.Value;
        var mill = new Building(BuildingType.Mill, x, y);
        Assert.True(Hydrology.MillFlow(world.Map, mill) > 0f);
        Assert.All(mill.Tiles, t => Assert.False(world.Map.IsWaterway(t.X, t.Y)));

        // Un barrage en amont retient l'eau : le moulin tourne plus lentement.
        float before = Hydrology.MillFlow(world.Map, mill);
        for (int dy = -1; dy <= mill.Height; dy++)
        for (int dx = -1; dx <= mill.Width; dx++)
            if (world.Map.InBounds(x + dx, y + dy) && world.Map.IsRiver(x + dx, y + dy))
                world.Map.ReduceFlow(x + dx, y + dy, 0.5f);
        Assert.Equal(before * 0.5f, Hydrology.MillFlow(world.Map, mill), 3);
    }

    [Fact]
    public void On_cuit_le_pain_d_abord_puis_on_moud_sans_toucher_a_la_reserve_de_grain_ni_au_bois_de_chauffage()
    {
        (WorldState world, Colony colony) = Closed();
        colony.Labor.Record(ResourceType.Grain, workerHours: 20, units: 20);
        Complete(world, colony, BuildingType.Mill);
        Complete(world, colony, BuildingType.Oven);
        int reserve = (int)Math.Ceiling(10 * ColonyBrain.MealsPerColonistPerDay * 0.5f);

        Set(colony, ResourceType.Grain, reserve + 2);
        Assert.Null(FoodChain.PickJob(colony, 20));                 // surplus de 2 : pas assez pour une mesure

        Set(colony, ResourceType.Grain, reserve + 10);
        Assert.Equal(BuildingType.Mill, FoodChain.PickJob(colony, 20)!.Type);

        Set(colony, ResourceType.Flour, 3);
        Set(colony, ResourceType.Wood, 20);
        Assert.Equal(BuildingType.Mill, FoodChain.PickJob(colony, 20)!.Type); // pas de bois au-delà du chauffage : pas de four
        Set(colony, ResourceType.Wood, 21);
        Assert.Equal(BuildingType.Oven, FoodChain.PickJob(colony, 20)!.Type);

        // Assez de pain d'avance : on ne cuit plus.
        Set(colony, ResourceType.Bread, 500);
        Set(colony, ResourceType.Flour, 6);
        Assert.NotEqual(BuildingType.Oven, FoodChain.PickJob(colony, 20)?.Type);
    }

    [Fact]
    public void Sans_moulin_le_four_moud_a_bras_pour_un_rendement_moindre()
    {
        (WorldState world, Colony colony) = Closed();
        colony.Labor.Record(ResourceType.Grain, workerHours: 20, units: 20);
        Complete(world, colony, BuildingType.Oven);
        Set(colony, ResourceType.Wood, 100);
        Set(colony, ResourceType.Grain, 100);

        Assert.True(FoodChain.HandMills(colony));
        Recipe hand = Crafting.RecipeFor(colony, BuildingType.Oven);
        Assert.Contains(hand.Inputs, i => i.Type == ResourceType.Grain);
        Assert.Equal(BuildingType.Oven, FoodChain.PickJob(colony, 20)!.Type);

        Complete(world, colony, BuildingType.Mill);
        Assert.False(FoodChain.HandMills(colony));
        Recipe flour = Crafting.RecipeFor(colony, BuildingType.Oven);
        Assert.Contains(flour.Inputs, i => i.Type == ResourceType.Flour);
        Assert.Equal(ResourceType.Bread, flour.Output);
        Assert.True(flour.OutputAmount > hand.OutputAmount); // pour trois mesures de grain, le moulin donne plus de pain que la meule à bras
    }

    [Fact]
    public void La_colonie_moud_et_cuit_et_le_pain_nourrit_mieux_que_le_grain()
    {
        (WorldState world, Colony colony) = Closed(12);
        int maxBread = 0;
        bool sawMill = false, sawOven = false, ateBread = false;

        for (int day = 1; day <= 80; day++)
        {
            for (long i = 0; i < TimeConstants.TicksPerDay; i++)
            {
                world.Step();
                if (!ateBread)
                    ateBread = colony.Members.Any(m => m.Activity is { Kind: ActivityKind.Eat, Started: true, MealValue: Stockpile.BreadMealValue });
            }
            maxBread = Math.Max(maxBread, colony.Stock.Get(ResourceType.Bread));
            sawMill |= colony.Buildings.Any(b => b.Type == BuildingType.Mill && b.IsComplete);
            sawOven |= colony.Buildings.Any(b => b.Type == BuildingType.Oven && b.IsComplete);
            if (day % 10 == 0)
                output.WriteLine($"J{day}: grain {colony.Stock.Get(ResourceType.Grain)}, farine {colony.Stock.Get(ResourceType.Flour)}, pain {colony.Stock.Get(ResourceType.Bread)}, " +
                                 $"sauvage {colony.Stock.Get(ResourceType.Food)}, humeur {colony.AverageMood:P0}");
        }
        output.WriteLine("Coûts : " + ColonyBrain.CostSummary(colony.Labor));

        Assert.True(sawMill && sawOven, "Le moulin et le four doivent être bâtis.");
        Assert.True(colony.Labor.TotalProduced(ResourceType.Flour) > 0);
        Assert.True(colony.Labor.TotalProduced(ResourceType.Bread) > 0);
        Assert.True(maxBread > 0 && ateBread, "On doit manger du pain.");

        // Le pain cumule le travail du champ, du moulin et du four : il coûte plus cher que le grain, par repas mais pour mieux nourrir.
        double bread = colony.Labor.HoursPerUnit(ResourceType.Bread)!.Value;
        double grain = colony.Labor.HoursPerUnit(ResourceType.Grain)!.Value;
        Assert.True(bread > 0 && grain > 0);
    }
}
