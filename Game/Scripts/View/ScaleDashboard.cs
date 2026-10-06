using System;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Les mesures du village : aucune règle économique n'est recalculée par le panneau.</summary>
public partial class ScaleDashboard : VBoxContainer
{
    private Label _production = null!, _workshops = null!, _services = null!, _growth = null!, _powers = null!, _money = null!, _effects = null!;
    private HBoxContainer _history = null!;
    private string _graphStamp = "";
    private Label _heading = null!, _villageHeading = null!, _powersHeading = null!;
    public string Section { get; set; } = "scale";
    public override void _Ready()
    {
        Name = "BilanEchelle";
        AddThemeConstantOverride("separation", 12);
        _heading = DashboardStyle.Text(this, "BILAN DES ÉCONOMIES D’ÉCHELLE", 15, DashboardStyle.Gold);
        _production = DashboardStyle.Text(this, "", 12, DashboardStyle.Ink, true);
        _workshops = DashboardStyle.Text(this, "", 12, DashboardStyle.Ink, true);
        _history = DashboardStyle.Row(this, 4);
        _villageHeading = DashboardStyle.Text(this, "VILLAGE · SERVICES ET CROISSANCE", 13, DashboardStyle.Gold);
        _services = DashboardStyle.Text(this, "", 12, DashboardStyle.Ink, true);
        _growth = DashboardStyle.Text(this, "", 12, DashboardStyle.Muted, true);
        _powersHeading = DashboardStyle.Text(this, "MONNAIE, OFFRANDES ET POUVOIRS", 13, DashboardStyle.Gold);
        _money = DashboardStyle.Text(this, "", 12, DashboardStyle.Ink, true);
        _effects = DashboardStyle.Text(this, "", 12, DashboardStyle.Mint, true);
        _powers = DashboardStyle.Text(this, "", 12, DashboardStyle.Ink, true);
    }

    public void Refresh(WorldState world, Colony colony)
    {
        Settlement place = colony.CurrentSettlement;
        ScaleSnapshot view = ScaleSnapshot.Of(place);
        _heading.Visible = _production.Visible = _workshops.Visible = _history.Visible = Section == "scale";
        _villageHeading.Visible = _services.Visible = _growth.Visible = Section == "village";
        _powersHeading.Visible = _money.Visible = _powers.Visible = _effects.Visible = Section == "powers";
        static string Goods(System.Collections.Generic.IReadOnlyDictionary<ResourceType, int> goods) =>
            goods.Count == 0 ? "aucune unité mesurée" : string.Join(" · ", goods.Select(p => $"{ResourceCatalog.Name(p.Key)} : {p.Value}"));
        _production.Text = $"Production · {view.WindowDays}/10 jours clos\n{Goods(view.Produced)}\nCombustible : {Goods(view.FuelUsed)}"
            + $"\nPic de charrettes : {view.CartPeak} · ravitaillements différés : {view.SupplyWaits}";
        _workshops.Text = view.Workshops.Count == 0 ? "Aucun four ou moulin achevé." : string.Join("\n\n", view.Workshops.Select(w =>
            $"{Building.NameOf(w.Type)} #{w.BuildingId} · {w.Slots} poste(s) · lot maximal {w.MaxBatch} · {w.Extensions} extension(s) achevée(s)"
            + $"\nUtilisation : {(w.Utilization is { } u ? u.ToString("P0") : "en mesure")} · sortie : {w.OutputWaiting} · attentes : {w.RefusalsPerDay}/jour"
            + $"\nExtension : {Verdict(w.Verdict)}"));
        string graphStamp = $"{place.Id}/{world.Clock.TotalDays}/" + string.Join(";", view.Workshops.Select(w => $"{w.BuildingId}/{w.Slots}/{w.MaxBatch}/{w.Utilization}"));
        if (_graphStamp != graphStamp)
        {
            _graphStamp = graphStamp;
            foreach (Node child in _history.GetChildren()) { _history.RemoveChild(child); child.QueueFree(); }
            foreach (WorkshopView workshop in view.Workshops)
            {
                var column = MenuStyle.Column(_history, 3);
                column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                DashboardStyle.Text(column, $"#{workshop.BuildingId} · utilisation / 10 jours", 11, DashboardStyle.Muted, true);
                if (workshop.Utilization is null) { DashboardStyle.Text(column, "Pas assez de données", 11, DashboardStyle.Muted, true); continue; }
                var bars = DashboardStyle.Row(column, 2);
                foreach (double value in WorkshopCapacity.DailyUtilization(colony, place.Buildings.First(b => b.Id == workshop.BuildingId)))
                {
                    var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = value, ShowPercentage = false,
                        CustomMinimumSize = new Vector2(12, 55), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                        FillMode = (int)ProgressBar.FillModeEnum.BottomToTop, TooltipText = $"Utilisation : {value:P0}" };
                    bar.AddThemeStyleboxOverride("fill", MenuStyle.Box(DashboardStyle.Mint, DashboardStyle.Mint, 2));
                    bars.AddChild(bar);
                }
                DashboardStyle.Text(column, "J−10 → J−1 · 0 à 100 %", 10, DashboardStyle.Muted);
            }
        }
        _services.Text = string.Join("\n", view.Services.Where(s => s.Use != CivicUse.Heal).Select(s =>
            $"{Building.NameOf(CivicServices.TypeOf(s.Use))} : {s.Occupied}/{s.Capacity} places · {s.Served5Days} servis, {s.Denied5Days} refusés / 5 jours · {s.HomesInReach:P0} des logements à portée"
            + (s.Gap == ServiceGap.None ? "" : s.Gap == ServiceGap.Saturated ? "\nNe suffit plus : service saturé." : "\nNe suffit plus : logements hors de portée.")
            + (CivicServices.NeedsAdditionalSite(colony, s.Use) ? " Site supplémentaire demandé." : "")
            + (place.Buildings.Any(b => b.Type == CivicServices.TypeOf(s.Use) && !b.IsComplete) ? " Nouveau site en chantier." : "")));
        SettlementGrowthState state = place.GrowthState;
        _growth.Text = $"Logement manquant : {state.HousingDays} j · mécontentement : {state.DiscontentDays} j · impossibilité confirmée : {state.BlockedDays} j"
            + $"\nExtensions en cours : {view.ExtensionsUnderConstruction} · ouvrages achevés : {view.CompletedBuildings}";
        _growth.Text += $"\nSurface achevée : {view.CompletedBuildingCells} cases · chemins aménagés : {colony.Map.Roads.DirtRoads} cases · sentiers : {colony.Map.Roads.Trails} cases"
            + "\nQuartiers : " + string.Join(" · ", colony.Layout.Districts.Select(d => $"#{d.Id} {DistrictName(d.Kind)}"));
        if (place == colony.PrimarySettlement && Schism.Leader(colony) is { } leader)
        {
            GrowthDecision decision = GrowthPolicy.EvaluateDeparture(world, colony, Schism.Followers(colony, leader).Count, recordReason: false);
            static string Cost(double value) => double.IsInfinity(value) ? "indisponible" : $"{value:0.0} h/habitant";
            _growth.Text += $"\nDépart : {Growth(decision.Reason)} · local : {Cost(decision.LocalCostPerResident)} · extérieur : {Cost(decision.OutsideCostPerResident)}";
            if (decision.Supporting.Count > 0) _growth.Text += "\nMotifs associés : " + string.Join(" · ", decision.Supporting.Select(Growth));
        }
        _money.Text = $"Quota mondial · année {world.Money.Year} : {world.Money.YearCap} pièces · frappées : {world.Money.MintedThisYear} · engagées : {world.Money.CommittedThisYear}"
            + $"\nPart de {colony.Name} : {world.Money.AllowanceOf(colony)} · frappées ou engagées : {world.Money.UsedBy(colony)} · disponibles : {world.Money.RemainingFor(colony)}";
        _money.Text += string.Join("", view.Mints.Select(m => $"\nFrappe #{m.BuildingId} · lot de {m.BatchCoins} pièces · sortie : {m.OutputWaiting} · utilisation : {(m.Utilization is { } use ? use.ToString("P0") : "en mesure")}"));
        _powers.Text = string.Join("\n\n", colony.Wishes.Select(w => $"Souhait #{w.Id} · {WishStatus(w.Status)}"
            + (w.AcceptedTicks is null ? "" : " · accord donné") + $"\n{w.Description}\n{w.Outcome}"));
        _effects.Text = string.Join("\n\n", view.Powers.Select(p => $"{p.Explanation}\nFin : {Date(p.ExpiresTicks)}"
                + (p.Kind == DivineEffectKind.HarvestYield ? $" · {p.PendingPlots} parcelles en attente, {p.ConsumedPlots} consommées, {p.InvalidPlots} invalidées · +{p.UnitsGained} unités" : " · effet seulement au combat")));
        if (_powers.Text.Length == 0) _powers.Text = "Aucun souhait ni pouvoir accordé.";
    }

    internal static string Date(long ticks) { var date = new GameClock(ticks); return $"{date.Season}, jour {date.DayOfSeason}, année {date.Year}"; }
    internal static string WishStatus(DivineWishStatus status) => status switch
    {
        DivineWishStatus.AwaitingResponse => "en attente", DivineWishStatus.Refused => "refusé",
        DivineWishStatus.TargetInvalid => "cible invalide", DivineWishStatus.Fulfilled => "exaucé", _ => "déjà béni",
    };
    private static string Growth(GrowthReason reason) => reason switch { GrowthReason.PersistentHousingPressure => "manque durable de logement", GrowthReason.LocalCapacityBlocked => "capacité locale bloquée", GrowthReason.PersistentDiscontent => "mécontentement durable", GrowthReason.BetterSettlementOpportunity => "installation extérieure moins coûteuse", _ => "aucun départ justifié" };
    private static string DistrictName(DistrictKind kind) => kind switch { DistrictKind.Civic => "services", DistrictKind.Residential => "habitat", DistrictKind.Industrial => "ateliers", _ => "cultures" };
    internal static string Verdict(ExtensionVerdict verdict) => verdict switch
    {
        ExtensionVerdict.Wanted => "souhaitée", ExtensionVerdict.TooLittleUse => "trop peu d’usage", ExtensionVerdict.NoDemand => "pas de demande",
        ExtensionVerdict.NoInputs => "intrants manquants", ExtensionVerdict.SurvivalFirst => "survie d’abord", ExtensionVerdict.PaybackTooLong => "amortissement trop long",
        ExtensionVerdict.MaxReached => "plafond atteint", ExtensionVerdict.NoSite => "pas de site", ExtensionVerdict.UnderConstruction => "chantier en cours", _ => "aucune décision",
    };
}
