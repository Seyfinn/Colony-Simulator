using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>Choisit un bon emplacement et y installe une nouvelle colonie.</summary>
public static class ColonyFounder
{
    private const int StartingFood = 80;
    private const int GatherRadius = 5;

    public static Colony Found(LocalMap map, Random random, string name, int colonistCount, Func<int> nextId)
    {
        (int campX, int campY) = FindCampSite(map);

        // On dégage la place autour du feu.
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (map.InBounds(campX + dx, campY + dy))
                map.ClearFlora(campX + dx, campY + dy);

        var colony = new Colony(name, campX, campY, FindGatherSpots(map, campX, campY))
        {
            Quarry = WorkSites.FindQuarry(map, campX, campY),
        };
        colony.Stock.Add(ResourceType.Food, StartingFood);

        for (int i = 0; i < colonistCount; i++)
        {
            Sex sex = i % 2 == 0 ? Sex.Female : Sex.Male;
            (int x, int y) = colony.GatherSpots[random.Next(colony.GatherSpots.Count)];
            var colonist = new Colonist(nextId(), Names.Pick(sex, random), sex, colony, Skills.Random(random), x + 0.5f, y + 0.5f);
            colonist.Needs.Food = 0.6f + 0.35f * random.NextSingle();
            colonist.Needs.Rest = 0.6f + 0.35f * random.NextSingle();
            colonist.Needs.Leisure = 0.6f + 0.35f * random.NextSingle();
            colony.Members.Add(colonist);
        }
        colony.AssignSectors();
        return colony;
    }

    /// <summary>
    /// Un bon site est une plaine plate, proche de l'eau, des baies, de la forêt et d'une montagne,
    /// sans être collée au bord de la carte.
    /// </summary>
    public static (int X, int Y) FindCampSite(LocalMap map)
    {
        (int, int) best = (map.Width / 2, map.Height / 2);
        float bestScore = float.MinValue;
        const int margin = 12;

        for (int y = margin; y < map.Height - margin; y += 2)
        for (int x = margin; x < map.Width - margin; x += 2)
        {
            if (!IsFlatClearing(map, x, y))
                continue;

            float score = 0;
            bool water = false, mountain = false;
            for (int dy = -25; dy <= 25; dy++)
            for (int dx = -25; dx <= 25; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (!map.InBounds(nx, ny))
                    continue;
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (distance <= 15 && map.GetFlora(nx, ny) == FloraType.Bush) score += 3f;
                if (distance <= 10 && map.GetFlora(nx, ny) == FloraType.Tree) score += 0.15f;
                if (distance <= 12 && map.IsWater(nx, ny)) water = true;
                if (map.IsMountain(nx, ny)) mountain = true;
            }
            if (water) score += 20f;
            if (mountain) score += 15f;
            score -= 0.1f * (Math.Abs(x - map.Width / 2) + Math.Abs(y - map.Height / 2));

            if (score > bestScore)
            {
                bestScore = score;
                best = (x, y);
            }
        }
        return best;
    }

    private static bool IsFlatClearing(LocalMap map, int x, int y)
    {
        if (map.IsMountain(x, y) || !map.IsWalkable(x, y))
            return false;
        int elevation = map.GetElevation(x, y);
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            if (!map.IsWalkable(x + dx, y + dy) || map.GetElevation(x + dx, y + dy) != elevation)
                return false;
        return true;
    }

    /// <summary>Cases accessibles à pied autour du feu, par proximité croissante.</summary>
    private static List<(int X, int Y)> FindGatherSpots(LocalMap map, int campX, int campY)
    {
        var spots = new List<(int X, int Y)>();
        var visited = new HashSet<(int, int)> { (campX, campY) };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((campX, campY));

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            if ((x, y) != (campX, campY))
                spots.Add((x, y));
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (Math.Max(Math.Abs(nx - campX), Math.Abs(ny - campY)) > GatherRadius)
                    continue;
                if (map.CanStep(x, y, nx, ny) && visited.Add((nx, ny)))
                    queue.Enqueue((nx, ny));
            }
        }
        return spots;
    }
}
