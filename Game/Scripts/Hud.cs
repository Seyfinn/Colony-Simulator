using Godot;

namespace GodColony;

/// <summary>Bandeau d'informations en haut de l'écran.</summary>
public partial class Hud : CanvasLayer
{
    private Label _status = null!;
    private Label _tileInfo = null!;

    public override void _Ready()
    {
        var panel = new PanelContainer { Position = new Vector2(12, 12) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.78f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var column = new VBoxContainer();
        panel.AddChild(column);

        _status = new Label();
        _status.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(_status);

        _tileInfo = new Label();
        _tileInfo.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        column.AddChild(_tileInfo);

        var help = new Label
        {
            Text = "Espace : pause   ·   1, 2, 3 : vitesses   ·   ZQSD / clic droit : déplacer   ·   molette : zoom   ·   clic gauche sur la roche : miner (test)",
        };
        help.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        help.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(help);
    }

    public void SetStatus(string text) => _status.Text = text;

    public void SetTileInfo(string text) => _tileInfo.Text = text;
}
