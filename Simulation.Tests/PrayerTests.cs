using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public class PrayerTests
{
    private static (WorldState World, Colony Colony) Closed()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false);
        return (world, world.Colonies[0]);
    }

    private static Prayer Ask(WorldState world, Colony colony, Action apply, string subject = "ici") =>
        colony.Prayers.Ask(DecisionKind.Dam, subject, "Construire un barrage ?", "La rivière est haute.", apply, world.Clock)!;

    [Fact]
    public void Une_priere_attend_la_reponse_sans_rien_faire()
    {
        (WorldState world, Colony colony) = Closed();
        bool applied = false;
        Prayer? received = null;
        colony.Prayers.Asked += p => received = p;

        Prayer prayer = Ask(world, colony, () => applied = true);

        Assert.Equal(PrayerStatus.Pending, prayer.Status);
        Assert.Same(prayer, received);
        Assert.False(applied);
        Assert.Single(colony.Prayers.Pending);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("prière"));
    }

    [Fact]
    public void La_meme_question_n_est_pas_posee_deux_fois_en_attente()
    {
        (WorldState world, Colony colony) = Closed();
        Ask(world, colony, () => { });
        Assert.Null(colony.Prayers.Ask(DecisionKind.Dam, "ici", "Encore ?", "", () => { }, world.Clock));
        Assert.NotNull(colony.Prayers.Ask(DecisionKind.Dam, "ailleurs", "Autre site ?", "", () => { }, world.Clock));
        Assert.Equal(2, colony.Prayers.Pending.Count());
    }

    [Fact]
    public void Accorder_execute_la_decision_et_renforce_la_foi()
    {
        (WorldState world, Colony colony) = Closed();
        bool applied = false;
        Prayer prayer = Ask(world, colony, () => applied = true);
        float before = colony.Members.Average(m => m.Needs.Faith);

        world.AnswerPrayer(prayer, approve: true);

        Assert.True(applied);
        Assert.Equal(PrayerStatus.Approved, prayer.Status);
        Assert.False(prayer.AutoApproved);
        Assert.NotNull(prayer.AnsweredTicks);
        Assert.Empty(colony.Prayers.Pending);
        Assert.True(colony.Members.Average(m => m.Needs.Faith) > before);

        // Répondre une seconde fois ne fait rien.
        applied = false;
        world.AnswerPrayer(prayer, approve: true);
        Assert.False(applied);
    }

    [Fact]
    public void Refuser_n_execute_rien_fait_vaciller_la_foi_surtout_des_pieux_et_la_colonie_n_insiste_pas()
    {
        (WorldState world, Colony colony) = Closed();
        bool applied = false;
        Prayer prayer = Ask(world, colony, () => applied = true);
        var before = colony.Members.ToDictionary(m => m, m => m.Needs.Faith);
        float moodBefore = colony.AverageMood;

        world.AnswerPrayer(prayer, approve: false);

        Assert.False(applied);
        Assert.Equal(PrayerStatus.Refused, prayer.Status);
        Assert.All(colony.Members, m => Assert.True(m.Needs.Faith < before[m]));
        Assert.True(colony.AverageMood < moodBefore, "Perdre la foi pèse sur l'humeur.");

        // Le plus pieux perd plus que le moins pieux.
        Colonist devout = colony.Members.OrderByDescending(m => m.Personality[Axis.Piete]).First();
        Colonist skeptic = colony.Members.OrderBy(m => m.Personality[Axis.Piete]).First();
        Assert.True(before[devout] - devout.Needs.Faith > before[skeptic] - skeptic.Needs.Faith);

        // La colonie n'insiste pas avant cinq jours, puis peut reposer la question.
        Assert.Null(colony.Prayers.Ask(DecisionKind.Dam, "ici", "Et maintenant ?", "", () => { }, world.Clock));
        for (long i = 0; i < PrayerBook.RefusalCooldownDays * TimeConstants.TicksPerDay + 1; i++)
            world.Step();
        Assert.NotNull(colony.Prayers.Ask(DecisionKind.Dam, "ici", "Et maintenant ?", "", () => { }, world.Clock));
    }

    [Fact]
    public void Un_accord_habituel_execute_tout_de_suite_sans_solliciter_le_joueur()
    {
        (WorldState world, Colony colony) = Closed();
        colony.Prayers.AutoApprove.Add(DecisionKind.Dam);
        bool applied = false, asked = false;
        colony.Prayers.Asked += _ => asked = true;

        Prayer prayer = Ask(world, colony, () => applied = true);

        Assert.True(applied);
        Assert.False(asked);
        Assert.True(prayer.AutoApproved);
        Assert.Equal(PrayerStatus.Approved, prayer.Status);
        Assert.Empty(colony.Prayers.Pending);
    }

    [Fact]
    public void La_foi_revient_doucement_vers_le_temperament_de_chacun()
    {
        (WorldState world, Colony colony) = Closed();
        foreach (Colonist c in colony.Members)
            c.Needs.Faith = 0f;

        for (int day = 1; day <= 30; day++)
        {
            for (long i = 0; i < TimeConstants.TicksPerDay; i++)
                world.Step();
        }

        // Au bout d'un mois, chacun a retrouvé (à peu près) sa foi naturelle, plus haute chez les pieux.
        Assert.All(colony.Members, c =>
            Assert.InRange(c.Needs.Faith, Needs.NeutralFaith + 0.2f * c.Personality[Axis.Piete] - 0.15f, 1f));
        Colonist devout = colony.Members.OrderByDescending(m => m.Personality[Axis.Piete]).First();
        Colonist skeptic = colony.Members.OrderBy(m => m.Personality[Axis.Piete]).First();
        Assert.True(devout.Needs.Faith >= skeptic.Needs.Faith);
    }
}
