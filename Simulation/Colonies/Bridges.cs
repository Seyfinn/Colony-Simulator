using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Un chantier de pont : les cases de rivière à franchir, et combien sont déjà bâties. Les matériaux sont pris au stock à l'ouverture du chantier.</summary>
public sealed class BridgeSite
{
    public int Id { get; internal set; }

    /// <summary>Numéros de case (y × largeur + x) de la rivière à enjamber.</summary>
    public List<int> Cells { get; internal set; } = [];

    public List<int> BuiltCells { get; internal set; } = [];

    /// <summary>Les deux cases de berge où le tablier s'ancre, et le sens du tablier (est-ouest ou nord-sud).</summary>
    public List<int> Landings { get; internal set; } = [];
    public bool Horizontal { get; internal set; } = true;
    public long CreatedTicks { get; internal set; }

    public bool IsComplete => BuiltCells.Count >= Cells.Count;
}

/// <summary>
/// Les gués et les ponts. Traverser une rivière à gué est lent (voir <see cref="LocalMap.MoveCost"/>) ; quand les habitants y passent souvent, la colonie bâtit un pont
/// (bois 8 et pierre 6 par case de rivière, par le travail de construction) et ces cases deviennent <see cref="RoadSurface.Bridge"/> : le franchissement n'est plus ralenti.
/// Les passages sur la rivière sont comptés comme ceux des sentiers, avec la même usure.
/// </summary>
public static class Bridges
{
    /// <summary>Passages équivalents sur une case de rivière au-delà desquels un pont mérite d'être bâti.</summary>
    public const int MinPasses = 6;

    public const int WoodPerCell = 8, StonePerCell = 6;

    /// <summary>Un pont enjambe au plus ce nombre de cases d'un seul chantier.</summary>
    public const int MaxCells = 6;

    /// <summary>Secondes de travail d'une case de pont : bien plus qu'une case de chemin de terre.</summary>
    private const float SecondsPerCell = 8f;

    private const int MaxWorkers = 2;

    public static BridgeSite? ActiveSite(Colony colony) => colony.LocalSettlement.BridgeSites.FirstOrDefault(s => !s.IsComplete);

    /// <summary>Un passage vient d'être achevé sur une case de rivière : il compte comme sur un sentier (l'usure vient de <see cref="RoadDevelopment"/>).</summary>
    internal static void OnRiverStep(Colony colony, LocalMap map, int x, int y)
    {
        RoadLayer roads = map.Roads;
        int cell = y * map.Width + x;
        int today = (int)colony.Clock.TotalDays;
        roads.SetWear(cell, Math.Min(RoadLayer.MaxWear, RoadDevelopment.Decayed(roads.RawWear(cell), roads.WearDay(cell), today) + RoadLayer.Pass), today);
        roads.TouchedToday.Add(cell);
    }

    /// <summary>
    /// Au début du jour (avant que les cases touchées ne soient oubliées) : si aucun pont n'est en chantier, que la colonie n'est pas en crise et que les matériaux sont en surplus,
    /// la case de rivière la plus fréquentée devient un chantier de pont, avec les cases voisines que l'on traverse aussi.
    /// </summary>
    internal static void OnDayStart(Colony colony)
    {
        LocalMap map = colony.Map;
        RoadLayer roads = map.Roads;
        if (roads.TouchedToday.Count == 0 || ActiveSite(colony) is not null || !RoadWorks.IsAllowed(colony) || colony.PresentMembers.Count < 6)
            return;
        int today = (int)colony.Clock.TotalDays;
        int seed = -1, seedWear = 0;
        foreach (int cell in roads.TouchedToday.Where(c => map.IsRiver(c % map.Width, c / map.Width) && roads.SurfaceAt(c) != RoadSurface.Bridge).Order())
        {
            int wear = RoadDevelopment.WearAt(map, cell, today);
            if (wear >= MinPasses * RoadLayer.Pass && wear > seedWear)
                (seed, seedWear) = (cell, wear);
        }
        if (seed < 0)
            return;

        // Un seul ouvrage droit d'une berge à l'autre, jamais près d'un pont existant (on l'emprunte plutôt).
        int woodRoom = colony.Stock.Available(ResourceType.Wood) - (int)ColonyBrain.HeatingTarget(colony, colony.Clock.Season) - 10;
        int stoneRoom = colony.Stock.Available(ResourceType.Stone);
        int affordable = Math.Min(MaxCells, Math.Min(woodRoom / WoodPerCell, stoneRoom / StonePerCell));
        if (affordable < 1 || Crossing(map, seed, affordable) is not { } crossing || NearBridge(map, crossing.Cells))
            return;
        List<int> cells = crossing.Cells;
        if (!colony.Stock.TryTake(ResourceType.Wood, WoodPerCell * cells.Count))
            return;
        if (!colony.Stock.TryTake(ResourceType.Stone, StonePerCell * cells.Count))
        {
            colony.Stock.Add(ResourceType.Wood, WoodPerCell * cells.Count, ResourceFlow.Transfer);
            return;
        }
        Settlement place = colony.LocalSettlement;
        place.BridgeSites.Add(new BridgeSite { Id = place.BridgeSites.Count + 1, Cells = cells, Landings = crossing.Landings, Horizontal = crossing.Horizontal, CreatedTicks = colony.Clock.Ticks });
        ColonyBrain.Say(colony, colony.Clock, $"On passe si souvent la rivière à gué que la colonie décide d'y bâtir un pont ({cells.Count} case{(cells.Count > 1 ? "s" : "")}, "
            + $"{WoodPerCell * cells.Count} bois et {StonePerCell * cells.Count} pierre).");
    }

    /// <summary>Distance (en cases) en deçà de laquelle un pont existant dispense d'en bâtir un autre.</summary>
    private const int SpacingCells = 8;

    /// <summary>
    /// Le plus court franchissement droit (est-ouest ou nord-sud) par la case de rivière donnée : toutes ses cases sont de la rivière, les deux extrémités sont des berges
    /// praticables. Null si la rivière est plus large que <paramref name="maxCells"/> ou si une rive n'est pas praticable.
    /// </summary>
    internal static (List<int> Cells, List<int> Landings, bool Horizontal)? Crossing(LocalMap map, int seed, int maxCells)
    {
        (List<int>, List<int>, bool)? best = null;
        foreach (bool horizontal in new[] { true, false })
        {
            (int dx, int dy) = horizontal ? (1, 0) : (0, 1);
            int x0 = seed % map.Width, y0 = seed / map.Width;
            var cells = new List<int>();
            int x = x0, y = y0;
            while (map.InBounds(x, y) && map.IsRiver(x, y) && cells.Count <= maxCells) { x -= dx; y -= dy; }
            (int bx, int by) = (x, y);
            x += dx; y += dy;
            while (map.InBounds(x, y) && map.IsRiver(x, y) && cells.Count <= maxCells) { cells.Add(y * map.Width + x); x += dx; y += dy; }
            if (cells.Count == 0 || cells.Count > maxCells || !Bank(map, bx, by) || !Bank(map, x, y)
                || cells.Any(c => map.Roads.SurfaceAt(c) == RoadSurface.Bridge) || (best is { } b && b.Item1.Count <= cells.Count))
                continue;
            best = (cells, [by * map.Width + bx, y * map.Width + x], horizontal);
        }
        return best;
    }

    private static bool Bank(LocalMap map, int x, int y) => map.InBounds(x, y) && map.IsWalkable(x, y) && !map.IsRiver(x, y);

    private static bool NearBridge(LocalMap map, List<int> cells)
    {
        foreach (int cell in cells)
            for (int dy = -SpacingCells; dy <= SpacingCells; dy++)
            for (int dx = -SpacingCells; dx <= SpacingCells; dx++)
            {
                int x = cell % map.Width + dx, y = cell / map.Width + dy;
                if (map.InBounds(x, y) && map.Roads.SurfaceAt(y * map.Width + x) == RoadSurface.Bridge)
                    return true;
            }
        return false;
    }

    /// <summary>
    /// La prochaine case de pont à bâtir offerte à un colon, ou null : jamais en crise, jamais plus de deux bras à la fois, une case que personne d'autre n'a réservée.
    /// </summary>
    internal static Activity? NextActivity(Colonist colonist, float workSpeed)
    {
        Colony colony = colonist.Colony;
        if (colonist.Stage == LifeStage.Child || !RoadWorks.IsAllowed(colony) || ActiveSite(colony) is not { } site
            || colony.PresentMembers.Count(m => m.Activity is { Kind: ActivityKind.BuildRoad, SegmentId: < 0 }) >= MaxWorkers)
            return null;
        LocalMap map = colony.Map;
        foreach (int cell in site.Cells.Where(c => !site.BuiltCells.Contains(c)))
        {
            int x = cell % map.Width, y = cell / map.Width;
            if (colony.Reserved.Contains((x, y)))
                continue;
            return new Activity(ActivityKind.BuildRoad, x, y, SecondsPerCell / workSpeed * TimeConstants.TicksPerSecond) { SegmentId = -site.Id };
        }
        return null;
    }

    /// <summary>La case est-elle toujours à bâtir (le chantier existe, la case n'est pas encore un pont) ?</summary>
    internal static bool CanBegin(Colony colony, Activity activity) =>
        colony.LocalSettlement.BridgeSites.FirstOrDefault(s => s.Id == -activity.SegmentId) is { } site
        && site.Cells.Contains(activity.TargetY * colony.Map.Width + activity.TargetX) && !site.BuiltCells.Contains(activity.TargetY * colony.Map.Width + activity.TargetX)
        && RoadWorks.IsAllowed(colony);

    /// <summary>Une case de pont est achevée : elle ne ralentit plus le franchissement. Quand toutes le sont, le pont est inauguré.</summary>
    internal static void CompleteCell(Colony colony, int x, int y, int siteId)
    {
        LocalMap map = colony.Map;
        int cell = y * map.Width + x;
        map.Roads.SetWork(cell, RoadLayer.WorkDone);
        map.Roads.SetSurface(cell, RoadSurface.Bridge);
        map.Roads.SetWear(cell, 0, (int)colony.Clock.TotalDays);
        map.NotifyRoadChanged(x, y);
        SettlementPlanner.Notify(colony, RetryEvents.Road);
        if (colony.LocalSettlement.BridgeSites.FirstOrDefault(s => s.Id == siteId) is not { } site)
            return;
        if (!site.BuiltCells.Contains(cell))
            site.BuiltCells.Add(cell);
        if (site.IsComplete)
        {
            // Les deux têtes de pont rejoignent les chemins : un sentier part de chaque berge.
            foreach (int landing in site.Landings.Where(l => map.Roads.SurfaceAt(l) == RoadSurface.None))
            {
                map.Roads.SetSurface(landing, RoadSurface.Trail);
                map.NotifyRoadChanged(landing % map.Width, landing / map.Width);
            }
            ColonyBrain.Say(colony, colony.Clock, "Le pont est achevé : on traverse la rivière sans se mouiller, et sans perdre de temps.");
        }
    }
}
