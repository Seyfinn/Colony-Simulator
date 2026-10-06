using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Où en est l'étude d'un raccord : en cours, acceptée, ou refusée avec son motif (les motifs permettent un nouvel essai à une autre date).</summary>
public enum ShortcutVerdict { None, Studying, Accepted, GainTooSmall, PaybackTooLong, NoCorridor, Stale }

/// <summary>
/// Un flux de déplacements récents entre deux lieux de l'établissement (identités stables : numéro de bâtiment, -1 pour le camp) : combien de trajets terminés, en combien
/// de temps et de cases, jour par jour sur la fenêtre de 14 jours. Un trajet abandonné n'y entre jamais. Persisté : la décision d'un raccord relit les mêmes mesures
/// après un rechargement.
/// </summary>
public sealed class TravelFlow
{
    public int A { get; internal set; }
    public int B { get; internal set; }
    public Dictionary<long, int> TripsByDay { get; } = [];
    public Dictionary<long, long> TicksByDay { get; } = [];
    public Dictionary<long, long> CellsByDay { get; } = [];
    public long LastDay { get; internal set; }

    /// <summary>L'étude du raccordement en cours (numéro de recherche, -1 s'il n'y en a pas), son dernier verdict et le jour de ce verdict.</summary>
    public int StudyJobId { get; internal set; } = -1;
    public ShortcutVerdict Verdict { get; internal set; }
    public long VerdictDay { get; internal set; }

    public int Trips => TripsByDay.Values.Sum();
    public double AverageSeconds => Trips == 0 ? 0 : TicksByDay.Values.Sum() / (double)Trips / TimeConstants.TicksPerSecond;
    public double AverageCells => Trips == 0 ? 0 : CellsByDay.Values.Sum() / (double)Trips;
}

/// <summary>
/// Les raccourcis décidés et construits par les habitants. Les trajets terminés alimentent des flux ; chaque jour, au plus huit couples au plus grand détour sont examinés
/// et au plus une étude s'ouvre, jamais en crise ni pendant un autre ouvrage facultatif. L'étude est une recherche reprenable qui compte le coût futur aménagé, puis chiffre à part
/// le travail (aménagement, défrichage). Gain de trajet d'un quart au moins et travail amorti en huit jours au plus par le flux réel : alors seulement un tracé et un projet
/// s'ouvrent, exécutés par <see cref="RoadWorks"/>. Rien n'est praticable ni aménagé gratuitement.
/// </summary>
public static class RoadShortcuts
{
    public const int WindowDays = 14;
    public const int MaxFlows = 256;
    public const int MaxReviewed = 8;
    public const int MinTrips = 10;
    public const double MinGain = 0.25;
    public const int MaxPaybackDays = 8;
    public const int RetryDays = 5;

    /// <summary>Un trajet de moins de ce nombre de cases n'est pas un détour qu'on corrige.</summary>
    private const int MinCells = 6;

    // --- Les flux ---

    /// <summary>L'identité stable d'une extrémité : le bâtiment, le camp (-1) s'il est tout proche, sinon 0 (rien de stable : le trajet n'est pas retenu).</summary>
    private static int EndId(Colony colony, int buildingId, int x, int y) =>
        buildingId > 0 ? buildingId : Math.Max(Math.Abs(x - colony.CampX), Math.Abs(y - colony.CampY)) <= 6 ? -1 : 0;

    /// <summary>
    /// Un colon vient d'achever un trajet : extrémités, durée et longueur entrent dans le flux du couple. Un trajet abandonné ne passe jamais ici ; un passage fictif n'existe pas.
    /// </summary>
    internal static void RecordCompletedTrip(Colony colony, Colonist colonist, long now)
    {
        int a = EndId(colony, colonist.PathStartBuilding, colonist.PathStartX, colonist.PathStartY);
        int b = EndId(colony, colonist.PathGoalBuilding, colonist.TileX, colonist.TileY);
        long ticks = now - colonist.PathCommittedTicks;
        if (a == 0 || b == 0 || a == b || ticks <= 0 || colonist.Path.Count < MinCells)
            return;
        (int low, int high) = a < b ? (a, b) : (b, a);
        List<TravelFlow> flows = colony.LocalSettlement.TravelFlows;
        TravelFlow? flow = flows.FirstOrDefault(f => f.A == low && f.B == high);
        long day = colony.Clock.TotalDays;
        if (flow is null)
        {
            if (flows.Count >= MaxFlows)
                flows.Remove(flows.OrderBy(f => f.LastDay).ThenBy(f => f.A).ThenBy(f => f.B).First()); // le moins récemment utilisé, départagé par les clés
            flows.Add(flow = new TravelFlow { A = low, B = high });
        }
        flow.TripsByDay[day] = flow.TripsByDay.GetValueOrDefault(day) + 1;
        flow.TicksByDay[day] = flow.TicksByDay.GetValueOrDefault(day) + ticks;
        flow.CellsByDay[day] = flow.CellsByDay.GetValueOrDefault(day) + colonist.Path.Count;
        flow.LastDay = day;
    }

    private static (int Cell, bool Ok) CellOf(Colony colony, int id)
    {
        SettlementLayout layout = colony.Layout;
        if (id == -1)
            return (layout.Cell(colony.CampX, colony.CampY), true);
        return colony.BuildingById(id) is { IsComplete: true } building
            ? (building.HasDoor ? layout.Cell(building.AccessX, building.AccessY) : layout.Cell(building.X, building.Y), true)
            : (-1, false);
    }

    // --- La revue quotidienne ---

    /// <summary>
    /// Chaque jour, sous <see cref="Colony.UseSettlement"/> : vieillit les flux, puis, hors crise et sans autre ouvrage facultatif, ouvre l'étude du couple dont le détour pèse le
    /// plus. Aucune recherche ici : la file de planification, sous son budget mondial, la mène ensuite.
    /// </summary>
    internal static void ReviewDaily(WorldState world, Settlement place)
    {
        Colony colony = place.Owner;
        long today = world.Clock.TotalDays;
        List<TravelFlow> flows = place.TravelFlows;
        foreach (TravelFlow flow in flows)
        {
            foreach (long day in flow.TripsByDay.Keys.Where(d => d <= today - WindowDays).ToList())
            {
                flow.TripsByDay.Remove(day); flow.TicksByDay.Remove(day); flow.CellsByDay.Remove(day);
            }
            if (flow.StudyJobId >= 0 && !world.Planning.Jobs.Any(j => j.Id == flow.StudyJobId))
                flow.StudyJobId = -1;
        }
        flows.RemoveAll(f => f.Trips == 0 && f.StudyJobId < 0);

        // Une crise ou un ouvrage facultatif déjà actif : l'attente se conserve, rien ne s'ouvre.
        if (!RoadWorks.IsAllowed(colony) || RoadWorks.ActiveProject(colony) is not null || flows.Any(f => f.StudyJobId >= 0))
            return;

        TravelFlow? best = null;
        double bestScore = 0;
        foreach (TravelFlow flow in flows.Where(f => f.Trips >= MinTrips && (f.Verdict == ShortcutVerdict.None || today - f.VerdictDay >= RetryDays))
                     .OrderByDescending(f => f.Trips * (f.AverageSeconds)).ThenBy(f => f.A).ThenBy(f => f.B).Take(MaxReviewed))
        {
            (int cellA, bool okA) = CellOf(colony, flow.A);
            (int cellB, bool okB) = CellOf(colony, flow.B);
            if (!okA || !okB)
                continue;
            SettlementLayout layout = colony.Layout;
            (int ax, int ay) = layout.Decode(cellA);
            (int bx, int by) = layout.Decode(cellB);
            double ideal = TraversalCost.Octile(ax - bx, ay - by) * TraversalCost.MinStepSeconds;
            double detour = flow.AverageSeconds - ideal;
            if (detour < MinGain * flow.AverageSeconds)
                continue; // déjà presque direct : aucun raccourci ne rapporterait un quart
            double score = flow.Trips * detour;
            if (score > bestScore)
                (best, bestScore) = (flow, score);
        }
        if (best is not null)
            Study(world, colony, place, best);
    }

    /// <summary>Ouvre l'étude d'un couple : un travail de recherche reprenable dans la file du monde, sans doublon.</summary>
    private static void Study(WorldState world, Colony colony, Settlement place, TravelFlow flow)
    {
        SettlementPlanningState? planning = colony.Planning;
        if (planning is null)
            return;
        SettlementLayout layout = colony.Layout;
        var request = new PlanRequest($"shortcut:{flow.A}:{flow.B}", DevelopmentKind.RoadShortcut, null, colony.Clock.Ticks) { Priority = DevelopmentPriority.Comfort };
        var job = new PlanningJob(planning.NextJobId++, colony, request, colony.Clock.Ticks, layout.Revision, colony.Map.TerrainRevision)
        {
            ShortcutA = flow.A, ShortcutB = flow.B,
            ShortcutCurrentSeconds = flow.AverageSeconds, ShortcutCurrentCells = flow.AverageCells,
            ShortcutTripsPerDay = flow.Trips / (double)WindowDays, ShortcutVerdict = ShortcutVerdict.Studying,
            Stage = PlanStage.Paths,
        };
        planning.Jobs.Add(job);
        flow.StudyJobId = job.Id;
        flow.Verdict = ShortcutVerdict.Studying;
        flow.VerdictDay = world.Clock.TotalDays;
    }

    // --- La recherche (appelée par SitePlanner.Advance) ---

    internal static void Advance(PlanningJob job, SitePlanner.Budget budget)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        while (!job.IsFinished)
        {
            if (job.Search is null)
            {
                (int start, bool okA) = CellOf(colony, job.ShortcutA);
                (int goal, bool okB) = CellOf(colony, job.ShortcutB);
                if (!okA || !okB)
                {
                    Fail(job, ShortcutVerdict.NoCorridor);
                    return;
                }
                if (budget.ActiveSearches >= budget.MaxActiveSearches)
                    return;
                // Ce qui ne ferait pas gagner un quart du trajet actuel n'a aucun intérêt : la recherche l'écarte (trop long, pas introuvable).
                float maxSeconds = (float)(job.ShortcutCurrentSeconds * (1 - MinGain));
                job.Search = IncrementalPathSearch.Begin(map, index, start, goal, maxSeconds, null, assumeDirtRoad: true);
                budget.ActiveSearches++;
            }
            int before = budget.Expansions;
            SearchStatus status = IncrementalPathSearch.Advance(job.Search, map, index, ref budget.Expansions, SettlementRules.MaxSearchExpansions);
            budget.SpentExpansions += before - budget.Expansions;
            if (status == SearchStatus.Running)
                return;
            budget.ActiveSearches--;
            if (status == SearchStatus.Stale && job.SearchRestarts < 3)
            {
                job.SearchRestarts++;
                IncrementalPathSearch.Release(job.Search);
                job.Search = null;
                continue;
            }
            List<int>? path = IncrementalPathSearch.PathOf(job.Search);
            float seconds = job.Search.ResultSeconds;
            IncrementalPathSearch.Release(job.Search);
            job.Search = null;
            if (path is null)
                Fail(job, status == SearchStatus.Stale ? ShortcutVerdict.Stale : status is SearchStatus.TooFar or SearchStatus.Capped ? ShortcutVerdict.GainTooSmall : ShortcutVerdict.NoCorridor);
            else
                Conclude(job, colony, path, seconds);
        }
    }

    private static void Fail(PlanningJob job, ShortcutVerdict verdict)
    {
        job.ShortcutVerdict = verdict;
        job.Stage = PlanStage.Failed;
    }

    /// <summary>
    /// Chiffre le tracé trouvé : gain de trajet par rapport au parcours actuel, travail réel (aménagement de chaque cellule, défrichage) et amortissement par le flux récent.
    /// Un arbre trop jeune pour être abattu invalide le tracé ; aucune cellule interdite n'est jamais acceptée pour améliorer le score.
    /// </summary>
    private static void Conclude(PlanningJob job, Colony colony, List<int> path, float proposedSeconds)
    {
        LocalMap map = colony.Map;
        SettlementLayout layout = colony.Layout;
        double workSeconds = 0;
        int work = 0;
        foreach (int cell in path)
        {
            (int x, int y) = layout.Decode(cell);
            if (!Buildable(colony, map, x, y))
            {
                Fail(job, ShortcutVerdict.NoCorridor);
                return;
            }
            switch (map.GetFlora(x, y))
            {
                case FloraType.Tree when !map.CanChop(x, y):
                    Fail(job, ShortcutVerdict.NoCorridor);
                    return;
                case FloraType.Tree: workSeconds += 4; break;
                case FloraType.Bush: workSeconds += 1.5; break;
            }
            if (map.Roads.SurfaceAt(cell) != RoadSurface.DirtRoad)
            {
                workSeconds += SettlementRules.RoadWorkSecondsPerCell;
                work++;
            }
        }
        job.ShortcutProposedSeconds = proposedSeconds;
        job.ShortcutWorkHours = workSeconds * ScaleRules.HoursPerSecond;
        job.ShortcutCells.Clear();
        job.ShortcutCells.AddRange(path);
        double gain = 1 - proposedSeconds / job.ShortcutCurrentSeconds;
        // Le tracé doit être un vrai raccourci (moins de cases que le parcours actuel), pas la même route en meilleur état.
        if (gain < MinGain || path.Count > job.ShortcutCurrentCells * (1 - MinGain))
        {
            Fail(job, ShortcutVerdict.GainTooSmall);
            return;
        }
        double savedHoursPerDay = job.ShortcutTripsPerDay * (job.ShortcutCurrentSeconds - proposedSeconds) * ScaleRules.HoursPerSecond;
        if (savedHoursPerDay <= 0 || job.ShortcutWorkHours / savedHoursPerDay > MaxPaybackDays)
        {
            Fail(job, ShortcutVerdict.PaybackTooLong);
            return;
        }
        job.ShortcutVerdict = ShortcutVerdict.Accepted;
        job.Stage = PlanStage.Ready;
    }

    /// <summary>La cellule peut recevoir un chemin de terre : praticable, hors eau, relief interdit, bâtiments, cultures et canaux.</summary>
    private static bool Buildable(Colony colony, LocalMap map, int x, int y) =>
        map.InBounds(x, y) && map.IsWalkable(x, y) && !map.IsWaterway(x, y) && !map.IsMountain(x, y)
        && !colony.Spatial.Has(x, y, CellUse.Building | CellUse.Field | CellUse.Canal);

    // --- L'admission ---

    /// <summary>
    /// Une étude achevée remet son verdict au flux. Acceptée, elle n'ouvre un projet qu'après revalidation complète du corridor sur l'état actuel, hors crise et sans autre
    /// ouvrage facultatif ; sinon le verdict reste et un nouvel essai viendra plus tard. Appelée par le planificateur, sous le contexte de l'établissement.
    /// </summary>
    internal static void Deliver(PlanningJob job)
    {
        Colony colony = job.Owner;
        Settlement? place = colony.Settlements.FirstOrDefault(s => s.Id == job.SettlementId);
        TravelFlow? flow = place?.TravelFlows.FirstOrDefault(f => f.A == job.ShortcutA && f.B == job.ShortcutB);
        if (flow is null)
            return;
        flow.StudyJobId = -1;
        flow.VerdictDay = colony.Clock.TotalDays;
        flow.Verdict = job.ShortcutVerdict;
        if (job.Stage != PlanStage.Ready || job.ShortcutVerdict != ShortcutVerdict.Accepted)
            return;
        if (!Commit(colony, job))
            flow.Verdict = ShortcutVerdict.Stale;
    }

    /// <summary>Revalide chaque cellule du corridor sur le monde actuel, puis ouvre le tracé et son projet (jamais de chemin de terre gratuit : les habitants le feront cellule par cellule).</summary>
    private static bool Commit(Colony colony, PlanningJob job)
    {
        LocalMap map = colony.Map;
        SettlementLayout layout = colony.Layout;
        if (!RoadWorks.IsAllowed(colony) || RoadWorks.ActiveProject(colony) is not null || job.ShortcutCells.Count == 0)
            return false;
        foreach (int cell in job.ShortcutCells)
        {
            (int x, int y) = layout.Decode(cell);
            if (!Buildable(colony, map, x, y) || map.GetFlora(x, y) == FloraType.Tree && !map.CanChop(x, y))
                return false;
        }
        long now = colony.Clock.Ticks;
        var segment = new RoadSegment(layout.NextSegmentId++, SegmentFunction.Link, RoadSurface.DirtRoad, (int)DevelopmentPriority.Comfort, now);
        segment.Cells.AddRange(job.ShortcutCells);
        var project = new DevelopmentProject(layout.NextProjectId++, DevelopmentKind.RoadShortcut, DevelopmentPriority.Comfort, -1, now, $"shortcut:{job.ShortcutA}:{job.ShortcutB}");
        project.SegmentIds.Add(segment.Id);
        segment.ActiveProjectId = project.Id;
        layout.RoadSegments.Add(segment);
        layout.Projects.Add(project);
        layout.Revision++;
        return true;
    }
}
