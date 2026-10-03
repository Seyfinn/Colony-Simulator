using System;
using System.Collections.Generic;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Tuiles cardinales du courant réel ; les diagonales reçoivent un coude purement visuel.</summary>
public static class RiverTiles
{
    private static readonly Dictionary<string, byte[]?> Pixels = [];
    private static bool? _complete;

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

    public static void Reload() { Pixels.Clear(); _complete = null; }

    public static byte[]? Get(string name)
    {
        if (Pixels.TryGetValue(name, out var cached)) return cached;
        var image = AssetLibrary.Get("terrain/" + name + ".png")?.GetImage();
        if (image is not null && (image.GetWidth() != 32 || image.GetHeight() != 32))
        {
            Godot.GD.PushWarning($"Tuile ignorée (taille attendue 32 × 32) : {name}");
            image = null;
        }
        return Pixels[name] = image?.GetData();
    }

    private static int Direction(int dx, int dy) => dx > 0 ? 2 : dx < 0 ? 8 : dy > 0 ? 4 : 1;

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
            if (map.RiverDownstream(sx, sy) is not { } next) continue;
            // Même coude vu des deux extrémités : d'abord est/ouest, puis nord/sud.
            Edge(sx, sy, next.X, sy);
            Edge(next.X, sy, next.X, next.Y);
        }
        return mask;
    }

    public static byte[]? Accent(LocalMap map, int x, int y)
    {
        if (map.IsRiver(x, y) && map.RiverDownstream(x, y) is { } next)
        {
            int direction = Direction(next.X - x, next.Y - y);
            bool source = true;
            foreach (var _ in map.RiverUpstream(x, y)) { source = false; break; }
            if (source) return Get($"river_end_{direction}");
            if (map.GetElevation(next.X, next.Y) < map.GetElevation(x, y))
                return Get($"river_fall_{direction}");
        }
        return null;
    }

    public static int LakeEdges(LocalMap map, int x, int y, int riverMask)
    {
        bool Land(int px, int py) => map.InBounds(px, py) && !map.IsWater(px, py);
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
