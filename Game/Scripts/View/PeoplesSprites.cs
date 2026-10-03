using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

public enum PeopleLook { Human, Dwarf, Elf, Orc }
public enum OutfitTrade { Everyday, Field, Woodland, Stonework, Workshop }

/// <summary>Identité visuelle complète, indépendante des talents et des règles propres aux peuples.</summary>
public readonly record struct ColonistAppearance(int Id, PeopleLook People, WoodlandBiome Biome,
    LifeStage Stage = LifeStage.Adult, Sex Sex = Sex.Male, OutfitTrade Trade = OutfitTrade.Everyday);

/// <summary>Quatre peuples, les pieds alignés, avec leurs propres proportions.</summary>
public static partial class PeoplesSprites
{
    public const int AppearanceCount = 24;
    private sealed record Assets(ImageTexture[] Frames, ImageTexture Rest, Rect2I Bounds);
    private static readonly Dictionary<ColonistAppearance, Assets> Cache = [];
    private static readonly Dictionary<ColonistAppearance, ImageTexture> HumanPortraits = [];
    private static readonly Color Ink = C(44, 48, 43), Leather = C(104, 73, 49), Gold = C(223, 182, 92);
    private static readonly Color Linen = C(225, 207, 169), Iron = C(129, 154, 155);
    private static Color C(byte r, byte g, byte b) => Color.Color8(r, g, b);
    private readonly record struct Body(int HeadTop, int FaceY, int ShoulderY, int HalfWidth, int HipY, int EyeGap);
    private readonly record struct Palette(Color Skin, Color Hair, Color Cloth, Color Accent);

    public static PeopleLook LookOf(Species species) => species == Species.Dwarf ? PeopleLook.Dwarf
        : species == Species.Elf ? PeopleLook.Elf : species == Species.Orc ? PeopleLook.Orc : PeopleLook.Human;

    public static ColonistAppearance Describe(Colonist person, WoodlandBiome biome)
    {
        PeopleLook people = LookOf(person.Species);
        OutfitTrade trade = person.Stage == LifeStage.Child || people == PeopleLook.Human ? OutfitTrade.Everyday : person.Sector switch
        {
            WorkSector.Farm => OutfitTrade.Field,
            WorkSector.Food or WorkSector.Wood => OutfitTrade.Woodland,
            WorkSector.Stone or WorkSector.Construction => OutfitTrade.Stonework,
            WorkSector.Craft => OutfitTrade.Workshop,
            _ => OutfitTrade.Everyday,
        };
        return new(person.Id, people, biome, person.Stage, person.Sex, trade);
    }

    private static ColonistAppearance Canonical(ColonistAppearance appearance) => appearance with
    {
        Id = ((appearance.Id % AppearanceCount) + AppearanceCount) % AppearanceCount,
        Trade = appearance.Stage == LifeStage.Child ? OutfitTrade.Everyday : appearance.Trade,
    };

    private static string AssetStem(ColonistAppearance a) => $"peoples/{a.People.ToString().ToLowerInvariant()}_{a.Stage.ToString().ToLowerInvariant()}_{(a.Sex == Sex.Female ? "f" : "m")}";
    private static ImageTexture[]? Supplied(ColonistAppearance a)
    {
        var frames = AssetLibrary.Frames(AssetStem(a) + "_{0}.png", 4);
        if (frames is not null)
            foreach (var frame in frames) if (frame.GetWidth() != 32 || frame.GetHeight() != 32) return null;
        return frames;
    }
    public static ImageTexture[] Get(ColonistAppearance appearance) => Supplied(appearance) ?? (appearance.People == PeopleLook.Human
        ? SpriteFactory.Colonist(appearance.Id, appearance.Stage == LifeStage.Elder, appearance.Biome)
        : GetAssets(appearance).Frames);
    public static ImageTexture Rest(ColonistAppearance appearance)
    {
        var rest = AssetLibrary.Get(AssetStem(appearance) + "_rest.png");
        if (rest is not null && rest.GetWidth() == 32 && rest.GetHeight() == 32) return rest;
        return Supplied(appearance)?[0] ?? (appearance.People == PeopleLook.Human ? Get(appearance)[0] : GetAssets(appearance).Rest);
    }
    public static Rect2I Bounds(ColonistAppearance appearance) => Supplied(appearance) is { } supplied ? supplied[0].GetImage().GetUsedRect()
        : appearance.People == PeopleLook.Human ? new Rect2I(2, 0, 12, 24) : GetAssets(appearance).Bounds;

    public static ImageTexture Portrait(ColonistAppearance appearance)
    {
        if (appearance.People != PeopleLook.Human) return Get(appearance)[0];
        if (Supplied(appearance) is { } supplied) return supplied[0];
        if (HumanPortraits.TryGetValue(appearance, out var portrait)) return portrait;
        // La même échelle pour les quatre peuples dans le portrait, y compris l'humain historique de 16 × 24.
        var art = new PixelArt(32, 32);
        var human = Get(appearance)[0].GetImage();
        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 16; x++) art.Dot(x + 8, y + 8, human.GetPixel(x, y));
        return HumanPortraits[appearance] = art.Texture();
    }

    public static Vector2 CarryOffset(PeopleLook people) => people switch
    {
        PeopleLook.Elf => new(6, -15), PeopleLook.Orc => new(9, -15), PeopleLook.Dwarf => new(9, -11), _ => new(5, -13),
    };
    public static int ShadowRadius(PeopleLook people) => people is PeopleLook.Dwarf or PeopleLook.Orc ? 10 : 7;

    private static Assets GetAssets(ColonistAppearance appearance)
    {
        var key = Canonical(appearance);
        if (Cache.TryGetValue(key, out var assets)) return assets;
        ImageTexture[] frames = [Build(key, 0), Build(key, 1), Build(key, 2), Build(key, 3)];
        var image = frames[0].GetImage();
        int left = 32, top = 32, right = 0, bottom = 0;
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
            if (image.GetPixel(x, y).A > 0)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        assets = new(frames, Build(key, 4), new Rect2I(left, top, right - left + 1, bottom - top + 1));
        Cache[key] = assets;
        return assets;
    }

    private static Palette ColorsFor(ColonistAppearance look)
    {
        Color[] skin = look.People == PeopleLook.Orc
            ? [C(118, 151, 93), C(93, 137, 104), C(142, 158, 98), C(82, 119, 106), C(160, 167, 107), C(108, 133, 92)]
            : [C(238, 202, 161), C(213, 165, 122), C(174, 126, 88), C(124, 87, 62), C(228, 182, 142), C(194, 150, 106)];
        Color[] hair = look.People switch
        {
            PeopleLook.Elf => [C(211, 189, 130), C(104, 75, 49), C(188, 125, 77), C(54, 61, 52), C(180, 181, 151), C(133, 91, 66)],
            PeopleLook.Orc => [C(51, 54, 43), C(94, 71, 48), C(138, 112, 73), C(76, 84, 64), C(56, 67, 57), C(128, 88, 63)],
            _ => [C(159, 95, 54), C(81, 57, 44), C(187, 143, 78), C(52, 47, 43), C(138, 113, 78), C(193, 161, 112)],
        };
        Color[] clothes = look.People switch
        {
            PeopleLook.Elf => [C(88, 134, 105), C(124, 143, 104), C(101, 137, 150), C(151, 120, 149)],
            PeopleLook.Orc => [C(159, 96, 63), C(127, 100, 68), C(105, 121, 104), C(128, 93, 78)],
            _ => [C(155, 86, 70), C(100, 124, 150), C(156, 132, 81), C(120, 115, 140)],
        };
        Color cloth = BiomeVisuals.Clothing(look.Biome, look.Id, clothes[look.Id % 4]);
        // Les terres sèches éclaircissent les étoffes, mais les cuirs orques et les bordures naines restent reconnaissables.
        Color accent = look.People switch
        {
            PeopleLook.Elf => C(182, 200, 138), PeopleLook.Orc => C(183, 132, 77), _ => C(214, 179, 96),
        };
        return new(skin[(look.Id * 5 + look.Id / 6) % 6],
            look.Stage == LifeStage.Elder ? C(181, 189, 171) : hair[(look.Id * 7 + look.Id / 4) % 6], cloth, accent);
    }

    private static Body Shape(ColonistAppearance look, int frame)
    {
        int bob = frame is 1 or 3 ? 1 : 0;
        int young = look.Stage == LifeStage.Teen ? 1 : 0;
        return look.People switch
        {
            PeopleLook.Elf => new(3 + bob, 10 + bob, 15 + bob, 5, 24, 2),
            PeopleLook.Orc => new(5 + bob, 12 + bob, 18 + bob, 8 - young, 26, 3),
            _ => new(10 + bob, 16 + bob, 21 + bob, 8 - young, 27, 3),
        };
    }

    private static ImageTexture Build(ColonistAppearance look, int frame)
    {
        var a = new PixelArt(32, 32);
        Palette p = ColorsFor(look);
        Body b = Shape(look, frame);
        HairBack(a, look, b, p);
        Cape(a, look, b, p);
        Legs(a, look, b, p, frame);
        Torso(a, look, b, p);
        Arms(a, look, b, p, frame);
        Climate(a, look, b, p);
        Equipment(a, look, b, p);
        Head(a, look, b, p, frame);
        HairFront(a, look, b, p);
        return a.Texture();
    }

    private static void Legs(PixelArt a, ColonistAppearance look, Body b, Palette p, int frame)
    {
        int swing = frame == 1 ? -1 : frame == 3 ? 1 : 0;
        int width = look.People == PeopleLook.Elf ? 3 : 4;
        Color trousers = look.Biome == WoodlandBiome.WetBank ? C(63, 82, 68) : look.People == PeopleLook.Orc ? C(104, 91, 58) : C(77, 86, 74);
        foreach (int side in new[] { -1, 1 })
        {
            int x = 16 + side * (look.People == PeopleLook.Elf ? 2 : 3) - width / 2 + side * swing;
            int sole = side == swing && swing != 0 ? 30 : 31;
            a.Box(x - 1, b.HipY - 1, width + 2, sole - b.HipY + 2, Ink);
            a.Box(x, b.HipY, width, Math.Max(1, sole - b.HipY - 1), trousers);
            a.Box(x, b.HipY, 1, Math.Max(1, sole - b.HipY - 1), trousers.Lightened(0.16f));
            a.Box(x - (side < 0 ? 1 : 0), sole - 1, width + 1, 1, Leather);
            a.Dot(x, sole - 1, p.Accent.Darkened(0.18f));
        }
    }
    private static void Cape(PixelArt a, ColonistAppearance look, Body b, Palette p)
    {
        if (look.People != PeopleLook.Elf && look.Biome is not (WoodlandBiome.CoolForest or WoodlandBiome.Highland)) return;
        a.Polygon(Ink, new(15 - b.HalfWidth, b.ShoulderY), new(17 + b.HalfWidth, b.ShoulderY),
            new(19 + b.HalfWidth, b.HipY + 1), new(12 - b.HalfWidth, b.HipY + 1));
        a.Polygon(p.Cloth.Darkened(0.3f), new(16 - b.HalfWidth, b.ShoulderY), new(16 + b.HalfWidth, b.ShoulderY),
            new(18 + b.HalfWidth, b.HipY), new(13 - b.HalfWidth, b.HipY));
        a.Line(14 - b.HalfWidth, b.HipY - 1, 18 + b.HalfWidth, b.HipY - 1, p.Accent.Darkened(0.25f));
    }
    private static void Torso(PixelArt a, ColonistAppearance look, Body b, Palette p)
    {
        a.Polygon(Ink, new(16 - b.HalfWidth, b.ShoulderY - 1), new(16 + b.HalfWidth, b.ShoulderY - 1),
            new(17 + b.HalfWidth, b.ShoulderY + 3), new(15 + b.HalfWidth, b.HipY), new(17 - b.HalfWidth, b.HipY), new(15 - b.HalfWidth, b.ShoulderY + 3));
        a.Polygon(p.Cloth, new(17 - b.HalfWidth, b.ShoulderY), new(15 + b.HalfWidth, b.ShoulderY),
            new(16 + b.HalfWidth, b.ShoulderY + 3), new(14 + b.HalfWidth, b.HipY - 1), new(18 - b.HalfWidth, b.HipY - 1), new(16 - b.HalfWidth, b.ShoulderY + 3));
        a.Box(17 - b.HalfWidth, b.ShoulderY + 1, 2, b.HipY - b.ShoulderY - 1, p.Cloth.Lightened(0.22f));
        a.Box(13 + b.HalfWidth, b.ShoulderY + 2, 2, Math.Max(1, b.HipY - b.ShoulderY - 2), p.Cloth.Darkened(0.22f));
        a.Box(18 - b.HalfWidth, b.HipY - 2, b.HalfWidth * 2 - 3, 2, Leather);
        a.Box(16, b.HipY - 2, 2, 2, look.People == PeopleLook.Orc ? Linen : Gold);
        if (look.People == PeopleLook.Elf)
        {
            a.Line(14, b.ShoulderY + 1, 19, b.HipY - 3, p.Accent.Darkened(0.1f));
            a.Dot(18, b.ShoulderY + 2, Gold); a.Dot(17, b.ShoulderY + 3, p.Accent);
        }
        else if (look.People == PeopleLook.Orc)
        {
            a.Line(10, b.ShoulderY, 21, b.HipY - 3, Leather);
            a.Line(11, b.ShoulderY, 22, b.HipY - 3, p.Accent);
            a.Box(9, b.ShoulderY, 5, 2, C(169, 151, 108));
            a.Dot(9, b.ShoulderY - 1, Linen); a.Dot(12, b.ShoulderY - 1, Linen);
        }
        else
        {
            a.Box(11, b.ShoulderY + 1, 1, Math.Max(1, b.HipY - b.ShoulderY - 2), Gold);
            a.Box(21, b.ShoulderY + 1, 1, Math.Max(1, b.HipY - b.ShoulderY - 2), Gold);
            a.Box(12, b.HipY - 3, 3, 1, Iron); a.Box(19, b.HipY - 3, 3, 1, Iron);
        }
    }
    private static void Arms(PixelArt a, ColonistAppearance look, Body b, Palette p, int frame)
    {
        int swing = frame == 1 ? 1 : frame == 3 ? -1 : 0;
        int width = look.People == PeopleLook.Elf ? 2 : 3;
        foreach (int side in new[] { -1, 1 })
        {
            int x = side < 0 ? 15 - b.HalfWidth - width : 17 + b.HalfWidth;
            int y = b.ShoulderY + 2 + side * swing, length = look.People == PeopleLook.Dwarf ? 4 : 6;
            a.Box(x - 1, y - 1, width + 2, length + 1, Ink);
            bool bare = look.Biome == WoodlandBiome.Dryland || look.People == PeopleLook.Orc
                && look.Biome is not (WoodlandBiome.CoolForest or WoodlandBiome.Highland);
            Color sleeve = bare ? p.Skin : p.Cloth;
            a.Box(x, y, width, length - 2, sleeve);
            Color hand = look.Biome == WoodlandBiome.Highland ? Leather : p.Skin;
            a.Box(x, y + length - 2, width, 2, hand);
            a.Dot(x, y + length - 2, hand.Lightened(0.13f));
            a.Box(x, y + length - 3, width, 1, look.People == PeopleLook.Elf ? p.Accent : Leather);
        }
    }
}
