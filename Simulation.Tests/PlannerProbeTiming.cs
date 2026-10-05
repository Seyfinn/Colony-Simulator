using System.Diagnostics;
using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Tests;

/// <summary>Mesure jetable : combien coûte une sonde synchrone du planificateur.</summary>
public sealed class PlannerProbeTiming
{
    [Fact]
    public void Mesure_les_sondes()
    {
        var world = new WorldState(12345);
        for (int i = 0; i < 200; i++) world.Step();
        Colony colony = world.Colonies[0];
        var lines = new List<string>();
        foreach ((string name, DevelopmentKind kind, BuildingType? type) in new (string, DevelopmentKind, BuildingType?)[]
                 { ("hut", DevelopmentKind.Housing, BuildingType.Hut), ("field", DevelopmentKind.Field, null), ("kiln", DevelopmentKind.Workshop, BuildingType.Kiln),
                   ("mill", DevelopmentKind.Workshop, BuildingType.Mill), ("tavern", DevelopmentKind.Civic, BuildingType.Tavern) })
        {
            var watch = Stopwatch.StartNew();
            PlacementProposal? p = SettlementPlanner.Probe(colony, kind, type, false, out PlacementFailureKind? failure);
            watch.Stop();
            lines.Add($"{name}: {watch.Elapsed.TotalMilliseconds:0.0} ms -> " + (p is null ? $"échec {failure}" : $"({p.X},{p.Y}) score {p.Score:0.00} service {p.ServiceSeconds:0.00}s path {p.PathCells.Count}"));
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "probe-timing.txt"), lines);
    }
}
