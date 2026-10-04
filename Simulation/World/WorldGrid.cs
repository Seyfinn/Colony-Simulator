namespace GodColony.Simulation.World;

/// <summary>Une case hexagonale de la carte du monde. Chaque case donne sa propre carte locale à qui s'y installe.</summary>
public sealed class WorldTile
{
    internal WorldTile(int index, int col, int row)
    {
        Index = index;
        Col = col;
        Row = row;
    }

    public int Index { get; }
    public int Col { get; }
    public int Row { get; }

    /// <summary>Altitude de 0 (fond de l'océan) à 1 (plus haut sommet) ; le niveau de la mer est <see cref="WorldGrid.SeaLevel"/>.</summary>
    public float Elevation { get; internal set; }

    /// <summary>Température moyenne de l'année, en degrés : froide au nord, chaude au sud, plus fraîche en altitude.</summary>
    public float Temperature { get; internal set; }

    /// <summary>Pluies, de 0 (aride) à 1 (détrempé).</summary>
    public float Rainfall { get; internal set; }

    public Biome Biome { get; internal set; }
    public Relief Relief { get; internal set; }

    /// <summary>Taille de la rivière qui traverse la case : 0 aucune, 1 rivière, 2 grand fleuve.</summary>
    public int River { get; internal set; }

    /// <summary>La case voisine vers laquelle l'eau s'écoule (-1 pour l'océan).</summary>
    public int FlowsTo { get; internal set; } = -1;

    /// <summary>La case touche l'océan.</summary>
    public bool Coastal { get; internal set; }

    public bool IsOcean => Biome == Biome.Ocean;

    public BiomeInfo Info => BiomeInfo.Of(Biome);

    /// <summary>Une colonie peut-elle s'y installer ?</summary>
    public bool Habitable => Info.Habitable && Relief != Relief.Impassable;

    /// <summary>Coût de la traversée de la case pour une caravane (1 = une case de prairie plate ; infini = impossible).</summary>
    public float TravelCost => Info.TravelCost * BiomeInfo.TravelFactor(Relief) * (River > 0 ? 1.1f : 1f);

    /// <summary>Description courte : « Forêt tempérée, collines ».</summary>
    public string Describe() => IsOcean ? Info.Name : $"{Info.Name}, {BiomeInfo.NameOf(Relief)}";
}

/// <summary>
/// La grille hexagonale du monde (hexagones « pointe en haut », lignes impaires décalées d'un demi-hexagone vers la droite).
/// Le nord est en haut : il y fait froid ; le sud, en bas, est tropical.
/// </summary>
public sealed class WorldGrid
{
    public const int DefaultWidth = 64, DefaultHeight = 40;

    /// <summary>Part du monde couverte par l'océan.</summary>
    public const float OceanShare = 0.40f;

    public WorldGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new WorldTile[width * height];
        for (int row = 0; row < height; row++)
        for (int col = 0; col < width; col++)
            Tiles[row * width + col] = new WorldTile(row * width + col, col, row);
    }

    public int Width { get; }
    public int Height { get; }
    public WorldTile[] Tiles { get; }

    /// <summary>Altitude du rivage.</summary>
    public float SeaLevel { get; internal set; }

    public WorldTile this[int index] => Tiles[index];

    public bool InBounds(int col, int row) => col >= 0 && row >= 0 && col < Width && row < Height;

    public int IndexOf(int col, int row) => row * Width + col;

    private static readonly (int Dc, int Dr)[] EvenRow = [(1, 0), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1)];
    private static readonly (int Dc, int Dr)[] OddRow = [(1, 0), (1, -1), (0, -1), (-1, 0), (0, 1), (1, 1)];

    /// <summary>Les six cases voisines (moins sur les bords).</summary>
    public IEnumerable<int> Neighbors(int index)
    {
        int col = index % Width, row = index / Width;
        foreach ((int dc, int dr) in (row & 1) == 0 ? EvenRow : OddRow)
        {
            int c = col + dc, r = row + dr;
            if (InBounds(c, r))
                yield return r * Width + c;
        }
    }

    /// <summary>Nombre de pas d'hexagone en hexagone entre deux cases.</summary>
    public int Distance(int a, int b)
    {
        (int ax, int az) = Cube(a);
        (int bx, int bz) = Cube(b);
        int dx = ax - bx, dz = az - bz, dy = -dx - dz;
        return (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz)) / 2;
    }

    private (int X, int Z) Cube(int index)
    {
        int col = index % Width, row = index / Width;
        return (col - (row - (row & 1)) / 2, row);
    }

    /// <summary>
    /// Position du centre de la case, en largeurs d'hexagone (x) et en même unité pour y : pratique pour dessiner
    /// et pour le bruit de génération, qui ne doit pas voir la grille.
    /// </summary>
    public static (float X, float Y) Center(int col, int row) => (col + 0.5f * (row & 1), row * 0.8660254f);
}
