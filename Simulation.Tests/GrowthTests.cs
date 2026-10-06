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
        // Neuf graines : la croissance d'une colonie est très chaotique (une même graine double en 1,2 ou en 4 ans selon le moindre
        // changement), la médiane de cinq parties basculait d'un côté ou de l'autre de la cible au gré des nouveautés.
        int[] seeds = [12345, 2, 3, 4, 5, 6, 7, 8, 9];
        var doubledAt = new double?[seeds.Length];
        var populationAtSix = new int[seeds.Length];

        // Les parties sont indépendantes : on les joue en parallèle, c'est le test le plus long de la suite.
        Parallel.For(0, seeds.Length, s =>
        {
            var world = new WorldState(seeds[s], startingColonists: 8);
            Colony colony = world.Colonies[0];
            double? doubled = null;
            for (long i = 1; i <= 6 * TimeConstants.TicksPerYear; i++)
            {
                world.Step();
                if (doubled is null && colony.Members.Count >= 16)
                    doubled = i / (double)TimeConstants.TicksPerYear;
            }
            populationAtSix[s] = colony.Members.Count;
            doubledAt[s] = doubled;
        });
        for (int s = 0; s < seeds.Length; s++)
            output.WriteLine($"graine {seeds[s]} : 16 colons en {doubledAt[s]:0.0} ans, {populationAtSix[s]} colons à 6 ans");
        double[] yearsToDouble = doubledAt.Select(d => d ?? 99).ToArray();

        double median = yearsToDouble.OrderBy(y => y).ElementAt(seeds.Length / 2);
        output.WriteLine($"Médiane du premier doublement : {median:0.0} ans ; population moyenne à 6 ans : {populationAtSix.Average():0}");

        Assert.InRange(median, 2.0, 5.0);
        Assert.All(yearsToDouble, y => Assert.True(y <= 6.0, $"Une colonie met {y:0.0} ans à doubler."));
        Assert.InRange(populationAtSix.Average(), 22, 40);
    }
}
