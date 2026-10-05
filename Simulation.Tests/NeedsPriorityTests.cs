using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public class NeedsPriorityTests
{
    private static WorldState ColonieFermee() =>
        new(12345, startingColonists: 6, migration: false, lifecycle: false);

    private static void AvancerHabitant(WorldState world, Colonist habitant, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.Clock.Advance();
            ColonistAI.Tick(habitant, world);
        }
    }

    [Fact]
    public void Un_habitant_affame_et_epuise_mange_avant_de_retourner_dormir()
    {
        WorldState world = ColonieFermee();
        Colony colony = world.Colonies[0];
        Colonist habitant = colony.Members[0];
        (int x, int y) = colony.GatherSpots[0];
        habitant.X = x + 0.5f;
        habitant.Y = y + 0.5f;
        habitant.Needs.Food = 0.05f;
        habitant.Needs.Rest = 0.14f;
        int repas = colony.Stock.FoodUnits;

        AvancerHabitant(world, habitant, 1);
        Assert.Equal(ActivityKind.Eat, habitant.Activity?.Kind);
        AvancerHabitant(world, habitant, 5 * TimeConstants.TicksPerSecond);

        Assert.True(habitant.Needs.Food > 0.5f);
        Assert.Equal(repas - 1, colony.Stock.FoodUnits);
        Assert.Equal(ActivityKind.Sleep, habitant.Activity?.Kind);
    }

    [Fact]
    public void L_epuisement_n_interrompt_pas_un_repas_urgent_deja_preleve()
    {
        WorldState world = ColonieFermee();
        Colony colony = world.Colonies[0];
        Colonist habitant = colony.Members[0];
        Assert.True(colony.Stock.TryTakeMeal(out float valeur));
        int repas = colony.Stock.FoodUnits;
        habitant.Needs.Food = 0.01f;
        habitant.Needs.Rest = 0f;
        habitant.Activity = new Activity(ActivityKind.Eat, habitant.TileX, habitant.TileY, TimeConstants.TicksPerSecond)
        {
            Started = true,
            MealValue = valeur,
        };

        AvancerHabitant(world, habitant, TimeConstants.TicksPerSecond);

        Assert.True(habitant.Needs.Food > 0.5f);
        Assert.Equal(repas, colony.Stock.FoodUnits);
        AvancerHabitant(world, habitant, 1);
        Assert.Equal(ActivityKind.Sleep, habitant.Activity?.Kind);
    }

    [Fact]
    public void L_epuisement_n_interrompt_pas_la_cueillette_d_un_repas_urgent()
    {
        WorldState world = ColonieFermee();
        Colonist habitant = world.Colonies[0].Members[0];
        LocalMap map = habitant.Colony.Map;
        int x = habitant.TileX, y = habitant.TileY;
        map.SetGenerated(x, y, map.GetElevation(x, y), map.GetSoil(x, y), FloraType.Bush, 1f);
        int baies = map.GetBerries(x, y);
        habitant.Needs.Food = 0.01f;
        habitant.Needs.Rest = 0f;
        habitant.Activity = new Activity(ActivityKind.ForageToEat, x, y, TimeConstants.TicksPerSecond) { Started = true };

        AvancerHabitant(world, habitant, TimeConstants.TicksPerSecond);

        Assert.True(habitant.Needs.Food > 0.1f);
        Assert.True(map.GetBerries(x, y) < baies);
    }

    [Fact]
    public void La_graine_42_traverse_six_ans_sans_famine_ni_mort_de_faim()
    {
        var world = new WorldState(42, startingColonists: 8);
        Colony colony = world.Colonies[0];
        var famine = new StarvationWatch();
        for (long tick = 1; tick <= 6L * TimeConstants.TicksPerYear; tick++)
        {
            world.Step();
            if (tick % TimeConstants.TicksPerDay == 0)
                famine.Observe(colony);
        }

        Assert.Null(famine.Victim);
        Assert.DoesNotContain(colony.Graves, tombe => tombe.Cause == "faim");
    }
}
