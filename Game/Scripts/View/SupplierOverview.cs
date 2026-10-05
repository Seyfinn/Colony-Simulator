using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Les renseignements rapportés par les voyageurs, sans lire les stocks cachés des voisins.</summary>
public partial class SupplierOverview : VBoxContainer
{
    private Label _empty = null!, _orders = null!;
    private readonly List<SupplierCard> _cards = [];
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        DashboardStyle.Text(this, "FOURNISSEURS CONNUS", 12, DashboardStyle.Gold);
        _empty = DashboardStyle.Text(this, "Les marchands rapporteront les premières offres après une rencontre.", 12, DashboardStyle.Muted, true);
        _orders = DashboardStyle.Text(this, "", 12, DashboardStyle.Mint, true);
        _orders.Name = "CommandesCommerce";
    }

    public void Refresh(WorldState world, Colony colony)
    {
        SupplierMemory[] suppliers = Trade.Suppliers(world, colony).OrderBy(m => m.Supplier.Name).ToArray();
        _empty.Visible = suppliers.Length == 0;
        TradeCommitment[] orders = Trade.Commitments(world, colony).ToArray();
        _orders.Visible = orders.Length > 0;
        _orders.Text = string.Join("\n", orders.GroupBy(o => (o.Good, o.Confirmed)).Select(g =>
            $"{(g.Key.Confirmed ? "Chargé au retour" : "Commandé, à confirmer")} : {g.Sum(o => o.Units)} {Trade.GoodName(g.Key.Good)}"));
        while (_cards.Count < suppliers.Length)
        {
            var card = new SupplierCard(); AddChild(card); _cards.Add(card);
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].Visible = i < suppliers.Length;
            if (i < suppliers.Length) _cards[i].Refresh(suppliers[i], world.Clock.Ticks);
        }
    }

    private sealed partial class SupplierCard : PanelContainer
    {
        private Label _title = null!, _date = null!, _record = null!, _offers = null!;
        private ProgressBar _confidence = null!;
        public override void _Ready()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddThemeStyleboxOverride("panel", MenuStyle.Box(DashboardStyle.Surface, MenuStyle.Edge, 10));
            var column = MenuStyle.Column(this, 6);
            var heading = DashboardStyle.Row(column, 8);
            DashboardStyle.Icon(heading, ResourceType.Coins, 24);
            _title = DashboardStyle.Text(heading, "", 14, DashboardStyle.Ink, true);
            _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _date = DashboardStyle.Text(column, "", 11, DashboardStyle.Gold, true);
            _confidence = DashboardStyle.Bar(column, DashboardStyle.Mint, 4);
            _record = DashboardStyle.Text(column, "", 11, DashboardStyle.Muted, true);
            _offers = DashboardStyle.Text(column, "", 12, DashboardStyle.Ink, true);
        }

        public void Refresh(SupplierMemory memory, long ticks)
        {
            _title.Text = memory.Supplier.Name;
            double age = memory.AgeDays(ticks), confidence = memory.Confidence(ticks);
            _date.Text = $"Offres observées il y a {age:0.0} j · confiance {confidence:P0}"
                + (age > Trade.OfferLifetimeDays ? " · à renouveler" : "");
            _confidence.Value = confidence;
            _record.Text = $"{memory.Deliveries} livraison(s) tenue(s) · {memory.Refusals} incomplète(s) · {memory.Delays} retard(s)";
            if (memory.LastTripDays > 0) _record.Text += $"\nDernier voyage : {memory.LastTripDays:0.0} j · coût complet {memory.LastCostHours:0} h";
            TradeRecord? last = memory.Observer.Trades.LastOrDefault(r => r.WeSent && r.Partner == memory.Supplier.Name);
            if (last?.GainHours is { } gain)
                _record.Text += $"\nBilan réalisé {gain:+0.0;-0.0;0} h · annoncé {last.ExpectedGainHours:0.0} h";
            var offers = memory.Offers.Where(o => o.Available > 0).OrderByDescending(o => o.Available).Take(4);
            _offers.Text = string.Join("\n", offers.Select(o => $"{ResourceIcons.Name(o.Good)} : {o.Available} proposés · ~{o.SellPrice:0.0} pièces/u"));
            if (_offers.Text.Length == 0) _offers.Text = "Aucun surplus proposé lors de la dernière rencontre.";
            TooltipText = "Informations datées, rapportées par les voyageurs. Les quantités et paiements sont revérifiés sur place.\n"
                + string.Join("\n", memory.Offers.Where(o => o.Wanted > 0).Select(o => $"Recherche {o.Wanted} {Trade.GoodName(o.Good)} · ~{o.BuyPrice:0.0} pièces/u"));
        }
    }
}
