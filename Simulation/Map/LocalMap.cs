using GodColony.Simulation.Generation;

namespace GodColony.Simulation.Map;

/// <summary>Type de sol en surface des plaines.</summary>
public enum SoilType : byte { Grass, Dirt, Sand }

/// <summary>Matière d'une couche de terrain.</summary>
public enum Material : byte { Soil, Stone, IronOre }

/// <summary>Ce qu'on voit sur le dessus d'une case.</summary>
public enum Surface : byte { Water, Grass, Dirt, Sand, Stone, IronOre }

public enum FloraType : byte { None, Tree, Bush }

/// <summary>
/// La carte locale d'une colonie.
///
/// Chaque case est une colonne de couches empilées : son altitude est le nombre de couches.
/// Miner une case retire la couche du dessus, ce qui creuse la montagne petit à petit
/// et révèle les filons de minerai cachés à l'intérieur.
/// </summary>
public sealed class LocalMap
{
    public const int MaxElevation = 12;

    /// <summary>Les cases dont l'altitude est inférieure ou égale à ce niveau sont sous l'eau.</summary>
    public const int WaterLevel = 2;

    /// <summary>À partir de cette altitude d'origine, une case est de la montagne (roche nue).</summary>
    public const int MountainElevation = 7;

    /// <summary>Épaisseur de terre au-dessus de la roche, dans les plaines.</summary>
    public const int SoilThickness = 1;

    /// <summary>On ne creuse pas plus bas, pour ne pas atteindre la nappe d'eau (l'eau viendra au jalon 2).</summary>
    public const int MinMiningElevation = WaterLevel + 1;

    public int Width { get; }
    public int Height { get; }
    public int Seed { get; }

    private readonly byte[] _elevation;
    private readonly byte[] _originalElevation;
    private readonly SoilType[] _soil;
    private readonly FloraType[] _flora;
    private readonly float[] _floraGrowth;
    private readonly byte[] _berries;

    /// <summary>Nombre maximal de baies sur un buisson ; il en repousse une par jour.</summary>
    public const int MaxBerries = 3;

    /// <summary>Déclenché quand une case change d'aspect (minée, arbre coupé…).</summary>
    public event Action<int, int>? TileChanged;

    internal LocalMap(int width, int height, int seed)
    {
        Width = width;
        Height = height;
        Seed = seed;
        int n = width * height;
        _elevation = new byte[n];
        _originalElevation = new byte[n];
        _soil = new SoilType[n];
        _flora = new FloraType[n];
        _floraGrowth = new float[n];
        _berries = new byte[n];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    private int Index(int x, int y) => y * Width + x;

    public int GetElevation(int x, int y) => _elevation[Index(x, y)];

    public bool IsWater(int x, int y) => _originalElevation[Index(x, y)] <= WaterLevel;

    public bool IsMountain(int x, int y) => _originalElevation[Index(x, y)] >= MountainElevation;

    public SoilType GetSoil(int x, int y) => _soil[Index(x, y)];

    public FloraType GetFlora(int x, int y) => _flora[Index(x, y)];

    /// <summary>Croissance de la plante, de 0 (pousse) à 1 (adulte).</summary>
    public float GetFloraGrowth(int x, int y) => _floraGrowth[Index(x, y)];

    public int GetBerries(int x, int y) => _berries[Index(x, y)];

    /// <summary>Cueille toutes les baies d'un buisson et renvoie leur nombre.</summary>
    public int HarvestBerries(int x, int y)
    {
        int i = Index(x, y);
        int count = _berries[i];
        if (count == 0)
            return 0;
        _berries[i] = 0;
        TileChanged?.Invoke(x, y);
        return count;
    }

    /// <summary>Retire la végétation d'une case (pour dégager un campement, par exemple).</summary>
    public void ClearFlora(int x, int y)
    {
        int i = Index(x, y);
        if (_flora[i] == FloraType.None)
            return;
        _flora[i] = FloraType.None;
        _berries[i] = 0;
        TileChanged?.Invoke(x, y);
    }

    /// <summary>Appelé à chaque nouveau jour : une baie repousse sur chaque buisson.</summary>
    public void DailyUpdate()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int i = Index(x, y);
            if (_flora[i] == FloraType.Bush && _berries[i] < MaxBerries)
            {
                _berries[i]++;
                TileChanged?.Invoke(x, y);
            }
        }
    }

    // --- Déplacements ---

    /// <summary>On peut marcher partout sauf dans l'eau.</summary>
    public bool IsWalkable(int x, int y) => InBounds(x, y) && !IsWater(x, y);

    /// <summary>
    /// Peut-on passer d'une case voisine à l'autre ? Une marche d'un niveau se monte ou se descend,
    /// une falaise de deux niveaux ou plus est infranchissable.
    /// </summary>
    public bool CanStep(int fromX, int fromY, int toX, int toY) =>
        IsWalkable(toX, toY) && Math.Abs(GetElevation(toX, toY) - GetElevation(fromX, fromY)) <= 1;

    /// <summary>Coût de traversée d'une case : on avance moins vite en forêt.</summary>
    public float MoveCost(int x, int y) => GetFlora(x, y) == FloraType.Tree ? 1.6f : 1f;

    /// <summary>Matière de la couche numéro <paramref name="level"/> (0 = tout en bas) de la case.</summary>
    public Material MaterialAt(int x, int y, int level)
    {
        int original = _originalElevation[Index(x, y)];
        bool isPlain = original < MountainElevation;
        if (isPlain && level >= original - SoilThickness)
            return Material.Soil;
        return IsIronVein(x, y, level) ? Material.IronOre : Material.Stone;
    }

    public Material TopMaterial(int x, int y) => MaterialAt(x, y, GetElevation(x, y) - 1);

    public Surface GetSurface(int x, int y)
    {
        if (IsWater(x, y))
            return Surface.Water;
        return TopMaterial(x, y) switch
        {
            Material.Stone => Surface.Stone,
            Material.IronOre => Surface.IronOre,
            _ => GetSoil(x, y) switch
            {
                SoilType.Dirt => Surface.Dirt,
                SoilType.Sand => Surface.Sand,
                _ => Surface.Grass,
            },
        };
    }

    public bool CanMine(int x, int y) =>
        InBounds(x, y)
        && !IsWater(x, y)
        && TopMaterial(x, y) != Material.Soil
        && GetElevation(x, y) > MinMiningElevation;

    /// <summary>Retire la couche du dessus et renvoie la matière extraite.</summary>
    public Material Mine(int x, int y)
    {
        if (!CanMine(x, y))
            throw new InvalidOperationException($"La case ({x}, {y}) ne peut pas être minée.");

        Material extracted = TopMaterial(x, y);
        int i = Index(x, y);
        _elevation[i]--;
        _flora[i] = FloraType.None;
        TileChanged?.Invoke(x, y);
        return extracted;
    }

    /// <summary>Les filons de fer forment des veines en 3D à l'intérieur de la roche.</summary>
    private bool IsIronVein(int x, int y, int level) =>
        Noise.Fractal3D(x * 0.11f, y * 0.11f, level * 0.45f, Seed + 500, 3) > 0.64f;

    // Utilisé uniquement par le générateur de carte.
    internal void SetGenerated(int x, int y, int elevation, SoilType soil, FloraType flora, float growth)
    {
        int i = Index(x, y);
        _elevation[i] = (byte)elevation;
        _originalElevation[i] = (byte)elevation;
        _soil[i] = soil;
        _flora[i] = flora;
        _floraGrowth[i] = growth;
        _berries[i] = flora == FloraType.Bush ? (byte)MaxBerries : (byte)0;
    }
}
