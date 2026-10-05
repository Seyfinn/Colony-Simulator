using System;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.View;

/// <summary>Terrain pastoral en 32 pixels : sols nuancés, rives, roche stratifiée et veines de fer.</summary>
public static partial class TerrainPainter
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
    private readonly record struct SoilPalette(Rgb Dark, Rgb Light, Rgb Sand, Rgb WetSand);
    private static SoilPalette SoilFor(Biome biome) => biome switch
    {
        Biome.Desert => new(new(178, 147, 98), new(216, 184, 123), new(231, 209, 153), new(171, 152, 112)),
        Biome.Steppe => new(new(154, 131, 87), new(191, 167, 112), Sand, WetSand),
        Biome.Savanna => new(new(163, 121, 72), new(199, 157, 91), new(224, 194, 133), new(163, 142, 99)),
        Biome.Tundra => new(new(125, 132, 117), new(161, 163, 143), new(193, 193, 166), new(142, 151, 137)),
        Biome.IceSheet => new(new(188, 205, 207), new(228, 237, 224), new(230, 237, 226), new(164, 191, 192)),
        Biome.TropicalForest => new(new(102, 86, 52), new(137, 110, 63), Sand, WetSand),
        Biome.Swamp => new(new(86, 93, 57), new(119, 117, 74), new(168, 165, 113), new(114, 130, 93)),
        _ => new(Loam, Clay, Sand, WetSand),
    };

    private static GrassPalette Palette(Biome region, WoodlandBiome local)
    {
        GrassPalette climate = region switch
        {
            Biome.Tundra => new(new(113, 132, 119), new(144, 157, 137), new(170, 180, 155)),
            Biome.IceSheet => new(new(182, 203, 204), new(217, 229, 223), new(235, 242, 231)),
            Biome.BorealForest => new(new(62, 102, 86), new(83, 122, 99), new(113, 145, 117)),
            Biome.Grassland => new(new(110, 139, 82), new(140, 159, 97), new(166, 178, 117)),
            Biome.Steppe => new(new(139, 138, 81), new(174, 164, 97), new(200, 186, 120)),
            Biome.Desert => new(new(185, 162, 104), new(211, 186, 129), new(230, 208, 153)),
            Biome.Savanna => new(new(151, 130, 65), new(186, 159, 82), new(212, 188, 111)),
            Biome.TropicalForest => new(new(51, 104, 63), new(75, 132, 73), new(113, 158, 91)),
            Biome.Swamp => new(new(66, 98, 63), new(96, 124, 74), new(133, 146, 92)),
            _ => Palette(local),
        };
        // Les accents de berge ne doivent pas ramener un désert ou une toundra à une prairie tempérée.
        return local == WoodlandBiome.WetBank && region is Biome.Desert or Biome.Steppe or Biome.Savanna
            ? Blend(climate, Palette(local), 0.16f) : climate;
    }
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

    /// <summary>
    /// Les bruits du terrain, lus pixel par pixel : chacun garde sa dernière cellule, que les pixels voisins partagent presque
    /// toujours. Le dessin est exactement celui qu'on obtient en appelant <see cref="Noise"/> à chaque pixel, en bien moins de calculs.
    /// </summary>
    private struct PixelNoise()
    {
        public Noise.Value2DCursor Patch = new(47), Edge = new(51), Stripe = new(57), Ripple = new(59), RockPatch = new(63),
            Deposit = new(67), Depth = new(69), Current = new(69), Ditch = new(73), Stream = new(191), Shore = new(193), Cracks = new(197);
        public CellHash Speck = new(53), Tuft = new(55), RippleFoam = new(61), Seam = new(65), CurrentFoam = new(71), Pebble = new(73);
    }

    /// <summary><see cref="Noise.Hash01"/> d'une cellule, gardé tant que les pixels suivants y tombent.</summary>
    private struct CellHash(int z)
    {
        private int _x, _y;
        private bool _ready;
        private float _value;

        public float At(int x, int y)
        {
            if (!_ready || x != _x || y != _y)
            {
                (_x, _y, _ready) = (x, y, true);
                _value = Noise.Hash01(x, y, z, 0);
            }
            return _value;
        }
    }

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

    /// <summary>Peint la case (x, y) dans <paramref name="pixels"/> (de largeur <paramref name="stride"/> pixels), à partir du pixel (ox, oy).</summary>
    public static void PaintTile(LocalMap map, int x, int y, byte[] pixels, int stride, int ox, int oy)
    {
        int height = map.GetElevation(x, y);
        bool canal = map.IsCanal(x, y), wet = canal && map.IsCanalWet(x, y);
        bool flooded = map.IsFlooded(x, y);
        // Un fleuve large se peint comme une nappe d'eau courante : les tuiles de rivière sont faites pour un lit d'une case.
        bool wide = !canal && !flooded && map.IsWideRiver(x, y);
        bool river = !canal && !wide && map.GetSurface(x, y) == Surface.River;
        (int wideFlowX, int wideFlowY, float wideFlow) = (0, 1, 1f);
        if (wide)
        {
            if (map.RiverDownstream(x, y) is { } downstream)
                (wideFlowX, wideFlowY) = (Math.Sign(downstream.X - x), Math.Sign(downstream.Y - y));
            wideFlow = map.GetFlow(x, y);
        }
        Surface surface = VisualSurface(map, x, y);
        WoodlandBiome biome = BiomeVisuals.At(map, x, y);
        Biome region = map.Biome;
        SoilPalette soilPalette = SoilFor(region);
        GrassPalette palette = Palette(region, biome);
        GrassPalette pn = Palette(region, BiomeVisuals.At(map, x, y - 1)), ps = Palette(region, BiomeVisuals.At(map, x, y + 1));
        GrassPalette pw = Palette(region, BiomeVisuals.At(map, x - 1, y)), pe = Palette(region, BiomeVisuals.At(map, x + 1, y));
        int connections = canal ? WaterGeometry.Connections(map, x, y) : 0;
        byte[]? canalTile = canal ? RiverTiles.Get($"canal_{(wet ? "wet" : "dry")}_{connections}") : null;
        int riverMask = RiverTiles.Connections(map, x, y);
        int riverCorners = RiverTiles.Corners(map, x, y);
        byte[]? riverShape = wide ? null : RiverTiles.River(riverMask, riverCorners, river);
        byte[]? riverTile = !canal && surface != Surface.Water ? riverShape : null;
        byte[]? lakeTile = surface == Surface.Water && !wide
            ? RiverTiles.Shore(RiverTiles.LakeEdges(map, x, y, riverMask), riverShape) : null;
        // Avec le jeu complet de PNG, les rives restent dans la case ; sans lui, garder l'ancien tracé.
        bool pngRivers = RiverTiles.Complete;
        WaterGeometry.Stream[] streams = !canal && ((riverTile is null && (!pngRivers || riverMask != 0 || riverCorners != 0)) || surface == Surface.Water)
            ? WaterGeometry.Streams(map, x, y) : [];
        int north = Elevation(map, x, y - 1, height), south = Elevation(map, x, y + 1, height);
        int west = Elevation(map, x - 1, y, height), east = Elevation(map, x + 1, y, height);
        Surface n = Neighbor(map, x, y - 1, surface), s = Neighbor(map, x, y + 1, surface);
        Surface w = Neighbor(map, x - 1, y, surface), e = Neighbor(map, x + 1, y, surface);
        // À l'embouchure, fondre le courant dans l'eau profonde plutôt que dessiner une séparation rectiligne.
        bool DeepWater(int ax, int ay) => wide && map.InBounds(ax, ay) && map.IsWater(ax, ay) && !map.IsWideRiver(ax, ay);
        bool lakeNorth = DeepWater(x, y - 1), lakeSouth = DeepWater(x, y + 1);
        bool lakeWest = DeepWater(x - 1, y), lakeEast = DeepWater(x + 1, y);
        int cliff = surface != Surface.Water && !river ? Math.Min(Math.Max(0, north - height), 3) * StratumHeight : 0;
        float ambient = 0.86f + height * 0.016f;
        var noise = new PixelNoise();
        for (int py = 0; py < TileSize; py++)
        {
        // Lisières fondues sur six pixels, même lorsque les milieux voisins ont des teintes très différentes :
        // d'abord avec la case au nord ou au sud, la même pour toute la rangée.
        GrassPalette rowGrass = palette;
        if (py < 6) rowGrass = Blend(rowGrass, pn, (6 - py) / 12f);
        else if (py > 25) rowGrass = Blend(rowGrass, ps, (py - 25) / 12f);
        for (int px = 0; px < TileSize; px++)
        {
            int wx = x * TileSize + px, wy = y * TileSize + py;
            Rgb color;
            if (py < cliff)
            {
                int band = py / StratumHeight;
                int layer = north - band - 1;
                Material material = map.MaterialAt(x, y - 1, layer);
                color = material == Material.Soil ? Blend(soilPalette.Dark, soilPalette.Light, 0.38f) : RockPixel(ref noise, wx, wy, material == Material.IronOre);
                int stratum = py % StratumHeight;
                // Chaque strate possède des fractures décalées et une arête propre.
                int joint = (wx + band * 9) % 23;
                float shade = stratum == 0 ? 0.75f : stratum == 7 ? 0.52f : 0.62f + stratum * 0.016f;
                if (joint is 0 or 1 && stratum is > 2 and < 7) shade *= 0.8f;
                color = Tint(color, shade);
            }
            else
            {
                // Puis avec la case à l'ouest ou à l'est.
                GrassPalette grass = rowGrass;
                if (px < 6) grass = Blend(grass, pw, (6 - px) / 12f);
                else if (px > 25) grass = Blend(grass, pe, (px - 25) / 12f);
                color = wide ? RiverPixel(ref noise, wx, wy, wideFlowX, wideFlowY, wideFlow)
                    : GroundPixel(ref noise, surface, wx, wy, height, biome, grass, region, soilPalette);
                if (wide)
                {
                    color = region switch
                    {
                        Biome.Swamp => Blend(color, new Rgb(65, 110, 87), 0.32f),
                        Biome.TropicalForest => Blend(color, new Rgb(63, 123, 105), 0.19f),
                        Biome.BorealForest or Biome.Tundra => Blend(color, new Rgb(76, 128, 147), 0.16f),
                        _ => color,
                    };
                    int estuary = TileSize;
                    if (lakeNorth) estuary = Math.Min(estuary, py);
                    if (lakeSouth) estuary = Math.Min(estuary, 31 - py);
                    if (lakeWest) estuary = Math.Min(estuary, px);
                    if (lakeEast) estuary = Math.Min(estuary, 31 - px);
                    if (estuary < 12)
                        color = Blend(color, GroundPixel(ref noise, Surface.Water, wx, wy, height, biome, grass, region, soilPalette),
                            1 - estuary / 12f);
                }
                if (flooded) color = Blend(color, Depth, 0.22f);
                int edge = 2 + (int)(noise.Edge.At(wx / 9f, wy / 9f) * 4);
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
                        color = surface == Surface.Sand ? soilPalette.WetSand : Blend(color, soilPalette.Dark, 0.52f);
                    else if (surface == Surface.Grass && adjacent is Surface.Dirt or Surface.Sand)
                        color = Blend(GroundPixel(ref noise, adjacent, wx, wy, height, biome, grass, region, soilPalette), color, distance / (float)edge);
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
                    float margin = 1.5f + noise.Shore.At(wx / 13f, wy / 13f) * 1.5f;
                    bool mouth = streams.Length > 0 && WaterGeometry.Nearest(streams, px, py).Distance < 10;
                    if (lakeTile is null && !mouth && shore < margin)
                    {
                        Rgb soil = GroundPixel(ref noise, Underlying(map, x, y), wx, wy, height, biome, grass, region, soilPalette);
                        color = Blend(soil, Underlying(map, x, y) == Surface.Sand ? soilPalette.WetSand : soilPalette.Dark, 0.25f);
                    }
                    else if (lakeTile is null && !mouth && shore < margin + 2.5f) color = Blend(color, Foam, 0.22f);
                }
                bool streamWater = false;
                if (streams.Length > 0 && surface != Surface.Water)
                {
                    var nearest = WaterGeometry.Nearest(streams, px, py);
                    float width = 10.5f + (noise.Stream.At(wx / 15f, wy / 15f) - 0.5f) * 3;
                    if (nearest.Distance < width)
                    {
                        var current = nearest.Stream;
                        int flowX = Math.Sign(current.Dx), flowY = Math.Sign(current.Dy);
                        if (flowX == 0 && flowY == 0) flowY = 1;
                        color = RiverPixel(ref noise, wx, wy, flowX, flowY, current.Flow);
                        if (nearest.Distance > width - 2) color = Blend(color, Foam, 0.12f);
                        streamWater = true;
                    }
                    else if (nearest.Distance < width + 3)
                        color = Blend(color, Blend(Loam, Moss, 0.35f), (width + 3 - nearest.Distance) / 4f);
                }
                if (canal && canalTile is null) color = CanalPixel(ref noise, color, wx, wy, px, py, connections, wet);
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
        if (riverTile is not null)
        {
            RiverTiles.Blend(riverTile, pixels, stride, ox, oy);
            if (RiverTiles.Accent(map, x, y) is { } accent)
                RiverTiles.Blend(accent, pixels, stride, ox, oy);
        }
        if (lakeTile is not null) RiverTiles.Blend(lakeTile, pixels, stride, ox, oy);
        if (canalTile is not null) RiverTiles.Blend(canalTile, pixels, stride, ox, oy);
        PaintRiverbank(map, x, y, pixels, stride, ox, oy, wide, riverMask, riverCorners);
        PaintRoad(map,x,y,pixels,stride,ox,oy);
    }

    private static Rgb GroundPixel(ref PixelNoise noise, Surface surface, int x, int y, int elevation, WoodlandBiome biome, GrassPalette grass,
        Biome region, SoilPalette soil)
    {
        float patch = noise.Patch.At(x / 42f, y / 42f);
        float speck = noise.Speck.At(x / 2, y / 2);
        Rgb color;
        switch (surface)
        {
            case Surface.Grass:
                color = patch < 0.5f ? Blend(grass.Shade, grass.Main, 0.35f + patch * 1.3f) : Blend(grass.Main, grass.Light, (patch - 0.5f) * 1.3f);
                int tuft = (int)(noise.Tuft.At(x / 11, y / 9) * 100);
                int sparseTuft = region is Biome.Tundra or Biome.Desert or Biome.IceSheet ? 97 : 85;
                if (tuft > sparseTuft && x % 11 is 4 or 6 && y % 9 is 3 or 4) color = Tint(color, 1.14f);
                if (region != Biome.IceSheet && tuft == 99 && x % 11 == 5 && y % 9 == 2) color = biome == WoodlandBiome.CoolForest ? new(166, 139, 100) : new(234, 214, 164);
                if (biome == WoodlandBiome.Dryland && patch < 0.28f) color = Blend(color, soil.Light, (0.28f - patch) * 1.2f);
                if (biome == WoodlandBiome.CoolForest && tuft > 82 && x % 11 is 3 or 4 && y % 9 == 6) color = new(148, 116, 73);
                // Reflets de flaques dans le sol humide : décor seulement, sans ajouter une case d'eau.
                if (region == Biome.Swamp && patch > 0.74f)
                {
                    color = Blend(color, new Rgb(85, 112, 91), Math.Min(1, (patch - 0.74f) * 10));
                    if (y % 17 == 0 && x % 13 < 6) color = Blend(color, new Rgb(151, 164, 121), 0.35f);
                }
                break;
            case Surface.Dirt:
                color = Blend(soil.Dark, soil.Light, 0.45f + patch * 0.4f);
                if (speck > 0.92f && y % 7 < 2) color = Tint(color, 1.1f);
                if (region == Biome.Desert)
                {
                    int warp = (int)(noise.Cracks.At(x / 21f, y / 19f) * 8);
                    int cx = x + warp, cy = y + warp / 2;
                    bool crack = (cx + cy / 29 * 13 + cy % 29 / 4) % 43 == 0 && cy % 29 < 22
                        || (cy + cx / 43 * 7) % 29 == 0 && cx % 43 < 32;
                    if (crack && patch < 0.7f) color = Tint(color, 0.83f);
                }
                break;
            case Surface.Sand:
                color = Blend(soil.WetSand, soil.Sand, 0.78f + patch * 0.18f);
                if ((y + (int)(noise.Stripe.At(x / 16f, y / 24f) * 5)) % 13 == 0) color = Tint(color, 0.97f);
                break;
            case Surface.Stone:
            case Surface.IronOre:
                color = RockPixel(ref noise, x, y, surface == Surface.IronOre);
                if (biome == WoodlandBiome.Highland)
                {
                    if (surface != Surface.IronOre) color = Blend(color, new Rgb(132, 157, 162), 0.25f);
                    if (patch > 0.65f && speck > 0.82f && surface != Surface.IronOre) color = Blend(color, new Rgb(185, 190, 142), 0.65f);
                }
                return color;
            default:
                color = region == Biome.Swamp
                    ? Blend(new Rgb(55, 81, 69), new Rgb(102, 129, 94), 0.25f + patch * 0.55f)
                    : Blend(Depth, Lake, elevation <= 1 ? 0.25f : 0.8f);
                int ripple = (y + (int)(noise.Ripple.At(x / 25f, y / 17f) * 5)) % 12;
                if (ripple == 0 && noise.RippleFoam.At(x / 9, y / 12) > 0.72f) color = Blend(color, Foam, region == Biome.Swamp ? 0.08f : 0.18f);
                return color;
        }
        return Tint(color, 0.985f + speck * 0.03f);
    }

    private static Rgb RockPixel(ref PixelNoise noise, int x, int y, bool ore)
    {
        float patch = noise.RockPatch.At(x / 16f, y / 13f);
        Rgb color = Blend(RockShade, Rock, 0.55f + patch * 0.4f);
        int seam = (y + (x / 19) * 3) % 17;
        if (seam == 0 && noise.Seam.At(x / 15, y / 17) > 0.4f) color = Tint(color, 0.88f);
        if (seam == 1 && x % 15 > 5) color = Tint(color, 1.05f);
        if (!ore) return color;
        float deposit = noise.Deposit.At(x / 10f, y / 8f);
        if (deposit > 0.61f)
            color = Blend(Rust, OreLight, Math.Clamp((deposit - 0.61f) * 3.5f, 0, 0.7f));
        else if (deposit > 0.58f)
            color = Blend(RockShade, Rust, 0.45f);
        return color;
    }

    private static int Elevation(LocalMap map, int x, int y, int fallback) => map.InBounds(x, y) ? map.GetElevation(x, y) : fallback;
    private static Surface Neighbor(LocalMap map, int x, int y, Surface fallback) => map.InBounds(x, y) ? VisualSurface(map, x, y) : fallback;
    private static Surface VisualSurface(LocalMap map, int x, int y) =>
        !map.IsCanal(x, y) && !map.IsFlooded(x, y) && map.IsWideRiver(x, y) ? Surface.Water
        : map.IsCanal(x, y) || (map.IsRiver(x, y) && !map.IsFlooded(x, y)) ? Underlying(map, x, y)
        : Structural(map.GetSurface(x, y));
    private static Surface Underlying(LocalMap map, int x, int y) => map.GetSoil(x, y) switch
    {
        SoilType.Dirt => Surface.Dirt, SoilType.Sand => Surface.Sand, _ => Surface.Grass,
    };

    /// <summary>Pour les bords et les falaises, la rivière compte comme de l'eau.</summary>
    private static Surface Structural(Surface surface) => surface == Surface.River ? Surface.Water : surface;

    private static Rgb CanalPixel(ref PixelNoise noise, Rgb ground, int x, int y, int px, int py, int connections, bool wet)
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
        if (!wet) return Tint(Blend(Loam, Clay, 0.18f + 0.15f * noise.Ditch.At(x / 14f, y / 14f)), 0.76f);
        Rgb water = Blend(Depth, Lake, 0.6f + distance * 0.065f);
        if (distance == 5) water = Blend(water, Foam, 0.23f);
        if (py % 11 == 0 && px % 7 < 4) water = Blend(water, Foam, 0.18f);
        return water;
    }

    /// <summary>Rides orientées par le vrai courant ; le débit réduit laisse une eau plus calme.</summary>
    private static Rgb RiverPixel(ref PixelNoise noise, int x, int y, int flowX, int flowY, float flow)
    {
        float depth = noise.Depth.At(x / 35f, y / 37f);
        Rgb color = Blend(new Rgb(79, 141, 145), new Rgb(123, 177, 161), 0.3f + depth * 0.6f);
        int along = x * flowX + y * flowY, across = y * flowX - x * flowY;
        int ripple = Math.Abs(along + (int)(noise.Current.At(x / 17f, y / 25f) * 6)) % 13;
        if (ripple == 0 && noise.CurrentFoam.At(across / 7, along / 13) > 0.68f)
            color = Blend(color, Foam, 0.14f + flow * 0.24f);
        if (depth > 0.8f && noise.Pebble.At(x / 3, y / 3) > 0.93f) color = Blend(color, new Rgb(166, 181, 141), 0.25f);
        return color;
    }
    private static Rgb Blend(Rgb a, Rgb b, float t) => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Rgb Tint(Rgb c, float t) => new(Scale(c.R, t), Scale(c.G, t), Scale(c.B, t));
    private static byte Scale(byte value, float t) => (byte)Math.Clamp(value * t, 0, 255);
}
