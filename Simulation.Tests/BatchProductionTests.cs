using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using Xunit;

namespace GodColony.Simulation.Tests;

/// <summary>Les lots du four et du moulin : formules, demande nette, postes, sorties physiques et conservation des biens.</summary>
public sealed class BatchProductionTests
{
    /// <summary>Un four sans moulin : il moud à bras (3 grains + 1 bois donnent 2 pains, 20 s).</summary>
    private static (WorldState World, Colony Colony, Building Oven) Bakery(int grainSurplus = 100, int wood = 80)
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Building oven = Urbanism.BuildInstantly(colony.Map, colony, BuildingType.Oven)!;
        Assert.NotNull(oven);
        colony.Labor.Record(ResourceType.Grain, 10, 10); // la chaîne du pain ne s'éveille qu'après quelques récoltes
        colony.Stock.Add(ResourceType.Wood, wood);
        SetGrainSurplus(colony, grainSurplus);
        return (world, colony, oven);
    }

    /// <summary>Règle le grain libre au-delà de la réserve des semailles.</summary>
    private static void SetGrainSurplus(Colony colony, int surplus)
    {
        if (colony.Stock.Get(ResourceType.Grain) < 1000) colony.Stock.Add(ResourceType.Grain, 1000); // la réserve ne se lit que sur un surplus positif
        int reserve = colony.Stock.Get(ResourceType.Grain) - FoodChain.Demand(colony).GrainSurplus;
        int target = reserve + surplus, now = colony.Stock.Get(ResourceType.Grain);
        if (target > now) colony.Stock.Add(ResourceType.Grain, target - now); else colony.Stock.TryTake(ResourceType.Grain, now - target);
        Assert.Equal(surplus, FoodChain.Demand(colony).GrainSurplus);
    }

    private static Building Extend(Colony colony, Building principal)
    {
        var module = new Building(principal.Type, principal.X, principal.Y) { ExtensionOfId = principal.Id, Progress = 1f };
        colony.Buildings.Add(module);
        return module;
    }

    private static Activity Engage(Colonist worker, Building workshop, BatchChoice choice) =>
        worker.Activity = new Activity(ActivityKind.Craft, worker.TileX, worker.TileY, 40)
        {
            Building = workshop, BatchCount = choice.Count, WorkshopSlotId = choice.SlotId, PlannedRecipe = choice.Recipe,
        };

    private static void Stand(Colonist worker) { worker.Path.Clear(); worker.PathIndex = 0; worker.Needs.Food = 1; worker.Needs.Rest = 1; }

    /// <summary>Les biens physiques d'un produit : stock, sorties d'atelier, intrants engagés et portés.</summary>
    private static int Physical(Colony colony, ResourceType good) =>
        colony.Stock.Get(good) + colony.Buildings.Sum(b => b.OutputUnits(good))
        + colony.Members.Sum(m => (m.Carrying is { } c && c.Type == good ? c.Amount : 0) + (m.Activity?.InputsInventory?.Get(good) ?? 0) + (m.PausedCraft?.InputsInventory?.Get(good) ?? 0));

    [Fact]
    public void La_formule_de_lot_partage_preparation_et_combustible_sans_economiser_les_matieres()
    {
        Recipe bread = FoodChain.RecipeFor(BuildingType.Oven);
        Assert.Same(bread, BatchProduction.BuildRecipe(bread, 1)); // k = 1 : la recette actuelle exacte

        Recipe two = BatchProduction.BuildRecipe(bread, 2), four = BatchProduction.BuildRecipe(bread, 4);
        Assert.Equal([(ResourceType.Flour, 6), (ResourceType.Wood, 2)], two.Inputs);
        Assert.Equal(8, two.OutputAmount);
        Assert.Equal(bread.Seconds * (0.3f + 2 * 0.7f), two.Seconds, 3);
        Assert.Equal([(ResourceType.Flour, 12), (ResourceType.Wood, 3)], four.Inputs); // 12 farines + 3 bois pour 16 pains
        Assert.Equal(16, four.OutputAmount);
        Assert.Equal(bread.Seconds * 3.1f, four.Seconds, 2);
        Assert.Equal(0.775, four.Seconds / 4 / bread.Seconds, 3); // 22,5 % de travail de cuisson en moins par pain
        Assert.Equal(0.75, (3.0 / 16) / (1.0 / 4), 3); // 25 % de bois en moins par pain

        // Le bois qui devient charbon est une matière transformée : jamais économisé comme du chauffage.
        Recipe charring = Crafting.RecipeFor(BuildingType.Kiln);
        Assert.Equal([(ResourceType.Wood, 24)], BatchProduction.BuildRecipe(charring, 4).Inputs);

        // Les mêmes intrants multiples sont fusionnés avant débit.
        var twice = new Recipe(BuildingType.Oven, [(ResourceType.Flour, 2), (ResourceType.Flour, 1)], ResourceType.Bread, 4, 10f);
        Assert.Equal([(ResourceType.Flour, 9)], BatchProduction.BuildRecipe(twice, 3).Inputs);
    }

    [Fact]
    public void Le_lot_suit_la_capacite_la_demande_nette_et_les_intrants_libres()
    {
        var (world, colony, oven) = Bakery();
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        Assert.Equal(ResourceType.Grain, bread.Inputs[0].Type);
        Assert.True(BatchProduction.NetDemand(colony, ResourceType.Bread) > 16);

        Assert.Equal(2, BatchProduction.Choose(colony, oven, bread, 0)!.Count); // capacité initiale : deux répétitions
        Extend(colony, oven);
        Assert.Equal(4, BatchProduction.Choose(colony, oven, bread, 0)!.Count); // première extension achevée : quatre

        // Un intrant manque : le plus grand lot possible, jamais un lot partiel débité.
        SetGrainSurplus(colony, 7);
        Assert.Equal(2, BatchProduction.Choose(colony, oven, bread, 0)!.Count);
        SetGrainSurplus(colony, 5);
        Assert.Equal(1, BatchProduction.Choose(colony, oven, bread, 0)!.Count);
        SetGrainSurplus(colony, 2);
        Assert.Null(BatchProduction.Choose(colony, oven, bread, 0));

        // Le chauffage garde la priorité sur le bois du four.
        SetGrainSurplus(colony, 100);
        Assert.Null(BatchProduction.Choose(colony, oven, bread, colony.Stock.Get(ResourceType.Wood)));
        Assert.NotNull(BatchProduction.Choose(colony, oven, bread, 0));
        Assert.True(world.Clock.Ticks >= 0);
    }

    [Fact]
    public void Une_demande_deja_couverte_ne_cree_aucun_lot_et_l_exces_indivisible_reste_permis()
    {
        var (_, colony, oven) = Bakery();
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        int target = FoodChain.Demand(colony).BreadTarget;

        colony.Stock.Add(ResourceType.Bread, target - 5);
        Assert.Equal(5, BatchProduction.NetDemand(colony, ResourceType.Bread));
        Assert.Equal(2, BatchProduction.Choose(colony, oven, bread, 0)!.Count); // 2 × 2 pains ≤ 5
        colony.Stock.Add(ResourceType.Bread, 4);
        Assert.Equal(1, BatchProduction.NetDemand(colony, ResourceType.Bread));
        Assert.Equal(1, BatchProduction.Choose(colony, oven, bread, 0)!.Count); // l'excédent indivisible d'une recette de base, pas d'un grand lot

        colony.Stock.Add(ResourceType.Bread, 1);
        Assert.Null(BatchProduction.Choose(colony, oven, bread, 0));
        Assert.Null(FoodChain.PickJob(colony, 0));
    }

    [Fact]
    public void La_sortie_pleine_empeche_un_nouveau_lot_et_les_postes_sont_exclusifs()
    {
        var (_, colony, oven) = Bakery();
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        int capacity = WorkshopCapacity.OutputCapacity(colony, oven, bread.OutputAmount); // deux lots maximaux de deux répétitions
        Assert.Equal(8, capacity);
        oven.StoreOutput(ResourceType.Bread, capacity - 2, 10);
        Assert.Equal(1, BatchProduction.Choose(colony, oven, bread, 0)!.Count); // la place restante ne permet qu'une répétition
        oven.StoreOutput(ResourceType.Bread, 2, 2);
        Assert.Null(BatchProduction.Choose(colony, oven, bread, 0));
        Assert.Null(FoodChain.PickJob(colony, 0));

        // Un lot engagé garde son poste, y compris en route : personne d'autre ne démarre dessus.
        oven.TakeOutput(ResourceType.Bread, capacity, out _);
        Colonist first = colony.Members[0];
        Engage(first, oven, BatchProduction.Choose(colony, oven, bread, 0)!);
        Assert.Equal(-1, WorkshopCapacity.FindFreeSlot(colony, oven));
        Assert.Null(BatchProduction.Choose(colony, oven, bread, 0));
        Assert.Equal(0, WorkshopCapacity.FindFreeSlot(colony, oven, first.Activity));
    }

    [Fact]
    public void Deux_artisans_ne_consomment_pas_les_memes_matieres()
    {
        var (world, colony, oven) = Bakery(grainSurplus: 6);
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        BatchChoice choice = BatchProduction.Choose(colony, oven, bread, 0)!;
        Assert.Equal(2, choice.Count);
        int grain = colony.Stock.Get(ResourceType.Grain), wood = colony.Stock.Get(ResourceType.Wood);
        Colonist a = colony.Members[0], b = colony.Members[1];
        Engage(a, oven, choice);
        Stand(a);

        ColonistAI.Tick(a, world);
        Assert.True(a.Activity!.InputsTaken);
        Assert.Equal(grain - 6, colony.Stock.Get(ResourceType.Grain));
        Assert.Equal(wood - 2, colony.Stock.Get(ResourceType.Wood));

        Engage(b, oven, choice); Stand(b); // un second artisan vise le même poste
        ColonistAI.Tick(b, world);
        Assert.True(b.Activity is null || !b.Activity.InputsTaken); // refusé à l'arrivée : ni poste ni matières
        Assert.Equal(grain - 6, colony.Stock.Get(ResourceType.Grain));
        Assert.Equal(wood - 2, colony.Stock.Get(ResourceType.Wood));
        Assert.Equal(4, Crafting.PendingUnits(colony, ResourceType.Bread));
    }

    [Fact]
    public void Un_lot_acheve_depose_sa_sortie_une_seule_fois_et_la_production_n_est_comptee_qu_au_depot()
    {
        var (world, colony, oven) = Bakery();
        Extend(colony, oven);
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        Colonist worker = colony.Members[0];
        BatchChoice choice = BatchProduction.Choose(colony, oven, bread, 0)!;
        Assert.Equal(4, choice.Count);
        int grain = colony.Stock.Get(ResourceType.Grain);
        long grainUsed = ResourceAccounting.Total(colony.Stock, ResourceType.Grain, ResourceFlow.Usage), woodUsed = ResourceAccounting.Total(colony.Stock, ResourceType.Wood, ResourceFlow.Usage);
        Engage(worker, oven, choice);
        Stand(worker);
        ColonistAI.Tick(worker, world); // arrivée : intrants pris, une seule fois
        Assert.True(worker.Activity!.InputsTaken);
        Assert.Equal(grain - 12, colony.Stock.Get(ResourceType.Grain));

        worker.Activity.ElapsedTicks = worker.Activity.DurationTicks - 1;
        ColonistAI.Tick(worker, world); // achèvement
        Assert.Null(worker.Activity);
        // 8 pains : 6 dans les bras, 2 restent à la sortie de l'atelier, avec leur part de travail.
        Assert.Equal((ResourceType.Bread, 6), worker.Carrying);
        Assert.Equal(2, oven.OutputUnits(ResourceType.Bread));
        Assert.True(oven.OutputHours![ResourceType.Bread] > 0);
        Assert.Equal(0, colony.Labor.TotalProduced(ResourceType.Bread)); // rien n'est encore « produit » au sens du stock
        Assert.Equal(12, ResourceAccounting.Total(colony.Stock, ResourceType.Grain, ResourceFlow.Usage) - grainUsed);
        Assert.Equal(3, ResourceAccounting.Total(colony.Stock, ResourceType.Wood, ResourceFlow.Usage) - woodUsed);
        int bread0 = colony.Stock.Get(ResourceType.Bread);
        Assert.Equal(bread0 + 8, Physical(colony, ResourceType.Bread));

        // Le dépôt : le stock reçoit les 6 pains, puis les 2 restants, jamais deux fois ; la demande est couverte pour qu'aucun nouveau lot ne s'engage.
        colony.Stock.Add(ResourceType.Bread, FoodChain.Demand(colony).BreadTarget);
        worker.Needs.Food = 1; worker.Needs.Rest = 1;
        int expected = colony.Stock.Get(ResourceType.Bread) + 8;
        for (int i = 0; i < 6000 && colony.Stock.Get(ResourceType.Bread) < expected; i++)
            world.Step();
        Assert.Equal(expected, Physical(colony, ResourceType.Bread));
        Assert.Equal(0, oven.OutputUnits(ResourceType.Bread));
        Assert.Equal(8, colony.Labor.TotalProduced(ResourceType.Bread));
    }

    [Fact]
    public void Les_retraits_partiels_gardent_les_heures_au_prorata_et_deux_ramasseurs_ne_se_doublent_pas()
    {
        var (world, colony, oven) = Bakery();
        oven.StoreOutput(ResourceType.Bread, 8, 24);
        Colonist first = colony.Members[0], second = colony.Members[1];
        Assert.Equal(6, BatchProduction.CollectInto(first, oven, world.Clock.Ticks));
        Assert.Equal(18, first.WorkCycleExtraHours, 6);
        Assert.Equal(2, BatchProduction.CollectInto(second, oven, world.Clock.Ticks));
        Assert.Equal(6, second.WorkCycleExtraHours, 6); // le reste exact demeure attaché au solde, jamais perdu ni dupliqué
        Assert.Equal(0, BatchProduction.CollectInto(colony.Members[2], oven, world.Clock.Ticks));
        Assert.Equal(0, oven.OutputUnits(ResourceType.Bread));
        Assert.DoesNotContain(ResourceType.Bread, oven.OutputHours!.Keys);
    }

    [Fact]
    public void La_pause_garde_le_lot_et_son_poste_et_la_mort_rend_les_intrants_une_seule_fois()
    {
        var (world, colony, oven) = Bakery();
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        Colonist worker = colony.Members[0];
        Engage(worker, oven, BatchProduction.Choose(colony, oven, bread, 0)!);
        Stand(worker);
        ColonistAI.Tick(worker, world);
        Activity craft = worker.Activity!;
        int grain = colony.Stock.Get(ResourceType.Grain), wood = colony.Stock.Get(ResourceType.Wood);

        worker.Needs.Food = 0.1f; // un repas : l'ouvrage est pausé avec ses matières et son poste
        ColonistAI.Tick(worker, world);
        Assert.Same(craft, worker.PausedCraft);
        Assert.Equal(-1, WorkshopCapacity.FindFreeSlot(colony, oven));
        Assert.Equal(4, Crafting.PendingUnits(colony, ResourceType.Bread));
        Assert.Equal(grain, colony.Stock.Get(ResourceType.Grain));

        worker.Needs.Food = 1; worker.Needs.Rest = 1;
        ColonistAI.Tick(worker, world); // reprise : pas de second débit
        Assert.Same(craft, worker.Activity);
        Assert.Equal(grain, colony.Stock.Get(ResourceType.Grain));
        Assert.Equal(wood, colony.Stock.Get(ResourceType.Wood));

        Lifecycle.Die(world, worker, "accident de test");
        Assert.Equal(grain + 6, colony.Stock.Get(ResourceType.Grain)); // les intrants encore présents reviennent, une seule fois
        Assert.Equal(wood + 2, colony.Stock.Get(ResourceType.Wood));
        Assert.Equal(0, WorkshopCapacity.FindFreeSlot(colony, oven));
        ColonistAI.DetachFromColony(worker);
        Assert.Equal(grain + 6, colony.Stock.Get(ResourceType.Grain));
    }

    [Fact]
    public void Un_lot_dont_la_recette_a_change_pendant_le_trajet_est_abandonne_sans_debit()
    {
        var (world, colony, oven) = Bakery();
        Recipe hand = FoodChain.RecipeFor(colony, BuildingType.Oven);
        Assert.Equal(ResourceType.Grain, hand.Inputs[0].Type);
        BatchChoice choice = BatchProduction.Choose(colony, oven, hand, 0)!;
        Colonist worker = colony.Members[0];
        Engage(worker, oven, choice);
        Stand(worker);

        colony.Buildings.Add(new Building(BuildingType.Mill, 0, 0) { Progress = 1f }); // le moulin s'achève pendant le trajet : la recette du four change
        Assert.Equal(ResourceType.Flour, FoodChain.RecipeFor(colony, BuildingType.Oven).Inputs[0].Type);
        int grain = colony.Stock.Get(ResourceType.Grain), wood = colony.Stock.Get(ResourceType.Wood);
        ColonistAI.Tick(worker, world);
        Assert.True(worker.Activity is null || !worker.Activity.InputsTaken);
        Assert.Equal(grain, colony.Stock.Get(ResourceType.Grain));
        Assert.Equal(wood, colony.Stock.Get(ResourceType.Wood));
    }

    [Fact]
    public void Un_atelier_detruit_perd_sa_sortie_une_fois_et_rend_les_intrants_engages()
    {
        var (world, colony, oven) = Bakery();
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        Colonist worker = colony.Members[0];
        Engage(worker, oven, BatchProduction.Choose(colony, oven, bread, 0)!);
        Stand(worker);
        ColonistAI.Tick(worker, world);
        oven.StoreOutput(ResourceType.Bread, 5, 4);
        int grain = colony.Stock.Get(ResourceType.Grain), bread0 = colony.Stock.Get(ResourceType.Bread);

        Civic.Burn(colony, oven, world.Clock);
        Assert.Null(worker.Activity);
        Assert.Equal(grain + 6, colony.Stock.Get(ResourceType.Grain));
        Assert.Equal(bread0, colony.Stock.Get(ResourceType.Bread)); // la sortie n'est jamais redéposée au stock pour masquer la destruction
        Assert.Equal(0, oven.OutputUnits(ResourceType.Bread));
        Assert.Equal(5, ResourceAccounting.Total(colony.Stock, ResourceType.Bread, ResourceFlow.Loss));
        BatchProduction.OnBuildingDestroyed(colony, oven);
        Assert.Equal(5, ResourceAccounting.Total(colony.Stock, ResourceType.Bread, ResourceFlow.Loss)); // enregistrée une seule fois
    }

    [Fact]
    public void Le_pain_a_la_sortie_de_l_atelier_compte_pour_la_survie_et_la_demande()
    {
        var (world, colony, oven) = Bakery();
        float before = ColonyBrain.Sense(colony, world.Clock).FoodDays;
        int expected = Crafting.Expected(colony, ResourceType.Bread);
        oven.StoreOutput(ResourceType.Bread, 8, 12);
        Assert.True(ColonyBrain.Sense(colony, world.Clock).FoodDays > before);
        Assert.Equal(expected + 8, Crafting.Expected(colony, ResourceType.Bread));
        Assert.Equal(8, BatchProduction.BufferedFood(colony));
    }

    [Fact]
    public void Un_lot_en_cours_et_une_sortie_partielle_survivent_a_la_sauvegarde_et_continuent_pareil()
    {
        var (world, colony, oven) = Bakery();
        Recipe bread = FoodChain.RecipeFor(colony, BuildingType.Oven);
        Colonist worker = colony.Members[0];
        Engage(worker, oven, BatchProduction.Choose(colony, oven, bread, 0)!);
        Stand(worker);
        ColonistAI.Tick(worker, world);
        oven.StoreOutput(ResourceType.Bread, 3, 7.5);
        string path = Path.Combine(Path.GetTempPath(), $"GodColony-lots-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Colony restored = loaded.Colonies[0];
            Building oven2 = restored.Buildings.First(b => b.Type == BuildingType.Oven);
            Assert.Equal(3, oven2.OutputUnits(ResourceType.Bread));
            Assert.Equal(7.5, oven2.OutputHours![ResourceType.Bread], 6);
            Activity craft = restored.Members[0].Activity!;
            Assert.True(craft.InputsTaken);
            Assert.Equal(2, craft.BatchCount);
            Assert.Equal(0, craft.WorkshopSlotId);
            Assert.Equal(4, craft.CommittedRecipe!.OutputAmount);
            Assert.Null(new WorldComparison().Difference(world, loaded));
            for (int i = 0; i < 1500; i++) { world.Step(); loaded.Step(); }
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
