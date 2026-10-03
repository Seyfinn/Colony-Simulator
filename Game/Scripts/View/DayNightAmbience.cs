using System;
using Godot;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>Effets d'observation au-dessus de la carte, sous l'interface. Lit uniquement le monde.</summary>
public partial class DayNightAmbience : Node2D
{
    public readonly record struct Profile(Color Tint, float Dawn, float Day, float Dusk, float Night, bool Detailed);

    private LocalMap _map = null!;
    private GradientTexture2D _glow = null!;
    private Profile _profile;
    private Color _tint = Colors.White;
    private double _time;
    private bool _initialized;

    public Color Tint => _tint;
    public bool DetailedEffects => _profile.Detailed;
    public double AnimationTime => _time;

    public void Init(LocalMap map)
    {
        _map = map;
        TextureFilter = TextureFilterEnum.Linear;
        _glow = new GradientTexture2D
        {
            Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1, 0.5f),
            Gradient = new Gradient
            {
                Offsets = [0f, 0.3f, 0.7f, 1f],
                Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0.12f), new Color(1, 1, 1, 0)],
            },
        };
    }

    public static Profile Evaluate(GameClock clock, GameSpeed speed)
    {
        float hour = clock.TimeOfDay * 24;
        float night = Math.Clamp(1 - Fade(5, 9, hour) + Fade(18, 22, hour), 0, 1);
        if (speed == GameSpeed.Rapide)
            return new(Colors.White.Lerp(new Color(0.87f, 0.92f, 0.98f), night), 0, 0, 0, night, false);
        if (speed != GameSpeed.Observation)
            return new(Colors.White, 0, 0, 0, 0, false);
        float dawn = Fade(4.5f, 6.5f, hour) * (1 - Fade(6.5f, 9, hour));
        float dusk = Fade(17, 19.5f, hour) * (1 - Fade(19.5f, 22, hour));
        float day = Fade(7.5f, 10, hour) * (1 - Fade(16, 19, hour));
        return new(ArtDirection.DayTint(clock), dawn, day, dusk, night, true);
    }

    private static float Fade(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public void Update(GameClock clock, GameSpeed speed, bool paused, double delta)
    {
        _profile = Evaluate(clock, speed);
        if (!paused) _time += delta;
        // L'heure change continûment ; un changement de vitesse s'adoucit en quelques images.
        _tint = _initialized ? _tint.Lerp(_profile.Tint, 1 - MathF.Exp(-(float)delta * 9)) : _profile.Tint;
        _initialized = true;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_initialized || !_profile.Detailed) return;
        Vector2 size = GetViewportRect().Size;
        float dawn = _profile.Dawn, day = _profile.Day, dusk = _profile.Dusk, night = _profile.Night;
        float warmth = dawn * 0.16f + day * 0.06f + dusk * 0.14f;
        Color sunsetGlow = new Color(1, 0.79f + day * 0.12f, 0.5f + day * 0.2f, warmth)
            .Lerp(new Color(1, 0.78f, 0.92f, warmth), dusk);
        Glow(new Rect2(-size.X * 0.4f, -size.Y * 0.7f, size.X * 1.4f, size.Y * 1.8f),
            sunsetGlow);
        Glow(new Rect2(size.X * 0.45f, -size.Y * 0.65f, size.X, size.Y * 1.6f),
            new Color(0.58f, 0.73f, 1, night * 0.075f));

        float rays = day * 0.014f + dawn * 0.025f + dusk * 0.032f;
        for (int i = 0; i < 3; i++)
        {
            float x = size.X * (-0.15f + i * 0.24f);
            float drift = MathF.Sin((float)_time * 0.07f + i) * 15;
            DrawColoredPolygon([
                new Vector2(x + drift, 0), new Vector2(x + 38 + drift, 0),
                new Vector2(x + size.X * 0.43f + 115, size.Y), new Vector2(x + size.X * 0.43f, size.Y)],
                new Color(1, 0.9f, 0.64f, rays).Lerp(new Color(1, 0.84f, 0.94f, rays), dusk));
        }

        // Brume basse de l'aube : nappes larges, lente dérive, faible opacité.
        if (dawn > 0.01f)
            for (int i = 0; i < 4; i++)
            {
                float drift = MathF.Sin((float)_time * 0.09f + i * 1.7f) * size.X * 0.06f;
                Glow(new Rect2(size.X * (i * 0.27f - 0.22f) + drift, size.Y * (0.35f + i % 2 * 0.23f),
                        size.X * 0.8f, size.Y * 0.3f), new Color(0.82f, 0.9f, 0.86f, dawn * 0.11f));
            }
        DrawMotes(size);
    }


    private void Glow(Rect2 rectangle, Color color) => DrawTextureRect(_glow, rectangle, false, color);

    private void DrawMotes(Vector2 size)
    {
        float night = _profile.Night, daylight = _profile.Day;
        if (night < 0.05f && daylight < 0.05f) return;
        Transform2D canvas = GetViewport().GetCanvasTransform();
        Transform2D inverse = canvas.AffineInverse();
        Vector2 first = inverse * Vector2.Zero, last = inverse * size;
        const int spacing = TerrainPainter.TileSize * 7;
        int left = (int)MathF.Floor(first.X / spacing) - 1, top = (int)MathF.Floor(first.Y / spacing) - 1;
        int right = (int)MathF.Ceiling(last.X / spacing) + 1, bottom = (int)MathF.Ceiling(last.Y / spacing) + 1;
        // Le coût dépend de l'écran, avec une limite fixe même en dézoomant sur toute la carte.
        int drawn = 0;
        for (int y = top; y <= bottom && drawn < 48; y++)
        for (int x = left; x <= right && drawn < 48; x++)
        {
            float seed = Noise.Hash01(x, y, 83, 0);
            if (seed < 0.38f) continue;
            Vector2 point = new(x * spacing + seed * spacing, y * spacing + Noise.Hash01(x, y, 89, 0) * spacing);
            int tx = (int)MathF.Floor(point.X / TerrainPainter.TileSize), ty = (int)MathF.Floor(point.Y / TerrainPainter.TileSize);
            if (!_map.InBounds(tx, ty) || _map.GetSurface(tx, ty) is not (Surface.Grass or Surface.Water)) continue;
            float phase = (float)_time + seed * 30;
            point += new Vector2(MathF.Sin(phase * 0.45f) * 14, MathF.Cos(phase * 0.6f) * 8);
            Vector2 screen = (canvas * point).Round();
            if (screen.X < 0 || screen.Y < 0 || screen.X > size.X || screen.Y > size.Y) continue;
            drawn++;
            if (night > 0.05f)
            {
                float pulse = MathF.Pow(Math.Max(0, MathF.Sin(phase * 1.2f)), 2);
                float opacity = pulse * night;
                DrawRect(new Rect2(screen - new Vector2(3, 3), new Vector2(8, 8)), new Color(0.78f, 0.94f, 0.53f, opacity * 0.06f));
                DrawRect(new Rect2(screen - Vector2.One, new Vector2(4, 4)), new Color(0.78f, 0.94f, 0.53f, opacity * 0.15f));
                DrawRect(new Rect2(screen, new Vector2(2, 2)), new Color(0.91f, 1, 0.65f, opacity * 0.8f));
            }
            else
                DrawRect(new Rect2(screen, new Vector2(1, 2)), new Color(1, 0.94f, 0.73f, daylight * (0.12f + seed * 0.16f)));
        }
    }
}
