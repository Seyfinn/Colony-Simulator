using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Affiche toute la carte locale, découpée en morceaux de 32 × 32 cases.</summary>
public partial class MapView : Node2D
{
    /// <summary>
    /// Quand le sol d'une case change, on repeint ses voisines jusqu'à cette distance : son aspect dépend de ses voisines
    /// (berges, falaises, ombres), et le courant d'une rivière dessiné sur une case suit aussi les canaux qui la bordent,
    /// jusqu'à deux cases. Loin de l'eau (une roche minée, le plus souvent), un carré de 3 × 3 cases suffit.
    /// </summary>
    private const int TerrainReach = 1, TerrainReachNearWater = 2;

    /// <summary>
    /// Morceaux mis à jour au plus par image : ceux qu'on voit d'abord. Une journée qui fait grandir les arbres
    /// de toute la carte se répartit ainsi sur quelques images au lieu d'en figer une.
    /// </summary>
    private const int MaxRefreshesPerFrame = 3;

    private LocalMap _map = null!;
    private ChunkView[,] _chunks = null!;
    private readonly List<ChunkView> _queue = [];

    public void Init(LocalMap map)
    {
        _map = map;
        TreeDistribution.Prepare(map);
        int chunksX = (map.Width + ChunkView.Size - 1) / ChunkView.Size;
        int chunksY = (map.Height + ChunkView.Size - 1) / ChunkView.Size;
        _chunks = new ChunkView[chunksX, chunksY];

        // La première peinture du terrain, la plus longue, se répartit sur tous les cœurs du processeur
        // (la simulation est arrêtée pendant ce temps, rien ne change sur la carte). Les tuiles PNG se chargent
        // d'abord ici, sur le fil principal : Godot ne crée pas de texture depuis plusieurs fils à la fois.
        RiverTiles.Preload();
        var pixels = new byte[chunksX * chunksY][];
        Parallel.For(0, pixels.Length, i => pixels[i] = TerrainPainter.Paint(map,
            i % chunksX * ChunkView.Size, i / chunksX * ChunkView.Size, ChunkView.Size, ChunkView.Size));

        // Ajoutés du nord au sud, pour que les arbres d'un morceau débordent par-dessus le morceau au-dessus.
        for (int cy = 0; cy < chunksY; cy++)
        for (int cx = 0; cx < chunksX; cx++)
        {
            var chunk = new ChunkView();
            chunk.Init(map, cx, cy, pixels[cy * chunksX + cx]);
            AddChild(chunk);
            _chunks[cx, cy] = chunk;
        }

        map.TileChanged += OnTileChanged;
        map.FloraChanged += OnFloraChanged;
    }

    public override void _ExitTree()
    {
        if (_map is null)
            return;
        _map.TileChanged -= OnTileChanged;
        _map.FloraChanged -= OnFloraChanged;
    }

    /// <summary>Le sol d'une case a changé : on la repeint avec ses voisines.</summary>
    private void OnTileChanged(int x, int y)
    {
        int reach = NearWater(x, y) ? TerrainReachNearWater : TerrainReach;
        for (int ty = y - reach; ty <= y + reach; ty++)
        for (int tx = x - reach; tx <= x + reach; tx++)
        {
            if (!_map.InBounds(tx, ty))
                continue;
            ChunkView chunk = _chunks[tx / ChunkView.Size, ty / ChunkView.Size];
            chunk.MarkTerrainDirty(tx, ty);
            Enqueue(chunk);
        }
    }

    /// <summary>De l'eau (lac, rivière, retenue) ou un canal sur la case ou l'une de ses voisines.</summary>
    private bool NearWater(int x, int y)
    {
        for (int ty = y - 1; ty <= y + 1; ty++)
        for (int tx = x - 1; tx <= x + 1; tx++)
            if (_map.InBounds(tx, ty) && (_map.HasWater(tx, ty) || _map.IsCanal(tx, ty)))
                return true;
        return false;
    }

    /// <summary>Seule la végétation a changé : le morceau redessinera ses plantes, sans repeindre le terrain.</summary>
    private void OnFloraChanged(int x, int y)
    {
        ChunkView chunk = _chunks[x / ChunkView.Size, y / ChunkView.Size];
        chunk.MarkFloraDirty();
        Enqueue(chunk);
    }

    private void Enqueue(ChunkView chunk)
    {
        if (chunk.Queued)
            return;
        chunk.Queued = true;
        _queue.Add(chunk);
    }

    public override void _Process(double delta)
    {
        if (_queue.Count == 0)
            return;

        // Ce qu'on voit à l'écran, en pixels de la carte.
        Transform2D inverse = GetViewport().GetCanvasTransform().AffineInverse();
        Vector2 size = GetViewportRect().Size;
        Rect2 screen = new Rect2(inverse * Vector2.Zero, Vector2.Zero).Expand(inverse * size);
        const float chunkPixels = ChunkView.Size * TerrainPainter.TileSize;

        int refreshed = 0;
        for (int pass = 0; pass < 2 && refreshed < MaxRefreshesPerFrame; pass++)
        for (int i = 0; i < _queue.Count && refreshed < MaxRefreshesPerFrame; i++)
        {
            ChunkView chunk = _queue[i];
            bool visible = screen.Intersects(new Rect2(chunk.Position, new Vector2(chunkPixels, chunkPixels)));
            if (pass == 0 && !visible)
                continue;
            chunk.Queued = false;
            chunk.Refresh();
            _queue.RemoveAt(i--);
            refreshed++;
        }
    }
}
