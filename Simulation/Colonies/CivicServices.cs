using GodColony.Simulation.Pathfinding;

namespace GodColony.Simulation.Colonies;

/// <summary>Ce qu'un habitant vient chercher dans un bâtiment de service.</summary>
public enum CivicUse { Study, Recover, Heal, Relax }

public enum ServiceGap { None, Saturated, OutOfReach }

/// <summary>Où en est un service dans un établissement : bâtiments, places, occupation et attentes récentes. Un résultat temporaire, jamais sauvegardé.</summary>
public sealed record ServiceCoverage(CivicUse Use, int Sites, int Capacity, int Occupied, int Served5Days, int Denied5Days, double HomesInReach, ServiceGap Gap);

/// <summary>
/// Les services partagés : école, soins et taverne. Un service n'agit que sur des usagers qui peuvent réellement l'atteindre et y être accueillis : places comptées en route comme
/// sur place, trajet plafonné, plusieurs quartiers pour un même bâtiment, et un service saturé ou trop loin motive un bâtiment de plus. Aucun effet n'est donné à distance
/// ni à tous par la simple existence d'un bâtiment.
/// </summary>
public static class CivicServices
{
    public static BuildingType TypeOf(CivicUse use) => use switch
    {
        CivicUse.Study => BuildingType.School,
        CivicUse.Recover or CivicUse.Heal => BuildingType.Infirmary,
        _ => BuildingType.Tavern,
    };

    /// <summary>Places simultanées par bâtiment : huit élèves, quatre patients, un soignant actif, huit clients (valeurs de départ, voir <see cref="ScaleRules"/>).</summary>
    public static int CapacityOf(CivicUse use) => use switch
    {
        CivicUse.Study => ScaleRules.SchoolSeats,
        CivicUse.Recover => ScaleRules.InfirmaryBeds,
        CivicUse.Heal => ScaleRules.HealersPerInfirmary,
        _ => ScaleRules.TavernSeats,
    };

    private static bool Matches(Activity activity, CivicUse use) => use switch
    {
        CivicUse.Study => activity.Kind == ActivityKind.Study,
        CivicUse.Heal => activity.Kind == ActivityKind.Heal,
        _ => activity.Kind == ActivityKind.Relax,
    };

    /// <summary>Les bâtiments achevés de ce service, dans l'ordre stable des identifiants.</summary>
    public static IEnumerable<Building> Sites(Colony colony, CivicUse use) =>
        colony.Buildings.Where(b => b.Type == TypeOf(use) && b.IsComplete && !b.IsExtension).OrderBy(b => b.Id);

    /// <summary>Les engagements d'un bâtiment : ceux qui y sont arrivés comme ceux qui sont en route (une activité réserve sa place dès son départ).</summary>
    public static int Occupancy(Colony colony, Building site, CivicUse use, Activity? ignoring = null) =>
        colony.PresentMembers.Count(m => m.Activity is { } a && a != ignoring && a.Building == site && Matches(a, use));

    /// <summary>Les patients réellement accueillis (installés à l'infirmerie) : seuls eux profitent des soins.</summary>
    public static IEnumerable<Colonist> PatientsAt(Colony colony, Building infirmary) =>
        colony.PresentMembers.Where(m => m.Ailment != Ailment.None && m.Activity is { Kind: ActivityKind.Relax, Started: true } a && a.Building == infirmary);

    /// <summary>Un trajet d'un habitant à un service dépasse-t-il le plafond (en secondes de marche, à vol d'oiseau jusqu'à sa case de service la plus proche) ?</summary>
    private static bool InReach(Colony colony, Building site, int x, int y)
    {
        SettlementLayout layout = colony.Layout;
        float best = float.MaxValue;
        foreach (int cell in SettlementServices.WorkCells(colony, site))
        {
            (int cx, int cy) = layout.Decode(cell);
            best = Math.Min(best, TraversalCost.Octile(cx - x, cy - y));
        }
        return best / SettlementRules.WalkTilesPerSecond <= ScaleRules.ServiceTravelCapSeconds;
    }

    /// <summary>
    /// Les bâtiments où ce colon peut aller maintenant, du plus proche au plus éloigné : accessibles dans le plafond de trajet et avec une place libre. Le chemin réel se
    /// vérifie au départ de l'activité, qui réserve alors sa place ; un service plein ou trop loin n'est jamais proposé.
    /// </summary>
    public static IReadOnlyList<Building> Candidates(Colony colony, Colonist colonist, CivicUse use, Activity? ignoring = null)
    {
        var result = new List<(Building Site, int Distance)>();
        foreach (Building site in Sites(colony, use))
            if (Occupancy(colony, site, use, ignoring) < CapacityOf(use) && InReach(colony, site, colonist.TileX, colonist.TileY))
                result.Add((site, Math.Abs(site.X - colonist.TileX) + Math.Abs(site.Y - colonist.TileY)));
        return result.OrderBy(r => r.Distance).ThenBy(r => r.Site.Id).Select(r => r.Site).Take(3).ToList();
    }

    /// <summary>L'habitant a voulu un service qui existe mais n'a pas pu l'obtenir (plein ou trop loin), ou il l'a obtenu : l'observation du jour.</summary>
    internal static void Note(Colony colony, CivicUse use, bool served)
    {
        ScaleDay today = colony.LocalSettlement.ScaleLedger.Today;
        Dictionary<CivicUse, int> counter = served ? today.ServiceServed : today.ServiceDenied;
        counter[use] = counter.GetValueOrDefault(use) + 1;
    }

    /// <summary>La couverture d'un service : bâtiments, places, occupation, usagers servis ou refusés sur cinq jours, et part des logements à portée.</summary>
    public static ServiceCoverage Coverage(Colony colony, CivicUse use)
    {
        List<Building> sites = Sites(colony, use).ToList();
        ScaleDay[] window = colony.LocalSettlement.ScaleLedger.Last(ScaleRules.ServiceDays).ToArray();
        int served = window.Sum(d => d.ServiceServed.GetValueOrDefault(use)), denied = window.Sum(d => d.ServiceDenied.GetValueOrDefault(use));
        var homes = colony.Buildings.Where(b => b.IsHut && b.IsComplete).ToList();
        double inReach = sites.Count == 0 || homes.Count == 0 ? (sites.Count == 0 ? 0 : 1) : homes.Count(h => sites.Any(s => InReach(colony, s, h.X, h.Y))) / (double)homes.Count;
        ServiceGap gap = sites.Count == 0 ? ServiceGap.None
            : denied >= ScaleRules.ServiceMinDenials && denied >= ScaleRules.ServiceDenialShare * (served + denied) ? ServiceGap.Saturated
            : homes.Count >= ScaleRules.ServiceMinHomes && inReach < ScaleRules.ServiceMinReach ? ServiceGap.OutOfReach
            : ServiceGap.None;
        return new ServiceCoverage(use, sites.Count, sites.Count * CapacityOf(use), sites.Sum(s => Occupancy(colony, s, use)), served, denied, inReach, gap);
    }

    /// <summary>
    /// Un site de plus : le service existe mais sature depuis cinq jours, ou une part durable des logements n'y a pas accès. Aucun bâtiment de ce type n'est déjà en chantier.
    /// Les soins (vitaux) passent avant le confort dans l'ordre des candidats de <see cref="Civic.Candidates"/>.
    /// </summary>
    public static bool NeedsAdditionalSite(Colony colony, CivicUse use) =>
        use != CivicUse.Heal && colony.LocalSettlement.ScaleLedger.Days.Count >= ScaleRules.ServiceDays // cinq jours entiers d'observation
        && !colony.Buildings.Any(b => b.Type == TypeOf(use) && !b.IsComplete) && Sites(colony, use).Any() && Coverage(colony, use).Gap != ServiceGap.None;
}
