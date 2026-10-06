using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class MajorBuildingTests
{
    [Fact]
    public void La_grande_mine_reserve_toute_sa_parcelle_et_les_habitants_l_approvisionnent_sans_excedent()
    {
        var world = new WorldState(12345, 128, 128, startingColonists: 12, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        colony.Stock.Add(ResourceType.Food, 2000);
        colony.Stock.Add(ResourceType.Wood, 500);
        colony.Stock.Add(ResourceType.Stone, 500);
        (int x, int y) = Assert.IsType<(int X, int Y)>(Urbanism.FindSite(colony.Map, colony, BuildingType.MineDepot));
        Building mine = Urbanism.PlanBuilding(colony.Map, colony, BuildingType.MineDepot, x, y);
        Assert.Equal((6, 4), (mine.Width, mine.Height));
        PlotReservation parcel = Assert.IsType<PlotReservation>(colony.Layout.ParcelById(mine.ParcelId));
        Assert.Equal((mine.Width, mine.Height), (parcel.Width, parcel.Height));
        Assert.True(mine.HasDoor);
        Assert.All(mine.Tiles, t => Assert.True(colony.Spatial.Has(t.X, t.Y, CellUse.Building)));
        Assert.NotNull(colony.Pathfinder.FindPath(colony.CampX, colony.CampY, mine.AccessX, mine.AccessY));
        for (int tick = 0; tick < 80 * TimeConstants.TicksPerDay && !mine.IsComplete; tick++) world.Step();
        Assert.True(mine.IsComplete, $"Mine inachevée : bois {mine.WoodDelivered}, pierre {mine.StoneDelivered}, travail {mine.Progress:P0}.");
        Assert.Equal((72, 72), (mine.WoodDelivered, mine.StoneDelivered));
        Assert.Equal((0, 0), (mine.WoodInTransit, mine.StoneInTransit));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("La mine est achevée"));
        Assert.Empty(mine.Tiles.Intersect(colony.Buildings.Where(b => b != mine).SelectMany(b => b.Tiles)));
    }

    [Fact]
    public void Les_habitants_achevent_le_grand_barrage_et_sa_retenue()
    {
        var world = new WorldState(12345, startingColonists: 12, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        colony.Stock.Add(ResourceType.Food, 2000);
        colony.Stock.Add(ResourceType.Wood, 500);
        colony.Stock.Add(ResourceType.Stone, 500);
        var site = Assert.IsType<(int X, int Y, Reservoir Reservoir)>(Hydrology.FindSite(colony.Map, colony));
        Building dam = Urbanism.PlanBuilding(colony.Map, colony, BuildingType.Dam, site.X, site.Y);
        Assert.True(dam.HasDoor);
        Assert.False(dam.Contains(dam.AccessX, dam.AccessY));
        for (int tick = 0; tick < 80 * TimeConstants.TicksPerDay && !dam.IsComplete; tick++) world.Step();
        Assert.True(dam.IsComplete, $"Barrage inachevé : bois {dam.WoodDelivered}, pierre {dam.StoneDelivered}, travail {dam.Progress:P0}.");
        Assert.Equal((100, 240), (dam.WoodDelivered, dam.StoneDelivered));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("un lac") && t.Text.Contains("se forme"));
        Assert.Contains(Enumerable.Range(0, colony.Map.Width * colony.Map.Height), i => colony.Map.IsFlooded(i % colony.Map.Width, i / colony.Map.Width));
        Assert.All(dam.Tiles, t => Assert.False(colony.Map.IsFlooded(t.X, t.Y)));
    }

    [Theory]
    [InlineData(ResourceType.CopperOre)]
    [InlineData(ResourceType.GoldOre)]
    [InlineData(ResourceType.MineralCoal)]
    [InlineData(ResourceType.Diamond)]
    public void Les_filieres_profondes_attendent_une_mine_achevee(ResourceType material)
    {
        var world = new WorldState(12345, 128, 128, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Deposit deposit = world.VisitRegion(colony.LocalSettlement.RegionTileIndex).Deposits.First();
        deposit.Material = material;
        deposit.Mode = DepositMode.Finite;
        deposit.InitialReserve = deposit.RemainingReserve = 50;
        deposit.DailyLimit = 10;
        colony.DepositReports.Add(new DepositKnowledge { SiteId = deposit.Id, Region = deposit.Region, Material = material, State = DepositObservation.Surveyed });
        colony.ExportInterest[material] = 10;
        Assert.Equal(BuildingType.MineDepot, ExtendedIndustry.NextWorkshop(colony));
        Assert.DoesNotContain(deposit, ExtendedIndustry.ExtractionJobs(world, colony));
        Assert.Equal(0, DepositExtraction.Extract(world, colony, deposit, 2));
        Assert.Equal(50, deposit.RemainingReserve);
        var mine = new Building(BuildingType.MineDepot, 10, 10);
        colony.Buildings.Add(mine);
        Assert.Equal(0, DepositExtraction.Extract(world, colony, deposit, 2));
        mine.Progress = 1;
        Assert.Contains(deposit, ExtendedIndustry.ExtractionJobs(world, colony));
        Assert.Equal(2, DepositExtraction.Extract(world, colony, deposit, 2));
        Assert.Equal(48, deposit.RemainingReserve);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Le_barrage_traverse_le_courant_et_son_orientation_survit_a_la_sauvegarde(bool side)
    {
        var world = new WorldState(12345, 128, 128, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        colony.Map.SetRiver(20, 20, side ? 21 : 20, side ? 20 : 21, width: 3);
        Building dam = Urbanism.PlanBuilding(colony.Map, colony, BuildingType.Dam, 20, 20);
        Assert.Equal(side ? (3, 5) : (5, 3), (dam.Width, dam.Height));
        Assert.Equal((20, 20), (dam.RiverX, dam.RiverY));
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            Building loaded = Assert.Single(WorldSave.Load(path).World.Colonies[0].Buildings, b => b.IsDam);
            Assert.Equal((dam.X, dam.Y, dam.Width, dam.Height), (loaded.X, loaded.Y, loaded.Width, loaded.Height));
        }
        finally { File.Delete(path); }
    }
}
