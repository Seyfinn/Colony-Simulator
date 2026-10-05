using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Ce que la colonie a cherché sans rien trouver : elle ne le recherche pas avant un an.</summary>
internal enum SearchKind { Trees, Fish, Rocks, Ore, OreQuarry }

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

    /// <summary>
    /// Les cases autour d'un centre, anneau après anneau (le centre, puis les cases à 1, à 2, à 3…). Le balayage est
    /// paresseux : celui qui consomme s'arrête dès qu'il a trouvé, et la carte entière n'est jamais parcourue pour rien.
    /// L'ordre est fixe, pour que les parties restent reproductibles.
    /// </summary>
    private static IEnumerable<(int X, int Y)> Rings(LocalMap map, int centerX, int centerY, int radius)
    {
        if (map.InBounds(centerX, centerY))
            yield return (centerX, centerY);
        for (int r = 1; r <= radius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (map.InBounds(centerX + dx, centerY - r))
                    yield return (centerX + dx, centerY - r);
                if (map.InBounds(centerX + dx, centerY + r))
                    yield return (centerX + dx, centerY + r);
            }
            for (int dy = -r + 1; dy <= r - 1; dy++)
            {
                if (map.InBounds(centerX - r, centerY + dy))
                    yield return (centerX - r, centerY + dy);
                if (map.InBounds(centerX + r, centerY + dy))
                    yield return (centerX + r, centerY + dy);
            }
        }
    }

    /// <summary>Arbres adultes non réservés, du plus proche au plus éloigné du camp (balayage en anneaux).</summary>
    public static IEnumerable<(int X, int Y)> TreesToChop(LocalMap map, Colony colony)
    {
        if (colony.IsExhausted(SearchKind.Trees))
            yield break;
        bool anyTree = false;
        foreach ((int x, int y) in Rings(map, colony.CampX, colony.CampY, TreeSearchRadius))
        {
            if (!map.CanChop(x, y))
                continue;
            anyTree = true;
            if (!colony.Reserved.Contains((x, y)))
                yield return (x, y);
        }
        // Le balayage est allé au bout sans voir un seul arbre : on n'y revient que dans un an.
        if (!anyTree)
            colony.MarkExhausted(SearchKind.Trees);
    }

    /// <summary>Cases d'eau poissonneuses près du camp, avec pour chacune une case de rive d'où pêcher.</summary>
    public static IEnumerable<(int WaterX, int WaterY, int StandX, int StandY)> FishingSpots(LocalMap map, Colony colony)
    {
        if (colony.IsExhausted(SearchKind.Fish))
            yield break;
        bool anyFish = false;
        foreach ((int x, int y) in Rings(map, colony.CampX, colony.CampY, FishingSearchRadius))
        {
            if (map.GetFish(x, y) == 0)
                continue;
            anyFish = true;
            if (colony.Reserved.Contains((x, y)))
                continue;
            foreach ((int dx, int dy) in Neighbors)
            {
                if (map.IsWalkable(x + dx, y + dy))
                {
                    yield return (x, y, x + dx, y + dy);
                    break;
                }
            }
        }
        if (!anyFish)
            colony.MarkExhausted(SearchKind.Fish);
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
        var occupied = colony.PresentMembers.Select(m => (m.TileX, m.TileY)).ToHashSet();

        // Quand la colonie manque de minerai, elle creuse d'abord là où il est proche de la surface (une veine qui
        // affleure, ou à quelques couches sous la roche), en partant de la carrière et en s'éloignant.
        if (preferOre && !colony.IsExhausted(SearchKind.Ore))
        {
            bool anyOre = false;
            foreach ((int x, int y) in Rings(map, quarry.X, quarry.Y, OreSearchRadius))
            {
                if (!map.CanMine(x, y) || map.DepthToOre(x, y, OreProspectDepth) == int.MaxValue)
                    continue;
                anyOre = true;
                if (StandFor(map, colony, occupied, x, y) is { } stand)
                    yield return (x, y, stand.X, stand.Y);
            }
            // Pas un filon à portée : inutile de le rechercher à chaque coup de pioche, on revérifiera dans un an.
            if (!anyOre)
                colony.MarkExhausted(SearchKind.Ore);
        }

        if (colony.IsExhausted(SearchKind.Rocks))
            yield break;
        bool anyRock = false;
        foreach ((int x, int y) in Rings(map, quarry.X, quarry.Y, preferOre ? OreSearchRadius : QuarryWorkRadius))
        {
            if (!map.CanMine(x, y))
                continue;
            anyRock = true;
            if (preferOre && map.DepthToOre(x, y, OreProspectDepth) != int.MaxValue)
                continue; // déjà proposée plus haut
            if (StandFor(map, colony, occupied, x, y) is { } stand)
                yield return (x, y, stand.X, stand.Y);
        }
        if (!anyRock)
            colony.MarkExhausted(SearchKind.Rocks);
    }

    /// <summary>Une case d'où attaquer la roche, de préférence du côté du camp, que personne d'autre n'a réservée.</summary>
    private static (int X, int Y)? StandFor(LocalMap map, Colony colony, HashSet<(int X, int Y)> occupied, int x, int y)
    {
        if (colony.Reserved.Contains((x, y)) || occupied.Contains((x, y)))
            return null;
        foreach ((int dx, int dy) in Neighbors.OrderBy(n => Distance(x + n.Dx, y + n.Dy, colony.CampX, colony.CampY)))
            if (!colony.Reserved.Contains((x + dx, y + dy)) && CanMineFrom(map, x + dx, y + dy, x, y))
                return (x + dx, y + dy);
        return null;
    }

    /// <summary>Rayon (en cases) sur lequel on juge la richesse d'un gisement.</summary>
    private const int QuarryJudgeRadius = 6;

    /// <summary>Richesse (roches à portée, filons visibles comptés triple) à partir de laquelle un gisement suffit : on prend alors le plus proche.</summary>
    private const float RichEnoughQuarry = 45f;

    /// <summary>On n'envisage qu'un nombre limité d'emplacements, les plus proches du camp à pied.</summary>
    private const int QuarryCandidates = 160;

    /// <summary>
    /// Cherche un front de taille près d'un filon de fer visible. On avance à pied depuis le camp, case après case,
    /// sans limite de distance, et on s'arrête au premier gisement qui laisse voir du fer : le plus proche à pied.
    /// </summary>
    public static (int X, int Y)? FindOreQuarry(LocalMap map, int campX, int campY)
    {
        var distance = new HashSet<(int, int)> { (campX, campY) };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((campX, campY));
        var seen = new HashSet<(int, int)>();

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (CanMineFrom(map, x, y, nx, ny) && seen.Add((nx, ny)) && VeinsNear(map, nx, ny))
                    return (nx, ny);
                if (map.CanStep(x, y, nx, ny) && distance.Add((nx, ny)))
                    queue.Enqueue((nx, ny));
            }
        }
        return null;
    }

    private static bool VeinsNear(LocalMap map, int rx, int ry)
    {
        foreach ((int x, int y) in Rings(map, rx, ry, QuarryJudgeRadius))
            if (map.CanMine(x, y) && map.TopMaterial(x, y) == Material.IronOre)
                return true;
        return false;
    }

    /// <summary>Un filon (affleurant ou à faible profondeur) est-il à portée de pioche autour de la carrière ?</summary>
    public static bool OreWithinReach(LocalMap map, Colony colony)
    {
        if (colony.Quarry is not { } quarry || colony.IsExhausted(SearchKind.Ore))
            return false;
        foreach ((int x, int y) in Rings(map, quarry.X, quarry.Y, OreSearchRadius))
            if (map.CanMine(x, y) && map.DepthToOre(x, y, OreProspectDepth) != int.MaxValue)
                return true;
        colony.MarkExhausted(SearchKind.Ore);
        return false;
    }

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
