using Godot;
using GodColony.Simulation.World;
using GodColony.View;

namespace GodColony;

/// <summary>Un atlas discret à l'accueil (et sous la vue chiffrée, celui du monde en cours), dessiné seulement au redimensionnement.</summary>
public partial class MenuBackdrop : Control
{
    private WorldGrid? _atlas;

    /// <summary>Le monde dessiné en fond ; à l'accueil, un monde fixe.</summary>
    public WorldGrid Atlas
    {
        get => _atlas ??= WorldGenerator.Generate(31891);
        set { _atlas = value; QueueRedraw(); }
    }

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
        // Toutes les cases en une seule liste de triangles : une commande de dessin au lieu de deux mille polygones
        // à chaque image (l'atlas reste à l'écran sous la vue chiffrée, qui laisse le processeur à la simulation).
        var tiles = Atlas.Tiles;
        var points = new Vector2[tiles.Length * 6];
        var colors = new Color[points.Length];
        var indices = new int[tiles.Length * 12];
        int t = 0;
        foreach (WorldTile tile in tiles)
        {
            (float x, float y) = WorldGrid.Center(tile.Col, tile.Row);
            Vector2 center = origin + new Vector2(x, y) * width;
            Color color = WorldMapView.BiomeColor(tile.Biome);
            color.A = tile.Biome == Biome.Ocean ? 0.10f : 0.19f;
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.DegToRad(60 * i - 30);
                points[t * 6 + i] = center + Vector2.FromAngle(angle) * (width / 1.732f * 0.97f);
                colors[t * 6 + i] = color;
            }
            for (int i = 0; i < 4; i++)
            {
                indices[t * 12 + i * 3] = t * 6;
                indices[t * 12 + i * 3 + 1] = t * 6 + i + 1;
                indices[t * 12 + i * 3 + 2] = t * 6 + i + 2;
            }
            t++;
        }
        RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), indices, points, colors);
        // Les anneaux évoquent une boussole sans concurrencer les textes du menu.
        var compass = new Vector2(Size.X * 0.87f, Size.Y * 0.79f);
        var ink = new Color(ArtDirection.Brass, 0.12f);
        DrawArc(compass, 72, 0, Mathf.Tau, 64, ink, 1, true);
        DrawArc(compass, 60, 0, Mathf.Tau, 64, ink, 1, true);
        DrawLine(compass - new Vector2(0, 90), compass + new Vector2(0, 90), ink, 1, true);
        DrawLine(compass - new Vector2(90, 0), compass + new Vector2(90, 0), ink, 1, true);
    }
}
