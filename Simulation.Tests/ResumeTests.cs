using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>La reprise d'une partie longue, à des instants où des missions, des gisements et des offrandes sont en cours, redonne exactement la même suite.</summary>
public sealed class ResumeTests
{
    [Theory]
    [InlineData(101, 90)]
    [InlineData(101, 150)]
    public void Une_partie_sauvegardee_a_n_importe_quel_instant_reprend_exactement(int seed, int dayOfSave)
    {
        var world = new WorldState(seed, startingColonists: 8, colonyCount: 2);
        for (long tick = 0; tick < dayOfSave * (long)TimeConstants.TicksPerDay; tick++) world.Step();
        string path = Path.Combine(Path.GetTempPath(), "GodColony-reprise-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Null(new WorldComparison().Difference(world, loaded));
            for (long tick = 0; tick < 10L * TimeConstants.TicksPerDay; tick++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world, loaded));
            Assert.Equal(world.Money.Minted, loaded.Money.Minted);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Deux_executions_identiques_donnent_exactement_le_meme_etat()
    {
        var a = new WorldState(102, startingColonists: 8, colonyCount: 2);
        var b = new WorldState(102, startingColonists: 8, colonyCount: 2);
        for (long tick = 0; tick < 100L * TimeConstants.TicksPerDay; tick++) { a.Step(); b.Step(); }
        Assert.Null(new WorldComparison().Difference(a, b));
    }
}
