using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

/// <summary>La carte est grande (200 × 200) et faite de vastes zones : un massif, de grandes forêts, de grandes plaines, des fleuves larges.</summary>
public class MapZonesTests(ITestOutputHelper output)
{
    private const int Size = MapGenerator.DefaultSize;

    /// <summary>Taille (en cases) de chaque zone connexe de cases vérifiant le critère ; <paramref name="gap"/> fusionne les zones séparées de peu.</summary>
    private static List<int> Zones(LocalMap map, Func<int, int, bool> criterion, int gap = 1)
    {
        int w = map.Width, h = map.Height;
        var inside = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            inside[y * w + x] = criterion(x, y);

        var seen = new bool[w * h];
        var sizes = new List<int>();
        var queue = new Queue<int>();
        for (int start = 0; start < w * h; start++)
        {
            if (!inside[start] || seen[start])
                continue;
            int count = 0;
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                count++;
                for (int dy = -gap; dy <= gap; dy++)
                for (int dx = -gap; dx <= gap; dx++)
                {
                    int nx = current % w + dx, ny = current / w + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || !inside[ny * w + nx] || seen[ny * w + nx])
                        continue;
                    seen[ny * w + nx] = true;
                    queue.Enqueue(ny * w + nx);
                }
            }
            sizes.Add(count);
        }
        return sizes.OrderByDescending(s => s).ToList();
    }

    /// <summary>Part de cases couvertes d'arbres dans le carré de 7 × 7 centré sur la case.</summary>
    private static float TreeDensity(LocalMap map, int x, int y)
    {
        int trees = 0, total = 0;
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
        {
            if (!map.InBounds(x + dx, y + dy))
                continue;
            total++;
            if (map.GetFlora(x + dx, y + dy) == FloraType.Tree)
                trees++;
        }
        return trees / (float)total;
    }

    private static List<(int X, int Y)> RiverTiles(LocalMap map)
    {
        var tiles = new List<(int X, int Y)>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.IsRiver(x, y))
                tiles.Add((x, y));
        return tiles;
    }

    [Fact]
    public void La_carte_d_une_colonie_fait_deux_cents_cases_de_cote()
    {
        Assert.Equal(200, Size);
        var world = new WorldState(12345, startingColonists: 6, migration: false, lifecycle: false);
        Assert.Equal(200, world.Map.Width);
        Assert.Equal(200, world.Map.Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(99)]
    [InlineData(12345)]
    public void Chaque_milieu_forme_de_grandes_zones(int seed)
    {
        LocalMap map = MapGenerator.Generate(Size, Size, seed);

        List<int> mountains = Zones(map, (x, y) => map.IsMountain(x, y));
        List<int> forests = Zones(map, (x, y) => TreeDensity(map, x, y) >= 0.35f, gap: 4);
        List<int> meadows = Zones(map, (x, y) => !map.IsWater(x, y) && !map.IsMountain(x, y) && TreeDensity(map, x, y) < 0.12f);
        List<int> waters = Zones(map, (x, y) => map.IsWater(x, y));
        output.WriteLine($"graine {seed} : montagnes {string.Join(", ", mountains.Take(3))} ; forêts {string.Join(", ", forests.Take(3))} ; " +
                         $"plaines dégagées {string.Join(", ", meadows.Take(3))} ; eaux {string.Join(", ", waters.Take(3))}");

        // Un massif d'un seul tenant (plus de la moitié de la montagne), de plusieurs milliers de cases.
        Assert.True(mountains[0] >= 4000, $"Le massif ne compte que {mountains[0]} cases.");
        Assert.True(mountains[0] >= 0.55f * mountains.Sum(), "La montagne doit former un grand massif, pas des îlots.");
        // Une vaste forêt et une vaste plaine dégagée.
        Assert.True(forests[0] >= 2000, $"La plus grande forêt ne compte que {forests[0]} cases.");
        Assert.True(meadows[0] >= 8000, $"La plus grande plaine dégagée ne compte que {meadows[0]} cases.");
        // Un grand lac ou une mer.
        Assert.True(waters[0] >= 1000, $"Le plus grand plan d'eau ne compte que {waters[0]} cases.");
    }

    [Fact]
    public void Un_pays_de_forets_a_une_forêt_immense_et_des_hautes_terres_beaucoup_de_roche()
    {
        LocalMap woodland = MapGenerator.Generate(Size, Size, 42, MapStyle.Woodlands);
        List<int> forests = Zones(woodland, (x, y) => TreeDensity(woodland, x, y) >= 0.35f, gap: 4);
        output.WriteLine($"pays de forêts : {string.Join(", ", forests.Take(3))}");
        Assert.True(forests[0] >= 4000, $"La forêt ne compte que {forests[0]} cases.");

        LocalMap highland = MapGenerator.Generate(Size, Size, 42, MapStyle.Highlands);
        List<int> mountains = Zones(highland, (x, y) => highland.IsMountain(x, y));
        output.WriteLine($"hautes terres : {string.Join(", ", mountains.Take(3))}");
        Assert.True(mountains[0] >= 8000, $"Le massif ne compte que {mountains[0]} cases.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(99)]
    [InlineData(12345)]
    public void Le_fleuve_est_un_ruisseau_pres_de_la_montagne_et_large_a_l_aval(int seed)
    {
        LocalMap map = MapGenerator.Generate(Size, Size, seed);
        List<(int X, int Y)> river = RiverTiles(map);
        int narrow = river.Count(t => map.RiverWidth(t.X, t.Y) == 1);
        int wide = river.Count(t => map.RiverWidth(t.X, t.Y) >= 3);
        output.WriteLine($"graine {seed} : {river.Count} cases de rivière, dont {narrow} de ruisseau et {wide} d'au moins 3 cases de large, " +
                         $"largeur maximale {river.Max(t => map.RiverWidth(t.X, t.Y))}");

        Assert.True(narrow >= 20, "Le fleuve naît en ruisseau d'une case de large.");
        Assert.True(wide >= 80, "Le fleuve s'élargit : au moins trois cases de large sur un long tronçon.");
        Assert.All(river, t => Assert.True(map.RiverWidth(t.X, t.Y) >= 1));
        Assert.All(river, t => Assert.True(map.IsWalkable(t.X, t.Y) && !map.IsWater(t.X, t.Y), "Un fleuve large reste un gué."));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(99)]
    [InlineData(12345)]
    public void Le_courant_va_toujours_vers_une_case_voisine_et_finit_dans_l_eau_sans_tourner_en_rond(int seed)
    {
        LocalMap map = MapGenerator.Generate(Size, Size, seed);
        List<(int X, int Y)> river = RiverTiles(map);
        int ends = 0;

        foreach ((int sx, int sy) in river)
        {
            (int x, int y) = (sx, sy);
            int steps = 0;
            while (map.RiverDownstream(x, y) is { } next)
            {
                Assert.True(Math.Max(Math.Abs(next.X - x), Math.Abs(next.Y - y)) == 1, $"({x}, {y}) coule vers une case qui n'est pas voisine.");
                (x, y) = next;
                Assert.True(++steps <= river.Count, $"Le courant tourne en rond depuis ({sx}, {sy}).");
            }
            // Au bout du courant : de l'eau, ou la fin de la carte ; jamais une case de terre qui n'a plus de sortie, sauf rares bouts de lit.
            if (!map.IsRiver(x, y) || map.HasWater(x, y))
                ends++;
        }
        Assert.True(ends >= river.Count * 0.95f, "Presque tout le courant doit finir dans l'eau.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(99)]
    [InlineData(12345)]
    public void Les_berges_d_un_fleuve_large_se_montent_a_pied(int seed)
    {
        LocalMap map = MapGenerator.Generate(Size, Size, seed);
        int cliffs = 0, banks = 0;
        foreach ((int x, int y) in RiverTiles(map).Where(t => map.IsWideRiver(t.X, t.Y)))
        {
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (!map.InBounds(nx, ny) || map.IsRiver(nx, ny) || map.IsWater(nx, ny))
                    continue;
                banks++;
                if (Math.Abs(map.GetElevation(nx, ny) - map.GetElevation(x, y)) > 1)
                    cliffs++;
            }
        }
        output.WriteLine($"graine {seed} : {banks} berges, dont {cliffs} en falaise");
        Assert.True(banks > 0);
        // Une falaise de deux niveaux enfermerait les gués : le relief est adouci au bord du lit.
        Assert.True(cliffs <= banks * 0.01f, $"{cliffs} berges sur {banks} sont des falaises infranchissables.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(12345)]
    public void Chaque_peuple_a_de_la_roche_du_bois_et_du_poisson_a_portee_de_marche(int seed)
    {
        var world = new WorldState(seed, startingColonists: 6, migration: false, lifecycle: false, colonyCount: 4, trade: false);
        foreach (Colony colony in world.Colonies)
        {
            int trees = WorkSites.TreesToChop(colony.Map, colony).Count();
            int fishing = WorkSites.FishingSpots(colony.Map, colony).Count();
            output.WriteLine($"graine {seed}, {colony.Species.Name} : camp ({colony.CampX}, {colony.CampY}), carrière {colony.Quarry}, {trees} arbres à portée, {fishing} cases de pêche");
            Assert.NotNull(colony.Quarry);
            Assert.True(trees >= 60, $"{colony.Species.Name} : seulement {trees} arbres à portée de hache.");
            // L'eau est rare : seule une région où passe une rivière du monde a du poisson à portée de marche.
            if (world.WorldMap.Grid[world.WorldMap.TileOf(colony)].River > 0)
                Assert.True(fishing > 0, $"{colony.Species.Name} : pas d'eau poissonneuse à portée.");
            Assert.True(world.Map.InBounds(colony.CampX, colony.CampY));
        }
    }
}
