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

    private Control _root = null!;
    private Label _colonyName = null!, _colonyMeta = null!, _calendar = null!, _hour = null!, _tileInfo = null!;
    private readonly Dictionary<ResourceType, Label> _stocks = [];
    private readonly Dictionary<GameSpeed, Button> _speedButtons = [];
    private Button _pause = null!, _journalTab = null!, _workTab = null!, _collapse = null!;
    private PanelContainer _tray = null!, _help = null!, _colonistPanel = null!;
    private ScrollContainer _journalBody = null!, _workBody = null!;
    private bool _trayExpanded = true, _showWork;
    private readonly List<(Label Date, Label Message)> _thoughtRows = [];
    private readonly Dictionary<WorkSector, (ProgressBar Bar, Label Value)> _shares = [];
    private Label _costs = null!;
    private string _thoughtStamp = "";

    private TextureRect _portrait = null!;
    private int _portraitId = -1;
    private bool _portraitElder;
    private Label _colonistName = null!, _colonistActivity = null!, _colonistSector = null!;
    private Label _colonistAge = null!, _colonistFamily = null!, _colonistTraits = null!, _colonistRelations = null!;
    private readonly Dictionary<SkillType, Label> _skills = [];
    private NeedBar _food = null!, _rest = null!, _leisure = null!, _social = null!, _comfort = null!, _mood = null!;

    public override void _Ready()
    {
        Layer = 10;
        _root = new Control { Name = "Interface", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.Theme = new Theme { DefaultFontSize = 14, DefaultFont = ArtDirection.BodyFont };
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
        })
        {
            var button = Button(speeds, caption, hint, 48);
            button.Name = $"Speed{(int)speed}";
            button.ToggleMode = true;
            button.Pressed += () => SpeedRequested?.Invoke(speed);
            _speedButtons[speed] = button;
        }
    }

    private void BuildResources()
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(row);
        Place(row, 0, 0, 1, 0, 16, 98, -16, 168);
        row.AddThemeConstantOverride("separation", 8);
        foreach (ResourceType type in Enum.GetValues<ResourceType>())
        {
            var card = Panel();
            card.Name = $"Resource{type}";
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            card.TooltipText = type.ToString() switch
            {
                "Food" => "Baies et poisson. Les habitants utilisent d'abord cette nourriture pour leurs repas.",
                "Grain" => "Céréales récoltées dans les champs. Elles complètent la réserve de nourriture.",
                "Wood" => "Bois disponible pour les constructions et le feu de camp.",
                "Stone" => "Pierre extraite par les mineurs.",
                "IronOre" => "Minerai de fer extrait de la roche.",
                "Charcoal" => "Charbon de bois produit par la charbonnière, utilisé pour travailler le fer.",
                "Iron" => "Fer produit au bas fourneau pour fabriquer des outils.",
                "Tools" => "Outils fabriqués à la forge pour équiper les travailleurs.",
                _ => ResourceIcons.Name(type),
            };
            row.AddChild(card);
            var content = Row(card, 12);
            var icon = new TextureRect
            {
                Texture = ResourceIcons.Get(type), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(32, 32), MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            content.AddChild(icon);
            var text = Column(content, 0);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var label = Text(text, ResourceIcons.Name(type), 12, Muted);
            label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _stocks[type] = Text(text, "0", 22, Ink);
        }
    }

    private void BuildJournal()
    {
        _tray = Panel();
        _tray.Name = "Journal";
        _root.AddChild(_tray);
        Place(_tray, 0, 1, 0, 1, 16, -316, 436, -60);
        var column = Column(_tray, 8);
        var header = Row(column, 6);
        _journalTab = Button(header, "Journal", "Dernières pensées de la colonie", 94);
        _workTab = Button(header, "Travail", "Répartition du travail et coûts de production", 94);
        _journalTab.ToggleMode = _workTab.ToggleMode = true;
        _journalTab.Pressed += () => { _showWork = false; _trayExpanded = true; UpdateTray(); };
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
        var work = Column(_workBody, 8);
        work.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        foreach (WorkSector sector in WorkSectors.All)
        {
            var line = Row(work, 8);
            var title = Text(line, SectorName(sector), 12, Ink);
            title.CustomMinimumSize = new Vector2(145, 0);
            var bar = Bar(Mint, 7);
            line.AddChild(bar);
            var value = Text(line, "", 12, Gold);
            value.CustomMinimumSize = new Vector2(40, 0);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            _shares[sector] = (bar, value);
        }
        Section(work, "COÛT PAR UNITÉ · HEURES DE TRAVAIL");
        _costs = Wrapped(work, "", 12, Muted);
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
        var close = Button(heading, "×", "Fermer la fiche · Échap", 28);
        close.Name = "CloseInspector";
        close.Pressed += () => SelectionClosed?.Invoke();
        var scroll = Scroll(column, 0);
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var content = Column(scroll, 6);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var identity = Row(content, 12);
        var portraitFrame = Panel(true);
        identity.AddChild(portraitFrame);
        _portrait = new TextureRect
        {
            CustomMinimumSize = new Vector2(32, 48), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        portraitFrame.AddChild(_portrait);
        var name = Column(identity, 4);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _colonistName = Wrapped(name, "", 21, Ink);
        _colonistAge = Text(name, "", 12, Muted);
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
        var helpButton = Button(row, "?  Commandes", "Afficher les commandes du jeu", 115);
        helpButton.Name = "Help";
        helpButton.CustomMinimumSize = new Vector2(115, 22);
        helpButton.Pressed += () => _help.Visible = !_help.Visible;
        _help = Panel();
        _help.Name = "Commands";
        _root.AddChild(_help);
        Place(_help, 0.5f, 1, 0.5f, 1, -235, -202, 235, -60);
        _help.Visible = false;
        var commands = Column(_help, 8);
        Section(commands, "COMMANDES");
        Wrapped(commands, "ZQSD / WASD / flèches   Déplacer la caméra\nClic droit maintenu   Glisser la carte   ·   Molette   Zoom\nClic gauche   Sélectionner un habitant / miner la roche\nEspace   Pause   ·   1 / 2 / 3   Vitesse   ·   Échap   Fermer la fiche", 13, Ink);
    }

    private void ResizePanels()
    {
        float inspectorWidth = Math.Clamp(_root.Size.X * 0.24f, 300, 336);
        _colonistPanel.OffsetLeft = -16 - inspectorWidth;
        _tray.OffsetRight = 16 + Math.Min(420, Math.Max(300, _root.Size.X - inspectorWidth - 56));
    }

    private void UpdateTray()
    {
        _journalBody.Visible = _trayExpanded && !_showWork;
        _workBody.Visible = _trayExpanded && _showWork;
        _journalTab.ButtonPressed = !_showWork;
        _workTab.ButtonPressed = _showWork;
        _collapse.Text = _trayExpanded ? "−" : "+";
        _tray.OffsetTop = _trayExpanded ? -316 : -122;
    }

    public void SetStatus(GameClock clock, GameSpeed speed)
    {
        string season = clock.Season switch { Season.Printemps => "Printemps", Season.Ete => "Été", Season.Automne => "Automne", _ => "Hiver" };
        _calendar.Text = $"{season} · Jour {clock.DayOfSeason} / 5";
        _hour.Text = $"An {clock.Year}   ·   {clock.Hour:00}:{clock.Minute:00}" + (speed == GameSpeed.Pause ? "   ·   En pause" : "");
        _pause.ButtonPressed = speed == GameSpeed.Pause;
        foreach (var (mode, button) in _speedButtons) button.ButtonPressed = speed == mode;
    }

    public void ShowColony(Colony colony, GameClock clock)
    {
        _colonyName.Text = colony.Name;
        int arriving = colony.Transients.Count(t => t.Transit == TransitState.Arriving);
        string population = $"{colony.Members.Count} habitants" + (colony.Children > 0 ? $" · {colony.Children} enfants" : "");
        if (arriving > 0) population += $" · {arriving} en route";
        if (colony.Graves.Count > 0) population += $" · {colony.Graves.Count} tombes";
        _colonyMeta.Text = $"{population}   ·   Humeur {colony.AverageMood * 100:0} %   ·   Attrait {Migration.Attractiveness(colony, clock) * 100:0} %";
        _colonyMeta.TooltipText = _colonyMeta.Text;
        foreach (var (resource, value) in _stocks) value.Text = colony.Stock.Get(resource).ToString("N0");
    }

    public void SetTileInfo(string text) => _tileInfo.Text = string.IsNullOrWhiteSpace(text) ? "Survolez le terrain pour l'inspecter" : text;

    public void ShowShares(Colony colony)
    {
        if (!_workBody.Visible) return;
        foreach (var (sector, display) in _shares)
        {
            display.Bar.Value = colony.WorkShares[sector];
            display.Value.Text = $"{colony.WorkShares[sector] * 100:0} %";
        }
        _costs.Text = ColonyBrain.CostSummary(colony.Labor);
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

    public void ShowColonist(Colonist? colonist)
    {
        _colonistPanel.Visible = colonist is not null;
        if (colonist is null) return;
        bool elder = colonist.Stage == LifeStage.Elder;
        if (_portraitId != colonist.Id || _portraitElder != elder)
        {
            _portrait.Texture = SpriteFactory.Colonist(colonist.Id, elder)[0];
            _portraitId = colonist.Id;
            _portraitElder = elder;
        }
        _colonistName.Text = colonist.FullName;
        _colonistActivity.Text = Describe(colonist);
        string stage = colonist.Stage switch
        {
            LifeStage.Child => "Enfant", LifeStage.Teen => "Adolescent" + (colonist.Sex == Sex.Female ? "e" : ""),
            LifeStage.Elder => colonist.Sex == Sex.Female ? "Ancienne" : "Ancien", _ => "Adulte",
        };
        _colonistAge.Text = $"{stage} · {colonist.AgeYears:0.#} ans";
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
    public static string SectorName(WorkSector sector) => sector.ToString() switch
    {
        "Food" => "cueillette et pêche", "Farm" => "agriculture", "Wood" => "bois", "Stone" => "pierre",
        "Construction" => "construction", "Craft" => "artisanat", _ => "temps libre",
    };

    private static string SkillName(SkillType skill) => skill.ToString() switch
    {
        "Foraging" => "Cueillette", "Fishing" => "Pêche", "Woodcutting" => "Bûcheronnage", "Mining" => "Minage",
        "Farming" => "Agriculture", "Smithing" => "Métallurgie", _ => "Construction",
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
                ? (activity.Building is { IsWorkshop: true } site
                    ? $"Bâtit {(site.Type == BuildingType.Bloomery ? "un" : "une")} {Building.NameOf(site.Type)}"
                    : "Bâtit une hutte")
                : "Part sur le chantier",
            ActivityKind.Craft => activity.Building is { } workshop
                ? $"{(there ? "Travaille" : "Part")} {(workshop.Type == BuildingType.Bloomery ? "au" : "à la")} {Building.NameOf(workshop.Type)}"
                : "Travaille à l'atelier",
            ActivityKind.Chat => there ? "Bavarde" : "Va rejoindre quelqu'un pour bavarder",
            ActivityKind.Sow => there ? "Sème" : "Part semer",
            ActivityKind.Harvest => there ? "Moissonne" : "Part moissonner",
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
