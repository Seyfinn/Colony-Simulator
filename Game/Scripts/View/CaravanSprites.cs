using System;
using Godot;

namespace GodColony.View;

/// <summary>Caravane miniature : deux voyageurs, une charrette, quatre poses à 24 × 16.</summary>
public static class CaravanSprites
{
    public const int Width = 24, Height = 16, FrameCount = 4;
    private static ImageTexture[]? _native;
    private static readonly Color Ink = Color.Color8(44, 48, 43), Skin = Color.Color8(225, 181, 132);
    private static readonly Color Leather = Color.Color8(104, 73, 49), Wood = Color.Color8(153, 108, 65);
    private static readonly Color Linen = Color.Color8(225, 207, 169), Gold = Color.Color8(223, 182, 92);
    private static readonly Color Sage = Color.Color8(123, 159, 121), Cloak = Color.Color8(84, 121, 110);

    public static ImageTexture[] Get()
    {
        var supplied = AssetLibrary.Frames("world/caravan_{0}.png", FrameCount);
        if (supplied is not null)
        {
            bool valid = true;
            foreach (var frame in supplied) valid &= frame.GetWidth() == Width && frame.GetHeight() == Height;
            if (valid) return supplied;
        }
        return _native ??= CreateNative();
    }

    /// <summary>Point central du marqueur. La boucle suit le temps fourni ; un temps figé arrête la marche.</summary>
    public static void Draw(CanvasItem target, Vector2 center, double time, bool left = false)
    {
        var frame = Get()[(int)(Math.Max(0, time) * 6) % FrameCount];
        target.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        var rectangle = left
            ? new Rect2(center + new Vector2(12, -8), new Vector2(-24, 16))
            : new Rect2(center - new Vector2(12, 8), new Vector2(24, 16));
        target.DrawTextureRect(frame, rectangle, false);
    }

    /// <summary>Export explicite depuis la scène de validation ; aucun fichier écrit pendant une partie.</summary>
    public static void Export()
    {
        string folder = ProjectSettings.GlobalizePath("res://Assets/world");
        System.IO.Directory.CreateDirectory(folder);
        var frames = CreateNative();
        for (int i = 0; i < FrameCount; i++)
        {
            var result = frames[i].GetImage().SavePng(System.IO.Path.Combine(folder, $"caravan_{i}.png"));
            if (result != Error.Ok) throw new InvalidOperationException($"Export caravane {i} : {result}");
        }
        AssetLibrary.Reload();
    }

    private static ImageTexture[] CreateNative()
    {
        var frames = new ImageTexture[FrameCount];
        for (int frame = 0; frame < FrameCount; frame++)
        {
            var p = new PixelArt(Width, Height);
            Traveler(p, 3, frame, false);
            // Sac et caisse dans la charrette ; les voyageurs restent discernables des marchandises.
            p.Box(6, 6, 7, 4, Ink);
            p.Box(7, 7, 3, 3, Linen);
            p.Dot(8, 6, Gold);
            p.Box(10, 8, 3, 2, Sage);
            p.Box(5, 10, 10, 3, Ink);
            p.Box(6, 10, 8, 2, Wood);
            p.Box(6, 10, 8, 1, Gold);
            p.Dot(9, 11, Leather); p.Dot(12, 11, Leather);
            p.Line(14, 10, 19, 8, Wood);
            Wheel(p, 7, frame); Wheel(p, 12, frame);
            Traveler(p, 20, (frame + 2) % FrameCount, true);
            frames[frame] = p.Texture();
        }
        return frames;
    }

    private static void Wheel(PixelArt p, int x, int frame)
    {
        p.Box(x - 1, 12, 3, 3, Ink);
        p.Dot(x, 12, Leather); p.Dot(x - 1, 13, Leather); p.Dot(x + 1, 13, Leather); p.Dot(x, 14, Leather);
        p.Dot(x, 13, Gold);
        p.Dot(frame % 2 == 0 ? x : x + 1, frame % 2 == 0 ? 12 : 13, Linen);
    }

    private static void Traveler(PixelArt p, int x, int frame, bool pulling)
    {
        int bob = frame % 2;
        int step = frame == 0 ? -1 : frame == 2 ? 1 : 0;
        int liftLeft = frame == 1 ? 1 : 0, liftRight = frame == 3 ? 1 : 0;
        p.Line(x - 1, 10, x - 1 + step, 13 - liftLeft, Leather);
        p.Line(x + 1, 10, x + 1 - step, 13 - liftRight, Leather);
        p.Box(x - 2 + step, 14 - liftLeft, 2, 1, Ink);
        p.Box(x - step, 14 - liftRight, 2, 1, Ink);
        p.Box(x - 2, 5 - bob, 5, 6, Ink);
        p.Box(x - 1, 6 - bob, 3, 4, pulling ? Cloak : Sage);
        p.Box(x - 2, 6 - bob, 1, 4, Leather); // Sac de voyage.
        p.Box(x - 1, 2 - bob, 4, 4, Ink);
        p.Box(x, 3 - bob, 3, 2, Skin);
        p.Dot(x + 2, 3 - bob, Ink);
        p.Box(x - 1, 2 - bob, 3, 1, Leather);
        p.Box(x - 2, 1 - bob, 5, 1, Linen);
        p.Box(x - 1, 0, 3, 1, Gold);
        if (pulling)
        {
            p.Line(x, 7 - bob, x - 2, 8, Linen);
            p.Dot(x - 2, 8, Skin);
        }
        else p.Dot(x + 2, 8 - bob, Skin);
    }
}
