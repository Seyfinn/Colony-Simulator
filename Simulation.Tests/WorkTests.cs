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
        var world = new WorldState(12345);
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

        int elevationBefore = ElevationAroundQuarry();
        float miningBefore = colony.Members.Where(m => m.Sector == WorkSector.Stone).Average(m => m.Skills.Level(SkillType.Mining));

        for (long i = 0; i < 3L * TimeConstants.TicksPerDay; i++)
            world.Step();

        int layersMined = elevationBefore - ElevationAroundQuarry();
        float miningAfter = colony.Members.Where(m => m.Sector == WorkSector.Stone).Average(m => m.Skills.Level(SkillType.Mining));
        int stumps = 0;
        for (int y = 0; y < world.Map.Height; y++)
        for (int x = 0; x < world.Map.Width; x++)
            if (world.Map.GetFlora(x, y) == FloraType.Stump) stumps++;

        output.WriteLine($"Carrière en {colony.Quarry} : {layersMined} couches minées");
        output.WriteLine($"Stock : nourriture {colony.Stock.Get(ResourceType.Food)}, bois {colony.Stock.Get(ResourceType.Wood)}, " +
                         $"pierre {colony.Stock.Get(ResourceType.Stone)}, fer {colony.Stock.Get(ResourceType.IronOre)}, souches {stumps}");
        output.WriteLine($"Minage moyen des mineurs : {miningBefore:F1} → {miningAfter:F1}");
        foreach (WorkSector sector in WorkSectors.All)
            output.WriteLine($"  {sector} : {colony.Members.Count(m => m.Sector == sector)} colons");

        Assert.True(colony.Stock.Get(ResourceType.Wood) > 20, "La colonie devrait avoir coupé du bois.");
        Assert.True(layersMined > 5, "La carrière devrait s'être creusée.");
        Assert.True(colony.Stock.Get(ResourceType.Stone) + colony.Stock.Get(ResourceType.IronOre) > 10);
        Assert.True(miningAfter > miningBefore + 0.5f, "Les mineurs devraient progresser en minage.");
        Assert.All(colony.Members, c => Assert.True(c.Needs.Food > 0.1f, $"{c.Name} meurt de faim."));
    }

    [Fact]
    public void Les_postes_vont_aux_plus_doues()
    {
        var world = new WorldState(12345);
        Colony colony = world.Colonies[0];
        Assert.Equal(10, colony.Members.Count(m => m.Sector == WorkSector.Food));
        Assert.Equal(6, colony.Members.Count(m => m.Sector == WorkSector.Wood));
        Assert.Equal(4, colony.Members.Count(m => m.Sector == WorkSector.Stone));

        float FitFor(Colonist c, SkillType s) => c.Skills.Level(s) + c.Skills.Talent(s) * 4f;
        float minersFit = colony.Members.Where(m => m.Sector == WorkSector.Stone).Average(m => FitFor(m, SkillType.Mining));
        float othersFit = colony.Members.Where(m => m.Sector != WorkSector.Stone).Average(m => FitFor(m, SkillType.Mining));
        Assert.True(minersFit > othersFit, "Les mineurs devraient être plus doués pour la mine que les autres.");
    }
}
