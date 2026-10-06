using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using GodColony.View;

namespace GodColony;

/// <summary>Le rythme réel de la vue chiffrée, mesuré par la boucle de jeu.</summary>
/// <param name="Multiplier">Vitesse atteinte : ×200 si la machine suit, moins sinon.</param>
/// <param name="StartDay">Le jour où l'on est passé en vue chiffrée.</param>
public readonly record struct StatsPace(double Multiplier, bool Paused, long Day, long StartDay);

/// <summary>
/// La vue chiffrée de la vitesse ×200 : à la place de la carte, les chiffres et les courbes de tous les empires.
/// Ni terrain ni habitants ne sont dessinés, si bien que presque tout le temps de calcul va à la simulation.
/// Les contrôles sont construits une fois ; une actualisation ne change que leurs textes, et les courbes ne se
/// redessinent qu'à chaque nouveau relevé quotidien.
/// </summary>
public partial class StatsPanel : CanvasLayer
{
    public event Action<int>? ObserveRequested;

    private static readonly Color CardFill = DashboardStyle.Surface;

    /// <summary>Largeur minimale d'une carte de colonie : le nombre de colonnes en découle.</summary>
    private const float CardWidth = 340;

    private WorldState? _world;
    private Control _root = null!;
    private VBoxContainer _body = null!;
    private MenuBackdrop _backdrop = null!;
    private GridContainer _grid = null!;
    private Label _empty = null!, _comparisonTitle = null!, _comparisonNote = null!;
    private PanelContainer _comparisonCard = null!, _busy = null!;
    private Label _busyText = null!;
    private HFlowContainer _legend = null!;
    private ColonyComparison _comparison = null!;
    private string _legendStamp = "";
    private readonly List<ColonyCard> _cards = [];
    private readonly Dictionary<ColonyMetric, Button> _metricButtons = [];
    private Figure _population = null!, _colonies = null!, _caravans = null!, _graves = null!, _pace = null!, _elapsed = null!;
    private Sparkline _worldCurve = null!;
    private ColonyMetric _metric = ColonyMetric.Population;
    private int _curvesVersion = -1;
    private ColonyMetric _curvesMetric;

    /// <summary>La dernière actualisation, pour redessiner tout de suite quand on choisit une autre courbe.</summary>
    private ColonyHistory? _lastHistory;
    private int _lastObserved;
    private StatsPace _lastPace;

    /// <summary>La courbe choisie pour toutes les colonies.</summary>
    public ColonyMetric Metric => _metric;

    public void Init(WorldState world)
    {
        _world = world;
        if (_backdrop is not null) _backdrop.Atlas = world.WorldMap.Grid;
    }

    public override void _Ready()
    {
        Layer = 9;
        _root = new Control { Name = "VueChiffree", MouseFilter = Control.MouseFilterEnum.Ignore, Theme = MenuStyle.Theme() };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // Le fond reprend l'atlas de l'accueil, avec le monde en cours : on le voit d'en haut pendant que le temps file.
        _backdrop = new MenuBackdrop();
        if (_world is not null) _backdrop.Atlas = _world.WorldMap.Grid;
        _root.AddChild(_backdrop);
        _backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _body = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _body.AddThemeConstantOverride("separation", 10);
        _root.AddChild(_body);
        _body.AnchorRight = 1; _body.AnchorBottom = 1;
        _body.OffsetLeft = 16; _body.OffsetRight = -16; _body.OffsetBottom = -60;
        BuildSummary();
        BuildMetricChoice();
        BuildColonies();

        _busy = new PanelContainer { Name = "RepeintCarte", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _busy.AddThemeStyleboxOverride("panel", MenuStyle.Surface(18));
        _root.AddChild(_busy);
        _busy.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _busy.GrowHorizontal = _busy.GrowVertical = Control.GrowDirection.Both;
        _busyText = MenuStyle.Text(_busy, "", 16, ArtDirection.Brass);
        _root.Resized += ResizePanels;
        ResizePanels();
    }

    /// <summary>Le temps de repeindre la carte, les chiffres s'effacent derrière un message ; null les fait revenir.</summary>
    public void SetBusy(string? message)
    {
        _body.Visible = message is null;
        _busy.Visible = message is not null;
        if (message is not null) _busyText.Text = message;
    }

    private void BuildSummary()
    {
        var row = DashboardStyle.Row(_body, 8);
        row.Name = "ChiffresDuMonde";
        _population = Tile(row, "POPULATION DU MONDE", 2, out HBoxContainer populationRow);
        _worldCurve = new Sparkline
        {
            Name = "CourbeDuMonde", CustomMinimumSize = new Vector2(110, 52), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Surface = CardFill, Format = value => Spoken(ColonyMetric.Population, value),
        };
        populationRow.AddChild(_worldCurve);
        _colonies = Tile(row, "EMPIRES", 1, out _);
        _caravans = Tile(row, "CARAVANES EN ROUTE", 1, out _);
        _graves = Tile(row, "DÉCÈS", 1, out _);
        _pace = Tile(row, "VITESSE RÉELLE", 1, out _);
        _pace.Value.Name = "VitesseReelle";
        _elapsed = Tile(row, "EN VUE CHIFFRÉE", 1, out _);
    }

    private void BuildMetricChoice()
    {
        var row = DashboardStyle.Row(_body, 6);
        var title = DashboardStyle.Text(row, "COURBES DES COLONIES", 11, DashboardStyle.Gold);
        title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        foreach (var (metric, caption, hint) in new[]
        {
            (ColonyMetric.Population, "Habitants", "Nombre d'habitants de chaque empire"),
            (ColonyMetric.FoodDays, "Réserves", "Jours de repas en réserve (nourriture, pain…; les céréales crues comptent à peine)"),
            (ColonyMetric.Mood, "Humeur", "Humeur moyenne des habitants"),
            (ColonyMetric.Coins, "Pièces", "Trésorerie de chaque empire"),
        })
        {
            Button button = WorldPanel.Chip(caption, hint);
            button.AddThemeColorOverride("font_color", DashboardStyle.MetricTint(metric));
            button.AddThemeColorOverride("font_pressed_color", DashboardStyle.MetricTint(metric));
            button.Name = $"Courbe{metric}";
            button.ToggleMode = true;
            button.CustomMinimumSize = new Vector2(92, 30);
            button.Pressed += () => ShowMetric(metric);
            row.AddChild(button);
            _metricButtons[metric] = button;
        }
        DashboardStyle.Spacer(row);
        DashboardStyle.Text(row, "Survolez une courbe pour lire ses relevés", 11, DashboardStyle.Muted).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        ShowMetric(ColonyMetric.Population);
    }

    private void BuildColonies()
    {
        _empty = DashboardStyle.Text(_body, "Aucun empire pour le moment : fondez-en un depuis la carte du monde.", 14, DashboardStyle.Muted, true);
        var scroll = new ScrollContainer
        {
            Name = "DefilementColonies", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseForcePassScrollEvents = false,
        };
        _body.AddChild(scroll);
        var content = MenuStyle.Column(scroll, 10);
        content.MouseFilter = Control.MouseFilterEnum.Ignore;
        _grid = new GridContainer { Name = "CartesColonies", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        content.AddChild(_grid);

        // Sous les cartes, chacune à sa propre échelle, toutes les colonies sur la même : qui grandit, qui s'épuise.
        _comparisonCard = DashboardStyle.Card(content, 14);
        _comparisonCard.Name = "ComparaisonColonies";
        var column = MenuStyle.Column(_comparisonCard, 8);
        var heading = DashboardStyle.Row(column, 12);
        _comparisonTitle = DashboardStyle.Text(heading, "", 11, DashboardStyle.Gold);
        _comparisonTitle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _legend = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _legend.AddThemeConstantOverride("h_separation", 14);
        heading.AddChild(_legend);
        _comparison = new ColonyComparison
        {
            Name = "CourbesComparees", CustomMinimumSize = new Vector2(0, 230), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Surface = CardFill,
        };
        column.AddChild(_comparison);
        _comparisonNote = DashboardStyle.Text(column, "", 11, DashboardStyle.Muted, true);
    }

    /// <summary>La teinte d'une colonie dans la comparaison (les huit premières seulement).</summary>
    private static Color? Hue(int index) => index < ColonyComparison.Palette.Length ? ColonyComparison.Palette[index] : null;

    private void RefreshComparison(WorldState world, ColonyHistory history)
    {
        _comparisonCard.Visible = world.Colonies.Count >= 2;
        if (!_comparisonCard.Visible) return;
        int shown = Math.Min(world.Colonies.Count, ColonyComparison.Palette.Length);
        var series = new List<ColonyComparison.Series>(shown);
        for (int i = 0; i < shown; i++)
            series.Add(new ColonyComparison.Series(world.Colonies[i].Name, Hue(i)!.Value, history.Of(world.Colonies[i])));
        ColonyMetric metric = _metric;
        _comparison.SetData(series, metric, value => Format(metric, value), Ceiling(metric));
        _comparisonTitle.Text = $"COMPARAISON DES COLONIES · {Caption(metric)}";
        _comparisonNote.Text = world.Colonies.Count > shown
            ? $"Les {shown} premiers empires ; les {world.Colonies.Count - shown} autres ont leur courbe dans leur carte." : "";
        _comparisonNote.Visible = _comparisonNote.Text.Length > 0;

        string stamp = string.Join("|", series.Select(s => s.Name));
        if (stamp == _legendStamp) return;
        _legendStamp = stamp;
        foreach (Node child in _legend.GetChildren()) { _legend.RemoveChild(child); child.QueueFree(); }
        foreach (ColonyComparison.Series line in series)
        {
            var entry = DashboardStyle.Row(_legend, 6);
            entry.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            entry.AddChild(new ColorRect
            {
                Color = line.Color, CustomMinimumSize = new Vector2(14, 3), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            DashboardStyle.Text(entry, line.Name, 12, DashboardStyle.Ink);
        }
    }

    private void ResizePanels()
    {
        _body.OffsetTop = InterfaceLayout.For(_root.Size, showStocks: false).ContentTop;
        _grid.Columns = Math.Clamp((int)((_root.Size.X - 32 + 10) / (CardWidth + 10)), 1, 5);
    }

    public void ShowMetric(ColonyMetric metric)
    {
        _metric = metric;
        foreach (var (choice, button) in _metricButtons) button.SetPressedNoSignal(choice == metric);
        if (_world is not null && _lastHistory is not null) Refresh(_world, _lastHistory, _lastObserved, _lastPace);
    }

    public void Refresh(WorldState world, ColonyHistory history, int observed, StatsPace pace)
    {
        _world = world; _lastHistory = history; _lastObserved = observed; _lastPace = pace;
        while (_cards.Count < world.Colonies.Count)
        {
            int index = _cards.Count;
            var card = new ColonyCard { Name = $"CarteColonie{index}" };
            card.ObserveRequested += () => ObserveRequested?.Invoke(index);
            _grid.AddChild(card);
            _cards.Add(card);
        }
        _empty.Visible = world.Colonies.Count == 0;

        // Les courbes ne changent qu'avec un nouveau relevé (une fois par jour de jeu) ou un autre choix de courbe.
        bool curves = history.Version != _curvesVersion || _metric != _curvesMetric;
        _curvesVersion = history.Version; _curvesMetric = _metric;
        for (int i = 0; i < world.Colonies.Count; i++)
            _cards[i].Refresh(world, world.Colonies[i], i, i == observed, history, _metric, curves);
        if (curves) RefreshComparison(world, history);
        RefreshSummary(world, history, pace, curves);
    }

    private void RefreshSummary(WorldState world, ColonyHistory history, StatsPace pace, bool curves)
    {
        int population = world.Colonies.Sum(c => c.Members.Count);
        _population.Value.Text = population.ToString("N0");
        SetChange(_population.Detail, history.World, ColonyMetric.Population);
        if (curves) _worldCurve.SetData(history.World, ColonyMetric.Population, 1);

        int alive = world.Colonies.Count(c => c.Members.Count > 0), extinct = world.Colonies.Count - alive;
        _colonies.Value.Text = alive.ToString();
        _colonies.Detail.Text = world.Colonies.Count == 0 ? "aucune fondée" : extinct == 0 ? "toutes habitées" : $"{extinct} éteinte{(extinct > 1 ? "s" : "")}";
        _colonies.Detail.AddThemeColorOverride("font_color", extinct > 0 ? DashboardStyle.Warning : DashboardStyle.Muted);

        _caravans.Value.Text = world.Caravans.Count.ToString();
        _caravans.Detail.Text = $"{world.CompletedCaravans:N0} voyage{(world.CompletedCaravans > 1 ? "s" : "")} mené{(world.CompletedCaravans > 1 ? "s" : "")}";

        var causes = world.Colonies.SelectMany(c => c.Settlements).SelectMany(s => s.Deaths).GroupBy(d => d.Cause)
            .Select(g => (Cause: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ToList();
        _graves.Value.Text = world.Colonies.Sum(c => c.TotalDeaths).ToString("N0");
        _graves.Detail.Text = causes.Count == 0 ? "aucun décès" : string.Join(" · ", causes.Take(2).Select(c => $"{c.Cause} {c.Count}"));
        _graves.Detail.AddThemeColorOverride("font_color", causes.Any(c => c.Cause == "faim") ? DashboardStyle.Warning : DashboardStyle.Muted);
        _graves.Card.TooltipText = causes.Count == 0 ? "Personne n'est mort pour le moment."
            : "Causes des décès :\n" + string.Join("\n", causes.Select(c => $"{c.Cause} : {c.Count}"));

        bool behind = !pace.Paused && pace.Multiplier < (int)GameSpeed.Fulgurante * 0.9;
        _pace.Value.Text = pace.Paused ? "En pause" : $"×{pace.Multiplier:0}";
        _pace.Detail.Text = pace.Paused ? "Espace pour reprendre"
            : behind ? "×200 visé : la machine ne suit pas"
            : $"une année toutes les {TimeConstants.TicksPerYear / (Math.Max(1, pace.Multiplier) * TimeConstants.TicksPerSecond):0.0} s";
        _pace.Detail.AddThemeColorOverride("font_color", behind ? DashboardStyle.Warning : DashboardStyle.Muted);
        _pace.Card.TooltipText = "Vitesse mesurée chaque demi-seconde, puis lissée.\nSans carte à dessiner, la simulation dispose de presque tout le temps de calcul ;\n"
            + "si les empires deviennent très grands, la vitesse réelle peut passer sous ×200.";

        _elapsed.Value.Text = Elapsed(pace.Day - pace.StartDay);
        _elapsed.Detail.Text = $"depuis {Sparkline.DateOf(pace.StartDay)}";
    }

    /// <summary>L'écart avec le relevé d'il y a un an (ou avec le premier relevé si l'historique est plus court).</summary>
    private static void SetChange(Label label, IReadOnlyList<ColonyHistory.Sample> samples, ColonyMetric metric)
    {
        if (ColonyHistory.YearAgo(samples) is not { } then)
        {
            label.Text = "premier relevé";
            label.AddThemeColorOverride("font_color", DashboardStyle.Muted);
            return;
        }
        ColonyHistory.Sample now = samples[^1];
        float delta = now.Value(metric) - then.Value(metric);
        bool fullYear = now.Day - then.Day >= TimeConstants.DaysPerYear;
        label.Text = $"{Change(metric, delta)} {(fullYear ? "en un an" : $"depuis {Sparkline.DateOf(then.Day)}")}";
        label.AddThemeColorOverride("font_color", Math.Abs(delta) < 0.005f ? DashboardStyle.Muted : delta > 0 ? DashboardStyle.Mint : DashboardStyle.Warning);
    }

    private static string Elapsed(long days)
    {
        long years = days / TimeConstants.DaysPerYear, rest = days % TimeConstants.DaysPerYear;
        if (years == 0) return $"+{rest} j";
        return $"+{years} an{(years > 1 ? "s" : "")}" + (rest > 0 ? $" {rest} j" : "");
    }

    private static string Caption(ColonyMetric metric) => metric switch
    {
        ColonyMetric.Population => "HABITANTS", ColonyMetric.FoodDays => "RÉSERVES DE REPAS", ColonyMetric.Mood => "HUMEUR", _ => "PIÈCES",
    };

    private static string Format(ColonyMetric metric, float value) => metric switch
    {
        ColonyMetric.FoodDays => $"{value:0.0} j",
        ColonyMetric.Mood => $"{value * 100:0} %",
        _ => ((int)Math.Round(value)).ToString("N0"),
    };

    /// <summary>La valeur en mots, pour la lecture au survol des courbes.</summary>
    private static string Spoken(ColonyMetric metric, float value) => metric switch
    {
        ColonyMetric.Population => $"{value:N0} habitant{(value >= 2 ? "s" : "")}",
        ColonyMetric.FoodDays => $"{value:0.0} j de repas",
        ColonyMetric.Mood => $"humeur {value * 100:0} %",
        _ => $"{value:N0} pièces",
    };

    private static string Change(ColonyMetric metric, float delta) => metric switch
    {
        ColonyMetric.FoodDays => $"{delta:+0.0;−0.0;0} j",
        ColonyMetric.Mood => $"{delta * 100:+0;−0;0} pts",
        _ => $"{delta:+#,0;−#,0;0}",
    };

    /// <summary>Le haut de l'échelle d'une courbe vide ou plate : 100 % pour l'humeur, quelques jours pour les réserves.</summary>
    private static float Ceiling(ColonyMetric metric) => metric switch
    {
        ColonyMetric.Mood => 1, ColonyMetric.FoodDays => 4, ColonyMetric.Population => 10, _ => 100,
    };

    private sealed record Figure(PanelContainer Card, Label Value, Label Detail);

    private static Figure Tile(Container parent, string title, float stretch, out HBoxContainer row)
    {
        PanelContainer card = DashboardStyle.Card(parent, 10);
        card.SizeFlagsStretchRatio = stretch;
        card.MouseFilter = Control.MouseFilterEnum.Pass;
        row = DashboardStyle.Row(card, 10);
        var column = MenuStyle.Column(row, 1);
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.CustomMinimumSize = new Vector2(96, 0);
        DashboardStyle.Text(column, title, 10, DashboardStyle.Muted);
        Label value = DashboardStyle.Text(column, "—", 22, DashboardStyle.Ink);
        Label detail = DashboardStyle.Text(column, "", 11, DashboardStyle.Muted);
        foreach (Label label in new[] { value, detail })
        {
            label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        return new Figure(card, value, detail);
    }

    /// <summary>Une colonie : qui elle est, ce qui l'inquiète, sa courbe, ses chiffres et sa dernière pensée.</summary>
    private sealed partial class ColonyCard : PanelContainer
    {
        public event Action? ObserveRequested;

        private static readonly string[] FigureNames = ["Habitants", "Humeur", "Réserves", "Pièces", "Bâtiments", "Bêtes", "Malades", "Décès", "Jalons"];

        private StyleBoxFlat _style = null!;
        private ColorRect _stripe = null!;
        private TextureRect _portrait = null!;
        private Label _name = null!, _people = null!, _caption = null!, _hero = null!, _change = null!, _thought = null!;
        private HFlowContainer _alerts = null!;
        private string _alertStamp = "";
        private Sparkline _curve = null!;
        private readonly Dictionary<string, Label> _figures = [];
        private Colony? _portraitOf;

        public override void _Ready()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(CardWidth, 0);
            _style = MenuStyle.Box(CardFill, MenuStyle.Edge, 14);
            AddThemeStyleboxOverride("panel", _style);
            var column = MenuStyle.Column(this, 8);

            var heading = DashboardStyle.Row(column, 10);
            // Le liseré reprend la teinte de la colonie dans la comparaison : c'est la légende de sa courbe.
            _stripe = new ColorRect { CustomMinimumSize = new Vector2(3, 40), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            heading.AddChild(_stripe);
            _portrait = new TextureRect
            {
                CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest,
                MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            heading.AddChild(_portrait);
            var identity = MenuStyle.Column(heading, 0);
            identity.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _name = DashboardStyle.Text(identity, "", 17, DashboardStyle.Ink);
            _people = DashboardStyle.Text(identity, "", 12, DashboardStyle.Muted);
            foreach (Label label in new[] { _name, _people })
            {
                label.ClipText = true;
                label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            }
            Button observe = WorldPanel.Chip("Observer", "Revenir à la carte de cet empire, à la vitesse d'avant");
            observe.Name = "ObserverColonie";
            observe.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            observe.Pressed += () => ObserveRequested?.Invoke();
            heading.AddChild(observe);

            _alerts = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, 22) };
            _alerts.AddThemeConstantOverride("h_separation", 6);
            _alerts.AddThemeConstantOverride("v_separation", 4);
            column.AddChild(_alerts);

            var hero = DashboardStyle.Row(column, 12);
            var number = MenuStyle.Column(hero, 0);
            number.CustomMinimumSize = new Vector2(118, 0);
            number.SizeFlagsHorizontal = SizeFlags.Fill;
            _caption = DashboardStyle.Text(number, "", 10, DashboardStyle.Muted);
            _hero = DashboardStyle.Text(number, "", 30, DashboardStyle.Ink);
            _change = DashboardStyle.Text(number, "", 11, DashboardStyle.Muted);
            _curve = new Sparkline
            {
                Name = "CourbeColonie", CustomMinimumSize = new Vector2(120, 72), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Surface = CardFill,
            };
            hero.AddChild(_curve);

            var figures = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Pass };
            figures.AddThemeConstantOverride("h_separation", 10);
            figures.AddThemeConstantOverride("v_separation", 6);
            column.AddChild(figures);
            foreach (string name in FigureNames)
            {
                var cell = MenuStyle.Column(figures, 0);
                cell.MouseFilter = MouseFilterEnum.Pass;
                var headingRow = DashboardStyle.Row(cell, 4);
                Texture2D? icon = name switch
                {
                    "Réserves" => ResourceIcons.Get(ResourceType.Grain), "Pièces" => ResourceIcons.Get(ResourceType.Coins),
                    "Bâtiments" => BuildingSprites.Get("Hut"), "Bêtes" => ResourceIcons.Get(ResourceType.Sheep),
                    "Malades" => VillageArt.Status("sick"), "Jalons" => ResourceIcons.Get("Milestone"), _ => null,
                };
                if (icon is not null) DashboardStyle.Picture(headingRow, icon, 14);
                DashboardStyle.Text(headingRow, name.ToUpperInvariant(), 10, DashboardStyle.Muted);
                Label value = DashboardStyle.Text(cell, "—", 15, DashboardStyle.Ink);
                value.ClipText = true;
                value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                _figures[name] = value;
            }

            _thought = DashboardStyle.Text(column, "", 12, DashboardStyle.Muted, true);
            _thought.MaxLinesVisible = 2;
            _thought.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _thought.CustomMinimumSize = new Vector2(0, 34);
        }

        public void Refresh(WorldState world, Colony colony, int index, bool observed, ColonyHistory history, ColonyMetric metric, bool curves)
        {
            _style.BorderColor = observed ? ArtDirection.Brass : MenuStyle.Edge;
            if (_portraitOf != colony)
            {
                _portraitOf = colony;
                _stripe.Color = Hue(index) ?? Colors.Transparent;
                _portrait.Texture = PeoplesSprites.Portrait(new ColonistAppearance(index, PeoplesSprites.LookOf(colony.Species),
                    BiomeVisuals.At(colony.Map, colony.CampX, colony.CampY)));
                _name.Text = colony.Name;
                _people.Text = $"{colony.Species.Plural} · {world.WorldMap.Grid[world.WorldMap.TileOf(colony)].Describe()}";
                _people.TooltipText = _people.Text;
            }

            int members = colony.Members.Count, children = colony.Children;
            float days = ColonyHistory.FoodDays(colony);
            RefreshAlerts(colony, members, days);

            IReadOnlyList<ColonyHistory.Sample> samples = history.Of(colony);
            ColonyHistory.Sample today = ColonyHistory.Measure(colony, world.Clock.TotalDays);
            _caption.Text = Caption(metric);
            _curve.LineColor = DashboardStyle.MetricTint(metric);
            _hero.Text = Format(metric, today.Value(metric));
            _hero.AddThemeColorOverride("font_color", metric == ColonyMetric.FoodDays && members > 0 && days < 2 ? DashboardStyle.Warning : DashboardStyle.Ink);
            SetChange(_change, samples, metric);
            if (curves)
            {
                _curve.Format = value => Spoken(metric, value);
                _curve.SetData(samples, metric, Ceiling(metric));
            }

            SetFigure("Habitants", children > 0 ? $"{members} · {children} enf." : members.ToString(),
                $"{members - children} adultes et adolescents · {children} enfants · {colony.Homeless} sans hutte");
            SetFigure("Humeur", members == 0 ? "—" : $"{today.Mood * 100:0} %", "Humeur moyenne des habitants");
            SetFigure("Réserves", members == 0 ? "—" : $"{days:0.0} j", "Jours de repas en réserve", members > 0 && days < 2);
            SetFigure("Pièces", today.Coins.ToString("N0"), "Trésorerie");
            int complete = colony.Buildings.Count(b => b.IsComplete), sites = colony.Buildings.Count - complete;
            SetFigure("Bâtiments", sites > 0 ? $"{complete} +{sites}" : complete.ToString(), $"{complete} achevés · {sites} en chantier");
            int pens = Husbandry.Pens(colony), animals = Husbandry.Animals(colony);
            SetFigure("Bêtes", pens == 0 && animals == 0 ? "—" : animals.ToString(),
                $"{colony.Chickens} poules · {colony.Sheep} moutons · {colony.Cows} vaches · {pens} enclos");
            SetFigure("Malades", Health.PatientCount(colony).ToString(), "Habitants malades ou blessés");
            var deaths = colony.Settlements.SelectMany(s => s.Deaths).ToList();
            var causes = deaths.GroupBy(d => d.Cause).Select(g => $"{g.Key} : {g.Count()}");
            SetFigure("Décès", colony.TotalDeaths.ToString(), colony.TotalDeaths == 0 ? "Aucun décès dans l'empire" : "Décès cumulés de l'empire depuis le début\nCauses :\n" + string.Join("\n", causes),
                deaths.Any(d => d.Cause == "faim"));
            SetFigure("Jalons", $"{Milestones.Reached(colony)} / {Milestones.All.Count}", "Objectifs atteints (détail dans l'écran Économie)");

            Thought? last = colony.Thoughts.Count > 0 ? colony.Thoughts[^1] : null;
            _thought.Text = last is null ? "L'empire prend ses marques…" : $"{Sparkline.DateOf(last.Ticks / TimeConstants.TicksPerDay)} · {last.Text}";
            _thought.TooltipText = _thought.Text;
        }

        private void SetFigure(string figure, string text, string hint, bool warning = false)
        {
            Label label = _figures[figure];
            label.Text = text;
            label.AddThemeColorOverride("font_color", warning ? DashboardStyle.Warning : DashboardStyle.Ink);
            ((Control)label.GetParent()).TooltipText = hint;
        }

        /// <summary>Ce qui demande l'attention, en toutes lettres (jamais par la seule couleur).</summary>
        private void RefreshAlerts(Colony colony, int members, float days)
        {
            var alerts = new List<(string Text, Color Color)>();
            if (members == 0) alerts.Add(("Empire éteint", DashboardStyle.Warning));
            else if (days < 2) alerts.Add(("Réserves basses", DashboardStyle.Warning));
            if (colony.ColdSnapDaysLeft > 0) alerts.Add(("Vague de froid", ArtDirection.Brass));
            if (colony.DroughtDaysLeft > 0) alerts.Add(("Sécheresse", ArtDirection.Brass));
            if (colony.Prayers.Pending.Any()) alerts.Add(("Prière en attente", ArtDirection.Brass));
            if (alerts.Count == 0) alerts.Add(("Rien à signaler", DashboardStyle.Muted));
            string stamp = string.Join("|", alerts.Select(a => a.Text));
            if (stamp == _alertStamp) return;
            _alertStamp = stamp;
            foreach (Node child in _alerts.GetChildren()) { _alerts.RemoveChild(child); child.QueueFree(); }
            foreach (var (text, color) in alerts) DashboardStyle.Pill(_alerts, text, color);
        }
    }
}
