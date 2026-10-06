using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Nature;

/// <summary>
/// La faune sauvage. Chaque région porte une population par espèce (<see cref="RegionWildlife"/>) qui suit une croissance logistique
/// bornée par la capacité d'accueil du biome, réduite par la présence humaine et par l'hiver. Autour d'un établissement actif, une partie
/// de cette population prend la forme de hardes visibles qui <b>empruntent</b> leurs bêtes à la région et les lui rendent en disparaissant.
/// Tout le hasard vient de <see cref="WorldState.Nature"/>.
/// </summary>
public static class Wildlife
{
    /// <summary>Hardes visibles au plus, par établissement actif.</summary>
    public const int MaxHerds = 8;

    /// <summary>Hardes de prédateurs visibles au plus (alpha non compris) : elles prennent deux des huit places.</summary>
    public const int MaxPredatorHerds = 2;

    private const int SpawnsPerDay = 2;
    private const float PredationRate = 0.02f;
    private const float AlphaChancePerDay = 0.002f;
    private const float HungerPerHour = 0.04f;
    private const float HungryAt = 0.5f;

    /// <summary>La population sauvage de la région d'un établissement.</summary>
    public static RegionWildlife Of(WorldState world, Settlement place) => world.Regions[place.RegionTileIndex].Wildlife;

    public static WildHerd? HerdById(Settlement place, int id) => id == 0 ? null : place.Herds.FirstOrDefault(h => h.Id == id);

    // --- Capacité et croissance ---

    /// <summary>Bêtes que la région peut nourrir : la capacité du biome, moins la gêne de l'homme (jusqu'à 60 %), moins la rigueur de l'hiver.</summary>
    public static float Cap(Biome biome, WildSpecies species, Season season, float disturbance)
    {
        float winter = season == Season.Hiver ? 1f - 0.1f - 0.35f * MathF.Min(1f, Climate.ColdSeverity(biome)) : 1f;
        return WildSpeciesInfo.BaseCap(biome, species) * (1f - 0.6f * disturbance) * winter;
    }

    private static float PreyRatio(RegionWildlife wild, Biome biome, Season season, Func<WildSpecies, int> borrowed)
    {
        float cap = 0f, prey = 0f;
        foreach (WildSpecies species in WildSpeciesInfo.All.Where(s => !WildSpeciesInfo.IsPredator(s)))
        {
            cap += Cap(biome, species, season, wild.Disturbance);
            prey += wild.PopulationOf(species) + borrowed(species);
        }
        return cap <= 0f ? 1f : prey / cap;
    }

    /// <summary>
    /// Un jour de croissance d'une région : logistique vers la capacité (les prédateurs se règlent sur la quantité de gibier), moins la prédation.
    /// Les bêtes des hardes comptent dans la population mais restent à leur harde : seule la variation touche la population libre.
    /// </summary>
    internal static void GrowRegion(WorldState world, RegionState region, Func<WildSpecies, int> borrowed)
    {
        RegionWildlife wild = region.Wildlife;
        Biome biome = region.Map.Biome;
        Season season = world.Clock.Season;
        float preyRatio = Math.Clamp(PreyRatio(wild, biome, season, borrowed), 0.3f, 1f);
        float predators = WildSpeciesInfo.All.Where(WildSpeciesInfo.IsPredator).Sum(s => wild.PopulationOf(s) + borrowed(s));
        foreach (WildSpecies species in WildSpeciesInfo.All)
        {
            bool predator = WildSpeciesInfo.IsPredator(species);
            float cap = Cap(biome, species, season, wild.Disturbance) * (predator ? preyRatio : 1f);
            int free = wild.PopulationOf(species);
            float total = free + borrowed(species);
            float delta;
            if (cap < 0.5f)
                delta = -0.05f * total;
            else if (total < 2f)
                delta = 0.02f; // quelques bêtes viennent des régions voisines
            else
                delta = WildSpeciesInfo.GrowthRate(species) * total * (1f - total / cap);
            if (!predator && cap >= 0.5f)
                delta -= PredationRate * predators * total / cap;
            float pending = wild.Pending.GetValueOrDefault(species) + delta;
            int whole = (int)MathF.Truncate(pending);
            wild.Pending[species] = pending - whole;
            wild.Population[species] = Math.Max(0, free + whole);
        }
    }

    // --- Chaque jour, autour d'un établissement actif ---

    /// <summary>La gêne, la croissance de la région, puis les hardes : les vides disparaissent, de nouvelles apparaissent loin du camp, un alpha peut surgir.</summary>
    public static void DailySettlement(WorldState world, Settlement place)
    {
        if (!world.Regions.TryGetValue(place.RegionTileIndex, out RegionState? region))
            return;
        RegionWildlife wild = region.Wildlife;
        place.Wildlife = wild;
        LocalMap map = region.Map;
        Season season = world.Clock.Season;
        wild.Disturbance = Math.Clamp(ClearedShare(place, map) + 0.05f * place.PresentColonists.Count(), 0f, 1f);
        place.Herds.RemoveAll(h => h.Count <= 0);
        GrowRegion(world, region, species => place.Herds.Where(h => h.Species == species).Sum(h => h.Count));

        // Les petits naissent au printemps et grandissent à l'automne.
        foreach (WildHerd herd in place.Herds)
            herd.Young = herd.IsPredator || herd.Count < 2 ? 0 : season switch
            {
                Season.Printemps => Math.Max(1, herd.Count / 3),
                Season.Ete => Math.Max(1, herd.Count / 4),
                _ => 0,
            };

        float radius = SpawnRadius(wild.Disturbance);
        for (int spawned = 0; spawned < SpawnsPerDay; spawned++)
        {
            // Deux places de la harde visible sont réservées aux prédateurs : sans elles, le gibier remplirait tout et le danger resterait invisible.
            bool predatorSlot = place.Herds.Count(h => h.IsPredator && !h.IsAlpha) < MaxPredatorHerds && wild.Predators >= 1
                && (spawned == 0 || place.Herds.Count(h => !h.IsPredator) >= MaxHerds - MaxPredatorHerds);
            if (predatorSlot ? !TrySpawn(world, place, wild, map, radius, predators: true)
                : place.Herds.Count(h => !h.IsPredator) >= MaxHerds - MaxPredatorHerds || !TrySpawn(world, place, wild, map, radius, predators: false))
                break;
        }
        MaybeAlpha(world, place, wild, region);
    }

    /// <summary>La faune recule avec le village : de 12 cases du camp en pleine nature à 42 quand la région est très habitée.</summary>
    public static float SpawnRadius(float disturbance) => 12f + 30f * disturbance;

    private static float ClearedShare(Settlement place, LocalMap map)
    {
        float area = place.Fields.Sum(f => f.Size * f.Size) + place.Buildings.Sum(b => b.Width * b.Height);
        return Math.Min(1f, 3f * area / (map.Width * map.Height));
    }

    internal static bool TrySpawn(WorldState world, Settlement place, RegionWildlife wild, LocalMap map, float radius, bool predators)
    {
        // Le tirage pondéré par la population libre, parmi les proies ou parmi les prédateurs.
        var weights = WildSpeciesInfo.All
            .Where(s => WildSpeciesInfo.IsPredator(s) == predators)
            .Select(s => (Species: s, Weight: (float)wild.PopulationOf(s)))
            .Where(p => wild.PopulationOf(p.Species) >= 1).ToList();
        float sum = weights.Sum(p => p.Weight);
        if (sum <= 0f)
            return false;
        float pick = world.Nature.NextSingle() * sum;
        WildSpecies chosen = weights[^1].Species;
        foreach ((WildSpecies species, float weight) in weights)
        {
            if (pick < weight) { chosen = species; break; }
            pick -= weight;
        }

        (int x, int y)? cell = SpawnCell(world, place, map, radius);
        if (cell is not { } at)
            return false;
        int count = Math.Min(wild.PopulationOf(chosen), WildSpeciesInfo.HerdSize(chosen));
        wild.Population[chosen] -= count;
        var herd = new WildHerd
        {
            Id = ++place.NextHerdId, Species = chosen, Count = count,
            X = at.x + 0.5f, Y = at.y + 0.5f, PrevX = at.x + 0.5f, PrevY = at.y + 0.5f,
            TargetX = at.x + 0.5f, TargetY = at.y + 0.5f,
            State = WildSpeciesInfo.IsPredator(chosen) ? HerdState.Roaming : HerdState.Grazing,
            Hunger = WildSpeciesInfo.IsPredator(chosen) ? 0.3f : 0f,
        };
        place.Herds.Add(herd);
        return true;
    }

    /// <summary>Une case praticable à plus de <paramref name="radius"/> du camp : parmi vingt-quatre essais, la plus proche de ce rayon (la faune se tient juste au-delà du village).</summary>
    private static (int X, int Y)? SpawnCell(WorldState world, Settlement place, LocalMap map, float radius)
    {
        (int X, int Y)? best = null, closest = null;
        float bestDistance = float.MaxValue, farthest = 0f;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int x = world.Nature.Next(map.Width), y = world.Nature.Next(map.Height);
            if (!map.IsWalkable(x, y) || map.IsRiver(x, y))
                continue;
            float distance = MathF.Sqrt((x - place.CampX) * (x - place.CampX) + (y - place.CampY) * (y - place.CampY));
            if (distance >= radius && distance < bestDistance) { bestDistance = distance; best = (x, y); }
            if (distance > farthest) { farthest = distance; closest = (x, y); }
        }
        return best ?? (farthest >= 8f ? closest : null);
    }

    /// <summary>Quand les prédateurs sont nombreux, un alpha peut surgir : un grand ours ou un loup meneur, au plus un par région et par an.</summary>
    private static void MaybeAlpha(WorldState world, Settlement place, RegionWildlife wild, RegionState region)
    {
        if (place.Herds.Any(h => h.IsAlpha && h.Count > 0) || world.Clock.Ticks - wild.LastAlphaTicks < TimeConstants.TicksPerYear)
            return;
        Biome biome = region.Map.Biome;
        float cap = WildSpeciesInfo.All.Where(WildSpeciesInfo.IsPredator).Sum(s => Cap(biome, s, world.Clock.Season, wild.Disturbance));
        // Les prédateurs comptent avec ceux des hardes visibles, qui empruntent leurs bêtes à la région.
        float predators = wild.Predators + place.Herds.Where(h => h.IsPredator).Sum(h => h.Count);
        if (cap < 1f || predators < 0.6f * cap || world.Nature.NextSingle() >= AlphaChancePerDay)
            return;
        WildSpecies species = wild.PopulationOf(WildSpecies.Bear) >= 1 && (wild.PopulationOf(WildSpecies.Wolf) < 1 || world.Nature.NextSingle() < 0.5f)
            ? WildSpecies.Bear : WildSpecies.Wolf;
        if (wild.PopulationOf(species) < 1)
            return;
        (int x, int y)? cell = SpawnCell(world, place, region.Map, SpawnRadius(wild.Disturbance) * 0.7f);
        if (cell is not { } at)
            return;
        int count = species == WildSpecies.Bear ? 1 : Math.Min(wild.PopulationOf(species), 3);
        wild.Population[species] -= count;
        wild.LastAlphaTicks = world.Clock.Ticks;
        var alpha = new WildHerd
        {
            Id = ++place.NextHerdId, Species = species, Count = count, IsAlpha = true,
            AlphaName = WildSpeciesInfo.AlphaName(species, (int)world.Clock.TotalDays + place.Id),
            X = at.x + 0.5f, Y = at.y + 0.5f, PrevX = at.x + 0.5f, PrevY = at.y + 0.5f, TargetX = at.x + 0.5f, TargetY = at.y + 0.5f,
            State = HerdState.Roaming, Hunger = 0.6f,
        };
        place.Herds.Add(alpha);
        ColonyBrain.Say(place.Owner, world.Clock, species == WildSpecies.Bear
            ? $"Un ours immense, « {alpha.AlphaName} », rôde dans la région : on a retrouvé ses traces près des enclos."
            : $"Un loup meneur, « {alpha.AlphaName} », guide une meute affamée dans la région : les bergers ont peur.");
    }

    // --- Les régions du monde (une fois par jour) ---

    /// <summary>
    /// Les régions sans établissement actif : la gêne retombe de 2 % par jour, la population croît, les hardes qu'un établissement fermé
    /// laisse derrière lui rendent leurs bêtes. À l'entrée de l'hiver, un cinquième du gibier des régions froides part vers une voisine plus douce ;
    /// il revient au printemps.
    /// </summary>
    public static void WorldDaily(WorldState world)
    {
        var inhabited = world.Settlements.Where(s => s.Status == SettlementStatus.Active).Select(s => s.RegionTileIndex).ToHashSet();
        foreach (RegionState region in world.Regions.Values.OrderBy(r => r.TileIndex))
        {
            RegionWildlife wild = region.Wildlife;
            if (inhabited.Contains(region.TileIndex))
                continue;
            foreach (Settlement closed in world.Settlements.Where(s => s.RegionTileIndex == region.TileIndex))
                ReturnHerds(closed, wild);
            wild.Disturbance *= 0.98f;
            GrowRegion(world, region, _ => 0);
        }
        if (world.Clock.DayOfSeason != 1)
            return;
        if (world.Clock.Season == Season.Hiver)
            Migrate(world);
        else if (world.Clock.Season == Season.Printemps)
            Return(world);
    }

    private static void ReturnHerds(Settlement place, RegionWildlife wild)
    {
        foreach (WildHerd herd in place.Herds.Where(h => h.Count > 0))
            wild.Population[herd.Species] = wild.PopulationOf(herd.Species) + herd.Count;
        place.Herds.Clear();
    }

    private static void Migrate(WorldState world)
    {
        foreach (RegionState region in world.Regions.Values.OrderBy(r => r.TileIndex))
        {
            if (Climate.ColdSeverity(region.Map.Biome) < 0.8f)
                continue;
            RegionState? mild = world.WorldMap.Grid.Neighbors(region.TileIndex).Order()
                .Select(t => world.Regions.GetValueOrDefault(t))
                .Where(r => r is not null && Climate.ColdSeverity(r.Map.Biome) < Climate.ColdSeverity(region.Map.Biome))
                .OrderBy(r => Climate.ColdSeverity(r!.Map.Biome)).FirstOrDefault();
            foreach (WildSpecies species in WildSpeciesInfo.All.Where(s => !WildSpeciesInfo.IsPredator(s)))
            {
                int leaving = region.Wildlife.PopulationOf(species) / 5;
                if (leaving <= 0)
                    continue;
                region.Wildlife.Population[species] -= leaving;
                region.Wildlife.Away[species] = region.Wildlife.Away.GetValueOrDefault(species) + leaving;
                if (mild is null)
                    continue;
                mild.Wildlife.Population[species] = mild.Wildlife.PopulationOf(species) + leaving;
                mild.Wildlife.Guests[species] = mild.Wildlife.Guests.GetValueOrDefault(species) + leaving;
            }
        }
    }

    private static void Return(WorldState world)
    {
        foreach (RegionState region in world.Regions.Values.OrderBy(r => r.TileIndex))
        {
            RegionWildlife wild = region.Wildlife;
            foreach ((WildSpecies species, int guests) in wild.Guests.ToList())
                wild.Population[species] = Math.Max(0, wild.PopulationOf(species) - guests);
            wild.Guests.Clear();
            foreach ((WildSpecies species, int away) in wild.Away.ToList())
                wild.Population[species] = wild.PopulationOf(species) + away;
            wild.Away.Clear();
        }
    }

    // --- Chaque heure : les hardes bougent ---

    /// <summary>
    /// Les hardes bougent de quelques cases par heure, par une marche gloutonne (sans recherche de chemin) : les proies broutent et fuient les colons
    /// proches, les prédateurs rôdent ou, affamés, traquent la cible la plus proche (harde de proies, enclos, colon isolé).
    /// </summary>
    public static void HourlyMove(WorldState world, Settlement place)
    {
        if (place.Herds.Count == 0)
            return;
        LocalMap map = place.Map;
        RegionWildlife wild = Of(world, place);
        float keepOut = SpawnRadius(wild.Disturbance) * 0.5f;
        List<Colonist> colonists = place.PresentColonists.ToList();
        foreach (WildHerd herd in place.Herds.OrderBy(h => h.Id))
        {
            herd.PrevX = herd.X;
            herd.PrevY = herd.Y;
            int speed = WildSpeciesInfo.Speed(herd.Species);
            if (herd.IsPredator)
                MovePredator(world, place, map, herd, colonists, speed);
            else
                MovePrey(world, place, map, herd, colonists, speed, keepOut);
        }
    }

    private static void MovePrey(WorldState world, Settlement place, LocalMap map, WildHerd herd, List<Colonist> colonists, int speed, float keepOut)
    {
        Colonist? threat = colonists.Where(c => Distance(c.X, c.Y, herd.X, herd.Y) < 6f).OrderBy(c => Distance(c.X, c.Y, herd.X, herd.Y)).FirstOrDefault();
        if (threat is not null)
        {
            herd.State = HerdState.Fleeing;
            Step(map, herd, herd.X + (herd.X - threat.X) * 3f, herd.Y + (herd.Y - threat.Y) * 3f, speed * 2);
            return;
        }
        if (herd.State == HerdState.Fleeing)
            herd.State = HerdState.Grazing;
        if (Distance(place.CampX, place.CampY, herd.X, herd.Y) < keepOut)
        {
            Step(map, herd, herd.X + (herd.X - place.CampX) * 3f, herd.Y + (herd.Y - place.CampY) * 3f, speed);
            return;
        }
        if (world.Nature.NextSingle() < 0.5f)
        {
            herd.TargetX = Math.Clamp(herd.X + world.Nature.Next(-6, 7), 1, map.Width - 2);
            herd.TargetY = Math.Clamp(herd.Y + world.Nature.Next(-6, 7), 1, map.Height - 2);
            Step(map, herd, herd.TargetX, herd.TargetY, speed);
        }
    }

    private static void MovePredator(WorldState world, Settlement place, LocalMap map, WildHerd herd, List<Colonist> colonists, int speed)
    {
        herd.Hunger = MathF.Min(1f, herd.Hunger + HungerPerHour);
        if (herd.Hunger <= HungryAt)
        {
            herd.State = HerdState.Roaming;
            if (world.Nature.NextSingle() < 0.5f)
            {
                herd.TargetX = Math.Clamp(herd.X + world.Nature.Next(-8, 9), 1, map.Width - 2);
                herd.TargetY = Math.Clamp(herd.Y + world.Nature.Next(-8, 9), 1, map.Height - 2);
                Step(map, herd, herd.TargetX, herd.TargetY, speed);
            }
            return;
        }

        // Affamé : la cible la plus proche (une harde de proies d'abord, puis un enclos, puis un colon isolé loin du camp).
        (float X, float Y, WildHerd? Prey, float Cost)? target = null;
        foreach (WildHerd prey in place.Herds.Where(h => !h.IsPredator && h.Count > 0))
        {
            float d = Distance(prey.X, prey.Y, herd.X, herd.Y);
            if (target is null || d < target.Value.Cost) target = (prey.X, prey.Y, prey, d);
        }
        foreach (Building pen in place.Buildings.Where(b => b.Type == BuildingType.Pen && b.IsComplete))
        {
            float d = Distance(pen.X, pen.Y, herd.X, herd.Y) * 1.2f;
            if (target is null || d < target.Value.Cost) target = (pen.X, pen.Y, null, d);
        }
        foreach (Colonist lone in colonists.Where(c => IsIsolated(place, c, colonists)))
        {
            float d = Distance(lone.X, lone.Y, herd.X, herd.Y) * 1.3f;
            if (target is null || d < target.Value.Cost) target = (lone.X, lone.Y, null, d);
        }
        if (target is not { } chosen)
        {
            herd.State = HerdState.Roaming;
            return;
        }
        herd.State = HerdState.Stalking;
        herd.TargetX = chosen.X;
        herd.TargetY = chosen.Y;
        Step(map, herd, chosen.X, chosen.Y, speed);
        if (chosen.Prey is { } victim && Distance(victim.X, victim.Y, herd.X, herd.Y) <= 1.5f)
        {
            victim.Count--;
            victim.Young = Math.Min(victim.Young, victim.Count);
            victim.State = HerdState.Fleeing;
            herd.Hunger = 0f;
            herd.State = HerdState.Roaming;
        }
    }

    /// <summary>Un colon à plus de huit cases du camp, seul à moins de quatre cases à la ronde.</summary>
    internal static bool IsIsolated(Settlement place, Colonist colonist, IReadOnlyList<Colonist> colonists) =>
        Distance(colonist.X, colonist.Y, place.CampX, place.CampY) > 8f
        && !colonists.Any(o => o != colonist && Distance(o.X, o.Y, colonist.X, colonist.Y) <= 4f);

    internal static float Distance(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>Marche gloutonne : à chaque pas, la case voisine praticable la plus proche de la cible, si elle rapproche.</summary>
    private static void Step(LocalMap map, WildHerd herd, float targetX, float targetY, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            int cx = herd.TileX, cy = herd.TileY;
            float current = (cx + 0.5f - targetX) * (cx + 0.5f - targetX) + (cy + 0.5f - targetY) * (cy + 0.5f - targetY);
            int bestX = cx, bestY = cy;
            float best = current;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((dx == 0 && dy == 0) || !map.IsWalkable(nx, ny) || !map.CanStep(cx, cy, nx, ny, 2))
                    continue;
                float d = (nx + 0.5f - targetX) * (nx + 0.5f - targetX) + (ny + 0.5f - targetY) * (ny + 0.5f - targetY);
                if (d < best) { best = d; bestX = nx; bestY = ny; }
            }
            if (bestX == cx && bestY == cy)
                return;
            herd.X = bestX + 0.5f;
            herd.Y = bestY + 0.5f;
        }
    }

    // --- Lectures pour le cerveau et l'affichage ---

    /// <summary>Le gibier disponible, de 0 à 100 : la population libre et les hardes de proies, rapportées à soixante bêtes.</summary>
    public static float GameAbundance(Settlement place) =>
        place.Wildlife is not { } wild ? 100f
        : Math.Min(100f, (wild.Prey + place.Herds.Where(h => !h.IsPredator).Sum(h => h.Count)) * 100f / 60f);

    public static bool HasAlpha(Settlement place) => place.Herds.Any(h => h.IsAlpha && h.Count > 0);
}
