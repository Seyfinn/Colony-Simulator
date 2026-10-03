using System;
using Godot;
using GodColony.Simulation.Map;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>Reflets à l'échelle native, dessinés sous les bâtiments et arrêtés en pause.</summary>
public static class WaterEffects
{
    public static void Draw(CanvasItem target, LocalMap map, double time)
    {
        Vector2 size = target.GetViewportRect().Size;
        Transform2D canvas = target.GetViewport().GetCanvasTransform(), inverse = canvas.AffineInverse();
        Vector2 first = inverse * Vector2.Zero, last = inverse * size;
        const int tile = TerrainPainter.TileSize;
        int left = Math.Max(0, (int)(first.X / tile)), top = Math.Max(0, (int)(first.Y / tile));
        int right = Math.Min(map.Width - 1, (int)(last.X / tile) + 1), bottom = Math.Min(map.Height - 1, (int)(last.Y / tile) + 1);
        int drawn = 0;
        for (int y = top; y <= bottom && drawn < 120; y++)
        for (int x = left; x <= right && drawn < 120; x++)
        {
            Surface surface = map.GetSurface(x, y);
            if (surface is not (Surface.Water or Surface.River)) continue;
            float seed = Noise.Hash01(x, y, 179, map.Seed);
            if (seed < 0.55f) continue;
            bool canal = map.IsCanal(x, y), river = surface == Surface.River && !canal;
            float flow = river ? map.GetFlow(x, y) : 0.3f;
            float phase = ((float)time * (0.22f + flow * 0.22f) + seed * 5) % 1;
            var downstream = river ? map.RiverDownstream(x, y) : null;
            Vector2 direction = downstream is { } next ? new Vector2(next.X - x, next.Y - y).Normalized() : Vector2.Down;
            Vector2 offset = new(11 + seed * 10, 10 + Noise.Hash01(x, y, 181, map.Seed) * 12);
            if (river) offset = new Vector2(16, 16) + direction * ((phase - 0.5f) * 15);
            if (canal) offset = new Vector2(15, 15);
            Vector2 point = new Vector2(x, y) * tile + offset;
            Vector2 across = river ? new Vector2(-direction.Y, direction.X) : Vector2.Right;
            float half = canal ? 3 : 2 + phase * 3;
            Color foam = Color.Color8(211, 233, 207);
            foam.A = MathF.Sin(phase * MathF.PI) * (0.14f + flow * 0.1f);
            Vector2 screen = canvas * point;
            if (screen.X < 0 || screen.Y < 0 || screen.X > size.X || screen.Y > size.Y) continue;
            target.DrawLine((point - across * half).Round(), (point + across * half).Round(), foam, 1);
            drawn++;
        }
    }
}
