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
/// Visuel volontairement simple ; il sera refait avec les illustrations.
/// </summary>
public partial class PrayerPanel : CanvasLayer
{
    private static readonly Color Ink = Color.Color8(230, 237, 221);
    private static readonly Color Muted = Color.Color8(150, 174, 162);
    private static readonly Color Gold = Color.Color8(226, 190, 119);
    private static readonly Color Mint = Color.Color8(133, 198, 167);

    private WorldState _world = null!;
    private VBoxContainer _stack = null!;
    private string _stamp = "";

    public void Init(WorldState world) => _world = world;

    public override void _Ready()
    {
        Layer = 12;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // En haut à droite, sous les stocks : visible sans cacher la carte.
        _stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 10);
        root.AddChild(_stack);
        _stack.AnchorLeft = 1; _stack.AnchorRight = 1; _stack.AnchorTop = 0; _stack.AnchorBottom = 0;
        _stack.OffsetLeft = -396; _stack.OffsetRight = -16; _stack.OffsetTop = 184;
    }

    public override void _Process(double delta)
    {
        // On ne reconstruit les cartes que quand la liste des prières en attente change.
        List<Prayer> pending = _world.Colonies.SelectMany(c => c.Prayers.Pending).ToList();
        string stamp = string.Join(",", pending.Select(p => $"{p.Colony.Name}#{p.Id}:{p.Colony.Prayers.AutoApprove.Contains(p.Kind)}"));
        if (stamp == _stamp)
            return;
        _stamp = stamp;

        foreach (Node child in _stack.GetChildren())
            child.QueueFree();
        foreach (Prayer prayer in pending)
            _stack.AddChild(BuildCard(prayer));
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
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontOverride("font", ArtDirection.BodyFont);
        return label;
    }

    private static Button MakeButton(string text, Color color)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(120, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", Ink);
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }
}
