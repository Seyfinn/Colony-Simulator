using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>Roseaux, touffes et galets sur la terre bordant un fleuve ; aucune ressource supplémentaire.</summary>
public static class RiverbankDetails
{
    private static readonly Dictionary<(Biome, int), ImageTexture> Textures = [];

    // True réserve cette berge à ses détails, même lorsqu'aucune touffe n'y est tirée.
    public static bool Draw(CanvasItem canvas, LocalMap map, int x, int y, Vector2 origin)
    {
        bool Wide(int ax, int ay) => map.InBounds(ax, ay) && map.IsWideRiver(ax, ay) && !map.IsFlooded(ax, ay);
        Vector2 toward;
        Span<Vector2> sides = stackalloc Vector2[4];
        int count = 0;
        if (Wide(x, y - 1)) sides[count++] = Vector2.Up;
        if (Wide(x + 1, y)) sides[count++] = Vector2.Right;
        if (Wide(x, y + 1)) sides[count++] = Vector2.Down;
        if (Wide(x - 1, y)) sides[count++] = Vector2.Left;
        if (count == 0) return false;
        float pick = Noise.Hash01(x, y, 229, map.Seed);
        float density = map.Biome switch
        {
            Biome.Swamp => 0.72f, Biome.TropicalForest => 0.62f,
            Biome.Desert => 0.22f, Biome.Tundra or Biome.IceSheet => 0.3f,
            _ => 0.48f,
        };
        if (map.GetFlora(x, y) == FloraType.Tree || Noise.Hash01(x, y, 233, map.Seed) > density) return true;
        toward = sides[Math.Min(count - 1, (int)(pick * count))];
        float along = (Noise.Hash01(x, y, 239, map.Seed) - 0.5f) * 12;
        // Le pied reste sur la terre, à quatre pixels de l'eau ; les silhouettes demeurent petites et lisibles.
        Vector2 foot = origin + (new Vector2(16, 16) + toward * 12 + new Vector2(-toward.Y, toward.X) * along).Round();
        int variant = Math.Min(2, (int)(Noise.Hash01(x, y, 241, map.Seed) * 3));
        Texture2D sprite = Get(map.Biome, variant);
        canvas.DrawTexture(sprite, foot - new Vector2(10, 20));
        return true;
    }

    public static ImageTexture Get(Biome biome, int variant)
    {
        var key = (biome, variant);
        if (Textures.TryGetValue(key, out var texture)) return texture;
        var a = new PixelArt(20, 20);
        bool cold = biome is Biome.Tundra or Biome.IceSheet;
        bool dry = biome is Biome.Desert or Biome.Savanna or Biome.Steppe;
        Color stem = cold ? Color.Color8(105, 127, 117) : dry ? Color.Color8(126, 135, 78) : Color.Color8(65, 113, 79);
        Color light = cold ? Color.Color8(173, 181, 151) : dry ? Color.Color8(192, 179, 112) : Color.Color8(151, 174, 112);
        a.Oval(10, 18, 7, 1, stem.Darkened(0.18f));
        if (variant == 2 || cold)
        {
            for (int i = 0; i < 3; i++)
            {
                int px = 4 + i * 5, py = 17 - i % 2;
                a.Oval(px, py, 3, 2, Color.Color8(123, 137, 123));
                a.Line(px - 2, py - 1, px + 1, py - 1, Color.Color8(191, 193, 161));
            }
            a.Line(11, 18, 12, 12, stem); a.Line(11, 18, 8, 13, light);
        }
        else
        {
            int count = variant == 0 ? 4 : 5;
            for (int i = 0; i < count; i++)
            {
                int px = 4 + i * 3, top = (variant == 0 ? 4 : 10) + (i * 3) % 5;
                a.Line(px, 18, px + i % 2, top, stem);
                a.Line(px + 1, 18, px + i % 2 + 1, top + 1, light);
                a.Line(px, 15, px - 3, 11 + i % 3, stem);
                if (variant == 0 && i % 2 == 0)
                {
                    a.Box(px, top - 3, 2, 4, Color.Color8(123, 86, 51));
                    a.Dot(px, top - 3, Color.Color8(191, 149, 88));
                }
            }
        }
        return Textures[key] = a.Texture();
    }
}
