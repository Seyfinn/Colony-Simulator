using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using GodColony.View;

namespace GodColony;

/// <summary>Navigation entre colonies et économie : les valeurs évoluent sans reconstruire les contrôles.</summary>
public partial class WorldPanel : CanvasLayer
{
    public event Action<int>? ColonyRequested;
    public event Action? FoundingRequested;
    public event Action<int>? SiteRequested;
    private WorldState _world = null!;
    private Control _root = null!;
    private VBoxContainer _stack = null!;
    private HBoxContainer _colonyRow = null!;
    private ScrollContainer _coloniesScroll = null!;
    private readonly List<Button> _colonyButtons = [];
    private readonly Dictionary<ResourceType, (Label Stock, Label Cost, Label Value)> _goods = [];
    private Button _economyButton = null!, _mapButton = null!, _foundingButton = null!;
    private PanelContainer _economyCard = null!;
    private Label _title = null!, _summary = null!, _grudges = null!, _caravans = null!, _trades = null!;
    private WorldMapView _map = null!;
    private double _sinceRefresh = 1;
    private bool _open, _navigationEnabled = true;
    private bool _stocksVisible = true;
    private int _observed;
    public bool PickingSite
    {
        get => _map.PickingSite;
        set { _map.PickingSite = value; ResizePanels(); if (value) MapOpen = true; }
    }

    public void SetNavigationEnabled(bool enabled)
    {
        _navigationEnabled = enabled;
        _coloniesScroll.Visible = enabled;
        _mapButton.Disabled = !enabled;
        _economyButton.Disabled = !enabled || _world.Colonies.Count == 0;
        _foundingButton.Disabled = !enabled;
    }

    public bool Open
    {
        get => _open;
        set
        {
            _open = value && _world is not null && _world.Colonies.Count > 0;
            if (_open) MapOpen = false;
            _sinceRefresh = 1;
            UpdateVisibility();
        }
    }

    public bool MapOpen
    {
        get => _map?.Visible ?? false;
        set
        {
            if (_map is null) return;
            _map.Visible = value;
            if (value) _open = false;
            _sinceRefresh = 1;
            UpdateVisibility();
        }
    }

    public void Init(WorldState world) { _world = world; _map?.Init(world); }

    public override void _Ready()
    {
        Layer = 11;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = MenuStyle.Theme() };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 8);
        _root.AddChild(_stack);
        _stack.AnchorRight = 1;
        _stack.OffsetLeft = 16; _stack.OffsetRight = -16;

        var navigation = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        navigation.AddThemeConstantOverride("separation", 8);
        _stack.AddChild(navigation);
        _mapButton = Chip("Carte du monde", "Ouvrir / fermer la carte du monde · M");
        _mapButton.Name = "CarteMonde"; _mapButton.ToggleMode = true;
        _mapButton.Pressed += () => MapOpen = !MapOpen;
        navigation.AddChild(_mapButton);
        _economyButton = Chip("Économie", "Stocks, coûts et commerce de la colonie · E");
        _economyButton.Name = "Economie"; _economyButton.ToggleMode = true;
        _economyButton.Pressed += () => Open = !Open;
        navigation.AddChild(_economyButton);
        _foundingButton = Chip("+ Fonder une colonie", "Choisir un peuple, une région et un emplacement de camp");
        _foundingButton.Name = "FonderColonie";
        _foundingButton.Pressed += () => FoundingRequested?.Invoke();
        navigation.AddChild(_foundingButton);

        _coloniesScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 40), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseForcePassScrollEvents = false,
        };
        _stack.AddChild(_coloniesScroll);
        _colonyRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _colonyRow.AddThemeConstantOverride("separation", 6);
        _coloniesScroll.AddChild(_colonyRow);

        _map = new WorldMapView { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _root.AddChild(_map);
        _map.AnchorRight = _map.AnchorBottom = 1;
        _map.OffsetLeft = 16; _map.OffsetBottom = -60;
        if (_world is not null) _map.Init(_world);
        _map.ColonyClicked += index => { MapOpen = false; ColonyRequested?.Invoke(index); };
        _map.SiteClicked += tile => SiteRequested?.Invoke(tile);
        BuildEconomy();
        _root.Resized += ResizePanels;
        ResizePanels();
    }

    private void ResizePanels()
    {
        var layout = InterfaceLayout.For(_root.Size, _stocksVisible);
        _stack.OffsetTop = layout.NavigationTop;
        _map.OffsetTop = _economyCard.OffsetTop = layout.ContentTop;
        _map.OffsetRight = PickingSite ? -InterfaceLayout.SideWidth(_root.Size) - 40 : -16;
        _economyCard.OffsetRight = 16 + Math.Clamp(_root.Size.X * 0.30f, 400, 460);
    }

    public void SetStocksVisible(bool visible)
    {
        if (_stocksVisible == visible) return;
        _stocksVisible = visible; ResizePanels();
    }

    private void UpdateVisibility()
    {
        if (_economyCard is null) return;
        _economyCard.Visible = _open;
        _economyButton.SetPressedNoSignal(_open);
        _mapButton.SetPressedNoSignal(MapOpen);
    }

    public int Observed { get => _observed; set { _observed = value; _sinceRefresh = 1; } }

    public override void _Process(double delta)
    {
        if (_world is null) return;
        _sinceRefresh += delta;
        if (_sinceRefresh < 0.4) return;
        _sinceRefresh = 0;
        Refresh();
    }

    private void Refresh()
    {
        while (_colonyButtons.Count < _world.Colonies.Count)
        {
            int index = _colonyButtons.Count;
            var button = Chip("", "Observer cette colonie · Tab pour passer à la suivante");
            button.Name = $"Colonie{index}"; button.ToggleMode = true;
            button.CustomMinimumSize = new Vector2(120, 30);
            button.Pressed += () => { MapOpen = false; ColonyRequested?.Invoke(index); };
            _colonyRow.AddChild(button); _colonyButtons.Add(button);
        }
        for (int i = 0; i < _colonyButtons.Count; i++)
        {
            Colony colony = _world.Colonies[i];
            var button = _colonyButtons[i];
            button.Text = $"{colony.Name} · {colony.Members.Count}";
            button.TooltipText = $"{colony.Name} · {colony.Species.Plural}\n{colony.Members.Count} habitants · Humeur {colony.AverageMood * 100:0} %\nObserver cette colonie · Tab pour passer à la suivante";
            button.SetPressedNoSignal(i == _observed);
        }
        _map.Observed = _observed;
        _economyButton.Disabled = _world.Colonies.Count == 0 || !_navigationEnabled;
        _foundingButton.Disabled = !_navigationEnabled || _world.Colonies.Count >= WorldState.MaxPlayerColonies;
        if (_world.Colonies.Count == 0) _open = false;
        UpdateVisibility();
        if (_open) RefreshEconomy(_world.Colonies[_observed]);
    }

    private void BuildEconomy()
    {
        _economyCard = new PanelContainer { Name = "PanneauEconomie", Visible = false, MouseForcePassScrollEvents = false };
        _economyCard.AddThemeStyleboxOverride("panel", MenuStyle.Surface(16));
        _root.AddChild(_economyCard);
        _economyCard.AnchorBottom = 1;
        _economyCard.OffsetLeft = 16; _economyCard.OffsetBottom = -60;
        var frame = MenuStyle.Column(_economyCard, 10);
        var heading = new HBoxContainer();
        frame.AddChild(heading);
        _title = MenuStyle.Text(heading, "Économie", 20, ArtDirection.Brass);
        _title.ClipText = true; _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var close = Chip("×", "Fermer l'économie · E / Échap");
        close.Name = "FermerEconomie"; close.Pressed += () => Open = false;
        heading.AddChild(close);
        var scroll = new ScrollContainer
        {
            Name = "DefilementEconomie", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseForcePassScrollEvents = false,
        };
        frame.AddChild(scroll);
        var column = MenuStyle.Column(scroll, 12);
        _summary = MenuStyle.Text(column, "", 13, MenuStyle.Muted, true);
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 9);
        column.AddChild(grid);
        foreach (string header in new[] { "RESSOURCE", "STOCK", "COÛT¹", "VALEUR¹" })
            MenuStyle.Text(grid, header, 10, MenuStyle.Muted);
        foreach (ResourceType good in Economy.Tradable)
        {
            var name = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            name.AddThemeConstantOverride("separation", 8);
            grid.AddChild(name);
            name.AddChild(new TextureRect
            {
                Texture = ResourceIcons.Get(good), CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            MenuStyle.Text(name, good == ResourceType.IronOre ? "Minerai" : ResourceIcons.Name(good), 13);
            _goods[good] = (Number(grid), Number(grid), Number(grid));
        }
        MenuStyle.Text(column, "¹ Heures de travail par unité. ~ indique une estimation.\nLa valeur reflète les besoins actuels de la colonie.", 11, MenuStyle.Muted, true);
        column.AddChild(new HSeparator());
        _grudges = MenuStyle.Text(column, "", 12, ArtDirection.Brass, true);
        MenuStyle.Text(column, "CARAVANES EN ROUTE", 11, ArtDirection.Brass);
        _caravans = MenuStyle.Text(column, "", 13, MenuStyle.Ink, true);
        column.AddChild(new HSeparator());
        MenuStyle.Text(column, "DERNIERS ÉCHANGES", 11, ArtDirection.Brass);
        _trades = MenuStyle.Text(column, "", 12, MenuStyle.Muted, true);
    }

    private static Label Number(Node parent)
    {
        var label = MenuStyle.Text(parent, "0", 13);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    private void RefreshEconomy(Colony colony)
    {
        _title.Text = $"Économie · {colony.Name}";
        _summary.Text = $"{colony.Stock.Get(ResourceType.Coins):N0} pièces\nTravail épargné grâce au commerce : {colony.LifetimeTradeGainHours:0} h";
        foreach (var (good, labels) in _goods)
        {
            double cost = Economy.Cost(colony, good), value = Economy.Value(colony, good);
            bool known = colony.Labor.HoursPerUnit(good) is not null;
            labels.Stock.Text = colony.Stock.Get(good).ToString("N0");
            labels.Cost.Text = known ? $"{cost:0.0}" : $"~{cost:0.0}";
            labels.Value.Text = $"{value:0.0}";
            labels.Value.AddThemeColorOverride("font_color", value > cost * 1.2 ? ArtDirection.Brass : value < cost * 0.8 ? MenuStyle.Mint : MenuStyle.Ink);
        }
        _grudges.Text = string.Join("\n", colony.Grudges.OrderByDescending(g => g.Value)
            .Select(g => $"Rancune envers {g.Key.Name} : {g.Value:0.0} · commerce plus coûteux"));
        _grudges.Visible = colony.Grudges.Count > 0;
        _caravans.Text = _world.Caravans.Count == 0 ? "Aucune caravane en route pour le moment."
            : string.Join("\n\n", _world.Caravans.Select(c => $"{c.From.Name} → {c.To.Name}\n{(c.State == CaravanState.Outbound ? "Aller" : "Retour")} · {c.Progress(_world.Clock.Ticks) * 100:0} %"));
        var records = colony.Trades.TakeLast(3).Reverse();
        _trades.Text = string.Join("\n\n", records.Select(r =>
            $"J{r.Ticks / TimeConstants.TicksPerDay + 1} · {r.Partner} · {r.NetCoins:+0;-0;0} pièces\n" +
            string.Join(", ", r.Lines.Select(l => $"{(l.IsSale ? "Vend" : "Achète")} {l.Units} {Trade.GoodName(l.Good, l.Units)}"))));
        if (_trades.Text.Length == 0) _trades.Text = "La colonie n'a pas encore réalisé d'échange.";
    }

    private static Button Chip(string text, string hint)
    {
        var button = new Button
        {
            Text = text, TooltipText = hint, CustomMinimumSize = new Vector2(0, 32),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            bool active = state.Contains("pressed");
            var box = MenuStyle.Box(active ? new Color(0.19f, 0.25f, 0.17f, 0.99f) : MenuStyle.Background,
                active ? ArtDirection.Brass : state == "hover" ? MenuStyle.Mint : MenuStyle.Edge, 10);
            box.ContentMarginTop = box.ContentMarginBottom = 4;
            button.AddThemeStyleboxOverride(state, box);
        }
        return button;
    }
}
