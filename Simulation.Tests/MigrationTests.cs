using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class MigrationTests(ITestOutputHelper output)
{
    private static void RunDays(WorldState world, double days)
    {
        long ticks = (long)(days * TimeConstants.TicksPerDay);
        for (long i = 0; i < ticks; i++)
            world.Step();
    }

    private static void Set(Colony colony, ResourceType type, int amount)
    {
        colony.Stock.TryTake(type, colony.Stock.Get(type));
        colony.Stock.Add(type, amount);
    }

    [Fact]
    public void Une_colonie_demarre_avec_cinq_a_dix_colons()
    {
        var sizes = new HashSet<int>();
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            int count = new WorldState(seed).Colonies[0].Members.Count;
            Assert.InRange(count, 5, 10);
            sizes.Add(count);
        }
        Assert.True(sizes.Count > 1, "La taille de départ doit varier d'une partie à l'autre.");
    }

    [Fact]
    public void Les_prenoms_d_une_colonie_sont_uniques()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Assert.Equal(colony.Members.Count, colony.Members.Select(m => m.Name).Distinct().Count());

        // Même quand tous les prénoms sont pris, on en trouve un nouveau.
        var taken = new List<string>();
        var random = new Random(1);
        for (int i = 0; i < 60; i++)
            taken.Add(Names.Pick(Sex.Female, random, taken));
        Assert.Equal(60, taken.Distinct().Count());
    }

    [Fact]
    public void Plus_la_colonie_est_prospere_plus_elle_attire_de_voyageurs()
    {
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        BuildHut(world, colony);

        Set(colony, ResourceType.Food, 0);
        Set(colony, ResourceType.Wood, 0);
        float poor = Migration.TravelerChancePerDay(colony, world.Clock);

        Set(colony, ResourceType.Food, 6 * 6);
        Set(colony, ResourceType.Wood, 20);
        float modest = Migration.TravelerChancePerDay(colony, world.Clock);

        Set(colony, ResourceType.Food, 500);
        Set(colony, ResourceType.Wood, 300);
        Set(colony, ResourceType.Stone, 300);
        float rich = Migration.TravelerChancePerDay(colony, world.Clock);

        output.WriteLine($"Chance par jour : pauvre {poor:P0}, modeste {modest:P0}, prospère {rich:P0}");
        Assert.True(poor < modest && modest < rich);
        Assert.True(poor < 0.2f, "Un stock vide attire peu de monde (et la faim ferait vite chuter l’humeur).");
        Assert.InRange(rich, 0.3f, Migration.MaxTravelerChancePerDay);

        // Des colons malheureux font fuir les voyageurs, même dans l'abondance.
        foreach (Colonist colonist in colony.Members)
        {
            colonist.Needs.Food = 0.1f;
            colonist.Needs.Rest = 0.2f;
            colonist.Needs.Leisure = 0.1f;
        }
        Assert.True(Migration.TravelerChancePerDay(colony, world.Clock) < rich / 2);
        Assert.Equal(0, Migration.Welcome(world, colony, 1));
    }

    [Fact]
    public void Un_voyageur_est_refuse_quand_les_reserves_sont_vides()
    {
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Set(colony, ResourceType.Food, 0);

        Assert.Equal(0, Migration.Welcome(world, colony, 1));
        Assert.Empty(colony.Transients);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("nous le renvoyons"));
    }

    [Fact]
    public void Un_voyageur_est_refuse_quand_personne_n_a_de_toit_a_lui_offrir()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Set(colony, ResourceType.Food, 1000);

        Assert.Equal(0, Migration.Welcome(world, colony, 1));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("toit"));
    }

    private static void BuildHut(WorldState world, Colony colony)
    {
        (int x, int y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
        Building hut = Urbanism.PlanHut(world.Map, colony, x, y);
        hut.Progress = 1f;
        colony.FillVacancies();
    }

    [Fact]
    public void Un_voyageur_accueilli_marche_jusqu_au_camp_et_rejoint_la_colonie()
    {
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Set(colony, ResourceType.Food, 1000);
        BuildHut(world, colony);

        Assert.Equal(1, Migration.Welcome(world, colony, 1));
        Colonist traveler = Assert.Single(colony.Transients);
        Assert.Equal(TransitState.Arriving, traveler.Transit);
        Assert.DoesNotContain(traveler, colony.Members);
        Assert.Equal(6, colony.Members.Count);

        for (int i = 0; i < 10 * TimeConstants.TicksPerSecond * 60 && colony.Transients.Count > 0; i++)
            world.Step();

        Assert.Empty(colony.Transients);
        Assert.Equal(7, colony.Members.Count);
        Assert.Contains(traveler, colony.Members);
        Assert.Equal(TransitState.None, traveler.Transit);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains(traveler.Name) && t.Text.Contains("rejoint la colonie"));

        // Un voyageur arrive avec un métier déjà maîtrisé.
        Assert.True(Skills.All.Max(traveler.Skills.Level) >= 8f);
    }

    [Fact]
    public void Un_voyageur_prend_la_place_libre_d_une_hutte()
    {
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Set(colony, ResourceType.Food, 1000);

        // Une hutte vide, déjà bâtie.
        (int x, int y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
        Building hut = Urbanism.PlanHut(world.Map, colony, x, y);
        hut.Progress = 1f;
        colony.FillVacancies();
        Assert.Equal(4, hut.Residents.Count);
        Assert.Equal(2, colony.Homeless);

        // Un colon part : sa place est reprise par quelqu'un qui dormait dehors.
        Colonist leaver = hut.Residents[0];
        ColonistAI.BeginDeparture(leaver, world);
        Assert.Equal(4, hut.Residents.Count);
        Assert.DoesNotContain(leaver, hut.Residents);
        Assert.Equal(1, colony.Homeless);
    }

    [Fact]
    public void Un_colon_malheureux_depuis_trop_longtemps_quitte_la_colonie()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Colonist unhappy = colony.Members[0];
        unhappy.Needs.Food = 0f;
        unhappy.Needs.Rest = 0.1f;
        unhappy.Needs.Leisure = 0f;
        unhappy.Needs.Social = 0f;
        unhappy.Needs.Comfort = 0f;
        unhappy.UnhappyHours = Migration.UnhappyHoursBeforeLeaving * 2; // même un enraciné a atteint sa limite

        Migration.Hourly(world, colony);

        Assert.DoesNotContain(unhappy, colony.Members);
        Assert.Equal(7, colony.Members.Count);
        Assert.Equal(TransitState.Leaving, unhappy.Transit);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains(unhappy.Name) && t.Text.Contains("quitte la colonie"));

        for (int i = 0; i < 10 * TimeConstants.TicksPerSecond * 60 && colony.Transients.Count > 0; i++)
            world.Step();
        Assert.Empty(colony.Transients);
    }

    [Fact]
    public void Un_colon_qui_part_rend_ce_qu_il_portait_et_libere_le_chantier()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        (int x, int y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
        Building site = Urbanism.PlanHut(world.Map, colony, x, y);

        Colonist carrier = colony.Members[0];
        carrier.Carrying = (ResourceType.Wood, 5);
        carrier.CarryingTo = site;
        site.WoodInTransit = 5;
        int woodBefore = colony.Stock.Get(ResourceType.Wood);

        ColonistAI.BeginDeparture(carrier, world);

        Assert.Equal(0, site.WoodInTransit);
        Assert.Equal(woodBefore + 5, colony.Stock.Get(ResourceType.Wood));
        Assert.Null(carrier.Carrying);
    }

    [Fact]
    public void Une_petite_colonie_prospere_attire_des_voyageurs_et_survit_deux_ans()
    {
        var world = new WorldState(12345, startingColonists: 6);
        Colony colony = world.Colonies[0];
        int founders = colony.Members.Count;
        int peak = founders;
        int minPopulation = founders;

        for (int day = 1; day <= 40; day++)
        {
            RunDays(world, 1);
            peak = Math.Max(peak, colony.Members.Count);
            minPopulation = Math.Min(minPopulation, colony.Members.Count);
        }

        output.WriteLine($"Fondateurs {founders}, maximum {peak}, minimum {minPopulation}, final {colony.Members.Count}");
        output.WriteLine($"Humeur {colony.AverageMood:P0}, nourriture {colony.Stock.Get(ResourceType.Food)}, bois {colony.Stock.Get(ResourceType.Wood)}, " +
                         $"huttes {colony.Buildings.Count(b => b.IsComplete)}, sans toit {colony.Homeless}");
        foreach (Thought thought in colony.Thoughts.TakeLast(12))
            output.WriteLine($"  J{thought.Ticks / TimeConstants.TicksPerDay + 1}  {thought.Text}");

        Assert.True(colony.Members.Count > founders, "Des voyageurs auraient dû rejoindre la colonie.");
        Assert.True(minPopulation >= Migration.MinPopulationToLeave);
        Assert.True(colony.Members.Average(m => m.Needs.Food) > 0.3f, "Personne ne doit mourir de faim à petit feu.");
    }
}
