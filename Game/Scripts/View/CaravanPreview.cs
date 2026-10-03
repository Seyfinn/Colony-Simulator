using Godot;

namespace GodColony.View;

/// <summary>Validation isolée des quatre poses, des deux directions et de la lecture en taille native.</summary>
public partial class CaravanPreview : Node2D
{
    private double _time;
    private int _frames = 24;
    private string? _capture;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--export-caravan") CaravanSprites.Export();
            if (arg.StartsWith("--capture=")) _capture = arg["--capture=".Length..];
        }
        AddLabel("Caravane — deux voyageurs et leur charrette", new Vector2(64, 30), 28);
        AddLabel("Quatre poses, agrandissement ×8 sans lissage", new Vector2(64, 90), 20);
        var frames = CaravanSprites.Get();
        for (int i = 0; i < frames.Length; i++)
        {
            AddLabel($"Image {i}", new Vector2(64 + i * 280, 145), 18);
            AddChild(new Sprite2D { Texture = frames[i], Centered = false,
                Position = new Vector2(64 + i * 280, 180), Scale = Vector2.One * 8 });
        }
        AddLabel("Boucle animée vers la droite et vers la gauche (×4)", new Vector2(64, 370), 20);
        AddLabel("Taille réelle : 24 × 16 pixels", new Vector2(64, 580), 20);
    }

    private void AddLabel(string text, Vector2 position, int size)
    {
        var label = new Label { Text = text, Position = position };
        label.AddThemeFontOverride("font", ArtDirection.BodyFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", ArtDirection.Cream);
        AddChild(label);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
        if (_capture is null || --_frames != 0) return;
        GetViewport().GetTexture().GetImage().SavePng(_capture);
        GetTree().Quit();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(48, 170, 1152, 155), ArtDirection.Charcoal);
        DrawSetTransform(new Vector2(64, 445), 0, Vector2.One * 4);
        CaravanSprites.Draw(this, new Vector2(24 + (float)_time * 16 % 80, 12), _time);
        DrawSetTransform(new Vector2(680, 445), 0, Vector2.One * 4);
        CaravanSprites.Draw(this, new Vector2(100 - (float)_time * 16 % 80, 12), _time, left: true);
        DrawSetTransform(Vector2.Zero);
        CaravanSprites.Draw(this, new Vector2(96, 650), _time);
        CaravanSprites.Draw(this, new Vector2(160, 650), _time, left: true);
    }
}
