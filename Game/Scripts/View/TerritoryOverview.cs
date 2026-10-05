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

    public override void _Ready()
    {
        Name = "TerritoireEconomie"; AddThemeConstantOverride("separation", 12);
        _summary = DashboardStyle.Text(this, "", 13, DashboardStyle.Mint, true);
        DashboardStyle.Text(this, "ÉTABLISSEMENTS DU MÊME PEUPLE", 12, DashboardStyle.Gold);
        _places = MenuStyle.Column(this, 8);
        DashboardStyle.Text(this, "GISEMENTS RECONNUS", 12, DashboardStyle.Gold);
        DashboardStyle.Text(this, "Les estimations viennent des prospecteurs revenus. Le sous-sol inconnu reste caché.", 12, DashboardStyle.Muted, true);
        _sites = MenuStyle.Column(this, 8);
    }

    public void Refresh(WorldState world, Colony owner)
    {
        _summary.Text = $"{owner.Settlements.Count(s => s.Status != SettlementStatus.Closed)} établissements · {owner.VisitedRegions.Count} régions reconnues"
            + $"\n{world.Caravans.Count(t => t.From == owner && t.Purpose != TerritorialPurpose.Commerce)} mission(s) de prospection ou de ravitaillement";
        foreach (Control card in _placeCards.Values) card.Visible = false;
        foreach (Settlement place in owner.Settlements)
        {
            if (!_settlements.TryGetValue(place.Id, out Label? row))
            {
                var card = DashboardStyle.Card(_places, 10);
                var column = MenuStyle.Column(card,8);
                row = DashboardStyle.Text(column, "", 12, DashboardStyle.Ink, true);
                var observe = new Button { Text = "Observer ce lieu", CustomMinimumSize = new Vector2(0,30), Disabled = place.Status == SettlementStatus.Closed };
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
            row.Text = $"{kind} · région {place.RegionTileIndex} · {place.Population.Count} présents / {place.Residents.Count()} résidents"
                + $"\n{place.Buildings.Count(b => b.IsComplete)} ouvrages · {days:0.0} j de vivres disponibles"
                + $"\n{stock}" + (place.Status == SettlementStatus.Closed ? "\nFermé · terrain et ouvrages conservés" : "");
            row.TooltipText = place.Name + "\n" + string.Join("\n", Enum.GetValues<ResourceType>().Select(g => new KeyValuePair<ResourceType,int>(g, place.Stock.Get(g))).Where(p => p.Value > 0).Select(p => $"{ResourceCatalog.Name(p.Key)} : {p.Value}"));
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
                + $"\n{(known.EstimateMax == 0 && known.State != DepositObservation.Depleted ? "Source renouvelable à débit limité" : $"Estimation : {known.EstimateMin}–{known.EstimateMax} unités")}";
            row.TooltipText = $"{known.Source} · observation vieille de {age:0.0} j";
        }
    }
}
