using Godot;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>
/// Affiche un morceau carré de la carte (32 × 32 cases) : le terrain dans une texture, puis la végétation.
/// Quand le sol d'une case change, seules les cases touchées sont repeintes ; quand seule la végétation change,
/// on redessine les plantes sans repeindre le terrain. C'est la vue de la carte qui décide quand mettre à jour.
/// </summary>
public partial class ChunkView : Node2D
{
    public const int Size = 32;
    private const int Pixels = Size * TerrainPainter.TileSize;

    private LocalMap _map = null!;
    private int _tileX0, _tileY0;

    /// <summary>Les pixels du terrain, gardés d'une mise à jour à l'autre pour ne repeindre que les cases touchées.</summary>
    private byte[] _pixels = null!;
    private Image _image = null!;
    private ImageTexture _texture = null!;

    /// <summary>Les cases dont le sol est à repeindre, ligne par ligne dans le morceau.</summary>
    private readonly bool[] _dirtyTiles = new bool[Size * Size];
    private int _dirtyCount;
    private bool _floraDirty;

    /// <summary>Le morceau attend une mise à jour (déjà inscrit dans la file de la vue de la carte).</summary>
    public bool Queued { get; set; }

    /// <param name="pixels">Le terrain déjà peint (voir <see cref="TerrainPainter.Paint"/>).</param>
    public void Init(LocalMap map, int chunkX, int chunkY, byte[] pixels)
    {
        _map = map;
        _tileX0 = chunkX * Size;
        _tileY0 = chunkY * Size;
        _pixels = pixels;
        Position = new Vector2(_tileX0, _tileY0) * TerrainPainter.TileSize;
        TextureFilter = TextureFilterEnum.Nearest;
        _image = Image.CreateFromData(Pixels, Pixels, false, Image.Format.Rgba8, _pixels);
        _texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>Le sol de la case (x, y), en coordonnées de la carte, est à repeindre.</summary>
    public void MarkTerrainDirty(int x, int y)
    {
        int index = (y - _tileY0) * Size + (x - _tileX0);
        if (_dirtyTiles[index])
            return;
        _dirtyTiles[index] = true;
        _dirtyCount++;
    }

    /// <summary>Seule la végétation a changé : il suffira de redessiner les plantes.</summary>
    public void MarkFloraDirty() => _floraDirty = true;

    /// <summary>Repeint les cases dont le sol a changé, puis redessine la végétation du morceau.</summary>
    public void Refresh()
    {
        if (_dirtyCount > 0)
        {
            const int tile = TerrainPainter.TileSize;
            for (int ty = 0; ty < Size; ty++)
            for (int tx = 0; tx < Size; tx++)
            {
                int index = ty * Size + tx;
                if (!_dirtyTiles[index])
                    continue;
                _dirtyTiles[index] = false;
                if (_map.InBounds(_tileX0 + tx, _tileY0 + ty))
                    TerrainPainter.PaintTile(_map, _tileX0 + tx, _tileY0 + ty, _pixels, Pixels, tx * tile, ty * tile);
            }
            _dirtyCount = 0;
            _image.SetData(Pixels, Pixels, false, Image.Format.Rgba8, _pixels);
            _texture.Update(_image);
        }
        _floraDirty = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawTexture(_texture, Vector2.Zero);

        // Ligne par ligne, du nord au sud : les arbres du bas passent devant ceux du haut (vue 3/4).
        // Les colons et les bâtiments, eux, sont triés avec la végétation par la vue des colons.
        const int tile = TerrainPainter.TileSize;
        for (int ty = 0; ty < Size; ty++)
        for (int tx = 0; tx < Size; tx++)
        {
            int x = _tileX0 + tx, y = _tileY0 + ty;
            if (_map.InBounds(x, y))
            {
                EnvironmentDetails.Draw(this, _map, x, y, new Vector2(tx * tile, ty * tile));
                FloraPainter.Draw(this, _map, x, y, new Vector2(tx * tile, ty * tile), shadow: true);
            }
        }
    }
}
