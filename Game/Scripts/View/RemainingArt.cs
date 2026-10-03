using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Exports du système pixel art natif : monnaie, roue, carte et voyageurs.</summary>
public static class RemainingArt
{
    private static readonly Color Ink = ArtDirection.Charcoal, Gold = ArtDirection.Brass;
    private static readonly Color Wood = Color.Color8(108, 72, 47), LightWood = Color.Color8(177, 126, 73);
    private static readonly Dictionary<PeopleLook, ImageTexture[]> NativeTraders = [];
    private static ImageTexture[]? NativeWheels;

    public static ImageTexture WheelFrame(int frame)
    {
        var provided = AssetLibrary.Frames("buildings/mill_wheel_{0}.png", 4);
        if (provided is not null && Array.TrueForAll(provided, p => p.GetWidth() == 24 && p.GetHeight() == 40)) return provided[frame];
        if (NativeWheels is null)
        {
            NativeWheels = new ImageTexture[4];
            for (int i = 0; i < 4; i++) NativeWheels[i] = ImageTexture.CreateFromImage(Wheel(i));
        }
        return NativeWheels[frame];
    }
    public static void ExportAll()
    {
        BuildingSprites.ExportDamConstruction();
        Save("icons/coins.png", Coins());
        for (int i = 0; i < 4; i++) Save($"buildings/mill_wheel_{i}.png", Wheel(i));
        foreach (PeopleLook people in Enum.GetValues<PeopleLook>())
        {
            string name = people.ToString().ToLowerInvariant();
            Save($"world/colony_{name}.png", Colony(people));
            for (int i = 0; i < 4; i++) Save($"peoples/trader_{name}_{i}.png", Trader(people, i));
        }
        Save("world/river_segment.png", River());
        Save("world/map_background.png", Background());
        AssetLibrary.Reload();
    }
    private static void Save(string file, Image image)
    {
        string path = ProjectSettings.GlobalizePath("res://Assets/" + file);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (image.SavePng(path) != Error.Ok) throw new InvalidOperationException("Export : " + file);
    }
    public static Image Coins()
    {
        var p = new PixelArt(16, 16);
        foreach (var (x, y) in new[] { (6, 11), (10, 9), (6, 7), (6, 4) })
        {
            p.Oval(x, y + 1, 4, 2, Ink); p.Box(x - 3, y, 7, 2, Color.Color8(147, 101, 43));
            p.Oval(x, y, 4, 1, Gold); p.Line(x - 2, y, x + 1, y, Color.Color8(248, 224, 157));
            p.Dot(x + 2, y + 1, LightWood);
        }
        return p.Image;
    }
    public static Image Wheel(int frame)
    {
        var p = new PixelArt(24, 40);
        p.Oval(12, 20, 11, 16, Ink); p.Oval(11, 19, 10, 15, Wood);
        p.Oval(11, 19, 7, 12, LightWood); p.Oval(11, 19, 6, 11, Color.Color8(64, 85, 77));
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4 + frame * Math.PI / 16;
            int x = 11 + (int)Math.Round(Math.Cos(angle) * 9), y = 19 + (int)Math.Round(Math.Sin(angle) * 14);
            p.Line(11, 19, x, y, LightWood); p.Box(x - 1, y - 1, 3, 2, Gold);
        }
        p.Oval(11, 19, 3, 3, Ink); p.Oval(10, 18, 2, 2, Color.Color8(180, 194, 170));
        p.Dot(11, 19, Ink);
        return p.Image;
    }
    public static Image Colony(PeopleLook people)
    {
        var p = new PixelArt(32, 32);
        p.Oval(16, 28, 13, 3, Ink);
        var wall = Color.Color8(183, 174, 133);
        p.Box(7, 15, 19, 12, Ink); p.Box(8, 16, 17, 10, wall);
        p.Box(15, 21, 5, 7, Wood); p.Box(10, 19, 3, 4, Color.Color8(62, 103, 104));
        Color roof = people switch { PeopleLook.Dwarf => Color.Color8(157, 131, 115), PeopleLook.Elf => Color.Color8(106, 157, 119), PeopleLook.Orc => Color.Color8(164, 104, 70), _ => Gold };
        p.Polygon(Ink, new(3, 16), new(16, 4), new(29, 16));
        p.Polygon(roof, new(5, 15), new(16, 6), new(27, 15));
        p.Line(8, 14, 16, 7, roof.Lightened(0.2f));
        if (people == PeopleLook.Dwarf) { p.Box(5, 17, 3, 11, Color.Color8(137, 150, 137)); p.Box(24, 17, 3, 11, Color.Color8(137, 150, 137)); }
        if (people == PeopleLook.Elf) { p.Line(16, 2, 16, 7, LightWood); p.Oval(16, 3, 3, 2, roof); p.Line(23, 17, 23, 27, roof); }
        if (people == PeopleLook.Orc) { p.Line(5, 14, 3, 7, Color.Color8(223, 210, 172)); p.Line(27, 14, 29, 7, Color.Color8(223, 210, 172)); p.Box(19, 17, 4, 3, Color.Color8(174, 89, 70)); }
        return p.Image;
    }
    public static Image River()
    {
        var p = new PixelArt(16, 8);
        p.Box(0, 1, 16, 6, Ink); p.Box(0, 2, 16, 4, Color.Color8(91, 151, 157));
        p.Box(0, 2, 16, 1, Color.Color8(123, 177, 161)); p.Box(4, 4, 5, 1, Color.Color8(193, 216, 194));
        return p.Image;
    }
    public static Image Background()
    {
        var p = new PixelArt(1024, 640);
        for (int y = 0; y < 640; y++)
        for (int x = 0; x < 1024; x++)
        {
            int grain = ((x * 17 + y * 31) ^ (x / 7 + y / 11)) % 5;
            p.Dot(x, y, Color.Color8((byte)(24 + grain), (byte)(40 + grain), (byte)(35 + grain)));
        }
        for (int x = 32; x < 1024; x += 64) p.Line(x, 0, x, 639, Color.Color8(34, 50, 44));
        for (int y = 32; y < 640; y += 64) p.Line(0, y, 1023, y, Color.Color8(34, 50, 44));
        return p.Image;
    }
    public static Image Trader(PeopleLook people, int frame)
    {
        var p = new PixelArt(32, 32);
        var source = PeoplesSprites.Get(new ColonistAppearance(3, people, WoodlandBiome.TemperatePlain))[frame].GetImage();
        int ox = (32 - source.GetWidth()) / 2, oy = 32 - source.GetHeight();
        p.Image.BlitRect(source, new Rect2I(0, 0, source.GetWidth(), source.GetHeight()), new Vector2I(ox, oy));
        // Sac, sangle diagonale et bâton de marche identifient le voyage, en conservant l'espèce.
        p.Box(5, 17, 6, 10, Ink); p.Box(6, 18, 4, 8, Wood); p.Box(6, 18, 4, 2, LightWood);
        p.Line(11, 17, 20, 25, Gold);
        int staffX = frame switch { 0 => 24, 2 => 26, _ => 25 };
        int staffY = frame switch { 1 => 14, 3 => 12, _ => 13 };
        p.Line(staffX, staffY, 25, 30, Wood); p.Dot(staffX, staffY, Gold);
        return p.Image;
    }
    public static ImageTexture[] TraderFrames(PeopleLook people)
    {
        var frames = AssetLibrary.Frames($"peoples/trader_{people.ToString().ToLowerInvariant()}_{{0}}.png", 4);
        if (frames is not null && Array.TrueForAll(frames, p => p.GetWidth() == 32 && p.GetHeight() == 32)) return frames;
        if (NativeTraders.TryGetValue(people, out var cached)) return cached;
        frames = new ImageTexture[4];
        for (int i = 0; i < 4; i++) frames[i] = ImageTexture.CreateFromImage(Trader(people, i));
        return NativeTraders[people] = frames;
    }
}
