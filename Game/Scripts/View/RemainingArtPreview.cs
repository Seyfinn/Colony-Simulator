using System;
using Godot;

namespace GodColony.View;

/// <summary>Planche de validation et export explicite des livraisons graphiques restantes.</summary>
public partial class RemainingArtPreview : Node2D
{
    private string? _capture;
    private int _frames = 24;
    private double _time;
    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--export-art") RemainingArt.ExportAll();
            if (arg.StartsWith("--capture=")) _capture = arg["--capture=".Length..];
        }
        Label("Barrage : pieux → culées → vanne en montage → terminé", 30, 15);
        for (int stage = 0; stage < 4; stage++)
        for (int side = 0; side < 2; side++)
        {
            var texture = stage == 3 ? BuildingSprites.Get(side == 1 ? "DamSide" : "Dam") : BuildingSprites.DamConstruction((stage + 0.1f) / 3, side == 1);
            Sprite(texture, 40 + stage * 190, 65 + side * 155, 3);
        }
        Label("Monnaie · colonies humain / nain / elfe / orque", 840, 15);
        Sprite(ResourceIcons.Get("Coins"), 850, 65, 4);
        int i = 0;
        foreach (PeopleLook people in Enum.GetValues<PeopleLook>())
        {
            Sprite(AssetLibrary.Get($"world/colony_{people.ToString().ToLowerInvariant()}.png")!, 850 + i % 2 * 190, 155 + i / 2 * 125, 3);
            i++;
        }
        Label("Roue : quatre poses (en bas, animation) · moulin sans roue fixe", 30, 405);
        for (int frame = 0; frame < 4; frame++) Sprite(AssetLibrary.Get($"buildings/mill_wheel_{frame}.png")!, 40 + frame * 125, 460, 3);
        Sprite(BuildingSprites.Get("Mill"), 600, 450, 3);
        Label("Voyageurs : humains, nains, elfes, orques · quatre pas chacun", 30, 695);
        i = 0;
        foreach (PeopleLook people in Enum.GetValues<PeopleLook>())
        {
            var frames = RemainingArt.TraderFrames(people);
            for (int frame = 0; frame < 4; frame++) Sprite(frames[frame], 40 + i * 350 + frame * 75, 745, 2);
            i++;
        }
    }
    private void Label(string text, float x, float y)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y) };
        label.AddThemeColorOverride("font_color", ArtDirection.Cream);
        label.AddThemeFontOverride("font", ArtDirection.BodyFont);
        label.AddThemeFontSizeOverride("font_size", 20);
        AddChild(label);
    }
    private void Sprite(Texture2D image, float x, float y, int scale) => AddChild(new Sprite2D
    {
        Texture = image, Centered = false, Position = new Vector2(x, y), Scale = Vector2.One * scale,
    });
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
        if (AssetLibrary.Get($"buildings/mill_wheel_{(int)(_time * 6) % 4}.png") is { } wheel)
            DrawTextureRect(wheel, new Rect2(840, 470, 72, 120), false);
    }
}
