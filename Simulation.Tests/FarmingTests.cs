using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using GodColony.Simulation.Map;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class FarmingTests(ITestOutputHelper output)
{
    private static WorldState ClosedColony(int colonists) =>
        new(12345, startingColonists: colonists, migration: false, lifecycle: false);

    private static void RunDays(WorldState world, double days)
    {
        long ticks = (long)(days * TimeConstants.TicksPerDay);
        for (long i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void Les_champs_grandissent_avec_la_population_et_restent_exploitables()
    {
        // Un campement défriche petit, un village développé voit grand : la surface existe dans la simulation, pas seulement à l'écran.
        Assert.Equal(Field.BaseSize, Farming.FieldSizeFor(ClosedColony(8).Colonies[0]));
        Assert.Equal(6, Farming.FieldSizeFor(ClosedColony(20).Colonies[0]));

        WorldState world = ClosedColony(40);
        Colony colony = world.Colonies[0];
        Assert.Equal(Field.MaxSize, Farming.FieldSizeFor(colony));
        Assert.NotEmpty(colony.Fields);
        Assert.All(colony.Fields, f => Assert.Equal(Field.MaxSize, f.Size));
        Assert.All(colony.Fields, f => Assert.Equal(f.Size * f.Size, f.Plots.Count));
        Assert.Equal(colony.Fields.Sum(f => f.Plots.Count), Farming.PlotCount(colony));

        // Les parcelles sont distinctes, toutes dans l'emprise de leur champ, et le champ est accessible.
        var tiles = Farming.Plots(colony).Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(tiles.Count, tiles.Distinct().Count());
        foreach (Field field in colony.Fields)
        {
            Assert.All(field.Plots, p => Assert.True(field.Contains(p.X, p.Y)));
            Assert.True(field.AccessX >= 0 && !field.Contains(field.AccessX, field.AccessY));
        }

        // Elles se sèment et se récoltent réellement : le grain entre au stock.
        RunDays(world, 6 * TimeConstants.DaysPerSeason);
        Assert.True(colony.Labor.TotalProduced(ResourceType.Grain) > 0, "Les grands champs doivent donner une récolte.");
    }

    [Fact]
    public void Au_printemps_la_colonie_defriche_des_champs_selon_ses_besoins()
    {
        WorldState world = ClosedColony(10);
        Colony colony = world.Colonies[0];

        Assert.NotEmpty(colony.Fields);
        int plots = Farming.PlotCount(colony);
        Assert.InRange(plots, 10 * 8 * 0.4, 10 * 8 + Field.BaseSize * Field.BaseSize);

        // Les champs ne se chevauchent pas, restent hors des huttes et sont en terrain plat.
        var tiles = Farming.Plots(colony).Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(tiles.Count, tiles.Distinct().Count());
        foreach (Field field in colony.Fields)
        {
            int elevation = world.Map.GetElevation(field.X, field.Y);
            Assert.All(field.Plots, p => Assert.Equal(elevation, world.Map.GetElevation(p.X, p.Y)));
        }
    }

    [Fact]
    public void Une_parcelle_semee_murit_au_neuvieme_jour_et_le_gel_detruit_ce_qui_reste()
    {
        WorldState world = ClosedColony(6);
        Colony colony = world.Colonies[0];
        FieldPlot plot = Farming.Plots(colony).First();
        plot.Stage = CropStage.Growing;

        var clock = new GameClock(0);
        for (int day = 1; day <= 8; day++)
            Farming.DailyUpdate(colony, clock);
        Assert.Equal(CropStage.Growing, plot.Stage);
        Farming.DailyUpdate(colony, clock);
        Assert.Equal(CropStage.Ripe, plot.Stage);

        // Au premier jour d'hiver (jour 15 de l'année, en comptant depuis 0), ce qui n'est pas rentré gèle.
        var winter = new GameClock(TimeConstants.TicksPerDay * 15L);
        Assert.Equal(Season.Hiver, winter.Season);
        Assert.Equal(1, Farming.DailyUpdate(colony, winter));
        Assert.Equal(CropStage.Fallow, plot.Stage);

        // Rien ne pousse en hiver.
        plot.Stage = CropStage.Growing;
        var midWinter = new GameClock(TimeConstants.TicksPerDay * 17L);
        Farming.DailyUpdate(colony, midWinter);
        Assert.Equal(0f, plot.Growth);
    }

    [Theory]
    [InlineData(1f, false, false, 5)]
    [InlineData(1f, true, false, 8)]
    [InlineData(1f, true, true, 9)]
    [InlineData(1.25f, false, false, 6)]
    [InlineData(1.25f, true, true, 10)]
    [InlineData(1.5f, false, false, 8)]
    public void La_recolte_plus_abondante_applique_le_meme_facteur_aux_bonus(float richesse, bool irrigue, bool assolement, int attendu)
    {
        WorldState world = ClosedColony(6);
        Colony colony = world.Colonies[0];
        var map = new LocalMap(16, 16, 1) { SoilRichness = richesse };
        if (irrigue)
        {
            map.DigCanal(8, 8);
            map.FillCanal(8, 8);
        }
        if (assolement)
            Knowledge.Discover(colony, Discovery.CropRotation, world.Clock);

        Assert.Equal(attendu, Farming.YieldAt(colony, map, 8, 8));
        if (!assolement)
            Assert.Equal(attendu, Farming.YieldAt(map, 8, 8));
    }

    [Fact]
    public void La_colonie_seme_au_printemps_et_moissonne_avant_l_hiver()
    {
        WorldState world = ClosedColony(8);
        Colony colony = world.Colonies[0];
        int plots = Farming.Plots(colony).Count();

        int maxGrowing = 0, maxRipe = 0, maxGrain = 0;
        bool harvestAnnounced = false;
        for (int day = 1; day <= 20; day++)
        {
            RunDays(world, 1);
            // La colonie ne garde que ses trente dernières pensées : on guette l'annonce de la moisson au fil des jours.
            harvestAnnounced |= colony.Thoughts.Any(t => t.Text.Contains("moisson"));
            maxGrowing = Math.Max(maxGrowing, Farming.Plots(colony).Count(p => p.Stage == CropStage.Growing));
            maxRipe = Math.Max(maxRipe, Farming.Plots(colony).Count(p => p.Stage == CropStage.Ripe));
            maxGrain = Math.Max(maxGrain, colony.Stock.Get(ResourceType.Grain));
            if (day is 5 or 10 or 15 or 20)
                output.WriteLine($"J{day}: parcelles {plots}, semées {Farming.Plots(colony).Count(p => p.Stage != CropStage.Fallow)}, " +
                                 $"mûres {Farming.Plots(colony).Count(p => p.Stage == CropStage.Ripe)}, céréales {colony.Stock.Get(ResourceType.Grain)}, " +
                                 $"nourriture {colony.Stock.Get(ResourceType.Food)}, humeur {colony.AverageMood:P0}");
        }
        output.WriteLine("Coûts : " + ColonyBrain.CostSummary(colony.Labor));
        foreach (Thought thought in colony.Thoughts.Where(t => t.Text.Contains("champ") || t.Text.Contains("parcelle") || t.Text.Contains("moisson") || t.Text.Contains("semailles") || t.Text.Contains("céréales")))
            output.WriteLine($"  J{thought.Ticks / TimeConstants.TicksPerDay + 1}  {thought.Text}");

        Assert.True(maxGrowing >= plots / 2, "La plupart des parcelles devraient être semées.");
        Assert.True(maxRipe > 0, "Les céréales doivent arriver à maturité.");
        Assert.True(colony.Labor.TotalProduced(ResourceType.Grain) >= plots, "Au moins une récolte par parcelle en moyenne.");
        Assert.NotNull(colony.Labor.HoursPerUnit(ResourceType.Grain));
        Assert.True(harvestAnnounced, "La colonie doit annoncer la moisson.");
        Assert.True(colony.Members.Average(m => m.Needs.Food) > 0.3f);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    public void Une_petite_colonie_fermee_traverse_deux_ans_grace_a_ses_champs(int colonists)
    {
        WorldState world = ClosedColony(colonists);
        Colony colony = world.Colonies[0];
        var watch = new StarvationWatch();

        for (int day = 1; day <= 40; day++)
        {
            RunDays(world, 1);
            watch.Observe(colony);
            if (day % 5 == 0)
                output.WriteLine($"J{day}: colons {colony.Members.Count}, nourriture {colony.Stock.Get(ResourceType.Food)}, " +
                                 $"céréales {colony.Stock.Get(ResourceType.Grain)}, parcelles {Farming.Plots(colony).Count()}, " +
                                 $"faim minimale {colony.Members.Min(m => m.Needs.Food):P0}, humeur {colony.AverageMood:P0}");
        }

        Assert.Equal(colonists, colony.Members.Count);
        Assert.Null(watch.Victim);
    }

    [Fact]
    public void Les_huttes_ne_sont_pas_batties_sur_les_champs()
    {
        WorldState world = ClosedColony(10);
        Colony colony = world.Colonies[0];
        var fieldTiles = Farming.Plots(colony).Select(p => (p.X, p.Y)).ToHashSet();
        for (int i = 0; i < 6; i++)
        {
            if (Urbanism.FindHutSite(world.Map, colony) is not { } site)
                break;
            Building hut = Urbanism.PlanHut(world.Map, colony, site.X, site.Y);
            Assert.DoesNotContain(hut.Tiles, t => fieldTiles.Contains(t));
        }
    }
}
