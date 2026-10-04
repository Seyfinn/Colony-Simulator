using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class IronChainTests(ITestOutputHelper output)
{
    private static (WorldState World, Colony Colony) Colony(int colonists)
    {
        var world = new WorldState(12345, startingColonists: colonists, migration: false, lifecycle: false);
        return (world, world.Colonies[0]);
    }

    private static void Set(Colony colony, ResourceType type, int amount)
    {
        colony.Stock.TryTake(type, colony.Stock.Get(type));
        colony.Stock.Add(type, amount);
    }

    private static void Complete(WorldState world, Colony colony, BuildingType type)
    {
        (int x, int y) = Urbanism.FindWorkshopSite(world.Map, colony)!.Value;
        Urbanism.PlanBuilding(world.Map, colony, type, x, y).Progress = 1f;
    }

    [Fact]
    public void Sans_avoir_vu_de_fer_la_colonie_ne_pense_pas_aux_outils()
    {
        (_, Colony colony) = Colony(10);
        colony.IronSeen = false; // la carrière de cette carte laisse voir un filon : on l'ignore pour ce test
        Assert.False(ToolChain.Demand(colony).Active);
        Assert.Null(ToolChain.NextWorkshopToBuild(colony));

        colony.Stock.Add(ResourceType.IronOre, 1);
        ChainDemand demand = ToolChain.Demand(colony);
        Assert.True(demand.Active);
        Assert.Equal(5, demand.ToolsWanted);
        Assert.Equal(5, demand.ToolShortfall);
        Assert.Equal(BuildingType.Kiln, ToolChain.NextWorkshopToBuild(colony));
    }

    [Fact]
    public void La_demande_remonte_la_chaine_des_outils_au_bois()
    {
        (_, Colony colony) = Colony(10);
        colony.Stock.Add(ResourceType.IronOre, 1);

        // Trois outils d'un coup : 6 fers (18 minerais, 12 charbons) plus 3 charbons pour la forge.
        ChainDemand demand = ToolChain.Demand(colony);
        Assert.Equal(6, demand.IronTarget);
        Assert.Equal(15, demand.CharcoalTarget);
        Assert.Equal(17, demand.OreMissing);
        Assert.Equal(30, demand.WoodForCharcoal);

        // Avec le fer en stock, il ne reste que le charbon de la forge.
        Set(colony, ResourceType.Iron, 6);
        Set(colony, ResourceType.IronOre, 0);
        demand = ToolChain.Demand(colony);
        Assert.Equal(3, demand.CharcoalTarget);
        Assert.Equal(0, demand.OreMissing);
    }

    [Fact]
    public void Les_ateliers_se_batissent_dans_l_ordre_de_la_chaine()
    {
        (WorldState world, Colony colony) = Colony(10);
        colony.Stock.Add(ResourceType.IronOre, 1);
        Assert.Equal(BuildingType.Kiln, ToolChain.NextWorkshopToBuild(colony));
        Complete(world, colony, BuildingType.Kiln);
        Assert.Equal(BuildingType.Bloomery, ToolChain.NextWorkshopToBuild(colony));
        Complete(world, colony, BuildingType.Bloomery);
        Assert.Equal(BuildingType.Forge, ToolChain.NextWorkshopToBuild(colony));
        Complete(world, colony, BuildingType.Forge);
        Assert.Null(ToolChain.NextWorkshopToBuild(colony));
    }

    [Fact]
    public void Une_recette_consomme_les_matieres_ou_ne_fait_rien()
    {
        (_, Colony colony) = Colony(10);
        Recipe smelting = ToolChain.RecipeFor(BuildingType.Bloomery);
        Set(colony, ResourceType.IronOre, 2);
        Set(colony, ResourceType.Charcoal, 2);
        Assert.False(ToolChain.TryTakeInputs(colony, smelting, out _));
        Assert.Equal(2, colony.Stock.Get(ResourceType.IronOre));

        Set(colony, ResourceType.IronOre, 3);
        Assert.True(ToolChain.TryTakeInputs(colony, smelting, out _));
        Assert.Equal(0, colony.Stock.Get(ResourceType.IronOre));
        Assert.Equal(0, colony.Stock.Get(ResourceType.Charcoal));
        ToolChain.Refund(colony, smelting);
        Assert.Equal(3, colony.Stock.Get(ResourceType.IronOre));
    }

    [Fact]
    public void La_colonie_forge_d_abord_puis_fond_puis_brule_du_charbon()
    {
        (WorldState world, Colony colony) = Colony(10);
        Complete(world, colony, BuildingType.Kiln);
        Complete(world, colony, BuildingType.Bloomery);
        Complete(world, colony, BuildingType.Forge);
        colony.Stock.Add(ResourceType.IronOre, 1);
        Set(colony, ResourceType.Wood, 200);

        Assert.Equal(BuildingType.Kiln, ToolChain.PickJob(colony, 20)!.Type);

        Set(colony, ResourceType.IronOre, 3);
        Set(colony, ResourceType.Charcoal, 2);
        Assert.Equal(BuildingType.Bloomery, ToolChain.PickJob(colony, 20)!.Type);

        Set(colony, ResourceType.Iron, 2);
        Set(colony, ResourceType.Charcoal, 1);
        Assert.Equal(BuildingType.Forge, ToolChain.PickJob(colony, 20)!.Type);
    }

    [Fact]
    public void La_charbonniere_ne_touche_pas_au_bois_de_chauffage()
    {
        (WorldState world, Colony colony) = Colony(10);
        Complete(world, colony, BuildingType.Kiln);
        colony.Stock.Add(ResourceType.IronOre, 1);

        Set(colony, ResourceType.Wood, 25);
        Assert.Null(ToolChain.PickJob(colony, 20));
        Set(colony, ResourceType.Wood, 26);
        Assert.NotNull(ToolChain.PickJob(colony, 20));
    }

    [Fact]
    public void Les_outils_accelerent_le_travail_puis_s_usent_et_cassent()
    {
        (_, Colony colony) = Colony(10);
        Assert.Equal(1f, ToolChain.SpeedFactor(colony, SkillType.Mining));

        Set(colony, ResourceType.Tools, 5);
        Assert.Equal(1f + ToolChain.ToolSpeedBonus, ToolChain.SpeedFactor(colony, SkillType.Mining), 3);
        Assert.Equal(1f, ToolChain.SpeedFactor(colony, SkillType.Fishing));

        // Les métiers sans outil ne les usent pas ; les autres les usent jusqu'à les briser.
        Assert.False(ToolChain.RecordUse(colony, SkillType.Foraging));
        int uses = 0;
        while (!ToolChain.RecordUse(colony, SkillType.Mining))
            uses++;
        Assert.InRange(uses, 140, 150);
        Assert.Equal(4, colony.Stock.Get(ResourceType.Tools));
    }

    [Fact]
    public void Une_colonie_ferme_forge_ses_outils_et_en_mesure_le_cout()
    {
        (WorldState world, Colony colony) = Colony(12);
        int maxTools = 0;
        long firstToolDay = -1;

        // Les autres chantiers (moulin, four, enclos…) passent parfois avant la forge : on laisse quatre ans à la chaîne du fer.
        for (int day = 1; day <= 80; day++)
        {
            for (long i = 0; i < TimeConstants.TicksPerDay; i++)
                world.Step();
            maxTools = Math.Max(maxTools, colony.Stock.Get(ResourceType.Tools));
            if (firstToolDay < 0 && colony.Stock.Get(ResourceType.Tools) > 0)
                firstToolDay = day;
            if (day % 10 == 0)
                output.WriteLine($"J{day}: bois {colony.Stock.Get(ResourceType.Wood)}, pierre {colony.Stock.Get(ResourceType.Stone)}, " +
                                 $"minerai {colony.Stock.Get(ResourceType.IronOre)}, charbon {colony.Stock.Get(ResourceType.Charcoal)}, " +
                                 $"fer {colony.Stock.Get(ResourceType.Iron)}, outils {colony.Stock.Get(ResourceType.Tools)}, " +
                                 $"humeur {colony.AverageMood:P0}");
        }
        output.WriteLine("Coûts : " + ColonyBrain.CostSummary(colony.Labor));
        output.WriteLine($"Premier outil au jour {firstToolDay}, maximum {maxTools}, produits {colony.Labor.TotalProduced(ResourceType.Tools)}");
        foreach (Thought thought in colony.Thoughts)
            output.WriteLine($"  [jour {thought.Ticks / TimeConstants.TicksPerDay}] {thought.Text}");

        Assert.NotEqual(-1, firstToolDay);
        Assert.True(colony.Labor.TotalProduced(ResourceType.Tools) >= 1);
        Assert.NotNull(colony.Labor.HoursPerUnit(ResourceType.Tools));

        // Un outil cumule le travail de toute la chaîne : il coûte plus cher que le fer, qui coûte plus cher que le charbon.
        double tool = colony.Labor.HoursPerUnit(ResourceType.Tools)!.Value;
        double iron = colony.Labor.HoursPerUnit(ResourceType.Iron)!.Value;
        double charcoal = colony.Labor.HoursPerUnit(ResourceType.Charcoal)!.Value;
        Assert.True(tool > iron && iron > charcoal, $"outil {tool:0.0} h, fer {iron:0.0} h, charbon {charcoal:0.0} h");
    }
}
