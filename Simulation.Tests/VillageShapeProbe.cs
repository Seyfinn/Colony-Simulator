using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Tests;

/// <summary>Sonde d'inspection : fait vivre quelques villages et écrit leur plan en texte dans le dossier temporaire (jamais un test de non-régression).</summary>
public sealed class VillageShapeProbe
{
    [Theory(Skip = "Sonde d'inspection manuelle : écrit le plan des villages dans le dossier temporaire.")]
    [InlineData(12345, 100)]
    [InlineData(777, 100)]
    public void Dessine_le_village(int seed, int days)
    {
        var world = new WorldState(seed, startingColonists: 10);
        var lines = new List<string>();
        for (int day = 1; day <= days; day++)
        {
            for (int i = 0; i < Time.TimeConstants.TicksPerDay; i++) world.Step();
            if (day % 50 == 0)
            {
                lines.Add($"===== jour {day}, graine {seed}, population {world.Colonies[0].Members.Count}");
                lines.Add(VillageAscii.Draw(world.Colonies[0]));
                lines.Add("pensées : " + string.Join(" | ", world.Colonies[0].Thoughts.TakeLast(6).Select(t => t.Text)));
                var roads = world.Colonies[0].Map.Roads;
                lines.Add($"sentiers {roads.Trails}, chemins {roads.DirtRoads}, usure active {roads.TouchedToday.Count}");
                lines.Add($"planification : {world.Planning.CompletedSearches} recherches, {world.Planning.Expansions} expansions, {world.Planning.Prefilters} préfiltres, {world.Planning.ValidationCells} cellules");
            }
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), $"village-{seed}.txt"), lines);
    }
}
