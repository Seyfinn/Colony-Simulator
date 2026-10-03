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

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void La_recherche_aboutit_exactement_quand_le_but_est_atteignable(int seed)
    {
        // La montagne creusée au hasard se couvre de replats isolés et de pas à sens unique :
        // la recherche doit renoncer vite à un but inaccessible, sans jamais manquer un but atteignable.
        var map = MapGenerator.Generate(64, 64, seed);
        var pathfinder = new Pathfinder(map);
        var random = new Random(seed);
        var mountain = new List<(int X, int Y)>();
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            if (map.IsMountain(x, y))
                mountain.Add((x, y));
        int unreachable = 0;

        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < 1500; i++)
            {
                int x = random.Next(64), y = random.Next(64);
                if (map.CanMine(x, y))
                    map.Mine(x, y);
            }

            for (int attempt = 0; attempt < 8; attempt++)
            {
                int sx = random.Next(64), sy = random.Next(64);
                if (!map.IsWalkable(sx, sy))
                    continue;
                bool[] reachable = Reachable(map, sx, sy);
                for (int i = 0; i < 200; i++)
                {
                    // Un but sur deux en montagne, là où le minage laisse des replats isolés.
                    (int gx, int gy) = i % 2 == 0 ? mountain[random.Next(mountain.Count)] : (random.Next(64), random.Next(64));
                    if (!map.IsWalkable(gx, gy) || (gx, gy) == (sx, sy))
                        continue;
                    Assert.Equal(reachable[gy * 64 + gx], pathfinder.FindPath(sx, sy, gx, gy) is not null);
                    if (!reachable[gy * 64 + gx])
                        unreachable++;
                }
            }
        }
        Assert.True(unreachable > 20, $"Le test doit comporter des buts inaccessibles ({unreachable}).");
    }

    [Fact]
    public void Un_long_couloir_a_sens_unique_se_parcourt_dans_un_sens_seulement()
    {
        // Deux couloirs en serpentin, de plus de 300 cases chacun, entre des falaises : le premier (niveau 5) mène au second
        // (niveau 6) par une seule diagonale, qu'on monte mais qu'on ne redescend pas (les cases du coin sont au niveau 4).
        var map = new LocalMap(41, 41, 0);
        for (int y = 0; y < 41; y++)
        for (int x = 0; x < 41; x++)
            map.SetGenerated(x, y, 9, SoilType.Grass, FloraType.None, 0f);
        Serpentine(map, firstRow: 1, rows: 9, elevation: 5, rightFirst: true, lastX: 38);
        Serpentine(map, firstRow: 19, rows: 10, elevation: 6, rightFirst: false, lastX: 39);
        map.SetGenerated(39, 17, 4, SoilType.Grass, FloraType.None, 0f);
        map.SetGenerated(38, 18, 4, SoilType.Grass, FloraType.None, 0f);
        map.SetGenerated(39, 18, 6, SoilType.Grass, FloraType.None, 0f);
        var pathfinder = new Pathfinder(map);

        List<(int X, int Y)>? path = pathfinder.FindPath(1, 1, 39, 37);
        Assert.NotNull(path);
        Assert.True(path.Count > 600, "Le chemin suit tout le serpentin.");
        Assert.Contains((39, 18), path);
        Assert.Null(pathfinder.FindPath(39, 37, 1, 1));
    }

    /// <summary>Un couloir qui parcourt des rangées une sur deux, reliées alternativement à droite et à gauche.</summary>
    private static void Serpentine(LocalMap map, int firstRow, int rows, int elevation, bool rightFirst, int lastX)
    {
        for (int i = 0; i < rows; i++)
        {
            int y = firstRow + 2 * i;
            for (int x = 1; x <= (i == rows - 1 ? lastX : 39); x++)
                map.SetGenerated(x, y, elevation, SoilType.Grass, FloraType.None, 0f);
            if (i == rows - 1)
                break;
            bool towardRight = rightFirst == (i % 2 == 0);
            map.SetGenerated(towardRight ? 39 : 1, y + 1, elevation, SoilType.Grass, FloraType.None, 0f);
        }
    }

    /// <summary>Toutes les cases qu'on atteint depuis (sx, sy), pas à pas, avec les règles de la recherche de chemin.</summary>
    private static bool[] Reachable(LocalMap map, int sx, int sy)
    {
        var seen = new bool[map.Width * map.Height];
        var queue = new Queue<(int X, int Y)>();
        seen[sy * map.Width + sx] = true;
        queue.Enqueue((sx, sy));
        while (queue.TryDequeue(out (int X, int Y) current))
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = current.X + dx, ny = current.Y + dy;
                if ((dx == 0 && dy == 0) || !map.CanStep(current.X, current.Y, nx, ny))
                    continue;
                if (dx != 0 && dy != 0
                    && (!map.CanStep(current.X, current.Y, nx, current.Y) || !map.CanStep(current.X, current.Y, current.X, ny)))
                    continue;
                if (seen[ny * map.Width + nx])
                    continue;
                seen[ny * map.Width + nx] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return seen;
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
