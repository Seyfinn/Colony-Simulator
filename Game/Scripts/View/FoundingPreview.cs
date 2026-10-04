using Godot;

namespace GodColony.View;

/// <summary>Le camp suit le curseur ; un deuxième contour conserve l'emplacement fixé par un clic.</summary>
public partial class FoundingPreview : Node2D
{
    public Vector2I HoverTile { get; set; }
    public Vector2I? SelectedTile { get; set; }
    public bool Valid { get; set; }
    public bool SelectedValid { get; set; }

    public override void _Draw()
    {
        if (SelectedTile is { } selected) DrawSite(selected, SelectedValid, true);
        if (SelectedTile != HoverTile) DrawSite(HoverTile, Valid, false);
    }

    private void DrawSite(Vector2I tile, bool valid, bool selected)
    {
        int size = TerrainPainter.TileSize;
        var rect = new Rect2((tile.X - 2) * size, (tile.Y - 2) * size, 5 * size, 5 * size);
        Color color = valid ? ArtDirection.Sage : Color.Color8(240, 112, 98);
        DrawRect(rect, new Color(color, selected ? 0.18f : 0.09f));
        DrawRect(rect, color, false, selected ? 3 : 2);
        Vector2 center = new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * size;
        DrawCircle(center, 7, color);
        DrawLine(center - new Vector2(14, 0), center + new Vector2(14, 0), color, 2);
        DrawLine(center - new Vector2(0, 14), center + new Vector2(0, 14), color, 2);
    }
}
