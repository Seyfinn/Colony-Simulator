using System;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Tracé visuel des canaux, sans changer leurs cases, leur débit ni l'irrigation.</summary>
public static class WaterGeometry
{
    public readonly record struct Stream(float X, float Y, float Dx, float Dy, float Flow);

    /// <summary>Segments du courant traversant cette case, y compris leurs coins arrondis sur les berges voisines.</summary>
    public static Stream[] Streams(LocalMap map, int x, int y)
    {
        System.Collections.Generic.List<Stream>? streams = null;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int sx = x + dx, sy = y + dy;
            if (!map.InBounds(sx, sy) || !map.IsRiver(sx, sy) || map.IsFlooded(sx, sy)) continue;
            var next = map.RiverDownstream(sx, sy);
            streams ??= [];
            streams.Add(next is { } to
                ? new Stream(dx * 32 + 16, dy * 32 + 16, (to.X - sx) * 32, (to.Y - sy) * 32, map.GetFlow(sx, sy))
                : new Stream(dx * 32 + 16, dy * 32 + 16, 0, 0, map.GetFlow(sx, sy)));
            foreach (var (cx, cy) in CanalSteps)
            {
                int nx = sx + cx, ny = sy + cy;
                if (map.InBounds(nx, ny) && map.IsCanalWet(nx, ny))
                    streams.Add(new Stream(dx * 32 + 16, dy * 32 + 16, cx * 32, cy * 32, 0.3f));
            }
        }
        return streams?.ToArray() ?? [];
    }
    private static readonly (int X, int Y)[] CanalSteps = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public static (float Distance, Stream Stream) Nearest(Stream[] streams, float x, float y)
    {
        float best = float.MaxValue;
        Stream closest = default;
        foreach (Stream stream in streams)
        {
            float length = stream.Dx * stream.Dx + stream.Dy * stream.Dy;
            float t = length == 0 ? 0 : Math.Clamp(((x - stream.X) * stream.Dx + (y - stream.Y) * stream.Dy) / length, 0, 1);
            float px = x - stream.X - stream.Dx * t, py = y - stream.Y - stream.Dy * t;
            float distance = px * px + py * py;
            if (distance >= best) continue;
            best = distance; closest = stream;
        }
        return (MathF.Sqrt(best), closest);
    }

    // Nord, est, sud, ouest. Les fossés secs raccordent aussi les tronçons en eau.
    public static int Connections(LocalMap map, int x, int y)
    {
        bool Connect(int px, int py) => map.InBounds(px, py) && (map.IsCanal(px, py) || map.HasWater(px, py));
        return (Connect(x, y - 1) ? 1 : 0) | (Connect(x + 1, y) ? 2 : 0)
            | (Connect(x, y + 1) ? 4 : 0) | (Connect(x - 1, y) ? 8 : 0);
    }
    public static int Distance(int px, int py, int connections)
    {
        if (connections == 0) connections = 5;
        int dx = Math.Abs(px - 15), dy = Math.Abs(py - 15);
        int distance = Math.Max(dx, dy);
        if (((connections & 1) != 0 && py <= 16) || ((connections & 4) != 0 && py >= 15)) distance = Math.Min(distance, dx);
        if (((connections & 8) != 0 && px <= 16) || ((connections & 2) != 0 && px >= 15)) distance = Math.Min(distance, dy);
        return distance;
    }
}
