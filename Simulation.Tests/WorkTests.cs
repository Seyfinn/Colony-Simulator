using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class WorkTests(ITestOutputHelper output)
{
    [Fact]
    public void La_colonie_coupe_du_bois_et_creuse_sa_carriere()
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Assert.NotNull(colony.Quarry);
        (int qx, int qy) = colony.Quarry.Value;

        int ElevationAroundQuarry()
        {
            int sum = 0;
            for (int dy = -10; dy <= 10; dy++)
            for (int dx = -10; dx <= 10; dx++)
                if (world.Map.InBounds(qx + dx, qy + dy))
                    sum += world.Map.GetElevation(qx + dx, qy + dy);
            return sum;
        }

        // La carrière n'est exploitée qu'une fois la survie et le logement assurés : on laisse 12 jours.
        int elevationBefore = ElevationAroundQuarry();
        for (long i = 0; i < 12L * TimeConstants.TicksPerDay; i++)
            world.Step();

        int layersMined = elevationBefore - ElevationAroundQuarry();
        int stumps = 0;
        for (int y = 0; y < world.Map.Height; y++)
        for (int x = 0; x < world.Map.Width; x++)
            if (world.Map.GetFlora(x, y) == FloraType.Stump) stumps++;

        output.WriteLine($"Carrière en {colony.Quarry} : {layersMined} couches minées, {stumps} souches");
        output.WriteLine($"Stock : nourriture {colony.Stock.Get(ResourceType.Food)}, bois {colony.Stock.Get(ResourceType.Wood)}, " +
                         $"pierre {colony.Stock.Get(ResourceType.Stone)}, fer {colony.Stock.Get(ResourceType.IronOre)}");
        foreach (Thought thought in colony.Thoughts)
            output.WriteLine($"  [{thought.Ticks / TimeConstants.TicksPerDay}] {thought.Text}");

        Assert.True(stumps > 5, "La colonie devrait avoir coupé du bois.");
        Assert.True(layersMined > 3, "La carrière devrait s'être creusée.");
        Assert.True(colony.Stock.Get(ResourceType.Stone) + colony.Stock.Get(ResourceType.IronOre) > 5);
        Assert.All(colony.Members, c => Assert.True(c.Needs.Food > 0.1f, $"{c.Name} meurt de faim."));
    }

    [Fact]
    public void Les_postes_vont_aux_plus_doues()
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        colony.WorkShares[WorkSector.Food] = 0.5f;
        colony.WorkShares[WorkSector.Wood] = 0.3f;
        colony.WorkShares[WorkSector.Stone] = 0.2f;
        colony.WorkShares[WorkSector.Free] = 0f;
        colony.AssignSectors();

        Assert.Equal(10, colony.Members.Count(m => m.Sector == WorkSector.Food));
        Assert.Equal(6, colony.Members.Count(m => m.Sector == WorkSector.Wood));
        Assert.Equal(4, colony.Members.Count(m => m.Sector == WorkSector.Stone));

        float FitFor(Colonist c, SkillType s) => c.Skills.Level(s) + c.Skills.Talent(s) * 4f;
        float minersFit = colony.Members.Where(m => m.Sector == WorkSector.Stone).Average(m => FitFor(m, SkillType.Mining));
        float othersFit = colony.Members.Where(m => m.Sector != WorkSector.Stone).Average(m => FitFor(m, SkillType.Mining));
        Assert.True(minersFit > othersFit, "Les mineurs devraient être plus doués pour la mine que les autres.");
    }
}
