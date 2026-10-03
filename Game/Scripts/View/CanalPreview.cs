using Godot;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Scène de validation graphique isolée : les canaux sont posés sur une carte de démonstration.</summary>
public partial class CanalPreview : Node2D
{
    private int _frames = 12;
    private string? _capture;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--capture=")) _capture = arg["--capture=".Length..];
        var title = new Label
        {
            Text = "Canaux d'irrigation — 16 formes sèches (haut), 16 en eau (bas)",
            Position = new Vector2(32, 14),
        };
        title.AddThemeColorOverride("font_color", ArtDirection.Cream);
        title.AddThemeFontOverride("font", ArtDirection.BodyFont);
        title.AddThemeFontSizeOverride("font_size", 22);
        AddChild(title);
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 8; column++)
        {
            int mask = row % 2 * 8 + column;
            bool wet = row >= 2;
            var map = MapGenerator.Generate(64, 64, 42);
            var (x, y) = DryPatch(map);
            map.DigCanal(x, y);
            foreach (var (dx, dy, bit) in new[] { (0, -1, 1), (1, 0, 2), (0, 1, 4), (-1, 0, 8) })
                if ((mask & bit) != 0)
                {
                    map.DigCanal(x + dx, y + dy);
                    if (wet) map.FillCanal(x + dx, y + dy);
                }
            if (wet) map.FillCanal(x, y);
            // Même chemin que ChunkView, y compris sélection des PNG par masque et par état.
            var image = Image.CreateFromData(96, 96, false, Image.Format.Rgba8,
                TerrainPainter.Paint(map, x - 1, y - 1, 3, 3));
            var sprite = new Sprite2D
            {
                Texture = ImageTexture.CreateFromImage(image), Centered = false,
                Position = new Vector2(32 + column * 192, 64 + row * 192), Scale = Vector2.One * 2,
            };
            AddChild(sprite);
            var label = new Label
            {
                Text = $"{(wet ? "Eau" : "Sec")} · {mask}", Position = sprite.Position + new Vector2(5, 4),
            };
            label.AddThemeColorOverride("font_color", ArtDirection.Cream);
            label.AddThemeColorOverride("font_outline_color", ArtDirection.Charcoal);
            label.AddThemeConstantOverride("outline_size", 4);
            AddChild(label);
        }
    }

    private static (int X, int Y) DryPatch(LocalMap map)
    {
        for (int y = 3; y < map.Height - 3; y++)
        for (int x = 3; x < map.Width - 3; x++)
        {
            bool dry = true;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                dry &= !map.HasWater(x + dx, y + dy) && !map.IsMountain(x + dx, y + dy);
            if (dry) return (x, y);
        }
        throw new System.InvalidOperationException("Aucune parcelle sèche pour la validation des canaux.");
    }

    public override void _Process(double delta)
    {
        if (_capture is null || --_frames != 0) return;
        GetViewport().GetTexture().GetImage().SavePng(_capture);
        GetTree().Quit();
    }
}
