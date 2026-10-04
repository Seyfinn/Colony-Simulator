using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Sources natives assorties au village ; export explicite des icônes et des métiers.</summary>
public static class WorkshopArt
{
    public static readonly string[] Goods = ["Food", "Grain", "Wood", "Stone", "IronOre", "Charcoal", "Iron", "Tools", "Flour", "Bread", "Beer"];
    public static readonly string[] Buildings = ["Hut", "Kiln", "Bloomery", "Forge", "Mill", "Oven", "Dam", "DamSide"];
    private static readonly Dictionary<string, ImageTexture> Cache = [];
    private static Color C(byte r, byte g, byte b) => Color.Color8(r, g, b);
    private static readonly Color Ink = C(45, 53, 48), Cream = C(245, 233, 201), Gold = C(222, 175, 78),
        Wood = C(133, 91, 57), WoodLight = C(190, 139, 83), Steel = C(112, 154, 153), SteelLight = C(187, 211, 193);

    public static ImageTexture Icon(string kind)
    {
        string key = "icon/" + kind;
        if (!Cache.TryGetValue(key, out var texture)) Cache[key] = texture = IconSource(kind).Texture();
        return texture;
    }

    internal static PixelArt IconSource(string kind)
    {
        var a = new PixelArt(16, 16);
        switch (kind)
        {
            case "Food":
                a.Box(2, 8, 12, 6, Ink); a.Box(3, 9, 10, 4, Wood); a.Line(3, 11, 12, 11, WoodLight);
                foreach (int x in new[] {4, 8, 11}) { a.Oval(x, 6, 2, 2, C(183, 69, 63)); a.Dot(x - 1, 5, C(237, 147, 126)); a.Dot(x, 3, C(109, 143, 119)); }
                a.Line(3, 9, 12, 9, Gold); break;
            case "Grain":
                foreach (int x in new[] {4, 8, 12})
                {
                    int top = x == 8 ? 2 : 4;
                    a.Line(x, top, x, 13, Ink); a.Line(x + 1, top + 2, x + 1, 13, Gold);
                    for (int y = top; y < 10; y += 3) { a.Box(x - 2, y, 2, 2, Gold); a.Box(x + 1, y + 1, 2, 2, Cream); }
                }
                a.Box(4, 12, 9, 2, Wood); a.Line(5, 12, 11, 12, WoodLight); break;
            case "Wood":
                foreach (int y in new[] {3, 9}) { a.Box(2, y, 12, 5, Ink); a.Box(3, y + 1, 9, 3, Wood); a.Line(3, y + 1, 10, y + 1, WoodLight); a.Oval(12, y + 2, 2, 2, Gold); a.Dot(12, y + 2, Wood); } break;
            case "Stone": case "IronOre":
                a.Polygon(Ink, new(2, 8), new(5, 3), new(11, 3), new(14, 8), new(13, 14), new(4, 14), new(1, 11));
                a.Polygon(C(132, 155, 143), new(3, 8), new(6, 4), new(10, 4), new(13, 8), new(12, 13), new(4, 13), new(2, 11));
                a.Polygon(SteelLight, new(3, 8), new(6, 4), new(10, 4), new(8, 9)); a.Line(8, 10, 12, 12, C(89, 114, 107));
                if (kind == "IronOre") foreach (var p in new[] {new Vector2I(5, 7), new Vector2I(10, 8), new Vector2I(7, 11)}) { a.Box(p.X, p.Y, 3, 2, C(143, 78, 51)); a.Dot(p.X, p.Y, C(224, 156, 83)); }
                break;
            case "Charcoal":
                foreach (var p in new[] {new Vector2I(2, 8), new Vector2I(6, 3), new Vector2I(9, 8)})
                { a.Polygon(Ink, new(p.X, p.Y + 1), new(p.X + 2, p.Y), new(p.X + 5, p.Y + 1), new(p.X + 4, p.Y + 6), new(p.X, p.Y + 5)); a.Box(p.X + 1, p.Y + 1, 3, 3, C(79, 96, 85)); a.Line(p.X + 1, p.Y + 1, p.X + 3, p.Y + 1, C(157, 169, 144)); a.Dot(p.X + 1, p.Y + 2, C(112, 130, 112)); }
                break;
            case "Iron":
                a.Polygon(Ink, new(1, 8), new(5, 4), new(12, 4), new(14, 7), new(14, 12), new(2, 13));
                a.Box(3, 8, 10, 4, Steel); a.Polygon(SteelLight, new(3, 7), new(6, 5), new(11, 5), new(13, 7)); a.Line(3, 8, 12, 8, Cream); break;
            case "Tools":
                a.Line(3, 13, 11, 3, Ink); a.Line(4, 13, 12, 3, WoodLight);
                a.Polygon(Ink, new(7, 2), new(11, 1), new(14, 4), new(13, 7), new(10, 5)); a.Box(8, 2, 4, 2, SteelLight); a.Box(12, 4, 2, 2, Steel);
                a.Line(3, 3, 11, 13, Ink); a.Line(2, 3, 10, 13, Wood); a.Box(1, 2, 5, 3, Steel); a.Line(1, 2, 5, 2, SteelLight); break;
            case "Flour":
                a.Polygon(Ink, new(5, 2), new(11, 2), new(10, 5), new(13, 8), new(13, 14), new(3, 14), new(3, 8), new(6, 5));
                a.Box(5, 7, 6, 6, Cream); a.Box(4, 9, 1, 4, C(200, 187, 156)); a.Box(11, 9, 1, 4, C(200, 187, 156)); a.Box(6, 3, 4, 2, Cream); a.Box(5, 5, 6, 1, Wood);
                // Épi sur le sac : la farine ne ressemble pas aux cristaux de sel.
                a.Line(8, 8, 8, 12, WoodLight); a.Dot(7, 9, Gold); a.Dot(9, 10, Gold); break;
            case "Bread":
                a.Oval(8, 9, 6, 4, Ink); a.Oval(8, 8, 5, 3, C(183, 111, 58)); a.Oval(7, 7, 4, 2, Gold);
                foreach (int x in new[] {5, 8, 11}) a.Line(x, 6, x - 1, 8, Cream); a.Line(5, 11, 11, 11, Wood); break;
            case "Beer":
                a.Box(3, 5, 8, 9, Ink); a.Box(4, 6, 6, 7, Gold); a.Box(4, 6, 2, 6, C(241, 204, 109)); a.Box(8, 7, 2, 6, C(181, 117, 49));
                a.Box(10, 6, 4, 6, Ink); a.Box(11, 7, 2, 4, Gold); a.Box(11, 8, 1, 2, Colors.Transparent);
                a.Box(2, 4, 10, 3, Cream); a.Oval(4, 4, 2, 2, Cream); a.Oval(8, 3, 2, 2, Cream); a.Dot(9, 2, Colors.White); a.Box(3, 7, 1, 2, Cream); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Icône inconnue.");
        }
        return a;
    }

    internal static string CaskState(Building cask, long ticks) => !cask.IsBrewing ? "empty" : ticks < cask.BrewReadyTicks ? "brewing" : "ready";
    public static Texture2D Cask(Building cask, long ticks) => Cask(CaskState(cask, ticks));
    internal static Texture2D Cask(string state)
    {
        string file = state == "empty" ? "cask" : "cask_" + state;
        var provided = AssetLibrary.Get($"buildings/{file}.png");
        if (provided is not null && provided.GetWidth() == 64 && provided.GetHeight() == 80) return provided;
        if (!Cache.TryGetValue(file, out var texture)) Cache[file] = texture = BuildingSprites.CaskSource(state).Texture();
        return texture;
    }

    /// <summary>La phase suit les ticks du monde ; deux dessins en pause gardent exactement les mêmes bulles.</summary>
    internal static Vector2[] Bubbles(long ticks)
    {
        int phase = (int)((ticks / 3) % 12);
        return [new(22 + phase % 2, 38 - phase / 2), new(25 - phase % 2, 36 - (phase + 6) % 12 / 2)];
    }

    public static void ExportAll()
    {
        void Save(PixelArt a, string relative)
        {
            string path = ProjectSettings.GlobalizePath("res://Assets/" + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (a.Image.SavePng(path) != Error.Ok) throw new InvalidOperationException(path);
        }
        foreach (string good in Goods) Save(IconSource(good), $"icons/{good.ToLowerInvariant()}.png");
        foreach (string kind in Buildings) Save(BuildingSprites.WorkshopSource(kind), $"buildings/{(kind == "DamSide" ? "dam_side" : kind.ToLowerInvariant())}.png");
        foreach (string state in new[] {"empty", "brewing", "ready"}) Save(BuildingSprites.CaskSource(state), $"buildings/cask{(state == "empty" ? "" : "_" + state)}.png");
    }
}
