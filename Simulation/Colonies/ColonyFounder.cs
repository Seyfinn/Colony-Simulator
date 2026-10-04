using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Choisit un bon emplacement et y installe une nouvelle colonie.</summary>
public static class ColonyFounder
{
    /// <summary>Provisions de départ par colon : de quoi tenir quelques jours, le temps d'organiser la cueillette.</summary>
    private const int StartingFoodPerColonist = 4;
    private const int GatherRadius = 5;

    /// <summary>La dotation de départ de chaque peuple en monnaie commune.</summary>
    public const int StartingCoins = 400;

    public const int MinStartingColonists = 5;
    public const int MaxStartingColonists = 10;

    /// <summary>Limite des fondateurs choisis par le joueur.</summary>
    public const int MaxPlayerFounders = 20;

    public static Colony Found(LocalMap map, Random random, string name, int colonistCount, Func<int> nextId, GameClock clock, Species? species = null)
    {
        species ??= Species.Human;
        (int campX, int campY) = FindCampSite(map);
        return Create(map, random, name, colonistCount, nextId, clock, species, campX, campY);
    }

    /// <summary>Installe les fondateurs à l'emplacement exact validé par le joueur.</summary>
    public static Colony FoundAt(LocalMap map, Random random, string name, int colonistCount,
        Func<int> nextId, GameClock clock, Species species, int campX, int campY)
    {
        if (!CanFoundAt(map, campX, campY, out string reason))
            throw new ArgumentException(reason);
        if (colonistCount < MinStartingColonists || colonistCount > MaxPlayerFounders)
            throw new ArgumentOutOfRangeException(nameof(colonistCount));
        return Create(map, random, name, colonistCount, nextId, clock, species, campX, campY);
    }

    private static Colony Create(LocalMap map, Random random, string name, int colonistCount,
        Func<int> nextId, GameClock clock, Species species, int campX, int campY)
    {

        // On dégage la place autour du feu.
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (map.InBounds(campX + dx, campY + dy))
                map.ClearFlora(campX + dx, campY + dy);

        var colony = new Colony(name, campX, campY, FindGatherSpots(map, campX, campY))
        {
            Quarry = WorkSites.FindQuarry(map, campX, campY),
        };
        colony.Clock = clock;
        colony.Species = species;
        colony.Map = map;
        colony.Pathfinder = new GodColony.Simulation.Pathfinding.Pathfinder(map);
        colony.Stock.Add(ResourceType.Food, StartingFoodPerColonist * colonistCount);
        colony.Stock.Add(ResourceType.Coins, StartingCoins);

        for (int i = 0; i < colonistCount; i++)
        {
            Sex sex = i % 2 == 0 ? Sex.Female : Sex.Male;
            (int x, int y) = colony.GatherSpots[random.Next(colony.GatherSpots.Count)];
            string colonistName = Names.Pick(sex, random, colony.Members.Select(m => m.Name));
            var colonist = new Colonist(nextId(), colonistName, sex, colony, Skills.Random(random, species), x + 0.5f, y + 0.5f)
            {
                Personality = Personality.Random(random, species),
                Species = species,
                // Tous des adultes, d'âges variés : la colonie ne vieillira pas d'un seul bloc.
                BirthTicks = clock.Ticks - (long)((Colonist.AdultAge + 6f * random.NextSingle()) * species.LifespanScale * TimeConstants.TicksPerYear),
                Surname = Names.PickSurname(random, colony.Members.Select(m => m.Surname)),
            };
            colonist.Needs.Food = 0.6f + 0.35f * random.NextSingle();
            colonist.Needs.Rest = 0.6f + 0.35f * random.NextSingle();
            colonist.Needs.Leisure = 0.6f + 0.35f * random.NextSingle();
            colonist.Needs.Social = 0.6f + 0.35f * random.NextSingle();
            colonist.Needs.Comfort = 0.5f;
            colony.Members.Add(colonist);
        }
        colony.AssignSectors();
        return colony;
    }

    /// <summary>Le feu et son voisinage doivent être plats, secs et accessibles ; rien n'est modifié ici.</summary>
    public static bool CanFoundAt(LocalMap map, int x, int y, out string reason)
    {
        const int margin = 8;
        if (x < margin || y < margin || x >= map.Width - margin || y >= map.Height - margin)
            reason = "Choisissez un emplacement à au moins 8 cases du bord.";
        else if (map.IsWater(x, y) || map.IsWaterway(x, y))
            reason = "Le camp doit être installé sur la terre ferme.";
        else if (!IsFlatClearing(map, x, y))
            reason = "Le camp nécessite une zone plate et sèche de 5 × 5 cases, hors des montagnes.";
        else
        {
            reason = "Terrain constructible. Privilégiez l'eau, les baies, les arbres et la roche à proximité.";
            return true;
        }
        return false;
    }

    /// <summary>Nombre d'arbres (de toute taille) qu'il faut à moins de 25 cases du camp pour que le bois ne manque pas.</summary>
    private const int MinTreesNearCamp = 90;

    /// <summary>Jusqu'où (en cases) on mesure la distance à la roche, aux arbres ou à l'eau : au-delà, c'est « trop loin ».</summary>
    private const int FarAway = 40;

    /// <summary>
    /// Un bon site est une plaine plate, proche de l'eau, des baies, de la forêt et d'une montagne,
    /// sans être collée au bord de la carte. Les zones de la carte étant vastes, on exige surtout de quoi vivre à portée de marche :
    /// de la roche pour la carrière, des arbres pour le bois (deux besoins qu'une colonie ne comble pas autrement).
    /// </summary>
    public static (int X, int Y) FindCampSite(LocalMap map)
    {
        (int, int) best = (map.Width / 2, map.Height / 2);
        float bestScore = float.MinValue;
        const int margin = 12;

        int[] rock = DistanceTo(map, (x, y) => map.CanMine(x, y));
        int[] water = DistanceTo(map, (x, y) => map.IsWater(x, y));
        int[] river = DistanceTo(map, (x, y) => map.IsRiver(x, y));
        int[] trees = Counts(map, FloraType.Tree), bushes = Counts(map, FloraType.Bush);

        for (int y = margin; y < map.Height - margin; y += 2)
        for (int x = margin; x < map.Width - margin; x += 2)
        {
            if (!IsFlatClearing(map, x, y))
                continue;

            int i = y * map.Width + x;
            // Des baies à portée pour les premiers jours, quelques arbres tout près (sans s'enfoncer dans la forêt).
            float score = Math.Min(3f * CountIn(map, bushes, x, y, 15), 45f);
            score += Math.Min(0.15f * CountIn(map, trees, x, y, 10), 25f);
            // Du bois à portée de hache : sinon la colonie ne peut ni bâtir ni se chauffer.
            if (CountIn(map, trees, x, y, 25) < MinTreesNearCamp)
                score -= 60f;
            // Du poisson à portée de pêche : sans eau, la cueillette seule épuise vite les alentours.
            if (Math.Min(water[i], river[i]) > 26)
                score -= 60f;
            if (water[i] <= 12) score += 20f;
            // Une rivière à portée : pêche, berges fertiles, et plus tard de quoi irriguer.
            if (river[i] <= 14) score += 25f;
            // De la roche à portée de marche : plus elle est proche, mieux c'est ; trop loin, pas de carrière.
            score += rock[i] <= 24 ? 30f - 1.25f * Math.Max(0, rock[i] - 8) : -80f;
            score -= 0.1f * (Math.Abs(x - map.Width / 2) + Math.Abs(y - map.Height / 2));

            if (score > bestScore)
            {
                bestScore = score;
                best = (x, y);
            }
        }
        return best;
    }

    /// <summary>Pour chaque case, la distance (en cases, à vol d'oiseau) à la case la plus proche vérifiant le critère, jusqu'à <see cref="FarAway"/>.</summary>
    private static int[] DistanceTo(LocalMap map, Func<int, int, bool> criterion)
    {
        int w = map.Width, h = map.Height;
        var distance = new int[w * h];
        Array.Fill(distance, FarAway);
        var queue = new Queue<int>();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (criterion(x, y))
            {
                distance[y * w + x] = 0;
                queue.Enqueue(y * w + x);
            }
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int next = distance[current] + 1;
            if (next >= FarAway)
                continue;
            int cx = current % w, cy = current / w;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || distance[ny * w + nx] <= next)
                    continue;
                distance[ny * w + nx] = next;
                queue.Enqueue(ny * w + nx);
            }
        }
        return distance;
    }

    /// <summary>Somme cumulée (image intégrale) des cases portant cette plante, pour en compter dans un carré en un instant.</summary>
    private static int[] Counts(LocalMap map, FloraType flora)
    {
        int w = map.Width + 1;
        var sums = new int[w * (map.Height + 1)];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            sums[(y + 1) * w + x + 1] = sums[y * w + x + 1] + sums[(y + 1) * w + x] - sums[y * w + x]
                + (map.GetFlora(x, y) == flora ? 1 : 0);
        return sums;
    }

    /// <summary>Nombre de cases comptées dans le carré de rayon <paramref name="radius"/> centré sur (x, y).</summary>
    private static int CountIn(LocalMap map, int[] sums, int x, int y, int radius)
    {
        int w = map.Width + 1;
        int x0 = Math.Max(0, x - radius), y0 = Math.Max(0, y - radius);
        int x1 = Math.Min(map.Width, x + radius + 1), y1 = Math.Min(map.Height, y + radius + 1);
        return sums[y1 * w + x1] - sums[y0 * w + x1] - sums[y1 * w + x0] + sums[y0 * w + x0];
    }

    private static bool IsFlatClearing(LocalMap map, int x, int y)
    {
        if (map.IsMountain(x, y) || !map.IsWalkable(x, y))
            return false;
        int elevation = map.GetElevation(x, y);
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            if (!map.IsWalkable(x + dx, y + dy) || map.IsWaterway(x + dx, y + dy) || map.GetElevation(x + dx, y + dy) != elevation)
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
