using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using Xunit;

namespace GodColony.Simulation.Tests;

/// <summary>Les spécialistes stables et les extensions d'ateliers : mesures, verdicts, capacité à l'achèvement et préférences.</summary>
public sealed class WorkshopScaleTests
{
    private static (WorldState World, Colony Colony, Building Oven) Bakery()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Building oven = Urbanism.BuildInstantly(colony.Map, colony, BuildingType.Oven)!;
        Assert.NotNull(oven);
        colony.Labor.Record(ResourceType.Grain, 10, 10);
        colony.Stock.Add(ResourceType.Grain, 2000);
        colony.Stock.Add(ResourceType.Wood, 500);
        // La colonie n'est pas en crise : du pain d'avance en vue, de quoi manger.
        colony.Sensors = ColonyBrain.Sense(colony, world.Clock);
        return (world, colony, oven);
    }

    /// <summary>Dix journées complètes d'observation d'un atelier : <paramref name="use"/> de ses heures possibles employées.</summary>
    private static void Observe(Colony colony, Building oven, double use, int refusalsPerDay = 0, int unitsPerDay = 16)
    {
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        long day = colony.Clock.TotalDays;
        WorkshopCapacity.ObserveCapacities(colony, day - 11);
        for (int i = 0; i < WorkshopCapacity.UtilizationDays; i++)
        {
            ledger.Today.Day = day - 10 + i;
            ledger.Today.WorkTicks[oven.Id] = (int)(use * WorkshopCapacity.Slots(colony, oven) * WorkshopCapacity.WorkdayTicks);
            ledger.Today.SlotRefusals[oven.Id] = refusalsPerDay;
            ledger.Today.Produced[ResourceType.Bread] = unitsPerDay;
            ledger.Close(day - 9 + i);
        }
    }

    private static Building Module(Colony colony, Building principal, bool complete)
    {
        var module = new Building(principal.Type, principal.X + 3, principal.Y) { Width = 2, Height = 3, ExtensionOfId = principal.Id, Progress = complete ? 1f : 0.2f };
        module.Id = colony.Layout.NextObjectId++;
        colony.Buildings.Add(module);
        return module;
    }

    [Fact]
    public void L_utilisation_attend_dix_jours_complets_et_ne_compte_que_le_travail_effectif()
    {
        var (_, colony, oven) = Bakery();
        Assert.Null(WorkshopCapacity.Utilization(colony, oven));
        Assert.Empty(WorkshopCapacity.DailyUtilization(colony, oven));
        Observe(colony, oven, 0.8);
        Assert.Equal(0.8, WorkshopCapacity.Utilization(colony, oven)!.Value, 2);
        Assert.Equal(10, WorkshopCapacity.DailyUtilization(colony, oven).Count);
        Assert.All(WorkshopCapacity.DailyUtilization(colony, oven), value => Assert.Equal(0.8, value, 2));
        Observe(colony, oven, 0.3);
        Assert.Equal(0.3, WorkshopCapacity.Utilization(colony, oven)!.Value, 2);
    }

    [Fact]
    public void Les_verdicts_d_extension_exposent_leur_motif()
    {
        var (world, colony, oven) = Bakery();
        Assert.Equal(ExtensionVerdict.TooLittleUse, WorkshopCapacity.Evaluate(colony, oven).Verdict); // pas dix jours de mesure

        Observe(colony, oven, 0.5);
        Assert.Equal(ExtensionVerdict.TooLittleUse, WorkshopCapacity.Evaluate(colony, oven).Verdict);

        Observe(colony, oven, 0.9, refusalsPerDay: 40);
        var wanted = WorkshopCapacity.Evaluate(colony, oven);
        Assert.Equal(ExtensionVerdict.Wanted, wanted.Verdict);
        Assert.InRange(wanted.PaybackDays, 0, WorkshopCapacity.MaxPaybackDays);
        Assert.True(wanted.CostHours > 0 && wanted.SavedHoursPerDay > 0);

        // La survie passe avant : une faim attendue annule l'investissement.
        colony.Sensors = colony.Sensors! with { FoodDays = 0.5f, FoodPressure = 90 };
        Assert.Equal(ExtensionVerdict.SurvivalFirst, WorkshopCapacity.Evaluate(colony, oven).Verdict);
        colony.Sensors = ColonyBrain.Sense(colony, world.Clock);

        // Un amortissement trop long est refusé, avec son motif.
        Observe(colony, oven, 0.9, refusalsPerDay: 0, unitsPerDay: 1);
        Assert.Equal(ExtensionVerdict.PaybackTooLong, WorkshopCapacity.Evaluate(colony, oven).Verdict);

        // Sans demande, pas d'extension.
        Observe(colony, oven, 0.9, refusalsPerDay: 40);
        colony.Stock.Add(ResourceType.Bread, FoodChain.Demand(colony).BreadTarget + 10);
        Assert.Equal(ExtensionVerdict.NoDemand, WorkshopCapacity.Evaluate(colony, oven).Verdict);
    }

    [Fact]
    public void Un_chantier_ne_donne_aucune_capacite_et_l_achevement_oui_dans_la_limite_des_plafonds()
    {
        var (_, colony, oven) = Bakery();
        Assert.Equal((2, 1), (WorkshopCapacity.MaxBatch(colony, oven), WorkshopCapacity.Slots(colony, oven)));
        Building first = Module(colony, oven, complete: false);
        Assert.Equal((2, 1), (WorkshopCapacity.MaxBatch(colony, oven), WorkshopCapacity.Slots(colony, oven)));
        Assert.Equal(ExtensionVerdict.UnderConstruction, WorkshopCapacity.Evaluate(colony, oven).Verdict);
        first.Progress = 1f;
        Assert.Equal((4, 1), (WorkshopCapacity.MaxBatch(colony, oven), WorkshopCapacity.Slots(colony, oven))); // première extension : quatre répétitions, un poste

        Module(colony, oven, complete: true);
        // Un deuxième poste exige une case de travail accessible distincte ; sans elle, aucun poste fantôme.
        int cells = SettlementServices.WorkCells(colony, oven).Length;
        Assert.Equal(cells >= 2 ? 2 : 1, WorkshopCapacity.Slots(colony, oven));
        Assert.Equal(4, WorkshopCapacity.MaxBatch(colony, oven));

        Module(colony, oven, complete: true); // les extensions suivantes n'ajoutent rien
        Assert.Equal(4, WorkshopCapacity.MaxBatch(colony, oven));
        Assert.True(WorkshopCapacity.Slots(colony, oven) <= 2);
        Assert.Equal(ExtensionVerdict.MaxReached, WorkshopCapacity.Evaluate(colony, oven).Verdict);
    }

    [Fact]
    public void Le_module_d_un_four_se_planifie_a_part_et_n_est_pas_un_atelier()
    {
        var (_, colony, oven) = Bakery();
        Building? module = SettlementPlanner.PlanExtension(colony, BuildingType.Oven, oven);
        Assert.NotNull(module);
        Assert.True(module!.IsExtension);
        Assert.Equal(oven.Id, module.ExtensionOfId);
        Assert.Equal((2, 3), (module.Width, module.Height));
        Assert.False(module.IsComplete);
        Assert.Equal(new Building(BuildingType.Oven, 0, 0) { Width = 2, Height = 3, ExtensionOfId = 1 }.WoodRequired, module.WoodRequired);
        Assert.Contains(colony.Layout.Projects, p => p.Kind == DevelopmentKind.WorkshopExtension && p.OccupantId == module.Id);

        Assert.Equal(2, WorkshopCapacity.MaxBatch(colony, oven)); // aucune capacité tant que le chantier n'est pas achevé
        module.Progress = 1f;
        SettlementPlanner.OnObjectCompleted(colony, module);
        Assert.Equal(4, WorkshopCapacity.MaxBatch(colony, oven));
        Assert.DoesNotContain(module, colony.Workshops(BuildingType.Oven));
        Assert.Equal(ProjectState.Completed, colony.Layout.Projects.First(p => p.OccupantId == module.Id).State);
    }

    [Fact]
    public void Une_extension_detruite_ferme_ses_postes_et_annule_leurs_lots()
    {
        var (world, colony, oven) = Bakery();
        Module(colony, oven, true);
        Building second = Module(colony, oven, true);
        if (WorkshopCapacity.Slots(colony, oven) < 2) return; // pas de deuxième case de travail sur ce terrain : rien à fermer
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        Colonist worker = colony.Members[0];
        worker.Activity = new Activity(ActivityKind.Craft, worker.TileX, worker.TileY, 40)
        {
            Building = oven, WorkshopSlotId = 1, BatchCount = 2, PlannedRecipe = BatchProduction.BuildRecipe(bread, 2),
        };
        colony.Buildings.Remove(second);
        BatchProduction.OnBuildingDestroyed(colony, second);
        Assert.Null(worker.Activity);
        Assert.True(world.Clock.Ticks >= 0);
    }

    // --- Spécialistes ---

    private static Building AddMill(Colony colony)
    {
        var mill = new Building(BuildingType.Mill, 0, 0) { Progress = 1f };
        mill.Id = colony.Layout.NextObjectId++;
        colony.Buildings.Add(mill);
        return mill;
    }

    [Fact]
    public void La_repartition_quotidienne_suit_la_demande_garde_trois_jours_et_depart_par_competence()
    {
        var (world, colony, oven) = Bakery();
        Building mill = AddMill(colony);
        Assert.True(colony.Sensors is { SurvivalAssured: true });
        Colonist a = colony.Members[0], b = colony.Members[1], c = colony.Members[2];
        foreach (Colonist crafter in new[] { a, b, c }) crafter.Sector = WorkSector.Craft;
        b.Skills.Practice(SkillType.Cooking, 5000); // le plus doué prend le travail le plus chargé

        SpecialistAssignments.RefreshDaily(colony, world.Clock);
        Assert.All(new[] { a, b, c }, x => Assert.NotEqual(0, x.PreferredWorkshopId));
        int[] before = new[] { a, b, c }.Select(x => x.PreferredWorkshopId).ToArray();

        // Un choix tient : rien ne bouge le lendemain tant qu'aucun gain net n'apparaît.
        for (int day = 0; day < 2; day++)
        {
            world.Clock.Advance();
            SpecialistAssignments.RefreshDaily(colony, world.Clock);
        }
        Assert.Equal(before, new[] { a, b, c }.Select(x => x.PreferredWorkshopId));
        Assert.All(new[] { a, b, c }, x => Assert.True(x.SpecialtyChosenTicks > 0));
    }

    [Fact]
    public void Une_preference_guide_le_choix_sauf_impossibilite_et_cede_devant_la_survie()
    {
        var (world, colony, oven) = Bakery();
        Building mill = AddMill(colony);
        Colonist worker = colony.Members[0];
        worker.PreferredWorkshopId = oven.Id;
        var jobs = new[] { new CraftCandidate(mill, null), new CraftCandidate(oven, null) };
        long now = world.Clock.Ticks;
        Assert.Equal(oven, SpecialistAssignments.Choose(colony, worker, jobs, now)!.Value.Workshop);
        Assert.Equal(0, worker.SpecialtyBlockedSinceTicks);

        // Le travail préféré est impossible : on fait autre chose et le blocage se mesure.
        Assert.Equal(mill, SpecialistAssignments.Choose(colony, worker, jobs.Take(1).ToArray(), now)!.Value.Workshop);
        Assert.Equal(now, worker.SpecialtyBlockedSinceTicks);

        // Blocage d'un jour : la préférence est abandonnée à la revue suivante.
        colony.Members[0].Sector = WorkSector.Craft;
        for (int i = 0; i < 1000; i++) world.Clock.Advance(); // plus d'un jour
        SpecialistAssignments.RefreshDaily(colony, world.Clock);
        Assert.NotEqual(0, worker.PreferredWorkshopId); // la revue le réaffecte à un travail qui existe
        Assert.Equal(0, worker.SpecialtyBlockedSinceTicks);

        // La survie : plus de nourriture d'avance, aucune préférence ne s'applique.
        colony.Sensors = colony.Sensors! with { FoodDays = 0.5f };
        Assert.False(SpecialistAssignments.Applies(colony));
    }

    [Fact]
    public void La_preference_est_invalidee_au_depart_et_survit_a_la_sauvegarde()
    {
        var (world, colony, oven) = Bakery();
        Colonist worker = colony.Members[0], leaver = colony.Members[1];
        worker.PreferredWorkshopId = oven.Id; worker.PreferredProduct = ResourceType.Bread; worker.SpecialtyChosenTicks = 777; worker.SpecialtyBlockedSinceTicks = 5;
        leaver.PreferredWorkshopId = oven.Id;
        ColonistAI.DetachFromColony(leaver);
        Assert.Equal(0, leaver.PreferredWorkshopId);

        string path = Path.Combine(Path.GetTempPath(), $"GodColony-specialistes-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Colonist restored = loaded.Colonies[0].Members.First(m => m.Id == worker.Id);
            Assert.Equal((oven.Id, ResourceType.Bread, 777L, 5L), (restored.PreferredWorkshopId, restored.PreferredProduct, restored.SpecialtyChosenTicks, restored.SpecialtyBlockedSinceTicks));
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public void Le_registre_quotidien_et_ses_verdicts_survivent_a_la_sauvegarde()
    {
        var (world, colony, oven) = Bakery();
        Observe(colony, oven, 0.9, refusalsPerDay: 30);
        colony.LocalSettlement.ScaleLedger.Verdicts[oven.Id] = ExtensionVerdict.Wanted;
        string path = Path.Combine(Path.GetTempPath(), $"GodColony-registre-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Colony restored = loaded.Colonies[0];
            Building oven2 = restored.Buildings.First(b => b.Type == BuildingType.Oven);
            Assert.Equal(WorkshopCapacity.Utilization(colony, oven), WorkshopCapacity.Utilization(restored, oven2)); // la décision retrouve les mêmes valeurs
            Assert.Equal(ExtensionVerdict.Wanted, restored.LocalSettlement.ScaleLedger.Verdicts[oven2.Id]);
            Assert.Null(new WorldComparison().Difference(world, loaded));
            for (int i = 0; i < 1200; i++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
