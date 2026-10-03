using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using GodColony.View;

namespace GodColony;

/// <summary>
/// Le monde et son économie : choisir quelle colonie observer, voir ce que vaut chaque bien pour elle,
/// les caravanes en route et ses derniers échanges. Visuel volontairement simple, à refaire avec les illustrations.
/// </summary>
public partial class WorldPanel : CanvasLayer
{
    private static readonly Color Ink = Color.Color8(230, 237, 221);
    private static readonly Color Muted = Color.Color8(150, 174, 162);
    private static readonly Color Gold = Color.Color8(226, 190, 119);
    private static readonly Color Mint = Color.Color8(133, 198, 167);
    private static readonly Color Panel = new(0.065f, 0.115f, 0.105f, 0.97f);
    private static readonly Color Edge = Color.Color8(65, 89, 75);

    public event Action<int>? ColonyRequested;

    private WorldState _world = null!;
    private VBoxContainer _stack = null!, _details = null!;
    private HBoxContainer _colonyRow = null!;
    private readonly List<Button> _colonyButtons = [];
    private Button _economyButton = null!;
    private double _sinceRefresh = 1;
    private bool _open;
    private int _observed;

    /// <summary>Le détail de l'économie est-il déplié ?</summary>
    public bool Open
    {
        get => _open;
        set { _open = value; _sinceRefresh = 1; }
    }

    public void Init(WorldState world) => _world = world;

    public override void _Ready()
    {
        Layer = 11;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // À gauche, sous les stocks : discret quand il est replié.
        _stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 8);
        root.AddChild(_stack);
        _stack.AnchorLeft = 0; _stack.AnchorRight = 0; _stack.AnchorTop = 0; _stack.AnchorBottom = 0;
        _stack.OffsetLeft = 16; _stack.OffsetRight = 420; _stack.OffsetTop = 184;

        _colonyRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _colonyRow.AddThemeConstantOverride("separation", 6);
        _stack.AddChild(_colonyRow);

        _details = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _details.AddThemeConstantOverride("separation", 4);
        _stack.AddChild(_details);
    }

    /// <summary>La colonie actuellement observée (le bouton correspondant est enfoncé).</summary>
    public int Observed
    {
        get => _observed;
        set { _observed = value; _sinceRefresh = 1; }
    }

    public override void _Process(double delta)
    {
        if (_world is null)
            return;
        _sinceRefresh += delta;
        if (_sinceRefresh < 0.4)
            return;
        _sinceRefresh = 0;
        Refresh();
    }

    private void Refresh()
    {
        // Les boutons des colonies : un par peuple, plus « Monde » pour déplier l'économie.
        if (_colonyButtons.Count != _world.Colonies.Count)
        {
            foreach (Node child in _colonyRow.GetChildren())
                child.QueueFree();
            _colonyButtons.Clear();
            for (int i = 0; i < _world.Colonies.Count; i++)
            {
                int index = i;
                Button button = Chip($"{_world.Colonies[i].Name} · {_world.Colonies[i].Species.Plural}");
                button.Name = $"Colonie{i}";
                button.ToggleMode = true;
                button.Pressed += () => ColonyRequested?.Invoke(index);
                _colonyRow.AddChild(button);
                _colonyButtons.Add(button);
            }
            _economyButton = Chip("Économie");
            _economyButton.Name = "Economie";
            _economyButton.ToggleMode = true;
            _economyButton.Pressed += () => Open = !Open;
            _colonyRow.AddChild(_economyButton);
        }
        for (int i = 0; i < _colonyButtons.Count; i++)
            _colonyButtons[i].SetPressedNoSignal(i == _observed);
        _economyButton.SetPressedNoSignal(_open);
        _colonyRow.Visible = _world.Colonies.Count > 1;
        if (_world.Colonies.Count <= 1)
            _open = false;

        foreach (Node child in _details.GetChildren())
            child.QueueFree();
        if (_open)
            _details.AddChild(BuildDetails(_world.Colonies[_observed]));
    }

    private Control BuildDetails(Colony colony)
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Panel, BorderColor = Edge,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 9, CornerRadiusTopRight = 9, CornerRadiusBottomLeft = 9, CornerRadiusBottomRight = 9,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 5);
        card.AddChild(column);

        column.AddChild(Text($"Économie de {colony.Name}", 15, Gold));
        column.AddChild(Text($"{colony.Stock.Get(ResourceType.Coins)} pièces · travail épargné par le commerce : {colony.LifetimeTradeGainHours:0} h", 12, Muted));

        // Chaque bien : ce que la colonie en a, ce qu'il lui coûte à produire, ce qu'il vaut pour elle en ce moment.
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 2);
        foreach (string header in new[] { "Bien", "Stock", "Coût (h)", "Valeur (h)" })
            grid.AddChild(Text(header, 11, Muted));
        foreach (ResourceType good in Economy.Tradable)
        {
            double cost = Economy.Cost(colony, good), value = Economy.Value(colony, good);
            bool known = colony.Labor.HoursPerUnit(good) is not null;
            var name = new HBoxContainer();
            name.AddThemeConstantOverride("separation", 5);
            name.AddChild(new TextureRect
            {
                Texture = ResourceIcons.Get(good), CustomMinimumSize = new Vector2(16, 16), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            });
            name.AddChild(Text(ResourceIcons.Name(good), 12, Ink));
            grid.AddChild(name);
            grid.AddChild(Text(colony.Stock.Get(good).ToString(), 12, Ink));
            grid.AddChild(Text(known ? $"{cost:0.0}" : $"~{cost:0.0}", 12, known ? Ink : Muted));
            grid.AddChild(Text($"{value:0.0}", 12, value > cost * 1.2 ? Gold : value < cost * 0.8 ? Mint : Ink));
        }
        column.AddChild(grid);

        // Les caravanes en route, tous peuples confondus.
        long now = _world.Clock.Ticks;
        var caravans = _world.Caravans.ToList();
        column.AddChild(Text(caravans.Count == 0 ? "Aucune caravane en route." : "Caravanes en route", 12, Muted));
        foreach (Caravan caravan in caravans)
        {
            string phase = caravan.State == CaravanState.Outbound ? "à l'aller" : "au retour";
            column.AddChild(Text($"{caravan.From.Name} → {caravan.To.Name} · {phase} · {caravan.Progress(now) * 100:0} %", 12, Ink));
        }

        // Les derniers échanges de la colonie.
        var records = colony.Trades.TakeLast(4).Reverse().ToList();
        if (records.Count > 0)
            column.AddChild(Text("Derniers échanges", 12, Muted));
        foreach (TradeRecord record in records)
        {
            string lines = string.Join(", ", record.Lines.Select(l => $"{(l.IsSale ? "vend" : "achète")} {l.Units} {Trade.GoodName(l.Good, l.Units)}"));
            var label = Text($"J{record.Ticks / TimeConstants.TicksPerDay + 1} {(record.WeSent ? "→" : "←")} {record.Partner} : {lines} · {record.NetCoins:+0;-0;0} pièces", 11, Ink);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(360, 0);
            column.AddChild(label);
        }
        return card;
    }

    private static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontOverride("font", ArtDirection.BodyFont);
        return label;
    }

    private static Button Chip(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 30) };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", Muted);
        button.AddThemeColorOverride("font_pressed_color", Gold);
        button.AddThemeColorOverride("font_hover_color", Ink);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxFlat
            {
                BgColor = state.Contains("pressed") ? new Color(0.12f, 0.2f, 0.17f, 0.99f) : Panel,
                BorderColor = state.Contains("pressed") ? Gold : Edge,
                BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 15, CornerRadiusTopRight = 15, CornerRadiusBottomLeft = 15, CornerRadiusBottomRight = 15,
                ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 3, ContentMarginBottom = 3,
            });
        return button;
    }
}
