using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class DiplomacyTests(ITestOutputHelper output)
{
    private static WorldState Peoples(int count = 2, int colonists = 14, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false, colonyCount: count, trade: false);

    private static void RunDays(WorldState world, int days)
    {
        for (long i = 0; i < (long)days * TimeConstants.TicksPerDay; i++)
            world.Step();
    }

    private static int CoinsInTheWorld(WorldState world) =>
        world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)) + world.Caravans.Sum(c => c.Coins)
        + world.WarParties.Sum(p => p.Loot.GetValueOrDefault(ResourceType.Coins));

    [Fact]
    public void L_opinion_rejoint_peu_a_peu_ce_que_justifient_les_rapports()
    {
        WorldState world = Peoples();
        Colony humans = world.Colonies[0], dwarves = world.Colonies[1];
        Assert.Equal(0f, humans.OpinionOf(dwarves));

        Diplomacy.Daily(world);
        float target = Diplomacy.TargetOpinion(world, humans, dwarves);
        Assert.Equal(Math.Clamp(target, -Diplomacy.OpinionDriftPerDay, Diplomacy.OpinionDriftPerDay), humans.OpinionOf(dwarves), 3);

        // Une rancune (un barrage, une querelle) fait tomber l'opinion et figure parmi les raisons.
        humans.Grudges[dwarves] = 2f;
        var factors = Diplomacy.OpinionFactors(world, humans, dwarves);
        Assert.Contains(factors, f => f.Reason.Contains("rancunes") && f.Value == -50f);
        Assert.True(Diplomacy.TargetOpinion(world, humans, dwarves) < target - 40f);
        Assert.Equal(Diplomacy.TargetOpinion(world, humans, dwarves), factors.Sum(f => f.Value), 3);
    }

    [Fact]
    public void Une_colonie_hostile_et_forte_prie_pour_declarer_la_guerre()
    {
        WorldState world = Peoples();
        Colony a = world.Colonies[0], b = world.Colonies[1];
        RunDays(world, 1);
        a.Opinions[b] = -90f;
        a.Grudges[b] = 3f;
        a.Stock.Add(ResourceType.Tools, 10);

        Diplomacy.Daily(world);
        Prayer war = Assert.Single(a.Prayers.Pending, p => p.Kind == DecisionKind.War);
        Assert.Contains(b.Name, war.Question);
        Assert.False(Diplomacy.AtWar(world, a, b));

        world.AnswerPrayer(war, approve: true);
        Assert.True(Diplomacy.AtWar(world, a, b));
        Assert.Contains(b.Thoughts, t => t.Text.Contains("déclare la guerre"));
        // On ne commerce plus avec l'ennemi.
        Assert.Null(Diplomacy.Allies(world, a).FirstOrDefault());
    }

    [Fact]
    public void Refuser_la_guerre_fait_vaciller_la_foi_et_la_colonie_n_insiste_pas()
    {
        WorldState world = Peoples();
        Colony a = world.Colonies[0], b = world.Colonies[1];
        RunDays(world, 1);
        a.Opinions[b] = -90f;
        a.Grudges[b] = 3f;
        a.Stock.Add(ResourceType.Tools, 10);
        Diplomacy.Daily(world);
        Prayer war = a.Prayers.Pending.Single(p => p.Kind == DecisionKind.War);
        float faith = a.Members.Average(m => m.Needs.Faith);

        world.AnswerPrayer(war, approve: false);
        Assert.False(Diplomacy.AtWar(world, a, b));
        Assert.True(a.Members.Average(m => m.Needs.Faith) < faith);
        a.Opinions[b] = -90f;
        Diplomacy.Daily(world);
        Assert.DoesNotContain(a.Prayers.Pending, p => p.Kind == DecisionKind.War);
    }

    [Fact]
    public void Une_bande_de_guerriers_marche_livre_bataille_et_rentre_sans_creer_de_pieces()
    {
        WorldState world = Peoples(colonists: 18);
        Colony a = world.Colonies[0], b = world.Colonies[1];
        RunDays(world, 1);
        a.Stock.Add(ResourceType.Tools, 8);
        Diplomacy.DeclareWar(world, a, b);
        int coins = CoinsInTheWorld(world) + world.CoinsLostToEvents;
        long dotationsAtStart = world.Money.Dotations;
        int people = a.Members.Count + a.Transients.Count + b.Members.Count;

        WarParty party = Warfare.Depart(world, a, b)!;
        Assert.NotNull(party);
        int warriors = party.Warriors.Count;
        Assert.InRange(warriors, Diplomacy.MinWarriors, Diplomacy.MaxWarriors);
        Assert.Equal(Math.Min(warriors, 8), party.Weapons);
        Assert.All(party.Warriors, w => Assert.Contains(w, a.Members)); // citoyens en marche
        Assert.DoesNotContain(a.PresentMembers, m => party.Warriors.Contains(m));

        // Elle avance sur la route, se bat chez l'ennemi, puis rentre.
        long half = (party.ArriveTicks - world.Clock.Ticks) / 2;
        for (long i = 0; i < half; i++) world.Step();
        Assert.InRange(party.RoutePosition(world.Clock.Ticks), 0.2f, 0.8f);
        while (world.WarParties.Contains(party))
        {
            world.Step();
            Assert.Equal(coins + world.Money.Minted + world.Money.Dotations - dotationsAtStart, CoinsInTheWorld(world) + world.CoinsLostToEvents);
        }
        Assert.NotNull(party.Victory);
        output.WriteLine(string.Join("\n", b.Thoughts.TakeLast(3).Concat(a.Thoughts.TakeLast(2)).Select(t => t.Text)));

        int dead = a.Deaths.Count(g => g.Cause == "guerre") + b.Deaths.Count(g => g.Cause == "guerre");
        Assert.Equal(people - dead, a.Members.Count + b.Members.Count); // les citoyens de retour restent comptés une seule fois
        Assert.Equal(1, a.BattlesWon + a.BattlesLost);
        Assert.True(b.OpinionOf(a) < 0f && b.GrudgeAgainst(a) > 0f);
        if (party.Victory == false)
            Assert.Contains(a.Deaths, g => g.Cause == "guerre");
        Assert.Contains(b.RecentEvents, e => e.Kind == ColonyEventKind.Raid);
    }

    [Fact]
    public void La_paix_ouvre_une_treve_qui_finit_par_expirer()
    {
        WorldState world = Peoples();
        Colony a = world.Colonies[0], b = world.Colonies[1];
        RunDays(world, 1);
        Diplomacy.DeclareWar(world, a, b);
        b.WarWeariness = 30f;

        Diplomacy.OfferPeace(world, a, b);
        Pact truce = Assert.Single(world.Pacts);
        Assert.Equal(PactKind.Truce, truce.Kind);
        Assert.False(Diplomacy.AtWar(world, a, b));
        Assert.True(a.OpinionOf(b) >= -30f);

        RunDays(world, Diplomacy.TruceDays + 1);
        Assert.Empty(world.Pacts);
    }

    [Fact]
    public void L_ennemi_qui_gagne_refuse_la_paix_au_debut()
    {
        WorldState world = Peoples();
        Colony a = world.Colonies[0], b = world.Colonies[1];
        RunDays(world, 1);
        Diplomacy.DeclareWar(world, a, b);
        b.BattlesWon = 3;
        b.WarWeariness = 0f;

        Diplomacy.OfferPeace(world, a, b);
        Assert.True(Diplomacy.AtWar(world, a, b));
        Assert.Contains(a.Thoughts, t => t.Text.Contains("rejette"));
    }

    [Fact]
    public void Une_colonie_battue_et_lasse_prie_pour_la_paix()
    {
        WorldState world = Peoples();
        Colony a = world.Colonies[0], b = world.Colonies[1];
        RunDays(world, 1);
        Diplomacy.DeclareWar(world, a, b);
        // Pas de bataille pendant ce temps : on ne mesure que la décision de demander la paix.
        a.LastWarPartyTicks = b.LastWarPartyTicks = long.MaxValue / 2;
        b.BattlesLost = 3;
        b.Prayers.AutoApprove.Add(DecisionKind.Peace);
        RunDays(world, 4);
        Assert.True(Diplomacy.AtWar(world, a, b));   // trop tôt pour parler de paix
        RunDays(world, 3);
        Assert.Contains(b.Prayers.All, p => p.Kind == DecisionKind.Peace && p.Status == PrayerStatus.Approved);
        Assert.Equal(PactKind.Truce, Diplomacy.PactBetween(world, a, b)?.Kind);
    }

    [Fact]
    public void Les_allies_se_defendent_et_commercent_plus_volontiers()
    {
        WorldState world = Peoples(count: 3);
        Colony a = world.Colonies[0], b = world.Colonies[1], c = world.Colonies[2];
        Assert.Equal(0f, Diplomacy.AlliedHelp(world, a, c));

        Diplomacy.SealAlliance(world, a, b);
        Assert.True(Diplomacy.AreAllied(world, a, b));
        Assert.True(Diplomacy.AlliedHelp(world, a, c) > 0f || world.WorldMap.TravelDays(a, b) > 6f, "Les alliés proches envoient des renforts.");
        Assert.Contains(Diplomacy.OpinionFactors(world, a, b), f => f.Reason == "Notre alliance");

        // L'alliance se rompt quand la confiance n'y est plus.
        a.Opinions[b] = -50f;
        b.Opinions[a] = -50f;
        a.Grudges[b] = 3f;
        b.Grudges[a] = 3f;
        Diplomacy.Daily(world);
        Assert.False(Diplomacy.AreAllied(world, a, b));
    }

    [Fact]
    public void Deux_peuples_amis_qui_connaissent_la_diplomatie_prient_pour_s_allier()
    {
        WorldState world = Peoples();
        Colony a = world.Colonies[0], b = world.Colonies[1];
        RunDays(world, 1);
        Knowledge.Discover(a, Discovery.Diplomacy, world.Clock);
        a.Opinions[b] = 80f;
        b.Opinions[a] = 80f;
        a.Prayers.AutoApprove.Add(DecisionKind.Alliance);

        Diplomacy.Daily(world);
        Assert.True(Diplomacy.AreAllied(world, a, b));
    }

    [Fact]
    public void Une_partie_en_pleine_guerre_se_recharge_a_l_identique()
    {
        var world = new WorldState(2026, 128, 128, startingColonists: 16, colonyCount: 3);
        RunDays(world, 2);
        Colony a = world.Colonies[0], b = world.Colonies[1], c = world.Colonies[2];
        b.Opinions[c] = c.Opinions[b] = 90f;
        Diplomacy.SealAlliance(world, b, c);
        Diplomacy.DeclareWar(world, a, b);
        Assert.NotNull(Warfare.Depart(world, a, b));
        Knowledge.Discover(a, Discovery.Writing, world.Clock);
        a.Opinions[c] = -90f;
        c.Prayers.Ask(DecisionKind.Peace, a.Name, "Faire la paix ?", "Pour l'essai.", () => { }, world.Clock);
        for (int i = 0; i < 500; i++) world.Step();

        string path = Path.Combine(Path.GetTempPath(), $"GodColony-guerre-{Guid.NewGuid():N}.gcsave");
        try
        {
            Persistence.WorldSave.Save(path, world);
            WorldState loaded = Persistence.WorldSave.Load(path).World;
            Assert.Single(loaded.WarParties);
            Assert.Equal(2, loaded.Pacts.Count);
            Assert.True(Diplomacy.AtWar(loaded, loaded.Colonies[0], loaded.Colonies[1]));
            Assert.Same(loaded.Colonies[0], loaded.WarParties[0].From);
            // La prière restaurée retrouve sa décision : l'accorder ouvre bien la paix.
            for (int i = 0; i < 3000; i++)
            {
                world.Step();
                loaded.Step();
            }
            Assert.Null(new WorldComparison().Difference(world, loaded));
            Prayer peace = loaded.Colonies[2].Prayers.Pending.First(p => p.Kind == DecisionKind.Peace);
            loaded.AnswerPrayer(peace, approve: true);
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public void Un_grand_village_peut_essaimer_une_colonie_soeur()
    {
        var world = new WorldState(77, startingColonists: 36, migration: true, lifecycle: false, colonyCount: 1);
        Colony mother = world.Colonies[0];
        mother.Stock.Add(ResourceType.Grain, 200);
        Colonist leader = Schism.Leader(mother)!;
        Assert.NotNull(leader);
        List<Colonist> group = Schism.Followers(mother, leader);
        Assert.InRange(group.Count, 2, mother.Members.Count / 3);
        int before = mother.Members.Count, grain = mother.Stock.Get(ResourceType.Grain);
        int coins = mother.Stock.Get(ResourceType.Coins);

        Colony daughter = Schism.Split(world, mother, leader.Id)!;
        Assert.NotNull(daughter);
        Assert.Equal(2, world.Colonies.Count);
        Assert.Same(mother, daughter.Parent);
        Assert.Equal(before, mother.Members.Count + daughter.Members.Count);
        Assert.All(daughter.Members, m => Assert.Same(daughter, m.Colony));
        Assert.Contains(leader, daughter.Members);
        Assert.Equal(grain, mother.Stock.Get(ResourceType.Grain) + daughter.Stock.Get(ResourceType.Grain));
        Assert.Equal(coins, mother.Stock.Get(ResourceType.Coins) + daughter.Stock.Get(ResourceType.Coins));
        Assert.Equal(mother.Known.Keys.OrderBy(k => k), daughter.Known.Keys.OrderBy(k => k));
        Assert.Contains(Diplomacy.OpinionFactors(world, daughter, mother), f => f.Reason == "Nos racines communes");
        Assert.True(world.WorldMap.Grid.Distance(world.WorldMap.TileOf(mother), world.WorldMap.TileOf(daughter)) <= Schism.MaxDistance);

        // La fille vit sa vie.
        RunDays(world, 2);
        Assert.NotEmpty(daughter.Members);
        Assert.All(daughter.Members, m => Assert.True(daughter.Map.InBounds(m.TileX, m.TileY)));
    }
}
