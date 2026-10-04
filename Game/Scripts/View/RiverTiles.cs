using System;
using System.Collections.Generic;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Tuiles à huit directions du courant réel, avec berges continues aux coins.</summary>
public static class RiverTiles
{
    private static readonly Dictionary<string, byte[]?> Pixels = [];
    private static bool? _complete;
    private static readonly Dictionary<(int Mask, int Corners), byte[]?> Composed = [];
    private static readonly (int X, int Y)[] CanalSteps = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public static bool Complete
    {
        get
        {
            if (_complete is { } complete) return complete;
            for (int mask = 0; mask < 16; mask++)
                if (Get($"river_{mask}") is null) return (_complete = false).Value;
            return (_complete = true).Value;
        }
    }

    public static void Reload() { Pixels.Clear(); Composed.Clear(); _complete = null; _preloaded = false; }

    /// <summary>Après <see cref="Preload"/>, toutes les tuiles du dossier sont en mémoire : un nom absent n'existe pas.</summary>
    private static bool _preloaded;

    /// <summary>
    /// Charge d'avance toutes les tuiles de <c>terrain/</c>, sur le fil principal. À faire avant la peinture en parallèle
    /// de <see cref="MapView"/> : les textures de Godot et les dictionnaires de cache ne supportent pas d'être remplis
    /// par plusieurs fils à la fois. Ensuite, la peinture ne fait plus que lire.
    /// </summary>
    public static void Preload()
    {
        if (_preloaded) return;
        foreach (string name in AssetLibrary.Names("terrain"))
            Get(name);
        _ = Complete;
        _preloaded = true;
    }

    public static byte[]? Get(string name)
    {
        if (Pixels.TryGetValue(name, out var cached)) return cached;
        if (_preloaded) return null;
        var image = AssetLibrary.Get("terrain/" + name + ".png")?.GetImage();
        if (image is not null && (image.GetWidth() != 32 || image.GetHeight() != 32))
        {
            Godot.GD.PushWarning($"Tuile ignorée (taille attendue 32 × 32) : {name}");
            image = null;
        }
        return Pixels[name] = image?.GetData();
    }

    private static int Direction(int dx, int dy) => (Math.Sign(dx), Math.Sign(dy)) switch
    {
        (1, -1) => 16, (1, 1) => 32, (-1, 1) => 64, (-1, -1) => 128,
        (1, 0) => 2, (-1, 0) => 8, (0, 1) => 4, _ => 1,
    };
    private static string Suffix(int direction) => direction switch
    {
        16 => "ne", 32 => "se", 64 => "sw", 128 => "nw", _ => direction.ToString(),
    };

    public static int Connections(LocalMap map, int x, int y)
    {
        int mask = 0;
        void Edge(int ax, int ay, int bx, int by)
        {
            if (ax == bx && ay == by) return;
            if (x == ax && y == ay) mask |= Direction(bx - ax, by - ay);
            if (x == bx && y == by) mask |= Direction(ax - bx, ay - by);
        }
        for (int sy = y - 1; sy <= y + 1; sy++)
        for (int sx = x - 1; sx <= x + 1; sx++)
        {
            if (!map.InBounds(sx, sy) || !map.IsRiver(sx, sy) || map.IsFlooded(sx, sy)) continue;
            if (map.RiverDownstream(sx, sy) is { } next && !BothWide(map, sx, sy, next.X, next.Y)) Edge(sx, sy, next.X, next.Y);
            // Une prise d'eau ouvre aussi la berge de la rivière vers le canal alimenté.
            foreach (var (dx, dy) in CanalSteps)
                if (map.InBounds(sx + dx, sy + dy) && map.IsCanalWet(sx + dx, sy + dy))
                    Edge(sx, sy, sx + dx, sy + dy);
        }
        return mask;
    }

    /// <summary>Berges des diagonales qui effleurent un coin de cette case latérale.</summary>
    public static int Corners(LocalMap map, int x, int y)
    {
        int mask = 0;
        for (int sy = y - 1; sy <= y + 1; sy++)
        for (int sx = x - 1; sx <= x + 1; sx++)
        {
            if (!map.InBounds(sx, sy) || !map.IsRiver(sx, sy) || map.IsFlooded(sx, sy)) continue;
            if (map.RiverDownstream(sx, sy) is not { } next || sx == next.X || sy == next.Y || BothWide(map, sx, sy, next.X, next.Y)) continue;
            if ((x == next.X && y == sy) || (x == sx && y == next.Y))
                mask |= Direction(Math.Max(sx, next.X) == x ? -1 : 1, Math.Max(sy, next.Y) == y ? -1 : 1);
        }
        return mask;
    }

    /// <summary>
    /// Deux cases d'un fleuve large : leur liaison n'a pas de tuile de rivière, le fleuve large est peint comme une nappe d'eau
    /// (voir <see cref="TerrainPainter"/>), et ses cases voisines n'ont pas de berge de ruisseau à dessiner.
    /// </summary>
    public static bool BothWide(LocalMap map, int ax, int ay, int bx, int by) =>
        map.InBounds(bx, by) && map.IsWideRiver(ax, ay) && map.IsWideRiver(bx, by);

    // L'eau l'emporte sur une berge superposée : aucun trait de terre à une confluence.
    private static int Priority(byte[] data, int index)
    {
        if (data[index + 3] == 0) return 0;
        int r = data[index], g = data[index + 1], b = data[index + 2];
        return b > r && g > r || r == 193 ? 2 : 1;
    }

    public static byte[]? River(int mask, int corners, bool center)
    {
        if (!center && mask == 0 && corners == 0) return null;
        // La peinture en parallèle peut demander la même tuile depuis plusieurs fils : un seul la compose à la fois.
        lock (Composed)
            return Compose(mask, corners, center);
    }

    private static byte[]? Compose(int mask, int corners, bool center)
    {
        // -1 distingue une berge latérale d'une mare isolée (masque 0).
        int key = mask == 0 && !center ? -1 : mask;
        if (Composed.TryGetValue((key, corners), out var cached)) return cached;
        byte[]? tile = key < 0 ? new byte[32 * 32 * 4] : Get(mask < 16 ? $"river_{mask}" : $"river_diag_{mask}");
        if (tile is null) return Composed[(key, corners)] = null;
        if (corners == 0) return Composed[(key, corners)] = tile;
        tile = (byte[])tile.Clone();
        foreach (int direction in new[] { 16, 32, 64, 128 })
        {
            if ((corners & direction) == 0) continue;
            var corner = Get($"river_corner_{Suffix(direction)}");
            if (corner is null) return Composed[(key, corners)] = null;
            for (int i = 0; i < tile.Length; i += 4)
                if (Priority(corner, i) > Priority(tile, i))
                    Array.Copy(corner, i, tile, i, 4);
        }
        return Composed[(key, corners)] = tile;
    }

    public static byte[]? Shore(int mask, byte[]? river)
    {
        var shore = Get($"lake_edge_{mask}");
        if (shore is null || river is null) return shore;
        shore = (byte[])shore.Clone();
        for (int i = 0; i < shore.Length; i += 4)
            if (Priority(river, i) == 2) shore[i + 3] = 0;
        return shore;
    }

    public static byte[]? Accent(LocalMap map, int x, int y)
    {
        if (map.IsRiver(x, y) && map.RiverDownstream(x, y) is { } next)
        {
            int direction = Direction(next.X - x, next.Y - y);
            bool source = true;
            foreach (var _ in map.RiverUpstream(x, y)) { source = false; break; }
            if (source) return Get($"river_end_{Suffix(direction)}");
            if (map.GetElevation(next.X, next.Y) < map.GetElevation(x, y))
                return Get($"river_fall_{Suffix(direction)}");
        }
        return null;
    }

    public static int LakeEdges(LocalMap map, int x, int y, int riverMask)
    {
        bool Land(int px, int py) => map.InBounds(px, py) && !map.IsWater(px, py) && !map.IsCanalWet(px, py)
            && !(map.IsWideRiver(px, py) && !map.IsFlooded(px, py));
        int mask = (Land(x, y - 1) ? 1 : 0) | (Land(x + 1, y) ? 2 : 0)
            | (Land(x, y + 1) ? 4 : 0) | (Land(x - 1, y) ? 8 : 0);
        return mask & ~riverMask; // L'embouchure reste ouverte.
    }

    public static void Blend(byte[] overlay, byte[] target, int stride, int ox, int oy)
    {
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            int source = (y * 32 + x) * 4, dest = ((oy + y) * stride + ox + x) * 4;
            int alpha = overlay[source + 3];
            for (int c = 0; c < 3; c++)
                target[dest + c] = (byte)((overlay[source + c] * alpha + target[dest + c] * (255 - alpha) + 127) / 255);
        }
    }
}
