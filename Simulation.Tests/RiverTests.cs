using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class RiverTests(ITestOutputHelper output)
{
    private static List<(int X, int Y)> RiverTiles(LocalMap map)
    {
        var tiles = new List<(int X, int Y)>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.IsRiver(x, y))
                tiles.Add((x, y));
        return tiles;
    }

    [Theory]
    [InlineData(12345)]
    [InlineData(7)]
    [InlineData(2024)]
    public void La_carte_a_des_rivieres_qui_descendent_jusqu_a_l_eau(int seed)
    {
        var world = new WorldState(seed, startingColonists: 6, migration: false, lifecycle: false);
        LocalMap map = world.Map;
        List<(int X, int Y)> tiles = RiverTiles(map);
        output.WriteLine($"graine {seed} : {tiles.Count} cases de rivière");
        Assert.True(tiles.Count >= 40, "Au moins une vraie rivière.");

        // Toute case de rivière touche une autre case de rivière ou de l'eau : pas de tache isolée.
        foreach ((int x, int y) in tiles)
        {
            bool linked = false;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && map.InBounds(x + dx, y + dy) && map.HasWater(x + dx, y + dy))
                    linked = true;
            Assert.True(linked, $"({x}, {y}) est une case de rivière isolée.");
        }
    }

    [Fact]
    public void Une_riviere_est_a_gue_poissonneuse_et_sans_plante_ni_minerai()
    {
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        LocalMap map = world.Map;
        List<(int X, int Y)> tiles = RiverTiles(map);

        Assert.All(tiles, t =>
        {
            Assert.True(map.IsWalkable(t.X, t.Y));
            Assert.False(map.IsWater(t.X, t.Y));
            Assert.Equal(FloraType.None, map.GetFlora(t.X, t.Y));
            Assert.False(map.CanMine(t.X, t.Y));
            Assert.Equal(Surface.River, map.GetSurface(t.X, t.Y));
            Assert.Equal(LocalMap.RiverMoveCost, map.MoveCost(t.X, t.Y));
            Assert.True(map.GetFish(t.X, t.Y) > 0);
        });
    }

    [Fact]
    public void Les_berges_sont_fertiles_et_donnent_une_cereale_de_plus()
    {
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        LocalMap map = world.Map;

        (int X, int Y) river = RiverTiles(map)[0];
        (int X, int Y)? bank = null;
        for (int d = 1; d <= LocalMap.BankReach && bank is null; d++)
            if (!map.HasWater(river.X + d, river.Y))
                bank = (river.X + d, river.Y);
        Assert.NotNull(bank);
        Assert.True(map.IsFertileBank(bank!.Value.X, bank.Value.Y));
        Assert.Equal(Farming.PlotYield + Farming.BankBonus, Farming.YieldAt(map, bank.Value.X, bank.Value.Y));

        // Loin de toute eau : rendement normal.
        (int X, int Y) far = (-1, -1);
        for (int y = 0; y < map.Height && far.X < 0; y++)
        for (int x = 0; x < map.Width; x++)
            if (!map.HasWater(x, y) && !map.IsFertileBank(x, y)) { far = (x, y); break; }
        Assert.Equal(Farming.PlotYield, Farming.YieldAt(map, far.X, far.Y));
    }

    [Fact]
    public void Ni_les_champs_ni_les_huttes_ne_sont_batis_dans_la_riviere()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Assert.All(Farming.Plots(colony), p => Assert.False(world.Map.IsRiver(p.X, p.Y)));

        for (int i = 0; i < 8; i++)
        {
            if (Urbanism.FindHutSite(world.Map, colony) is not { } site)
                break;
            Building hut = Urbanism.PlanHut(world.Map, colony, site.X, site.Y);
            Assert.All(hut.Tiles, t => Assert.False(world.Map.IsRiver(t.X, t.Y)));
        }
        Assert.False(world.Map.IsRiver(colony.CampX, colony.CampY));
    }

    [Fact]
    public void Une_riviere_se_traverse_a_pied()
    {
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        LocalMap map = world.Map;

        // Cherche une case de rivière dont les deux rives (est et ouest) sont à terre, au même niveau.
        foreach ((int x, int y) in RiverTiles(map))
        {
            if (map.InBounds(x - 1, y) && map.InBounds(x + 1, y) && !map.HasWater(x - 1, y) && !map.HasWater(x + 1, y)
                && map.GetElevation(x - 1, y) == map.GetElevation(x, y) && map.GetElevation(x + 1, y) == map.GetElevation(x, y))
            {
                var path = world.Pathfinder.FindPath(x - 1, y, x + 1, y);
                Assert.NotNull(path);
                // Franchissable : le chemin peut passer dans l'eau ou la contourner par une diagonale.
                return;
            }
        }
        Assert.Fail("Aucune traversée plate trouvée.");
    }

    [Fact]
    public void Une_colonie_de_dix_traverse_deux_ans_avec_ses_rivieres()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        for (long i = 0; i < 2 * TimeConstants.TicksPerYear; i++)
            world.Step();

        int bankPlots = Farming.Plots(colony).Count(p => world.Map.IsFertileBank(p.X, p.Y));
        output.WriteLine($"Parcelles : {Farming.Plots(colony).Count()}, dont {bankPlots} sur les berges ; " +
                         $"poisson pêché : {colony.Labor.TotalProduced(ResourceType.Food)} ; céréales : {colony.Labor.TotalProduced(ResourceType.Grain)}");
        Assert.Equal(10, colony.Members.Count);
    }
}
