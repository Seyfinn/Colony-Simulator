using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>Petits éléments purement décoratifs : ils ne créent aucune ressource récoltable.</summary>
public static class EnvironmentDetails
{
    private static readonly Dictionary<(WoodlandBiome Biome, int Variant), ImageTexture> Textures = [];

    public static void Draw(CanvasItem canvas, LocalMap map, int x, int y, Vector2 origin)
    {
        if (map.HasWater(x, y) || map.IsCanal(x, y)) return;
        Surface surface = map.GetSurface(x, y);
        if (surface is Surface.Grass or Surface.Dirt or Surface.Sand && RiverbankDetails.Draw(canvas, map, x, y, origin)) return;
        if (surface is not (Surface.Grass or Surface.Dirt or Surface.Stone)) return;
        WoodlandBiome biome = BiomeVisuals.At(map, x, y);
        if (surface == Surface.Stone && biome != WoodlandBiome.Highland) return;
        // Des roseaux longent aussi les nouvelles retenues, sans reclasser toute la forêt d'origine.
        bool waterNear = Water(map, x - 1, y) || Water(map, x + 1, y) || Water(map, x, y - 1) || Water(map, x, y + 1);
        if (waterNear && surface != Surface.Stone) biome = WoodlandBiome.WetBank;
        float density = biome switch
        {
            WoodlandBiome.WetBank => waterNear ? 0.43f : 0.2f,
            WoodlandBiome.CoolForest => 0.21f, WoodlandBiome.Dryland => 0.16f,
            WoodlandBiome.Highland => 0.13f, _ => 0.12f,
        };
        if (map.Biome is Biome.Tundra or Biome.Desert or Biome.IceSheet) density *= 0.3f;
        if (map.Biome == Biome.TropicalForest) density *= 1.5f;
        if (Noise.Hash01(x, y, 163, map.Seed) > density) return;
        int variant = Math.Min(2, (int)(Noise.Hash01(x, y, 167, map.Seed) * 3));
        Texture2D sprite = Get(biome, variant);
        Vector2 foot = origin + new Vector2(10 + Noise.Hash01(x, y, 169, map.Seed) * 12, 20 + Noise.Hash01(x, y, 173, map.Seed) * 8).Round();
        Color tint = map.Biome == Biome.TropicalForest ? new Color(0.8f, 1.08f, 0.82f) : Colors.White;
        canvas.DrawTexture(sprite, foot - new Vector2(sprite.GetWidth() / 2f, sprite.GetHeight()), tint);
    }

    private static bool Water(LocalMap map, int x, int y) => map.InBounds(x, y) && (map.HasWater(x, y) || map.IsCanalWet(x, y));

    public static ImageTexture Get(WoodlandBiome biome, int variant = 0)
    {
        variant = Math.Clamp(variant, 0, 2);
        var key = (biome, variant);
        if (Textures.TryGetValue(key, out var sprite)) return sprite;
        var a = new PixelArt(24, 24);
        Color green = Color.Color8(67, 112, 76), light = Color.Color8(151, 171, 103);
        switch (biome)
        {
            case WoodlandBiome.WetBank:
                a.Oval(12, 22, 9, 1, green.Darkened(0.15f));
                for (int i = 0; i < 5; i++)
                {
                    int x = 5 + i * 3, top = 5 + (i * 3 + variant * 5) % 9;
                    a.Line(x, 21, x + i % 2, top, green); a.Line(x + 1, 21, x + i % 2 + 1, top + 1, light);
                    a.Line(x, 17, x - 3, 13 + i % 3, green);
                    if (i % 2 == variant % 2)
                    {
                        a.Box(x + i % 2, top - 4, 2, 5, Color.Color8(130, 85, 53));
                        a.Dot(x + i % 2, top - 4, Color.Color8(186, 132, 74));
                    }
                }
                break;
            case WoodlandBiome.CoolForest:
                if (variant == 2)
                {
                    foreach (var p in new[] { new Vector2I(7, 19), new Vector2I(14, 16), new Vector2I(19, 20) })
                    {
                        a.Box(p.X, p.Y, 2, 4, Color.Color8(219, 199, 155));
                        a.Oval(p.X + 1, p.Y, 3, 2, Color.Color8(171, 103, 76));
                        a.Dot(p.X, p.Y - 1, Color.Color8(236, 200, 151));
                    }
                }
                else
                {
                    for (int i = -2; i <= 2; i++)
                    {
                        int ex = 12 + i * 4, ey = 9 + Math.Abs(i) * 3 + variant;
                        a.Line(12, 22, ex, ey, green);
                        for (int j = 2; j <= 8; j += 2)
                        {
                            int x = 12 + (ex - 12) * j / 9, y = 22 + (ey - 22) * j / 9;
                            a.Line(x, y, x - 3, y - 1, Color.Color8(85, 141, 94));
                            a.Line(x, y, x + 3, y - 2, light.Darkened(0.14f));
                        }
                    }
                }
                break;
            case WoodlandBiome.Highland:
                a.Polygon(Color.Color8(91, 113, 107), new(2, 22), new(6, 15), new(14, 14), new(19, 22));
                a.Polygon(Color.Color8(171, 184, 161), new(3, 21), new(7, 15), new(13, 15), new(14, 19));
                a.Line(14, 15, 17, 20, Color.Color8(129, 151, 148));
                a.Box(7, 17, 4, 2, Color.Color8(184, 189, 134));
                a.Oval(20, 21, 3, 2, Color.Color8(133, 151, 137));
                if (variant == 1)
                {
                    a.Line(19, 20, 20, 16, green); a.Box(19, 15, 3, 2, Color.Color8(218, 214, 179));
                }
                break;
            default:
                bool dry = biome == WoodlandBiome.Dryland;
                Color stem = dry ? Color.Color8(150, 129, 75) : green;
                Color tip = dry ? Color.Color8(206, 186, 118) : light;
                for (int i = -2; i <= 2; i++)
                {
                    int x = 12 + i * 3, y = 13 + Math.Abs(i) * 2 - variant;
                    a.Line(12 + i, 22, x, y, stem); a.Dot(x, y, tip);
                    if (!dry && variant != 0 && i % 2 == 0)
                    {
                        a.Box(x - 1, y - 2, 3, 2, variant == 1 ? Color.Color8(230, 206, 145) : Color.Color8(191, 157, 174));
                        a.Dot(x, y - 2, Color.Color8(249, 231, 187));
                    }
                }
                if (dry && variant == 2) a.Oval(18, 21, 3, 2, Color.Color8(165, 149, 113));
                break;
        }
        return Textures[key] = a.Texture();
    }
}
