using System;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>
/// Dessine le terrain pixel par pixel, en attendant de vrais graphismes en pixel art.
///
/// Effet 3/4 : quand la case au nord est plus haute, le haut de la case montre la face de la falaise,
/// avec une bande par couche de roche. Les filons de fer apparaissent donc aussi dans les falaises.
/// </summary>
public static class TerrainPainter
{
    public const int TileSize = 16;
    private const int PixelsPerLayer = 5;
    private const int MaxFaceLayers = 3;

    private readonly record struct Rgb(byte R, byte G, byte B);

    private static readonly Rgb GrassColor = new(88, 142, 60);
    private static readonly Rgb GrassLight = new(112, 168, 72);
    private static readonly Rgb GrassDark = new(70, 118, 50);
    private static readonly Rgb DirtColor = new(132, 98, 66);
    private static readonly Rgb SandColor = new(210, 190, 132);
    private static readonly Rgb StoneColor = new(128, 124, 118);
    private static readonly Rgb RustColor = new(166, 86, 52);
    private static readonly Rgb WaterColor = new(56, 100, 168);
    private static readonly Rgb DeepWaterColor = new(40, 76, 140);

    /// <summary>Renvoie les pixels RGBA d'un morceau de carte (un "chunk").</summary>
    public static byte[] Paint(LocalMap map, int tileX0, int tileY0, int tilesWide, int tilesHigh)
    {
        int width = tilesWide * TileSize;
        var data = new byte[width * tilesHigh * TileSize * 4];
        for (int ty = 0; ty < tilesHigh; ty++)
        for (int tx = 0; tx < tilesWide; tx++)
        {
            int x = tileX0 + tx, y = tileY0 + ty;
            if (map.InBounds(x, y))
                PaintTile(map, x, y, data, width, tx * TileSize, ty * TileSize);
        }
        return data;
    }

    private static void PaintTile(LocalMap map, int x, int y, byte[] data, int stride, int ox, int oy)
    {
        int elevation = map.GetElevation(x, y);
        Surface surface = map.GetSurface(x, y);
        int north = Neighbor(map, x, y - 1, elevation);
        int south = Neighbor(map, x, y + 1, elevation);
        int west = Neighbor(map, x - 1, y, elevation);
        int east = Neighbor(map, x + 1, y, elevation);

        // Plus c'est haut, plus c'est clair : on lit le relief d'un coup d'œil.
        float heightShade = 0.5f + elevation * 0.06f;
        int faceRows = !map.IsWater(x, y) && north > elevation
            ? Math.Min(north - elevation, MaxFaceLayers) * PixelsPerLayer
            : 0;
        // Ombre portée au pied de la falaise, sur quelques pixels.
        int shadowRows = faceRows > 0 ? 4 : 0;

        for (int py = 0; py < TileSize; py++)
        for (int px = 0; px < TileSize; px++)
        {
            int wx = x * TileSize + px, wy = y * TileSize + py;
            Rgb color;
            float shade;

            if (py < faceRows)
            {
                // Face de la falaise : la couche représentée par cette bande, vue de côté.
                int layer = north - 1 - py / PixelsPerLayer;
                color = FaceColor(map.MaterialAt(x, y - 1, layer), wx, wy);
                shade = 0.42f + 0.05f * (py % PixelsPerLayer);
                if (py % PixelsPerLayer == PixelsPerLayer - 1)
                    shade *= 0.8f;
            }
            else
            {
                color = SurfaceColor(surface, elevation, wx, wy);
                shade = heightShade;
                int belowFace = py - faceRows;
                if (belowFace < shadowRows) shade *= 0.72f + 0.07f * belowFace;
                // Murs latéraux : bande sombre de 2 pixels du côté de la case plus haute.
                if (west > elevation && px < 2) shade *= px == 0 ? 0.62f : 0.8f;
                if (east > elevation && px > TileSize - 3) shade *= px == TileSize - 1 ? 0.62f : 0.8f;
                // Rebords éclairés au sommet des falaises, au sud comme au nord (bord d'un trou).
                if (py == TileSize - 1 && south < elevation && surface != Surface.Water) shade *= 1.25f;
                if (py < 2 && north < elevation && surface != Surface.Water) shade *= py == 0 ? 1.3f : 1.12f;
            }

            int i = ((oy + py) * stride + ox + px) * 4;
            data[i] = Scale(color.R, shade);
            data[i + 1] = Scale(color.G, shade);
            data[i + 2] = Scale(color.B, shade);
            data[i + 3] = 255;
        }
    }

    private static Rgb SurfaceColor(Surface surface, int elevation, int wx, int wy)
    {
        float n = Noise.Hash01(wx, wy, 7, 0);
        Rgb color = surface switch
        {
            Surface.Grass => n > 0.92f ? GrassLight : n < 0.07f ? GrassDark : GrassColor,
            Surface.Dirt => DirtColor,
            Surface.Sand => SandColor,
            Surface.Stone => StoneColor,
            Surface.IronOre => IsRustSpeck(wx, wy) ? RustColor : StoneColor,
            _ => elevation <= 1 ? DeepWaterColor : WaterColor,
        };

        // Un léger grain sur chaque pixel donne une texture de pixel art.
        float grain = surface == Surface.Water ? (n > 0.97f ? 1.25f : 1f) : 0.94f + n * 0.12f;
        if (surface == Surface.Stone && n < 0.05f) grain = 0.75f;
        return Tint(color, grain);
    }

    private static Rgb FaceColor(Material material, int wx, int wy) => material switch
    {
        Material.IronOre => IsRustSpeck(wx, wy) ? RustColor : StoneColor,
        Material.Soil => DirtColor,
        _ => StoneColor,
    };

    /// <summary>Les taches de rouille du minerai de fer, regroupées par petits amas de 3 pixels.</summary>
    private static bool IsRustSpeck(int wx, int wy) => Noise.Hash01(wx / 3, wy / 3, 9, 0) > 0.6f;

    private static int Neighbor(LocalMap map, int x, int y, int fallback) =>
        map.InBounds(x, y) ? map.GetElevation(x, y) : fallback;

    private static Rgb Tint(Rgb c, float f) => new(Scale(c.R, f), Scale(c.G, f), Scale(c.B, f));

    private static byte Scale(byte value, float factor) => (byte)Math.Clamp(value * factor, 0f, 255f);
}
