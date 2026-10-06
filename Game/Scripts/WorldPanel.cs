using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.View;

namespace GodColony;

/// <summary>Navigation entre colonies et économie : les valeurs évoluent sans reconstruire les contrôles.</summary>
public partial class WorldPanel : CanvasLayer
{
    public event Action<int>? ColonyRequested;
    public event Action<int>? SettlementRequested;
    public int ObservedSettlementId { get; set; }
    public event Action? FoundingRequested;
    public event Action<int>? SiteRequested;
    private WorldState _world = null!;
    private Control _root = null!;
    private VBoxContainer _stack = null!;
    private HBoxContainer _colonyRow = null!;
    private ScrollContainer _coloniesScroll = null!;
    private readonly List<Button> _colonyButtons = [];
    private EconomyDashboard _economyDashboard = null!;
    private Button _economyButton = null!, _mapButton = null!, _foundingButton = null!, _civilizationButton = null!;
    private PanelContainer _economyCard = null!, _civilizationCard = null!;
    private CivilizationPanel _civilization = null!;
    private Label _civilizationTitle = null!;
    private Label _title = null!;
    private WorldMapView _map = null!;
    private double _sinceRefresh = 1;
    private bool _open, _civilizationOpen, _navigationEnabled = true;
    private bool _stocksVisible = true;
    private int _observed;
    public bool PickingSite
    {
        get => _map.PickingSite;
        set { _map.PickingSite = value; ResizePanels(); if (value) MapOpen = true; }
    }

    /// <summary>Met en avant les régions conseillées sur la carte et recentre sur celle qui est proposée.</summary>
    public void ShowSuggestions(IReadOnlyList<int> tiles, int current)
    {
        _map.Suggestions = tiles; _map.CurrentSuggestion = current;
        if (current >= 0) _map.CenterOn(current);
    }

    public void SetNavigationEnabled(bool enabled)
    {
        _navigationEnabled = enabled;
        _coloniesScroll.Visible = enabled;
        _mapButton.Disabled = !enabled;
        _economyButton.Disabled = !enabled || _world.Colonies.Count == 0;
        _civilizationButton.Disabled = !enabled || _world.Colonies.Count == 0;
        _foundingButton.Disabled = !enabled;
    }

    public bool Open
    {
        get => _open;
        set
        {
            _open = value && _world is not null && _world.Colonies.Count > 0;
            if (_open) { MapOpen = false; _civilizationOpen = false; }
            _sinceRefresh = 1;
            UpdateVisibility();
        }
    }

    /// <summary>Le panneau des savoirs et des relations entre colonies (touche R).</summary>
    public bool CivilizationOpen
    {
        get => _civilizationOpen;
        set
        {
            _civilizationOpen = value && _world is not null && _world.Colonies.Count > 0;
            if (_civilizationOpen) { MapOpen = false; _open = false; }
            _sinceRefresh = 1;
            UpdateVisibility();
        }
    }

    public bool ShowingRelations => _civilization.ShowingRelations;
    public void ShowRelations() { CivilizationOpen = true; _civilization.ShowRelations(); }

    public bool MapOpen
    {
        get => _map?.Visible ?? false;
        set
        {
            if (_map is null) return;
            _map.Visible = value;
            if (value) { _open = false; _civilizationOpen = false; }
            _sinceRefresh = 1;
            UpdateVisibility();
        }
    }

    public void Init(WorldState world) { _world = world; _map?.Init(world); }

    public ResourceHistory? ResourceHistory { get; set; }

    public void ShowResourceGraphs() { Open = true; _economyDashboard.ShowGraphs(); }

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
        _economyButton = Chip("Économie", "Stocks, coûts et commerce de l'empire · E");
        _economyButton.Name = "Economie"; _economyButton.ToggleMode = true;
        _economyButton.Pressed += () => Open = !Open;
        navigation.AddChild(_economyButton);
        _civilizationButton = Chip("Savoirs et relations", "Âge, savoirs, alliances et guerres de l'empire · R");
        _civilizationButton.Name = "Civilisation"; _civilizationButton.ToggleMode = true;
        _civilizationButton.Pressed += () => CivilizationOpen = !CivilizationOpen;
        navigation.AddChild(_civilizationButton);
        _foundingButton = Chip("+ Fonder un empire", "Choisir un peuple, une région et un emplacement de camp");
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
        _map.SettlementClicked += id => { MapOpen = false; SettlementRequested?.Invoke(id); };
        _map.SiteClicked += tile => SiteRequested?.Invoke(tile);
        BuildEconomy();
        BuildCivilization();
        _root.Resized += ResizePanels;
        ResizePanels();
    }

    private void ResizePanels()
    {
        var layout = InterfaceLayout.For(_root.Size, _stocksVisible);
        _stack.OffsetTop = layout.NavigationTop;
        _map.OffsetTop = _economyCard.OffsetTop = _civilizationCard.OffsetTop = layout.ContentTop;
        _map.OffsetRight = PickingSite ? -InterfaceLayout.SideWidth(_root.Size) - 40 : -16;
        _economyCard.OffsetRight = _civilizationCard.OffsetRight = 16 + Math.Clamp(_root.Size.X * 0.49f, 600, 740);
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
        _civilizationCard.Visible = _civilizationOpen;
        _civilizationButton.SetPressedNoSignal(_civilizationOpen);
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
            var button = Chip("", "Observer cet empire · Tab pour passer au suivant");
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
            button.TooltipText = $"{colony.Name} · {colony.Species.Plural}\n{colony.Members.Count} habitants · Humeur {colony.AverageMood * 100:0} %\nObserver cet empire · Tab pour passer au suivant";
            button.SetPressedNoSignal(i == _observed);
        }
        _map.Observed = _observed;
        _economyButton.Disabled = _world.Colonies.Count == 0 || !_navigationEnabled;
        _civilizationButton.Disabled = _economyButton.Disabled;
        _foundingButton.Disabled = !_navigationEnabled || _world.Colonies.Count >= WorldState.MaxPlayerColonies;
        if (_world.Colonies.Count == 0) _open = _civilizationOpen = false;
        UpdateVisibility();
        if (_open) RefreshEconomy(_world.Colonies[_observed]);
        if (_civilizationOpen)
        {
            Colony colony = _world.Colonies[_observed];
            _civilizationTitle.Text = $"Savoirs et relations · {colony.Name}";
            _civilization.Refresh(_world, colony);
        }
    }

    private void BuildCivilization()
    {
        _civilizationCard = new PanelContainer { Name = "PanneauCivilisation", Visible = false, MouseForcePassScrollEvents = false };
        _civilizationCard.AddThemeStyleboxOverride("panel", MenuStyle.Surface(16));
        _root.AddChild(_civilizationCard);
        _civilizationCard.AnchorBottom = 1;
        _civilizationCard.OffsetLeft = 16; _civilizationCard.OffsetBottom = -60;
        var frame = MenuStyle.Column(_civilizationCard, 10);
        var heading = new HBoxContainer();
        frame.AddChild(heading);
        _civilizationTitle = MenuStyle.Text(heading, "Savoirs et relations", 20, ArtDirection.Brass);
        _civilizationTitle.ClipText = true; _civilizationTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _civilizationTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var close = Chip("×", "Fermer · R / Échap");
        close.Name = "FermerCivilisation"; close.Pressed += () => CivilizationOpen = false;
        heading.AddChild(close);
        var scroll = new ScrollContainer
        {
            Name = "DefilementCivilisation", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseForcePassScrollEvents = false,
        };
        frame.AddChild(scroll);
        _civilization = new CivilizationPanel();
        scroll.AddChild(_civilization);
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
        _economyDashboard = new EconomyDashboard();
        _economyDashboard.SettlementRequested += id => { MapOpen = false; Open = false; SettlementRequested?.Invoke(id); };
        scroll.AddChild(_economyDashboard);
    }

    private void RefreshEconomy(Colony colony)
    {
        Settlement place = _world.SettlementById(ObservedSettlementId) is { } selected && selected.Owner == colony ? selected : colony.PrimarySettlement;
        using var scope = place.Observe();
        _title.Text = $"Économie · {place.Name}";
        _economyDashboard.Refresh(_world, colony);
        _economyDashboard.RefreshGraphs(colony, ResourceHistory);
    }
    internal static Button Chip(string text, string hint)
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
