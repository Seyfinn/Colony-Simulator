using System;
using Godot;

namespace GodColony.View;

/// <summary>Traits à la résolution native : aucun lissage ni agrandissement des détails.</summary>
internal sealed class PixelArt(int width, int height)
{
    public Image Image { get; } = Image.CreateEmpty(width, height, false, Godot.Image.Format.Rgba8);
    public void Dot(int x, int y, Color color)
    {
        if (x >= 0 && y >= 0 && x < Image.GetWidth() && y < Image.GetHeight()) Image.SetPixel(x, y, color);
    }
    public void Box(int x, int y, int w, int h, Color color)
    {
        for (int j = Math.Max(0, y); j < Math.Min(Image.GetHeight(), y + h); j++)
        for (int i = Math.Max(0, x); i < Math.Min(Image.GetWidth(), x + w); i++) Dot(i, j, color);
    }
    public void Line(int x0, int y0, int x1, int y1, Color color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            Dot(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int twice = error * 2;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }
    public void Oval(int cx, int cy, int rx, int ry, Color color)
    {
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
            if ((x - cx) * (x - cx) / (float)(rx * rx) + (y - cy) * (y - cy) / (float)(ry * ry) <= 1) Dot(x, y, color);
    }
    public void Polygon(Color color, params Vector2I[] points)
    {
        for (int y = 0; y < Image.GetHeight(); y++)
        for (int x = 0; x < Image.GetWidth(); x++)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                Vector2I a = points[i], b = points[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (float)(b.Y - a.Y) + a.X) inside = !inside;
            }
            if (inside) Dot(x, y, color);
        }
    }
    public ImageTexture Texture() => ImageTexture.CreateFromImage(Image);
}
