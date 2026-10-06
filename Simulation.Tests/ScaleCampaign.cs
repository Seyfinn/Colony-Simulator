using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

/// <summary>Campagne de mesures des grandes colonies (15 graines) : lancée seulement avec GODCOLONY_MESURES=1 ; n'affirme rien, résume.</summary>
public sealed class ScaleCampaign(ITestOutputHelper output)
{
    [Fact]
    public void Mesures_integrees_sur_quinze_graines()
    {
        if (Environment.GetEnvironmentVariable("GODCOLONY_MESURES") != "1") return;
        int years = int.Parse(Environment.GetEnvironmentVariable("GODCOLONY_ANNEES") ?? "8");
        var rows = new List<string>();
        for (int seed = 1; seed <= 15; seed++)
        {
            var world = new WorldState(seed * 101, startingColonists: 10, migration: true, lifecycle: true, colonyCount: 1);
            var watch = new StarvationWatch();
            Colony colony = world.Colonies[0];
            int schismAsks = 0, maxPop = 0;
            for (long i = 0; i < (long)years * TimeConstants.TicksPerYear; i++)
            {
                world.Step();
                if (i % TimeConstants.TicksPerDay == 0)
                {
                    foreach (Colony c in world.Colonies) watch.Observe(c);
                    schismAsks = world.Colonies.Sum(c => c.Prayers.All.Count(p => p.Kind == DecisionKind.Schism));
                    maxPop = Math.Max(maxPop, world.Colonies.Sum(c => c.Members.Count));
                }
            }
            var snaps = world.Settlements.Select(ScaleSnapshot.Of).ToList();
            int bread = snaps.Sum(s => s.Produced.GetValueOrDefault(ResourceType.Bread));
            int fuel = snaps.Sum(s => s.FuelUsed.GetValueOrDefault(ResourceType.Wood));
            double? use = snaps.SelectMany(s => s.Workshops).Select(w => w.Utilization).Where(u => u is not null).DefaultIfEmpty(null).Average();
            int ext = world.Settlements.SelectMany(s => s.Buildings).Count(b => b.IsExtension && b.IsComplete);
            int shortcuts = world.Settlements.Sum(s => s.Owner.Layout.Projects.Count(p => p.Kind == DevelopmentKind.RoadShortcut));
            rows.Add($"graine {seed * 101}: pop {world.Colonies.Sum(c => c.Members.Count)} (max {maxPop}), colonies {world.Colonies.Count}, schismes demandés {schismAsks}, "
                + $"famine {(watch.Victim is null ? "non" : "OUI")}, pain/10j {bread}, bois four/10j {fuel}, utilisation {(use is null ? "n/a" : use.Value.ToString("P0"))}, extensions {ext}, raccourcis {shortcuts}");
        }
        foreach (string row in rows) output.WriteLine(row);
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "mesures-grandes-colonies.txt"), rows);
    }
}
