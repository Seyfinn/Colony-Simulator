using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class SocialTests(ITestOutputHelper output)
{
    private static WorldState ClosedColony(int colonists, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false);

    private static void RunDays(WorldState world, double days)
    {
        long ticks = (long)(days * TimeConstants.TicksPerDay);
        for (long i = 0; i < ticks; i++)
            world.Step();
    }

    private static Personality With(params (Axis Axis, float Value)[] values)
    {
        // Une personnalité sur mesure : on tire au hasard puis on impose les axes voulus.
        var personality = Personality.Neutral;
        var field = typeof(Personality).GetField("_value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var array = (float[])field.GetValue(personality)!;
        foreach ((Axis axis, float value) in values)
            array[(int)axis] = value;
        return personality;
    }

    [Fact]
    public void Les_axes_de_personnalite_sont_bornes_et_centres_sur_zero()
    {
        var random = new Random(7);
        var sums = new float[Personality.All.Length];
        const int count = 2000;
        for (int i = 0; i < count; i++)
        {
            Personality personality = Personality.Random(random);
            foreach (Axis axis in Personality.All)
            {
                Assert.InRange(personality[axis], -1f, 1f);
                sums[(int)axis] += personality[axis];
            }
        }
        Assert.All(sums, sum => Assert.InRange(sum / count, -0.05f, 0.05f));
    }

    [Fact]
    public void Les_axes_modifient_vraiment_le_comportement()
    {
        Personality worker = With((Axis.Ardeur, 1f), (Axis.Curiosite, 1f), (Axis.Sociabilite, 1f), (Axis.Attachement, 1f));
        Personality idler = With((Axis.Ardeur, -1f), (Axis.Curiosite, -1f), (Axis.Sociabilite, -1f), (Axis.Attachement, -1f));

        Assert.True(worker.WorkFactor > idler.WorkFactor);
        Assert.True(worker.LearningFactor > idler.LearningFactor);
        Assert.True(worker.BoredomFactor < idler.BoredomFactor);
        Assert.True(worker.LonelinessFactor > idler.LonelinessFactor);
        Assert.True(worker.PatienceFactor > idler.PatienceFactor);
        Assert.Contains("travailleur", worker.NotableTraits(Sex.Male));
        Assert.Contains("paresseuse", idler.NotableTraits(Sex.Female));
    }

    [Fact]
    public void Des_personnalites_proches_s_entendent_et_des_opposees_se_frottent()
    {
        Personality calm = With((Axis.Temperament, -0.8f), (Axis.Sociabilite, 0.5f), (Axis.Piete, 0.6f), (Axis.Ardeur, 0.5f));
        Personality alsoCalm = With((Axis.Temperament, -0.7f), (Axis.Sociabilite, 0.6f), (Axis.Piete, 0.5f), (Axis.Ardeur, 0.4f));
        Personality fierce = With((Axis.Temperament, 0.9f), (Axis.Sociabilite, -0.6f), (Axis.Piete, -0.7f), (Axis.Ardeur, -0.5f), (Axis.Curiosite, -0.4f));
        Assert.True(Personality.Compatibility(calm, alsoCalm) > 0.8f);
        Assert.True(Personality.Compatibility(calm, fierce) < 0.2f);

        WorldState world = ClosedColony(6);
        Colony colony = world.Colonies[0];
        Colonist a = colony.Members[0], b = colony.Members[1], c = colony.Members[2];
        var friends = new Colonist(900, "Ami", Sex.Male, colony, a.Skills, 0, 0) { Personality = alsoCalm };
        var rival = new Colonist(901, "Rival", Sex.Male, colony, a.Skills, 0, 0) { Personality = fierce };
        var host = new Colonist(902, "Hôte", Sex.Female, colony, a.Skills, 0, 0) { Personality = calm };

        var random = new Random(3);
        var firstChange = Relations.Change.None;
        for (int i = 0; i < 30 && firstChange == Relations.Change.None; i++)
            firstChange = Relations.Converse(host, friends, random).Change;
        Assert.Equal(Relations.Change.BecameFriends, firstChange);
        Assert.Contains(friends.Id, host.Friends);
        Assert.Contains(host.Id, friends.Friends);
        Assert.Equal(Relations.Affinity(host, friends), Relations.Affinity(friends, host));

        firstChange = Relations.Change.None;
        for (int i = 0; i < 60 && firstChange == Relations.Change.None; i++)
            firstChange = Relations.Converse(host, rival, random).Change;
        Assert.Equal(Relations.Change.BecameRivals, firstChange);
        Assert.Contains(rival.Id, host.Rivals);
        _ = (b, c);
    }

    [Fact]
    public void Un_colon_seul_va_chercher_de_la_compagnie_le_soir_et_les_deux_y_gagnent()
    {
        WorldState world = ClosedColony(8);
        Colony colony = world.Colonies[0];

        // On avance jusqu'à 17 h, puis tout le monde manque de compagnie.
        while (world.Clock.Hour != 17)
            world.Step();
        foreach (Colonist colonist in colony.Members)
        {
            colonist.Needs.Social = 0.1f;
            colonist.Needs.Food = 1f;
            colonist.Needs.Rest = 1f;
        }

        bool sawChat = false;
        for (int i = 0; i < 4 * TimeConstants.TicksPerHour; i++)
        {
            world.Step();
            sawChat |= colony.Members.Any(m => m.Activity is { Kind: ActivityKind.Chat, Started: true });
        }

        output.WriteLine($"Compagnie moyenne : {colony.Members.Average(m => m.Needs.Social):P0}");
        Assert.True(sawChat, "Quelqu'un aurait dû aller bavarder.");
        Assert.True(colony.Members.Average(m => m.Needs.Social) > 0.2f);
        Assert.Contains(colony.Members, m => m.Affinities.Count > 0);
    }

    [Theory]
    [InlineData(12345)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Au_fil_des_jours_des_amities_et_des_rivalites_apparaissent(int seed)
    {
        WorldState world = ClosedColony(10, seed);
        Colony colony = world.Colonies[0];
        RunDays(world, 15);

        int friendships = colony.Members.Sum(m => m.Friends.Count) / 2;
        int rivalries = colony.Members.Sum(m => m.Rivals.Count) / 2;
        output.WriteLine($"Amitiés {friendships}, rivalités {rivalries}, compagnie moyenne {colony.Members.Average(m => m.Needs.Social):P0}, " +
                         $"confort moyen {colony.Members.Average(m => m.Needs.Comfort):P0}, humeur {colony.AverageMood:P0}");
        output.WriteLine($"Liens connus : {colony.Members.Sum(m => m.Affinities.Count) / 2}, affinité maximale {colony.Members.SelectMany(m => m.Affinities.Values).DefaultIfEmpty(0f).Max():0}, " +
                         $"minimale {colony.Members.SelectMany(m => m.Affinities.Values).DefaultIfEmpty(0f).Min():0}");
        foreach (Thought thought in colony.Thoughts.Where(t => t.Text.Contains("amis") || t.Text.Contains("rivalité")))
            output.WriteLine($"  {thought.Text}");

        Assert.True(friendships >= 1, "Au moins une amitié doit être née en quinze jours.");
        Assert.True(friendships + rivalries <= 10 * 9 / 2 / 2, "Tout le monde ne peut pas être ami (ou ennemi) avec tout le monde.");
        Assert.True(colony.Members.Average(m => m.Needs.Social) > 0.15f, "La compagnie ne doit pas s'effondrer.");
        // Toutes les relations sont réciproques.
        foreach (Colonist colonist in colony.Members)
        foreach (Colonist friend in colonist.FriendsIn(colony))
            Assert.Contains(colonist.Id, friend.Friends);
    }

    [Fact]
    public void Dans_une_grande_colonie_les_amities_dominent_et_les_rivalites_restent_rares()
    {
        int rivalries = 0, friendships = 0;
        foreach (int seed in new[] { 12345, 1, 2, 3, 4, 5, 6, 7 })
        {
            WorldState world = ClosedColony(20, seed);
            RunDays(world, 20);
            Colony colony = world.Colonies[0];
            rivalries += colony.Members.Sum(m => m.Rivals.Count) / 2;
            friendships += colony.Members.Sum(m => m.Friends.Count) / 2;
            foreach (Thought thought in colony.Thoughts.Where(t => t.Text.Contains("rivalité")))
                output.WriteLine($"  seed {seed}: {thought.Text}");
        }
        output.WriteLine($"Sur 8 colonies de 20 colons, 20 jours : {friendships} amitiés, {rivalries} rivalités");
        Assert.True(friendships > rivalries, "Il y a plus d'amitiés que de rivalités.");
    }

    [Fact]
    public void Le_confort_vient_d_un_toit_et_d_un_feu()
    {
        WorldState world = ClosedColony(6);
        Colony colony = world.Colonies[0];
        (int x, int y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
        Building hut = Urbanism.PlanHut(world.Map, colony, x, y);
        hut.Progress = 1f;
        colony.FillVacancies();

        // On avance jusqu'en hiver, puis on installe l'un sous un toit et l'autre dehors.
        var winter = TimeConstants.TicksPerDay * 17L;
        while (world.Clock.Ticks < winter)
            world.Step();
        foreach (Colonist member in colony.Members.Where(m => m.Home is not null).ToList())
        {
            member.Home!.Residents.Remove(member);
            member.Home = null;
        }
        Building shelter = colony.Buildings.First(b => b.IsComplete);
        Colonist housed = colony.Members[0], outdoors = colony.Members[1];
        housed.Home = shelter;
        shelter.Residents.Add(housed);

        // Le feu s'éteint : les sans-abri grelottent, les autres s'en sortent mieux.
        colony.FireLit = false;
        for (int i = 0; i < 12 * TimeConstants.TicksPerHour; i++)
            world.Step();
        output.WriteLine($"Confort en hiver sans feu : abrité {housed.Needs.Comfort:P0}, dehors {outdoors.Needs.Comfort:P0}");
        Assert.True(housed.Needs.Comfort > outdoors.Needs.Comfort + 0.2f);
    }

    [Fact]
    public void L_humeur_depend_de_la_compagnie_et_du_confort()
    {
        var happy = new Needs { Food = 0.8f, Rest = 0.8f, Leisure = 0.8f, Social = 0.9f, Comfort = 0.9f };
        var lonely = new Needs { Food = 0.8f, Rest = 0.8f, Leisure = 0.8f, Social = 0.0f, Comfort = 0.9f };
        var cold = new Needs { Food = 0.8f, Rest = 0.8f, Leisure = 0.8f, Social = 0.9f, Comfort = 0.1f };
        Assert.True(happy.Mood > lonely.Mood + 0.1f);
        Assert.True(happy.Mood > cold.Mood + 0.1f);
    }
}
