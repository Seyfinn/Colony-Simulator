using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.View;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony;

/// <summary>Interface d'observation : stocks illustrés, journal, travail et fiche individuelle.</summary>
public partial class Hud : CanvasLayer
{
    private static readonly Color Ink = Color.Color8(230, 237, 221);
    private static readonly Color Muted = Color.Color8(150, 174, 162);
    private static readonly Color Gold = Color.Color8(226, 190, 119);
    private static readonly Color Mint = Color.Color8(133, 198, 167);
    private static readonly Color Background = new(0.065f, 0.115f, 0.105f, 0.97f);
    private static readonly Color Border = Color.Color8(65, 89, 75);
    public event Action<GameSpeed>? SpeedRequested;
    public event Action? PauseRequested;
    public event Action? SelectionClosed;
    public event Action? MenuRequested;
    public event Action? RecenterRequested;
    public event Action? HelpRequested;
    public event Action<Colonist, string, string>? ColonistRenameRequested;

    private Control _root = null!;
    private Label _colonyName = null!, _colonyMeta = null!, _calendar = null!, _hour = null!, _tileInfo = null!;
    private readonly Dictionary<ResourceType, Label> _stocks = [];
    private readonly Dictionary<ResourceType, PanelContainer> _resourceCards = [];
    private readonly Dictionary<ResourceType, Label> _foodDetails = [];
    private PanelContainer _foodDropdown = null!;
    private static readonly ResourceType[] FoodResources = [ResourceType.Food, ResourceType.Fish, ResourceType.Eggs, ResourceType.Milk, ResourceType.Meat, ResourceType.SaltedMeat,
        ResourceType.Cake, ResourceType.Stew, ResourceType.Grain, ResourceType.Bread, ResourceType.Flour, ResourceType.Grapes];

    /// <summary>Marchandises de l'élevage et du négoce : elles ont leur place dans l'écran Économie plutôt que dans le bandeau des stocks.</summary>
    private static readonly ResourceType[] TradeGoods = [ResourceType.Wool, ResourceType.Clothes, ResourceType.Salt, ResourceType.Spices, ResourceType.Hardwood,
        ResourceType.Chickens, ResourceType.Sheep, ResourceType.Cows];
    private readonly Dictionary<GameSpeed, Button> _speedButtons = [];
    private Button _pause = null!, _journalTab = null!, _workTab = null!, _collapse = null!, _recenter = null!;
    private PanelContainer _tray = null!, _help = null!, _colonistPanel = null!;
    private GridContainer _resourceRow = null!;
    private ScrollContainer _journalBody = null!, _workBody = null!;
    private bool _trayExpanded = true, _showWork;
    private bool _mapOverlay, _economyOverlay;
    private readonly List<(Label Date, Label Message)> _thoughtRows = [];

    private ProductionDashboard _productionDashboard = null!;
    private long _shownTicks;
    private string _thoughtStamp = "";

    private TextureRect _portrait = null!;
    private ColonistAppearance? _portraitAppearance;
    private Label _colonistName = null!, _colonistActivity = null!, _colonistSector = null!;
    private Label _colonistAge = null!, _colonistFamily = null!, _colonistTraits = null!, _colonistRelations = null!;
    private Colonist? _shownColonist;
    private ScrollContainer _colonistScroll = null!;
    private VBoxContainer _nameEditor = null!;
    private LineEdit _firstNameEdit = null!, _surnameEdit = null!;
    private Button _rename = null!, _confirmRename = null!;
    private readonly Dictionary<SkillType, Label> _skills = [];
    private NeedBar _food = null!, _rest = null!, _leisure = null!, _social = null!, _comfort = null!, _mood = null!;

    public override void _Ready()
    {
        Layer = 10;
        _root = new Control { Name = "Interface", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.Theme = MenuStyle.Theme();
        _root.Theme.SetStylebox("panel", "TooltipPanel", Style(Background, Gold, 6, 10));
        _root.Theme.SetColor("font_color", "TooltipLabel", Ink);
        _root.Theme.SetFontSize("font_size", "TooltipLabel", 13);
        BuildHeader();
        BuildResources();
        BuildJournal();
        BuildInspector();
        BuildFooter();
        _root.Resized += ResizePanels;
        ResizePanels();
    }

    public override void _Input(InputEvent @event)
    {
        if (_foodDropdown.Visible && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            CloseFoodDetails();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_foodDropdown.Visible && @event is InputEventMouseButton { Pressed: true } mouse
            && !_foodDropdown.GetGlobalRect().HasPoint(mouse.Position)
            && !_resourceCards[ResourceType.Food].GetGlobalRect().HasPoint(mouse.Position))
            CloseFoodDetails();
        if (_nameEditor.IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            CancelRename();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildHeader()
    {
        var panel = Panel();
        panel.Name = "Bandeau";
        _root.AddChild(panel);
        Place(panel, 0, 0, 1, 0, 16, 16, -16, 86);
        var row = Row(panel, 20);
        var badge = Text(row, "GC", 22, Gold);
        badge.CustomMinimumSize = new Vector2(42, 0);
        var identity = Column(row, 1);
        identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _colonyName = Text(identity, "Première colonie", 21, Ink);
        _colonyName.ClipText = true;
        _colonyName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _colonyMeta = Text(identity, "", 12, Muted);
        _colonyMeta.ClipText = true;
        _colonyMeta.MouseFilter = Control.MouseFilterEnum.Pass;
        _colonyMeta.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var date = Column(row, 0);
        date.CustomMinimumSize = new Vector2(190, 0);
        _calendar = Text(date, "", 14, Gold);
        _hour = Text(date, "", 12, Muted);
        var speeds = Row(row, 4);
        _pause = Button(speeds, "Ⅱ", "Mettre en pause / reprendre · Espace", 42);
        _pause.Name = "Pause";
        _pause.ToggleMode = true;
        _pause.Pressed += () => PauseRequested?.Invoke();
        foreach (var (speed, caption, hint) in new[]
        {
            (GameSpeed.Observation, "×1", "Observation · touche 1"),
            (GameSpeed.Rapide, "×4", "Rapide · touche 2"),
            (GameSpeed.TresRapide, "×30", "Très rapide · touche 3"),
            (GameSpeed.Fulgurante, "×200", "Fulgurante · touche 4\nLa carte se met en veille : seuls les chiffres et les courbes des colonies restent à l'écran, pour voir passer les années."),
        })
        {
            var button = Button(speeds, caption, hint, speed == GameSpeed.Fulgurante ? 56 : 48);
            button.Name = $"Speed{(int)speed}";
            button.ToggleMode = true;
            button.Pressed += () => SpeedRequested?.Invoke(speed);
            _speedButtons[speed] = button;
        }
        var menu = Button(row, "Menu", "Menu et paramètres · Échap", 70);
        menu.Name = "MenuJeu";
        menu.Pressed += () => MenuRequested?.Invoke();
    }

    private void BuildResources()
    {
        var row = new GridContainer { Columns = 8, MouseFilter = Control.MouseFilterEnum.Ignore };
        _resourceRow = row;
        _root.AddChild(row);
        Place(row, 0, 0, 1, 0, 16, 98, -16, 168);
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 6);
        foreach (ResourceType type in Enum.GetValues<ResourceType>())
        {
            if ((int)type >= 27 || (type != ResourceType.Food && FoodResources.Contains(type)) || TradeGoods.Contains(type)) continue;
            var card = Panel();
            card.Name = $"Resource{type}";
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            card.CustomMinimumSize = new Vector2(0, 54);
            card.AddThemeStyleboxOverride("panel", MenuStyle.Surface(8));
            _resourceCards[type] = card;
            card.TooltipText = type.ToString() switch
            {
                "Food" => "Réserve nutritive totale. 100 points de faim = 1 nourriture. Cliquez pour voir le détail.",
                "Grain" => "Céréales récoltées dans les champs. Elles complètent la réserve de nourriture.",
                "Wood" => "Bois disponible pour les constructions et le feu de camp.",
                "Stone" => "Pierre extraite par les mineurs.",
                "IronOre" => "Minerai de fer extrait de la roche.",
                "Charcoal" => "Charbon de bois produit par la charbonnière, utilisé pour travailler le fer.",
                "Iron" => "Fer produit au bas fourneau pour fabriquer des outils.",
                "Tools" => "Outils fabriqués à la forge pour équiper les travailleurs.",
                "Flour" => "Farine moulue par le moulin à eau ; elle ne se mange pas crue.",
                "Bread" => "Pain cuit au four : il nourrit mieux que les céréales crues.",
                "Coins" => "Pièces de la monnaie commune à toutes les colonies.",
                "Eggs" => "Œufs de l'enclos : ils se mangent comme la nourriture sauvage.",
                "Milk" => "Lait des vaches : il nourrit un peu mieux que la nourriture sauvage, mais il tourne vite.",
                _ => ResourceIcons.Name(type),
            };
            row.AddChild(card);
            var content = Row(card, 8);
            var icon = new TextureRect
            {
                Texture = ResourceIcons.Get(type), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(24, 24), MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            content.AddChild(icon);
            var text = Column(content, 0);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var label = Text(text, type == ResourceType.Food ? "Nourriture ▾" : type == ResourceType.IronOre ? "Minerai" : ResourceIcons.Name(type), 12, Muted);
            label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _stocks[type] = Text(text, "0", 21, Ink);
            _stocks[type].ClipText = true;
            _stocks[type].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            if (type == ResourceType.Food)
            {
                _stocks[type].Name = "FoodTotal";
                var toggle = new Button
                {
                    Name = "ToggleFoodDetails", Flat = true, FocusMode = Control.FocusModeEnum.All,
                    MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = card.TooltipText,
                };
                card.AddChild(toggle);
                toggle.Pressed += () =>
                {
                    _foodDropdown.Visible = !_foodDropdown.Visible;
                    Layer = _foodDropdown.Visible ? 13 : 10;
                    PositionFoodDetails();
                };
            }
        }
        _foodDropdown = Panel();
        _foodDropdown.AddThemeStyleboxOverride("panel", Style(new Color(0.065f, 0.115f, 0.105f, 1f), Border));
        _foodDropdown.Name = "FoodDetails";
        _foodDropdown.ZIndex = 20;
        _foodDropdown.CustomMinimumSize = new Vector2(360, 0);
        _root.AddChild(_foodDropdown);
        var detail = Column(_foodDropdown, 10);
        Text(detail, "Stock de nourriture", 16, Gold);
        Text(detail, "100 points de faim = 1 nourriture", 12, Muted);
        foreach (var type in FoodResources)
        {
            string name = type == ResourceType.Food ? "Baies" : ResourceIcons.Name(type);
            var line = Row(detail, 12);
            line.AddChild(new TextureRect
            {
                Texture = ResourceIcons.Get(type), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            Text(line, name, 14, Ink).CustomMinimumSize = new Vector2(85, 0);
            _foodDetails[type] = Text(line, "", 13, Ink);
            _foodDetails[type].Name = $"FoodDetail{type}";
        }
        Text(detail, "Farine : à transformer en pain", 12, Muted);
        _foodDropdown.Hide();
    }

    internal static int ResourceCardCount => Enum.GetValues<ResourceType>().Count(type =>
        (int)type < 27 && (type == ResourceType.Food || !FoodResources.Contains(type)) && !TradeGoods.Contains(type));

    private void PositionFoodDetails()
    {
        var card = _resourceCards[ResourceType.Food];
        _foodDropdown.Position = card.GlobalPosition - _root.GlobalPosition + new Vector2(0, card.Size.Y + 6);
    }

    public void CloseFoodDetails() { _foodDropdown.Hide(); Layer = 10; }

    private void BuildJournal()
    {
        _tray = Panel();
        _tray.Name = "Journal";
        _root.AddChild(_tray);
        Place(_tray, 0, 1, 0, 1, 16, -316, 436, -60);
        var column = Column(_tray, 8);
        var header = Row(column, 6);
        _journalTab = Button(header, "Journal", "Dernières pensées de la colonie", 94);
        _workTab = Button(header, "Production", "Travail, recettes des ateliers et coûts de production", 110);
        _workTab.Name = "Production";
        _journalTab.ToggleMode = _workTab.ToggleMode = true;
        _journalTab.Pressed += () => { _showWork = false; _trayExpanded = true; _thoughtStamp = ""; UpdateTray(); };
        _workTab.Pressed += () => { _showWork = true; _trayExpanded = true; UpdateTray(); };
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _collapse = Button(header, "−", "Replier / déplier ce panneau", 34);
        _collapse.Name = "CollapseJournal";
        _collapse.Pressed += () => { _trayExpanded = !_trayExpanded; UpdateTray(); };
        _journalBody = Scroll(column, 174);
        var messages = Column(_journalBody, 10);
        messages.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        for (int i = 0; i < 8; i++)
        {
            var line = Row(messages, 10);
            var date = Text(line, "", 11, Gold);
            date.CustomMinimumSize = new Vector2(43, 0);
            date.VerticalAlignment = VerticalAlignment.Top;
            var message = Wrapped(line, "", 13, Ink);
            _thoughtRows.Add((date, message));
        }
        _workBody = Scroll(column, 174);
        _workBody.Name = "DefilementProduction";
        _productionDashboard = new ProductionDashboard();
        _workBody.AddChild(_productionDashboard);
        UpdateTray();
    }

    private void BuildInspector()
    {
        _colonistPanel = Panel();
        _colonistPanel.Name = "FicheHabitant";
        _root.AddChild(_colonistPanel);
        Place(_colonistPanel, 1, 0, 1, 1, -352, 182, -16, -60);
        _colonistPanel.Visible = false;
        var column = Column(_colonistPanel, 10);
        var heading = Row(column, 4);
        Text(heading, "HABITANT", 11, Gold).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rename = Button(heading, "Renommer", "Changer le prénom et le nom de famille", 88);
        _rename.Name = "RenameColonist";
        _rename.Pressed += BeginRename;
        var close = Button(heading, "×", "Fermer la fiche · Échap", 28);
        close.Name = "CloseInspector";
        close.Pressed += () => SelectionClosed?.Invoke();
        var scroll = Scroll(column, 0);
        _colonistScroll = scroll;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var content = Column(scroll, 6);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var identity = Row(content, 12);
        var portraitFrame = Panel(true);
        identity.AddChild(portraitFrame);
        _portrait = new TextureRect
        {
            CustomMinimumSize = new Vector2(64, 64), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        portraitFrame.AddChild(_portrait);
        var name = Column(identity, 4);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _colonistName = Wrapped(name, "", 21, Ink);
        _colonistName.Name = "ColonistName";
        _colonistAge = Text(name, "", 12, Muted);
        _nameEditor = Column(content, 4);
        _nameEditor.Name = "ColonistNameEditor";
        _nameEditor.Visible = false;
        Text(_nameEditor, "Prénom", 12, Muted);
        _firstNameEdit = NameField(_nameEditor, "ColonistFirstName", "Prénom obligatoire");
        Text(_nameEditor, "Nom de famille", 12, Muted);
        _surnameEdit = NameField(_nameEditor, "ColonistSurname", "Facultatif");
        var nameActions = Row(_nameEditor, 6);
        _confirmRename = Button(nameActions, "Valider", "Enregistrer le nom · Entrée", 85);
        _confirmRename.Name = "ConfirmColonistName";
        _confirmRename.Pressed += ConfirmRename;
        var cancelRename = Button(nameActions, "Annuler", "Conserver le nom actuel · Échap", 85);
        cancelRename.Name = "CancelColonistName";
        cancelRename.Pressed += CancelRename;
        _firstNameEdit.TextChanged += _ => _confirmRename.Disabled = string.IsNullOrWhiteSpace(_firstNameEdit.Text);
        _colonistActivity = Wrapped(content, "", 14, Gold);
        Section(content, "BIEN-ÊTRE");
        _mood = new NeedBar(content, "Humeur", Gold);
        _food = new NeedBar(content, "Nourriture", Mint);
        _rest = new NeedBar(content, "Repos", Color.Color8(144, 173, 212));
        _leisure = new NeedBar(content, "Détente", Color.Color8(192, 163, 206));
        _social = new NeedBar(content, "Compagnie", Color.Color8(212, 159, 139));
        _comfort = new NeedBar(content, "Confort", Color.Color8(183, 189, 139));
        Section(content, "QUOTIDIEN");
        _colonistSector = Wrapped(content, "", 13, Ink);
        Section(content, "PERSONNALITÉ & LIENS");
        _colonistTraits = Wrapped(content, "", 13, Muted);
        _colonistRelations = Wrapped(content, "", 13, Ink);
        _colonistFamily = Wrapped(content, "", 12, Muted);
        Section(content, "COMPÉTENCES");
        foreach (SkillType skill in Skills.All)
        {
            var line = Row(content, 4);
            Text(line, SkillName(skill), 13, Ink).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _skills[skill] = Text(line, "", 13, Gold);
            _skills[skill].MouseFilter = Control.MouseFilterEnum.Pass;
        }
    }

    private void BuildFooter()
    {
        var footer = Panel(true);
        _root.AddChild(footer);
        Place(footer, 0, 1, 1, 1, 16, -46, -16, -12);
        var row = Row(footer, 12);
        _tileInfo = Text(row, "Survolez le terrain pour l'inspecter", 12, Muted);
        _tileInfo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _tileInfo.ClipText = true;
        _tileInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _recenter = Button(row, "⌖  Recentrer", "Revenir au camp ou à l'habitant sélectionné · C", 110);
        _recenter.Name = "Recentrer";
        _recenter.CustomMinimumSize = new Vector2(110, 22);
        _recenter.Pressed += () => RecenterRequested?.Invoke();
        var helpButton = Button(row, "?  Commandes", "Afficher les commandes du jeu", 115);
        helpButton.Name = "Help";
        helpButton.CustomMinimumSize = new Vector2(115, 22);
        helpButton.Pressed += () => HelpRequested?.Invoke();
        _help = Panel();
        _help.Name = "Commands";
        _root.AddChild(_help);
        Place(_help, 0.5f, 1, 0.5f, 1, -260, -320, 260, -60);
        _help.Visible = false;
        var commands = Column(_help, 8);
        Section(commands, "COMMANDES");
        Wrapped(commands, "ZQSD / WASD / flèches   Déplacer la caméra\nClic droit ou molette maintenue   Glisser   ·   Molette   Zoom\nClic gauche   Sélectionner un habitant / miner la roche\nC   Recentrer sur le village ou l'habitant sélectionné\nTab   Observer la colonie suivante\nM   Carte du monde   ·   E   Économie   ·   R   Savoirs et relations   ·   P   Prières\nJ   Journal   ·   H   Afficher / fermer les commandes\nEspace   Pause   ·   1 / 2 / 3   Vitesse   ·   4   Vue chiffrée (×200)\nF5   Sauvegarde rapide   ·   F9   Chargement rapide\nÉchap   Fermer un panneau / menu", 13, Ink);
    }

    private void ResizePanels()
    {
        var layout = InterfaceLayout.For(_root.Size);
        _resourceRow.Columns = Math.Min(layout.ResourceColumns, _resourceCards.Count);
        _resourceRow.OffsetBottom = 98 + layout.ResourcesHeight;
        float inspectorWidth = Math.Clamp(_root.Size.X * 0.24f, 300, 336);
        _colonistPanel.OffsetLeft = -16 - inspectorWidth;
        _colonistPanel.OffsetTop = layout.ContentTop;
        UpdateTray();
    }

    private void UpdateTray()
    {
        _journalBody.Visible = _trayExpanded && !_showWork;
        _workBody.Visible = _trayExpanded && _showWork;
        _journalTab.ButtonPressed = !_showWork;
        _workTab.ButtonPressed = _showWork;
        _collapse.Text = _trayExpanded ? "−" : "+";
        float inspectorWidth = Math.Clamp(_root.Size.X * 0.24f, 300, 336);
        float width = _trayExpanded && _showWork ? 680 : 420;
        _tray.OffsetRight = 16 + Math.Min(width, Math.Max(300, _root.Size.X - inspectorWidth - 56));
        float availableHeight = _root.Size.Y - InterfaceLayout.For(_root.Size).ContentTop - 60;
        _tray.OffsetTop = _trayExpanded && _showWork
            ? -60 - Math.Min(600, Math.Max(240, availableHeight))
            : _trayExpanded ? -Math.Min(316, Math.Max(220, _root.Size.Y * 0.35f)) : -122;
        float bodyHeight = Math.Max(100, -_tray.OffsetTop - 120);
        _journalBody.CustomMinimumSize = _workBody.CustomMinimumSize = new Vector2(0, bodyHeight);
    }
    public bool HelpOpen => _help.Visible;
    public bool IsRenaming => _nameEditor.IsVisibleInTree();
    public void ToggleHelp() { _help.Visible = !_help.Visible; SetOverlayState(_mapOverlay, _economyOverlay); }
    public void CloseHelp() { _help.Hide(); SetOverlayState(_mapOverlay, _economyOverlay); }
    public void ToggleJournal()
    {
        _trayExpanded = _mapOverlay || _economyOverlay || !_trayExpanded;
        _showWork = false; _thoughtStamp = ""; UpdateTray();
    }

    public void SetOverlayState(bool map, bool economy)
    {
        if (map || economy) CloseFoodDetails();
        _mapOverlay = map; _economyOverlay = economy;
        if (map || economy) _help.Hide();
        _tray.Visible = _resourceRow.Visible && !map && !economy && !HelpOpen;
        _colonistPanel.Visible = _shownColonist is not null && !map && !HelpOpen;
    }

    public void SetStatus(GameClock clock, GameSpeed speed)
    {
        string season = clock.Season switch { Season.Printemps => "Printemps", Season.Ete => "Été", Season.Automne => "Automne", _ => "Hiver" };
        _calendar.Text = $"{season} · Jour {clock.DayOfSeason} / 5";
        _hour.Text = $"An {clock.Year}   ·   {clock.Hour:00}:{clock.Minute:00}" + (speed == GameSpeed.Pause ? "   ·   En pause" : "");
        _pause.SetPressedNoSignal(speed == GameSpeed.Pause);
        _pause.Text = speed == GameSpeed.Pause ? "▶" : "Ⅱ";
        _pause.TooltipText = speed == GameSpeed.Pause ? "Reprendre le temps · Espace" : "Mettre en pause · Espace";
        foreach (var (mode, button) in _speedButtons) button.SetPressedNoSignal(speed == mode);
    }

    public void ShowColony(Colony colony, GameClock clock)
    {
        _shownTicks = clock.Ticks;
        _resourceRow.Show(); _tray.Visible = !_mapOverlay && !_economyOverlay && !HelpOpen;
        _colonyName.Text = colony.CurrentSettlement.Name;
        int arriving = colony.Transients.Count(t => t.Transit == TransitState.Arriving);
        string population = $"{colony.Members.Count} citoyens · {colony.PresentMembers.Count} présents" + (colony.Children > 0 ? $" · {colony.Children} enfants" : "");
        if (arriving > 0) population += $" · {arriving} en route";
        if (colony.Graves.Count > 0) population += $" · {colony.Graves.Count} tombes";
        int sick = Health.PatientCount(colony);
        if (sick > 0) population += $" · {sick} malade{(sick > 1 ? "s" : "")}";
        float days = colony.Stock.FoodUnits / (Math.Max(1, colony.PresentMembers.Count) * ColonyBrain.MealsPerColonistPerDay);
        _colonyMeta.Text = $"{population}   ·   Humeur {colony.AverageMood * 100:0} %   ·   Réserves {days:0.0} j";
        _colonyMeta.TooltipText = $"{population}\nRéserves de repas : {days:0.0} jours (nourriture, céréales et pain)\nHumeur : {colony.AverageMood * 100:0} % · Attrait : {Migration.Attractiveness(colony, clock) * 100:0} %";
        foreach (var (resource, value) in _stocks)
        {
            decimal stock = resource == ResourceType.Food ? colony.Stock.FoodNutrition : colony.Stock.Get(resource);
            value.Text = resource == ResourceType.Food ? stock.ToString("0.##") : stock.ToString("N0");
            value.AddThemeColorOverride("font_color", resource == ResourceType.Food && days < 2 ? MenuStyle.Error : stock == 0 ? Muted : Ink);
        }
        var foodStyle = (StyleBoxFlat)_resourceCards[ResourceType.Food].GetThemeStylebox("panel");
        foodStyle.BorderColor = days < 2 ? MenuStyle.Error : Border;
        foreach (var (type, value) in _foodDetails)
            value.Text = $"{colony.Stock.Get(type):N0} × {Stockpile.NutritionPerItem(type):0.##} = {colony.Stock.Nutrition(type):0.##}";
        PositionFoodDetails();
    }

    public void ShowUnsettled(string name, string hint)
    {
        CancelRename();
        _shownColonist = null;
        _colonyName.Text = name; _colonyMeta.Text = hint;
        _resourceRow.Hide(); _tray.Hide(); _colonistPanel.Hide();
        CloseFoodDetails();
    }

    public void ResetColony() { _thoughtStamp = ""; CloseFoodDetails(); }

    public void SetSpeedControlsEnabled(bool enabled)
    {
        _pause.Disabled = !enabled;
        foreach (Button button in _speedButtons.Values) button.Disabled = !enabled;
    }

    public void SetTileInfo(string text) => _tileInfo.Text = string.IsNullOrWhiteSpace(text) ? "Survolez le terrain pour l'inspecter" : text;

    /// <summary>Recentrer n'a de sens qu'avec une carte à l'écran (pas en vue chiffrée).</summary>
    public void SetMapTools(bool visible) => _recenter.Visible = visible;

    public void ShowShares(Colony colony)
    {
        if (!_workBody.Visible) return;
        _productionDashboard.Refresh(colony, _shownTicks);
    }
    public void ShowThoughts(Colony colony)
    {
        if (!_journalBody.Visible) return;
        var thoughts = colony.Thoughts.AsEnumerable().Reverse().Take(_thoughtRows.Count).ToArray();
        string stamp = thoughts.Length == 0 ? "empty" : $"{thoughts.Length}:{thoughts[0].Ticks}:{thoughts[0].Text}";
        if (_thoughtStamp == stamp) return;
        _thoughtStamp = stamp;
        for (int i = 0; i < _thoughtRows.Count; i++)
        {
            var (date, message) = _thoughtRows[i];
            date.GetParent<Control>().Visible = i < thoughts.Length;
            if (i >= thoughts.Length) continue;
            long day = thoughts[i].Ticks / TimeConstants.TicksPerDay + 1;
            int hour = (int)(thoughts[i].Ticks % TimeConstants.TicksPerDay * 24 / TimeConstants.TicksPerDay);
            date.Text = $"J{day}\n{hour:00}h";
            message.Text = thoughts[i].Text;
        }
        if (thoughts.Length == 0)
        {
            _thoughtRows[0].Date.GetParent<Control>().Visible = true;
            _thoughtRows[0].Date.Text = "·";
            _thoughtRows[0].Message.Text = "La colonie prend ses marques…";
        }
    }

    public void ShowColonist(Colonist? colonist, WoodlandBiome biome = WoodlandBiome.TemperatePlain)
    {
        if (_shownColonist != colonist) CancelRename();
        _shownColonist = colonist;
        _colonistPanel.Visible = colonist is not null && !_mapOverlay && !HelpOpen;
        if (colonist is null) return;
        var appearance = PeoplesSprites.Describe(colonist, biome);
        if (_portraitAppearance != appearance)
        {
            _portrait.Texture = PeoplesSprites.Portrait(appearance);
            _portraitAppearance = appearance;
        }
        _colonistName.Text = colonist.FullName;
        _colonistActivity.Text = (colonist.Ailment == Ailment.None ? "" : Health.Describe(colonist) + " · ")
            + (colonist.IsBoosted ? "Revigoré par le ragoût · " : "") + (colonist.Needs.BeerCheer > 0.1f ? "Requinqué par la bière · " : "") + Describe(colonist);
        string stage = colonist.Stage switch
        {
            LifeStage.Child => "Enfant", LifeStage.Teen => "Adolescent" + (colonist.Sex == Sex.Female ? "e" : ""),
            LifeStage.Elder => colonist.Sex == Sex.Female ? "Ancienne" : "Ancien", _ => "Adulte",
        };
        string people = colonist.Sex != Sex.Female ? colonist.Species.Name
            : colonist.Species == Species.Human ? "Humaine" : colonist.Species == Species.Dwarf ? "Naine" : colonist.Species.Name;
        _colonistAge.Text = $"{people} · {stage}\n{colonist.AgeYears:0.#} ans";
        _food.Set(colonist.Needs.Food); _rest.Set(colonist.Needs.Rest); _leisure.Set(colonist.Needs.Leisure);
        _social.Set(colonist.Needs.Social); _comfort.Set(colonist.Needs.Comfort); _mood.Set(colonist.Needs.Mood);
        _colonistSector.Text = colonist.Transit switch
        {
            TransitState.Arriving => "Voyageur · rejoint la colonie", TransitState.Leaving => "A quitté la colonie",
            _ => $"Travail : {SectorName(colonist.Sector)}\n" + (colonist.Home is null ? "Logement : à la belle étoile" : "Logement : hutte"),
        };
        string traits = string.Join(", ", colonist.Personality.NotableTraits(colonist.Sex));
        _colonistTraits.Text = traits.Length == 0 ? "Caractère modéré" : traits;
        string friends = string.Join(", ", colonist.FriendsIn(colonist.Colony).Select(f => f.Name).Take(4));
        string rivals = string.Join(", ", colonist.RivalsIn(colonist.Colony).Select(f => f.Name).Take(3));
        _colonistRelations.Text = (friends.Length > 0 ? "Amis : " + friends : "Pas encore d'ami") + (rivals.Length > 0 ? "\nRivaux : " + rivals : "");
        var family = new List<string>();
        if (colonist.PregnantUntilTicks is not null) family.Add("Attend un enfant");
        if (colonist.Needs.Grief > 0.15f) family.Add("En deuil");
        if (colonist.Partner is { } partner) family.Add($"En couple avec {partner.Name}");
        if (colonist.Mother is not null || colonist.Father is not null)
            family.Add($"Parents : {string.Join(" et ", new[] { colonist.Mother?.Name, colonist.Father?.Name }.Where(n => n is not null))}");
        if (colonist.Children.Count > 0) family.Add($"Enfants : {string.Join(", ", colonist.Children.Select(c => c.Name))}");
        _colonistFamily.Text = string.Join("\n", family);
        _colonistFamily.Visible = family.Count > 0;
        foreach (var (skill, label) in _skills)
        {
            float talent = colonist.Skills.Talent(skill);
            string mark = talent > 1.2f ? " ★" : talent < 0.75f ? " ·" : "";
            label.Text = $"{colonist.Skills.Level(skill):0.0}{mark}";
            label.TooltipText = talent > 1.2f ? "Très doué : apprend plus vite" : talent < 0.75f ? "Apprentissage plus lent" : "Apprentissage normal";
        }
    }

    private LineEdit NameField(Node parent, string name, string placeholder)
    {
        var field = new LineEdit
        {
            Name = name, PlaceholderText = placeholder, MaxLength = Colonist.MaxNameLength,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        field.AddThemeStyleboxOverride("normal", Style(new Color(0.08f, 0.14f, 0.12f), Border, 5, 6));
        field.AddThemeStyleboxOverride("focus", Style(new Color(0.08f, 0.14f, 0.12f), Gold, 5, 6));
        field.AddThemeColorOverride("font_color", Ink);
        field.AddThemeColorOverride("font_placeholder_color", Muted);
        parent.AddChild(field);
        field.TextSubmitted += _ => ConfirmRename();
        return field;
    }

    private void BeginRename()
    {
        if (_shownColonist is null) return;
        _firstNameEdit.Text = _shownColonist.Name;
        _surnameEdit.Text = _shownColonist.Surname;
        _confirmRename.Disabled = false;
        _nameEditor.Show();
        _colonistScroll.ScrollVertical = 0;
        _rename.Disabled = true;
        _firstNameEdit.GrabFocus();
        _firstNameEdit.SelectAll();
    }

    private void ConfirmRename()
    {
        if (_shownColonist is null || string.IsNullOrWhiteSpace(_firstNameEdit.Text)) return;
        ColonistRenameRequested?.Invoke(_shownColonist, _firstNameEdit.Text, _surnameEdit.Text);
        _colonistName.Text = _shownColonist.FullName;
        CancelRename();
    }

    public void CancelRename()
    {
        _firstNameEdit.ReleaseFocus();
        _surnameEdit.ReleaseFocus();
        _nameEditor.Hide();
        _rename.Disabled = false;
    }

    public static string SectorName(WorkSector sector) => sector.ToString() switch
    {
        "Food" => "cueillette et pêche", "Farm" => "agriculture", "Wood" => "bois", "Stone" => "pierre",
        "Construction" => "construction", "Craft" => "artisanat", _ => "temps libre",
    };

    private static string SkillName(SkillType skill) => skill.ToString() switch
    {
        "Foraging" => "Cueillette", "Fishing" => "Pêche", "Woodcutting" => "Bûcheronnage", "Mining" => "Minage",
        "Farming" => "Agriculture", "Smithing" => "Métallurgie", "Cooking" => "Boulangerie", "Husbandry" => "Élevage",
        "Weaving" => "Tissage", "Trading" => "Négoce", "Medicine" => "Médecine", _ => "Construction",
    };

    private static string Describe(Colonist colonist)
    {
        Activity? activity = colonist.Activity;
        if (activity is null)
            return "Réfléchit à ce qu'il va faire";
        bool there = activity.Started;
        return activity.Kind switch
        {
            ActivityKind.Sleep => there ? "Dort" : "Va se coucher",
            ActivityKind.Eat => there ? "Mange" : "Va manger au camp",
            ActivityKind.Relax when activity.Building is { Type: BuildingType.Infirmary } => there ? "Se repose à l'infirmerie" : "Va à l'infirmerie",
            ActivityKind.Relax when activity.Building is { Type: BuildingType.Tavern } => there ? "Se détend à la taverne" : "Va à la taverne",
            ActivityKind.Relax => there ? "Se détend près du feu" : "Va se détendre près du feu",
            ActivityKind.Forage => there ? "Cueille des baies" : "Part cueillir des baies",
            ActivityKind.ForageToEat => there ? "Mange des baies sauvages" : "Cherche des baies à manger",
            ActivityKind.Fish => there ? "Pêche" : "Part pêcher au lac",
            ActivityKind.Chop => there ? "Abat un arbre" : "Part couper du bois",
            ActivityKind.Mine => there ? "Taille la roche" : "Part à la carrière",
            ActivityKind.Deliver => "Rapporte sa récolte au camp",
            ActivityKind.FetchMaterials => "Va chercher des matériaux pour le chantier",
            ActivityKind.SupplySite => "Apporte des matériaux au chantier",
            ActivityKind.Build => there
                ? (activity.Building is { IsHut: false } site
                    ? $"Bâtit {Building.WithArticle(site.Type)}"
                    : "Bâtit une hutte")
                : "Part sur le chantier",
            ActivityKind.Dig => there ? "Creuse un canal" : "Part creuser le canal",
            ActivityKind.Craft when activity.Building is { Type: BuildingType.Cask } => there ? "Verse les céréales dans le fût" : "Part au fût de la taverne",
            ActivityKind.Craft => activity.Building is { } workshop
                ? $"{(there ? "Travaille" : "Part")} {(Building.IsFeminine(workshop.Type) ? "à la" : "au")} {Building.NameOf(workshop.Type)}"
                : "Travaille à l'atelier",
            ActivityKind.Chat => there ? "Bavarde" : "Va rejoindre quelqu'un pour bavarder",
            ActivityKind.Sow => there ? "Sème" : "Part semer",
            ActivityKind.Harvest => there ? "Moissonne" : "Part moissonner",
            ActivityKind.Tend => there ? "Soigne les bêtes de l'enclos" : "Part à l'enclos",
            ActivityKind.Slaughter => there ? "Abat une bête" : "Part à l'enclos pour abattre une bête",
            ActivityKind.Heal => there ? "Soigne les malades" : "Part à l'infirmerie",
            ActivityKind.Study => there ? "Apprend à l'école" : "Part à l'école",
            ActivityKind.Arrive => "Marche vers la colonie",
            ActivityKind.Depart => "Quitte la colonie pour de bon",
            _ => "Se promène",
        };
    }

    private static StyleBoxFlat Style(Color fill, Color border, int radius = 9, int margin = 12) => new()
    {
        BgColor = fill, BorderColor = border,
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
    };

    private static PanelContainer Panel(bool inset = false)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseForcePassScrollEvents = false };
        var style = Style(inset ? new Color(0.08f, 0.14f, 0.12f, 0.96f) : Background, Border, inset ? 6 : 9, inset ? 5 : 12);
        if (!inset) { style.ShadowColor = new Color(0, 0, 0, 0.24f); style.ShadowSize = 5; style.ShadowOffset = new Vector2(0, 3); }
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static void Place(Control control, float al, float at, float ar, float ab, float left, float top, float right, float bottom)
    {
        control.AnchorLeft = al; control.AnchorTop = at; control.AnchorRight = ar; control.AnchorBottom = ab;
        control.OffsetLeft = left; control.OffsetTop = top; control.OffsetRight = right; control.OffsetBottom = bottom;
    }

    private static VBoxContainer Column(Node parent, int gap)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        column.AddThemeConstantOverride("separation", gap); parent.AddChild(column); return column;
    }

    private static HBoxContainer Row(Node parent, int gap)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", gap); parent.AddChild(row); return row;
    }

    private static Label Text(Node parent, string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (size >= 20) label.AddThemeFontOverride("font", ArtDirection.HeadingFont);
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label); return label;
    }

    private static Label Wrapped(Node parent, string text, int size, Color color)
    {
        var label = Text(parent, text, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    private static void Section(Node parent, string title)
    {
        var line = new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = Border, Thickness = 1 });
        parent.AddChild(line);
        Text(parent, title, 10, Muted);
    }

    private static Button Button(Node parent, string text, string hint, int width)
    {
        var button = new Button
        {
            Text = text, TooltipText = hint, CustomMinimumSize = new Vector2(width, 30),
            FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", Muted);
        button.AddThemeColorOverride("font_hover_color", Ink);
        button.AddThemeColorOverride("font_pressed_color", Gold);
        button.AddThemeStyleboxOverride("normal", Style(new Color(0.1f, 0.17f, 0.14f), Border, 5, 5));
        button.AddThemeStyleboxOverride("hover", Style(new Color(0.16f, 0.24f, 0.18f), Mint, 5, 5));
        button.AddThemeStyleboxOverride("pressed", Style(new Color(0.23f, 0.24f, 0.15f), Gold, 5, 5));
        button.AddThemeStyleboxOverride("hover_pressed", Style(new Color(0.28f, 0.29f, 0.19f), Gold, 5, 5));
        button.AddThemeStyleboxOverride("disabled", Style(Background, Border, 5, 5));
        button.AddThemeColorOverride("font_disabled_color", Muted.Darkened(0.35f));
        parent.AddChild(button); return button;
    }

    private static ScrollContainer Scroll(Node parent, int height)
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = Control.MouseFilterEnum.Stop, MouseForcePassScrollEvents = false,
        };
        parent.AddChild(scroll);
        var scrollbar = scroll.GetVScrollBar();
        scrollbar.AddThemeStyleboxOverride("scroll", new StyleBoxFlat
        {
            BgColor = Colors.Transparent, ContentMarginLeft = 3, ContentMarginRight = 3,
        });
        foreach (string state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
        {
            scrollbar.AddThemeStyleboxOverride(state, new StyleBoxFlat
            {
                BgColor = state == "grabber" ? Border : Mint,
                ContentMarginLeft = 3, ContentMarginRight = 3,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            });
        }
        return scroll;
    }

    private static ProgressBar Bar(Color color, int height)
    {
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false,
            CustomMinimumSize = new Vector2(20, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("background", Style(new Color(0.16f, 0.22f, 0.18f), Colors.Transparent, 3, 0));
        bar.AddThemeStyleboxOverride("fill", Style(color, Colors.Transparent, 3, 0));
        return bar;
    }

    private sealed class NeedBar
    {
        private readonly ProgressBar _bar;
        private readonly Label _value;
        private readonly StyleBoxFlat _fill;
        private readonly Color _healthy;
        public NeedBar(Node parent, string name, Color healthy)
        {
            _healthy = healthy;
            var row = Row(parent, 8);
            var title = Text(row, name, 12, Muted);
            title.CustomMinimumSize = new Vector2(76, 0);
            _bar = Bar(healthy, 8); row.AddChild(_bar);
            _fill = (StyleBoxFlat)_bar.GetThemeStylebox("fill");
            _value = Text(row, "", 12, Ink);
            _value.CustomMinimumSize = new Vector2(39, 0);
            _value.HorizontalAlignment = HorizontalAlignment.Right;
        }
        public void Set(float value)
        {
            _bar.Value = value;
            _fill.BgColor = value < 0.25f ? Color.Color8(211, 121, 102) : value < 0.45f ? Gold : _healthy;
            _value.Text = $"{value * 100:0} %";
        }
    }
}
