using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Prospection progressive : affleurements, profondeur sondée selon le temps et la compétence, fourchettes honnêtes qui se resserrent.</summary>
public sealed class ProspectionTests
{
    private static (WorldState World, Colony Owner, int Target, Deposit Shallow, Deposit Deep) Prepare()
    {
        var world = new WorldState(42, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony owner = world.Colonies[0];
        owner.Stock.Add(ResourceType.Grain, 500); owner.Stock.Add(ResourceType.Wood, 100);
        foreach (Colonist c in owner.Members) c.Sector = WorkSector.Free;
        int target = world.WorldMap.Grid.Neighbors(owner.PrimarySettlement.RegionTileIndex)
            .First(t => world.WorldMap.Grid[t].Habitable && world.WorldMap.TravelRoute(owner.PrimarySettlement.RegionTileIndex, t, new HashSet<int>()) is not null);
        RegionState region = world.VisitRegion(target);
        (int x, int y) = ColonyFounder.FindCampSite(region.Map);
        // Deux gîtes connus du test : un affleurement de fer et de l'or enfoui.
        var shallow = new Deposit { Id = 900001, Region = target, Material = ResourceType.IronOre, X = x, Y = y, Mode = DepositMode.Finite,
            InitialReserve = 300, RemainingReserve = 300, DailyLimit = 8, Depth = 0 };
        var deep = new Deposit { Id = 900002, Region = target, Material = ResourceType.GoldOre, X = x, Y = y, Mode = DepositMode.Finite,
            InitialReserve = 60, RemainingReserve = 60, DailyLimit = 4, Depth = 3 };
        region.Geology = region.Geology!.Where(d => d.Material is not (ResourceType.IronOre or ResourceType.GoldOre)).Append(shallow).Append(deep).ToList();
        return (world, owner, target, shallow, deep);
    }

    private static void Master(IEnumerable<Colonist> people)
    {
        foreach (Colonist person in people) for (int i = 0; i < 60; i++) person.Skills.Practice(SkillType.Mining, 100000f);
    }

    private static void Finish(WorldState world)
    {
        for (int hour = 0; hour < 24 * 30 && world.Caravans.Count > 0; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.Empty(world.Caravans);
    }

    [Fact]
    public void Une_visite_ne_revele_que_les_affleurements_comme_des_indices()
    {
        var (world, owner, target, shallow, deep) = Prepare();
        Prospection.Survey(world, owner, world.Regions[target], 0, "Visite");
        DepositKnowledge hint = owner.DepositReports.Single(k => k.SiteId == shallow.Id);
        Assert.Equal(DepositObservation.Hint, hint.State);
        Assert.InRange(hint.Confidence, 0.1f, 0.3f);
        Assert.DoesNotContain(owner.DepositReports, k => k.SiteId == deep.Id); // l'or enfoui reste inconnu
        // La fourchette contient la vérité sans la nommer.
        Assert.InRange(shallow.RemainingReserve, hint.EstimateMin, hint.EstimateMax);
        Assert.NotEqual(hint.EstimateMin, hint.EstimateMax);
    }

    [Fact]
    public void La_profondeur_sondee_croit_avec_le_temps_et_la_competence()
    {
        var (_, owner, _, _, _) = Prepare();
        var team = owner.Members.Take(2).ToList();
        int novice = Prospection.ReachOf(Prospection.WorkOf(team, Prospection.StayDays));
        Master(team);
        int expert = Prospection.ReachOf(Prospection.WorkOf(team, Prospection.StayDays));
        int expertLong = Prospection.ReachOf(Prospection.WorkOf(team, 3 * Prospection.StayDays));
        Assert.True(novice < expert, $"{novice} < {expert}");
        Assert.True(expert <= expertLong);
        Assert.Equal(Prospection.MaxReach, expertLong);
        Assert.True(novice < Prospection.MaxReach, "Des débutants ne trouvent pas l'or enfoui en trois jours.");
    }

    [Fact]
    public void Un_sondage_plus_profond_donne_une_fourchette_plus_etroite_et_toujours_honnete()
    {
        var (world, _, _, shallow, deep) = Prepare();
        long ticks = world.Clock.Ticks;
        DepositKnowledge? first = null;
        foreach (int reach in new[] { 0, 1, 2, 3 })
        {
            DepositKnowledge report = Prospection.Report(shallow, reach, ticks, "test")!;
            Assert.InRange(shallow.RemainingReserve, report.EstimateMin, report.EstimateMax);
            if (first is not null)
            {
                Assert.True(report.Confidence >= first.Confidence);
                Assert.True(report.EstimateMax - report.EstimateMin <= first.EstimateMax - first.EstimateMin);
            }
            first = report;
        }
        Assert.Null(Prospection.Report(deep, 2, ticks, "test")); // trop profond
        Assert.NotNull(Prospection.Report(deep, 3, ticks, "test"));
    }

    [Fact]
    public void L_exploitation_resserre_l_estimation_sans_jamais_la_rendre_fausse()
    {
        var (world, owner, _, shallow, _) = Prepare();
        Prospection.Survey(world, owner, world.Regions[shallow.Region], 0, "Visite");
        DepositKnowledge known = owner.DepositReports.Single(k => k.SiteId == shallow.Id);
        float confidence = known.Confidence;
        int width = known.EstimateMax - known.EstimateMin;
        for (int day = 0; day < 25; day++)
        {
            for (int tick = 0; tick < TimeConstants.TicksPerDay; tick++) world.Clock.Advance();
            DepositExtraction.Extract(world, owner, shallow, 100);
            if (shallow.RemainingReserve > 0) Assert.InRange(shallow.RemainingReserve, known.EstimateMin, known.EstimateMax);
        }
        Assert.Equal(DepositObservation.Working, known.State);
        Assert.True(known.Confidence > confidence);
        Assert.True(known.EstimateMax - known.EstimateMin < width);
    }

    [Fact]
    public void Des_prospecteurs_sondent_sur_place_et_ne_rapportent_qu_au_retour()
    {
        var (world, owner, target, shallow, deep) = Prepare();
        var trip = Assert.IsType<Caravan>(TerritorialTravel.Depart(world, owner.PrimarySettlement, target, TerritorialPurpose.Prospection, new Dictionary<ResourceType, int>()));
        int reachBefore = Prospection.ReachOf(Prospection.WorkOf(trip.Traders, Prospection.StayDays));
        for (int hour = 0; hour < 24 * 30 && trip.State == CaravanState.Outbound; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.DoesNotContain(owner.DepositReports, k => k.Region == target);
        Finish(world);
        Assert.True(trip.WorkedTicks > 0);
        Assert.Equal(reachBefore, trip.SurveyReach);
        DepositKnowledge iron = owner.DepositReports.Single(k => k.SiteId == shallow.Id);
        Assert.Equal(reachBefore >= 1 ? DepositObservation.Surveyed : DepositObservation.Hint, iron.State);
        Assert.Equal(reachBefore, owner.RegionReach[target]);
        Assert.Equal(reachBefore >= 3, owner.DepositReports.Any(k => k.SiteId == deep.Id));
    }

    [Fact]
    public void Des_prospecteurs_experimentes_trouvent_l_or_enfoui_et_la_prospection_reprend_exactement_apres_sauvegarde()
    {
        var (world, owner, target, _, deep) = Prepare();
        Master(owner.Members);
        // Une équipe de quatre experts sonde assez profond pour trouver l'or enfoui ; deux n'y suffiraient pas en trois jours.
        var trip = Assert.IsType<Caravan>(TerritorialTravel.Depart(world, owner.PrimarySettlement, target, TerritorialPurpose.Prospection, new Dictionary<ResourceType, int>(), people: 4));
        // En plein séjour : sauvegarde et reprise.
        for (int hour = 0; hour < 24 * 30 && trip.WorkedTicks < TimeConstants.TicksPerDay; hour++)
        { for (int tick = 0; tick < TimeConstants.TicksPerHour; tick++) world.Clock.Advance(); Trade.Hourly(world); }
        Assert.True(trip.WorkedTicks > 0);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-prospection-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Finish(world); Finish(loaded);
            Assert.Contains(owner.DepositReports, k => k.SiteId == deep.Id && k.State == DepositObservation.Surveyed);
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Les_habitants_sondent_lentement_leur_propre_region()
    {
        var (world, owner, _, _, _) = Prepare();
        Settlement home = owner.PrimarySettlement;
        RegionState region = world.VisitRegion(home.RegionTileIndex);
        (int x, int y) = ColonyFounder.FindCampSite(region.Map);
        var buried = new Deposit { Id = 900003, Region = home.RegionTileIndex, Material = ResourceType.Emerald, X = x, Y = y, Mode = DepositMode.Finite,
            InitialReserve = 8, RemainingReserve = 8, DailyLimit = 2, Depth = 3 };
        region.Geology = region.Geology!.Append(buried).ToList();
        for (int day = 0; day < 5; day++) Prospection.LocalDaily(world, home);
        Assert.DoesNotContain(owner.DepositReports, k => k.SiteId == buried.Id); // quelques jours ne suffisent pas pour les pierres précieuses
        home.SurveyWork = 100;
        Prospection.LocalDaily(world, home);
        Assert.Contains(owner.DepositReports, k => k.SiteId == buried.Id && k.State == DepositObservation.Surveyed);
    }
}
