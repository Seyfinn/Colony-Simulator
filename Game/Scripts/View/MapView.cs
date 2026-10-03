using Godot;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Affiche toute la carte locale, découpée en morceaux de 32 × 32 cases.</summary>
public partial class MapView : Node2D
{
    private LocalMap _map = null!;
    private ChunkView[,] _chunks = null!;

    public void Init(LocalMap map)
    {
        _map = map;
        int chunksX = (map.Width + ChunkView.Size - 1) / ChunkView.Size;
        int chunksY = (map.Height + ChunkView.Size - 1) / ChunkView.Size;
        _chunks = new ChunkView[chunksX, chunksY];

        // Ajoutés du nord au sud, pour que les arbres d'un morceau débordent par-dessus le morceau au-dessus.
        for (int cy = 0; cy < chunksY; cy++)
        for (int cx = 0; cx < chunksX; cx++)
        {
            var chunk = new ChunkView();
            chunk.Init(map, cx, cy);
            AddChild(chunk);
            _chunks[cx, cy] = chunk;
        }

        map.TileChanged += OnTileChanged;
    }

    public override void _ExitTree()
    {
        if (_map is not null)
            _map.TileChanged -= OnTileChanged;
    }

    /// <summary>Une case modifiée change aussi l'aspect de ses voisines (falaises, bords).</summary>
    private void OnTileChanged(int x, int y)
    {
        MarkDirty(x, y);
        MarkDirty(x, y + 1);
        MarkDirty(x - 1, y);
        MarkDirty(x + 1, y);
    }

    private void MarkDirty(int x, int y)
    {
        if (_map.InBounds(x, y))
            _chunks[x / ChunkView.Size, y / ChunkView.Size].MarkDirty();
    }
}
