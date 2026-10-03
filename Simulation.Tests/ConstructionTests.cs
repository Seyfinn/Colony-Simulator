using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class ConstructionTests(ITestOutputHelper output)
{
    [Fact]
    public void Quand_la_survie_est_assuree_la_colonie_bati_une_hutte_et_s_y_installe()
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false);
        Colony colony = world.Colonies[0];
        colony.Stock.Add(ResourceType.Food, 1000);
        colony.Stock.Add(ResourceType.Wood, 100);

        for (long i = 0; i < 2L * TimeConstants.TicksPerDay; i++)
            world.Step();

        foreach (Thought thought in colony.Thoughts)
            output.WriteLine($"[jour {thought.Ticks / TimeConstants.TicksPerDay}] {thought.Text}");

        Building first = Assert.IsType<Building>(colony.Buildings.FirstOrDefault());
        Assert.True(first.IsComplete, $"La première hutte devrait être achevée (avancement {first.Progress:P0}).");
        Assert.Equal(Building.HutCapacity, first.Residents.Count);
        Assert.All(first.Tiles, t => Assert.True(world.Map.IsWalkable(t.X, t.Y)));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("hutte"));
    }

    [Fact]
    public void Les_huttes_ne_se_chevauchent_pas_et_laissent_un_passage()
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false);
        Colony colony = world.Colonies[0];
        for (int i = 0; i < 6; i++)
        {
            (int x, int y) = Assert.IsType<(int, int)>(Urbanism.FindHutSite(world.Map, colony));
            Urbanism.PlanHut(world.Map, colony, x, y);
        }

        foreach (Building a in colony.Buildings)
        foreach (Building b in colony.Buildings.Where(b => b != a))
        {
            bool tooClose = a.X < b.X + b.Width + 1 && a.X + a.Width > b.X - 1 && a.Y < b.Y + b.Height + 1 && a.Y + a.Height > b.Y - 1;
            Assert.False(tooClose, $"Huttes trop proches en ({a.X},{a.Y}) et ({b.X},{b.Y}).");
        }
    }
}
