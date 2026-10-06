using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class CraftPauseTests
{
    private static (WorldState World, Colonist Worker, Activity Craft) Pause(bool salted = true)
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Colonist worker = colony.Members[0];
        Building workshop = Urbanism.BuildInstantly(colony.Map, colony, BuildingType.Tavern)!;
        Assert.NotNull(workshop);
        colony.Stock.Add(salted ? ResourceType.SaltedMeat : ResourceType.Meat, 2);
        colony.Stock.Add(ResourceType.Eggs, 1); colony.Stock.Add(ResourceType.Grain, 2);
        colony.Stock.AgeMeat();
        Assert.True(ToolChain.TryTakeInputs(colony, Cuisine.StewRecipe, out double hours, out Stockpile inputs));
        var craft = new Activity(ActivityKind.Craft, worker.TileX, worker.TileY, 1000)
        {
            Building = workshop, Product = ResourceType.Stew, Started = true, InputsTaken = true,
            InputsInventory = inputs, CommittedRecipe = Cuisine.StewRecipe, InputLaborHours = hours, ElapsedTicks = 120,
        };
        worker.Activity = craft; worker.Path.Clear(); worker.PathIndex = 0;
        worker.Needs.Food = .1f; worker.WorkCycleStartTicks = world.Clock.Ticks - 100;
        ColonistAI.Tick(worker, world);
        Assert.Same(craft, worker.PausedCraft); Assert.Null(worker.Activity);
        return (world, worker, craft);
    }

    [Fact]
    public void La_pause_garde_les_matieres_et_la_reprise_ne_les_prend_pas_deux_fois()
    {
        var (world, worker, craft) = Pause();
        Colony colony = worker.Colony;
        int eggs = colony.Stock.Get(ResourceType.Eggs), grain = colony.Stock.Get(ResourceType.Grain);
        Assert.Equal(1, Crafting.Pending(colony, ResourceType.Stew));
        Assert.Equal(120, craft.ElapsedTicks);
        worker.Needs.Food = 1; worker.Needs.Rest = 1;
        ColonistAI.Tick(worker, world);
        Assert.Null(worker.PausedCraft); Assert.Same(craft, worker.Activity);
        Assert.Equal(eggs, colony.Stock.Get(ResourceType.Eggs)); Assert.Equal(grain, colony.Stock.Get(ResourceType.Grain));
        Assert.Equal(2, craft.InputsInventory!.Get(ResourceType.SaltedMeat));
        Assert.True(craft.InputsTaken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mort_ou_depart_pendant_la_pause_rend_les_matieres_exactes_une_seule_fois(bool death)
    {
        var (world, worker, _) = Pause();
        Colony colony = worker.Colony;
        int eggs = colony.Stock.Get(ResourceType.Eggs), grain = colony.Stock.Get(ResourceType.Grain);
        if (death) Lifecycle.Die(world, worker, "accident de test"); else ColonistAI.DetachFromColony(worker);
        Assert.Null(worker.PausedCraft);
        Assert.Equal(2, colony.Stock.Get(ResourceType.SaltedMeat));
        Assert.Equal(eggs + 1, colony.Stock.Get(ResourceType.Eggs)); Assert.Equal(grain + 2, colony.Stock.Get(ResourceType.Grain));
        ColonistAI.DetachFromColony(worker);
        Assert.Equal(2, colony.Stock.Get(ResourceType.SaltedMeat));
    }

    [Fact]
    public void Un_atelier_disparu_pendant_la_pause_rend_les_matieres()
    {
        var (world, worker, craft) = Pause();
        worker.Colony.Buildings.Remove(craft.Building!);
        worker.Needs.Food = 1; worker.Needs.Rest = 1;
        ColonistAI.Tick(worker, world);
        Assert.Null(worker.PausedCraft);
        Assert.False(craft.InputsTaken);
        Assert.Equal(2, worker.Colony.Stock.Get(ResourceType.SaltedMeat));
    }

    [Fact]
    public void Les_intrants_perissables_vieillissent_aussi_pendant_un_repas()
    {
        var (_, worker, _) = Pause(salted: false);
        ColonistAI.AgeCraftInputs(worker.Colony); ColonistAI.AgeCraftInputs(worker.Colony);
        Assert.Null(worker.PausedCraft);
        Assert.Equal(1, worker.Colony.Stock.MeatAtLeast(3));
        Assert.Equal(1, ResourceAccounting.Total(worker.Colony.Stock, ResourceType.Meat, ResourceFlow.Loss));
    }

    [Fact]
    public void La_sauvegarde_garde_la_pause_et_le_pont_partiellement_bati()
    {
        var (world, worker, _) = Pause();
        Settlement place = worker.Colony.PrimarySettlement;
        int cell = place.CampY * place.Map.Width + place.CampX;
        place.BridgeSites.Add(new BridgeSite { Id = 1, Cells = [cell, cell + 1], BuiltCells = [cell], CreatedTicks = world.Clock.Ticks });
        place.Map.Roads.SetSurface(cell, RoadSurface.Bridge);
        worker.LastMealTicks = world.Clock.Ticks - 10;
        string path = Path.Combine(Path.GetTempPath(), "GodColony-pause-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Colonist restored = loaded.Colonies[0].Members[0];
            Assert.Equal(120, restored.PausedCraft!.ElapsedTicks);
            Assert.Same(loaded.Colonies[0].Buildings.First(b => b.Id == restored.PausedCraft.Building!.Id), restored.PausedCraft.Building);
            BridgeSite bridge = Assert.Single(loaded.Colonies[0].PrimarySettlement.BridgeSites);
            Assert.False(bridge.IsComplete); Assert.Equal([cell], bridge.BuiltCells);
            Assert.Equal(RoadSurface.Bridge, loaded.Colonies[0].Map.Roads.SurfaceAt(cell));
            Assert.Equal(worker.LastMealTicks, restored.LastMealTicks);
            Assert.Null(new WorldComparison().Difference(world, loaded));
            ColonistAI.DetachFromColony(restored);
            Assert.Equal(2, restored.Colony.Stock.Get(ResourceType.SaltedMeat));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Un_petit_dejeuner_entre_deux_observations_affamees_ne_signale_pas_de_famine()
    {
        var world = new WorldState(12345, startingColonists: 8, lifecycle: false);
        Colonist worker = world.Colonies[0].Members[0];
        var watch = new StarvationWatch();
        worker.Needs.Food = 0; watch.Observe(worker.Colony);
        worker.LastMealTicks = world.Clock.Ticks; watch.Observe(worker.Colony);
        Assert.Null(watch.Victim);
        watch.Observe(worker.Colony);
        Assert.Same(worker, watch.Victim);
    }
}
