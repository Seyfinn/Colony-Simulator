using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;

namespace GodColony.Simulation.Tests;

/// <summary>Outil jetable : fige le schéma et des sauvegardes témoins du format v1 AVANT de changer les classes.</summary>
public sealed class FreezeV1Generator
{
    [Fact(Skip = "Outil à usage unique : le format v1 est déjà figé.")]
    public void Fige_le_schema_et_les_sauvegardes_v1()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        Directory.CreateDirectory(Path.Combine(root, "Simulation.Tests", "Fixtures"));
        File.WriteAllText(Path.Combine(root, "Simulation", "Persistence", "SchemaV1.txt"), StateGraph.Schema);

        var small = new WorldState(12345, 128, 128, colonyCount: 1);
        for (int i = 0; i < 16000; i++) small.Step();
        WorldSave.Save(Path.Combine(root, "Simulation.Tests", "Fixtures", "v1-solo.gcsave"), small);

        var two = new WorldState(2026, 128, 128, colonyCount: 2);
        Urbanism.BuildInstantly(two.Colonies[0].Map, two.Colonies[0], BuildingType.Storehouse);
        Urbanism.BuildInstantly(two.Colonies[0].Map, two.Colonies[0], BuildingType.Tavern);
        for (int i = 0; i < 9000; i++) two.Step();
        WorldSave.Save(Path.Combine(root, "Simulation.Tests", "Fixtures", "v1-duo.gcsave"), two);
    }
}
