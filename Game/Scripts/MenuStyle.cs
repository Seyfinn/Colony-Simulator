using Godot;
using GodColony.View;

namespace GodColony;

/// <summary>Les menus partagent la palette et les polices de l'interface d'observation.</summary>
internal static class MenuStyle
{
    public static readonly Color Ink = Color.Color8(230, 237, 221);
    public static readonly Color Muted = Color.Color8(150, 174, 162);
    public static readonly Color Error = Color.Color8(237, 148, 128);
    public static readonly Color Background = Color.Color8(18, 32, 29);

    public static Theme Theme()
    {
        var theme = new Theme { DefaultFont = ArtDirection.BodyFont, DefaultFontSize = 15 };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "CheckBox", Ink);
        foreach (string type in new[] { "Button", "OptionButton" })
        {
            theme.SetColor("font_color", type, Ink);
            theme.SetColor("font_hover_color", type, ArtDirection.Cream);
            theme.SetColor("font_disabled_color", type, Muted.Darkened(0.35f));
            theme.SetStylebox("normal", type, Box(new Color(0.09f, 0.16f, 0.13f), ArtDirection.Charcoal));
            theme.SetStylebox("hover", type, Box(new Color(0.15f, 0.23f, 0.18f), ArtDirection.Brass));
            theme.SetStylebox("pressed", type, Box(new Color(0.19f, 0.27f, 0.20f), ArtDirection.Brass));
            theme.SetStylebox("disabled", type, Box(Background, ArtDirection.Charcoal));
            var focus = Box(Colors.Transparent, ArtDirection.Brass);
            theme.SetStylebox("focus", type, focus);
        }
        theme.SetStylebox("normal", "LineEdit", Box(new Color(0.045f, 0.085f, 0.07f), ArtDirection.Charcoal));
        theme.SetStylebox("focus", "LineEdit", Box(new Color(0.045f, 0.085f, 0.07f), ArtDirection.Brass));
        theme.SetColor("font_color", "LineEdit", Ink);
        theme.SetColor("font_placeholder_color", "LineEdit", Muted);
        theme.SetStylebox("panel", "PopupMenu", Box(Background, ArtDirection.Brass));
        theme.SetColor("font_color", "PopupMenu", Ink);
        theme.SetStylebox("panel", "PanelContainer", Box(Background, ArtDirection.Charcoal, 22));
        return theme;
    }

    public static StyleBoxFlat Box(Color fill, Color border, int padding = 12) => new()
    {
        BgColor = fill, BorderColor = border,
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 9, CornerRadiusTopRight = 9, CornerRadiusBottomLeft = 9, CornerRadiusBottomRight = 9,
        ContentMarginLeft = padding, ContentMarginRight = padding, ContentMarginTop = padding, ContentMarginBottom = padding,
    };

    public static Label Text(Node parent, string text, int size = 15, Color? color = null, bool wrap = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Ink);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        parent.AddChild(label);
        return label;
    }

    public static Button Button(Node parent, string text, System.Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 42) };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    public static VBoxContainer Column(Node parent, int gap = 12)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", gap);
        parent.AddChild(column);
        return column;
    }

    public static CheckBox Check(Node parent, string text, bool value)
    {
        var check = new CheckBox { Text = text, ButtonPressed = value };
        parent.AddChild(check);
        return check;
    }
}
