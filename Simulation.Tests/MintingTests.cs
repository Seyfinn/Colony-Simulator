using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Frappe monétaire : quota annuel partagé, engagement du lot, annulation, passage d'année et reprise.</summary>
public sealed class MintingTests
{
    private static (WorldState World, Colony Colony, Building Mint) Mint(int colonies = 1, int gold = 20)
    {
        var world = new WorldState(7, startingColonists: 12, migration: false, lifecycle: false, trade: false, colonyCount: colonies);
        Colony colony = world.Colonies[0];
        Knowledge.Grant(colony, [Discovery.Metallurgy, Discovery.Commerce, Discovery.Coinage], world.Clock.Ticks);
        (int x, int y) = Urbanism.FindSite(colony.Map, colony, BuildingType.Mint)!.Value;
        Building mint = Urbanism.PlanBuilding(colony.Map, colony, BuildingType.Mint, x, y);
        mint.Progress = 1f;
        colony.Stock.Add(ResourceType.Gold, gold);
        colony.Stock.Add(ResourceType.Food, 4000);
        foreach (Colonist c in colony.Members) c.Sector = WorkSector.Craft;
        return (world, colony, mint);
    }

    private static void Run(WorldState world, int hours)
    {
        for (int i = 0; i < hours * TimeConstants.TicksPerHour; i++) world.Step();
    }

    [Fact]
    public void La_frappe_expose_son_lot_son_utilisation_et_les_pieces_produites_sans_double_compte()
    {
        var (world, colony, mint) = Mint();
        var activity = new Activity(ActivityKind.Craft, mint.X, mint.Y, 1) { Building = mint };
        Recipe recipe = Crafting.RecipeFor(colony, BuildingType.Mint);
        Assert.True(Minting.TryCommit(colony, activity, recipe));
        Assert.Equal(recipe.OutputAmount, world.Money.CommittedThisYear);
        Minting.Complete(colony, activity);
        Minting.Complete(colony, activity);
        Assert.Equal(recipe.OutputAmount, colony.PrimarySettlement.ScaleLedger.Today.Produced[ResourceType.Coins]);
        Assert.Equal(0, world.Money.CommittedThisYear);
        MintWorkshopView view = Assert.Single(ScaleSnapshot.Of(colony.PrimarySettlement).Mints);
        Assert.Equal(recipe.OutputAmount, view.BatchCoins);
        Assert.Null(view.Utilization);
        Assert.Equal(0, view.OutputWaiting);
    }

    [Fact]
    public void Le_plafond_annuel_est_partage_exactement_et_de_facon_deterministe()
    {
        var world = new WorldState(3, startingColonists: 9, migration: false, lifecycle: false, trade: false, colonyCount: 3);
        MonetaryLedger money = world.Money;
        long mass = MonetaryLedger.Mass(world);
        Assert.Equal(mass * MonetaryLedger.CapPercent / 100, money.YearCap);
        Assert.Equal(money.YearCap, money.Allowances.Values.Sum());
        Assert.Equal(0, money.Imbalance(world));
        // Un second monde identique donne les mêmes parts.
        var twin = new WorldState(3, startingColonists: 9, migration: false, lifecycle: false, trade: false, colonyCount: 3);
        Assert.Equal(money.Allowances.OrderBy(p => p.Key), twin.Money.Allowances.OrderBy(p => p.Key));
    }

    [Fact]
    public void Un_atelier_frappe_quarante_pieces_par_or_dans_la_limite_du_quota()
    {
        var (world, colony, _) = Mint();
        long allowance = world.Money.AllowanceOf(colony);
        Assert.True(allowance >= 4);
        colony.Stock.Add(ResourceType.Gold, 100); // beaucoup plus d'or que de quota
        Run(world, 24 * 12);
        long minted = world.Money.Minted;
        Assert.InRange(minted, MonetaryLedger.CoinsPerGold, allowance);
        Assert.Equal(0, minted % MonetaryLedger.CoinsPerGold);
        Assert.Equal(ColonyFounder.StartingCoins + minted, colony.Stock.Get(ResourceType.Coins));
        Assert.InRange(colony.Stock.Get(ResourceType.Gold), 120 - minted / MonetaryLedger.CoinsPerGold - 2, 120 - minted / MonetaryLedger.CoinsPerGold); // un lot en cours (ou un bijou) peut avoir déjà pris de l'or
        Assert.Equal(0, world.Money.Imbalance(world));
        Assert.InRange(world.Money.UsedBy(colony), minted, allowance);
    }

    [Fact]
    public void Sans_quota_ni_registre_rien_n_est_frappe()
    {
        var (world, colony, _) = Mint();
        colony.Ledger = null;
        Run(world, 24 * 4);
        Assert.Equal(ColonyFounder.StartingCoins, colony.Stock.Get(ResourceType.Coins));
        Assert.Equal(20, colony.Stock.Get(ResourceType.Gold));
    }

    [Fact]
    public void Un_lot_interrompu_rend_l_or_et_l_engagement()
    {
        var (world, colony, mint) = Mint();
        Colonist worker = colony.Members[0];
        var activity = new Activity(ActivityKind.Craft, worker.TileX, worker.TileY, 100000) { Building = mint, Product = ResourceType.Coins };
        Recipe recipe = Crafting.RecipeFor(colony, BuildingType.Mint, ResourceType.Coins);
        long before = world.Money.UsedBy(colony);
        Assert.True(Minting.TryCommit(colony, activity, recipe));
        Assert.True(ToolChain.TryTakeInputs(colony, recipe, out double hours, out Stockpile inputs));
        activity.InputsTaken = true; activity.InputsInventory = inputs; activity.CommittedRecipe = recipe; activity.InputLaborHours = hours;
        worker.Activity = activity;
        Assert.Equal(before + MonetaryLedger.CoinsPerGold, world.Money.UsedBy(colony));
        Assert.Equal(19, colony.Stock.Get(ResourceType.Gold));
        ColonistAI.DetachFromColony(worker);
        Assert.Equal(before, world.Money.UsedBy(colony));
        Assert.Equal(20, colony.Stock.Get(ResourceType.Gold));
        Assert.Equal(ColonyFounder.StartingCoins, colony.Stock.Get(ResourceType.Coins));
        Assert.Equal(0, world.Money.Minted);
    }

    [Fact]
    public void Un_lot_qui_traverse_l_annee_garde_son_engagement_et_ne_reduit_pas_le_nouveau_budget()
    {
        var (world, colony, mint) = Mint();
        Colonist worker = colony.Members[0];
        Recipe recipe = Crafting.RecipeFor(colony, BuildingType.Mint, ResourceType.Coins);
        var activity = new Activity(ActivityKind.Craft, worker.TileX, worker.TileY, 100000) { Building = mint, Product = ResourceType.Coins };
        Assert.True(Minting.TryCommit(colony, activity, recipe));
        int oldYear = world.Money.Year;
        while (world.Clock.Ticks % TimeConstants.TicksPerYear < TimeConstants.TicksPerYear - 1) world.Clock.Advance();
        world.Step();
        Assert.Equal(oldYear + 1, world.Money.Year);
        Assert.Equal(0, world.Money.UsedBy(colony));
        world.Money.Complete(activity.MintYear, activity.MintCoins);
        Assert.Equal(MonetaryLedger.CoinsPerGold, world.Money.Minted);
        Assert.Equal(0, world.Money.MintedThisYear);
        world.Money.Release(colony, activity.MintYear, activity.MintCoins);
        Assert.Equal(0, world.Money.UsedBy(colony));
    }

    [Fact]
    public void La_frappe_reprend_exactement_apres_sauvegarde()
    {
        var (world, colony, _) = Mint();
        Run(world, 24 * 2);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-frappe-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Equal(world.Money.Minted, loaded.Money.Minted);
            Assert.Equal(world.Money.AllowanceOf(colony), loaded.Money.AllowanceOf(loaded.Colonies[0]));
            Run(world, 24 * 6); Run(loaded, 24 * 6);
            Assert.Equal(world.Money.Minted, loaded.Money.Minted);
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
