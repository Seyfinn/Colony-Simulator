using GodColony.Simulation.Generation;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Map;

/// <summary>Type de sol en surface des plaines.</summary>
public enum SoilType : byte { Grass, Dirt, Sand }

/// <summary>Matière d'une couche de terrain.</summary>
public enum Material : byte { Soil, Stone, IronOre }

/// <summary>Ce qu'on voit sur le dessus d'une case.</summary>
public enum Surface : byte { Water, Grass, Dirt, Sand, Stone, IronOre, River }

/// <summary>Végétation d'une case. Une souche reste après l'abattage d'un arbre, et peut repousser.</summary>
public enum FloraType : byte { None, Tree, Bush, Stump }

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

    /// <summary>Nombre maximal de baies sur un buisson ; il en repousse une par jour, sauf en hiver.</summary>
    public const int MaxBerries = 3;

    /// <summary>Nombre maximal de poissons par case d'eau ; ils se renouvellent lentement.</summary>
    public const int MaxFish = 3;

    private const float FishRegrowthChancePerDay = 0.5f;
    private const float WinterFishRegrowthChancePerDay = 0.25f;

    private readonly byte[] _fish;

    /// <summary>Les cases de rivière : de l'eau peu profonde, qu'on traverse à gué (en ralentissant) et où l'on pêche.</summary>
    private readonly bool[] _river;

    /// <summary>Cases proches de l'eau (lac, mer ou rivière) : leur terre est plus riche.</summary>
    private readonly bool[] _bank;

    /// <summary>Distance (en cases) à l'eau en deçà de laquelle une terre est fertile.</summary>
    public const int BankReach = 2;

    /// <summary>Niveau de l'eau retenue derrière un barrage (0 = case non inondée).</summary>
    private readonly byte[] _floodLevel;

    /// <summary>Pour chaque case de rivière, l'index de la case vers laquelle elle coule (-1 sinon).</summary>
    private readonly int[] _downstream;

    /// <summary>Largeur de la rivière au droit de chaque case de rivière, en cases (0 si ce n'est pas une rivière).</summary>
    private readonly byte[] _riverWidth;

    /// <summary>Humidité du sol, de 0 (sec) à 255 (détrempé) : les forêts poussent là où c'est humide, la terre nue là où c'est sec.</summary>
    private readonly byte[] _moisture;

    /// <summary>Débit relatif de la rivière sur chaque case : 1 au naturel, moins en aval d'un barrage.</summary>
    private readonly float[] _flow;

    /// <summary>Les filons de fer déjà calculés, couche par couche (voir <see cref="IsIronVein"/>).</summary>
    private readonly uint[] _veins;

    /// <summary>Canaux creusés par les colons : 0 = rien, 1 = fossé à sec, 2 = fossé où l'eau coule.</summary>
    private readonly byte[] _canal;

    /// <summary>Pour chaque case, le nombre de canaux en eau à portée : au-dessus de 0, la terre est irriguée.</summary>
    private readonly byte[] _irrigation;

    /// <summary>Distance (en cases) jusqu'où un canal en eau irrigue les terres.</summary>
    public const int IrrigationReach = 3;

    /// <summary>On avance un peu moins vite dans un canal en eau.</summary>
    public const float CanalMoveCost = 1.5f;

    /// <summary>
    /// Déclenché quand le sol d'une case change d'aspect (minée, noyée, canal creusé ou mis en eau, rivière qui faiblit) ;
    /// sa végétation a pu changer aussi.
    /// </summary>
    public event Action<int, int>? TileChanged;

    /// <summary>
    /// Déclenché quand seule la végétation d'une case change (baies cueillies ou repoussées, arbre coupé ou qui grandit) :
    /// le sol est intact, l'affichage n'a pas à repeindre le terrain.
    /// </summary>
    public event Action<int, int>? FloraChanged;

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
        _fish = new byte[n];
        _river = new bool[n];
        _bank = new bool[n];
        _canal = new byte[n];
        _irrigation = new byte[n];
        _floodLevel = new byte[n];
        _downstream = new int[n];
        Array.Fill(_downstream, -1);
        _riverWidth = new byte[n];
        _moisture = new byte[n];
        _flow = new float[n];
        Array.Fill(_flow, 1f);
        _veins = new uint[n];
        Roads = new RoadLayer(n);
        _terrainStamp = new int[((width + LocalSpatialIndex.RegionSize - 1) / LocalSpatialIndex.RegionSize) * ((height + LocalSpatialIndex.RegionSize - 1) / LocalSpatialIndex.RegionSize)];
    }

    /// <summary>
    /// La couche routière : surface réalisée, trafic et travaux d'aménagement de chaque cellule. Indépendante du type de sol.
    /// </summary>
    public RoadLayer Roads { get; internal set; }

    /// <summary>Déclenché quand la surface d'une cellule change (un sentier apparaît ou s'efface, un chemin est aménagé).</summary>
    public event Action<int, int>? RoadChanged;

    internal void NotifyRoadChanged(int x, int y) => RoadChanged?.Invoke(x, y);

    /// <summary>
    /// Nombre de modifications du relief, de l'eau ou des canaux depuis la fondation, et la dernière révision de chaque région de 16 × 16 cases :
    /// une proposition de site n'est périmée que si le terrain a changé dans la zone qu'elle utilise.
    /// </summary>
    public int TerrainRevision { get; private set; }

    private int[] _terrainStamp = null!;

    private int RegionCount => ((Width + LocalSpatialIndex.RegionSize - 1) / LocalSpatialIndex.RegionSize) * ((Height + LocalSpatialIndex.RegionSize - 1) / LocalSpatialIndex.RegionSize);

    internal bool HasValidTerrainStamps => _terrainStamp is not null && _terrainStamp.Length == RegionCount;

    /// <summary>Complète une carte lue au format v1 : la couche routière et les tampons de terrain n'existaient pas.</summary>
    internal void EnsureV2Data()
    {
        Roads ??= new RoadLayer(Width * Height);
        if (!HasValidTerrainStamps)
            _terrainStamp = new int[RegionCount];
    }

    public int TerrainStampAt(int x, int y) =>
        _terrainStamp[(y / LocalSpatialIndex.RegionSize) * ((Width + LocalSpatialIndex.RegionSize - 1) / LocalSpatialIndex.RegionSize) + x / LocalSpatialIndex.RegionSize];

    private void Stamp(int x, int y)
    {
        TerrainRevision++;
        _terrainStamp[(y / LocalSpatialIndex.RegionSize) * ((Width + LocalSpatialIndex.RegionSize - 1) / LocalSpatialIndex.RegionSize) + x / LocalSpatialIndex.RegionSize] = TerrainRevision;
    }

    /// <summary>
    /// Vrai si aucune région touchée par le rectangle (cases incluses) n'a changé de relief ou d'eau depuis la révision donnée.
    /// </summary>
    public bool TerrainUnchangedSince(int revision, int x0, int y0, int x1, int y1)
    {
        int size = LocalSpatialIndex.RegionSize;
        for (int ry = Math.Max(0, y0) / size; ry <= Math.Min(Height - 1, y1) / size; ry++)
        for (int rx = Math.Max(0, x0) / size; rx <= Math.Min(Width - 1, x1) / size; rx++)
            if (_terrainStamp[ry * ((Width + size - 1) / size) + rx] > revision)
                return false;
        return true;
    }

    /// <summary>Tableaux de travail des recherches sur cette carte (canaux, barrages).</summary>
    internal SearchScratch Scratch => _scratch ??= new SearchScratch(Width * Height);
    private SearchScratch? _scratch;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    private int Index(int x, int y) => y * Width + x;

    public int GetElevation(int x, int y) => _elevation[Index(x, y)];

    /// <summary>Eau profonde, qu'on ne traverse pas : lac, mer, ou étendue retenue par un barrage.</summary>
    public bool IsWater(int x, int y) => _originalElevation[Index(x, y)] <= WaterLevel || _floodLevel[Index(x, y)] != 0;

    /// <summary>Cette case est noyée derrière un barrage.</summary>
    public bool IsFlooded(int x, int y) => InBounds(x, y) && _floodLevel[Index(x, y)] != 0;

    /// <summary>
    /// Hauteur de la surface de l'eau sur cette case : celle de la retenue derrière un barrage, celle du sol
    /// pour une rivière ou un canal en eau. Un canal ne peut être alimenté que par de l'eau au moins aussi haute que lui.
    /// </summary>
    public int WaterHeight(int x, int y) => IsFlooded(x, y) ? _floodLevel[Index(x, y)] : GetElevation(x, y);

    /// <summary>Débit de la rivière sur cette case (1 au naturel).</summary>
    public float GetFlow(int x, int y) => _flow[Index(x, y)];

    /// <summary>La case de rivière vers laquelle l'eau coule depuis (x, y), ou null (embouchure, ou pas une rivière).</summary>
    public (int X, int Y)? RiverDownstream(int x, int y)
    {
        int d = _downstream[Index(x, y)];
        return d < 0 ? null : (d % Width, d / Width);
    }

    /// <summary>Les cases de rivière qui se jettent directement dans (x, y).</summary>
    public IEnumerable<(int X, int Y)> RiverUpstream(int x, int y)
    {
        int self = Index(x, y);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if ((dx != 0 || dy != 0) && InBounds(x + dx, y + dy) && _downstream[Index(x + dx, y + dy)] == self)
                yield return (x + dx, y + dy);
    }

    /// <summary>Un barrage retient l'eau : ces cases sont noyées jusqu'au niveau donné, la végétation y meurt et des poissons s'y installent.</summary>
    public void Flood(IEnumerable<(int X, int Y)> tiles, int level)
    {
        foreach ((int x, int y) in tiles)
        {
            int i = Index(x, y);
            _floodLevel[i] = (byte)level;
            _flora[i] = FloraType.None;
            _floraGrowth[i] = 0f;
            _berries[i] = 0;
            _canal[i] = 0;
            _fish[i] = MaxFish;
            Stamp(x, y);
            TileChanged?.Invoke(x, y);
        }
        ComputeBanks();
    }

    /// <summary>Toutes les rivières de la carte coulent moins fort (un barrage, plus haut sur le même fleuve, retient l'eau).</summary>
    public void ScaleRiverFlows(float factor)
    {
        for (int i = 0; i < _flow.Length; i++)
            if (_river[i])
            {
                _flow[i] *= factor;
                TileChanged?.Invoke(i % Width, i / Width);
            }
    }

    /// <summary>Un barrage fait baisser le débit en aval.</summary>
    public void ReduceFlow(int x, int y, float factor)
    {
        _flow[Index(x, y)] *= factor;
        TileChanged?.Invoke(x, y);
    }

    /// <summary>Une rivière : de l'eau peu profonde, qui se traverse à pied.</summary>
    public bool IsRiver(int x, int y) => _river[Index(x, y)];

    /// <summary>
    /// Largeur du fleuve au droit de cette case, en cases : 1 pour un ruisseau, 3 ou plus pour un fleuve large,
    /// 0 si la case n'est pas une rivière.
    /// </summary>
    public int RiverWidth(int x, int y) => _riverWidth[Index(x, y)];

    /// <summary>Un tronçon de fleuve large (deux cases ou plus), à l'opposé d'un ruisseau d'une case de large.</summary>
    public bool IsWideRiver(int x, int y) => _river[Index(x, y)] && _riverWidth[Index(x, y)] > 1;

    /// <summary>Humidité du sol de 0 (sec) à 1 (détrempé), fixée une fois pour toutes à la génération.</summary>
    public float GetMoisture(int x, int y) => _moisture[Index(x, y)] / 255f;

    /// <summary>Un canal creusé sur cette case, à sec ou en eau.</summary>
    public bool IsCanal(int x, int y) => _canal[Index(x, y)] != 0;

    public bool IsCanalWet(int x, int y) => _canal[Index(x, y)] == 2;

    /// <summary>Rivière ou canal : on n'y bâtit rien et on n'y sème pas.</summary>
    public bool IsWaterway(int x, int y) => IsRiver(x, y) || IsCanal(x, y);

    /// <summary>Un canal en eau coule à moins de trois cases : la terre est irriguée.</summary>
    public bool IsIrrigated(int x, int y) => InBounds(x, y) && _irrigation[Index(x, y)] > 0;

    /// <summary>Les colons creusent un fossé : la végétation de la case est arrachée.</summary>
    public void DigCanal(int x, int y)
    {
        int i = Index(x, y);
        _canal[i] = 1;
        _flora[i] = FloraType.None;
        _berries[i] = 0;
        Stamp(x, y);
        TileChanged?.Invoke(x, y);
    }

    /// <summary>L'eau arrive jusqu'à cette case du canal : elle irrigue les terres alentour.</summary>
    public void FillCanal(int x, int y)
    {
        int i = Index(x, y);
        if (_canal[i] != 1)
            return;
        _canal[i] = 2;
        for (int dy = -IrrigationReach; dy <= IrrigationReach; dy++)
        for (int dx = -IrrigationReach; dx <= IrrigationReach; dx++)
        {
            if (!InBounds(x + dx, y + dy))
                continue;
            int j = Index(x + dx, y + dy);
            if (_irrigation[j] < byte.MaxValue)
                _irrigation[j]++;
        }
        Stamp(x, y);
        TileChanged?.Invoke(x, y);
    }

    /// <summary>De l'eau, profonde (lac, mer) ou courante (rivière).</summary>
    public bool HasWater(int x, int y) => IsWater(x, y) || IsRiver(x, y);

    /// <summary>La terre est riche à moins de deux cases de l'eau.</summary>
    public bool IsFertileBank(int x, int y) => InBounds(x, y) && _bank[Index(x, y)];

    public bool IsMountain(int x, int y) => _originalElevation[Index(x, y)] >= MountainElevation;

    public SoilType GetSoil(int x, int y) => _soil[Index(x, y)];

    public FloraType GetFlora(int x, int y) => _flora[Index(x, y)];

    /// <summary>Croissance de la plante, de 0 (pousse) à 1 (adulte).</summary>
    public float GetFloraGrowth(int x, int y) => _floraGrowth[Index(x, y)];

    public int GetBerries(int x, int y) => _berries[Index(x, y)];

    public int GetFish(int x, int y) => _fish[Index(x, y)];

    /// <summary>Pêche un poisson dans une case d'eau. Renvoie false s'il n'y en a plus.</summary>
    public bool CatchFish(int x, int y)
    {
        int i = Index(x, y);
        if (_fish[i] == 0)
            return false;
        _fish[i]--;
        return true;
    }

    /// <summary>Cueille toutes les baies d'un buisson et renvoie leur nombre.</summary>
    public int HarvestBerries(int x, int y)
    {
        int i = Index(x, y);
        int count = _berries[i];
        if (count == 0)
            return 0;
        _berries[i] = 0;
        FloraChanged?.Invoke(x, y);
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
        FloraChanged?.Invoke(x, y);
    }

    /// <summary>Un arbre doit avoir atteint cette croissance pour être abattu.</summary>
    public const float MinChopGrowth = 0.5f;

    private const float TreeGrowthPerDay = 0.04f;
    private const float StumpRegrowthChancePerDay = 0.03f;

    public bool CanChop(int x, int y) =>
        InBounds(x, y) && GetFlora(x, y) == FloraType.Tree && GetFloraGrowth(x, y) >= MinChopGrowth;

    /// <summary>Abat un arbre, laisse une souche et renvoie la quantité de bois obtenue (plus l'arbre est grand, plus il en donne).</summary>
    public int ChopTree(int x, int y)
    {
        if (!CanChop(x, y))
            throw new InvalidOperationException($"Pas d'arbre à abattre en ({x}, {y}).");
        int i = Index(x, y);
        int wood = (int)MathF.Round(3 + 7 * _floraGrowth[i]);
        _flora[i] = FloraType.Stump;
        _floraGrowth[i] = 0f;
        FloraChanged?.Invoke(x, y);
        return wood;
    }

    /// <summary>
    /// Appelé à chaque nouveau jour : une baie repousse sur chaque buisson, les arbres grandissent,
    /// et quelques souches donnent une jeune pousse. La forêt repousse, mais lentement.
    /// </summary>
    public void DailyUpdate(long day, Season season)
    {
        bool winter = season == Season.Hiver;
        float fishChance = winter ? WinterFishRegrowthChancePerDay : FishRegrowthChancePerDay;

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int i = Index(x, y);
            if (_fish[i] < MaxFish && (IsWater(x, y) || _river[i]) && Noise.Hash01(x, y, (int)day, Seed + 91) < fishChance * _flow[i])
                _fish[i]++;

            switch (_flora[i])
            {
                case FloraType.Bush when !winter && _berries[i] < MaxBerries:
                    _berries[i]++;
                    FloraChanged?.Invoke(x, y);
                    break;
                case FloraType.Tree when _floraGrowth[i] < 1f:
                {
                    // On ne prévient l'affichage que quand l'arbre grandit visiblement.
                    int before = (int)(_floraGrowth[i] * 10);
                    _floraGrowth[i] = MathF.Min(1f, _floraGrowth[i] + TreeGrowthPerDay);
                    if ((int)(_floraGrowth[i] * 10) != before)
                        FloraChanged?.Invoke(x, y);
                    break;
                }
                case FloraType.Stump when Noise.Hash01(x, y, (int)day, Seed + 77) < StumpRegrowthChancePerDay:
                    _flora[i] = FloraType.Tree;
                    _floraGrowth[i] = 0.05f;
                    FloraChanged?.Invoke(x, y);
                    break;
            }
        }
    }

    // --- Déplacements ---

    /// <summary>On peut marcher partout sauf dans l'eau.</summary>
    public bool IsWalkable(int x, int y) => InBounds(x, y) && !IsWater(x, y);

    /// <summary>
    /// Peut-on passer d'une case voisine à l'autre ? Une marche d'un niveau se monte ou se descend,
    /// une falaise de deux niveaux ou plus est infranchissable. <paramref name="maxStep"/> vaut 2
    /// seulement pour escalader hors d'un trou où l'on serait coincé.
    /// </summary>
    public bool CanStep(int fromX, int fromY, int toX, int toY, int maxStep = 1) =>
        IsWalkable(toX, toY) && Math.Abs(GetElevation(toX, toY) - GetElevation(fromX, fromY)) <= maxStep;

    /// <summary>La même règle que <see cref="CanStep"/>, sur les indices de deux cases déjà dans la carte : le pathfinder l'appelle des millions de fois.</summary>
    internal bool CanStepCell(int from, int to, int maxStep) =>
        _originalElevation[to] > WaterLevel && _floodLevel[to] == 0 && Math.Abs(_elevation[to] - _elevation[from]) <= maxStep;

    internal int ElevationCell(int cell) => _elevation[cell];

    internal bool IsWalkableCell(int cell) => _originalElevation[cell] > WaterLevel && _floodLevel[cell] == 0;

    /// <summary>
    /// L'irrégularité du sol, de 0,94 à 1,06 (moyenne 1) : un petit relief de quelques cases, fixé par la graine de la carte. Elle multiplie le coût d'un pas ; les sentiers
    /// et les tracés d'accès épousent ainsi le terrain au lieu de tirer des lignes droites, et un même trajet reste le même d'une fois à l'autre.
    /// </summary>
    public float Ruggedness(int x, int y) => RuggednessCell(y * Width + x);

    internal float RuggednessCell(int cell)
    {
        byte[] table = _ruggedness ??= BuildRuggedness();
        return RuggednessMin + (RuggednessMax - RuggednessMin) * (table[cell] / 255f);
    }

    public const float RuggednessMin = 0.94f;
    public const float RuggednessMax = 1.06f;

    [NonSerialized] private byte[]? _ruggedness;

    private byte[] BuildRuggedness()
    {
        const int scale = 4;
        var table = new byte[Width * Height];
        uint seed = unchecked((uint)Seed) ^ 0x6B8B4567u;
        float Lattice(int gx, int gy) => (Colonies.SettlementRules.Mix(seed, gx, gy) >> 8) / (float)(1 << 24);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int gx = x / scale, gy = y / scale;
            float fx = (x % scale) / (float)scale, fy = (y % scale) / (float)scale;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Lattice(gx, gy), b = Lattice(gx + 1, gy), c = Lattice(gx, gy + 1), d = Lattice(gx + 1, gy + 1);
            float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
            table[y * Width + x] = (byte)Math.Clamp((int)MathF.Round((top + (bottom - top) * fy) * 255f), 0, 255);
        }
        return table;
    }

    /// <summary>Coût de traversée d'une case : on avance moins vite en forêt.</summary>
    public float MoveCost(int x, int y) => MoveCostCell(Index(x, y));

    internal float MoveCostCell(int cell) =>
        _flora[cell] == FloraType.Tree ? 1.6f : _river[cell] ? FordCostCell(cell) : _canal[cell] == 2 ? CanalMoveCost : 1f;

    /// <summary>
    /// Le coût du gué : on avance trois fois moins vite dans une rivière, six fois moins dans un grand fleuve, sauf sur une case de pont
    /// (<see cref="Colonies.RoadSurface.Bridge"/>), qui ne ralentit plus personne.
    /// </summary>
    public float FordCost(int x, int y) => FordCostCell(Index(x, y));

    private float FordCostCell(int cell) =>
        Roads is { } roads && roads.SurfaceAt(cell) == Colonies.RoadSurface.Bridge ? 1f : _river[cell] && _riverWidth[cell] > 1 ? WideRiverMoveCost : RiverMoveCost;

    /// <summary>Le coût d'une case de rivière à gué, et celui d'un grand fleuve.</summary>
    public const float RiverMoveCost = 3f;
    public const float WideRiverMoveCost = 6f;

    /// <summary>Matière de la couche numéro <paramref name="level"/> (0 = tout en bas) de la case.</summary>
    public Material MaterialAt(int x, int y, int level)
    {
        int original = _originalElevation[Index(x, y)];
        bool isPlain = original < MountainElevation;
        if (isPlain && level >= original - SoilThickness)
            return Material.Soil;
        return IsIronVein(x, y, level) ? Material.IronOre : Material.Stone;
    }

    /// <summary>
    /// Combien de couches faut-il retirer pour atteindre un filon de fer sous cette case ? 0 si le filon affleure,
    /// <see cref="int.MaxValue"/> s'il n'y en a pas dans les <paramref name="maxDepth"/> premières couches (ni de quoi creuser plus bas).
    /// C'est le flair des mineurs : ils creusent là où le minerai est proche.
    /// </summary>
    public int DepthToOre(int x, int y, int maxDepth)
    {
        int top = GetElevation(x, y) - 1;
        for (int depth = 0; depth < maxDepth; depth++)
        {
            int layer = top - depth;
            if (layer < MinMiningElevation)
                break;
            if (MaterialAt(x, y, layer) == Material.IronOre)
                return depth;
        }
        return int.MaxValue;
    }

    /// <summary>Richesse du sol cultivable (1 au naturel) : les terres de montagne rendent moins aux semailles.</summary>
    public float SoilRichness { get; internal set; } = 1f;

    /// <summary>Le biome de la case du monde d'où vient cette carte (l'affichage peut s'en servir pour teinter le sol).</summary>
    public World.Biome Biome { get; internal set; } = World.Biome.TemperateForest;

    public Material TopMaterial(int x, int y) => MaterialAt(x, y, GetElevation(x, y) - 1);

    public Surface GetSurface(int x, int y)
    {
        if (IsWater(x, y))
            return Surface.Water;
        if (IsRiver(x, y) || IsCanalWet(x, y))
            return Surface.River;
        if (IsCanal(x, y))
            return Surface.Dirt;
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
        && !IsRiver(x, y)
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
        Stamp(x, y);
        TileChanged?.Invoke(x, y);
        return extracted;
    }

    /// <summary>
    /// Les filons de fer forment des veines en 3D à l'intérieur de la roche. Le bruit qui les dessine coûte cher et ne change
    /// jamais : chaque couche n'est calculée qu'une fois, puis gardée dans <see cref="_veins"/> (deux bits par couche :
    /// « déjà calculée », puis « filon »). Une couche perdue par deux calculs simultanés (peinture en parallèle) est
    /// simplement recalculée : la réponse est toujours la même.
    /// </summary>
    private bool IsIronVein(int x, int y, int level)
    {
        if ((uint)level >= MaxElevation)
            return ComputeIronVein(x, y, level);
        int i = Index(x, y);
        uint veins = _veins[i];
        uint known = 1u << (2 * level), vein = 2u << (2 * level);
        if ((veins & known) == 0)
        {
            veins |= known | (ComputeIronVein(x, y, level) ? vein : 0u);
            _veins[i] = veins;
        }
        return (veins & vein) != 0;
    }

    private bool ComputeIronVein(int x, int y, int level) =>
        Noise.Fractal3D(x * 0.11f, y * 0.11f, level * 0.45f, Seed + 500, 3) > 0.64f;

    /// <summary>Utilisé par le générateur : trace une rivière sur la case (sans plante, poissonneuse).</summary>
    internal void SetRiver(int x, int y, int downX, int downY, int width = 1)
    {
        int i = Index(x, y);
        _river[i] = true;
        _riverWidth[i] = (byte)width;
        _downstream[i] = InBounds(downX, downY) ? Index(downX, downY) : -1;
        _flora[i] = FloraType.None;
        _floraGrowth[i] = 0f;
        _berries[i] = 0;
        _fish[i] = MaxFish;
    }

    /// <summary>Utilisé par le générateur, une fois l'eau en place : repère les berges fertiles.</summary>
    internal void ComputeBanks()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            bool near = false;
            for (int dy = -BankReach; dy <= BankReach && !near; dy++)
            for (int dx = -BankReach; dx <= BankReach && !near; dx++)
                near = InBounds(x + dx, y + dy) && HasWater(x + dx, y + dy);
            _bank[Index(x, y)] = near && !HasWater(x, y);
        }
    }

    // Utilisé uniquement par le générateur de carte.
    internal void SetGenerated(int x, int y, int elevation, SoilType soil, FloraType flora, float growth, float moisture = 0.5f)
    {
        int i = Index(x, y);
        _moisture[i] = (byte)Math.Clamp((int)MathF.Round(moisture * 255f), 0, 255);
        _elevation[i] = (byte)elevation;
        _originalElevation[i] = (byte)elevation;
        _soil[i] = soil;
        _flora[i] = flora;
        _floraGrowth[i] = growth;
        _berries[i] = flora == FloraType.Bush ? (byte)MaxBerries : (byte)0;
        _fish[i] = elevation <= WaterLevel ? (byte)MaxFish : (byte)0;
    }
}
