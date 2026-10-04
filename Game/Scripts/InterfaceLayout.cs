using System;
using Godot;

namespace GodColony;

/// <summary>Une même grille pour les stocks, la navigation et les panneaux, même dans une petite fenêtre.</summary>
internal readonly record struct InterfaceLayout(int ResourceColumns, float ResourcesHeight, float NavigationTop, float ContentTop)
{
    public static InterfaceLayout For(Vector2 size, bool showStocks = true)
    {
        int columns = size.X >= 1480 ? 11 : 6;
        float height = columns == 11 ? 64 : 116;
        return showStocks ? new(columns, height, 98 + height + 12, 98 + height + 94) : new(columns, 0, 98, 180);
    }

    public static float SideWidth(Vector2 size) => Math.Clamp(size.X * 0.27f, 320, 380);
}
