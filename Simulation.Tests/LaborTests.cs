using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class LaborTests(ITestOutputHelper output)
{
    [Fact]
    public void La_moyenne_glissante_suit_les_conditions_recentes()
    {
        var ledger = new LaborLedger();
        ledger.Record(ResourceType.Wood, workerHours: 2, units: 4);
        Assert.Equal(0.5, ledger.HoursPerUnit(ResourceType.Wood)!.Value, 3);

        // La forêt s'éloigne : chaque bois coûte maintenant 1,5 h. La moyenne monte, progressivement.
        for (int i = 0; i < 30; i++)
            ledger.Record(ResourceType.Wood, workerHours: 6, units: 4);
        Assert.InRange(ledger.HoursPerUnit(ResourceType.Wood)!.Value, 1.4, 1.5);
        Assert.Equal(124, ledger.TotalProduced(ResourceType.Wood));
        Assert.Null(ledger.HoursPerUnit(ResourceType.Stone));
    }

    [Fact]
    public void La_colonie_mesure_ce_que_lui_coute_chaque_ressource()
    {
        var world = new WorldState(12345);
        Colony colony = world.Colonies[0];
        for (long i = 0; i < 12L * TimeConstants.TicksPerDay; i++)
            world.Step();

        output.WriteLine("Coûts : " + ColonyBrain.CostSummary(colony.Labor));
        output.WriteLine($"Produit : nourriture {colony.Labor.TotalProduced(ResourceType.Food)}, bois {colony.Labor.TotalProduced(ResourceType.Wood)}, " +
                         $"pierre {colony.Labor.TotalProduced(ResourceType.Stone)}");

        foreach (ResourceType type in new[] { ResourceType.Food, ResourceType.Wood, ResourceType.Stone })
        {
            double? hours = colony.Labor.HoursPerUnit(type);
            Assert.NotNull(hours);
            Assert.InRange(hours.Value, 0.05, 20);
        }
        Assert.NotNull(colony.Labor.HoursPerHut);
        Assert.Contains(colony.Thoughts, t => t.Text.StartsWith("Bilan de saison"));
    }
}
