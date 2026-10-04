using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace GodColony.View;

/// <summary>Sources pixel art reproductibles du village ; export uniquement sur demande explicite.</summary>
public static class VillageArt
{
    public static readonly string[] Goods = ["Fish", "Eggs", "Milk", "Meat", "SaltedMeat", "Cake", "Stew", "Chickens", "Sheep", "Cows", "Wool", "Clothes", "Salt", "Spices", "Hardwood"];
    public static readonly string[] Buildings = ["Pen", "Loom", "Market", "Infirmary", "Storehouse", "Well", "Tavern", "School"];
    private static readonly Dictionary<string, ImageTexture> Cache = [];
    private static Color C(byte r, byte g, byte b) => Color.Color8(r, g, b);
    private static readonly Color Ink = C(45, 53, 48), Cream = C(245, 233, 201), Gold = C(222, 175, 78), Red = C(183, 69, 63), Wood = C(133, 91, 57);

    public static ImageTexture Icon(string kind) => Load($"icons/{kind.ToLowerInvariant()}.png", () => IconArt(kind));
    public static ImageTexture Status(string kind) => Load($"effects/status_{kind}.png", () => StatusArt(kind));
    public static ImageTexture Animal(string kind, int frame) => Load($"animals/{kind}_{frame % 4}.png", () => AnimalArt(kind, frame % 4));
    public static ImageTexture Event(string kind, int frame = 0) => Load($"effects/{kind}{(kind == "building_fire" ? "_" + frame % 4 : "")}.png", () => EventArt(kind, frame % 4));
    private static ImageTexture Load(string path, Func<PixelArt> make)
    {
        if (!Cache.TryGetValue(path, out var texture)) Cache[path] = texture = AssetLibrary.Get(path) ?? make().Texture();
        return texture;
    }

    internal static PixelArt IconArt(string kind)
    {
        var a = new PixelArt(16, 16);
        switch (kind)
        {
            case "Fish":
                a.Polygon(Ink, new(1, 5), new(5, 7), new(8, 3), new(13, 5), new(15, 8), new(12, 11), new(6, 11), new(4, 9), new(1, 12));
                a.Oval(9, 7, 4, 3, C(98, 152, 155)); a.Line(6, 6, 11, 5, C(182, 211, 198)); a.Line(7, 9, 12, 9, Cream); a.Dot(12, 6, Ink); break;
            case "Eggs":
                a.Box(2, 10, 12, 4, Ink); a.Box(3, 11, 10, 2, Wood);
                foreach (int x in new[] {4, 8, 11}) { a.Oval(x, 7, 2, 3, C(200, 168, 119)); a.Oval(x, 6, 1, 2, Cream); } break;
            case "Milk":
                a.Box(6, 2, 4, 3, Ink); a.Box(7, 2, 2, 1, Wood); a.Box(4, 5, 8, 9, Ink); a.Box(5, 5, 6, 8, C(206, 222, 214));
                a.Box(5, 6, 2, 6, Cream); a.Box(5, 9, 6, 2, C(103, 151, 165)); break;
            case "Meat": case "SaltedMeat":
                a.Oval(8, 9, 6, 4, Ink); a.Oval(8, 8, 5, 3, kind == "Meat" ? Red : C(133, 78, 63));
                a.Oval(6, 7, 2, 1, C(237, 147, 126)); a.Oval(10, 8, 1, 1, Cream);
                if (kind == "SaltedMeat") { a.Box(3, 3, 10, 2, Wood); foreach (int x in new[] {4, 7, 11}) a.Box(x, 6 + x % 3, 2, 2, Cream); } break;
            case "Cake":
                a.Oval(8, 12, 6, 2, C(178, 196, 184)); a.Box(3, 6, 10, 6, Ink); a.Box(4, 7, 8, 4, Gold); a.Box(4, 9, 8, 1, Red);
                a.Box(3, 5, 10, 3, Cream); a.Dot(5, 4, Red); a.Dot(10, 4, Red); a.Box(8, 1, 1, 3, Gold); break;
            case "Stew":
                a.Oval(8, 10, 6, 4, Ink); a.Oval(8, 9, 5, 3, C(111, 149, 144)); a.Oval(8, 7, 5, 2, C(116, 77, 46));
                a.Box(4, 6, 2, 2, C(210, 143, 77)); a.Dot(9, 7, C(140, 171, 90)); a.Box(11, 3, 2, 5, Wood);
                a.Line(5, 4, 6, 2, Cream); a.Line(8, 4, 9, 1, Cream); break;
            case "Chickens": case "Sheep": case "Cows":
                var animal = AnimalArt(kind == "Chickens" ? "chicken" : kind == "Sheep" ? "sheep" : "cow", 0).Image;
                // Silhouettes redessinées à la grille 16 px, sans interpolation.
                for (int y = 2; y < 14; y++) for (int x = 1; x < 15; x++)
                { var color = animal.GetPixel((x - 1) * animal.GetWidth() / 14, (y - 2) * animal.GetHeight() / 12); if (color.A > 0) a.Dot(x, y, color); }
                break;
            case "Wool":
                a.Oval(8, 9, 6, 5, C(154, 153, 130));
                foreach (var p in new[] {new Vector2I(4, 8), new Vector2I(7, 5), new Vector2I(11, 7), new Vector2I(9, 11), new Vector2I(5, 11)}) a.Oval(p.X, p.Y, 3, 2, Cream);
                a.Line(5, 7, 9, 8, C(200, 187, 156)); a.Line(9, 8, 7, 11, C(200, 187, 156)); break;
            case "Clothes":
                a.Polygon(Ink, new(5, 2), new(10, 2), new(14, 5), new(12, 9), new(11, 8), new(11, 14), new(4, 14), new(4, 8), new(2, 9), new(1, 5));
                a.Box(5, 4, 5, 9, C(109, 143, 119)); a.Box(2, 5, 3, 2, C(109, 143, 119)); a.Box(10, 5, 3, 2, C(109, 143, 119));
                a.Box(6, 3, 3, 2, Cream); a.Box(5, 11, 5, 1, Gold); break;
            case "Salt":
                a.Oval(8, 12, 6, 2, C(105, 137, 145));
                foreach (var p in new[] {new Vector2I(3, 8), new Vector2I(7, 4), new Vector2I(10, 8)})
                { a.Box(p.X, p.Y, 4, 4, C(172, 197, 192)); a.Box(p.X, p.Y, 3, 3, Cream); a.Dot(p.X, p.Y, Colors.White); } break;
            case "Spices":
                foreach (var (x, tint) in new[] {(3, Red), (8, Gold), (12, C(133, 153, 71))})
                { a.Box(x - 2, 9, 5, 5, Ink); a.Box(x - 1, 10, 3, 3, Wood); a.Oval(x, 8, 2, 2, tint); a.Dot(x - 1, 7, Cream); } break;
            case "Hardwood":
                foreach (int y in new[] {3, 9}) { a.Box(2, y, 12, 5, Ink); a.Box(3, y + 1, 9, 3, C(102, 61, 45)); a.Line(3, y + 1, 10, y + 1, C(167, 100, 60)); a.Oval(12, y + 2, 2, 2, C(202, 148, 91)); a.Dot(12, y + 2, Wood); } break;
            case "Milestone":
                a.Polygon(Ink, new(8, 1), new(10, 5), new(15, 6), new(11, 9), new(12, 14), new(8, 12), new(3, 14), new(4, 9), new(1, 6), new(6, 5));
                a.Polygon(Gold, new(8, 3), new(9, 6), new(13, 7), new(10, 9), new(10, 12), new(8, 10), new(5, 12), new(6, 8), new(3, 7), new(7, 6)); a.Dot(8, 5, Cream); break;
        }
        return a;
    }

    internal static PixelArt AnimalArt(string kind, int frame)
    {
        int w = kind == "chicken" ? 12 : kind == "sheep" ? 16 : 24, h = kind == "chicken" ? 12 : kind == "sheep" ? 14 : 18;
        var a = new PixelArt(w, h); int step = frame == 1 ? 1 : frame == 3 ? -1 : 0, graze = frame == 2 ? 2 : 0;
        if (kind == "chicken")
        {
            a.Box(4 + step, 9, 1, 2, Gold); a.Box(7 - step, 9, 1, 2, Gold); a.Oval(5, 7, 4, 3, Ink); a.Oval(5, 6, 3, 2, C(190, 118, 62));
            a.Box(1, 3, 2, 4, Wood); a.Box(8, 3 + graze, 2, 4, Cream); a.Box(8, 2 + graze, 2, 1, Red); a.Dot(10, 5 + graze, Gold); a.Dot(9, 4 + graze, Ink); a.Line(3, 6, 6, 7, Gold);
        }
        else
        {
            bool cow = kind == "cow"; int cx = cow ? 9 : 6, cy = cow ? 9 : 7, rx = cow ? 8 : 5, ry = cow ? 5 : 4;
            foreach (int x in cow ? new[] {4, 7, 12, 15} : new[] {3, 5, 9, 11}) a.Box(x + (x % 2 == 0 ? step : -step), cy + ry - 1, 1, 4, Ink);
            a.Oval(cx, cy, rx, ry, C(172, 167, 145)); a.Oval(cx - 1, cy - 1, rx - 1, ry - 1, Cream);
            if (cow) { a.Oval(6, 8, 3, 2, Ink); a.Oval(12, 11, 2, 2, Ink); a.Line(1, 8, 1, 13, Wood); }
            else { a.Oval(3, 5, 2, 2, Cream); a.Oval(7, 4, 2, 2, Cream); a.Oval(10, 7, 2, 2, Cream); }
            int head = cow ? 18 : 12; a.Box(head, cy - 3 + graze, cow ? 5 : 3, 5, cow ? Cream : Ink);
            if (cow) { a.Box(20, cy + 1 + graze, 3, 2, C(209, 137, 121)); a.Dot(18, cy - 4 + graze, Gold); a.Dot(21, cy - 4 + graze, Gold); }
            a.Dot(head + 1, cy - 1 + graze, cow ? Ink : Cream);
        }
        return a;
    }

    internal static PixelArt StatusArt(string kind)
    {
        var a = new PixelArt(12, 12); a.Oval(5, 5, 5, 5, Ink);
        if (kind == "sick") { a.Box(4, 2, 3, 6, Cream); a.Oval(5, 8, 2, 2, Red); a.Box(5, 4, 1, 5, Red); a.Box(7, 3, 2, 1, Gold); }
        else if (kind == "injured") { a.Polygon(Cream, new(2, 4), new(4, 2), new(10, 8), new(8, 10)); a.Box(4, 4, 4, 4, C(198, 156, 110)); a.Dot(5, 5, Red); a.Dot(6, 6, Red); }
        else { a.Polygon(C(132, 191, 121), new(6, 1), new(2, 6), new(5, 6), new(4, 10), new(10, 4), new(7, 4), new(8, 1)); a.Dot(6, 3, Cream); }
        return a;
    }

    internal static PixelArt EventArt(string kind, int frame)
    {
        var a = new PixelArt(64, 80);
        if (kind == "building_ashes")
        {
            a.Oval(32, 69, 27, 7, C(80, 79, 69)); a.Oval(30, 67, 23, 5, C(112, 110, 94));
            foreach (int x in new[] {8, 24, 43, 54}) { a.Box(x, 48 + x % 7, 3, 22 - x % 7, Ink); a.Line(x, 60, x + 8, 72, Wood); }
            for (int i = 0; i < 18; i++) a.Box(8 + i * 17 % 47, 65 + i * 13 % 9, 3, 2, i % 3 == 0 ? Red.Darkened(0.4f) : Ink);
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                int x = 12 + i * 12, tip = 23 + (i * 7 + frame * 5) % 22;
                a.Oval(x + frame - 1, 17 - i * 3, 7, 9, C(120, 123, 111));
                a.Polygon(Red, new(x - 7, 70), new(x - 4, tip + 10), new(x, tip), new(x + 4, tip + 16), new(x + 8, 70));
                a.Polygon(Gold, new(x - 4, 68), new(x, tip + 9), new(x + 5, 68)); a.Box(x, 56, 2, 8, Cream);
            }
        }
        return a;
    }

    public static void ExportAll()
    {
        void Save(PixelArt a, string name) { string path = ProjectSettings.GlobalizePath("res://Assets/" + name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); if (a.Image.SavePng(path) != Error.Ok) throw new InvalidOperationException(path); }
        foreach (string good in Goods) Save(IconArt(good), $"icons/{good.ToLowerInvariant()}.png");
        Save(IconArt("Milestone"), "icons/milestone.png");
        foreach (string kind in new[] {"sick", "injured", "boosted"}) Save(StatusArt(kind), $"effects/status_{kind}.png");
        foreach (string kind in new[] {"chicken", "sheep", "cow"}) for (int f = 0; f < 4; f++) Save(AnimalArt(kind, f), $"animals/{kind}_{f}.png");
        foreach (string kind in Buildings) Save(BuildingSprites.VillageSource(kind), $"buildings/{kind.ToLowerInvariant()}.png");
        for (int f = 0; f < 4; f++) Save(EventArt("building_fire", f), $"effects/building_fire_{f}.png");
        Save(EventArt("building_ashes", 0), "effects/building_ashes.png");
    }
}
