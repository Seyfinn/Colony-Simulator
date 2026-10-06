using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;
using Xunit;

namespace GodColony.Simulation.Tests;

/// <summary>Les raccourcis décidés par les habitants : flux mesurés, étude reprenable sous budget, admission revalidée, aucun chemin gratuit.</summary>
public sealed class RoadShortcutTests
{
    private static (WorldState World, Colony Colony, Settlement Place, Building Oven) Village()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Building oven = Urbanism.BuildInstantly(colony.Map, colony, BuildingType.Oven)!;
        Assert.NotNull(oven);
        colony.Stock.Add(ResourceType.Bread, 3000);
        colony.Stock.Add(ResourceType.Wood, 600);
        colony.Sensors = ColonyBrain.Sense(colony, world.Clock);
        Assert.True(RoadWorks.IsAllowed(colony));
        return (world, colony, colony.PrimarySettlement, oven);
    }

    private static TravelFlow AddFlow(Settlement place, int a, int b, int trips, double seconds, double cells, long day)
    {
        var flow = new TravelFlow { A = a, B = b, LastDay = day };
        flow.TripsByDay[day] = trips;
        flow.TicksByDay[day] = (long)(trips * seconds * TimeConstants.TicksPerSecond);
        flow.CellsByDay[day] = (long)(trips * cells);
        place.TravelFlows.Add(flow);
        return flow;
    }

    private static void Review(WorldState world, Settlement place)
    {
        using var scope = place.Owner.UseSettlement(place);
        RoadShortcuts.ReviewDaily(world, place);
    }

    private static void Complete(WorldState world) => SettlementPlanningScheduler.Drain(world);

    [Fact]
    public void Seuls_les_trajets_termines_et_assez_longs_entrent_dans_un_flux()
    {
        var (world, colony, place, oven) = Village();
        Colonist walker = colony.Members[0];
        walker.PathStartBuilding = -0; walker.PathStartX = colony.CampX; walker.PathStartY = colony.CampY;
        walker.PathGoalBuilding = oven.Id;
        walker.PathCommittedTicks = world.Clock.Ticks - 100;
        walker.Path = Enumerable.Range(0, 12).Select(i => (colony.CampX + i, colony.CampY)).ToList();

        RoadShortcuts.RecordCompletedTrip(colony, walker, world.Clock.Ticks);
        TravelFlow flow = Assert.Single(place.TravelFlows);
        Assert.Equal((-1, oven.Id, 1, 100L, 12L), (flow.A, flow.B, flow.Trips, flow.TicksByDay.Values.Sum(), flow.CellsByDay.Values.Sum()));
        Assert.Equal(5.0, flow.AverageSeconds, 6);

        RoadShortcuts.RecordCompletedTrip(colony, walker, world.Clock.Ticks); // le même couple, dans l'autre sens, additionne
        Assert.Equal(2, Assert.Single(place.TravelFlows).Trips);

        walker.Path = walker.Path.Take(3).ToList(); // un trajet très court n'est pas un détour
        RoadShortcuts.RecordCompletedTrip(colony, walker, world.Clock.Ticks);
        walker.Path = Enumerable.Range(0, 12).Select(i => (colony.CampX + i, colony.CampY)).ToList();
        walker.PathCommittedTicks = world.Clock.Ticks + 5; // un trajet sans durée réelle n'existe pas
        RoadShortcuts.RecordCompletedTrip(colony, walker, world.Clock.Ticks);
        walker.PathGoalBuilding = 0; walker.PathStartBuilding = 0; walker.PathStartX = colony.CampX + 40; // aucune extrémité stable
        RoadShortcuts.RecordCompletedTrip(colony, walker, world.Clock.Ticks);
        Assert.Equal(2, Assert.Single(place.TravelFlows).Trips);
    }

    [Fact]
    public void Les_flux_sont_bornes_a_256_couples_et_expirent_apres_quatorze_jours()
    {
        var (world, colony, place, _) = Village();
        Colonist walker = colony.Members[0];
        walker.Path = Enumerable.Range(0, 12).Select(i => (colony.CampX + i, colony.CampY)).ToList();
        for (int i = 1; i <= RoadShortcuts.MaxFlows + 20; i++)
        {
            walker.PathStartBuilding = i; walker.PathGoalBuilding = 10_000 + i; walker.PathCommittedTicks = world.Clock.Ticks - 50;
            RoadShortcuts.RecordCompletedTrip(colony, walker, world.Clock.Ticks);
        }
        Assert.Equal(RoadShortcuts.MaxFlows, place.TravelFlows.Count);
        // Même jour : l'éviction départage par clés, les plus petites d'abord ; les couples récents restent.
        Assert.DoesNotContain(place.TravelFlows, f => f.A == 1);
        Assert.Contains(place.TravelFlows, f => f.A == RoadShortcuts.MaxFlows + 20);

        for (int day = 0; day < RoadShortcuts.WindowDays + 1; day++)
            for (int tick = 0; tick < TimeConstants.TicksPerDay; tick++) world.Clock.Advance();
        Review(world, place);
        Assert.Empty(place.TravelFlows); // plus aucun trajet dans la fenêtre : les flux s'effacent
    }

    [Fact]
    public void Un_detour_mesure_ouvre_une_seule_etude_hors_crise_et_hors_autre_ouvrage()
    {
        var (world, colony, place, oven) = Village();
        long today = world.Clock.TotalDays;
        TravelFlow flow = AddFlow(place, -1, oven.Id, trips: 40, seconds: 300, cells: 200, today);

        // Crise : la faim passe avant ; l'attente est conservée.
        colony.Sensors = colony.Sensors! with { FoodDays = 0.5f, FoodPressure = 90 };
        Review(world, place);
        Assert.Empty(world.Planning.Jobs);
        colony.Sensors = ColonyBrain.Sense(colony, world.Clock);

        // Un ouvrage facultatif déjà actif : rien ne s'ouvre.
        var active = new DevelopmentProject(colony.Layout.NextProjectId++, DevelopmentKind.RoadImprovement, DevelopmentPriority.Comfort, -1, world.Clock.Ticks, "road");
        colony.Layout.Projects.Add(active);
        Review(world, place);
        Assert.Empty(world.Planning.Jobs);
        active.State = ProjectState.Completed;

        Review(world, place);
        PlanningJob job = Assert.Single(world.Planning.Jobs);
        Assert.Equal(DevelopmentKind.RoadShortcut, job.Kind);
        Assert.Equal((-1, oven.Id, flow.Id()), (job.ShortcutA, job.ShortcutB, job.Id));
        Assert.Equal(job.Id, flow.StudyJobId);
        Assert.Equal(ShortcutVerdict.Studying, flow.Verdict);

        Review(world, place); // pas de doublon pendant l'étude
        Assert.Single(world.Planning.Jobs);
    }

    [Fact]
    public void Un_raccourci_rentable_ouvre_un_projet_sans_rendre_aucune_cellule_amenagee_gratuitement()
    {
        var (world, colony, place, oven) = Village();
        AddFlow(place, -1, oven.Id, trips: 40, seconds: 300, cells: 200, world.Clock.TotalDays);
        Review(world, place);
        PlanningJob job = Assert.Single(world.Planning.Jobs);
        Complete(world);

        TravelFlow flow = Assert.Single(place.TravelFlows);
        Assert.Equal(ShortcutVerdict.Accepted, flow.Verdict);
        DevelopmentProject project = Assert.Single(colony.Layout.Projects, p => p.Kind == DevelopmentKind.RoadShortcut);
        Assert.Equal(ProjectState.Accepted, project.State);
        RoadSegment segment = colony.Layout.SegmentById(Assert.Single(project.SegmentIds))!;
        Assert.Equal(SegmentFunction.Link, segment.Function);
        Assert.Equal(job.ShortcutCells, segment.Cells);
        Assert.True(job.ShortcutProposedSeconds < 300 * (1 - RoadShortcuts.MinGain));
        Assert.True(job.ShortcutWorkHours > 0);
        // L'existence du projet ne rend aucune cellule aménagée : le travail reste à faire, cellule par cellule.
        Assert.All(segment.Cells, cell => Assert.NotEqual(RoadSurface.DirtRoad, colony.Map.Roads.SurfaceAt(cell)));
        Assert.Equal(project, RoadWorks.ActiveProject(colony));
        Assert.Empty(world.Planning.Jobs);
    }

    [Fact]
    public void Un_gain_insuffisant_ou_un_amortissement_trop_long_est_refuse_avec_son_motif_et_ne_revient_pas_aussitot()
    {
        var (world, colony, place, oven) = Village();
        // Un parcours déjà presque direct : aucun tracé ne fait gagner un quart.
        TravelFlow flow = AddFlow(place, -1, oven.Id, trips: 40, seconds: 300, cells: 200, world.Clock.TotalDays);
        Review(world, place);
        PlanningJob first = Assert.Single(world.Planning.Jobs);
        Complete(world);
        Assert.Equal(ShortcutVerdict.Accepted, flow.Verdict);
        double proposed = first.ShortcutProposedSeconds;
        int proposedCells = first.ShortcutCells.Count;
        foreach (DevelopmentProject p in colony.Layout.Projects.Where(p => p.Kind == DevelopmentKind.RoadShortcut).ToList())
            colony.Layout.Projects.Remove(p);
        colony.Layout.RoadSegments.RemoveAll(s => s.Function == SegmentFunction.Link);

        // On rejoue l'étude avec un parcours à peine plus long que le tracé trouvé : gain insuffisant.
        flow.TripsByDay.Clear(); flow.TicksByDay.Clear(); flow.CellsByDay.Clear();
        // Le parcours actuel ne dépasse le tracé trouvé que de 10 % en durée et en longueur.
        long day = world.Clock.TotalDays;
        flow.TripsByDay[day] = 40; flow.TicksByDay[day] = (long)(40 * proposed * 1.1 * TimeConstants.TicksPerSecond); flow.CellsByDay[day] = (long)(40 * proposedCells * 1.1);
        flow.Verdict = ShortcutVerdict.None;
        Review(world, place);
        Complete(world);
        Assert.NotEqual(ShortcutVerdict.Accepted, flow.Verdict);
        Assert.DoesNotContain(colony.Layout.Projects, p => p.Kind == DevelopmentKind.RoadShortcut);

        // Le même couple n'est pas retenté avant le délai.
        ShortcutVerdict refused = flow.Verdict;
        Review(world, place);
        Assert.Empty(world.Planning.Jobs);
        Assert.Equal(refused, flow.Verdict);
    }

    [Fact]
    public void Un_corridor_invalide_entre_la_proposition_et_l_admission_n_ouvre_rien()
    {
        var (world, colony, place, oven) = Village();
        AddFlow(place, -1, oven.Id, trips: 40, seconds: 300, cells: 200, world.Clock.TotalDays);
        Review(world, place);
        PlanningJob job = Assert.Single(world.Planning.Jobs);
        using (colony.UseSettlement(place))
            SitePlanner.Advance(job, SitePlanner.Budget.Unlimited());
        Assert.Equal(PlanStage.Ready, job.Stage);

        // Un bâtiment s'élève sur le corridor avant l'admission.
        (int x, int y) = colony.Layout.Decode(job.ShortcutCells[job.ShortcutCells.Count / 2]);
        colony.Buildings.Add(new Building(BuildingType.Hut, x, y) { Progress = 1f });
        colony.Layout.Revision++;
        using (colony.UseSettlement(place))
            RoadShortcuts.Deliver(job);

        Assert.Equal(ShortcutVerdict.Stale, Assert.Single(place.TravelFlows).Verdict);
        Assert.DoesNotContain(colony.Layout.Projects, p => p.Kind == DevelopmentKind.RoadShortcut);
    }

    [Fact]
    public void L_etude_respecte_le_budget_mondial_et_survit_a_la_sauvegarde_en_cours_de_recherche()
    {
        var (world, colony, place, oven) = Village();
        AddFlow(place, -1, oven.Id, trips: 40, seconds: 300, cells: 200, world.Clock.TotalDays);
        Review(world, place);
        PlanningJob job = Assert.Single(world.Planning.Jobs);
        var small = new SitePlanner.Budget { Prefilters = 8, Expansions = 3, Cells = 256, MaxActiveSearches = 2 };
        using (colony.UseSettlement(place))
            SitePlanner.Advance(job, small);
        Assert.NotNull(job.Search); // la recherche est en cours, son état est celui d'un A* reprenable
        Assert.True(small.SpentExpansions <= 3);

        string path = Path.Combine(Path.GetTempPath(), $"GodColony-raccourci-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Null(new WorldComparison().Difference(world, loaded));
            for (int i = 0; i < 2500; i++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world, loaded));
            Assert.True(world.Planning.Expansions <= world.Planning.Rendezvous * SettlementRules.AStarExpansionBudget + 3);
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}

internal static class FlowTestExtensions
{
    /// <summary>Le numéro d'étude attendu d'un flux qui vient d'être ouvert (la première recherche du monde).</summary>
    public static int Id(this TravelFlow flow) => flow.StudyJobId;
}
