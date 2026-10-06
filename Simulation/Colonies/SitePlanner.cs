using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Trouve la place d'un projet : teste les contraintes physiques, note les parcelles et leurs accès, produit une proposition sans rien modifier au monde.
/// Le travail total d'une demande se déroule par étapes reprenables (balayage des candidats, puis chemin d'accès de chaque finaliste) : une recherche est un
/// <see cref="PlanningJob"/> qui avance par petits lots, jamais un appel synchrone à exécuter entièrement dans une pensée.
/// </summary>
public static class SitePlanner
{
    /// <summary>Les composantes du score, dans l'ordre de <see cref="PlacementProposal.ScoreParts"/> : les six premières comptent en positif, les quatre suivantes en négatif.</summary>
    public static readonly string[] ScoreNames =
        ["terrain", "cohésion", "chaîne", "services", "réseau", "nouveau noyau", "marche", "nuisances", "défrichage", "bonne terre", "variation"];

    private const int PartCount = 11;
    private const float DetourFactor = 1.2f;
    private const float SecondsPerCell = 1f / SettlementRules.WalkTilesPerSecond;

    /// <summary>La rangée la plus lointaine scrutée par une ancre de nouveau noyau : une ancre n'est qu'un point de départ, pas une fenêtre.</summary>
    private const int AnchorRadius = 5;

    /// <summary>Au-delà de ce nombre de cases balayées, la recherche renonce (le terrain n'offre rien d'utile).</summary>
    private const int MaxScannedCells = 30000;

    /// <summary>Les tours de balayage supplémentaires quand tous les finalistes d'un lot échouent (un chemin introuvable, un trajet trop long).</summary>
    private const int MaxRounds = 3;

    /// <summary>Ce qu'un rendez-vous du planificateur peut dépenser : jamais des millisecondes, toujours un nombre d'opérations.</summary>
    internal sealed class Budget
    {
        public int Prefilters, Expansions, Cells;

        /// <summary>Contextes A* actifs dans le monde entier, et le maximum autorisé.</summary>
        public int ActiveSearches, MaxActiveSearches;

        // Ce qui a vraiment été dépensé (pour les compteurs du monde).
        public long SpentPrefilters, SpentExpansions, SpentCells;

        public static Budget Unlimited() => new()
        {
            Prefilters = int.MaxValue / 2, Expansions = int.MaxValue / 2, Cells = int.MaxValue / 2,
            MaxActiveSearches = int.MaxValue / 2,
        };
    }

    /// <summary>Les trajets visés et plafonnés d'une demande, en secondes de simulation à vitesse ×1.</summary>
    internal readonly record struct Limits(ServiceNeed Need, float Target, float Hard, float DepotTarget, float DepotHard);

    internal static Limits LimitsFor(PlanningJob job)
    {
        float scale = job.Urgent ? 2f : 1f;
        if (job.Kind == DevelopmentKind.Field)
            return new Limits(ServiceNeed.Depot, SettlementRules.PlotRoundTripSeconds / 2f, SettlementRules.PlotRoundTripCeilingSeconds / 2f * scale,
                SettlementRules.PlotRoundTripSeconds / 2f, SettlementRules.PlotRoundTripCeilingSeconds / 2f * scale);
        BuildingPlacementProfile profile = BuildingPlacementProfile.For(job.Type ?? BuildingType.Hut);
        return profile.Service switch
        {
            ServiceNeed.Meal => new Limits(ServiceNeed.Meal, SettlementRules.HomeToMealSeconds, SettlementRules.HomeToMealSeconds * 2f * scale,
                SettlementRules.HomeToDepotSeconds, SettlementRules.HomeToDepotSeconds * 2f * scale),
            ServiceNeed.Depot => new Limits(ServiceNeed.Depot, SettlementRules.PlotRoundTripSeconds / 2f, SettlementRules.PlotRoundTripCeilingSeconds / 2f * scale,
                SettlementRules.PlotRoundTripSeconds / 2f, SettlementRules.PlotRoundTripCeilingSeconds / 2f * scale),
            ServiceNeed.Tavern => new Limits(ServiceNeed.Tavern, 0.8f, 1.5f * scale, SettlementRules.HomeToDepotSeconds, SettlementRules.HomeToDepotSeconds * 2f * scale),
            _ => new Limits(ServiceNeed.None, 0f, 0f, 99f, 99f),
        };
    }

    internal static (int Width, int Height) FootprintOf(PlanningJob job) =>
        job.Kind == DevelopmentKind.Field ? (Farming.FieldSizeFor(job.Owner), Farming.FieldSizeFor(job.Owner)) : (BuildingPlacementProfile.For(job.Type ?? BuildingType.Hut).Width, BuildingPlacementProfile.For(job.Type ?? BuildingType.Hut).Height);

    // --- L'avancement d'une recherche ---

    /// <summary>
    /// Fait avancer une recherche tant que son budget le permet. Aucune mutation du monde : le résultat est une proposition prête ou une impossibilité.
    /// </summary>
    internal static void Advance(PlanningJob job, Budget budget)
    {
        if (job.Kind == DevelopmentKind.RoadShortcut)
        {
            RoadShortcuts.Advance(job, budget); // une recherche de raccord : même file, même budget, même reprise
            return;
        }
        while (!job.IsFinished)
        {
            bool progressed = job.Stage == PlanStage.Candidates ? AdvanceCandidates(job, budget) : AdvancePaths(job, budget);
            if (!progressed)
                return;
        }
    }

    // --- Le balayage des candidats ---

    private static bool AdvanceCandidates(PlanningJob job, Budget budget)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        if (!job.FrontsReady)
        {
            DistrictPlanner.BuildFronts(job, budget);
            job.FrontsReady = true;
            job.ScanRing = 0;
        }
        int fronts = job.FrontDistrict.Count;
        if (fronts == 0)
        {
            job.RejectedDistrict++;
            Fail(job);
            return true;
        }

        (int width, int height) = FootprintOf(job);
        Limits limits = LimitsFor(job);
        int wanted = job.Urgent ? 1 : SettlementRules.MaxCandidates;
        int perFront = Math.Max(4, SettlementRules.MaxCandidates / fronts);

        while (job.CandCell.Count < wanted)
        {
            if (budget.Cells <= 0)
                return false;
            if (!NextPosition(job, fronts, out int front, out int x, out int y))
            {
                if (!StartNextPass(job))
                    break;
                continue;
            }
            budget.Cells--;
            budget.SpentCells++;
            job.ScannedCells++;
            if (job.ScannedCells > MaxScannedCells)
            {
                job.ScanExhausted = true;
                break;
            }
            if (!map.InBounds(x, y) || !map.IsWalkable(x, y) || index.IsSolid(x, y))
                continue;
            if (CountOfFront(job, front) >= perFront)
                continue;
            if (job.Type == BuildingType.Mill && !HasWaterNearby(map, x, y))
                continue;

            if (budget.Prefilters <= 0)
            {
                // Pas de quoi examiner ce candidat à ce rendez-vous : on reprendra à cette position.
                job.ScanStep--;
                budget.Cells++;
                budget.SpentCells--;
                job.ScannedCells--;
                return false;
            }
            budget.Prefilters--;
            budget.SpentPrefilters++;
            Evaluate(job, front, x, y, width, height, limits);
        }

        if (job.CandCell.Count == 0)
        {
            Fail(job);
            return true;
        }
        SelectFinalists(job);
        job.Stage = PlanStage.Paths;
        return true;
    }

    private static int CountOfFront(PlanningJob job, int front)
    {
        int count = 0;
        foreach (int f in job.CandFront)
            if (f == front)
                count++;
        return count;
    }

    private static bool HasWaterNearby(LocalMap map, int x, int y)
    {
        for (int dy = -1; dy <= 2; dy++)
        for (int dx = -1; dx <= 2; dx++)
            if (map.InBounds(x + dx, y + dy) && (map.IsRiver(x + dx, y + dy) || map.IsFlooded(x + dx, y + dy) || map.IsCanalWet(x + dx, y + dy)))
                return true;
        return false;
    }

    /// <summary>Le rayon scruté autour d'un front à la passe courante : une ancre ne regarde que ses abords, les autres fenêtres grandissent de passe en passe.</summary>
    private static int FrontRadius(PlanningJob job, int front, LocalMap map)
    {
        if (!IsAnchorFront(job, front))
            return job.AnchorPhase ? -1 : PassRadius(job.Pass, map);
        // Une ancre ne regarde que ses abords ; pour un logement urgent, seulement après la fenêtre locale des quartiers existants.
        bool active = job.Urgent ? job.AnchorPhase : job.Pass == 0;
        return active ? AnchorRadius : -1;
    }

    /// <summary>Un front de nouveau noyau (une ancre) : une graine viable à quelque distance du noyau parent.</summary>
    private static bool IsAnchorFront(PlanningJob job, int front) => job.FrontAnchor[front] != 0;

    private static int PassRadius(int pass, LocalMap map) =>
        pass == 0 ? SettlementRules.Scaled(map, SettlementRules.WindowMax) : SettlementRules.Scaled(map, SettlementRules.ExplorationPasses[pass - 1]);

    private static int MaxPass(PlanningJob job) => job.Type == BuildingType.Mill ? SettlementRules.ExplorationPasses.Length : 2;

    /// <summary>Passe à une exploration plus large autour des pôles ; faux s'il n'y en a plus.</summary>
    private static bool StartNextPass(PlanningJob job)
    {
        if (job.AnchorPhase)
        {
            // Les ancres d'un logement urgent sont épuisées : retour aux passes d'exploration des quartiers existants.
            job.AnchorPhase = false;
            job.AnchorPhaseDone = true;
            job.ScanRing = PassRadius(job.Pass, job.Owner.Map) + 1;
            job.ScanFront = 0;
            job.ScanStep = 0;
            return StartNextPass(job);
        }
        if (job.Urgent && !job.AnchorPhaseDone && job.FrontAnchor.Contains(1))
        {
            job.AnchorPhase = true;
            job.ScanRing = 0;
            job.ScanFront = 0;
            job.ScanStep = 0;
            return true;
        }
        if (job.Pass >= MaxPass(job))
        {
            job.ScanExhausted = true;
            return false;
        }
        LocalMap map = job.Owner.Map;
        job.Pass++;
        job.ScanRing = PassRadius(job.Pass - 1, map) + 1;
        job.ScanFront = 0;
        job.ScanStep = 0;
        return true;
    }

    /// <summary>Avance le curseur : les anneaux d'abord, puis chaque front à son tour dans l'anneau (le plus proche des fronts d'abord, tous représentés).</summary>
    private static bool NextPosition(PlanningJob job, int fronts, out int front, out int x, out int y)
    {
        LocalMap map = job.Owner.Map;
        while (true)
        {
            if (job.ScanFront >= fronts)
            {
                job.ScanFront = 0;
                job.ScanStep = 0;
                job.ScanRing++;
            }
            int largest = -1;
            for (int f = 0; f < fronts; f++)
                largest = Math.Max(largest, FrontRadius(job, f, map));
            if (job.ScanRing > largest)
            {
                front = x = y = 0;
                return false;
            }
            int radius = FrontRadius(job, job.ScanFront, map);
            int cells = job.ScanRing == 0 ? 1 : 8 * job.ScanRing;
            if (job.ScanRing > radius || job.ScanStep >= cells)
            {
                job.ScanFront++;
                job.ScanStep = 0;
                continue;
            }
            (int dx, int dy) = RingOffset(job.ScanRing, job.ScanStep++);
            front = job.ScanFront;
            x = job.FrontCx[front] + dx;
            y = job.FrontCy[front] + dy;
            return true;
        }
    }

    /// <summary>La case d'un anneau : le tour, dans le sens des aiguilles d'une montre depuis le coin haut gauche (8 × r cases).</summary>
    internal static (int Dx, int Dy) RingOffset(int r, int step)
    {
        if (r == 0)
            return (0, 0);
        if (step < 2 * r) return (-r + step, -r);
        if (step < 4 * r) return (r, -r + (step - 2 * r));
        if (step < 6 * r) return (r - (step - 4 * r), r);
        return (-r, r - (step - 6 * r));
    }

    // --- Évaluation d'un candidat ---

    private static void Evaluate(PlanningJob job, int front, int x, int y, int width, int height, Limits limits)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        SettlementLayout layout = colony.Layout;
        bool isField = job.Kind == DevelopmentKind.Field;
        BuildingType type = job.Type ?? BuildingType.Hut;
        BuildingPlacementProfile profile = BuildingPlacementProfile.For(type);

        PlacementChecks.Verdict verdict = isField ? PlacementChecks.Field(map, index, x, y, width) : PlacementChecks.Building(map, index, x, y, width, height);
        if (verdict != PlacementChecks.Verdict.Ok)
        {
            if (verdict == PlacementChecks.Verdict.Terrain) job.RejectedTerrain++; else job.RejectedSpace++;
            return;
        }
        if (profile.NeedsFlow && !isField && Hydrology.MillFlow(map, new Building(type, x, y)) <= 0f)
        {
            job.RejectedTerrain++;
            return;
        }

        // Les nuisances : l'industrie lourde et le calme se tiennent à distance. Plancher de trois cases, toujours.
        float nuisance = 0f;
        if (!isField && (profile.Heavy || profile.Sensitive))
        {
            int gap = NuisanceGap(colony, profile, x, y, width, height);
            if (gap < SettlementRules.HeavySeparationFloor)
            {
                job.RejectedSpace++;
                return;
            }
            nuisance = gap >= SettlementRules.HeavySeparation ? 0f : (SettlementRules.HeavySeparation - gap) / (float)(SettlementRules.HeavySeparation - SettlementRules.HeavySeparationFloor + 1);
        }

        bool newCore = job.FrontDistrict[front] < 0 && job.FrontParent[front] >= 0 && IsAnchorFront(job, front);
        (int ax, int ay)? access = ChooseAccess(colony, map, index, job, x, y, width, height, limits.Need, newCore);
        if (access is not { } door)
        {
            job.RejectedAccess++;
            return;
        }

        // Le trajet estimé vers le service principal (à vol d'oiseau, avec un détour), puis vers le dépôt.
        (float primary, float depot) = EstimateTravel(colony, limits, newCore && !isField, door.ax, door.ay);
        if (limits.Need != ServiceNeed.None && primary > limits.Hard * 1.5f)
        {
            job.RejectedTravel++;
            return;
        }
        if (depot > limits.DepotHard * 1.5f)
        {
            job.RejectedTravel++;
            return;
        }

        float[] parts = new float[PartCount];
        ScoreParts(job, front, x, y, width, height, door.ax, door.ay, newCore, primary, depot, limits, nuisance, parts);
        float score = Combine(job, parts);

        job.CandCell.Add(layout.Cell(x, y));
        job.CandFront.Add(front);
        job.CandDoor.Add(layout.Cell(door.ax, door.ay));
        job.CandScore.Add(score);
        job.CandServiceSeconds.Add(primary);
        job.CandParts.AddRange(parts);
    }

    /// <summary>L'écart minimal (cases libres) au bâtiment dont on se tient à l'écart : l'industrie lourde pour un logement sensible, et inversement.</summary>
    private static int NuisanceGap(Colony colony, BuildingPlacementProfile profile, int x, int y, int width, int height)
    {
        int gap = 99;
        foreach (Building other in colony.Buildings)
        {
            BuildingPlacementProfile otherProfile = BuildingPlacementProfile.For(other.Type);
            bool conflict = (profile.Heavy && otherProfile.Sensitive) || (profile.Sensitive && otherProfile.Heavy);
            if (!conflict)
                continue;
            gap = Math.Min(gap, PlacementChecks.Gap(x, y, width, height, other.X, other.Y, other.Width, other.Height));
        }
        return gap;
    }

    /// <summary>
    /// La porte : parmi les accès praticables, celui qui regarde le service le plus utile (le plus proche de son but). Sans service, vers le cœur du village.
    /// À distance égale, l'ordre des côtés (stable) départage.
    /// </summary>
    private static (int ax, int ay)? ChooseAccess(Colony colony, LocalMap map, LocalSpatialIndex index, PlanningJob job, int x, int y, int width, int height, ServiceNeed need, bool newCore)
    {
        (int gx, int gy) = GoalPoint(colony, job, need, newCore, x, y);
        (int ax, int ay)? best = null;
        float bestDistance = float.MaxValue;
        if (job.Kind == DevelopmentKind.Field)
        {
            foreach ((int ax, int ay) in PlacementChecks.FieldAccesses(map, index, x, y, width))
            {
                float d = TraversalCost.Octile(ax - gx, ay - gy);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = (ax, ay);
                }
            }
            return best;
        }
        foreach (var door in PlacementChecks.FreeDoors(map, index, x, y, width, height))
        {
            float d = TraversalCost.Octile(door.AccessX - gx, door.AccessY - gy);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = (door.AccessX, door.AccessY);
            }
        }
        return best;
    }

    /// <summary>Le point que l'accès doit viser : la case de service la plus proche selon le besoin, ou le cœur du village.</summary>
    internal static (int X, int Y) GoalPoint(Colony colony, PlanningJob job, ServiceNeed need, bool newCore, int x, int y)
    {
        ServiceUse use = need switch
        {
            ServiceNeed.Meal => newCore ? ServiceUse.Stock : ServiceUse.Meal,
            ServiceNeed.Depot => ServiceUse.Stock,
            _ => ServiceUse.Stock,
        };
        if (need == ServiceNeed.Tavern && job.HintX >= 0)
            return (job.HintX, job.HintY);
        ServicePoint? point = SettlementServices.Nearest(colony, use, x, y);
        if (point is null)
            return (colony.CampX, colony.CampY);
        int cell = SettlementServices.NearestCell(colony, point, x, y);
        return colony.Layout.Decode(cell);
    }

    private static (float Primary, float Depot) EstimateTravel(Colony colony, Limits limits, bool ownPublicSpace, int ax, int ay)
    {
        float Seconds(ServiceUse use)
        {
            ServicePoint? point = SettlementServices.Nearest(colony, use, ax, ay);
            if (point is null)
                return 0f;
            return SettlementServices.DistanceTo(colony, point, ax, ay) * SecondsPerCell * DetourFactor;
        }
        float depot = Seconds(ServiceUse.Stock);
        float primary = limits.Need switch
        {
            ServiceNeed.Meal => ownPublicSpace ? 0.5f : Seconds(ServiceUse.Meal),
            ServiceNeed.Depot => depot,
            ServiceNeed.Tavern => depot,
            _ => 0f,
        };
        return (primary, depot);
    }

    // --- Le score ---

    private static void ScoreParts(PlanningJob job, int front, int x, int y, int width, int height, int ax, int ay, bool newCore,
        float primary, float depot, Limits limits, float nuisance, float[] parts)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        SettlementLayout layout = colony.Layout;
        bool isField = job.Kind == DevelopmentKind.Field;
        BuildingType type = job.Type ?? BuildingType.Hut;
        BuildingPlacementProfile profile = BuildingPlacementProfile.For(type);
        DistrictKind frontKind = (DistrictKind)job.FrontKind[front];

        // 0. Le terrain : la fertilité pour un champ, l'espace dégagé autour de l'emprise pour un bâtiment.
        int fertileSum = 0, fertile = 0, cells = 0, trees = 0, bushes = 0, openRing = 0, ringCells = 0;
        if (isField)
        {
            float richness = 0f;
            for (int ty = y; ty < y + height; ty++)
            for (int tx = x; tx < x + width; tx++)
            {
                richness += map.SoilRichness + (map.IsFertileBank(tx, ty) ? 0.35f : 0f) + (map.IsIrrigated(tx, ty) ? 0.25f : 0f);
                cells++;
                switch (map.GetFlora(tx, ty))
                {
                    case FloraType.Tree: trees++; break;
                    case FloraType.Bush: bushes++; break;
                }
            }
            parts[0] = Math.Clamp(richness / (cells * 1.6f), 0f, 1f);
            parts[8] = Math.Clamp((trees * 1.5f + bushes * 2.5f) / 24f, 0f, 1f);
        }
        else
        {
            for (int ty = y - 2; ty < y + height + 2; ty++)
            for (int tx = x - 2; tx < x + width + 2; tx++)
            {
                if (!map.InBounds(tx, ty) || (tx >= x && tx < x + width && ty >= y && ty < y + height))
                    continue;
                ringCells++;
                FloraType flora = map.GetFlora(tx, ty);
                if (flora == FloraType.Tree) trees++;
                else if (flora == FloraType.Bush) bushes++;
                if (map.IsWalkable(tx, ty) && !map.IsWaterway(tx, ty) && flora is not (FloraType.Tree or FloraType.Bush))
                    openRing++;
                if (map.IsFertileBank(tx, ty)) fertile++;
            }
            parts[0] = ringCells == 0 ? 0f : openRing / (float)ringCells;
            parts[8] = Math.Clamp((trees + 1.5f * bushes) / 14f, 0f, 1f);
            parts[9] = ringCells == 0 ? 0f : Math.Min(1f, fertile / (float)ringCells * 2f);
        }
        _ = fertileSum;

        // 1. La cohésion : une plage de distance au quartier, jamais la distance zéro ; une petite préférence stable (1 à 3 cases) varie les cours.
        parts[1] = Cohesion(job, front, x, y, width, height, frontKind, profile, isField);

        // 2. L'utilité pour la chaîne de production : partenaires, ressources, centralité selon le bâtiment.
        parts[2] = isField ? 0f : Chain(colony, job, type, profile, x, y, width, height);

        // 3. Les services : la qualité du trajet vers le service principal (et le dépôt pour un logement).
        float quality = limits.Need == ServiceNeed.None ? 1f : TimeQuality(primary, limits.Target, limits.Hard);
        parts[3] = type == BuildingType.Hut && !isField ? 0.6f * quality + 0.4f * TimeQuality(depot, limits.DepotTarget, limits.DepotHard) : quality;

        // 4. La réutilisation du réseau : plus l'accès est près d'un chemin, d'une place ou d'un corridor, moins il y a de tracé à ouvrir.
        parts[4] = NetworkProximity(colony, map, index, ax, ay);

        // 5. L'intérêt d'un nouveau noyau, quand la demande le justifie.
        if (newCore && frontKind == DistrictKind.Residential)
        {
            int largest = DistrictPlanner.HutGroups(colony).DefaultIfEmpty(0).Max();
            parts[5] = Math.Clamp((largest - (SettlementRules.GroupMaxHuts - 2)) / 2f, 0f, 1f);
        }

        // 6. La marche (coût) : le trajet principal rapporté à son plafond.
        parts[6] = limits.Need == ServiceNeed.None || limits.Hard <= 0f ? 0f : Math.Clamp(primary / limits.Hard, 0f, 1f);

        // 7. Les nuisances et la saturation (coût).
        float saturation = isField || newCore ? 0f : DistrictPlanner.Saturation(colony, x, y, 7);
        parts[7] = Math.Max(nuisance, 0.5f * saturation);

        // 10. Une petite variation spatiale déterministe : elle départage des choix comparables, jamais ne rend un site moins accessible.
        parts[10] = SettlementRules.Taste(layout.Seed, x, y, (int)frontKind, SettlementRules.HashKey(job.RequestKey));
    }

    private static float TimeQuality(float seconds, float target, float hard)
    {
        if (seconds <= target)
            return 1f;
        if (hard <= target)
            return 0f;
        return 1f - Math.Clamp((seconds - target) / (hard - target), 0f, 1f);
    }

    private static float Cohesion(PlanningJob job, int front, int x, int y, int width, int height, DistrictKind frontKind, BuildingPlacementProfile profile, bool isField)
    {
        Colony colony = job.Owner;
        SettlementLayout layout = colony.Layout;
        int gap = int.MaxValue;
        foreach (PlotReservation parcel in layout.ActiveParcels)
        {
            if (parcel.Kind is not (ParcelKind.Building or ParcelKind.Field))
                continue;
            District? district = layout.DistrictById(parcel.DistrictId);
            bool related = job.FrontDistrict[front] >= 0 ? parcel.DistrictId == job.FrontDistrict[front] : district?.Kind == frontKind;
            if (!related)
                continue;
            gap = Math.Min(gap, PlacementChecks.Gap(x, y, width, height, parcel.X, parcel.Y, parcel.Width, parcel.Height));
        }

        float baseValue;
        if (gap == int.MaxValue)
        {
            int distance = Math.Max(Math.Abs(x + width / 2 - job.FrontCx[front]), Math.Abs(y + height / 2 - job.FrontCy[front]));
            baseValue = distance <= 6 ? 1f : Math.Max(0f, 1f - (distance - 6) / 10f);
        }
        else if (isField)
            baseValue = gap <= 2 ? 1f : Math.Max(0f, 1f - (gap - 2) / 8f);
        else
        {
            baseValue = gap <= SettlementRules.MaxGap ? 1f : Math.Max(0.1f, 1f - 0.8f * (gap - SettlementRules.MaxGap) / 4f);
            int preferred = SettlementRules.MinGap + (int)(SettlementRules.Mix(layout.Seed, x, y, 7) % (uint)(SettlementRules.MaxGap - SettlementRules.MinGap + 1));
            baseValue *= gap == preferred ? 1f : 0.88f;
        }
        return profile.Preferred == frontKind || isField ? baseValue : baseValue * 0.9f;
    }

    private static float Chain(Colony colony, PlanningJob job, BuildingType type, BuildingPlacementProfile profile, int x, int y, int width, int height)
    {
        float Near(IEnumerable<Building> buildings, int radius)
        {
            int best = int.MaxValue;
            foreach (Building b in buildings)
                best = Math.Min(best, PlacementChecks.Gap(x, y, width, height, b.X, b.Y, b.Width, b.Height));
            return best == int.MaxValue ? 0.3f : Math.Clamp(1f - best / (float)radius, 0f, 1f);
        }

        float partners = profile.Partners.Length == 0 ? 0.5f : Near(colony.Buildings.Where(b => Array.IndexOf(profile.Partners, b.Type) >= 0), 10);
        LocalMap map = colony.Map;
        switch (type)
        {
            case BuildingType.Kiln:
            {
                // La charbonnière brûle du bois : elle aime la forêt.
                int trees = 0;
                for (int dy = -8; dy <= 8; dy += 2)
                for (int dx = -8; dx <= 8; dx += 2)
                    if (map.InBounds(x + dx, y + dy) && map.GetFlora(x + dx, y + dy) == FloraType.Tree)
                        trees++;
                return 0.5f * Math.Min(1f, trees / 25f) + 0.5f * partners;
            }
            case BuildingType.Bloomery:
                // Le bas fourneau tient près du minerai : la carrière.
                return colony.Quarry is { } quarry
                    ? 0.5f * Math.Clamp(1f - Math.Max(Math.Abs(quarry.X - x), Math.Abs(quarry.Y - y)) / 30f, 0f, 1f) + 0.5f * partners
                    : partners;
            case BuildingType.Mill:
                return 0.5f * Math.Clamp(Hydrology.MillFlow(map, new Building(type, x, y)), 0f, 1f) + 0.5f * partners;
            case BuildingType.Market:
            case BuildingType.Tavern:
            case BuildingType.School:
            case BuildingType.Infirmary:
            case BuildingType.Well:
            {
                // Les services de la vie du village : près des habitations.
                var huts = colony.Buildings.Where(b => b.IsHut).ToList();
                if (huts.Count == 0)
                    return 0.5f;
                float cx = (float)huts.Average(b => b.X), cy = (float)huts.Average(b => b.Y);
                return Math.Clamp(1f - Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) / 16f, 0f, 1f);
            }
            case BuildingType.Storehouse:
            case BuildingType.Cask:
                return job.HintX >= 0 ? Math.Clamp(1f - Math.Max(Math.Abs(x - job.HintX), Math.Abs(y - job.HintY)) / 8f, 0f, 1f) : 0.5f;
            case BuildingType.Pen:
            {
                int best = colony.Fields.Select(f => PlacementChecks.Gap(x, y, width, height, f.X, f.Y, f.Size, f.Size)).DefaultIfEmpty(12).Min();
                return 0.5f * Math.Clamp(1f - best / 12f, 0f, 1f) + 0.5f * partners;
            }
            default:
                return partners;
        }
    }

    private static float NetworkProximity(Colony colony, LocalMap map, LocalSpatialIndex index, int ax, int ay)
    {
        SettlementLayout layout = colony.Layout;
        int best = 7;
        for (int dy = -6; dy <= 6; dy++)
        for (int dx = -6; dx <= 6; dx++)
        {
            int x = ax + dx, y = ay + dy;
            if (!map.InBounds(x, y))
                continue;
            int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (d >= best)
                continue;
            if ((index.UseAt(x, y) & (CellUse.Corridor | CellUse.Plaza | CellUse.PublicSpace)) != 0 || map.Roads.SurfaceAt(layout.Cell(x, y)) != RoadSurface.None)
                best = d;
        }
        return 1f - best / 7f;
    }

    /// <summary>Le score : les composantes positives moins les négatives, pondérées selon le profil (fertilité pour un champ, nuisances et services pour une hutte…).</summary>
    private static float Combine(PlanningJob job, float[] parts)
    {
        float terrain = SettlementRules.WTerrain, services = SettlementRules.WServices, nuisance = SettlementRules.WNuisance, chain = SettlementRules.WChain;
        if (job.Kind == DevelopmentKind.Field)
            terrain *= 1.5f;
        else if (job.Type == BuildingType.Hut)
        {
            services *= 1.2f;
            nuisance *= 1.2f;
        }
        else if (BuildingPlacementProfile.For(job.Type ?? BuildingType.Hut).Heavy)
            chain *= 1.5f;
        return terrain * parts[0] + SettlementRules.WCohesion * parts[1] + chain * parts[2] + services * parts[3]
            + SettlementRules.WNetwork * parts[4] + SettlementRules.WNewCore * parts[5]
            - SettlementRules.WWalking * parts[6] - nuisance * parts[7] - SettlementRules.WClearing * parts[8] - SettlementRules.WGoodSoil * parts[9]
            + SettlementRules.WVariation * parts[10];
    }

    /// <summary>
    /// Évalue un emplacement demandé explicitement (outil, test) comme un candidat unique : mêmes contraintes physiques, même porte, même tracé d'accès, mêmes
    /// durées. Les plafonds de trajet sont ceux d'un cas urgent. Renvoie null si l'emplacement est refusé ; ne modifie rien.
    /// </summary>
    internal static PlacementProposal? ProposeAt(Colony colony, DevelopmentKind kind, BuildingType? type, int x, int y)
    {
        SettlementLayout layout = colony.Layout;
        var request = new PlanRequest(SettlementPlanner.KeyFor(kind, type), kind, type, colony.Clock.Ticks) { Urgent = true, Priority = DevelopmentPriority.Housing };
        var job = new PlanningJob(0, colony, request, colony.Clock.Ticks, layout.Revision, colony.Map.TerrainRevision);
        BuildingPlacementProfile profile = BuildingPlacementProfile.For(type ?? BuildingType.Hut);
        DistrictKind frontKind = kind == DevelopmentKind.Field ? DistrictKind.Agricultural : profile.Preferred;
        (int width, int height) = FootprintOf(job);
        District? district = SettlementPlanner.DistrictFor(layout, kind == DevelopmentKind.Field ? null : profile, x, y, width, height, kind == DevelopmentKind.Field ? DistrictKind.Agricultural : null);
        job.FrontsReady = true;
        job.FrontDistrict.Add(district?.Id ?? -1);
        job.FrontKind.Add((int)(district?.Kind ?? frontKind));
        job.FrontCx.Add(x);
        job.FrontCy.Add(y);
        job.FrontParent.Add(district?.ParentDistrictId ?? SettlementPlanner.NearestDistrictId(layout, x, y));
        job.FrontAnchor.Add(0);

        Evaluate(job, 0, x, y, width, height, LimitsFor(job));
        if (job.CandCell.Count == 0)
            return null;
        SelectFinalists(job);
        job.Stage = PlanStage.Paths;
        Advance(job, Budget.Unlimited());
        if (job.Search is not null)
            IncrementalPathSearch.Release(job.Search);
        return job.Stage == PlanStage.Ready ? job.Result : null;
    }

    // --- Les finalistes et leurs chemins d'accès ---

    private static void SelectFinalists(PlanningJob job)
    {
        job.Finalists.Clear();
        job.PathCursor = 0;
        SettlementLayout layout = job.Owner.Layout;
        var order = Enumerable.Range(0, job.CandCell.Count).ToList();
        // Départage stable : le score, puis le quartier, puis Y, puis X.
        order.Sort((a, b) =>
        {
            int byScore = job.CandScore[b].CompareTo(job.CandScore[a]);
            if (byScore != 0) return byScore;
            int byFront = job.FrontDistrict[job.CandFront[a]].CompareTo(job.FrontDistrict[job.CandFront[b]]);
            if (byFront != 0) return byFront;
            (int ax, int ay) = layout.Decode(job.CandCell[a]);
            (int bx, int by) = layout.Decode(job.CandCell[b]);
            return ay != by ? ay.CompareTo(by) : ax.CompareTo(bx);
        });
        job.Finalists.AddRange(order.Take(job.Urgent ? Math.Max(1, order.Count) : SettlementRules.MaxFinalists));
    }

    private static bool AdvancePaths(PlanningJob job, Budget budget)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        SettlementLayout layout = colony.Layout;
        Limits limits = LimitsFor(job);

        while (true)
        {
            if (job.Search is null)
            {
                if (job.PathCursor >= job.Finalists.Count)
                    return Finish(job);
                if (budget.ActiveSearches >= budget.MaxActiveSearches)
                    return false;
                StartSearch(job, limits);
                if (job.Search is null)
                {
                    job.PathCursor++;
                    continue;
                }
                budget.ActiveSearches++;
            }

            int before = budget.Expansions;
            SearchStatus status = IncrementalPathSearch.Advance(job.Search, map, index, ref budget.Expansions, SettlementRules.MaxSearchExpansions);
            budget.SpentExpansions += before - budget.Expansions;
            if (status == SearchStatus.Running)
                return false;

            int candidate = job.Finalists[job.PathCursor];
            switch (status)
            {
                case SearchStatus.Found:
                    CompleteCandidate(job, candidate, job.Search, limits);
                    break;
                case SearchStatus.Unreachable:
                    job.RejectedAccess++;
                    break;
                case SearchStatus.Stale when job.SearchRestarts < 3:
                    // Le monde a changé dans la fenêtre : on rouvre la recherche de ce finaliste, sans rien conclure.
                    job.SearchRestarts++;
                    IncrementalPathSearch.Release(job.Search);
                    job.Search = null;
                    budget.ActiveSearches--;
                    StartSearch(job, limits);
                    if (job.Search is null)
                        job.PathCursor++;
                    else
                        budget.ActiveSearches++;
                    continue;
                default:
                    job.RejectedTravel++;
                    break;
            }
            IncrementalPathSearch.Release(job.Search);
            job.Search = null;
            job.SearchRestarts = 0;
            budget.ActiveSearches--;
            job.PathCursor++;
            if (job.Urgent && job.Completed.Count > 0)
                return Finish(job);
        }
    }

    private static void StartSearch(PlanningJob job, Limits limits)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        SettlementLayout layout = colony.Layout;
        int candidate = job.Finalists[job.PathCursor];
        (int x, int y) = layout.Decode(job.CandCell[candidate]);
        (int ax, int ay) = layout.Decode(job.CandDoor[candidate]);
        (int width, int height) = FootprintOf(job);
        bool newCore = IsAnchorFront(job, job.CandFront[candidate]);

        // Le monde a pu changer depuis le balayage : l'emprise et la porte sont revérifiées avant de dépenser un A*.
        PlacementChecks.Verdict verdict = job.Kind == DevelopmentKind.Field
            ? PlacementChecks.Field(map, index, x, y, width)
            : PlacementChecks.Building(map, index, x, y, width, height);
        bool doorFree = job.Kind == DevelopmentKind.Field
            ? PlacementChecks.FieldAccesses(map, index, x, y, width).Any(a => a.AccessX == ax && a.AccessY == ay)
            : PlacementChecks.FreeDoors(map, index, x, y, width, height).Any(d => d.AccessX == ax && d.AccessY == ay);
        if (verdict != PlacementChecks.Verdict.Ok || !doorFree)
        {
            job.RejectedSpace++;
            return;
        }

        // Une porte praticable est garantie : le but est le service (ou le dépôt, pour relier un nouveau noyau au village).
        (int gx, int gy) = GoalPoint(colony, job, limits.Need, newCore, ax, ay);
        float maxSeconds = Math.Max(limits.Hard, limits.DepotHard) * 1.1f + 0.5f;
        if (limits.Need == ServiceNeed.None)
            maxSeconds = 4f;
        // Un trajet de secours peut être plus long qu'à vol d'oiseau : le plafond est en secondes de marche réelles.
        job.Search = IncrementalPathSearch.Begin(map, index, layout.Cell(ax, ay), layout.Cell(gx, gy), maxSeconds, (x, y, width, height));
    }

    /// <summary>Un chemin d'accès a été trouvé : on en fait une proposition complète (tracé de raccordement, durées, score recalculé).</summary>
    private static void CompleteCandidate(PlanningJob job, int candidate, PathSearchState search, Limits limits)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        SettlementLayout layout = colony.Layout;
        (int x, int y) = layout.Decode(job.CandCell[candidate]);
        (int width, int height) = FootprintOf(job);
        (int ax, int ay) = layout.Decode(job.CandDoor[candidate]);
        int front = job.CandFront[candidate];
        bool newCore = IsAnchorFront(job, front);
        bool isField = job.Kind == DevelopmentKind.Field;

        // Le tracé : de la case d'accès jusqu'à la première case du réseau (comprise) ; le reste est déjà possédé par d'autres.
        List<int> path = IncrementalPathSearch.PathOf(search) ?? [];
        var connection = new List<int> { layout.Cell(ax, ay) };
        bool attached = IsNetwork(map, index, layout.Cell(ax, ay));
        if (!attached)
            foreach (int cell in path)
            {
                connection.Add(cell);
                if (IsNetwork(map, index, cell))
                    break;
            }

        PlacementProposal proposal = new(job.RequestKey, job.Kind, job.Type, x, y, width, height)
        {
            DistrictId = job.FrontDistrict[front],
            NewKind = (DistrictKind)job.FrontKind[front],
            ForceNewDistrict = newCore,
            NewAnchorX = x,
            NewAnchorY = y,
            ParentDistrictId = job.FrontParent[front],
            Margin = isField ? 0 : 1,
            AccessX = ax,
            AccessY = ay,
            EvaluatedRevision = index.Revision,
            EvaluatedTerrainRevision = map.TerrainRevision,
        };
        if (!isField)
        {
            foreach (var door in PlacementChecks.FreeDoors(map, index, x, y, width, height))
                if (door.AccessX == ax && door.AccessY == ay)
                {
                    proposal.EntryX = door.EntryX;
                    proposal.EntryY = door.EntryY;
                    break;
                }
        }
        proposal.PathCells.AddRange(connection);
        foreach (int cell in connection)
        {
            (int cx, int cy) = layout.Decode(cell);
            if (map.GetFlora(cx, cy) is FloraType.Tree or FloraType.Bush)
                proposal.ClearCells.Add(cell);
        }

        // Les durées réelles : le trajet exact vers le but, et l'aller-retour au dépôt.
        float seconds = search.ResultSeconds;
        bool goalIsStock = limits.Need is ServiceNeed.Depot or ServiceNeed.None || newCore;
        (float primaryEstimate, float depotEstimate) = EstimateTravel(colony, limits, newCore && !isField, ax, ay);
        float primary = limits.Need == ServiceNeed.Meal && newCore ? 0.5f : (limits.Need == ServiceNeed.Meal || limits.Need == ServiceNeed.Tavern) ? seconds : (goalIsStock ? seconds : primaryEstimate);
        float depot = goalIsStock ? seconds : depotEstimate;
        proposal.ServiceSeconds = primary;
        proposal.DepotRoundTripSeconds = 2f * depot;

        // Au-dessus de la cible mais sous le plafond : on garde, avec le motif.
        if (limits.Need != ServiceNeed.None && primary > limits.Target)
            proposal.Degradation = $"trajet de {primary:0.0} s (cible {limits.Target:0.0} s)";
        else if (depot > limits.DepotTarget && job.Kind != DevelopmentKind.Housing)
            proposal.Degradation = $"dépôt à {depot:0.0} s (cible {limits.DepotTarget:0.0} s)";

        if (job.Type == BuildingType.Hut && newCore)
            AttachPublicSpace(colony, map, index, proposal);

        // Le score est recalculé avec le trajet exact.
        float[] parts = new float[PartCount];
        Array.Copy(job.CandParts.ToArray(), candidate * PartCount, parts, 0, PartCount);
        parts[3] = job.Type == BuildingType.Hut && !isField
            ? 0.6f * (limits.Need == ServiceNeed.None ? 1f : TimeQuality(primary, limits.Target, limits.Hard)) + 0.4f * TimeQuality(depot, limits.DepotTarget, limits.DepotHard)
            : (limits.Need == ServiceNeed.None ? 1f : TimeQuality(primary, limits.Target, limits.Hard));
        parts[6] = limits.Need == ServiceNeed.None || limits.Hard <= 0f ? 0f : Math.Clamp(primary / limits.Hard, 0f, 1f);
        parts[8] = Math.Clamp(parts[8] + proposal.ClearCells.Count / 12f, 0f, 1f);
        proposal.ScoreParts = parts;
        proposal.Score = Combine(job, parts);
        job.Completed.Add(proposal);
    }

    private static bool IsNetwork(LocalMap map, LocalSpatialIndex index, int cell) =>
        map.Roads.SurfaceAt(cell) != RoadSurface.None || (index.UseAt(cell) & (CellUse.Corridor | CellUse.Plaza | CellUse.PublicSpace)) != 0;

    /// <summary>
    /// Un nouveau noyau d'habitat reçoit un petit espace public (3 × 3 si possible, sinon 2 × 2) où l'on se retrouve et où l'on mange : sur un terrain déjà
    /// utilisable (plat, sec, sans arbre ni buisson), tout près de la première hutte, hors de son emprise, de son tracé et de leurs abords. Sans lui, on garde les repas au camp.
    /// </summary>
    private static void AttachPublicSpace(Colony colony, LocalMap map, LocalSpatialIndex index, PlacementProposal proposal)
    {
        SettlementLayout layout = colony.Layout;
        var path = new HashSet<int>(proposal.PathCells);
        foreach (int size in new[] { 3, 2 })
        {
            for (int r = 2; r <= 5; r++)
            for (int step = 0; step < 8 * r; step++)
            {
                (int dx, int dy) = RingOffset(r, step);
                int px = proposal.X + dx, py = proposal.Y + dy;
                if (!PublicSpaceFits(map, index, layout, proposal, path, px, py, size))
                    continue;
                proposal.PublicX = px;
                proposal.PublicY = py;
                proposal.PublicWidth = proposal.PublicHeight = size;
                return;
            }
        }
    }

    private static bool PublicSpaceFits(LocalMap map, LocalSpatialIndex index, SettlementLayout layout, PlacementProposal proposal, HashSet<int> path, int x, int y, int size)
    {
        if (x < 1 || y < 1 || x + size >= map.Width || y + size >= map.Height)
            return false;
        int elevation = map.GetElevation(x, y);
        for (int ty = y; ty < y + size; ty++)
        for (int tx = x; tx < x + size; tx++)
        {
            if (!map.IsWalkable(tx, ty) || map.IsWaterway(tx, ty) || map.IsMountain(tx, ty) || map.GetElevation(tx, ty) != elevation
                || map.GetFlora(tx, ty) is FloraType.Tree or FloraType.Bush
                || index.IsSolid(tx, ty) || index.Has(tx, ty, CellUse.Corridor | CellUse.Courtyard)
                || path.Contains(layout.Cell(tx, ty)))
                return false;
            // Hors de l'emprise de la hutte et de sa cour (une case autour).
            if (tx >= proposal.X - 1 && tx <= proposal.X + proposal.Width && ty >= proposal.Y - 1 && ty <= proposal.Y + proposal.Height)
                return false;
        }
        return true;
    }

    /// <summary>Le dernier finaliste est traité : le meilleur devient le résultat, sinon on balaie plus loin (quelques tours) ou l'on renonce avec le motif.</summary>
    private static bool Finish(PlanningJob job)
    {
        if (job.Completed.Count > 0)
        {
            PlacementProposal best = job.Completed[0];
            for (int i = 1; i < job.Completed.Count; i++)
                if (job.Completed[i].Score > best.Score)
                    best = job.Completed[i];
            job.Result = best;
            job.Stage = PlanStage.Ready;
            return true;
        }
        if (!job.ScanExhausted && job.Round < MaxRounds)
        {
            job.Round++;
            job.CandCell.Clear();
            job.CandFront.Clear();
            job.CandDoor.Clear();
            job.CandScore.Clear();
            job.CandParts.Clear();
            job.CandServiceSeconds.Clear();
            job.Finalists.Clear();
            job.PathCursor = 0;
            job.Stage = PlanStage.Candidates;
            return true;
        }
        Fail(job);
        return true;
    }

    /// <summary>Aucun emplacement : une impossibilité physique, avec la raison la plus fréquente parmi les refus (jamais un manque de budget).</summary>
    private static void Fail(PlanningJob job)
    {
        job.Stage = PlanStage.Failed;
        int terrain = job.RejectedTerrain, space = job.RejectedSpace, access = job.RejectedAccess, travel = job.RejectedTravel;
        if (job.RejectedDistrict > 0 && terrain + space + access + travel == 0)
        {
            job.Failure = PlacementFailureKind.NoCompatibleDistrict;
            job.FailureDetail = "aucun quartier compatible";
        }
        else if (travel >= Math.Max(Math.Max(terrain, space), access) && travel > 0)
        {
            job.Failure = PlacementFailureKind.TravelBudgetExceeded;
            job.FailureDetail = "tous les emplacements sont trop loin d'un service";
        }
        else if (access >= Math.Max(terrain, space) && access > 0)
        {
            job.Failure = PlacementFailureKind.NoAccess;
            job.FailureDetail = "aucun accès praticable";
        }
        else if (space >= terrain && space > 0)
        {
            job.Failure = PlacementFailureKind.NoSpace;
            job.FailureDetail = "pas de place libre";
        }
        else
        {
            job.Failure = PlacementFailureKind.NoSuitableTerrain;
            job.FailureDetail = "pas de terrain adapté";
        }
    }
}
