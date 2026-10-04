using Godot;
using GodColony.Simulation.World;
using GodColony.View;

namespace GodColony;

/// <summary>Un atlas discret à l'accueil, dessiné seulement au redimensionnement.</summary>
public partial class MenuBackdrop : Control
{
    private readonly WorldGrid _atlas = WorldGenerator.Generate(31891);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Color.Color8(9, 22, 21));
        float width = Mathf.Max(Size.X / 56, Size.Y / 32);
        Vector2 origin = (Size - new Vector2(64 * width, 35 * width)) / 2;
        foreach (WorldTile tile in _atlas.Tiles)
        {
            (float x, float y) = WorldGrid.Center(tile.Col, tile.Row);
            Vector2 center = origin + new Vector2(x, y) * width;
            var points = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.DegToRad(60 * i - 30);
                points[i] = center + Vector2.FromAngle(angle) * (width / 1.732f * 0.97f);
            }
            Color color = WorldMapView.BiomeColor(tile.Biome);
            color.A = tile.Biome == Biome.Ocean ? 0.10f : 0.19f;
            DrawColoredPolygon(points, color);
        }
        // Les anneaux évoquent une boussole sans concurrencer les textes du menu.
        var compass = new Vector2(Size.X * 0.87f, Size.Y * 0.79f);
        var ink = new Color(ArtDirection.Brass, 0.12f);
        DrawArc(compass, 72, 0, Mathf.Tau, 64, ink, 1, true);
        DrawArc(compass, 60, 0, Mathf.Tau, 64, ink, 1, true);
        DrawLine(compass - new Vector2(0, 90), compass + new Vector2(0, 90), ink, 1, true);
        DrawLine(compass - new Vector2(90, 0), compass + new Vector2(90, 0), ink, 1, true);
    }
}
