using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Stocks illustrés et voyages commerciaux ; actualisation des contrôles conservés.</summary>
public partial class EconomyDashboard : VBoxContainer
{
    private Label _coins = null!, _food = null!, _gain = null!, _context = null!, _grudges = null!, _routeCount = null!, _noRoutes = null!, _noTrades = null!;
    private VBoxContainer _stocks = null!, _commerce = null!, _routes = null!, _trades = null!;
    private GridContainer _goodsGrid = null!;
    private SupplierOverview _suppliers = null!;
    public event Action<int>? SettlementRequested;
    private Label _presence = null!;
    private Label _placeTitle = null!;
    private TerritoryOverview _territory = null!;
    private Button _territoryTab = null!;
    private Button _stocksTab = null!, _commerceTab = null!;
    private HBoxContainer _legend = null!;
    private readonly Dictionary<ResourceType, GoodCard> _goods = [];
    private readonly List<RouteCard> _routeCards = [];
    private readonly List<ExchangeCard> _exchangeCards = [];
    private VBoxContainer _milestones = null!;
    private Button _milestonesTab = null!;
    private readonly Dictionary<string, Label> _milestoneRows = [];
    private Button _graphsTab = null!;
    private ResourceGraphs _graphs = null!;
    private HBoxContainer _metrics = null!;
    private Control _settlementCard = null!;

    public override void _Ready()
    {
        Name = "TableauEconomie";
        Theme = MenuStyle.Theme(); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 14);
        var metrics = _metrics = new HBoxContainer(); metrics.AddThemeConstantOverride("separation", 8); AddChild(metrics);
        _coins = DashboardStyle.Metric(metrics, "Trésorerie", ResourceType.Coins, DashboardStyle.Gold);
        _food = DashboardStyle.Metric(metrics, "Réserves de repas", ResourceType.Bread, DashboardStyle.Mint);
        _gain = DashboardStyle.Metric(metrics, "Bilan des voyages", ResourceType.Tools, DashboardStyle.Mint);
        _gain.TooltipText = "Cumul des bilans réalisés : pièces et biens réellement revenus, moins les biens cédés, les pertes et le temps des voyageurs. Les biens sont valorisés aux coûts connus au départ. Les anciens voyages sans bilan sont exclus.";
        var settlementCard = _settlementCard = DashboardStyle.Card(this, 8);
        var settlementColumn = MenuStyle.Column(settlementCard, 4);
        _placeTitle = DashboardStyle.Text(settlementColumn, "VILLAGE PRINCIPAL", 11, DashboardStyle.Gold);
        _presence = DashboardStyle.Text(settlementColumn, "", 12, DashboardStyle.Ink, true);
        _presence.Name = "PopulationEtablissement";
        _context = DashboardStyle.Text(this, "", 12, DashboardStyle.Muted, true);
        var tabs = DashboardStyle.Row(this);
        _stocksTab = Tab(tabs, "Stocks et besoins", "StocksEconomie", false);
        _commerceTab = Tab(tabs, "Commerce", "CommerceEconomie", true);
        _graphsTab = new Button { Text = "Graphiques", Name = "GraphiquesEconomie", ToggleMode = true,
            CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tabs.AddChild(_graphsTab); _graphsTab.Pressed += ShowGraphs;
        _milestonesTab = new Button { Text = "Jalons", Name = "JalonsEconomie", ToggleMode = true, Icon = ResourceIcons.Get("Milestone"),
            CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tabs.AddChild(_milestonesTab); _milestonesTab.Pressed += ShowMilestones;
        _stocks = MenuStyle.Column(this, 10);
        _legend = DashboardStyle.Row(_stocks);
        DashboardStyle.Text(_legend, "RÉSERVES DE LA COLONIE", 11, DashboardStyle.Gold);
        DashboardStyle.Spacer(_legend);
        DashboardStyle.Pill(_legend, "À compléter", DashboardStyle.Warning);
        DashboardStyle.Pill(_legend, "Couvert", DashboardStyle.Mint);
        _goodsGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _goodsGrid.AddThemeConstantOverride("h_separation", 10); _goodsGrid.AddThemeConstantOverride("v_separation", 10);
        _stocks.AddChild(_goodsGrid);
        foreach (ResourceType good in Economy.Tradable)
        {
            var card = new GoodCard { Good = good };
            _goodsGrid.AddChild(card); _goods.Add(good, card);
        }
        DashboardStyle.Text(_stocks, "Les jauges comparent le stock au besoin actuel. Coût et valeur : heures de travail par unité. ~ indique une estimation.",
            11, DashboardStyle.Muted, true);
        _commerce = MenuStyle.Column(this, 12);
        _routeCount = DashboardStyle.Text(_commerce, "CARAVANES EN ROUTE", 12, DashboardStyle.Gold);
        _noRoutes = DashboardStyle.Text(_commerce, "Aucune caravane en route pour le moment.", 13, DashboardStyle.Muted, true);
        _routes = MenuStyle.Column(_commerce, 8);
        _grudges = DashboardStyle.Text(_commerce, "", 12, DashboardStyle.Warning, true);
        _suppliers = new SupplierOverview(); _commerce.AddChild(_suppliers);
        DashboardStyle.Text(_commerce, "DERNIERS ÉCHANGES", 12, DashboardStyle.Gold);
        _noTrades = DashboardStyle.Text(_commerce, "La colonie n'a pas encore réalisé d'échange.", 13, DashboardStyle.Muted, true);
        _trades = MenuStyle.Column(_commerce, 8);
        for (int i = 0; i < 3; i++)
        {
            var card = new ExchangeCard(); _trades.AddChild(card); _exchangeCards.Add(card);
        }
        _milestones = MenuStyle.Column(this, 9);
        DashboardStyle.Text(_milestones, "HISTOIRE DE LA COLONIE", 12, DashboardStyle.Gold);
        foreach (Milestone milestone in Milestones.All)
            _milestoneRows[milestone.Id] = DashboardStyle.Text(_milestones, milestone.Title, 13, DashboardStyle.Muted, true);
        _territory = new TerritoryOverview(); AddChild(_territory);
        _territory.SettlementRequested += id => SettlementRequested?.Invoke(id);
        _territoryTab = new Button { Text = "Territoires", Name = "OngletTerritoires", ToggleMode = true,
            CustomMinimumSize = new Vector2(0,36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tabs.AddChild(_territoryTab); _territoryTab.Pressed += ShowTerritories;
        _graphs = new ResourceGraphs(); AddChild(_graphs);
        ShowCommerce(false);
        Resized += ResizeDashboard;
        GetViewport().SizeChanged += ResizeDashboard;
        ResizeDashboard();
    }

    private void ResizeDashboard()
    {
        _goodsGrid.Columns = Size.X >= 500 ? 2 : 1;
        bool compact = GetViewportRect().Size.Y < 760;
        _context.Visible = !compact && !_graphs.Visible;
        _legend.Visible = !compact;
        _metrics.Visible = !_graphs.Visible && !_territory.Visible;
        _settlementCard.Visible = !_territory.Visible;
        AddThemeConstantOverride("separation", compact ? 8 : 14);
    }

    private Button Tab(Node parent, string title, string name, bool commerce)
    {
        var button = new Button { Text = title, Name = name, ToggleMode = true, CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += () => ShowCommerce(commerce); parent.AddChild(button); return button;
    }

    public void ShowCommerce(bool commerce)
    {
        _territory.Visible = false; _territoryTab.SetPressedNoSignal(false);
        _graphs.Visible = false; _graphsTab.SetPressedNoSignal(false);
        _milestones.Visible = false; _milestonesTab.SetPressedNoSignal(false);
        _stocks.Visible = !commerce; _commerce.Visible = commerce;
        _stocksTab.SetPressedNoSignal(!commerce); _commerceTab.SetPressedNoSignal(commerce);
        ResizeDashboard();
    }

    public void ShowMilestones()
    {
        _territory.Visible = false; _territoryTab.SetPressedNoSignal(false);
        _graphs.Visible = false; _graphsTab.SetPressedNoSignal(false);
        _stocks.Visible = _commerce.Visible = false; _milestones.Visible = true;
        _stocksTab.SetPressedNoSignal(false); _commerceTab.SetPressedNoSignal(false); _milestonesTab.SetPressedNoSignal(true);
        ResizeDashboard();
    }

    public void ShowGraphs()
    {
        _territory.Visible = false; _territoryTab.SetPressedNoSignal(false);
        _stocks.Visible = _commerce.Visible = _milestones.Visible = false;
        _graphs.Visible = true;
        _stocksTab.SetPressedNoSignal(false); _commerceTab.SetPressedNoSignal(false); _milestonesTab.SetPressedNoSignal(false);
        _graphsTab.SetPressedNoSignal(true);
        ResizeDashboard();
    }

    public void ShowTerritories()
    {
        _stocks.Visible = _commerce.Visible = _graphs.Visible = _milestones.Visible = false;
        _territory.Visible = true; _territoryTab.SetPressedNoSignal(true);
        foreach (Button tab in new[] { _stocksTab, _commerceTab, _graphsTab, _milestonesTab }) tab.SetPressedNoSignal(false);
        ResizeDashboard();
    }

    public void RefreshGraphs(Colony colony, ResourceHistory? history) => _graphs.Refresh(colony, history);

    public void Refresh(WorldState world, Colony colony)
    {
        _territory.Refresh(world, colony);
        _placeTitle.Text = colony.CurrentSettlement == colony.PrimarySettlement ? "VILLAGE PRINCIPAL" : colony.CurrentSettlement.Name;
        foreach (Milestone milestone in Milestones.All)
        {
            bool reached = colony.Achievements.TryGetValue(milestone.Id, out long ticks);
            var when = new GameClock(ticks);
            var label = _milestoneRows[milestone.Id];
            label.Text = reached ? $"★ {milestone.Title}\n   {when.Season} · jour {when.DayOfSeason} · année {when.Year}" : $"○ {milestone.Title} · à atteindre";
            label.AddThemeColorOverride("font_color", reached ? DashboardStyle.Gold : DashboardStyle.Muted);
        }
        _coins.Text = $"{colony.Stock.Get(ResourceType.Coins):N0}";
        _presence.Text = $"{colony.Members.Count} citoyens · {colony.CurrentSettlement.Population.Count} présents · {colony.Members.Count(c => c.TravelId != 0)} en mission";
        int approaching = colony.Members.Count(c => c.TravelId == 0 && c.Transit != TransitState.None);
        if (approaching > 0) _presence.Text += $" · {approaching} en approche";
        _presence.TooltipText = "Les voyageurs restent citoyens. Seuls les habitants présents utilisent les stocks et travaillent dans ce village.";
        float days = (float)(colony.Stock.AvailableNutrition / (Math.Max(1, colony.PresentMembers.Count) * (decimal)Trade.TravelerNutritionPerDay));
        _food.Text = $"{days:0.0} j";
        _food.AddThemeColorOverride("font_color", days < 2 ? DashboardStyle.Warning : DashboardStyle.Mint);
        _gain.Text = $"{colony.LifetimeTradeGainHours:+0;-0;0} h";
        _gain.AddThemeColorOverride("font_color", colony.LifetimeTradeGainHours >= 0 ? DashboardStyle.Mint : DashboardStyle.Warning);
        _context.Text = $"Région : {Specialties.Name(Specialties.NativeOf(colony))}   ·   Objectifs : {Milestones.Reached(colony)} / {Milestones.All.Count}"
            + (Husbandry.Pens(colony) > 0 ? $"\nÉlevage : {colony.Chickens} poules · {colony.Sheep} moutons · {colony.Cows} vaches" : "");
        _context.Text += $"\nBêtes de la région : {HerdRegion(colony)}";
        TooltipText = _context.Text;
        foreach (var (good, card) in _goods) card.Refresh(world, colony);
        _suppliers.Refresh(world, colony);
        _grudges.Text = string.Join("\n", colony.Grudges.OrderByDescending(g => g.Value)
            .Select(g => $"Rancune envers {g.Key.Name} : {g.Value:0.0} · commerce plus coûteux"));
        _grudges.Visible = colony.Grudges.Count > 0;
        Caravan[] trips = world.Caravans.Where(c => c.From == colony || c.To == colony).ToArray();
        _routeCount.Text = $"CARAVANES EN ROUTE · {trips.Length}";
        _noRoutes.Visible = trips.Length == 0;
        while (_routeCards.Count < trips.Length)
        {
            var card = new RouteCard(); _routes.AddChild(card); _routeCards.Add(card);
        }
        for (int i = 0; i < _routeCards.Count; i++)
        {
            _routeCards[i].Visible = i < trips.Length;
            if (i < trips.Length) _routeCards[i].Refresh(trips[i], world.Clock.Ticks);
        }
        TradeRecord[] records = colony.Trades.TakeLast(3).Reverse().ToArray();
        _noTrades.Visible = records.Length == 0;
        for (int i = 0; i < _exchangeCards.Count; i++)
        {
            _exchangeCards[i].Visible = i < records.Length;
            if (i < records.Length) _exchangeCards[i].Refresh(records[i]);
        }
    }

    /// <summary>Quelles bêtes abondent ou manquent dans la région : de quoi savoir quoi acheter et quoi vendre.</summary>
    private static string HerdRegion(Colony colony)
    {
        string Names(Func<float, bool> test) => string.Join(", ", Husbandry.Species
            .Where(s => test(Husbandry.Abundance(colony, s))).Select(s => Trade.GoodName(s, 2)));
        string plenty = Names(a => a >= 1.2f), scarce = Names(a => a < 0.5f);
        return (plenty.Length > 0 ? $"abondantes ({plenty})" : "")
            + (plenty.Length > 0 && scarce.Length > 0 ? " · " : "")
            + (scarce.Length > 0 ? $"rares ({scarce})" : "")
            + (plenty.Length == 0 && scarce.Length == 0 ? "ordinaires" : "");
    }

    private sealed partial class GoodCard : PanelContainer
    {
        public ResourceType Good;
        private Label _stock = null!, _state = null!, _target = null!, _cost = null!, _value = null!, _allocations = null!;
        private ProgressBar _bar = null!;
        public override void _Ready()
        {
            Name = $"StockCard{Good}"; SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddThemeStyleboxOverride("panel", MenuStyle.Box(new Color(0.09f, 0.16f, 0.135f), MenuStyle.Edge, 12));
            var column = MenuStyle.Column(this, 6);
            var heading = DashboardStyle.Row(column, 10);
            DashboardStyle.Icon(heading, Good, 32);
            var name = DashboardStyle.Text(heading, ResourceIcons.Name(Good), 14);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill; name.ClipText = true;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _stock = DashboardStyle.Text(heading, "0", 26, DashboardStyle.Ink); _stock.Name = $"StockValue{Good}";
            _state = DashboardStyle.Text(column, "", 11, DashboardStyle.Muted);
            _bar = DashboardStyle.Bar(column, DashboardStyle.Mint); _bar.Name = $"StockNeed{Good}";
            _target = DashboardStyle.Text(column, "", 11, DashboardStyle.Muted);
            _allocations = DashboardStyle.Text(column, "", 11, DashboardStyle.Gold, true);
            _allocations.Name = $"StockAllocations{Good}";
            var costs = DashboardStyle.Row(column);
            _cost = DashboardStyle.Text(costs, "", 11, DashboardStyle.Muted);
            DashboardStyle.Spacer(costs);
            _value = DashboardStyle.Text(costs, "", 11, DashboardStyle.Gold);
        }
        public void Refresh(WorldState world, Colony colony)
        {
            SupplyForecast forecast = Trade.Forecast(world, colony, Good);
            _allocations.Text = $"Libre {forecast.Available:N0} · réservé {forecast.Reserved:N0} · à l'atelier {forecast.Processing:N0}";
            if (forecast.LocalIncoming > 0) _allocations.Text += $"\nVers le stock : {forecast.LocalIncoming:N0}";
            if (forecast.Fermenting > 0) _allocations.Text += $"\nFermentation prévue sous 5 j : {forecast.Fermenting:N0}";
            if (forecast.Incoming + forecast.Ordered + forecast.InProduction > 0)
                _allocations.Text += $"\nRetour chargé {forecast.Incoming:N0} · commandé {forecast.Ordered:N0} · en fabrication {forecast.InProduction:N0}";
            if (forecast.Shortage > 0 || forecast.LocalIncoming + forecast.Fermenting + forecast.Incoming + forecast.Ordered + forecast.InProduction > 0) _allocations.Text += $"\nÀ acheter : {forecast.PurchaseNeed:N0}";
            int stock = colony.Stock.Get(Good), shortage = forecast.Shortage, surplus = Economy.Surplus(colony, Good);
            float need = Economy.Need(colony, Good);
            _stock.Text = stock.ToString("N0");
            Color color = shortage > 0 ? DashboardStyle.Warning : need > 0 ? DashboardStyle.Mint : DashboardStyle.Muted;
            _state.Text = shortage > 0 ? $"À compléter · manque {shortage:N0}" : surplus > 0 ? $"{surplus:N0} disponibles pour le commerce"
                : need > 0 ? "Besoins couverts" : "Aucun besoin actuel";
            _state.AddThemeColorOverride("font_color", color);
            _bar.Value = need > 0 ? Math.Clamp(forecast.Available / need, 0, 1) : 0;
            DashboardStyle.ColorBar(_bar, color);
            _target.Text = need > 0 ? $"Stock {stock:N0} / besoin {Math.Ceiling(need):N0}" : $"Stock disponible : {stock:N0}";
            string estimate = colony.Labor.HoursPerUnit(Good) is null ? "~" : "";
            double cost = Economy.Cost(colony, Good), value = Economy.Value(colony, Good);
            _cost.Text = $"Coût {estimate}{cost:0.0} h";
            _value.Text = $"Valeur {estimate}{value:0.0} h";
            _value.AddThemeColorOverride("font_color", value > cost * 1.2 ? DashboardStyle.Gold : value < cost * 0.8 ? DashboardStyle.Mint : DashboardStyle.Ink);
            TooltipText = $"{ResourceIcons.Name(Good)}\nStock : {stock:N0} · Besoin actuel : {need:0.##}\nCoût et valeur : heures de travail par unité."
                + (estimate.Length > 0 ? "\nEstimation : cette ressource n'a pas encore été produite." : "\nCoût mesuré par la colonie, déplacements compris.");
        }
    }

    private sealed partial class RouteCard : PanelContainer
    {
        private Label _route = null!, _state = null!, _load = null!, _cargo = null!;
        private ProgressBar _weight = null!;
        private ProgressBar _progress = null!;
        public override void _Ready()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddThemeStyleboxOverride("panel", MenuStyle.Box(new Color(0.09f, 0.16f, 0.135f), MenuStyle.Edge, 12));
            var row = DashboardStyle.Row(this, 14);
            DashboardStyle.Picture(row, AssetLibrary.Get("world/caravan_0.png") ?? ResourceIcons.Get(ResourceType.Wood), 42);
            var column = MenuStyle.Column(row, 6);
            _route = DashboardStyle.Text(column, "", 14, DashboardStyle.Ink, true);
            _state = DashboardStyle.Text(column, "", 12, DashboardStyle.Muted);
            _progress = DashboardStyle.Bar(column, DashboardStyle.Gold);
            _load = DashboardStyle.Text(column, "", 11, DashboardStyle.Mint, true);
            _weight = DashboardStyle.Bar(column, DashboardStyle.Mint, 4);
            _cargo = DashboardStyle.Text(column, "", 11, DashboardStyle.Muted, true);
        }
        public void Refresh(Caravan caravan, long ticks)
        {
            _route.Text = caravan.State == CaravanState.Returning ? $"{caravan.To.Name} → {caravan.From.Name}" : $"{caravan.From.Name} → {caravan.To.Name}";
            double progress = caravan.Progress(ticks);
            _state.Text = $"{(caravan.State == CaravanState.Outbound ? "Aller" : "Retour")} · {progress * 100:0} % · {caravan.Traders.Count} voyageurs";
            _progress.Value = progress;
            if (caravan.Purpose != TerritorialPurpose.Commerce) _state.Text = TerritorialTravel.Label(caravan.Purpose) + " · " + _state.Text;
            bool blocked = caravan.BlockedReason is not null;
            _state.Text = blocked ? caravan.BlockedReason! : caravan.ContactOnly ? "Prise de contact · " + _state.Text
                : caravan.Aborted ? "Voyage interrompu · " + _state.Text : _state.Text;
            _state.AddThemeColorOverride("font_color", blocked ? DashboardStyle.Warning : DashboardStyle.Muted);
            int capacity = caravan.Purpose == TerritorialPurpose.Commerce ? Trade.CapacityOf(caravan.From, caravan.To) : 40 * caravan.Traders.Count;
            double days = Math.Max(0, caravan.ReturnTicks - ticks) / (double)TimeConstants.TicksPerDay;
            decimal foodDays = caravan.Provisions.AvailableNutrition / Math.Max(1, caravan.Traders.Count) / (decimal)Trade.TravelerNutritionPerDay;
            _load.Text = $"Charge {caravan.LoadWeight:0.0} / {capacity} · vivres {foodDays:0.0} j"
                + (blocked ? "\nRetour incertain" : $"\nRetour estimé dans {days:0.0} j");
            _weight.Value = caravan.LoadWeight / capacity;
            _cargo.Text = string.Join(" · ", caravan.Cargo.Where(p => p.Value > 0).Select(p => $"{p.Value} {Trade.GoodName(p.Key, p.Value)}"));
            TooltipText = string.Join("\n", caravan.Plan.Select(l => $"{(l.IsSale ? "Vente" : "Achat")} : {l.Units} {Trade.GoodName(l.Good, l.Units)}"));
        }
    }

    private sealed partial class ExchangeCard : PanelContainer
    {
        private Label _partner = null!, _coins = null!, _outcome = null!;
        private HFlowContainer _lines = null!;
        private string _stamp = "";
        public override void _Ready()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddThemeStyleboxOverride("panel", MenuStyle.Box(new Color(0.09f, 0.16f, 0.135f), MenuStyle.Edge, 12));
            var column = MenuStyle.Column(this, 10); var heading = DashboardStyle.Row(column);
            _partner = DashboardStyle.Text(heading, "", 13, DashboardStyle.Ink, true);
            _partner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _coins = DashboardStyle.Text(heading, "", 16, DashboardStyle.Mint);
            _outcome = DashboardStyle.Text(column, "", 12, DashboardStyle.Muted, true);
            _outcome.Name = "BilanVoyage";
            _lines = new HFlowContainer(); _lines.AddThemeConstantOverride("h_separation", 12); _lines.AddThemeConstantOverride("v_separation", 6);
            column.AddChild(_lines);
        }
        public void Refresh(TradeRecord record)
        {
            _partner.Text = $"{record.Partner} · J{record.Ticks / TimeConstants.TicksPerDay + 1}";
            _coins.Text = $"{record.NetCoins:+0;-0;0} pièces";
            _coins.AddThemeColorOverride("font_color", record.NetCoins >= 0 ? DashboardStyle.Mint : DashboardStyle.Gold);
            _outcome.Visible = record.WeSent;
            _outcome.Text = record.GainHours is { } gain
                ? $"Bilan réalisé {gain:+0.0;-0.0;0} h · coût complet {record.CostHours:0.0} h\nGain annoncé au départ : {record.ExpectedGainHours:0.0} h"
                : "Bilan réalisé inconnu pour cet ancien voyage.";
            _outcome.AddThemeColorOverride("font_color", record.GainHours < 0 ? DashboardStyle.Warning : DashboardStyle.Muted);
            string stamp = $"{record.Ticks}:{record.Partner}:" + string.Join(";", record.Lines.Select(l => $"{l.Good}:{l.Units}:{l.IsSale}"));
            if (stamp == _stamp) return;
            _stamp = stamp;
            foreach (Node child in _lines.GetChildren()) { _lines.RemoveChild(child); child.QueueFree(); }
            foreach (TradeLine line in record.Lines)
            {
                var row = DashboardStyle.Row(_lines, 5); DashboardStyle.Icon(row, line.Good, 20);
                DashboardStyle.Text(row, $"{(line.IsSale ? "Vendu" : "Acheté")} {line.Units} · {ResourceIcons.Name(line.Good)}", 12, DashboardStyle.Muted);
                row.TooltipText = $"{line.UnitPrice:0.##} pièces par unité";
            }
        }
    }
}
