using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

/// <summary>
/// L'objectif de croissance du joueur : une colonie qui va bien (pas de famine, pas de guerre) double sa population
/// tous les 3 à 5 ans. Ces mesures protègent ce réglage quand on ajoute des fonctionnalités qui enrichissent la colonie.
/// </summary>
public class GrowthTests(ITestOutputHelper output)
{
    [Fact]
    public void Une_colonie_de_huit_fondateurs_double_en_trois_a_cinq_ans_puis_continue_de_croitre()
    {
        int[] seeds = [12345, 1, 2, 3, 4];
        var yearsToDouble = new List<double>();
        var populationAtSix = new List<int>();

        foreach (int seed in seeds)
        {
            var world = new WorldState(seed, startingColonists: 8);
            Colony colony = world.Colonies[0];
            double? doubled = null;
            for (long i = 1; i <= 6 * TimeConstants.TicksPerYear; i++)
            {
                world.Step();
                if (doubled is null && colony.Members.Count >= 16)
                    doubled = i / (double)TimeConstants.TicksPerYear;
            }
            populationAtSix.Add(colony.Members.Count);
            yearsToDouble.Add(doubled ?? 99);
            output.WriteLine($"graine {seed} : 16 colons en {doubled:0.0} ans, {colony.Members.Count} colons à 6 ans");
        }

        double median = yearsToDouble.OrderBy(y => y).ElementAt(seeds.Length / 2);
        output.WriteLine($"Médiane du premier doublement : {median:0.0} ans ; population moyenne à 6 ans : {populationAtSix.Average():0}");

        Assert.InRange(median, 2.0, 5.0);
        Assert.All(yearsToDouble, y => Assert.True(y <= 6.0, $"Une colonie met {y:0.0} ans à doubler."));
        Assert.InRange(populationAtSix.Average(), 22, 40);
    }
}
