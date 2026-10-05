using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Où chercher la place d'un nouveau projet : les prolongements des quartiers existants (leurs « fronts de croissance ») et, quand il le faut, quelques
/// ancres viables pour un nouveau noyau. Il mesure aussi la saturation d'un quartier. Il ne réserve rien : une ancre est un candidat, pas une parcelle.
/// </summary>
internal static class DistrictPlanner
{
    /// <summary>Directions échantillonnées autour d'un noyau pour trouver des ancres : seize, sans secteur imposé (le terrain décide).</summary>
    private const int Directions = 16;
    private const int MaxAnchors = 6;
    private const int AnchorWindow = 4;

    /// <summary>Un quartier assez rempli pour que la demande suivante favorise un nouveau groupe : nombre de bâtiments (huttes) acceptés.</summary>
    internal static int HutsInGroup(Colony colony, District district) =>
        colony.Layout.ParcelsOf(district).Count(p => p.Kind == ParcelKind.Building && p.BuildingType == BuildingType.Hut);

    /// <summary>Le nombre de huttes de chaque quartier qui en abrite (résidentiel, ou civique pour les premières).</summary>
    internal static List<int> HutGroups(Colony colony) =>
        colony.Layout.Districts.Select(d => HutsInGroup(colony, d)).Where(n => n > 0).ToList();

    /// <summary>Toutes les huttes acceptées du village (le noyau parent compte celles qui logent dans le quartier civique).</summary>
    internal static int HutsAccepted(Colony colony) =>
        colony.Layout.ActiveParcels.Count(p => p.Kind == ParcelKind.Building && p.BuildingType == BuildingType.Hut);

    /// <summary>Le centre d'un quartier : le milieu de l'enveloppe de ses parcelles (l'ancre pour un quartier encore vide).</summary>
    internal static (int X, int Y) CenterOf(Colony colony, District district)
    {
        (int minX, int minY, int maxX, int maxY) = colony.Layout.EnvelopeOf(district);
        return ((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>Le noyau d'habitat le plus fourni (le parent d'un nouveau noyau) : le quartier résidentiel aux plus nombreuses huttes, sinon le cœur civique.</summary>
    internal static District PrimaryCore(Colony colony)
    {
        SettlementLayout layout = colony.Layout;
        District? best = null;
        int bestHuts = -1;
        foreach (District district in layout.Districts)
        {
            if (district.Kind is not (DistrictKind.Residential or DistrictKind.Civic))
                continue;
            int huts = HutsInGroup(colony, district);
            if (huts > bestHuts)
            {
                bestHuts = huts;
                best = district;
            }
        }
        return best ?? layout.Districts[0];
    }

    /// <summary>
    /// Construit les fronts de croissance d'une recherche : où scruter, dans l'ordre. Les prolongements des quartiers compatibles d'abord (la vocation préférée avant
    /// les vocations acceptées), puis les ancres d'un nouveau noyau si la demande le justifie.
    /// </summary>
    internal static void BuildFronts(PlanningJob job, SitePlanner.Budget budget)
    {
        Colony colony = job.Owner;
        SettlementLayout layout = colony.Layout;
        LocalMap map = colony.Map;

        void Front(District? district, DistrictKind kind, int cx, int cy, int parent)
        {
            job.FrontDistrict.Add(district?.Id ?? -1);
            job.FrontKind.Add((int)kind);
            job.FrontCx.Add(cx);
            job.FrontCy.Add(cy);
            job.FrontAnchor.Add(0);
            job.FrontParent.Add(parent);
        }

        if (job.Kind == DevelopmentKind.Field)
        {
            foreach (District district in layout.Districts.Where(d => d.Kind == DistrictKind.Agricultural))
            {
                (int cx, int cy) = CenterOf(colony, district);
                Front(district, DistrictKind.Agricultural, cx, cy, district.ParentDistrictId);
            }
            // Un champ s'ouvre aussi au plus près d'un dépôt : le camp et les entrepôts achevés sont des pôles où un nouveau groupe de champs peut naître.
            foreach (ServicePoint point in SettlementServices.Points(colony).Where(p => p.Provides(ServiceUse.Stock)))
            {
                (int cx, int cy) = layout.Decode(point.Cells[0]);
                Front(null, DistrictKind.Agricultural, cx, cy, point.DistrictId);
            }
            return;
        }

        BuildingType type = job.Type ?? BuildingType.Hut;
        BuildingPlacementProfile profile = BuildingPlacementProfile.For(type);
        budget.Cells -= 4;

        if (type == BuildingType.Cask || (profile.FollowsNeighbourhood && job.HintX >= 0))
        {
            District? near = NearestDistrict(colony, job.HintX, job.HintY);
            Front(near, near?.Kind ?? profile.Preferred, job.HintX, job.HintY, near?.ParentDistrictId ?? -1);
            return;
        }

        // Les quartiers existants : la vocation préférée, puis les vocations acceptées, chacun dans l'ordre de création.
        foreach (District district in layout.Districts.Where(d => d.Kind == profile.Preferred))
        {
            (int cx, int cy) = CenterOf(colony, district);
            Front(district, district.Kind, cx, cy, district.ParentDistrictId);
        }
        foreach (District district in layout.Districts.Where(d => d.Kind != profile.Preferred && profile.Accepts(d.Kind)))
        {
            (int cx, int cy) = CenterOf(colony, district);
            Front(district, district.Kind, cx, cy, district.ParentDistrictId);
        }

        if (type == BuildingType.Hut)
        {
            if (AllowsNewCore(colony, job))
                AddAnchors(job, budget, DistrictKind.Residential, PrimaryCore(colony));
        }
        else if (!layout.Districts.Any(d => d.Kind == profile.Preferred) && type != BuildingType.Dam)
        {
            // Un quartier spécialisé n'existe qu'après l'acceptation de son premier projet : on ne propose que ses ancres, pas un quartier vide.
            AddAnchors(job, budget, profile.Preferred, layout.Districts[0]);
        }
    }

    /// <summary>
    /// Un nouveau noyau d'habitat : seulement pour une demande réelle, quand le premier groupe compte déjà au moins trois huttes ; les nouveaux quartiers de confort
    /// s'espacent de quelques jours (le logement urgent est exempté).
    /// </summary>
    private static bool AllowsNewCore(Colony colony, PlanningJob job)
    {
        // Un noyau de plus seulement quand les groupes existants sont étoffés : aucun n'est resté en deçà de trois huttes, et le plus gros approche de six.
        var groups = HutGroups(colony);
        if (groups.Count == 0 || groups.Min() < SettlementRules.GroupMinHuts || groups.Max() < SettlementRules.GroupMaxHuts - 1)
            return false;
        if (job.Urgent)
            return true;
        return colony.Clock.Ticks - colony.Layout.LastDistrictOpenedTicks >= SettlementRules.ComfortCooldownDays * TimeConstants.TicksPerDay;
    }

    private static District? NearestDistrict(Colony colony, int x, int y)
    {
        District? best = null;
        int bestDistance = int.MaxValue;
        foreach (District district in colony.Layout.Districts)
        {
            int distance = SettlementPlanner.DistanceToDistrict(colony.Layout, district, x, y, 1, 1);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = district;
            }
        }
        return best;
    }

    /// <summary>
    /// Quelques ancres viables autour d'un noyau parent, à 18–30 cases (réduit sur petite carte) : seize directions, quatre rayons, jamais un secteur imposé.
    /// Une ancre s'appuie sur des lieux de terrain (une fenêtre plate et libre, loin de l'eau et de la roche), pas sur un bruit tiré pour chaque bâtiment.
    /// </summary>
    private static void AddAnchors(PlanningJob job, SitePlanner.Budget budget, DistrictKind kind, District parent)
    {
        Colony colony = job.Owner;
        LocalMap map = colony.Map;
        SettlementLayout layout = colony.Layout;
        LocalSpatialIndex index = colony.Spatial;
        (int pcx, int pcy) = CenterOf(colony, parent);

        int min = kind == DistrictKind.Residential ? SettlementRules.Scaled(map, SettlementRules.SecondaryAnchorMin) : SettlementRules.Scaled(map, 8);
        int max = kind == DistrictKind.Residential ? SettlementRules.Scaled(map, SettlementRules.SecondaryAnchorMax) : SettlementRules.Scaled(map, 40);
        var radii = new List<int>();
        for (int i = 0; i < 4; i++)
            radii.Add(min + (max - min) * i / 3);

        var anchors = new List<(float Score, int X, int Y)>();
        int variation = SettlementRules.HashKey(job.RequestKey);
        for (int d = 0; d < Directions; d++)
        {
            double angle = 2 * Math.PI * d / Directions;
            foreach (int radius in radii)
            {
                budget.Cells--;
                int ax = pcx + (int)Math.Round(Math.Cos(angle) * radius), ay = pcy + (int)Math.Round(Math.Sin(angle) * radius);
                float viability = AnchorViability(colony, map, index, kind, ax, ay);
                if (viability <= 0f)
                    continue;
                float taste = SettlementRules.Taste(layout.Seed, ax, ay, (int)kind, variation);
                anchors.Add((viability + 0.15f * taste, ax, ay));
            }
        }

        // Les meilleures, espacées d'au moins huit cases pour représenter de vrais choix différents ; départage stable (score, puis Y, puis X).
        anchors.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        int chosen = 0;
        var picked = new List<(int X, int Y)>();
        foreach ((float _, int x, int y) in anchors)
        {
            if (chosen >= MaxAnchors)
                break;
            if (picked.Any(p => Math.Max(Math.Abs(p.X - x), Math.Abs(p.Y - y)) < 8))
                continue;
            picked.Add((x, y));
            chosen++;
            job.FrontDistrict.Add(-1);
            job.FrontKind.Add((int)kind);
            job.FrontCx.Add(x);
            job.FrontCy.Add(y);
            job.FrontAnchor.Add(1);
            job.FrontParent.Add(parent.Id);
        }
    }

    /// <summary>
    /// La viabilité d'une ancre, de 0 (inutilisable) à 1 : la part de cases plates, sèches et libres dans une petite fenêtre, la présence de bonne terre ou de forêt
    /// selon la vocation, et l'éloignement des logements pour une industrie lourde.
    /// </summary>
    private static float AnchorViability(Colony colony, LocalMap map, LocalSpatialIndex index, DistrictKind kind, int ax, int ay)
    {
        if (!map.InBounds(ax, ay) || ax < 2 || ay < 2 || ax >= map.Width - 2 || ay >= map.Height - 2)
            return 0f;
        if (!map.IsWalkable(ax, ay) || map.IsWaterway(ax, ay) || map.IsMountain(ax, ay))
            return 0f;
        int elevation = map.GetElevation(ax, ay);
        int open = 0, total = 0, trees = 0, fertile = 0;
        for (int dy = -AnchorWindow; dy <= AnchorWindow; dy++)
        for (int dx = -AnchorWindow; dx <= AnchorWindow; dx++)
        {
            int x = ax + dx, y = ay + dy;
            if (!map.InBounds(x, y))
                continue;
            total++;
            if (!map.IsWalkable(x, y) || map.IsWaterway(x, y) || map.IsMountain(x, y) || map.GetElevation(x, y) != elevation || index.IsSolid(x, y))
                continue;
            FloraType flora = map.GetFlora(x, y);
            if (flora is FloraType.Tree or FloraType.Bush)
            {
                trees++;
                continue;
            }
            open++;
            if (map.GetSoil(x, y) != SoilType.Sand && (map.IsFertileBank(x, y) || map.SoilRichness >= 1f))
                fertile++;
        }
        float openShare = total == 0 ? 0f : open / (float)total;
        if (openShare < 0.35f)
            return 0f;

        float score = openShare;
        if (kind == DistrictKind.Industrial)
        {
            // Une industrie lourde s'écarte des logements ; elle aime la forêt (le bois) et la roche (la carrière).
            int gap = colony.Buildings.Where(b => b.IsHut).Select(b => PlacementChecks.Gap(ax - 1, ay - 1, 2, 2, b.X, b.Y, b.Width, b.Height)).DefaultIfEmpty(99).Min();
            if (gap < SettlementRules.HeavySeparationFloor)
                return 0f;
            score += gap >= SettlementRules.HeavySeparation ? 0.4f : 0f;
            score += 0.2f * Math.Min(1f, trees / 20f);
            if (colony.Quarry is { } quarry)
                score += 0.4f * Math.Max(0f, 1f - Math.Max(Math.Abs(quarry.X - ax), Math.Abs(quarry.Y - ay)) / 30f);
        }
        else if (kind == DistrictKind.Residential)
        {
            score -= 0.2f * Math.Min(1f, trees / 30f);
            score -= 0.2f * Math.Min(1f, fertile / (float)Math.Max(1, open));
        }
        return score;
    }

    /// <summary>La saturation d'un quartier autour d'un point : la part de ses parcelles proches, de 0 à 1 (au-delà de six huttes sur un petit groupe, il est plein).</summary>
    internal static float Saturation(Colony colony, int x, int y, int radius)
    {
        int near = 0;
        foreach (PlotReservation parcel in colony.Layout.ActiveParcels)
            if (parcel.Kind == ParcelKind.Building && Math.Max(Math.Abs(parcel.X - x), Math.Abs(parcel.Y - y)) <= radius)
                near++;
        return Math.Clamp((near - SettlementRules.GroupMinHuts) / (float)(SettlementRules.GroupMaxHuts - SettlementRules.GroupMinHuts + 1), 0f, 1f);
    }
}
