using System;
using Godot;
using GodColony.Simulation.Map;
using GodColony.Simulation.Colonies;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>Reflets à l'échelle native, dessinés sous les bâtiments et arrêtés en pause.</summary>
public static class WaterEffects
{
    // L'intégration conserve la pose lors d'un changement de débit ; à zéro, elle reste figée.
    internal static double MillPhase(double phase, double elapsed, float flow) =>
        (phase + Math.Max(0, elapsed) * Math.Clamp(flow, 0, 1) * 6) % 4;

    /// <summary>Roue, coursier et vanne accolés à la case d'eau du côté indiqué par Hydrology.</summary>
    public static void DrawMill(CanvasItem target, LocalMap map, Building mill, float flow, MillSide? side, double phase)
    {
        MillSide orientation = side ?? MillSide.East;
        bool lateral = orientation is MillSide.East or MillSide.West;
        int count = lateral ? mill.Height : mill.Width;
        int chosen = 0;
        float best = -1;
        for (int i = 0; i < count; i++)
        {
            var cell = orientation switch
            {
                MillSide.West => (mill.X - 1, mill.Y + i),
                MillSide.East => (mill.X + mill.Width, mill.Y + i),
                MillSide.North => (mill.X + i, mill.Y - 1),
                _ => (mill.X + i, mill.Y + mill.Height),
            };
            if (!map.InBounds(cell.Item1, cell.Item2)) continue;
            float water = map.IsFlooded(cell.Item1, cell.Item2) || map.IsCanalWet(cell.Item1, cell.Item2) ? 1
                : map.IsRiver(cell.Item1, cell.Item2) ? map.GetFlow(cell.Item1, cell.Item2) : 0;
            // À débit égal, préférer l'extrémité avant du mur : l'axe rejoint la maçonnerie, pas le toit.
            if (water >= best) { best = water; chosen = i; }
        }
        Vector2 contact = orientation switch
        {
            MillSide.West => new Vector2(mill.X * 32 - 16, (mill.Y + chosen) * 32 + 16),
            MillSide.East => new Vector2((mill.X + mill.Width) * 32 + 16, (mill.Y + chosen) * 32 + 16),
            MillSide.North => new Vector2((mill.X + chosen) * 32 + 16, mill.Y * 32 - 16),
            _ => new Vector2((mill.X + chosen) * 32 + 16, (mill.Y + mill.Height) * 32 + 16),
        };
        Vector2 inward = orientation switch { MillSide.West => Vector2.Right, MillSide.East => Vector2.Left, MillSide.North => Vector2.Down, _ => Vector2.Up };
        Vector2 across = new(-inward.Y, inward.X);
        // Au nord, le coursier passe sous le toit jusqu'au mur arrière de la silhouette en 3/4.
        Vector2 start = contact + inward * (orientation == MillSide.North ? 56 : 23);
        Color stone = Color.Color8(100, 110, 96), timber = Color.Color8(177, 126, 73);
        target.DrawLine(start, contact - inward * 13, ArtDirection.Charcoal, 14);
        target.DrawLine(start, contact - inward * 13, stone, 12);
        target.DrawLine(start, contact - inward * 13, flow > 0 ? Color.Color8(80, 144, 151) : Color.Color8(113, 91, 61), 8);
        target.DrawLine(start - across * 8, start + across * 8, timber, 3);
        target.DrawLine(start - across * 8, start - across * 8 + new Vector2(0, -9), timber, 2);
        target.DrawLine(start + across * 8, start + across * 8 + new Vector2(0, -9), timber, 2);
        if (flow > 0)
            for (int i = 0; i < 3; i++)
            {
                Vector2 foam = contact - inward * (8 + i * 4) + across * (i % 2 * 3 - 2);
                target.DrawLine((foam - across * 3).Round(), (foam + across * 3).Round(), new Color(ArtDirection.Cream, .5f + .3f * flow), 1);
            }
        Texture2D wheel = RemainingArt.WheelFrame((int)phase, orientation);
        target.DrawTexture(wheel, (contact - new Vector2(wheel.GetWidth() / 2f, wheel.GetHeight() - 4)).Round());
    }

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
            if (map.Roads.SurfaceAt(y * map.Width + x) == GodColony.Simulation.Colonies.RoadSurface.Bridge) continue;
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
