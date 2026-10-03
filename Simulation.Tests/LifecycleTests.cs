using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class LifecycleTests(ITestOutputHelper output)
{
    private static WorldState Colony(int colonists, int seed = 12345, bool migration = false) =>
        new(seed, startingColonists: colonists, migration: migration, lifecycle: true);

    private static void RunDays(WorldState world, double days)
    {
        long ticks = (long)(days * TimeConstants.TicksPerDay);
        for (long i = 0; i < ticks; i++)
            world.Step();
    }

    private static void SetAge(Colonist colonist, float years) =>
        typeof(Colonist).GetProperty(nameof(Colonist.BirthTicks))!.SetValue(colonist,
            colonist.Colony.Clock.Ticks - (long)(years * TimeConstants.TicksPerYear));

    private static (Colonist Woman, Colonist Man) Singles(Colony colony)
    {
        Colonist woman = colony.Members.First(m => m.Sex == Sex.Female);
        Colonist man = colony.Members.First(m => m.Sex == Sex.Male);
        return (woman, man);
    }

    private static void Bond(Colonist a, Colonist b, float affinity)
    {
        a.Affinities[b.Id] = affinity;
        b.Affinities[a.Id] = affinity;
    }

    [Fact]
    public void Une_colonie_commence_avec_des_adultes_seulement_sans_couple_ni_tombe()
    {
        foreach (int seed in new[] { 1, 2, 3, 12345 })
        {
            WorldState world = Colony(8, seed);
            Colony colony = world.Colonies[0];
            Assert.All(colony.Members, m =>
            {
                Assert.Equal(LifeStage.Adult, m.Stage);
                Assert.InRange(m.AgeYears, Colonist.AdultAge, Colonist.AdultAge + 6.01f);
                Assert.Null(m.Partner);
                Assert.NotEmpty(m.Surname);
            });
            Assert.Equal(0, colony.Children);
            Assert.Empty(colony.Graves);
        }
    }

    [Fact]
    public void Les_ages_de_la_vie_suivent_le_document_de_conception()
    {
        WorldState world = Colony(6);
        Colonist colonist = world.Colonies[0].Members[0];
        SetAge(colonist, 1f);
        Assert.Equal(LifeStage.Child, colonist.Stage);
        SetAge(colonist, 4f);
        Assert.Equal(LifeStage.Teen, colonist.Stage);
        SetAge(colonist, 10f);
        Assert.Equal(LifeStage.Adult, colonist.Stage);
        SetAge(colonist, 17f);
        Assert.Equal(LifeStage.Elder, colonist.Stage);
    }

    [Fact]
    public void Un_couple_ne_se_forme_qu_entre_un_homme_et_une_femme_qui_s_entendent_bien()
    {
        WorldState world = Colony(8);
        Colony colony = world.Colonies[0];
        var women = colony.Members.Where(m => m.Sex == Sex.Female).ToList();
        var men = colony.Members.Where(m => m.Sex == Sex.Male).ToList();

        // Deux femmes qui s'adorent ne forment pas un couple ; deux hommes non plus.
        Bond(women[0], women[1], 95f);
        Bond(men[0], men[1], 95f);
        // Un homme et une femme tout juste amis (affinité trop faible) non plus.
        Bond(women[2], men[2], 45f);
        Lifecycle.FormCouples(world, colony);
        Assert.All(colony.Members, m => Assert.Null(m.Partner));

        // En revanche, un homme et une femme très proches, de caractères compatibles, oui.
        Bond(women[3], men[3], 90f);
        SetCompatible(women[3], men[3]);
        Lifecycle.FormCouples(world, colony);
        Assert.Same(men[3], women[3].Partner);
        Assert.Same(women[3], men[3].Partner);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("forment un couple"));
    }

    private static void SetCompatible(Colonist a, Colonist b)
    {
        // On aligne la personnalité de b sur celle de a.
        var field = typeof(Personality).GetField("_value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var source = (float[])field.GetValue(a.Personality)!;
        var target = (float[])field.GetValue(b.Personality)!;
        Array.Copy(source, target, source.Length);
    }

    [Fact]
    public void Des_proches_parents_ne_forment_pas_un_couple()
    {
        WorldState world = Colony(8);
        Colony colony = world.Colonies[0];
        Colonist father = colony.Members.First(m => m.Sex == Sex.Male);
        Colonist mother = colony.Members.First(m => m.Sex == Sex.Female);
        mother.PregnancyFather = father;
        Colonist daughter = Lifecycle.GiveBirth(world, mother);
        // Elle est adulte pour les besoins du test.
        SetAge(daughter, 8f);
        daughter.Needs.Food = 1f;

        // Un fils du même père (demi-frère) : parenté proche aussi.
        Bond(father, daughter, 99f);
        Assert.True(Colonist.AreKin(father, daughter));
        Lifecycle.FormCouples(world, colony);
        Assert.Null(daughter.Partner);
    }

    [Fact]
    public void Un_couple_attend_un_an_entre_deux_naissances()
    {
        WorldState world = Colony(6);
        Colony colony = world.Colonies[0];
        (Colonist woman, Colonist man) = Singles(colony);
        woman.Partner = man;
        man.Partner = woman;
        long now = world.Clock.Ticks;
        long due = now + (long)(Lifecycle.PregnancyDays * TimeConstants.TicksPerDay);

        Assert.True(Lifecycle.CanConceive(woman, due));

        // Elle vient d'accoucher : un nouvel enfant ne peut pas naître avant un an.
        woman.LastBirthTicks = now;
        Assert.False(Lifecycle.CanConceive(woman, due));
        // La naissance suivante arriverait après 0,9 an : trop tôt. Après un an pile : permis
        // (on peut donc concevoir cinq jours avant le premier anniversaire de la naissance précédente).
        Assert.False(Lifecycle.CanConceive(woman, now + (long)(0.9f * TimeConstants.TicksPerYear)));
        Assert.True(Lifecycle.CanConceive(woman, now + TimeConstants.TicksPerYear));

        // Pas de couple, pas d'enfant ; pas enceinte deux fois ; pas trop âgée.
        woman.LastBirthTicks = long.MinValue / 2;
        woman.PregnantUntilTicks = due;
        Assert.False(Lifecycle.CanConceive(woman, due));
        woman.PregnantUntilTicks = null;
        woman.Partner = null;
        Assert.False(Lifecycle.CanConceive(woman, due));
    }

    [Fact]
    public void En_temps_de_crise_les_couples_renoncent_aux_enfants()
    {
        WorldState world = Colony(8);
        Colony colony = world.Colonies[0];
        var women = colony.Members.Where(m => m.Sex == Sex.Female).Take(3).ToList();
        var men = colony.Members.Where(m => m.Sex == Sex.Male).Take(3).ToList();
        for (int i = 0; i < 3; i++)
        {
            women[i].Partner = men[i];
            men[i].Partner = women[i];
        }

        // Famine : plus rien à manger, tout le monde est mal.
        colony.Stock.TryTake(ResourceType.Food, colony.Stock.Get(ResourceType.Food));
        foreach (Colonist colonist in colony.Members)
        {
            colonist.Needs.Food = 0.5f;
            colonist.Needs.Leisure = 0.2f;
        }
        Assert.True(Lifecycle.Prosperity(colony, world.Clock) < 0.05f);
        for (int day = 0; day < 40; day++)
        {
            // On ne laisse pas la simulation relancer les récoltes : on appelle seulement la vie.
            Lifecycle.Daily(world, colony);
            colony.Stock.TryTake(ResourceType.Food, colony.Stock.Get(ResourceType.Food));
        }
        Assert.All(women, w => Assert.Null(w.PregnantUntilTicks));

        // Prospérité : des grossesses surviennent.
        colony.Stock.Add(ResourceType.Food, 5000);
        foreach (Colonist colonist in colony.Members)
        {
            colonist.Needs.Food = 1f;
            colonist.Needs.Rest = 1f;
            colonist.Needs.Leisure = 1f;
            colonist.Needs.Social = 1f;
            colonist.Needs.Comfort = 1f;
        }
        (int x, int y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
        Building hut = Urbanism.PlanHut(world.Map, colony, x, y);
        hut.Progress = 1f;
        for (int i = 0; i < 3; i++)
        {
            (x, y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
            Building more = Urbanism.PlanHut(world.Map, colony, x, y);
            more.Progress = 1f;
        }
        colony.FillVacancies();
        output.WriteLine($"Prospérité : {Lifecycle.Prosperity(colony, world.Clock):P0}");
        for (int day = 0; day < 30 && women.All(w => w.PregnantUntilTicks is null); day++)
            Lifecycle.Daily(world, colony);
        Assert.Contains(women, w => w.PregnantUntilTicks is not null);
    }

    [Fact]
    public void Un_enfant_herite_de_ses_parents_et_porte_le_nom_de_son_pere()
    {
        WorldState world = Colony(8);
        Colony colony = world.Colonies[0];
        (Colonist mother, Colonist father) = Singles(colony);
        mother.Partner = father;
        father.Partner = mother;
        mother.PregnantUntilTicks = world.Clock.Ticks;
        mother.PregnancyFather = father;
        int before = colony.Members.Count;

        Colonist child = Lifecycle.GiveBirth(world, mother);

        Assert.Equal(before + 1, colony.Members.Count);
        Assert.Equal(LifeStage.Child, child.Stage);
        Assert.InRange(child.AgeYears, 0f, 0.01f);
        Assert.Equal(father.Surname, child.Surname);
        Assert.Same(mother, child.Mother);
        Assert.Same(father, child.Father);
        Assert.Contains(child, mother.Children);
        Assert.Contains(child, father.Children);
        Assert.Null(mother.PregnantUntilTicks);
        Assert.Equal(WorkSector.Free, child.Sector);
        Assert.All(Skills.All, s => Assert.Equal(0f, child.Skills.Level(s)));
        Assert.All(Skills.All, s => Assert.InRange(child.Skills.Talent(s), 0.5f, 1.5f));
        Assert.All(Personality.All, a => Assert.InRange(child.Personality[a], -1f, 1f));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains(child.Name));

        // Les enfants ne travaillent pas : ils ne comptent pas parmi les bras de la colonie.
        colony.AssignSectors();
        Assert.DoesNotContain(child, colony.Workers);
        Assert.Equal(WorkSector.Free, child.Sector);
        Assert.Equal(1, colony.Children);
    }

    [Fact]
    public void Le_talent_des_enfants_est_la_moyenne_de_celui_des_parents()
    {
        var random = new Random(5);
        WorldState world = Colony(8);
        Colony colony = world.Colonies[0];
        (Colonist mother, Colonist father) = Singles(colony);
        double total = 0, expected = 0;
        for (int i = 0; i < 500; i++)
        {
            Skills child = Skills.Inherit(random, mother.Skills, father.Skills);
            foreach (SkillType skill in Skills.All)
            {
                total += child.Talent(skill);
                expected += (mother.Skills.Talent(skill) + father.Skills.Talent(skill)) / 2f;
            }
        }
        Assert.InRange(total / expected, 0.97, 1.03);
    }

    [Fact]
    public void Au_dela_de_dix_sept_ans_on_finit_par_mourir_de_vieillesse_et_on_est_enterre()
    {
        Assert.Equal(0f, Lifecycle.OldAgeDeathChance(16.9f));
        Assert.True(Lifecycle.OldAgeDeathChance(19f) < Lifecycle.OldAgeDeathChance(21f));
        Assert.Equal(1f, Lifecycle.OldAgeDeathChance(23f));

        WorldState world = Colony(8);
        Colony colony = world.Colonies[0];
        (Colonist woman, Colonist man) = Singles(colony);
        woman.Partner = man;
        man.Partner = woman;
        Colonist friend = colony.Members.First(m => m != woman && m != man);
        woman.Friends.Add(friend.Id);
        friend.Friends.Add(woman.Id);
        SetAge(woman, 23.5f);
        int before = colony.Members.Count;

        Lifecycle.Daily(world, colony);

        Assert.DoesNotContain(woman, colony.Members);
        Assert.Equal(before - 1, colony.Members.Count);
        Grave grave = Assert.Single(colony.Graves);
        Assert.Contains(woman.Name, grave.FullName);
        Assert.Equal("vieillesse", grave.Cause);
        Assert.True(grave.X >= 0, "Le cimetière doit avoir trouvé une place.");
        Assert.Null(man.Partner);
        Assert.Equal(1f, man.Needs.Grief);
        Assert.True(friend.Needs.Grief > 0.3f);
        Assert.True(man.Needs.Mood < 0.75f);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("s'éteint de vieillesse"));
    }

    [Fact]
    public void Deux_jours_sans_rien_manger_c_est_la_mort_apres_un_avertissement()
    {
        WorldState world = Colony(6);
        Colony colony = world.Colonies[0];
        Colonist starving = colony.Members[0];
        for (int hour = 0; hour < Lifecycle.StarvationWarningHours; hour++)
        {
            starving.Needs.Food = 0f;
            Lifecycle.Hourly(world, colony);
        }
        Assert.Contains(starving, colony.Members);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("n'a rien mangé depuis un jour"));

        for (int hour = Lifecycle.StarvationWarningHours; hour < Lifecycle.StarvationDeathHours; hour++)
        {
            starving.Needs.Food = 0f;
            Lifecycle.Hourly(world, colony);
        }
        Assert.DoesNotContain(starving, colony.Members);
        Assert.Equal("faim", colony.Graves.Single().Cause);

        // Un repas remet les compteurs à zéro.
        Colonist other = colony.Members[0];
        other.Needs.Food = 0f;
        for (int hour = 0; hour < 40; hour++)
            Lifecycle.Hourly(world, colony);
        other.Needs.Food = 0.5f;
        Lifecycle.Hourly(world, colony);
        Assert.Contains(other, colony.Members);
        Assert.Equal(0, other.StarvedHours);
    }

    [Fact]
    public void Les_tombes_restent_a_l_ecart_et_ne_se_touchent_pas()
    {
        WorldState world = Colony(8);
        Colony colony = world.Colonies[0];
        foreach (Colonist colonist in colony.Members.Take(5).ToList())
            Lifecycle.Die(world, colonist, "vieillesse");

        Assert.Equal(5, colony.Graves.Count);
        Assert.All(colony.Graves, g => Assert.True(g.X >= 0));
        foreach (Grave a in colony.Graves)
        foreach (Grave b in colony.Graves.Where(g => g != a))
            Assert.True(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) >= 2);
        Assert.All(colony.Graves, g => Assert.True(Math.Max(Math.Abs(g.X - colony.CampX), Math.Abs(g.Y - colony.CampY)) >= 8));
    }

    [Fact]
    public void Les_adolescents_travaillent_a_mi_temps_et_les_anciens_a_soixante_dix_pour_cent()
    {
        WorldState world = Colony(6);
        Colony colony = world.Colonies[0];
        Colonist colonist = colony.Members[0];

        // On compare le temps qu'il met à couper un arbre : la durée de l'activité varie avec l'âge.
        float AdultSeconds(float age)
        {
            SetAge(colonist, age);
            colonist.Activity = null;
            colonist.Sector = WorkSector.Wood;
            colonist.Needs.Food = 1f;
            colonist.Needs.Rest = 1f;
            while (world.Clock.Hour != 8)
                world.Step();
            for (int i = 0; i < 400 && colonist.Activity is not { Kind: ActivityKind.Chop }; i++)
            {
                colonist.Activity = null;
                world.Step();
            }
            return colonist.Activity is { Kind: ActivityKind.Chop } chop ? chop.DurationTicks : float.NaN;
        }

        float adult = AdultSeconds(10f);
        float teen = AdultSeconds(4f);
        float elder = AdultSeconds(18f);
        output.WriteLine($"Durée d'abattage (ticks) : adulte {adult}, adolescent {teen}, ancien {elder}");
        Assert.True(teen > adult * 1.8f, "Un adolescent met environ deux fois plus de temps.");
        Assert.InRange(elder / adult, 1.3f, 1.6f);
    }

    [Fact]
    public void Une_colonie_traverse_les_generations_avec_naissances_et_deces()
    {
        WorldState world = new(12345, startingColonists: 8, migration: true, lifecycle: true);
        Colony colony = world.Colonies[0];
        int peak = colony.Members.Count;
        int births = 0;
        var seen = new HashSet<int>(colony.Members.Select(m => m.Id));

        for (int day = 1; day <= 160; day++)
        {
            RunDays(world, 1);
            peak = Math.Max(peak, colony.Members.Count);
            foreach (Colonist member in colony.Members.Where(m => m.Mother is not null && seen.Add(m.Id)))
                births++;
            seen.UnionWith(colony.Members.Select(m => m.Id));
            if (day % 20 == 0)
                output.WriteLine($"An {day / 20}: colons {colony.Members.Count} (enfants {colony.Children}), couples {colony.Members.Count(m => m.Sex == Sex.Female && m.Partner is not null)}, " +
                                 $"naissances {births}, tombes {colony.Graves.Count}, nourriture {colony.Stock.FoodUnits}, humeur {colony.AverageMood:P0}, huttes {colony.Buildings.Count(b => b.IsComplete)}");
        }
        foreach (Thought thought in colony.Thoughts.TakeLast(10))
            output.WriteLine($"  J{thought.Ticks / TimeConstants.TicksPerDay + 1} {thought.Text}");

        Assert.True(births >= 3, "Des couples doivent avoir des enfants.");
        Assert.True(colony.Members.Count >= 4, "La colonie ne doit pas s'éteindre.");
        Assert.Contains(colony.Members, m => m.Mother is not null && m.Stage != LifeStage.Child);
    }
}
