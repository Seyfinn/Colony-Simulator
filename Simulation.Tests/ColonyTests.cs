using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class ColonyTests(ITestOutputHelper output)
{
    [Fact]
    public void La_colonie_est_fondee_sur_un_site_praticable_avec_20_colons()
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false, lifecycle: false);
        Colony colony = Assert.Single(world.Colonies);
        Assert.Equal(20, colony.Members.Count);
        Assert.True(world.Map.IsWalkable(colony.CampX, colony.CampY));
        Assert.True(colony.GatherSpots.Count >= 20, "Il faut assez de place autour du feu pour dormir.");
    }

    [Fact]
    public void Les_colons_dorment_la_nuit_mangent_et_cueillent_pendant_cinq_jours()
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        int foodAtStart = colony.Stock.Get(ResourceType.Food);
        int sleepingAt3am = 0;

        long end = world.Clock.Ticks + 5L * TimeConstants.TicksPerDay;
        while (world.Clock.Ticks < end)
        {
            world.Step();
            if (world.Clock.Hour == 3 && world.Clock.Minute == 0)
                sleepingAt3am = Math.Max(sleepingAt3am, colony.Members.Count(m => m.IsSleeping));
            foreach (Colonist c in colony.Members)
                Assert.True(world.Map.IsWalkable(c.TileX, c.TileY), $"{c.Name} est sorti de la terre ferme.");
        }

        output.WriteLine($"Nourriture : {foodAtStart} → {colony.Stock.Get(ResourceType.Food)}, " +
                         $"dormeurs à 3 h : {sleepingAt3am}, humeur moyenne : {colony.AverageMood:P0}");
        foreach (Colonist c in colony.Members)
            output.WriteLine($"  {c.Name} : faim {c.Needs.Food:P0}, repos {c.Needs.Rest:P0}, détente {c.Needs.Leisure:P0}, {c.Activity?.Kind}");

        Assert.True(sleepingAt3am >= 16, "Presque tout le monde devrait dormir à 3 h du matin.");
        Assert.All(colony.Members, c => Assert.True(c.Needs.Rest > 0.2f, $"{c.Name} est épuisé."));
        Assert.All(colony.Members, c => Assert.True(c.Needs.Food > 0.1f, $"{c.Name} meurt de faim."));
    }
}
