using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Établissements physiques et renseignements rapportés : aucune réserve souterraine inconnue n'est affichée.</summary>
public partial class TerritoryOverview : VBoxContainer
{
    public event Action<int>? SettlementRequested;
    private Label _summary = null!;
    private readonly Dictionary<int, Label> _settlements = [];
    private readonly Dictionary<int, Control> _placeCards = [], _siteCards = [];
    private readonly Dictionary<int, Button> _observeButtons = [];
    private readonly Dictionary<int, Label> _deposits = [];
    private VBoxContainer _places = null!, _sites = null!;
    private Label _shortcuts = null!;

    public override void _Ready()
    {
        Name = "TerritoireEconomie"; AddThemeConstantOverride("separation", 12);
        _summary = DashboardStyle.Text(this, "", 13, DashboardStyle.Mint, true);
        DashboardStyle.Text(this, "COLONIES DE L'EMPIRE", 12, DashboardStyle.Gold);
        _places = MenuStyle.Column(this, 8);
        DashboardStyle.Text(this, "GISEMENTS RECONNUS", 12, DashboardStyle.Gold);
        DashboardStyle.Text(this, "Les estimations viennent des prospecteurs revenus. Le sous-sol inconnu reste caché.", 12, DashboardStyle.Muted, true);
        _sites = MenuStyle.Column(this, 8);
        DashboardStyle.Text(this, "RACCOURCIS ÉTUDIÉS · 14 JOURS", 12, DashboardStyle.Gold);
        _shortcuts = DashboardStyle.Text(this, "", 12, DashboardStyle.Ink, true);
    }

    public void Refresh(WorldState world, Colony owner)
    {
        _summary.Text = $"{owner.Settlements.Count(s => s.Status != SettlementStatus.Closed)} colonies · {owner.VisitedRegions.Count} régions reconnues"
            + $"\n{world.Caravans.Count(t => t.From == owner && t.Purpose != TerritorialPurpose.Commerce)} mission(s) territoriale(s)";
        _summary.Text += string.Join("", world.Caravans.Where(t => t.From == owner && t.Purpose != TerritorialPurpose.Commerce)
            .Select(t => $"\n{TerritorialTravel.Label(t.Purpose)} · région {t.TargetRegion}"));
        foreach (Control card in _placeCards.Values) card.Visible = false;
        foreach (Settlement place in owner.Settlements)
        {
            if (!_settlements.TryGetValue(place.Id, out Label? row))
            {
                var card = DashboardStyle.Card(_places, 10);
                var column = MenuStyle.Column(card,8);
                row = DashboardStyle.Text(column, "", 12, DashboardStyle.Ink, true);
                var observe = new Button { Text = "Observer cette colonie", CustomMinimumSize = new Vector2(0,30), Disabled = place.Status == SettlementStatus.Closed };
                int id = place.Id; observe.Pressed += () => SettlementRequested?.Invoke(id); column.AddChild(observe);
                _settlements.Add(place.Id, row);
                _placeCards.Add(place.Id, card); _observeButtons.Add(place.Id, observe);
            }
            _placeCards[place.Id].Visible = true;
            _observeButtons[place.Id].Disabled = place.Status == SettlementStatus.Closed;
            string kind = place.Kind switch { SettlementKind.Camp => "Camp", SettlementKind.Hamlet => "Hameau", _ => "Village" };
            decimal days = place.Stock.AvailableNutrition / Math.Max(1, place.Population.Count) / (decimal)Trade.TravelerNutritionPerDay;
            string stock = string.Join(" · ", Enum.GetValues<ResourceType>().Select(g => new KeyValuePair<ResourceType,int>(g, place.Stock.Get(g))).Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(6)
                .Select(p => $"{p.Value} {ResourceCatalog.Name(p.Key)}"));
            ScaleSnapshot measures = ScaleSnapshot.Of(place);
            row.Text = $"{kind} · région {place.RegionTileIndex} · {place.Population.Count} présents / {place.Residents.Count()} résidents"
                + $"\n{measures.CompletedBuildings} ouvrages · {days:0.0} j de vivres disponibles"
                + $"\n{stock}" + (place.Status == SettlementStatus.Closed ? "\nFermé · terrain et ouvrages conservés" : "");
            row.TooltipText = place.Name + "\n" + string.Join("\n", Enum.GetValues<ResourceType>().Select(g => new KeyValuePair<ResourceType,int>(g, place.Stock.Get(g))).Where(p => p.Value > 0).Select(p => $"{ResourceCatalog.Name(p.Key)} : {p.Value}"));
            var needs = LogisticsPlanner.Needs(world, owner, place);
            row.Text += "\nBesoins : " + (needs.Count == 0 ? "aucun manque signalé" : string.Join("\n", needs.GroupBy(n => (n.Priority, n.Reason)).Select(group =>
                $"{Priority(group.Key.Priority)} · {group.Key.Reason} : " + string.Join(" · ", group.Select(n => n.Good is { } good ? $"{n.Units} {ResourceCatalog.Name(good)}" : $"{n.Nutrition:0.0} de nutrition")))));
            row.TooltipText += "\n" + string.Join("\n", needs.Select(n => $"{Priority(n.Priority)} · {n.Reason} · échéance {ScaleDashboard.Date(n.DueTicks)}"));
            if (owner.VisitedRegions.Contains(place.RegionTileIndex) || owner.RegionReach.ContainsKey(place.RegionTileIndex))
            {
                double routeDays = ExpansionPlanner.KnownRouteDays(world, owner, owner.PrimarySettlement.RegionTileIndex, place.RegionTileIndex);
                row.Text += double.IsInfinity(routeDays) ? "\nAucune route connue." : $"\nRoute connue : {routeDays:0.0} jours de trajet.";
            }
            using (place.Observe())
                row.Text += "\nCoûts de production : " + (measures.Produced.Count == 0 ? "aucune production mesurée" : string.Join(" · ", measures.Produced.Keys.Select(g => $"{ResourceCatalog.Name(g)} {(owner.Labor.HoursPerUnit(g) is null ? "~" : "")}{Economy.Cost(owner, g):0.0} h/u")));
            row.Text += "\nProduction récente : " + (measures.Produced.Count == 0 ? "en mesure" : string.Join(" · ", measures.Produced.Select(p => $"{ResourceCatalog.Name(p.Key)} {p.Value}")));
            foreach (Caravan delivery in world.Caravans.Where(t => t.ToSettlementId == place.Id && t.Purpose == TerritorialPurpose.Supply && !t.Delivered && t.State != CaravanState.Home))
                row.Text += "\nLivraison attendue : " + TransportView.TransportLabel(delivery);
            if (!needs.Any(n => n.Priority == SupplyPriority.Survival))
                foreach (SupplyWaitState wait in owner.Settlements.SelectMany(s => s.SupplyWaits).Where(w => w.DestinationId == place.Id))
                    row.Text += $"\nAttente depuis {ScaleDashboard.Date(wait.FirstTicks)} : {wait.LastCause}";
        }
        foreach (Control card in _siteCards.Values) card.Visible = false;
        foreach (DepositKnowledge known in owner.DepositReports.OrderBy(k => k.Region).ThenBy(k => k.Material))
        {
            if (!_deposits.TryGetValue(known.SiteId, out Label? row))
            {
                var card = DashboardStyle.Card(_sites, 9);
                var content = DashboardStyle.Row(card, 9); DashboardStyle.Icon(content, known.Material, 28);
                row = DashboardStyle.Text(content, "", 12, DashboardStyle.Ink, true);
                row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                _deposits.Add(known.SiteId, row);
                _siteCards.Add(known.SiteId, card);
            }
            _siteCards[known.SiteId].Visible = true;
            string state = known.State switch { DepositObservation.Hint => "Indice", DepositObservation.Surveyed => "Reconnu",
                DepositObservation.Working => "En exploitation", _ => "Épuisé" };
            double age = Math.Max(0, world.Clock.Ticks - known.ObservedTicks) / (double)TimeConstants.TicksPerDay;
            row.Text = $"{ResourceIcons.Name(known.Material)} · région {known.Region} · {state}"
                + $"\n{(known.EstimateMax == 0 && known.State != DepositObservation.Depleted ? "Quantité non estimée" : $"Estimation : {known.EstimateMin}–{known.EstimateMax} unités")}";
            row.Text += $"\nConfiance : {known.Confidence:P0} · observation vieille de {age:0.0} j";
            row.Text += known.SurveyedDepth < 0 ? " · indice de surface" : $" · sondage : niveau {known.SurveyedDepth}";
            row.TooltipText = $"{known.Source} · observation vieille de {age:0.0} j";
        }
        _shortcuts.Text = string.Join("\n", ScaleSnapshot.Of(owner.CurrentSettlement).Shortcuts.Select(s =>
            $"{s.A} ↔ {s.B} · {s.TripsInWindow} trajets · {s.AverageSeconds:0.0} s · {Shortcut(s.Verdict)} · jour {s.VerdictDay}"));
        if (_shortcuts.Text.Length == 0) _shortcuts.Text = "Aucun raccord étudié.";
    }

    private static string Priority(SupplyPriority priority) => priority switch { SupplyPriority.Survival => "urgent", SupplyPriority.Regular => "régulier", SupplyPriority.Tools => "outils", SupplyPriority.Industrial => "industrie", SupplyPriority.Expansion => "construction", _ => "confort" };
    private static string Shortcut(ShortcutVerdict verdict) => verdict switch { ShortcutVerdict.Studying => "en étude", ShortcutVerdict.Accepted => "accepté", ShortcutVerdict.GainTooSmall => "gain trop faible", ShortcutVerdict.PaybackTooLong => "amortissement trop long", ShortcutVerdict.NoCorridor => "pas de corridor", ShortcutVerdict.Stale => "résultat périmé", _ => "aucun verdict" };
}
