using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;

namespace GodColony.Simulation.Tests;

public class RecentEventTests
{
    [Theory]
    [InlineData(false, ColonyEventOutcome.Ruined)]
    [InlineData(true, ColonyEventOutcome.Extinguished)]
    public void Le_feu_fournit_sa_case_exacte_et_son_issue(bool well, ColonyEventOutcome outcome)
    {
        var world = new WorldState(42, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        colony.Buildings.Clear();
        var target = new Building(BuildingType.Hut, colony.CampX + 2, colony.CampY + 2);
        target.Progress = 1;
        colony.Buildings.Add(target);
        if (well)
        {
            var water = new Building(BuildingType.Well, colony.CampX + 5, colony.CampY + 2);
            water.Progress = 1;
            // Le hasard peut choisir le puits : vérifier le vrai bâtiment identifié plutôt qu'un emplacement présumé.
            colony.Buildings.Add(water);
        }
        Events.Fire(world, colony);
        RecentEvent report = Assert.Single(colony.RecentEvents);
        Assert.Equal(ColonyEventKind.Fire, report.Kind);
        Assert.Equal(outcome, report.Outcome);
        Assert.Equal(world.Clock.Ticks, report.Ticks);
        Assert.True(report.X == target.X && report.Y == target.Y || well && report.X == colony.CampX + 5 && report.Y == colony.CampY + 2);
        Assert.Equal(well ? 2 : 0, colony.Buildings.Count);
    }

    [Fact]
    public void L_historique_est_borne_et_ne_modifie_ni_la_sauvegarde_ni_le_hasard()
    {
        var world = new WorldState(42, migration: false, lifecycle: false);
        byte[] Bytes()
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            StateGraph.Write(writer, world); return stream.ToArray();
        }
        byte[] before = Bytes();
        Colony colony = world.Colonies[0];
        for (int i = 0; i < 40; i++) colony.RecordEvent(new(ColonyEventKind.Raid, 1, colony.CampY, i, ColonyEventOutcome.Repelled));
        Assert.Equal(24, colony.RecentEvents.Count);
        Assert.Equal(16, colony.RecentEvents[0].Ticks);
        Assert.Equal(before, Bytes());
    }

    [Fact]
    public void Un_colporteur_qui_ne_peut_rien_vendre_a_une_issue_distincte()
    {
        var world = new WorldState(42, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        foreach (ResourceType good in Enum.GetValues<ResourceType>()) colony.Stock.TryTake(good, colony.Stock.Get(good));
        Events.Peddler(world, colony);
        RecentEvent report = Assert.Single(colony.RecentEvents);
        Assert.Equal(ColonyEventKind.Peddler, report.Kind);
        Assert.Equal(ColonyEventOutcome.Unaffordable, report.Outcome);
        Assert.Equal(colony.CampX, report.X); Assert.Equal(colony.CampY, report.Y);
        Assert.Equal(0, colony.Stock.Get(ResourceType.Coins));
    }
}
