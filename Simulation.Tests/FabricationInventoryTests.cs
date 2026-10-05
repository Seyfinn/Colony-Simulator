using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;

namespace GodColony.Simulation.Tests;

public sealed class FabricationInventoryTests
{
    private static (WorldState World, Colonist Worker, Stockpile Inputs) Engager(bool salted)
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        var worker = colony.Members[0];
        colony.Stock.Add(salted ? ResourceType.SaltedMeat : ResourceType.Meat, 2);
        colony.Stock.Add(ResourceType.Eggs, 1);
        colony.Stock.Add(ResourceType.Grain, 2);
        colony.Stock.AgeMeat();
        Assert.True(ToolChain.TryTakeInputs(colony, Cuisine.StewRecipe, out double hours, out Stockpile inputs));
        worker.Activity = new Activity(ActivityKind.Craft, worker.TileX, worker.TileY, 1)
        {
            Building = new Building(BuildingType.Tavern, worker.TileX, worker.TileY),
            Product = ResourceType.Stew, InputsTaken = true, Started = true,
            InputLaborHours = hours, InputsInventory = inputs, CommittedRecipe = Cuisine.StewRecipe,
        };
        worker.Path.Clear();
        worker.PathIndex = 0;
        return (world, worker, inputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Une_interruption_apres_sauvegarde_restitue_les_intrants_exacts(bool salted)
    {
        var (world, _, _) = Engager(salted);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-intrants-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            var loaded = WorldSave.Load(path).World;
            Colony colony = loaded.Colonies[0];
            Colonist worker = colony.Members[0];
            Assert.Equal(ResourceType.Stew, worker.Activity!.CommittedRecipe!.Output);
            ColonistAI.DetachFromColony(worker);
            Assert.Equal(salted ? 2 : 0, colony.Stock.Get(ResourceType.SaltedMeat));
            Assert.Equal(salted ? 0 : 2, colony.Stock.MeatAtLeast(1));
            Assert.Null(worker.Activity);
            ColonistAI.DetachFromColony(worker);
            Assert.Equal(2, colony.Stock.Get(salted ? ResourceType.SaltedMeat : ResourceType.Meat));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void La_viande_gatee_interrompt_la_recette_et_rend_seulement_les_intrants_restants()
    {
        var (_, worker, inputs) = Engager(false);
        Colony colony = worker.Colony;
        int eggs = colony.Stock.Get(ResourceType.Eggs), grain = colony.Stock.Get(ResourceType.Grain);
        ColonistAI.AgeCraftInputs(colony);
        Assert.Equal(2, inputs.MeatAtLeast(2));
        ColonistAI.AgeCraftInputs(colony);
        Assert.Null(worker.Activity);
        Assert.Equal(1, colony.Stock.MeatAtLeast(3));
        Assert.Equal(eggs + 1, colony.Stock.Get(ResourceType.Eggs));
        Assert.Equal(grain + 2, colony.Stock.Get(ResourceType.Grain));
        Assert.Equal(1, ResourceAccounting.Total(colony.Stock, ResourceType.Meat, ResourceFlow.Loss));
        Assert.Equal(0, ResourceAccounting.Total(colony.Stock, ResourceType.Meat, ResourceFlow.Usage));
        Assert.Null(worker.Carrying);
    }

    [Fact]
    public void Une_fabrication_terminee_consomme_les_intrants_une_seule_fois()
    {
        var (world, worker, inputs) = Engager(true);
        ColonistAI.Tick(worker, world);
        Assert.Null(worker.Activity);
        Assert.Equal((ResourceType.Stew, 4), worker.Carrying);
        Assert.Equal(0, inputs.Get(ResourceType.SaltedMeat));
        Assert.Equal(2, ResourceAccounting.Total(worker.Colony.Stock, ResourceType.SaltedMeat, ResourceFlow.Usage));
        Assert.Equal(0, worker.Colony.Stock.Get(ResourceType.SaltedMeat));
    }
}
