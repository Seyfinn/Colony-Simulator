using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

public static partial class PeoplesSprites
{
    private static void HairBack(PixelArt a, ColonistAppearance look, Body b, Palette p)
    {
        bool longHair = look.People == PeopleLook.Elf && (look.Sex == Sex.Female || look.Id % 3 != 0);
        bool braids = look.People == PeopleLook.Dwarf && look.Sex == Sex.Female;
        if (!longHair && !braids && !(look.People == PeopleLook.Orc && look.Id % 4 == 2)) return;
        int width = look.People == PeopleLook.Elf ? 4 : 3;
        foreach (int side in new[] { -1, 1 })
        {
            int x = side < 0 ? 10 : 21, end = look.People == PeopleLook.Elf ? 22 : 25;
            a.Box(x - 1, b.FaceY - 4, width + 1, end - b.FaceY + 5, Ink);
            a.Box(x, b.FaceY - 4, width - 1, end - b.FaceY + 4, p.Hair.Darkened(0.12f));
            a.Line(x, b.FaceY - 3, x + side, end - 1, p.Hair.Lightened(0.17f));
            if (braids || look.Id % 3 == 1)
            {
                for (int y = b.FaceY + 2; y < end; y += 3) a.Box(x, y, 2, 1, p.Hair.Darkened(0.3f));
                a.Box(x, end - 2, width - 1, 1, Gold);
            }
        }
    }

    private static void Climate(PixelArt a, ColonistAppearance look, Body b, Palette p)
    {
        switch (look.Biome)
        {
            case WoodlandBiome.Dryland:
                a.Box(14, b.ShoulderY, 5, 2, Linen);
                a.Line(18, b.ShoulderY + 1, 21, b.ShoulderY + 5, Linen.Darkened(0.18f));
                a.Box(18 - b.HalfWidth, b.ShoulderY + 3, 1, b.HipY - b.ShoulderY - 3, Linen.Darkened(0.12f));
                break;
            case WoodlandBiome.CoolForest:
                a.Box(16 - b.HalfWidth, b.ShoulderY, b.HalfWidth * 2 + 1, 2, p.Cloth.Darkened(0.22f));
                a.Line(18 - b.HalfWidth, b.ShoulderY, 15, b.ShoulderY + 3, p.Accent);
                a.Dot(19, b.ShoulderY + 1, Gold);
                if (look.People == PeopleLook.Elf)
                {
                    a.Line(11, b.HipY, 13, b.HipY + 1, p.Accent);
                    a.Line(20, b.HipY, 22, b.HipY - 1, p.Accent);
                }
                break;
            case WoodlandBiome.Highland:
                Color wool = look.People == PeopleLook.Orc ? C(177, 164, 122) : Linen;
                a.Box(16 - b.HalfWidth, b.ShoulderY, b.HalfWidth * 2 + 1, 2, wool.Darkened(0.15f));
                for (int x = 17 - b.HalfWidth; x < 16 + b.HalfWidth; x += 3) a.Dot(x, b.ShoulderY - 1, wool);
                a.Box(18 - b.HalfWidth, b.HipY - 3, b.HalfWidth * 2 - 3, 1, wool.Darkened(0.13f));
                a.Dot(20, b.ShoulderY + 2, Gold);
                break;
            case WoodlandBiome.WetBank:
                Color oilcloth = look.People == PeopleLook.Elf ? C(68, 106, 100) : Leather;
                a.Box(18 - b.HalfWidth, b.ShoulderY + 1, 2, b.HipY - b.ShoulderY - 2, oilcloth);
                a.Box(14 + b.HalfWidth, b.ShoulderY + 1, 2, b.HipY - b.ShoulderY - 2, oilcloth.Darkened(0.18f));
                for (int y = b.ShoulderY + 2; y < b.HipY - 2; y += 2) a.Box(14, y, 5, 1, Linen.Darkened(0.1f));
                a.Box(12, b.HipY, 2, 2, C(60, 80, 66)); a.Box(19, b.HipY, 2, 2, C(60, 80, 66));
                break;
        }
    }

    private static void Equipment(PixelArt a, ColonistAppearance look, Body b, Palette p)
    {
        switch (look.Trade)
        {
            case OutfitTrade.Workshop:
                a.Polygon(Leather.Darkened(0.13f), new(13, b.ShoulderY + 1), new(21, b.ShoulderY + 1), new(22, b.HipY - 1), new(11, b.HipY - 1));
                a.Line(13, b.ShoulderY + 1, 12, b.HipY - 2, C(166, 120, 72));
                a.Box(15, b.HipY - 4, 5, 2, Leather.Darkened(0.3f));
                a.Dot(14, b.ShoulderY + 2, Gold); a.Dot(20, b.ShoulderY + 2, Gold);
                a.Box(21, b.HipY - 1, 2, 2, Iron); a.Dot(21, b.HipY - 1, Linen);
                break;
            case OutfitTrade.Stonework:
                a.Box(18 - b.HalfWidth, b.HipY - 2, b.HalfWidth * 2 - 3, 2, C(144, 106, 63));
                a.Box(10, b.HipY - 1, 1, 3, Leather.Lightened(0.28f));
                a.Box(8, b.HipY - 2, 5, 2, Iron); a.Box(8, b.HipY - 2, 4, 1, Iron.Lightened(0.3f));
                a.Box(20, b.HipY - 1, 3, 3, Leather); a.Dot(21, b.HipY - 1, Gold);
                break;
            case OutfitTrade.Field:
                a.Polygon(Linen.Darkened(0.17f), new(13, b.ShoulderY + 2), new(20, b.ShoulderY + 2), new(21, b.HipY - 1), new(12, b.HipY - 1));
                a.Box(14, b.HipY - 3, 5, 1, Linen.Lightened(0.1f));
                a.Oval(21, b.HipY, 2, 2, Leather); a.Dot(21, b.HipY - 1, p.Accent);
                break;
            case OutfitTrade.Woodland:
                a.Line(11, b.ShoulderY, 20, b.HipY - 3, Leather);
                a.Line(12, b.ShoulderY, 21, b.HipY - 3, p.Accent.Darkened(0.15f));
                a.Box(21, b.HipY - 2, 4, 3, Ink); a.Box(21, b.HipY - 2, 3, 2, Leather);
                a.Dot(22, b.HipY - 2, Gold);
                break;
            default:
                a.Dot(19, b.HipY - 3, p.Accent);
                break;
        }
    }

    private static void Head(PixelArt a, ColonistAppearance look, Body b, Palette p, int frame)
    {
        int radius = look.People == PeopleLook.Elf ? 4 : 6;
        a.Box(14, b.FaceY + 3, 5, System.Math.Max(1, b.ShoulderY - b.FaceY - 3), p.Skin.Darkened(0.18f));
        a.Oval(16, b.FaceY - 1, radius + 1, 6, Ink);
        a.Oval(16, b.FaceY, radius, 4, p.Skin);
        a.Box(13, b.FaceY - 2, 3, 3, p.Skin.Lightened(0.1f));
        a.Line(16 + radius - 1, b.FaceY, 16 + radius - 2, b.FaceY + 3, p.Skin.Darkened(0.16f));
        if (look.People is PeopleLook.Elf or PeopleLook.Orc)
        {
            int tip = look.People == PeopleLook.Elf ? 8 : 7;
            if (look.Stage == LifeStage.Child) tip++;
            a.Polygon(Ink, new(tip, b.FaceY - 4), new(12, b.FaceY - 2), new(12, b.FaceY + 1), new(tip + 1, b.FaceY - 1));
            a.Polygon(p.Skin, new(tip + 1, b.FaceY - 3), new(12, b.FaceY - 1), new(11, b.FaceY));
            a.Polygon(Ink, new(32 - tip, b.FaceY - 4), new(20, b.FaceY - 2), new(20, b.FaceY + 1), new(31 - tip, b.FaceY - 1));
            a.Polygon(p.Skin.Darkened(0.1f), new(31 - tip, b.FaceY - 3), new(20, b.FaceY - 1), new(21, b.FaceY));
            a.Dot(tip + 2, b.FaceY - 2, p.Skin.Lightened(0.18f));
        }
        else
        {
            a.Box(9, b.FaceY - 1, 2, 3, p.Skin.Darkened(0.15f));
            a.Box(22, b.FaceY - 1, 2, 3, p.Skin.Darkened(0.15f));
        }
        if (look.Sex == Sex.Female && look.People == PeopleLook.Orc)
        {
            a.Dot(24, b.FaceY, Gold); a.Dot(24, b.FaceY + 1, p.Accent);
        }
        foreach (int side in new[] { -1, 1 })
        {
            int x = 16 + side * b.EyeGap;
            if (frame == 4) a.Box(x - 1, b.FaceY - 1, 2, 1, p.Skin.Darkened(0.35f));
            else
            {
                a.Dot(x, b.FaceY - 1, Ink);
                a.Dot(x, b.FaceY - 2, p.Hair.Darkened(0.2f));
            }
        }
        a.Box(15, b.FaceY + 1, look.People == PeopleLook.Elf ? 1 : 3, 1, p.Skin.Lightened(0.22f));
        a.Box(15, b.FaceY + 3, 3, 1, p.Skin.Darkened(0.3f));
        if (look.People == PeopleLook.Orc)
        {
            a.Box(12, b.FaceY + 3, 9, 1, p.Skin.Darkened(0.4f));
            foreach (int x in new[] { 12, 20 })
            {
                a.Box(x, b.FaceY + 2, 1, look.Stage == LifeStage.Child ? 1 : 3, Linen);
                a.Dot(x, b.FaceY + 2, Linen.Lightened(0.16f));
            }
            if (look.Id % 3 == 1 && look.Stage != LifeStage.Child) a.Line(21, b.FaceY - 2, 20, b.FaceY, p.Skin.Lightened(0.24f));
            if (look.Id % 2 == 0) a.Dot(8, b.FaceY + 1, Gold);
        }
    }

    private static void HairFront(PixelArt a, ColonistAppearance look, Body b, Palette p)
    {
        int width = look.People == PeopleLook.Elf ? 9 : 13, left = 16 - width / 2;
        a.Box(left - 1, b.HeadTop, width + 2, 2, Ink);
        a.Box(left, b.HeadTop, width, 3, p.Hair);
        a.Box(left + 1, b.HeadTop, width - 3, 1, p.Hair.Lightened(0.23f));
        int style = look.Id % 4;
        bool frontBraids = look.People == PeopleLook.Dwarf && look.Sex == Sex.Female
            || look.People == PeopleLook.Elf && (look.Sex == Sex.Female || look.Id % 3 != 0);
        if (frontBraids)
        {
            int end = look.People == PeopleLook.Elf ? 23 : 26;
            foreach (int x in new[] { look.People == PeopleLook.Elf ? 11 : 10, 21 })
            {
                a.Box(x, b.FaceY + 1, 2, end - b.FaceY, p.Hair.Darkened(0.15f));
                a.Line(x, b.FaceY + 1, x, end - 1, p.Hair.Lightened(0.18f));
                if (look.Sex == Sex.Female)
                {
                    for (int y = b.FaceY + 3; y < end - 1; y += 3) a.Dot(x + 1, y, p.Hair.Darkened(0.33f));
                    a.Box(x, end - 1, 2, 1, Gold);
                }
            }
        }
        if (look.People == PeopleLook.Elf)
        {
            a.Line(12, b.HeadTop + 2, 12, b.FaceY - 1, p.Hair);
            if (style == 0) a.Line(18, b.HeadTop + 2, 20, b.HeadTop + 4, p.Hair.Darkened(0.15f));
            else if (style == 2) a.Box(13, b.HeadTop + 3, 3, 2, p.Hair);
            a.Dot(21, b.HeadTop + 2, p.Accent); a.Dot(22, b.HeadTop + 1, p.Accent.Lightened(0.15f));
            a.Dot(20, b.HeadTop + 3, Gold);
        }
        else if (look.People == PeopleLook.Orc)
        {
            if (style is 0 or 1)
            {
                a.Box(13, b.HeadTop + 1, 8, 2, p.Skin.Darkened(0.13f));
                a.Box(15, b.HeadTop - 2, 3, 4, p.Hair); a.Dot(15, b.HeadTop - 2, p.Hair.Lightened(0.22f));
                if (style == 1) a.Box(14, b.HeadTop - 1, 5, 1, p.Hair);
            }
            else if (style == 3)
            {
                a.Box(12, b.HeadTop, 9, 2, p.Skin.Darkened(0.08f));
                a.Box(20, b.HeadTop - 1, 3, 3, p.Hair);
                a.Dot(22, b.HeadTop - 1, Gold);
            }
        }
        else if (look.Sex == Sex.Male && look.Stage != LifeStage.Child)
        {
            Beard(a, look, b, p);
        }
        if (look.Biome == WoodlandBiome.Highland)
        {
            a.Box(left - 1, b.HeadTop - 1, width + 2, 2, p.Cloth.Darkened(0.25f));
            a.Box(left, b.HeadTop - 1, width, 1, p.Cloth.Lightened(0.2f));
            a.Box(left, b.HeadTop + 1, width, 1, Linen.Darkened(0.1f));
            a.Box(left - 1, b.HeadTop + 2, 1, 3, p.Cloth);
            a.Box(left + width, b.HeadTop + 2, 1, 3, p.Cloth.Darkened(0.2f));
        }
        else if (look.Biome == WoodlandBiome.Dryland)
        {
            if (look.People == PeopleLook.Dwarf)
            {
                a.Box(left, b.HeadTop, width, 2, C(207, 166, 95));
                a.Box(left - 2, b.HeadTop + 2, width + 4, 1, C(233, 197, 124));
                a.Box(left + 1, b.HeadTop + 1, width - 2, 1, Leather);
            }
            else
            {
                a.Box(left, b.HeadTop + 2, width, 1, Linen);
                a.Box(left + width - 1, b.HeadTop + 3, 1, 3, Linen.Darkened(0.14f));
            }
        }
        else if (look.Biome == WoodlandBiome.WetBank)
        {
            a.Box(left, b.HeadTop + 1, width, 1, p.Accent.Darkened(0.15f));
            a.Dot(left + 2, b.HeadTop + 1, Linen);
        }
        if (look.Trade == OutfitTrade.Stonework)
        {
            a.Box(left, b.HeadTop, width, 2, Leather);
            a.Box(left, b.HeadTop, width, 1, C(183, 143, 86));
            a.Dot(left + 2, b.HeadTop + 1, Iron); a.Dot(left + width - 3, b.HeadTop + 1, Iron);
        }
        // Les anciennes orques gardent leurs mèches argentées même sous le couvre-chef.
        if (look.Stage == LifeStage.Elder && look.People == PeopleLook.Orc)
            a.Box(left, b.HeadTop + 3, 2, 2, p.Hair);
    }

    private static void Beard(PixelArt a, ColonistAppearance look, Body b, Palette p)
    {
        int y = b.FaceY + 2;
        bool apprentice = look.Stage == LifeStage.Teen;
        int bottom = apprentice ? b.FaceY + 5 : 26;
        a.Polygon(Ink, new(10, y), new(22, y), new(22, bottom - 2), new(19, bottom + 1), new(13, bottom + 1), new(10, bottom - 2));
        a.Polygon(p.Hair, new(11, y), new(21, y), new(21, bottom - 2), new(18, bottom), new(14, bottom), new(11, bottom - 2));
        a.Box(14, y, 5, 1, p.Hair.Darkened(0.3f));
        a.Box(15, y, 3, 1, p.Skin.Darkened(0.2f));
        int style = look.Id % 3;
        if (!apprentice)
        {
            if (style == 0)
            {
                a.Box(16, bottom - 2, 1, 3, Ink);
                a.Box(13, bottom - 1, 2, 1, Gold); a.Box(18, bottom - 1, 2, 1, Gold);
            }
            else if (style == 1)
            {
                a.Line(13, y + 1, 15, bottom - 1, p.Hair.Lightened(0.22f));
                a.Line(20, y + 1, 18, bottom - 1, p.Hair.Darkened(0.23f));
                a.Box(15, bottom - 1, 4, 1, Gold);
            }
            else
            {
                a.Box(12, bottom - 1, 9, 1, p.Hair.Darkened(0.13f));
                a.Box(13, y + 1, 1, 3, p.Hair.Lightened(0.2f));
            }
        }
        else a.Line(12, y + 1, 20, y + 1, p.Hair.Lightened(0.17f));
    }
}
