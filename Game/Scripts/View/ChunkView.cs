using Godot;
using GodColony.Simulation.Map;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>
/// Affiche un morceau carré de la carte (32 × 32 cases) : le terrain dans une texture, puis la végétation.
/// Quand une case change, seul son morceau est redessiné.
/// </summary>
public partial class ChunkView : Node2D
{
    public const int Size = 32;

    private LocalMap _map = null!;
    private int _tileX0, _tileY0;
    private Image? _image;
    private ImageTexture? _texture;
    private bool _dirty = true;

    public void Init(LocalMap map, int chunkX, int chunkY)
    {
        _map = map;
        _tileX0 = chunkX * Size;
        _tileY0 = chunkY * Size;
        Position = new Vector2(_tileX0, _tileY0) * TerrainPainter.TileSize;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void MarkDirty() => _dirty = true;

    public override void _Process(double delta)
    {
        if (_dirty)
            Rebuild();
    }

    private void Rebuild()
    {
        _dirty = false;
        int pixels = Size * TerrainPainter.TileSize;
        byte[] data = TerrainPainter.Paint(_map, _tileX0, _tileY0, Size, Size);
        if (_image is null || _texture is null)
        {
            _image = Image.CreateFromData(pixels, pixels, false, Image.Format.Rgba8, data);
            _texture = ImageTexture.CreateFromImage(_image);
        }
        else
        {
            _image.SetData(pixels, pixels, false, Image.Format.Rgba8, data);
            _texture.Update(_image);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_texture is null)
            return;
        DrawTexture(_texture, Vector2.Zero);

        // Ligne par ligne, du nord au sud : les arbres du bas passent devant ceux du haut (vue 3/4).
        const int tile = TerrainPainter.TileSize;
        for (int ty = 0; ty < Size; ty++)
        for (int tx = 0; tx < Size; tx++)
        {
            int x = _tileX0 + tx, y = _tileY0 + ty;
            if (!_map.InBounds(x, y))
                continue;
            FloraType flora = _map.GetFlora(x, y);
            if (flora == FloraType.None)
                continue;

            Texture2D sprite = flora == FloraType.Tree ? SpriteFactory.Tree
                : _map.GetBerries(x, y) > 0 ? SpriteFactory.Bush
                : SpriteFactory.BushEmpty;
            float scale = 0.55f + 0.45f * _map.GetFloraGrowth(x, y);
            Vector2 size = sprite.GetSize() * scale;
            // Le pied de la plante est posé au bas de la case, légèrement décalé pour éviter un effet de grille.
            var jitter = new Vector2(
                (Noise.Hash01(x, y, 3, 0) - 0.5f) * 8f,
                (Noise.Hash01(x, y, 4, 0) - 0.5f) * 4f);
            var bottomCenter = new Vector2(tx * tile + tile / 2f, ty * tile + tile - 3) + jitter;
            DrawTextureRect(sprite, new Rect2(bottomCenter - new Vector2(size.X / 2f, size.Y), size), false);
        }
    }
}
