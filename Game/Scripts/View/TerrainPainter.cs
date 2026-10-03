using System;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Terrain pastoral en 32 pixels : sols nuancés, rives, roche stratifiée et veines de fer.</summary>
public static class TerrainPainter
{
    public const int TileSize = 32;
    private const int StratumHeight = 8;
    private readonly record struct Rgb(byte R, byte G, byte B);
    private static readonly Rgb Meadow = new(119, 151, 94), Moss = new(97, 129, 85), SunlitGrass = new(143, 167, 109);
    private static readonly Rgb Clay = new(165, 123, 85), Loam = new(130, 101, 71);
    private static readonly Rgb Sand = new(224, 208, 164), WetSand = new(178, 164, 126);
    private static readonly Rgb Rock = new(153, 165, 152), RockShade = new(112, 130, 123);
    private static readonly Rgb Rust = new(178, 111, 64), OreLight = new(221, 164, 100);
    private static readonly Rgb Lake = new(91, 151, 157), Depth = new(61, 114, 136), Foam = new(193, 216, 194);

    public static byte[] Paint(LocalMap map, int tileX0, int tileY0, int tilesWide, int tilesHigh)
    {
        int width = tilesWide * TileSize;
        var pixels = new byte[width * tilesHigh * TileSize * 4];
        for (int ty = 0; ty < tilesHigh; ty++)
        for (int tx = 0; tx < tilesWide; tx++)
        {
            int x = tileX0 + tx, y = tileY0 + ty;
            if (!map.InBounds(x, y)) continue;
            PaintTile(map, x, y, pixels, width, tx * TileSize, ty * TileSize);
        }
        return pixels;
    }

    private static void PaintTile(LocalMap map, int x, int y, byte[] pixels, int stride, int ox, int oy)
    {
        int height = map.GetElevation(x, y);
        // Une rivière se dessine comme de l'eau (rives, pas de falaise) ; seule sa teinte, plus claire, change.
        bool river = map.IsRiver(x, y);
        Surface surface = Structural(map.GetSurface(x, y));
        int north = Elevation(map, x, y - 1, height), south = Elevation(map, x, y + 1, height);
        int west = Elevation(map, x - 1, y, height), east = Elevation(map, x + 1, y, height);
        Surface n = Neighbor(map, x, y - 1, surface), s = Neighbor(map, x, y + 1, surface);
        Surface w = Neighbor(map, x - 1, y, surface), e = Neighbor(map, x + 1, y, surface);
        int cliff = surface != Surface.Water ? Math.Min(Math.Max(0, north - height), 3) * StratumHeight : 0;
        float ambient = 0.86f + height * 0.016f;
        for (int py = 0; py < TileSize; py++)
        for (int px = 0; px < TileSize; px++)
        {
            int wx = x * TileSize + px, wy = y * TileSize + py;
            Rgb color;
            if (py < cliff)
            {
                int band = py / StratumHeight;
                int layer = north - band - 1;
                Material material = map.MaterialAt(x, y - 1, layer);
                color = material == Material.Soil ? Blend(Loam, Clay, 0.38f) : RockPixel(wx, wy, material == Material.IronOre);
                int stratum = py % StratumHeight;
                // Chaque strate possède des fractures décalées et une arête propre.
                int joint = (wx + band * 9) % 23;
                float shade = stratum == 0 ? 0.75f : stratum == 7 ? 0.52f : 0.62f + stratum * 0.016f;
                if (joint is 0 or 1 && stratum is > 2 and < 7) shade *= 0.8f;
                color = Tint(color, shade);
            }
            else
            {
                color = river ? RiverPixel(wx, wy) : GroundPixel(surface, wx, wy, height);
                int edge = 2 + (int)(Noise.Value2D(wx / 9f, wy / 9f, 51) * 4);
                int distance = TileSize;
                Surface adjacent = surface;
                if (n != surface && py < distance) { adjacent = n; distance = py; }
                if (s != surface && TileSize - 1 - py < distance) { adjacent = s; distance = TileSize - 1 - py; }
                if (w != surface && px < distance) { adjacent = w; distance = px; }
                if (e != surface && TileSize - 1 - px < distance) { adjacent = e; distance = TileSize - 1 - px; }
                if (distance < edge)
                {
                    if (surface == Surface.Water && adjacent != Surface.Water)
                        color = distance == 0 ? Foam : Blend(Lake, Foam, 0.22f);
                    else if (surface != Surface.Water && adjacent == Surface.Water)
                        color = surface == Surface.Sand ? WetSand : Blend(color, Loam, 0.52f);
                    else if (surface == Surface.Grass && adjacent is Surface.Dirt or Surface.Sand)
                        color = Blend(GroundPixel(adjacent, wx, wy, height), color, distance / (float)edge);
                    else if (surface is Surface.Stone or Surface.IronOre && adjacent == Surface.Grass)
                        color = Blend(color, Moss, (1 - distance / (float)edge) * 0.3f);
                }
                float shade = ambient;
                int foot = py - cliff;
                if (cliff > 0 && foot < 5) shade *= 0.74f + foot * 0.05f;
                if (west > height && px < 3) shade *= 0.72f + px * 0.08f;
                if (east > height && px >= TileSize - 3) shade *= 0.77f + (TileSize - 1 - px) * 0.06f;
                if (surface != Surface.Water)
                {
                    if (south < height && py >= TileSize - 2) shade *= py == TileSize - 1 ? 1.13f : 1.05f;
                    if (north < height && py == 0) shade *= 1.1f;
                }
                color = Tint(color, shade);
            }
            int index = ((oy + py) * stride + ox + px) * 4;
            pixels[index] = color.R; pixels[index + 1] = color.G; pixels[index + 2] = color.B; pixels[index + 3] = 255;
        }
    }

    private static Rgb GroundPixel(Surface surface, int x, int y, int elevation)
    {
        float patch = Noise.Value2D(x / 42f, y / 42f, 47);
        float speck = Noise.Hash01(x / 2, y / 2, 53, 0);
        Rgb color;
        switch (surface)
        {
            case Surface.Grass:
                color = patch < 0.5f ? Blend(Moss, Meadow, 0.35f + patch * 1.3f) : Blend(Meadow, SunlitGrass, (patch - 0.5f) * 1.3f);
                int tuft = (int)(Noise.Hash01(x / 11, y / 9, 55, 0) * 100);
                if (tuft > 85 && x % 11 is 4 or 6 && y % 9 is 3 or 4) color = Tint(color, 1.14f);
                if (tuft == 99 && x % 11 == 5 && y % 9 == 2) color = new Rgb(222, 204, 152);
                break;
            case Surface.Dirt:
                color = Blend(Loam, Clay, 0.45f + patch * 0.4f);
                if (speck > 0.92f && y % 7 < 2) color = Tint(color, 1.1f);
                break;
            case Surface.Sand:
                color = Blend(WetSand, Sand, 0.78f + patch * 0.18f);
                if ((y + (int)(Noise.Value2D(x / 16f, y / 24f, 57) * 5)) % 13 == 0) color = Tint(color, 0.97f);
                break;
            case Surface.Stone:
            case Surface.IronOre:
                return RockPixel(x, y, surface == Surface.IronOre);
            default:
                color = Blend(Depth, Lake, elevation <= 1 ? 0.15f : 0.82f);
                int ripple = (y + (int)(Noise.Value2D(x / 25f, y / 17f, 59) * 5)) % 12;
                if (ripple == 0 && Noise.Hash01(x / 9, y / 12, 61, 0) > 0.72f) color = Blend(color, Foam, 0.18f);
                return color;
        }
        return Tint(color, 0.985f + speck * 0.03f);
    }

    private static Rgb RockPixel(int x, int y, bool ore)
    {
        float patch = Noise.Value2D(x / 16f, y / 13f, 63);
        Rgb color = Blend(RockShade, Rock, 0.55f + patch * 0.4f);
        int seam = (y + (x / 19) * 3) % 17;
        if (seam == 0 && Noise.Hash01(x / 15, y / 17, 65, 0) > 0.4f) color = Tint(color, 0.88f);
        if (seam == 1 && x % 15 > 5) color = Tint(color, 1.05f);
        if (!ore) return color;
        float deposit = Noise.Value2D(x / 10f, y / 8f, 67);
        if (deposit > 0.61f)
            color = Blend(Rust, OreLight, Math.Clamp((deposit - 0.61f) * 3.5f, 0, 0.7f));
        else if (deposit > 0.58f)
            color = Blend(RockShade, Rust, 0.45f);
        return color;
    }

    private static int Elevation(LocalMap map, int x, int y, int fallback) => map.InBounds(x, y) ? map.GetElevation(x, y) : fallback;
    private static Surface Neighbor(LocalMap map, int x, int y, Surface fallback) => map.InBounds(x, y) ? Structural(map.GetSurface(x, y)) : fallback;

    /// <summary>Pour les bords et les falaises, la rivière compte comme de l'eau.</summary>
    private static Surface Structural(Surface surface) => surface == Surface.River ? Surface.Water : surface;

    /// <summary>Eau peu profonde, claire, avec de petites rides dans le sens du courant (en attendant les vraies illustrations).</summary>
    private static Rgb RiverPixel(int x, int y)
    {
        Rgb color = Blend(Lake, Foam, 0.18f);
        int ripple = (x + (int)(Noise.Value2D(x / 17f, y / 25f, 69) * 6)) % 10;
        if (ripple == 0 && Noise.Hash01(x / 12, y / 9, 71, 0) > 0.7f)
            color = Blend(color, Foam, 0.3f);
        return color;
    }
    private static Rgb Blend(Rgb a, Rgb b, float t) => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Rgb Tint(Rgb c, float t) => new(Scale(c.R, t), Scale(c.G, t), Scale(c.B, t));
    private static byte Scale(byte value, float t) => (byte)Math.Clamp(value * t, 0, 255);
}
