using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Pourquoi un village voudrait envoyer une partie de ses habitants fonder ailleurs. La taille seule n'en est jamais une.</summary>
public enum GrowthReason { None, PersistentHousingPressure, LocalCapacityBlocked, PersistentDiscontent, BetterSettlementOpportunity }

public enum GrowthOptionKind { LocalExpansion, NewSettlement }

/// <summary>Une manière de loger de nouveaux habitants, avec son coût complet par habitant (heures de travail, sur <see cref="ExpansionPlanner.HorizonDays"/> jours). Un résultat temporaire.</summary>
public sealed record GrowthOption(GrowthOptionKind Kind, double CostPerResident, int Residents, int Region, string Note);

/// <summary>La décision de croissance d'un village : la raison principale, celles qui la soutiennent et les coûts comparés. Un résultat temporaire, jamais sauvegardé.</summary>
public sealed record GrowthDecision(GrowthReason Reason, IReadOnlyList<GrowthReason> Supporting, double LocalCostPerResident, double OutsideCostPerResident, int Residents, int Region)
{
    public static readonly GrowthDecision None = new(GrowthReason.None, [], double.PositiveInfinity, double.PositiveInfinity, 0, -1);
}

/// <summary>
/// Les observations quotidiennes d'un établissement qui fondent une décision de croissance : jours consécutifs de manque de logement, de mécontentement et d'impossibilité
/// confirmée de bâtir sur place. Persistées : une décision qui s'appuie sur une fenêtre doit la retrouver après un rechargement.
/// </summary>
public sealed class SettlementGrowthState
{
    public long LastDay { get; internal set; } = -1;
    public int HousingDays { get; internal set; }
    public int DiscontentDays { get; internal set; }
    public int CalmDays { get; internal set; }

    /// <summary>Jours consécutifs où la demande de hutte est restée sans place ou hors budget de trajet, sans événement utile depuis (jamais un budget de recherche épuisé).</summary>
    public int BlockedDays { get; internal set; }
    public GrowthReason LastReason { get; internal set; }
}

/// <summary>
/// La croissance d'un village : on agrandit sur place avant de disperser. Un schisme n'a de raison que durable — manque de place persistant, mécontentement soutenu
/// ou installation extérieure nettement moins chère — jamais le seul nombre d'habitants.
/// </summary>
public static class GrowthPolicy
{
    /// <summary>
    /// Compte, une fois par jour, les faits qui soutiennent une raison. Appelée sous <see cref="Colony.UseSettlement"/>, après les changements quotidiens.
    /// Une recherche en file ou périmée est une attente : elle ne vaut jamais saturation.
    /// </summary>
    internal static void ObserveDaily(WorldState world, Settlement place)
    {
        SettlementGrowthState state = place.GrowthState;
        long day = world.Clock.TotalDays;
        if (state.LastDay == day)
            return;
        state.LastDay = day;
        Colony colony = place.Owner;
        PresentPopulation people = place.Population;
        if (people.Count == 0)
        {
            state.HousingDays = state.DiscontentDays = state.CalmDays = state.BlockedDays = 0;
            return;
        }

        bool housing = HousingNeedUnresolved(colony);
        state.HousingDays = housing ? state.HousingDays + 1 : 0;

        float mood = people.Average(m => m.Needs.Mood);
        if (mood < ScaleRules.DiscontentMood)
        {
            state.DiscontentDays++;
            state.CalmDays = 0;
        }
        else if (mood > ScaleRules.CalmMood && ++state.CalmDays >= ScaleRules.CalmDays)
            state.DiscontentDays = 0;

        PlanRequest? hut = HutRequest(colony);
        if (!housing)
            state.BlockedDays = 0;
        else if (IsConfirmedBlock(hut))
            state.BlockedDays++;
        else if (hut is not { State: PlanningOutcome.Pending })
            state.BlockedDays = 0;
    }

    private static PlanRequest? HutRequest(Colony colony)
    {
        string key = SettlementPlanner.KeyFor(DevelopmentKind.Housing, BuildingType.Hut);
        return colony.Layout.Requests.FirstOrDefault(r => r.Key == key);
    }

    /// <summary>Des sans-abri que ni une hutte en chantier ni une place libre ne couvrent encore.</summary>
    private static bool HousingNeedUnresolved(Colony colony) =>
        colony.Homeless - colony.ConstructionSites.Count(b => b.IsHut) * Building.HutCapacity >= ScaleRules.HousingPressureHomeless;

    /// <summary>Le terrain a dit non (plus de place, ou plus rien dans le budget de trajet) et aucun événement utile n'est survenu depuis.</summary>
    private static bool IsConfirmedBlock(PlanRequest? request) =>
        request is { State: PlanningOutcome.WaitingForChange, Failure: PlacementFailureKind.NoSpace or PlacementFailureKind.TravelBudgetExceeded } waiting
        && (waiting.Seen & waiting.RetryOn) == 0;

    /// <summary>Le coût d'une hutte de plus : matériaux au coût observé et travail de construction (heures).</summary>
    private static double HutHours(Colony colony)
    {
        var hut = new Building(BuildingType.Hut, 0, 0);
        return hut.WoodRequired * Economy.Cost(colony, ResourceType.Wood) + hut.StoneRequired * Economy.Cost(colony, ResourceType.Stone)
            + hut.WorkSeconds * ScaleRules.HoursPerSecond;
    }

    /// <summary>Heures par habitant et par jour que coûte l'éloignement des huttes au cœur du village (trajet quotidien aller-retour au-delà du rayon confortable).</summary>
    private static double CrowdingHoursPerDay(Colony colony)
    {
        var homes = colony.Buildings.Where(b => b.IsHut && b.IsComplete).ToList();
        if (homes.Count == 0)
            return 0;
        double mean = homes.Average(b => Pathfinding.TraversalCost.Octile(b.X - colony.CampX, b.Y - colony.CampY));
        return 2 * Math.Max(0, mean - ScaleRules.ComfortRadius) / SettlementRules.WalkTilesPerSecond * ScaleRules.HoursPerSecond;
    }

    /// <summary>
    /// Compare, pour <paramref name="residents"/> habitants de plus, l'agrandissement du village et une installation ailleurs, sur leur coût complet par habitant.
    /// Une option dont les informations manquent n'existe pas : l'installation n'est envisagée que dans une région que la colonie connaît.
    /// </summary>
    public static IReadOnlyList<GrowthOption> CompareGrowthOptions(WorldState world, Colony colony, int residents)
    {
        residents = Math.Max(1, residents);
        var options = new List<GrowthOption>();
        Settlement home = colony.PrimarySettlement;
        using var scope = colony.UseSettlement(home);
        double hut = HutHours(colony);
        bool blocked = home.GrowthState.BlockedDays >= ScaleRules.BlockedDays;
        double local = blocked ? double.PositiveInfinity
            : hut / Building.HutCapacity + CrowdingHoursPerDay(colony) * ExpansionPlanner.HorizonDays;
        options.Add(new GrowthOption(GrowthOptionKind.LocalExpansion, local, residents, home.RegionTileIndex,
            blocked ? "plus de place sur place" : "huttes dans les quartiers existants"));

        int region = KnownRegion(world, colony);
        if (region >= 0 && ExpansionPlanner.RouteDays(world, colony, home.RegionTileIndex, region) is var days && !double.IsInfinity(days))
        {
            double kit = ExpansionPlanner.InstallationHours(colony, 0) * residents / ExpansionPlanner.CampPeople;
            double storehouse = new Building(BuildingType.Storehouse, 0, 0) is var depot
                ? depot.WoodRequired * Economy.Cost(colony, ResourceType.Wood) + depot.StoneRequired * Economy.Cost(colony, ResourceType.Stone) : 0;
            double total = residents * days * Trade.WorkHoursPerDay + kit + Math.Ceiling(residents / (double)Building.HutCapacity) * hut + storehouse;
            options.Add(new GrowthOption(GrowthOptionKind.NewSettlement, total / residents, residents, region, "nouvelle installation, transport et services compris"));
        }
        return options;
    }

    /// <summary>La région libre connue la plus agréable à moins de <see cref="Schism.MaxDistance"/> cases : une région vue ou sondée par la colonie (-1 sinon).</summary>
    private static int KnownRegion(WorldState world, Colony colony)
    {
        int home = world.WorldMap.TileOf(colony);
        foreach (int tile in world.WorldMap.SuggestTiles(colony.Species, 40))
            if (world.WorldMap.Grid.Distance(home, tile) <= Schism.MaxDistance && (colony.VisitedRegions.Contains(tile) || colony.RegionReach.ContainsKey(tile)))
                return tile;
        return -1;
    }

    /// <summary>
    /// La raison durable, s'il y en a une, qui justifie qu'un groupe quitte le village principal. L'agrandissement local qui avance (recherche en file, proposition prête)
    /// passe avant : un manque de logement qu'il résout n'ouvre aucun schisme.
    /// </summary>
    public static GrowthDecision EvaluateDeparture(WorldState world, Colony colony, int groupSize, bool recordReason = true)
    {
        Settlement home = colony.PrimarySettlement;
        SettlementGrowthState state = home.GrowthState;
        using var scope = colony.UseSettlement(home);
        if (home.Population.Count < Schism.MinCrampedPopulation)
            return GrowthDecision.None;

        PlanRequest? hut = HutRequest(colony);
        bool housing = state.HousingDays >= ScaleRules.HousingPressureDays;
        bool blocked = state.BlockedDays >= ScaleRules.BlockedDays;
        bool discontent = state.DiscontentDays >= ScaleRules.DiscontentDays;
        bool localAdvancing = hut is not { State: PlanningOutcome.WaitingForChange };

        var reasons = new List<GrowthReason>();
        if (housing && blocked) reasons.Add(GrowthReason.LocalCapacityBlocked);
        if (discontent) reasons.Add(GrowthReason.PersistentDiscontent);
        if (housing && !localAdvancing) reasons.Add(GrowthReason.PersistentHousingPressure);

        IReadOnlyList<GrowthOption> options = CompareGrowthOptions(world, colony, groupSize);
        double local = options.First(o => o.Kind == GrowthOptionKind.LocalExpansion).CostPerResident;
        GrowthOption? outside = options.FirstOrDefault(o => o.Kind == GrowthOptionKind.NewSettlement);
        if (outside is not null && outside.CostPerResident <= local * ScaleRules.OpportunityAdvantage)
            reasons.Add(GrowthReason.BetterSettlementOpportunity);

        if (recordReason) state.LastReason = reasons.Count == 0 ? GrowthReason.None : reasons[0];
        return reasons.Count == 0 && recordReason ? GrowthDecision.None
            : new GrowthDecision(reasons.Count == 0 ? GrowthReason.None : reasons[0], reasons, local, outside?.CostPerResident ?? double.PositiveInfinity, groupSize, outside?.Region ?? -1);
    }

    /// <summary>La raison en une phrase, pour la prière.</summary>
    public static string Explain(GrowthDecision decision, int days) => decision.Reason switch
    {
        GrowthReason.LocalCapacityBlocked => $"Depuis {Math.Max(days, ScaleRules.BlockedDays)} jours, nous ne trouvons plus de place pour de nouvelles huttes et des habitants dorment dehors. ",
        GrowthReason.PersistentHousingPressure => $"Depuis {Math.Max(days, ScaleRules.HousingPressureDays)} jours, des habitants dorment dehors sans qu'un toit soit en vue. ",
        GrowthReason.PersistentDiscontent => $"Depuis {Math.Max(days, ScaleRules.DiscontentDays)} jours, l'humeur du village reste sombre. ",
        GrowthReason.BetterSettlementOpportunity => $"Une installation nouvelle reviendrait à {decision.OutsideCostPerResident:0} h par habitant, contre {(double.IsInfinity(decision.LocalCostPerResident) ? "bien plus" : decision.LocalCostPerResident.ToString("0") + " h")} sur place. ",
        _ => "",
    };
}
