using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Où travailler : quels arbres abattre, où ouvrir la carrière, quelle roche miner ensuite.</summary>
public static class WorkSites
{
    private const int TreeSearchRadius = 25;
    private const int FishingSearchRadius = 30;
    private const int QuarrySearchRadius = 28;
    private const int QuarryWorkRadius = 10;

    /// <summary>Un filon de fer affleure-t-il à la carrière ? Les mineurs le voient sans avoir à creuser.</summary>
    public static bool OreVisibleNearQuarry(LocalMap map, Colony colony)
    {
        if (colony.Quarry is not { } quarry)
            return false;
        for (int dy = -OreSightRadius; dy <= OreSightRadius; dy++)
        for (int dx = -OreSightRadius; dx <= OreSightRadius; dx++)
        {
            int x = quarry.X + dx, y = quarry.Y + dy;
            if (map.InBounds(x, y) && map.CanMine(x, y) && map.TopMaterial(x, y) == Material.IronOre)
                return true;
        }
        return false;
    }

    /// <summary>Distance (en cases) à laquelle on repère un filon qui affleure.</summary>
    private const int OreSightRadius = 14;

    /// <summary>Quand on cherche du minerai, on s'éloigne un peu plus de la carrière.</summary>
    private const int OreSearchRadius = 16;

    /// <summary>Profondeur (en couches) jusqu'où les mineurs repèrent un filon sous la roche.</summary>
    public const int OreProspectDepth = 4;

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
        int radius = preferOre ? OreSearchRadius : QuarryWorkRadius;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int x = quarry.X + dx, y = quarry.Y + dy;
            if (map.CanMine(x, y) && !colony.Reserved.Contains((x, y)) && !occupied.Contains((x, y)))
                rocks.Add((x, y, dx * dx + dy * dy));
        }

        // Quand la colonie manque de minerai, elle creuse là où il est le plus proche de la surface : une veine
        // qui affleure d'abord, puis celles qui ne sont qu'à une, deux, trois couches sous la roche.
        IEnumerable<(int X, int Y, int Distance)> ordered = preferOre
            ? rocks.OrderBy(r => map.DepthToOre(r.X, r.Y, OreProspectDepth)).ThenBy(r => r.Distance)
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

    /// <summary>Rayon (en cases) sur lequel on juge la richesse d'un gisement.</summary>
    private const int QuarryJudgeRadius = 6;

    /// <summary>Richesse (roches à portée, filons visibles comptés triple) à partir de laquelle un gisement suffit : on prend alors le plus proche.</summary>
    private const float RichEnoughQuarry = 45f;

    /// <summary>On n'envisage qu'un nombre limité d'emplacements, les plus proches du camp à pied.</summary>
    private const int QuarryCandidates = 160;

    /// <summary>Une carrière qui offre moins de roches à portée que cela est épuisée : la colonie en cherche une autre.</summary>
    public const int ExhaustedQuarryRocks = 25;

    /// <summary>Nombre de roches minables à portée de travail autour de la carrière.</summary>
    public static int RocksLeft(LocalMap map, (int X, int Y) quarry)
    {
        int count = 0;
        for (int dy = -QuarryWorkRadius; dy <= QuarryWorkRadius; dy++)
        for (int dx = -QuarryWorkRadius; dx <= QuarryWorkRadius; dx++)
            if (map.CanMine(quarry.X + dx, quarry.Y + dy))
                count++;
        return count;
    }

    /// <summary>
    /// Choisit où ouvrir la carrière : parmi les roches que l'on atteint à pied depuis le camp, le gisement le plus
    /// riche (beaucoup de roche à portée, des filons de fer visibles), sans aller trop loin du camp.
    /// </summary>
    public static (int X, int Y)? FindQuarry(LocalMap map, int campX, int campY)
    {
        var distance = new Dictionary<(int, int), int> { [(campX, campY)] = 0 };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((campX, campY));
        var candidates = new List<((int X, int Y) Rock, int Steps)>();
        var seen = new HashSet<(int, int)>();

        while (queue.Count > 0 && candidates.Count < QuarryCandidates)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (CanMineFrom(map, x, y, nx, ny) && seen.Add((nx, ny)))
                    candidates.Add(((nx, ny), distance[(x, y)] + 1));
                if (Math.Max(Math.Abs(nx - campX), Math.Abs(ny - campY)) <= QuarrySearchRadius
                    && map.CanStep(x, y, nx, ny) && !distance.ContainsKey((nx, ny)))
                {
                    distance[(nx, ny)] = distance[(x, y)] + 1;
                    queue.Enqueue((nx, ny));
                }
            }
        }
        if (candidates.Count == 0)
            return null;

        // Un gisement est « assez riche » s'il offre de quoi tailler longtemps ; parmi ceux-là, on prend le plus proche du camp.
        // Si aucun n'est assez riche, on se contente du plus riche.
        (int X, int Y)? best = null;
        float bestScore = float.MinValue;
        foreach (((int rx, int ry), int steps) in candidates)
        {
            float richness = 0f;
            for (int dy = -QuarryJudgeRadius; dy <= QuarryJudgeRadius; dy++)
            for (int dx = -QuarryJudgeRadius; dx <= QuarryJudgeRadius; dx++)
            {
                int x = rx + dx, y = ry + dy;
                if (!map.CanMine(x, y))
                    continue;
                richness += 1f;
                if (map.TopMaterial(x, y) == Material.IronOre)
                    richness += 3f;
            }
            float score = richness >= RichEnoughQuarry ? 1000f - steps : richness;
            if (score > bestScore)
            {
                bestScore = score;
                best = (rx, ry);
            }
        }
        return best;
    }

    private static int Distance(int x0, int y0, int x1, int y1) => Math.Max(Math.Abs(x0 - x1), Math.Abs(y0 - y1));
}
