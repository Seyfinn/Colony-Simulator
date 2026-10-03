using Godot;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Palette d'ambiance et typographie du village, indépendantes du moteur de simulation.</summary>
public static class ArtDirection
{
    public static readonly Color Cream = Color.Color8(245, 230, 192);
    public static readonly Color Sage = Color.Color8(155, 203, 171);
    public static readonly Color Charcoal = Color.Color8(48, 64, 59);
    public static readonly Color Brass = Color.Color8(226, 186, 111);
    public static readonly Font BodyFont = new SystemFont
    {
        FontNames = ["Trebuchet MS", "Verdana", "sans-serif"], FontWeight = 400,
    };
    public static readonly Font HeadingFont = new SystemFont
    {
        FontNames = ["Trebuchet MS", "Verdana", "sans-serif"], FontWeight = 700,
    };

    public static Color DayTint(GameClock clock)
    {
        float hour = clock.TimeOfDay * 24;
        Color night = new(0.40f, 0.50f, 0.66f);
        Color dawn = new(0.98f, 0.83f, 0.73f);
        Color day = new(1.00f, 0.99f, 0.94f);
        Color dusk = new(0.95f, 0.69f, 0.55f);
        if (hour < 5 || hour >= 22) return night;
        if (hour < 7) return night.Lerp(dawn, Smooth((hour - 5) / 2));
        if (hour < 9) return dawn.Lerp(day, Smooth((hour - 7) / 2));
        if (hour < 17) return day;
        if (hour < 20) return day.Lerp(dusk, Smooth((hour - 17) / 3));
        return dusk.Lerp(night, Smooth((hour - 20) / 2));
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);
}
