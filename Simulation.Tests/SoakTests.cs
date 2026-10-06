using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

/// <summary>Parties longues avec tout activé : on cherche les incohérences, pas un comportement précis.</summary>
public class SoakTests(ITestOutputHelper output)
{
    private static int Coins(WorldState world) =>
        world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)) + world.Caravans.Sum(c => c.Coins);

    [Fact]
    public void Quatre_peuples_vivent_deux_ans_sans_incoherence()
    {
        var world = new WorldState(2026, startingColonists: 8, colonyCount: 4);
        int coins = Coins(world);
        long dotationsAtStart = world.Money.Dotations;
        var watches = world.Colonies.ToDictionary(c => c, _ => new StarvationWatch());

        for (long i = 0; i < 2 * TimeConstants.TicksPerYear; i++)
        {
            world.Step();
            if (i % TimeConstants.TicksPerDay != 0)
                continue;

            Assert.Equal(coins + world.Money.Minted + world.Money.Dotations - dotationsAtStart, Coins(world) + world.CoinsLostToEvents); // hors colporteurs et frappe enregistrée
            foreach (Colony colony in world.Colonies)
            {
                watches[colony].Observe(colony);

                // Personne n'est compté deux fois, ni ailleurs que chez lui, ni à la fois au camp et en caravane.
                Assert.Equal(colony.Members.Count, colony.Members.Distinct().Count());
                Assert.All(colony.Members, m => Assert.Same(colony, m.Colony));
                // Les voyageurs gardent leur citoyenneté : ils restent dans Members mais ne comptent pas parmi les présents.
                var away = world.Caravans.Where(c => c.From == colony).SelectMany(c => c.Traders).ToList();
                Assert.All(away, t => Assert.Contains(t, colony.Members));
                Assert.Empty(away.Intersect(colony.PresentMembers));

                // Aucune quantité négative en stock, aucun logement partagé par trop de monde.
                foreach (ResourceType type in Enum.GetValues<ResourceType>())
                    Assert.True(colony.Stock.Get(type) >= 0, $"{colony.Name} : stock négatif de {type}.");
                Assert.All(colony.Buildings.Where(b => b.IsHut), b => Assert.True(b.Residents.Count <= Building.HutCapacity));
                Assert.All(colony.Members.Where(m => m.Home is not null), m => Assert.Contains(m, m.Home!.Residents));
            }
        }

        foreach (Colony colony in world.Colonies)
        {
            output.WriteLine($"{colony.Name} ({colony.Species.Plural}) : {colony.Members.Count} colons, {colony.Buildings.Count(b => b.IsComplete)} bâtiments, " +
                             $"{colony.Stock.Get(ResourceType.Coins)} pièces, {colony.Trades.Count} voyages, naissances/tombes {colony.Deaths.Count}");
            Assert.NotEmpty(colony.Members);
            Assert.Null(watches[colony].Victim);
        }
        output.WriteLine($"Voyages menés à terme : {world.CompletedCaravans}");
    }
}
