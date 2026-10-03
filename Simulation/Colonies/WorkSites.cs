using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Où travailler : quels arbres abattre, où ouvrir la carrière, quelle roche miner ensuite.</summary>
public static class WorkSites
{
    private const int TreeSearchRadius = 25;
    private const int FishingSearchRadius = 30;
    private const int QuarrySearchRadius = 40;
    private const int QuarryWorkRadius = 10;

    private static readonly (int Dx, int Dy)[] Neighbors =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// Un mineur debout sur une case peut attaquer une roche voisine qui le dépasse d'au plus deux niveaux,
    /// ou qui est un niveau plus bas. On creuse ainsi la montagne en gradins, sans jamais s'enfermer dans un trou.
    /// </summary>
    public static bool CanMineFrom(LocalMap map, int standX, int standY, int rockX, int rockY)
    {
        if (!map.IsWalkable(standX, standY) || !map.CanMine(rockX, rockY))
            return false;
        if (Math.Max(Math.Abs(standX - rockX), Math.Abs(standY - rockY)) != 1)
            return false;
        int difference = map.GetElevation(rockX, rockY) - map.GetElevation(standX, standY);
        return difference is >= -1 and <= 2;
    }

    /// <summary>Arbres adultes non réservés, du plus proche au plus éloigné du camp.</summary>
    public static IEnumerable<(int X, int Y)> TreesToChop(LocalMap map, Colony colony)
    {
        var trees = new List<(int X, int Y, int Distance)>();
        for (int dy = -TreeSearchRadius; dy <= TreeSearchRadius; dy++)
        for (int dx = -TreeSearchRadius; dx <= TreeSearchRadius; dx++)
        {
            int x = colony.CampX + dx, y = colony.CampY + dy;
            if (map.CanChop(x, y) && !colony.Reserved.Contains((x, y)))
                trees.Add((x, y, dx * dx + dy * dy));
        }
        return trees.OrderBy(t => t.Distance).Select(t => (t.X, t.Y));
    }

    /// <summary>Cases d'eau poissonneuses près du camp, avec pour chacune une case de rive d'où pêcher.</summary>
    public static IEnumerable<(int WaterX, int WaterY, int StandX, int StandY)> FishingSpots(LocalMap map, Colony colony)
    {
        var spots = new List<(int WaterX, int WaterY, int StandX, int StandY, int Distance)>();
        for (int dy = -FishingSearchRadius; dy <= FishingSearchRadius; dy++)
        for (int dx = -FishingSearchRadius; dx <= FishingSearchRadius; dx++)
        {
            int x = colony.CampX + dx, y = colony.CampY + dy;
            if (!map.InBounds(x, y) || map.GetFish(x, y) == 0 || colony.Reserved.Contains((x, y)))
                continue;
            foreach ((int nx, int ny) in Neighbors.Select(n => (x + n.Dx, y + n.Dy)))
            {
                if (map.IsWalkable(nx, ny))
                {
                    spots.Add((x, y, nx, ny, Distance(nx, ny, colony.CampX, colony.CampY)));
                    break;
                }
            }
        }
        return spots.OrderBy(s => s.Distance).Select(s => (s.WaterX, s.WaterY, s.StandX, s.StandY));
    }

    /// <summary>
    /// Roches à miner autour de la carrière, les plus proches de son centre d'abord,
    /// avec pour chacune une case d'où la travailler.
    /// </summary>
    public static IEnumerable<(int RockX, int RockY, int StandX, int StandY)> RocksToMine(LocalMap map, Colony colony, bool preferOre = false)
    {
        if (colony.Quarry is not { } quarry)
            yield break;

        // On ne creuse jamais sous les pieds de quelqu'un.
        var occupied = colony.Members.Select(m => (m.TileX, m.TileY)).ToHashSet();

        var rocks = new List<(int X, int Y, int Distance)>();
        for (int dy = -QuarryWorkRadius; dy <= QuarryWorkRadius; dy++)
        for (int dx = -QuarryWorkRadius; dx <= QuarryWorkRadius; dx++)
        {
            int x = quarry.X + dx, y = quarry.Y + dy;
            if (map.CanMine(x, y) && !colony.Reserved.Contains((x, y)) && !occupied.Contains((x, y)))
                rocks.Add((x, y, dx * dx + dy * dy));
        }

        // Quand la colonie manque de minerai, les veines de fer visibles passent avant la pierre ordinaire.
        IEnumerable<(int X, int Y, int Distance)> ordered = preferOre
            ? rocks.OrderBy(r => map.TopMaterial(r.X, r.Y) == Material.IronOre ? 0 : 1).ThenBy(r => r.Distance)
            : rocks.OrderBy(r => r.Distance);
        foreach ((int x, int y, _) in ordered)
        {
            // On préfère se tenir du côté du camp, sur une case que personne d'autre n'a réservée.
            foreach ((int dx, int dy) in Neighbors.OrderBy(n => Distance(x + n.Dx, y + n.Dy, colony.CampX, colony.CampY)))
            {
                if (!colony.Reserved.Contains((x + dx, y + dy)) && CanMineFrom(map, x + dx, y + dy, x, y))
                {
                    yield return (x, y, x + dx, y + dy);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Ouvre la carrière sur la roche minable la plus proche du camp à pied,
    /// en parcourant le terrain praticable depuis le feu.
    /// </summary>
    public static (int X, int Y)? FindQuarry(LocalMap map, int campX, int campY)
    {
        var visited = new HashSet<(int, int)> { (campX, campY) };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((campX, campY));

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (CanMineFrom(map, x, y, nx, ny))
                    return (nx, ny);
                if (Math.Max(Math.Abs(nx - campX), Math.Abs(ny - campY)) <= QuarrySearchRadius
                    && map.CanStep(x, y, nx, ny) && visited.Add((nx, ny)))
                    queue.Enqueue((nx, ny));
            }
        }
        return null;
    }

    private static int Distance(int x0, int y0, int x1, int y1) => Math.Max(Math.Abs(x0 - x1), Math.Abs(y0 - y1));
}
