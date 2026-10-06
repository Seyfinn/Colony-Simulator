namespace GodColony.Simulation.Colonies;

/// <summary>Un atelier à lots vu par l'interface : capacité réelle, utilisation mesurée, sortie en attente et verdict d'extension.</summary>
public sealed record WorkshopView(int BuildingId, BuildingType Type, int Slots, int MaxBatch, int Extensions, double? Utilization, int OutputWaiting, int RefusalsPerDay, ExtensionVerdict Verdict);

/// <summary>Un raccord à l'étude ou décidé : les extrémités, le flux mesuré et le verdict.</summary>
public sealed record ShortcutView(int A, int B, int TripsInWindow, double AverageSeconds, ShortcutVerdict Verdict, long VerdictDay);

/// <summary>Frappe réelle : taille de recette, occupation mesurée et pièces encore à la sortie physique.</summary>
public sealed record MintWorkshopView(int BuildingId, int BatchCoins, double? Utilization, int OutputWaiting);

/// <summary>
/// L'instantané des mesures d'un établissement pour les panneaux économiques, territoriaux et divins et pour l'équilibrage : production, combustible, utilisation des ateliers, services,
/// raccourcis et pouvoirs. Une lecture seule : rien ici ne décide ni ne mute, et chaque valeur vient du registre persistant.
/// </summary>
public sealed record ScaleSnapshot(int SettlementId, int WindowDays, IReadOnlyDictionary<ResourceType, int> Produced, IReadOnlyDictionary<ResourceType, int> FuelUsed,
    IReadOnlyList<WorkshopView> Workshops, IReadOnlyList<ServiceCoverage> Services, IReadOnlyList<ShortcutView> Shortcuts, IReadOnlyList<DivineEffectView> Powers,
    GrowthReason GrowthReason, int SupplyWaits, int CartPeak)
{
    public IReadOnlyList<MintWorkshopView> Mints { get; init; } = [];
    public int CompletedBuildings { get; init; }
    public int ExtensionsUnderConstruction { get; init; }
    public int CompletedBuildingCells { get; init; }
    /// <summary>Lit les mesures de l'établissement (sa fenêtre de dix jours). Ne fait avancer rien.</summary>
    public static ScaleSnapshot Of(Settlement place)
    {
        Colony colony = place.Owner;
        using var scope = colony.UseSettlement(place);
        SettlementScaleLedger ledger = place.ScaleLedger;
        ScaleDay[] window = ledger.Last(WorkshopCapacity.UtilizationDays).ToArray();
        var produced = new Dictionary<ResourceType, int>();
        var fuel = new Dictionary<ResourceType, int>();
        foreach (ScaleDay day in window)
        {
            foreach ((ResourceType good, int units) in day.Produced) produced[good] = produced.GetValueOrDefault(good) + units;
            foreach ((ResourceType good, int units) in day.FuelUsed) fuel[good] = fuel.GetValueOrDefault(good) + units;
        }
        var workshops = colony.Workshops(BuildingType.Oven).Concat(colony.Workshops(BuildingType.Mill)).OrderBy(b => b.Id).Select(b =>
            new WorkshopView(b.Id, b.Type, WorkshopCapacity.Slots(colony, b), WorkshopCapacity.MaxBatch(colony, b), WorkshopCapacity.CompletedExtensions(colony, b),
                WorkshopCapacity.Utilization(colony, b), b.OutputStock?.Amounts.Values.Sum() ?? 0,
                window.Length == 0 ? 0 : window.Sum(d => d.SlotRefusals.GetValueOrDefault(b.Id)) / window.Length,
                ledger.Verdicts.GetValueOrDefault(b.Id))).ToList();
        var services = Enum.GetValues<CivicUse>().Select(use => CivicServices.Coverage(colony, use)).ToList();
        var shortcuts = place.TravelFlows.Where(f => f.Verdict != ShortcutVerdict.None).OrderBy(f => f.A).ThenBy(f => f.B)
            .Select(f => new ShortcutView(f.A, f.B, f.Trips, f.AverageSeconds, f.Verdict, f.VerdictDay)).ToList();
        return new ScaleSnapshot(place.Id, window.Length, produced, fuel, workshops, services, shortcuts, DivinePowers.Views(colony),
            place.GrowthState.LastReason, place.SupplyWaits.Count, window.Select(d => d.CartPeak).DefaultIfEmpty(0).Max())
        {
            CompletedBuildings = colony.Buildings.Count(b => b.IsComplete),
            ExtensionsUnderConstruction = colony.Buildings.Count(b => b.IsExtension && !b.IsComplete),
            CompletedBuildingCells = VillageMeasures.CompletedBuildingCells(colony),
            Mints = colony.Workshops(BuildingType.Mint).Select(b => new MintWorkshopView(b.Id,
                ExtendedIndustry.Recipes.First(r => r.Workshop == BuildingType.Mint).OutputAmount,
                WorkshopCapacity.Utilization(colony, b), b.OutputUnits(ResourceType.Coins))).ToList(),
        };
    }
}
