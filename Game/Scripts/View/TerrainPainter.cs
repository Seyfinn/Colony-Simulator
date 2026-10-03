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
    private readonly record struct GrassPalette(Rgb Shade, Rgb Main, Rgb Light);
    private static GrassPalette Palette(WoodlandBiome biome) => biome switch
    {
        WoodlandBiome.Dryland => new(new(137, 137, 83), new(170, 164, 104), new(193, 181, 119)),
        WoodlandBiome.CoolForest => new(new(73, 112, 91), new(97, 134, 103), new(123, 153, 117)),
        WoodlandBiome.Highland => new(new(112, 134, 119), new(138, 154, 132), new(165, 172, 148)),
        WoodlandBiome.WetBank => new(new(69, 119, 95), new(94, 146, 108), new(124, 165, 123)),
        _ => new(Moss, Meadow, SunlitGrass),
    };
    private static GrassPalette Blend(GrassPalette a, GrassPalette b, float t) =>
        new(Blend(a.Shade, b.Shade, t), Blend(a.Main, b.Main, t), Blend(a.Light, b.Light, t));

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
        bool canal = map.IsCanal(x, y), wet = canal && map.IsCanalWet(x, y);
        bool river = !canal && map.GetSurface(x, y) == Surface.River;
        bool flooded = map.IsFlooded(x, y);
        Surface surface = VisualSurface(map, x, y);
        WoodlandBiome biome = BiomeVisuals.At(map, x, y);
        GrassPalette palette = Palette(biome);
        GrassPalette pn = Palette(BiomeVisuals.At(map, x, y - 1)), ps = Palette(BiomeVisuals.At(map, x, y + 1));
        GrassPalette pw = Palette(BiomeVisuals.At(map, x - 1, y)), pe = Palette(BiomeVisuals.At(map, x + 1, y));
        int connections = canal ? WaterGeometry.Connections(map, x, y) : 0;
        WaterGeometry.Stream[] streams = !canal ? WaterGeometry.Streams(map, x, y) : [];
        int north = Elevation(map, x, y - 1, height), south = Elevation(map, x, y + 1, height);
        int west = Elevation(map, x - 1, y, height), east = Elevation(map, x + 1, y, height);
        Surface n = Neighbor(map, x, y - 1, surface), s = Neighbor(map, x, y + 1, surface);
        Surface w = Neighbor(map, x - 1, y, surface), e = Neighbor(map, x + 1, y, surface);
        int cliff = surface != Surface.Water && !river ? Math.Min(Math.Max(0, north - height), 3) * StratumHeight : 0;
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
                // Lisières fondues sur six pixels, même lorsque les milieux voisins ont des teintes très différentes.
                GrassPalette grass = palette;
                if (py < 6) grass = Blend(grass, pn, (6 - py) / 12f);
                else if (py > 25) grass = Blend(grass, ps, (py - 25) / 12f);
                if (px < 6) grass = Blend(grass, pw, (6 - px) / 12f);
                else if (px > 25) grass = Blend(grass, pe, (px - 25) / 12f);
                color = GroundPixel(surface, wx, wy, height, biome, grass);
                if (flooded) color = Blend(color, Depth, 0.22f);
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
                        color = Blend(color, Foam, 0.12f);
                    else if (surface != Surface.Water && adjacent == Surface.Water)
                        color = surface == Surface.Sand ? WetSand : Blend(color, Loam, 0.52f);
                    else if (surface == Surface.Grass && adjacent is Surface.Dirt or Surface.Sand)
                        color = Blend(GroundPixel(adjacent, wx, wy, height, biome, grass), color, distance / (float)edge);
                    else if (surface is Surface.Stone or Surface.IronOre && adjacent == Surface.Grass)
                        color = Blend(color, Moss, (1 - distance / (float)edge) * 0.3f);
                }
                if (surface == Surface.Water)
                {
                    float shore = distance;
                    const float radius = 9;
                    // Les coins de la retenue sont adoucis à l'intérieur des cases réellement noyées.
                    if (n != Surface.Water && w != Surface.Water && px < radius && py < radius)
                        shore = Math.Min(shore, radius - MathF.Sqrt((px - radius) * (px - radius) + (py - radius) * (py - radius)));
                    if (n != Surface.Water && e != Surface.Water && px > 31 - radius && py < radius)
                        shore = Math.Min(shore, radius - MathF.Sqrt((px - 31 + radius) * (px - 31 + radius) + (py - radius) * (py - radius)));
                    if (s != Surface.Water && w != Surface.Water && px < radius && py > 31 - radius)
                        shore = Math.Min(shore, radius - MathF.Sqrt((px - radius) * (px - radius) + (py - 31 + radius) * (py - 31 + radius)));
                    if (s != Surface.Water && e != Surface.Water && px > 31 - radius && py > 31 - radius)
                        shore = Math.Min(shore, radius - MathF.Sqrt((px - 31 + radius) * (px - 31 + radius) + (py - 31 + radius) * (py - 31 + radius)));
                    float margin = 1.5f + Noise.Value2D(wx / 13f, wy / 13f, 193) * 1.5f;
                    bool mouth = streams.Length > 0 && WaterGeometry.Nearest(streams, px, py).Distance < 10;
                    if (!mouth && shore < margin)
                    {
                        Rgb soil = GroundPixel(Underlying(map, x, y), wx, wy, height, biome, grass);
                        color = Blend(soil, Underlying(map, x, y) == Surface.Sand ? WetSand : Loam, 0.25f);
                    }
                    else if (!mouth && shore < margin + 2.5f) color = Blend(color, Foam, 0.22f);
                }
                bool streamWater = false;
                if (streams.Length > 0 && surface != Surface.Water)
                {
                    var nearest = WaterGeometry.Nearest(streams, px, py);
                    float width = 10.5f + (Noise.Value2D(wx / 15f, wy / 15f, 191) - 0.5f) * 3;
                    if (nearest.Distance < width)
                    {
                        var current = nearest.Stream;
                        int flowX = Math.Sign(current.Dx), flowY = Math.Sign(current.Dy);
                        if (flowX == 0 && flowY == 0) flowY = 1;
                        color = RiverPixel(wx, wy, flowX, flowY, current.Flow);
                        if (nearest.Distance > width - 2) color = Blend(color, Foam, 0.12f);
                        streamWater = true;
                    }
                    else if (nearest.Distance < width + 3)
                        color = Blend(color, Blend(Loam, Moss, 0.35f), (width + 3 - nearest.Distance) / 4f);
                }
                if (canal) color = CanalPixel(color, wx, wy, px, py, connections, wet);
                float shade = surface == Surface.Water || streamWater ? 1 : ambient;
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

    private static Rgb GroundPixel(Surface surface, int x, int y, int elevation, WoodlandBiome biome, GrassPalette grass)
    {
        float patch = Noise.Value2D(x / 42f, y / 42f, 47);
        float speck = Noise.Hash01(x / 2, y / 2, 53, 0);
        Rgb color;
        switch (surface)
        {
            case Surface.Grass:
                color = patch < 0.5f ? Blend(grass.Shade, grass.Main, 0.35f + patch * 1.3f) : Blend(grass.Main, grass.Light, (patch - 0.5f) * 1.3f);
                int tuft = (int)(Noise.Hash01(x / 11, y / 9, 55, 0) * 100);
                if (tuft > 85 && x % 11 is 4 or 6 && y % 9 is 3 or 4) color = Tint(color, 1.14f);
                if (tuft == 99 && x % 11 == 5 && y % 9 == 2) color = biome == WoodlandBiome.CoolForest ? new(166, 139, 100) : new(234, 214, 164);
                if (biome == WoodlandBiome.Dryland && patch < 0.28f) color = Blend(color, Clay, (0.28f - patch) * 1.2f);
                if (biome == WoodlandBiome.CoolForest && tuft > 82 && x % 11 is 3 or 4 && y % 9 == 6) color = new(148, 116, 73);
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
                color = RockPixel(x, y, surface == Surface.IronOre);
                if (biome == WoodlandBiome.Highland)
                {
                    if (surface != Surface.IronOre) color = Blend(color, new Rgb(132, 157, 162), 0.25f);
                    if (patch > 0.65f && speck > 0.82f && surface != Surface.IronOre) color = Blend(color, new Rgb(185, 190, 142), 0.65f);
                }
                return color;
            default:
                color = Blend(Depth, Lake, elevation <= 1 ? 0.25f : 0.8f);
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
    private static Surface Neighbor(LocalMap map, int x, int y, Surface fallback) => map.InBounds(x, y) ? VisualSurface(map, x, y) : fallback;
    private static Surface VisualSurface(LocalMap map, int x, int y) => map.IsCanal(x, y) || (map.IsRiver(x, y) && !map.IsFlooded(x, y))
        ? Underlying(map, x, y) : Structural(map.GetSurface(x, y));
    private static Surface Underlying(LocalMap map, int x, int y) => map.GetSoil(x, y) switch
    {
        SoilType.Dirt => Surface.Dirt, SoilType.Sand => Surface.Sand, _ => Surface.Grass,
    };

    /// <summary>Pour les bords et les falaises, la rivière compte comme de l'eau.</summary>
    private static Surface Structural(Surface surface) => surface == Surface.River ? Surface.Water : surface;

    private static Rgb CanalPixel(Rgb ground, int x, int y, int px, int py, int connections, bool wet)
    {
        int distance = WaterGeometry.Distance(px, py, connections);
        if (distance > 8) return ground;
        if (distance >= 6)
        {
            Rgb earth = Blend(Loam, Clay, 0.45f);
            if (distance == 8) return Blend(ground, earth, 0.6f);
            if (distance == 7) return Tint(earth, 1.12f);
            return Tint(earth, 0.7f);
        }
        if (!wet) return Tint(Blend(Loam, Clay, 0.18f + 0.15f * Noise.Value2D(x / 14f, y / 14f, 73)), 0.76f);
        Rgb water = Blend(Depth, Lake, 0.6f + distance * 0.065f);
        if (distance == 5) water = Blend(water, Foam, 0.23f);
        if (py % 11 == 0 && px % 7 < 4) water = Blend(water, Foam, 0.18f);
        return water;
    }

    /// <summary>Rides orientées par le vrai courant ; le débit réduit laisse une eau plus calme.</summary>
    private static Rgb RiverPixel(int x, int y, int flowX, int flowY, float flow)
    {
        float depth = Noise.Value2D(x / 35f, y / 37f, 69);
        Rgb color = Blend(new Rgb(79, 141, 145), new Rgb(123, 177, 161), 0.3f + depth * 0.6f);
        int along = x * flowX + y * flowY, across = y * flowX - x * flowY;
        int ripple = Math.Abs(along + (int)(Noise.Value2D(x / 17f, y / 25f, 69) * 6)) % 13;
        if (ripple == 0 && Noise.Hash01(across / 7, along / 13, 71, 0) > 0.68f)
            color = Blend(color, Foam, 0.14f + flow * 0.24f);
        if (depth > 0.8f && Noise.Hash01(x / 3, y / 3, 73, 0) > 0.93f) color = Blend(color, new Rgb(166, 181, 141), 0.25f);
        return color;
    }
    private static Rgb Blend(Rgb a, Rgb b, float t) => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Rgb Tint(Rgb c, float t) => new(Scale(c.R, t), Scale(c.G, t), Scale(c.B, t));
    private static byte Scale(byte value, float t) => (byte)Math.Clamp(value * t, 0, 255);
}
