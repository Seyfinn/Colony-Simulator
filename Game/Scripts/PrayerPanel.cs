using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.View;

namespace GodColony;

/// <summary>
/// Les prières en attente : la colonie demande au joueur-dieu l'autorisation d'une grande décision.
/// Une petite pastille signale qu'une prière attend ; le détail s'ouvre d'un clic, sans jamais interrompre le jeu.
/// Visuel volontairement simple ; il sera refait avec les illustrations.
/// </summary>
public partial class PrayerPanel : CanvasLayer
{
    public event Action? Expanded;
    private static readonly Color Ink = Color.Color8(230, 237, 221);
    private static readonly Color Muted = Color.Color8(150, 174, 162);
    private static readonly Color Gold = Color.Color8(226, 190, 119);
    private static readonly Color Mint = Color.Color8(133, 198, 167);

    private WorldState _world = null!;
    private VBoxContainer _stack = null!, _cards = null!;
    private Control _root = null!;
    private ScrollContainer _scroll = null!;
    private Button _badge = null!;
    private string _stamp = "";
    private double _time;
    private bool _stocksVisible = true;

    /// <summary>Le détail des prières est-il déplié ?</summary>
    public bool Open
    {
        get => _open;
        set { _open = value; _stamp = ""; if (value) Expanded?.Invoke(); }
    }
    private bool _open;

    public void Init(WorldState world) => _world = world;

    public override void _Ready()
    {
        Layer = 12;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = MenuStyle.Theme() };
        _root = root;
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // En haut à droite, sous les stocks : visible sans cacher la carte.
        _stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 10);
        root.AddChild(_stack);
        _stack.AnchorLeft = 1; _stack.AnchorRight = 1; _stack.AnchorTop = 0; _stack.AnchorBottom = 0;
        _stack.AnchorBottom = 1;
        _stack.OffsetLeft = -396; _stack.OffsetRight = -16; _stack.OffsetBottom = -60;
        _stack.Alignment = BoxContainer.AlignmentMode.Begin;

        // La pastille : discrète, alignée à droite, visible seulement quand une prière attend.
        _badge = new Button { Name = "PastillePriere", Visible = false, ToggleMode = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, CustomMinimumSize = new Vector2(0, 32), MouseDefaultCursorShape = Control.CursorShape.PointingHand, FocusMode = Control.FocusModeEnum.None };
        _badge.AddThemeFontSizeOverride("font_size", 13);
        _badge.AddThemeColorOverride("font_color", Gold);
        _badge.AddThemeColorOverride("font_hover_color", Ink);
        _badge.AddThemeStyleboxOverride("normal", BadgeStyle(0.97f));
        _badge.AddThemeStyleboxOverride("hover", BadgeStyle(1f));
        _badge.AddThemeStyleboxOverride("pressed", BadgeStyle(1f));
        _badge.Pressed += () => Open = !Open;
        _stack.AddChild(_badge);

        _scroll = new ScrollContainer
        {
            Name = "DefilementPrieres", Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseForcePassScrollEvents = false,
        };
        _stack.AddChild(_scroll);
        _cards = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _cards.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _cards.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(_cards);
        root.Resized += ResizePanels;
        ResizePanels();
    }

    private void ResizePanels()
    {
        _stack.OffsetTop = InterfaceLayout.For(_root.Size, _stocksVisible).NavigationTop;
        _stack.OffsetLeft = -16 - InterfaceLayout.SideWidth(_root.Size);
    }

    /// <summary>Sans la rangée des stocks (vue chiffrée, fondation), la pastille remonte avec la navigation.</summary>
    public void SetStocksVisible(bool visible)
    {
        if (_stocksVisible == visible) return;
        _stocksVisible = visible;
        if (_root is not null) ResizePanels();
    }

    public void SetMapOverlay(bool open) => _stack.Visible = !open;

    private static StyleBoxFlat BadgeStyle(float alpha) => new()
    {
        BgColor = new Color(0.065f, 0.115f, 0.105f, alpha), BorderColor = Gold,
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 15, CornerRadiusTopRight = 15, CornerRadiusBottomLeft = 15, CornerRadiusBottomRight = 15,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 4, ContentMarginBottom = 4,
    };

    public override void _Process(double delta)
    {
        if (_world is null) return;
        List<Prayer> pending = _world.Colonies.SelectMany(c => c.Prayers.Pending).ToList();

        if (pending.Count == 0)
            _open = false;

        // La pastille respire doucement tant qu'une prière attend et que son détail est replié.
        _time += delta;
        _badge.Modulate = new Color(1, 1, 1, pending.Count > 0 && !_open ? 0.78f + 0.22f * (float)Math.Sin(_time * 3) : 1f);

        // On ne reconstruit les cartes que quand la liste des prières en attente (ou l'état déplié) change.
        string stamp = _open + string.Join(",", pending.Select(p => $"{p.Colony.Name}#{p.Id}:{p.Colony.Prayers.AutoApprove.Contains(p.Kind)}"));
        if (stamp == _stamp)
            return;
        _stamp = stamp;

        _badge.Visible = pending.Count > 0;
        _badge.SetPressedNoSignal(_open);
        _scroll.Visible = _open;
        if (pending.Count > 0)
        {
            string colonies = string.Join(", ", pending.Select(p => p.Colony.Name).Distinct());
            _badge.Text = pending.Count == 1 ? "1 prière en attente" : $"{pending.Count} prières en attente";
            _badge.TooltipText = $"{colonies}\n{(_open ? "Replier les prières" : "Voir les prières et y répondre")} · P";
        }

        foreach (Node child in _cards.GetChildren())
        {
            _cards.RemoveChild(child); child.QueueFree();
        }
        if (_open)
            foreach (Prayer prayer in pending)
                _cards.AddChild(BuildCard(prayer));
    }

    private Control BuildCard(Prayer prayer)
    {
        var card = new PanelContainer { Name = $"Priere{prayer.Id}", MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.065f, 0.115f, 0.105f, 0.97f), BorderColor = Gold,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 9, CornerRadiusTopRight = 9, CornerRadiusBottomLeft = 9, CornerRadiusBottomRight = 9,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
        });

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        card.AddChild(column);

        column.AddChild(MakeLabel($"Prière de {prayer.Colony.Name}", 12, Gold));
        column.AddChild(MakeLabel(prayer.Question, 17, Ink));
        column.AddChild(MakeLabel(prayer.Reason, 13, Muted));

        var auto = new CheckBox { Text = "Toujours accorder ce type de décision", ButtonPressed = prayer.Colony.Prayers.AutoApprove.Contains(prayer.Kind) };
        auto.AddThemeColorOverride("font_color", Muted);
        auto.AddThemeFontSizeOverride("font_size", 12);
        column.AddChild(auto);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        column.AddChild(buttons);
        var grant = MakeButton("Accorder", Mint);
        grant.Name = "Accorder";
        grant.Pressed += () => Answer(prayer, true, auto.ButtonPressed);
        var refuse = MakeButton("Refuser", Color.Color8(222, 140, 120));
        refuse.Name = "Refuser";
        refuse.Pressed += () => Answer(prayer, false, false);
        buttons.AddChild(grant);
        buttons.AddChild(refuse);
        return card;
    }

    private void Answer(Prayer prayer, bool approve, bool alwaysGrant)
    {
        if (alwaysGrant)
            prayer.Colony.Prayers.AutoApprove.Add(prayer.Kind);
        _world.AnswerPrayer(prayer, approve);
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontOverride("font", ArtDirection.BodyFont);
        if (size >= 17) label.AddThemeFontOverride("font", ArtDirection.HeadingFont);
        return label;
    }

    private static Button MakeButton(string text, Color color)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", Ink);
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }
}
