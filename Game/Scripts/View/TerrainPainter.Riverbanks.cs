using System;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.View;

public static partial class TerrainPainter
{
    // Décor contenu dans la case, dépendant des huit voisines seulement : le rafraîchissement existant suffit.
    // Aucun objet Godot ni cache partagé ici : la peinture des morceaux tourne sur plusieurs fils.
    private static void PaintRiverbank(LocalMap map, int x, int y, byte[] pixels, int stride, int ox, int oy,
        bool wide, int riverMask, int riverCorners)
    {
        if (map.IsCanal(x, y) || map.IsFlooded(x, y)) return;
        Surface ground = map.GetSurface(x, y);
        if (!wide && (ground is not (Surface.Grass or Surface.Dirt or Surface.Sand) || riverMask != 0 || riverCorners != 0)) return;
        int edges;
        if (wide) edges = RiverTiles.LakeEdges(map, x, y, riverMask);
        else
        {
            bool Wide(int ax, int ay) => map.InBounds(ax, ay) && map.IsWideRiver(ax, ay)
                && !map.IsFlooded(ax, ay) && !map.IsCanal(ax, ay);
            edges = (Wide(x, y - 1) ? 1 : 0) | (Wide(x + 1, y) ? 2 : 0)
                | (Wide(x, y + 1) ? 4 : 0) | (Wide(x - 1, y) ? 8 : 0);
        }
        if (edges == 0) return;
        SoilPalette soil = SoilFor(map.Biome);
        bool marsh = map.Biome is Biome.Swamp or Biome.TropicalForest;
        Rgb sediment = marsh ? Blend(soil.Dark, soil.WetSand, 0.28f) : soil.Sand;
        var scallop = new Noise.Value2DCursor(211);
        var deposits = new Noise.Value2DCursor(223);
        var pebbles = new CellHash(227);
        for (int py = 0; py < TileSize; py++)
        for (int px = 0; px < TileSize; px++)
        {
            float distance = TileSize;
            if ((edges & 1) != 0) distance = Math.Min(distance, py);
            if ((edges & 2) != 0) distance = Math.Min(distance, 31 - px);
            if ((edges & 4) != 0) distance = Math.Min(distance, 31 - py);
            if ((edges & 8) != 0) distance = Math.Min(distance, px);
            // Arrondir les coins de terre vers l'intérieur du fleuve sans interrompre les embouchures.
            if (wide)
            {
                const float radius = 8;
                if ((edges & 9) == 9 && px < radius && py < radius)
                    distance = Math.Min(distance, radius - MathF.Sqrt((px - radius) * (px - radius) + (py - radius) * (py - radius)));
                if ((edges & 3) == 3 && px > 31 - radius && py < radius)
                    distance = Math.Min(distance, radius - MathF.Sqrt((px - 31 + radius) * (px - 31 + radius) + (py - radius) * (py - radius)));
                if ((edges & 12) == 12 && px < radius && py > 31 - radius)
                    distance = Math.Min(distance, radius - MathF.Sqrt((px - radius) * (px - radius) + (py - 31 + radius) * (py - 31 + radius)));
                if ((edges & 6) == 6 && px > 31 - radius && py > 31 - radius)
                    distance = Math.Min(distance, radius - MathF.Sqrt((px - 31 + radius) * (px - 31 + radius) + (py - 31 + radius) * (py - 31 + radius)));
            }
            if (distance > (wide ? 12 : 20)) continue;
            int wx = x * TileSize + px, wy = y * TileSize + py;
            float wave = scallop.At(wx / 19f, wy / 19f);
            float patch = deposits.At(wx / 59f, wy / 59f);
            float band = wide ? 4 + wave * 4 + patch * 2 : (marsh ? 3 : 5) + wave * 5 + patch * 8;
            int index = ((oy + py) * stride + ox + px) * 4;
            Rgb color = new(pixels[index], pixels[index + 1], pixels[index + 2]);
            if (wide)
            {
                float lip = 0.6f + wave * 1.6f;
                if (distance < lip) color = Blend(soil.Dark, soil.WetSand, 0.58f);
                else
                {
                    float shallows = Math.Clamp(1 - (distance - lip) / (band + 2), 0, 1);
                    Rgb submerged = Blend(soil.WetSand, new Rgb(112, 166, 150), marsh ? 0.68f : 0.52f);
                    color = Blend(color, submerged, shallows * 0.66f);
                    if (distance < lip + 1) color = Blend(color, Foam, 0.16f);
                }
            }
            else
            {
                float cover = Math.Clamp((band - distance) / 3f, 0, 1);
                // Des plages par grandes taches, au lieu d'un ruban de sable uniforme.
                cover *= ground == Surface.Sand ? 0.62f : Math.Clamp((patch - 0.2f) * 2, 0.24f, 0.95f);
                Rgb bank = distance < 2 ? soil.WetSand : sediment;
                color = Blend(color, bank, cover);
                float pebble = pebbles.At(wx / 5, wy / 4);
                if (distance > 2 && distance < band && pebble > 0.9f && wx % 5 is 1 or 2 && wy % 4 is 1 or 2)
                    color = wy % 4 == 1 ? Blend(soil.Sand, Rock, 0.38f) : Blend(soil.Dark, RockShade, 0.45f);
            }
            pixels[index] = color.R; pixels[index + 1] = color.G; pixels[index + 2] = color.B;
        }
    }
}
