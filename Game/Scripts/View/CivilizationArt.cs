using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Pixel art des savoirs et des guerriers ; mêmes sources pour les PNG et le secours natif.</summary>
public static class CivilizationArt
{
    private static readonly Dictionary<Discovery, ImageTexture> Icons = [];
    private static ImageTexture[]? _warband;
    private static ImageTexture? _pact, _equipment;
    private static readonly Color Ink = C(44, 48, 43), Cream = C(245, 233, 201), Gold = C(222, 175, 78),
        Wood = C(137, 96, 61), LightWood = C(190, 139, 83), Steel = C(112, 154, 153),
        LightSteel = C(187, 211, 193), Sage = C(109, 143, 119), Mint = C(137, 198, 158),
        Red = C(183, 69, 63), Skin = C(225, 181, 132);
    private static Color C(byte r, byte g, byte b) => Color.Color8(r, g, b);

    public static Texture2D KnowledgeIcon(Discovery discovery)
    {
        var png = AssetLibrary.Get($"icons/knowledge_{discovery.ToString().ToLowerInvariant()}.png");
        if (png is not null && png.GetWidth() == 16 && png.GetHeight() == 16) return png;
        if (!Icons.TryGetValue(discovery, out var texture)) Icons[discovery] = texture = KnowledgeSource(discovery).Texture();
        return texture;
    }

    internal static PixelArt KnowledgeSource(Discovery discovery)
    {
        var p = new PixelArt(16, 16);
        void Grain(int x, int top)
        {
            p.Line(x, top, x, 13, Wood);
            for (int y = top; y < 10; y += 3) { p.Box(x - 2, y, 2, 2, Gold); p.Box(x + 1, y + 1, 2, 2, Cream); }
        }
        void Wall()
        {
            p.Box(2, 6, 12, 8, Ink); p.Box(3, 7, 10, 6, LightSteel);
            p.Line(3, 9, 12, 9, Steel); p.Line(3, 12, 12, 12, Steel);
            p.Line(7, 7, 7, 8, Steel); p.Line(5, 10, 5, 11, Steel); p.Line(10, 10, 10, 11, Steel);
        }
        void Book()
        {
            p.Polygon(Ink, new(1, 3), new(6, 2), new(8, 4), new(10, 2), new(14, 3), new(14, 13), new(9, 12), new(8, 14), new(6, 12), new(1, 13));
            p.Box(2, 4, 5, 8, Cream); p.Box(9, 4, 4, 8, Cream); p.Line(8, 5, 8, 12, Gold);
            p.Line(3, 6, 5, 6, Wood); p.Line(10, 8, 12, 8, Wood); p.Line(3, 9, 5, 9, Wood);
        }
        switch (discovery)
        {
            case Discovery.Agriculture:
                p.Line(2, 14, 13, 14, Wood); Grain(5, 3); Grain(11, 2); break;
            case Discovery.Husbandry:
                p.Box(2, 8, 12, 6, Wood); p.Line(3, 10, 12, 10, LightWood);
                p.Oval(7, 6, 4, 3, Ink); p.Oval(7, 5, 3, 2, Cream); p.Box(10, 4, 4, 4, Ink); p.Box(11, 5, 2, 2, LightSteel);
                p.Dot(12, 5, Ink); p.Line(4, 8, 4, 12, Cream); p.Line(9, 8, 9, 12, Cream); break;
            case Discovery.Metallurgy:
                p.Box(2, 10, 12, 4, Ink); p.Box(3, 10, 10, 2, Steel); p.Box(6, 12, 4, 1, LightSteel);
                p.Polygon(Red, new(4, 9), new(5, 4), new(8, 7), new(10, 1), new(13, 9));
                p.Polygon(Gold, new(6, 9), new(8, 4), new(10, 9)); p.Dot(8, 8, Cream); break;
            case Discovery.Milling:
                p.Oval(8, 8, 6, 6, Ink); p.Oval(8, 8, 5, 5, Wood); p.Oval(8, 8, 3, 3, Ink);
                p.Line(3, 4, 12, 12, LightWood); p.Line(3, 12, 12, 4, LightWood); p.Line(2, 8, 13, 8, Gold); p.Line(8, 2, 8, 13, Gold);
                p.Box(7, 7, 3, 3, LightSteel); break;
            case Discovery.Masonry:
                Wall(); p.Line(3, 4, 10, 2, Wood); p.Polygon(Ink, new(7, 3), new(13, 2), new(12, 6));
                p.Line(9, 3, 12, 3, Cream); break;
            case Discovery.Irrigation:
                p.Box(2, 2, 12, 12, Wood); p.Box(3, 3, 4, 10, Sage); p.Box(10, 3, 3, 10, Sage);
                p.Box(7, 2, 3, 12, Steel); p.Line(8, 3, 8, 13, LightSteel);
                p.Line(3, 6, 5, 6, Gold); p.Line(11, 10, 12, 10, Gold); break;
            case Discovery.Weaving:
                p.Box(2, 2, 12, 12, Ink); p.Box(3, 3, 10, 10, Wood);
                for (int x = 4; x < 13; x += 2) p.Line(x, 3, x, 12, Cream);
                for (int y = 5; y < 12; y += 3) p.Line(3, y, 12, y, Sage);
                p.Line(2, 12, 13, 4, Gold); break;
            case Discovery.Medicine:
                p.Box(3, 4, 10, 10, Ink); p.Box(4, 5, 8, 8, Cream); p.Box(6, 2, 4, 3, Steel);
                p.Box(7, 6, 2, 6, Red); p.Box(5, 8, 6, 2, Red); break;
            case Discovery.Brewing:
                p.Oval(8, 8, 5, 6, Ink); p.Oval(8, 8, 4, 5, LightWood);
                p.Line(6, 4, 6, 12, Wood); p.Line(10, 4, 10, 12, Wood);
                p.Line(4, 5, 12, 5, Steel); p.Line(4, 11, 12, 11, Steel); p.Box(11, 7, 3, 2, Gold); break;
            case Discovery.Commerce:
                p.Box(2, 5, 12, 8, Ink); p.Box(3, 6, 10, 6, Wood); p.Line(3, 7, 12, 7, LightWood);
                p.Line(6, 3, 9, 3, Gold); p.Line(6, 3, 6, 5, Gold); p.Line(9, 3, 9, 5, Gold);
                p.Oval(11, 11, 3, 3, Ink); p.Oval(11, 11, 2, 2, Gold); p.Dot(11, 10, Cream); break;
            case Discovery.Hydraulics:
                p.Box(2, 3, 12, 10, Steel); p.Line(3, 5, 12, 5, LightSteel); p.Line(3, 11, 12, 11, LightSteel);
                p.Box(6, 2, 4, 12, Ink); p.Box(7, 3, 2, 10, LightWood); p.Box(4, 6, 8, 2, Wood);
                p.Line(6, 2, 10, 2, Cream); break;
            case Discovery.Writing:
                p.Box(2, 3, 9, 11, Ink); p.Box(3, 4, 7, 9, Cream); p.Line(4, 7, 8, 7, Wood); p.Line(4, 10, 7, 10, Wood);
                p.Line(8, 12, 13, 2, Ink); p.Line(9, 10, 13, 3, LightSteel); p.Line(10, 7, 13, 3, Mint); break;
            case Discovery.Diplomacy:
                p.Polygon(Ink, new(1, 4), new(5, 5), new(8, 7), new(11, 5), new(14, 4), new(14, 10), new(10, 10), new(8, 13), new(3, 10), new(1, 10));
                p.Box(2, 5, 3, 5, Sage); p.Box(11, 5, 3, 5, Red); p.Line(5, 7, 9, 10, Skin); p.Line(6, 9, 10, 7, Cream); p.Dot(8, 10, Gold); break;
            case Discovery.CropRotation:
                p.Box(4, 5, 8, 6, Wood); p.Box(5, 6, 3, 4, Sage); p.Box(9, 6, 2, 4, Gold);
                p.Line(2, 4, 12, 2, Mint); p.Line(12, 2, 13, 6, Mint); p.Line(12, 2, 9, 1, Mint);
                p.Line(13, 12, 3, 14, Mint); p.Line(3, 14, 2, 10, Mint); p.Line(3, 14, 6, 14, Mint); break;
            case Discovery.Fortification:
                Wall(); p.Box(2, 3, 3, 4, Ink); p.Box(6, 3, 3, 4, Ink); p.Box(11, 3, 3, 4, Ink);
                p.Box(3, 4, 1, 3, LightSteel); p.Box(7, 4, 1, 3, LightSteel); p.Box(12, 4, 1, 3, LightSteel);
                p.Box(7, 10, 3, 4, Ink); break;
            case Discovery.Warfare:
                Swords(p); break;
            case Discovery.Herbalism:
                p.Line(8, 3, 8, 13, Wood); p.Oval(5, 5, 3, 2, Ink); p.Oval(5, 5, 2, 1, Mint);
                p.Oval(11, 8, 3, 2, Ink); p.Oval(11, 8, 2, 1, Sage); p.Oval(5, 10, 3, 2, Sage);
                p.Box(6, 12, 4, 2, Gold); p.Dot(8, 2, Cream); break;
            case Discovery.Coinage:
                p.Box(2, 10, 8, 4, Ink); p.Box(3, 11, 6, 2, Gold); p.Line(3, 12, 8, 12, Wood);
                p.Oval(10, 7, 4, 5, Ink); p.Oval(10, 7, 3, 4, Gold); p.Line(9, 4, 9, 9, Cream);
                p.Dot(11, 6, Wood); p.Dot(11, 8, Wood); break;
            case Discovery.Philosophy:
                Book(); p.Oval(8, 3, 3, 2, Ink); p.Oval(8, 3, 2, 1, Gold); p.Dot(8, 1, Cream); break;
            default: throw new ArgumentOutOfRangeException(nameof(discovery));
        }
        return p;
    }

    private static void Swords(PixelArt p)
    {
        p.Line(3, 3, 12, 12, Ink); p.Line(2, 3, 11, 12, LightSteel); p.Line(3, 2, 12, 11, Steel);
        p.Line(12, 3, 3, 12, Ink); p.Line(11, 3, 2, 12, LightSteel); p.Line(12, 2, 3, 11, Cream);
        p.Line(1, 10, 5, 14, Wood); p.Line(10, 14, 14, 10, Wood);
        p.Line(2, 10, 5, 13, Gold); p.Line(10, 13, 13, 10, Gold);
        p.Box(1, 13, 2, 2, Wood); p.Box(12, 13, 2, 2, Wood);
    }

    internal static PixelArt WarbandSource(int frame)
    {
        var p = new PixelArt(24, 16);
        for (int i = 0; i < 3; i++)
        {
            int x = 4 + i * 7, phase = (frame + i) % 4, bob = phase % 2;
            int step = phase == 0 ? -1 : phase == 2 ? 1 : 0;
            p.Line(x - 1, 10, x - 1 + step, 14 - (phase == 1 ? 1 : 0), Wood);
            p.Line(x + 1, 10, x + 1 - step, 14 - (phase == 3 ? 1 : 0), Wood);
            p.Dot(x - 1 + step, 14, Ink); p.Dot(x + 1 - step, 14, Ink);
            p.Box(x - 2, 5 - bob, 5, 6, Ink); p.Box(x - 1, 6 - bob, 3, 4, i == 1 ? Sage : Red);
            p.Box(x - 1, 2 - bob, 4, 4, Ink); p.Box(x, 3 - bob, 2, 2, Skin); p.Dot(x + 2, 3 - bob, Ink);
            p.Box(x - 1, 2 - bob, 3, 1, Steel); p.Dot(x, 1 - bob, LightSteel);
            // La pointe est dirigée vers la droite ; les trois silhouettes gardent leurs lances et boucliers.
            p.Line(x + 4, 3, x + 3, 13, Ink); p.Line(x + 4, 4, x + 4, 12, LightWood); p.Box(x + 3, 1, 2, 3, LightSteel);
            p.Oval(x - 1, 9 - bob, 2, 3, Ink); p.Oval(x - 1, 9 - bob, 1, 2, Gold); p.Dot(x - 1, 9 - bob, Cream);
        }
        return p;
    }

    public static Texture2D WarbandFrame(long ticks)
    {
        int frame = (int)(Math.Max(0, ticks) * 6 / GodColony.Simulation.Time.TimeConstants.TicksPerSecond % 4);
        var supplied = AssetLibrary.Frames("world/warband_{0}.png", 4);
        if (supplied is not null && Array.TrueForAll(supplied, t => t.GetWidth() == 24 && t.GetHeight() == 16)) return supplied[frame];
        _warband ??= [WarbandSource(0).Texture(), WarbandSource(1).Texture(), WarbandSource(2).Texture(), WarbandSource(3).Texture()];
        return _warband[frame];
    }

    public static Texture2D WarPact()
    {
        var png = AssetLibrary.Get("world/pact_war.png");
        return png is not null && png.GetWidth() == 16 && png.GetHeight() == 16 ? png : _pact ??= KnowledgeSource(Discovery.Warfare).Texture();
    }

    public static void DrawWarband(CanvasItem canvas, Vector2 center, long ticks, bool left, bool returning)
    {
        canvas.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        canvas.DrawSetTransform(center, 0, new Vector2(left ? -1 : 1, 1));
        canvas.DrawTextureRect(WarbandFrame(ticks), new Rect2(-12, -8, 24, 16), false,
            returning ? Color.Color8(191, 207, 191) : Colors.White);
        canvas.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    internal static bool IsWarrior(WorldState world, Colonist colonist) => world.WarParties.Exists(p => p.Warriors.Contains(colonist));

    /// <summary>Accessoires seulement : identité, costume et marche du colon conservés, sans lui attribuer un outil.</summary>
    internal static void DrawEquipment(CanvasItem canvas, Vector2 feet, ColonistAppearance appearance, bool left, int frame)
    {
        if (_equipment is null)
        {
            var p = new PixelArt(24, 32);
            p.Line(21, 5, 21, 30, Ink); p.Line(20, 5, 20, 29, LightWood);
            p.Polygon(Ink, new(18, 6), new(20, 0), new(23, 6)); p.Line(20, 2, 20, 5, LightSteel);
            p.Oval(6, 23, 4, 6, Ink); p.Oval(6, 22, 3, 5, Wood); p.Oval(6, 22, 2, 4, Red);
            p.Line(3, 22, 9, 22, Gold); p.Line(6, 18, 6, 26, Gold); p.Dot(6, 22, Cream);
            _equipment = p.Texture();
        }
        float scale = appearance.People == PeopleLook.Dwarf ? 0.8f : 1f;
        canvas.DrawSetTransform(feet + new Vector2(0, -frame % 2), 0, new Vector2(left ? -scale : scale, scale));
        canvas.DrawTextureRect(_equipment, new Rect2(-12, -32, 24, 32), false);
        canvas.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    public static void Export()
    {
        void Save(PixelArt art, string relative)
        {
            string path = ProjectSettings.GlobalizePath("res://Assets/" + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (art.Image.SavePng(path) != Error.Ok) throw new InvalidOperationException(path);
        }
        foreach (Discovery d in Enum.GetValues<Discovery>()) Save(KnowledgeSource(d), $"icons/knowledge_{d.ToString().ToLowerInvariant()}.png");
        for (int frame = 0; frame < 4; frame++) Save(WarbandSource(frame), $"world/warband_{frame}.png");
        Save(KnowledgeSource(Discovery.Warfare), "world/pact_war.png");
        AssetLibrary.Reload();
    }
}
