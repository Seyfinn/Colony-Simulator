using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;

namespace GodColony.Simulation.Tests;

public class PathfinderTests
{
    [Fact]
    public void Les_chemins_ne_franchissent_jamais_une_falaise_ni_l_eau()
    {
        var map = MapGenerator.Generate(160, 160, 42);
        var pathfinder = new Pathfinder(map);
        var random = new Random(1);
        int found = 0;

        for (int attempt = 0; attempt < 60; attempt++)
        {
            int sx = random.Next(160), sy = random.Next(160), tx = random.Next(160), ty = random.Next(160);
            if (!map.IsWalkable(sx, sy))
                continue;
            List<(int X, int Y)>? path = pathfinder.FindPath(sx, sy, tx, ty);
            if (path is null)
                continue;

            found++;
            (int px, int py) = (sx, sy);
            foreach ((int x, int y) in path)
            {
                Assert.True(Math.Max(Math.Abs(x - px), Math.Abs(y - py)) == 1, "Chaque pas va vers une case voisine.");
                Assert.True(map.CanStep(px, py, x, y), $"Pas interdit de ({px},{py}) vers ({x},{y}).");
                (px, py) = (x, y);
            }
            Assert.Equal((tx, ty), (px, py));
        }
        Assert.True(found > 20, "La plupart des trajets devraient être possibles.");
    }

    [Fact]
    public void Une_destination_dans_l_eau_est_inaccessible()
    {
        var map = MapGenerator.Generate(160, 160, 42);
        var pathfinder = new Pathfinder(map);
        (int wx, int wy) = Find(map, (x, y) => map.IsWater(x, y));
        (int lx, int ly) = Find(map, (x, y) => map.IsWalkable(x, y));
        Assert.Null(pathfinder.FindPath(lx, ly, wx, wy));
    }

    private static (int, int) Find(LocalMap map, Func<int, int, bool> predicate)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (predicate(x, y))
                return (x, y);
        throw new InvalidOperationException();
    }
}
