using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

public sealed class HouseholdEquipment
{
    // Chaque objet équipé garde son échéance propre ; les réserves ne vieillissent pas à sa place.
    public List<long> PotteryExpiry { get; } = [];
    public List<long> CopperwareExpiry { get; } = [];
    public int Jewelry { get; internal set; }
    public long LastWineDay { get; internal set; } = -1;
}

/// <summary>Filières supplémentaires utilisant les activités, intrants engagés et livraisons déjà existants.</summary>
public static class ExtendedIndustry
{
    public static readonly Recipe[] Recipes =
    [
        new(BuildingType.Bloomery, [(ResourceType.CopperOre,3),(ResourceType.Charcoal,2)], ResourceType.Copper,1,24),
        new(BuildingType.Bloomery, [(ResourceType.GoldOre,3),(ResourceType.Charcoal,2)], ResourceType.Gold,1,32),
        new(BuildingType.PotteryKiln, [(ResourceType.Clay,3),(ResourceType.Wood,1)], ResourceType.Pottery,2,14),
        new(BuildingType.Loom, [(ResourceType.Flax,3)], ResourceType.Linen,2,14),
        new(BuildingType.Loom, [(ResourceType.Linen,2)], ResourceType.Clothes,1,16),
        new(BuildingType.Tannery, [(ResourceType.Hides,2),(ResourceType.Salt,1)], ResourceType.Leather,2,16),
        new(BuildingType.Forge, [(ResourceType.Leather,2)], ResourceType.Shoes,1,16),
        new(BuildingType.Forge, [(ResourceType.Copper,2)], ResourceType.Copperware,1,20),
        new(BuildingType.Cask, [(ResourceType.Grapes,4)], ResourceType.Wine,2,6),
        new(BuildingType.Goldsmith, [(ResourceType.Gold,1),(ResourceType.Ruby,1)], ResourceType.Jewelry,1,24),
        new(BuildingType.Goldsmith, [(ResourceType.Gold,1),(ResourceType.Sapphire,1)], ResourceType.Jewelry,1,24),
        new(BuildingType.Goldsmith, [(ResourceType.Gold,1),(ResourceType.Emerald,1)], ResourceType.Jewelry,1,24),
        new(BuildingType.Goldsmith, [(ResourceType.Gold,1),(ResourceType.Diamond,1)], ResourceType.Jewelry,1,24),
        new(BuildingType.Mint, [(ResourceType.Gold,1)], ResourceType.Coins,MonetaryLedger.CoinsPerGold,24),
    ];

    public static bool Crisis(Colony colony) => colony.Stock.AvailableNutrition < Math.Max(1, colony.PresentMembers.Count)
        * (decimal)Trade.TravelerNutritionPerDay * 3;

    public static int Target(Colony colony, ResourceType good)
    {
        int people = colony.PresentMembers.Count;
        HouseholdEquipment equipment = colony.LocalSettlement.Equipment;
        int households = (people + 3) / 4;
        int luxury = Crisis(colony) ? 0 : Math.Min(households, colony.Stock.Available(ResourceType.Coins) / 10);
        return good switch
        {
            ResourceType.Pottery => Math.Max(0, households - equipment.PotteryExpiry.Count),
            ResourceType.Copperware => Math.Max(0, Math.Min(households - equipment.CopperwareExpiry.Count, luxury)),
            ResourceType.Shoes => colony.PresentMembers.Count(c => c.Stage != LifeStage.Child && !c.HasShoes),
            ResourceType.Jewelry => Math.Max(0, Math.Min(1 - equipment.Jewelry, luxury)),
            ResourceType.Wine => Civic.Has(colony, BuildingType.Tavern) ? luxury : 0,
            ResourceType.Clay => 3 * Math.Max(0, Target(colony, ResourceType.Pottery) - colony.Stock.Get(ResourceType.Pottery)),
            ResourceType.Copper => 2 * Math.Max(0, Target(colony, ResourceType.Copperware) - colony.Stock.Get(ResourceType.Copperware)),
            ResourceType.CopperOre => 3 * Math.Max(0, Target(colony, ResourceType.Copper) - colony.Stock.Get(ResourceType.Copper)),
            ResourceType.Gold => JewelryGold(colony) + Minting.GoldWish(colony) + Offerings.Need(colony, ResourceType.Gold),
            ResourceType.Coins => colony.Stock.Get(ResourceType.Coins) + Minting.Wanted(colony),
            ResourceType.GoldOre => 3 * Math.Max(0, Target(colony, ResourceType.Gold) - colony.Stock.Get(ResourceType.Gold)),
            ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond => JewelryGold(colony) + Offerings.Need(colony, good),
            ResourceType.Leather => 2 * Math.Max(0, Target(colony, ResourceType.Shoes) - colony.Stock.Get(ResourceType.Shoes)),
            ResourceType.Hides => Math.Max(0, Target(colony, ResourceType.Leather) - colony.Stock.Get(ResourceType.Leather)),
            ResourceType.Clothes => Husbandry.ClothesTarget(colony),
            ResourceType.Linen => 2 * Math.Max(0, Husbandry.ClothesTarget(colony) - colony.Stock.Get(ResourceType.Clothes)),
            ResourceType.Flax => 3 * Math.Max(0, Target(colony, ResourceType.Linen) - colony.Stock.Get(ResourceType.Linen)),
            ResourceType.Grapes => 2 * Math.Max(0, Target(colony, ResourceType.Wine) - colony.Stock.Get(ResourceType.Wine)),
            ResourceType.MineralCoal => Math.Max(ToolChain.Demand(colony).CharcoalTarget,FuelTarget(colony)),
            ResourceType.IronOre => (int)Economy.Need(colony,ResourceType.IronOre),
            _ => 0,
        };
    }

    /// <summary>L'or (et autant de pierres) qu'il faut aux bijoux souhaités : ni la frappe ni les offrandes ne passent par là.</summary>
    private static int JewelryGold(Colony colony) => Math.Max(0, Target(colony, ResourceType.Jewelry) - colony.Stock.Get(ResourceType.Jewelry));

    public static Recipe? RecipeFor(Colony colony, BuildingType workshop, ResourceType? product)
    {
        foreach (Recipe recipe in Recipes.Where(r => r.Workshop == workshop && (product is null || r.Output == product)))
        {
            if (product is null && Target(colony, recipe.Output) + colony.ExportInterest.GetValueOrDefault(recipe.Output)
                <= colony.Stock.Get(recipe.Output) + Crafting.Pending(colony, recipe.Output) * recipe.OutputAmount) continue;
            if (ToolChain.MissingInputs(colony, recipe).Count == 0) return recipe;
        }
        return product is not null ? Recipes.FirstOrDefault(r => r.Workshop == workshop && r.Output == product) : null;
    }

    /// <summary>Combustible des métaux supplémentaires, même lorsque les outils du village sont déjà suffisants.</summary>
    public static int FuelTarget(Colony colony) => Crisis(colony) ? 0 : Recipes
        .Where(r => r.Inputs.Any(i => i.Type == ResourceType.Charcoal))
        .Sum(r => Math.Max(0,Target(colony,r.Output) + colony.ExportInterest.GetValueOrDefault(r.Output) - colony.Stock.Get(r.Output))
            * r.Inputs.First(i => i.Type == ResourceType.Charcoal).Amount);

    internal static (Building Workshop, Recipe Recipe)? PickJob(Colony colony)
    {
        if (FuelTarget(colony) > colony.Stock.Get(ResourceType.Charcoal) + colony.Stock.Get(ResourceType.MineralCoal)
            && colony.Stock.Available(ResourceType.Wood) >= 6 + ColonyBrain.HeatingTarget(colony,colony.Clock.Season)
            && Knowledge.Allows(colony,BuildingType.Kiln)
            && colony.Workshops(BuildingType.Kiln).FirstOrDefault(b => !colony.PresentMembers.Any(c => c.Activity?.Building == b)) is { } kiln)
            return (kiln,ToolChain.RecipeFor(BuildingType.Kiln));
        foreach (Building workshop in colony.Buildings.Where(b => b.IsComplete && b.IsWorkshop))
        {
            if (!Knowledge.Allows(colony, workshop.Type) || colony.PresentMembers.Any(c => c.Activity?.Building == workshop)
                || workshop.Type == BuildingType.Cask && workshop.IsBrewing) continue;
            Recipe? recipe = RecipeFor(colony, workshop.Type, null);
            if (recipe is null || recipe.Output is ResourceType.Wine or ResourceType.Jewelry or ResourceType.Copperware && Crisis(colony)) continue;
            // Au plus un dixième des artisans consacre du temps au confort (au moins un pour un petit village prospère).
            if (recipe.Output is ResourceType.Wine or ResourceType.Jewelry or ResourceType.Copperware
                && colony.PresentMembers.Count(c => c.Activity?.Product is ResourceType.Wine or ResourceType.Jewelry or ResourceType.Copperware)
                    >= Math.Max(1, colony.Workers.Count() / 10)) continue;
            return (workshop, recipe);
        }
        return null;
    }

    internal static BuildingType? NextWorkshop(Colony colony)
    {
        if (FuelTarget(colony) > colony.Stock.Get(ResourceType.Charcoal) + colony.Stock.Get(ResourceType.MineralCoal)
            && Knowledge.Allows(colony,BuildingType.Kiln) && !colony.Buildings.Any(b => b.Type == BuildingType.Kiln)) return BuildingType.Kiln;
        bool mineNeeded = colony.LocalSettlement.Kind == SettlementKind.Camp || colony.DepositReports.Any(k =>
            k.Region == colony.LocalSettlement.RegionTileIndex && k.State != DepositObservation.Depleted && NeedsMine(k.Material)
            && Target(colony, k.Material) + colony.ExportInterest.GetValueOrDefault(k.Material) > colony.Stock.Get(k.Material));
        if (mineNeeded && !colony.Buildings.Any(b => b.Type == BuildingType.MineDepot)) return BuildingType.MineDepot;
        foreach (Recipe recipe in Recipes)
            if (Knowledge.Allows(colony, recipe.Workshop) && !colony.Buildings.Any(b => b.Type == recipe.Workshop)
                && Target(colony, recipe.Output) > colony.Stock.Get(recipe.Output)
                && ToolChain.MissingInputs(colony, recipe).Count == 0) return recipe.Workshop;
        return null;
    }

    internal static IEnumerable<Deposit> ExtractionJobs(WorldState world, Colony colony)
    {
        RegionState region = world.VisitRegion(colony.LocalSettlement.RegionTileIndex);
        return region.Deposits.Where(d => (!NeedsMine(d.Material) || Civic.Has(colony, BuildingType.MineDepot))
                && colony.DepositReports.Any(k => k.SiteId == d.Id && k.State != DepositObservation.Depleted)
                && (d.Material != ResourceType.IronOre || ToolChain.Demand(colony).OreMissing > 0 || colony.LocalSettlement.Kind == SettlementKind.Camp) && (Target(colony, d.Material) + colony.ExportInterest.GetValueOrDefault(d.Material)
                    > colony.Stock.Get(d.Material) || colony.LocalSettlement.Kind == SettlementKind.Camp && colony.Stock.Get(d.Material) < 20))
            .OrderBy(d => d.Id);
    }

    /// <summary>Le fer de surface et les carrières restent accessibles ; les filières profondes demandent les installations de la mine.</summary>
    public static bool NeedsMine(ResourceType material) => material is ResourceType.CopperOre or ResourceType.GoldOre or ResourceType.MineralCoal
        or ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond;

    internal static void Daily(WorldState world, Settlement settlement)
    {
        Colony colony = settlement.Owner;
        HouseholdEquipment equipment = settlement.Equipment;
        foreach (var item in new[] { (ResourceType.Pottery, equipment.PotteryExpiry, 2), (ResourceType.Copperware, equipment.CopperwareExpiry, 4) })
        {
            int expired = item.Item2.RemoveAll(t => t <= world.Clock.Ticks);
            if (expired > 0) ResourceAccounting.Record(settlement.Stock, item.Item1, ResourceFlow.Loss, expired);
            int wanted = Target(colony, item.Item1);
            for (int i = 0; i < wanted && settlement.Stock.TryTake(item.Item1, 1, ResourceFlow.Transfer); i++)
                item.Item2.Add(world.Clock.Ticks + item.Item3 * TimeConstants.TicksPerYear);
        }
        foreach (Colonist person in settlement.Population.Where(c => c.Stage != LifeStage.Child && !c.HasShoes))
            if (settlement.Stock.TryTake(ResourceType.Shoes, 1, ResourceFlow.Transfer)) { person.HasShoes = true; person.ShoeDistance = 0; }
        if (!Crisis(colony))
        {
            if (equipment.Jewelry == 0 && settlement.Stock.TryTake(ResourceType.Jewelry, 1, ResourceFlow.Transfer)) equipment.Jewelry++;
            if (Civic.Has(colony, BuildingType.Tavern) && equipment.LastWineDay + 5 <= world.Clock.TotalDays
                && settlement.Stock.TryTake(ResourceType.Wine, 1))
            {
                equipment.LastWineDay = world.Clock.TotalDays;
                foreach (Colonist person in settlement.Population.Where(c => c.Stage != LifeStage.Child)) person.Needs.Comfort = Math.Min(1, person.Needs.Comfort + .1f);
            }
        }
    }

    internal static void Walk(Colonist person, float distance)
    {
        if (!person.HasShoes) return;
        person.ShoeDistance += distance;
        if (person.ShoeDistance < 20000) return;
        person.HasShoes = false; person.ShoeDistance = 0;
        ResourceAccounting.Record(person.Colony.Stock, ResourceType.Shoes, ResourceFlow.Loss, 1);
    }
}
