using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using Xunit;

namespace GodColony.Simulation.Tests;

/// <summary>Un schisme n'a que des raisons durables : la taille seule d'un village prospère n'en ouvre aucun.</summary>
public sealed class GrowthPolicyTests
{
    [Fact]
    public void Une_route_n_est_presentee_que_si_ses_regions_sont_connues()
    {
        var (world, colony, home) = Village();
        int target = world.WorldMap.Grid.Neighbors(home.RegionTileIndex).First(t => world.WorldMap.Grid[t].Habitable
            && !colony.VisitedRegions.Contains(t) && !colony.RegionReach.ContainsKey(t));
        Assert.True(double.IsInfinity(ExpansionPlanner.KnownRouteDays(world, colony, home.RegionTileIndex, target)));
        var route = world.WorldMap.TravelRoute(home.RegionTileIndex, target, new HashSet<int>());
        Assert.NotNull(route);
        colony.VisitedRegions.AddRange(route.Tiles);
        Assert.Equal(route.Cost / WorldMap.CaravanTilesPerDay, ExpansionPlanner.KnownRouteDays(world, colony, home.RegionTileIndex, target));
    }
    [Fact]
    public void La_lecture_interface_ne_remplace_pas_la_derniere_raison_observee()
    {
        var (world, colony, home) = Village();
        home.GrowthState.LastReason = GrowthReason.PersistentDiscontent;
        GrowthDecision decision = GrowthPolicy.EvaluateDeparture(world, colony, 6, recordReason: false);
        Assert.Equal(GrowthReason.PersistentDiscontent, home.GrowthState.LastReason);
        Assert.True(double.IsFinite(decision.LocalCostPerResident));
    }
    private static (WorldState World, Colony Mother, Settlement Home) Village(int people = 36)
    {
        var world = new WorldState(77, startingColonists: people, migration: true, lifecycle: false, colonyCount: 1);
        Colony mother = world.Colonies[0];
        return (world, mother, mother.PrimarySettlement);
    }

    private static void Observe(WorldState world, Settlement home, int days)
    {
        for (int i = 0; i < days; i++)
        {
            home.GrowthState.LastDay = -1; // un jour de plus, sans faire avancer le monde
            using var scope = home.Owner.UseSettlement(home);
            GrowthPolicy.ObserveDaily(world, home);
        }
    }

    private static void SetMood(Settlement home, float level)
    {
        foreach (Colonist colonist in home.Population)
        {
            colonist.Needs.Food = colonist.Needs.Rest = colonist.Needs.Leisure = colonist.Needs.Social = colonist.Needs.Comfort = level;
            colonist.Needs.Grief = 0;
        }
    }

    private static PlanRequest HutRequest(Colony colony, PlanningOutcome state, PlacementFailureKind? failure)
    {
        using var scope = colony.UseSettlement(colony.PrimarySettlement);
        PlanRequest request = SettlementPlanner.RequestFor(colony, DevelopmentKind.Housing, BuildingType.Hut, DevelopmentPriority.Housing);
        request.State = state;
        request.Failure = failure;
        request.RetryOn = RetryEvents.Occupancy;
        request.Seen = RetryEvents.None;
        request.JobId = -1;
        return request;
    }

    [Fact]
    public void La_population_seule_n_ouvre_aucun_schisme()
    {
        (WorldState world, Colony mother, Settlement home) = Village();
        SetMood(home, 1f);
        Observe(world, home, 30);
        Assert.Equal(GrowthReason.None, GrowthPolicy.EvaluateDeparture(world, mother, 8).Reason);
        for (int i = 0; i < 400; i++)
            Schism.Daily(world, mother);
        Assert.DoesNotContain(mother.Prayers.Pending, p => p.Kind == DecisionKind.Schism);
    }

    [Fact]
    public void Le_mecontentement_durable_est_une_raison_avec_son_retour_au_calme()
    {
        (WorldState world, Colony mother, Settlement home) = Village();
        SetMood(home, 0.1f);
        Observe(world, home, ScaleRules.DiscontentDays - 1);
        Assert.Equal(GrowthReason.None, GrowthPolicy.EvaluateDeparture(world, mother, 8).Reason);
        Observe(world, home, 1);
        Assert.Equal(GrowthReason.PersistentDiscontent, GrowthPolicy.EvaluateDeparture(world, mother, 8).Reason);

        // Une humeur entre les deux seuils ne désamorce rien ; seul un vrai retour au calme le fait.
        SetMood(home, 0.5f);
        Observe(world, home, 5);
        Assert.Equal(ScaleRules.DiscontentDays, home.GrowthState.DiscontentDays);
        SetMood(home, 1f);
        Observe(world, home, ScaleRules.CalmDays - 1);
        Assert.Equal(ScaleRules.DiscontentDays, home.GrowthState.DiscontentDays);
        Observe(world, home, 1);
        Assert.Equal(0, home.GrowthState.DiscontentDays);
    }

    [Fact]
    public void Une_recherche_en_attente_ou_a_budget_epuise_n_est_pas_une_saturation()
    {
        (WorldState world, Colony mother, Settlement home) = Village();
        SetMood(home, 1f);
        Assert.True(mother.Homeless >= ScaleRules.HousingPressureHomeless);

        HutRequest(mother, PlanningOutcome.Pending, null);
        Observe(world, home, 8);
        Assert.Equal(0, home.GrowthState.BlockedDays);
        Assert.Equal(GrowthReason.None, GrowthPolicy.EvaluateDeparture(world, mother, 8).Reason); // l'agrandissement local avance

        HutRequest(mother, PlanningOutcome.WaitingForChange, PlacementFailureKind.SearchBudgetExceeded);
        Observe(world, home, 8);
        Assert.Equal(0, home.GrowthState.BlockedDays);
    }

    [Fact]
    public void Une_impossibilite_confirmee_de_loger_devient_une_raison()
    {
        (WorldState world, Colony mother, Settlement home) = Village();
        SetMood(home, 1f);
        PlanRequest request = HutRequest(mother, PlanningOutcome.WaitingForChange, PlacementFailureKind.NoSpace);
        Observe(world, home, ScaleRules.BlockedDays - 1);
        Assert.Equal(GrowthReason.None, GrowthPolicy.EvaluateDeparture(world, mother, 8).Reason);
        Observe(world, home, 1);
        GrowthDecision decision = GrowthPolicy.EvaluateDeparture(world, mother, 8);
        Assert.Equal(GrowthReason.LocalCapacityBlocked, decision.Reason);
        Assert.True(double.IsPositiveInfinity(decision.LocalCostPerResident));

        // Un événement utile (un bâtiment achevé ailleurs, une parcelle libérée) lève la certitude.
        request.Seen = RetryEvents.Occupancy;
        Observe(world, home, 1);
        Assert.Equal(0, home.GrowthState.BlockedDays);
    }

    [Fact]
    public void L_accord_tardif_est_revalide_sans_rien_retirer()
    {
        (WorldState world, Colony mother, Settlement home) = Village(12);
        Colonist? leader = mother.PresentMembers.FirstOrDefault();
        int members = mother.Members.Count, wood = mother.Stock.Get(ResourceType.Wood), thoughts = mother.Thoughts.Count;
        Assert.Null(Schism.Split(world, mother, leader!.Id));
        Assert.Single(world.Colonies);
        Assert.Equal(members, mother.Members.Count);
        Assert.Equal(wood, mother.Stock.Get(ResourceType.Wood));
        Assert.True(mother.Thoughts.Count > thoughts);
    }

    [Fact]
    public void Une_installation_exterieure_n_est_comparee_que_dans_une_region_connue()
    {
        (WorldState world, Colony mother, Settlement home) = Village();
        Assert.DoesNotContain(GrowthPolicy.CompareGrowthOptions(world, mother, 8), o => o.Kind == GrowthOptionKind.NewSettlement);
        int tile = world.WorldMap.SuggestTiles(mother.Species, 40).First(t => world.WorldMap.Grid.Distance(world.WorldMap.TileOf(mother), t) <= Schism.MaxDistance);
        mother.VisitedRegions.Add(tile);
        IReadOnlyList<GrowthOption> options = GrowthPolicy.CompareGrowthOptions(world, mother, 8);
        GrowthOption outside = Assert.Single(options, o => o.Kind == GrowthOptionKind.NewSettlement);
        Assert.True(outside.CostPerResident > 0 && !double.IsInfinity(outside.CostPerResident));
        Assert.Equal(tile, outside.Region);
    }

    [Fact]
    public void Les_compteurs_de_croissance_survivent_a_la_sauvegarde()
    {
        (WorldState world, Colony mother, Settlement home) = Village();
        SetMood(home, 0.1f);
        HutRequest(mother, PlanningOutcome.WaitingForChange, PlacementFailureKind.TravelBudgetExceeded);
        Observe(world, home, 6);
        string path = Path.Combine(Path.GetTempPath(), $"GodColony-croissance-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            SettlementGrowthState loaded = WorldSave.Load(path).World.Colonies[0].PrimarySettlement.GrowthState;
            Assert.Equal(6, loaded.DiscontentDays);
            Assert.Equal(6, loaded.BlockedDays);
            Assert.Equal(home.GrowthState.LastDay, loaded.LastDay);
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
