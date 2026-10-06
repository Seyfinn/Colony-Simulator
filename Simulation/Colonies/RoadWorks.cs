using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'aménagement facultatif des chemins : un axe très fréquenté devient un chemin de terre, cellule par cellule, par quelques bras volontaires. Rien ici n'est
/// indispensable : la construction d'une hutte ne dépend jamais de l'aménagement complet de son chemin, un seul aménagement est actif à la fois, la main-d'œuvre
/// plafonne à 10 % des travailleurs (arrondi inférieur), et rien ne s'ouvre ni ne s'affecte en crise. Un arbre à abattre donne du bois par l'activité d'abattage
/// ordinaire ; un buisson s'arrache par un travail explicite (<see cref="ActivityKind.ClearAccess"/>), jamais par la simple usure.
/// </summary>
public static class RoadWorks
{
    /// <summary>Un axe doit voir passer au moins ce nombre de passages équivalents par cellule (en moyenne) pour mériter d'être aménagé.</summary>
    private const int UsefulPasses = 6;

    /// <summary>Un axe très court n'est pas un chemin : trois cellules au moins.</summary>
    private const int MinCells = 3;

    /// <summary>Nourriture minimale (en jours) pour ouvrir ou poursuivre un aménagement : on n'améliore pas un chemin quand on a faim.</summary>
    private const float MinFoodDays = SettlementRules.ComfortFoodDays;

    public static bool IsAllowed(Colony colony) =>
        colony.Sensors is { SurvivalAssured: true } sensors && sensors.FoodDays >= MinFoodDays && !colony.PresentMembers.Any(m => m.Needs.Food < 0.15f);

    /// <summary>Un seul aménagement facultatif actif à la fois.</summary>
    public static DevelopmentProject? ActiveProject(Colony colony) =>
        colony.Layout.Projects.FirstOrDefault(p => p.Kind is DevelopmentKind.RoadImprovement or DevelopmentKind.RoadShortcut && p.State is ProjectState.Accepted or ProjectState.Working);

    /// <summary>
    /// Chaque jour, une demande d'amélioration bornée : si la prospérité le permet et qu'aucun aménagement n'est en cours, l'axe le plus fréquenté (usure moyenne par
    /// cellule, à la date du jour) devient l'objet d'un projet. Les cellules de l'axe sont lues sans balayage de la grille.
    /// </summary>
    internal static void OnDayStart(Colony colony)
    {
        SettlementLayout layout = colony.Layout;
        if (ActiveProject(colony) is not null || !IsAllowed(colony) || colony.PresentMembers.Count < 6)
            return;
        LocalMap map = colony.Map;
        int today = (int)colony.Clock.TotalDays;

        RoadSegment? best = null;
        long bestScore = 0;
        foreach (RoadSegment segment in layout.RoadSegments)
        {
            if (segment.TargetSurface == RoadSurface.DirtRoad || segment.Cells.Count < MinCells || segment.ActiveProjectId >= 0)
                continue;
            long wear = 0;
            int walkable = 0;
            foreach (int cell in segment.Cells)
            {
                if (!Buildable(colony, map, cell))
                    continue;
                walkable++;
                wear += RoadDevelopment.WearAt(map, cell, today);
            }
            if (walkable < MinCells || wear / walkable < UsefulPasses * RoadLayer.Pass)
                continue;
            long score = wear;
            if (score > bestScore)
            {
                bestScore = score;
                best = segment;
            }
        }
        if (best is null)
            return;

        var project = new DevelopmentProject(layout.NextProjectId++, DevelopmentKind.RoadImprovement, DevelopmentPriority.Comfort, -1, colony.Clock.Ticks, "road");
        project.SegmentIds.Add(best.Id);
        layout.Projects.Add(project);
        best.ActiveProjectId = project.Id;
        best.TargetSurface = RoadSurface.DirtRoad;
    }

    /// <summary>La cellule peut recevoir un chemin de terre : terre nue ou arbres à abattre, hors des bâtiments, cultures, canaux et eau.</summary>
    private static bool Buildable(Colony colony, LocalMap map, int cell)
    {
        int x = cell % map.Width, y = cell / map.Width;
        if (!map.IsWalkable(x, y) || map.IsWaterway(x, y) || map.IsMountain(x, y))
            return false;
        return !colony.Spatial.Has(x, y, CellUse.Building | CellUse.Field | CellUse.Canal);
    }

    /// <summary>
    /// Combien de bras l'aménagement peut mobiliser : au plus 10 % des travailleurs, arrondi inférieur. Si le plafond vaut zéro, personne ne travaille aux routes
    /// sauf pendant un vrai temps libre (un seul colon sans affectation).
    /// </summary>
    public static int WorkerCap(Colony colony)
    {
        int workers = colony.Workers.Count();
        return (int)(workers * SettlementRules.RoadWorkerShare);
    }

    public static int ActiveWorkers(Colony colony) =>
        colony.PresentMembers.Count(m => m.Activity is { SegmentId: > 0 });

    /// <summary>
    /// La prochaine activité d'aménagement offerte à un colon, ou null : une cellule d'un lot de huit au plus, sur l'axe en cours, dans l'ordre de l'axe. Un arbre adulte
    /// s'abat, un buisson s'arrache, puis la cellule se travaille (le temps de la compétence de construction).
    /// </summary>
    internal static Activity? NextActivity(Colonist colonist, float workSpeed, bool idleHands)
    {
        Colony colony = colonist.Colony;
        if (!IsAllowed(colony) || colonist.Stage == LifeStage.Child || ActiveProject(colony) is not { } project)
            return null;
        int cap = WorkerCap(colony);
        int active = ActiveWorkers(colony);
        if (active >= Math.Max(cap, idleHands ? 1 : 0))
            return null;
        if (cap == 0 && !idleHands)
            return null;

        SettlementLayout layout = colony.Layout;
        LocalMap map = colony.Map;
        foreach (int segmentId in project.SegmentIds)
        {
            if (layout.SegmentById(segmentId) is not { } segment)
                continue;
            int offered = 0;
            foreach (int cell in segment.Cells)
            {
                if (map.Roads.SurfaceAt(cell) == RoadSurface.DirtRoad || !Buildable(colony, map, cell))
                    continue;
                if (++offered > SettlementRules.RoadBatchCells)
                    break;
                (int x, int y) = layout.Decode(cell);
                if (colony.Reserved.Contains((x, y)))
                    continue;
                project.State = ProjectState.Working;
                switch (map.GetFlora(x, y))
                {
                    case FloraType.Tree when map.CanChop(x, y):
                        return new Activity(ActivityKind.Chop, x, y, ChopTicks(workSpeed)) { SegmentId = segment.Id };
                    case FloraType.Tree:
                        continue; // un jeune arbre : on ne l'abat pas, on passe à la cellule suivante du lot
                    case FloraType.Bush:
                        return new Activity(ActivityKind.ClearAccess, x, y, ClearTicks(workSpeed)) { SegmentId = segment.Id };
                    default:
                        return new Activity(ActivityKind.BuildRoad, x, y, BuildTicks(workSpeed)) { SegmentId = segment.Id };
                }
            }
        }
        return null;
    }

    private static float ChopTicks(float speed) => 4f / speed * TimeConstants.TicksPerSecond;
    private static float ClearTicks(float speed) => 1.5f / speed * TimeConstants.TicksPerSecond;
    private static float BuildTicks(float speed) => SettlementRules.RoadWorkSecondsPerCell / speed * TimeConstants.TicksPerSecond;

    /// <summary>La cellule est aménagée : chemin de terre, souche arrachée, projet achevé quand tout l'axe l'est.</summary>
    internal static void CompleteCell(Colony colony, int x, int y, int segmentId)
    {
        LocalMap map = colony.Map;
        SettlementLayout layout = colony.Layout;
        int cell = y * map.Width + x;
        map.ClearFlora(x, y);
        map.Roads.SetWork(cell, RoadLayer.WorkDone);
        map.Roads.SetSurface(cell, RoadSurface.DirtRoad);
        map.Roads.SetWear(cell, 0, (int)colony.Clock.TotalDays);
        map.NotifyRoadChanged(x, y);
        SettlementPlanner.Notify(colony, RetryEvents.Road);

        if (layout.SegmentById(segmentId) is not { } segment)
            return;
        foreach (int other in segment.Cells)
            if (map.Roads.SurfaceAt(other) != RoadSurface.DirtRoad && Buildable(colony, map, other))
                return;
        if (layout.ProjectById(segment.ActiveProjectId) is { } project)
            project.State = ProjectState.Completed;
        segment.ActiveProjectId = -1;
    }

    /// <summary>L'aménagement s'arrête net en crise : les projets restent ouverts (leurs cellules et leur progression sont gardées), mais personne n'est plus affecté.</summary>
    public static bool IsPaused(Colony colony) => ActiveProject(colony) is not null && !IsAllowed(colony);
}
