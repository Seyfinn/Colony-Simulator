using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Map;

/// <summary>Ce qui occupe ou réserve une cellule. Plusieurs drapeaux peuvent se superposer (une cour peut toucher un corridor).</summary>
[Flags]
public enum CellUse : byte
{
    None = 0,
    /// <summary>L'emprise d'un bâtiment (ou d'un barrage) : les chemins extérieurs la contournent.</summary>
    Building = 1,
    Field = 2,
        /// <summary>Un canal creusé ou prévu.</summary>
    Canal = 8,
    /// <summary>La place initiale : la clairière du feu et ses accès.</summary>
    Plaza = 16,
    /// <summary>Un tronçon d'accès réservé.</summary>
    Corridor = 32,
    /// <summary>La marge libre d'une parcelle (sa cour).</summary>
    Courtyard = 64,
    /// <summary>Un espace public secondaire.</summary>
    PublicSpace = 128,
}

/// <summary>
/// L'occupation de la grille d'une colonie, reconstruite à volonté depuis les objets de la simulation (bâtiments, champs, tombes, canaux et plan du village) :
/// c'est un cache, jamais une source de vérité, et il n'est pas sauvegardé. Un seul ensemble de règles d'occupation sert la proposition de site, le
/// pathfinder et la vérification du mouvement.
/// Les emprises d'une parcelle ancienne (<see cref="PlotReservation.LegacyOpen"/>) restent traversables, comme avant.
/// </summary>
public sealed class LocalSpatialIndex
{
    public const int RegionSize = 16;

    private readonly int _width, _height;
    private readonly CellUse[] _use;
    private readonly int[] _owner;
    private readonly byte[] _legacy;
    private readonly int[] _regionRevision;
    private readonly int _regionsX;

    // L'état dont le cache a été tiré : un changement de l'un d'eux le périme.
    private int _buildings = -1, _fields = -1, _canals = -1, _layoutRevision = -1, _parcels = -1, _segments = -1;

    internal LocalSpatialIndex(int width, int height)
    {
        _width = width;
        _height = height;
        _use = new CellUse[width * height];
        _owner = new int[width * height];
        _legacy = new byte[width * height];
        _regionsX = (width + RegionSize - 1) / RegionSize;
        _regionRevision = new int[_regionsX * ((height + RegionSize - 1) / RegionSize)];
    }

    /// <summary>Compte les reconstructions et les changements d'occupation : une proposition évaluée à une autre révision doit être revalidée.</summary>
    public int Revision { get; private set; }

    /// <summary>Révision de la région de 16 × 16 cases qui contient la cellule : elle ne change que si l'occupation y change.</summary>
    public int RegionRevision(int x, int y) => _regionRevision[(y / RegionSize) * _regionsX + x / RegionSize];

    public CellUse UseAt(int x, int y) => _use[y * _width + x];
    public CellUse UseAt(int cell) => _use[cell];

    public bool Has(int x, int y, CellUse use) => (uint)x < (uint)_width && (uint)y < (uint)_height && (_use[y * _width + x] & use) != 0;

    /// <summary>Le propriétaire (identifiant du bâtiment ou du champ) de la cellule bâtie ou cultivée, -1 sinon.</summary>
    public int OwnerAt(int x, int y) => (_use[y * _width + x] & (CellUse.Building | CellUse.Field)) != 0 ? _owner[y * _width + x] : -1;

    /// <summary>L'emprise d'un bâtiment à la porte duquel on ne peut entrer que par l'accès : un obstacle pour les chemins extérieurs.</summary>
    public bool BlocksWalking(int x, int y)
    {
        int i = y * _width + x;
        return (_use[i] & CellUse.Building) != 0 && _legacy[i] == 0;
    }

    public bool BlocksWalking(int cell) => (_use[cell] & CellUse.Building) != 0 && _legacy[cell] == 0;

    public bool IsLegacyOpen(int x, int y) => _legacy[y * _width + x] != 0;

    /// <summary>La cellule est occupée par quelque chose qui interdit d'y poser un bâtiment (emprise, champ, canal, place, espace public).</summary>
    public bool IsSolid(int x, int y) =>
        (_use[y * _width + x] & (CellUse.Building | CellUse.Field | CellUse.Canal | CellUse.Plaza | CellUse.PublicSpace)) != 0;

    /// <summary>
    /// Les cellules de la grille ont-elles changé depuis le dernier calcul ? Reconstruit le cache si un objet a été ajouté ou retiré :
    /// c'est ce qui garde la grille juste même quand un test ou un outil modifie directement les listes de la colonie.
    /// </summary>
    internal void Refresh(Colony colony)
    {
        SettlementLayout? layout = colony.Layout;
        int buildings = colony.Buildings.Count, fields = colony.Fields.Count, canals = colony.CanalTiles.Count;
        int revision = layout?.Revision ?? 0, parcels = layout?.Parcels.Count ?? 0, segments = layout?.RoadSegments.Count ?? 0;
        if (buildings == _buildings && fields == _fields && canals == _canals
            && revision == _layoutRevision && parcels == _parcels && segments == _segments)
            return;
        Rebuild(colony);
        (_buildings, _fields, _canals, _layoutRevision, _parcels, _segments) =
            (buildings, fields, canals, revision, parcels, segments);
    }

    internal void Rebuild(Colony colony)
    {
        Array.Clear(_use);
        Array.Clear(_legacy);
        Array.Fill(_owner, -1);
        SettlementLayout? layout = colony.Layout;

        foreach (Building building in colony.Buildings)
        {
            bool legacy = layout?.ParcelById(building.ParcelId) is { LegacyOpen: true };
            foreach ((int x, int y) in building.Tiles)
                Mark(x, y, CellUse.Building, building.Id, legacy);
        }
        foreach (Field field in colony.Fields)
            for (int y = field.Y; y < field.Y + field.Size; y++)
            for (int x = field.X; x < field.X + field.Size; x++)
                Mark(x, y, CellUse.Field, field.Id, false);
        foreach ((int x, int y) in colony.CanalTiles)
            Mark(x, y, CellUse.Canal, -1, false);

        if (layout is not null)
        {
            foreach (int cell in layout.PlazaCells)
                _use[cell] |= CellUse.Plaza;
            foreach (PlotReservation parcel in layout.Parcels)
            {
                if (parcel.State == ReservationState.Released)
                    continue;
                if (parcel.Kind == ParcelKind.PublicSpace)
                    foreach ((int x, int y) in parcel.Tiles)
                        Mark(x, y, CellUse.PublicSpace, -1, false);
                else if (parcel.Kind == ParcelKind.Building)
                    MarkCourtyard(parcel);
            }
            foreach (RoadSegment segment in layout.RoadSegments)
                foreach (int cell in segment.Cells)
                    if (cell >= 0 && cell < _use.Length)
                        _use[cell] |= CellUse.Corridor;
        }
        UpdateRegionRevisions(layout);
    }

    /// <summary>
    /// Signature du contenu de chaque région de 16 × 16 cases : après une reconstruction, seules les régions dont le contenu a réellement
    /// changé voient leur révision avancer (une nouvelle hutte invalide ses abords, pas toute la carte).
    /// </summary>
    private void UpdateRegionRevisions(SettlementLayout? layout)
    {
        var signatures = new uint[_regionRevision.Length];
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width; x++)
        {
            int i = y * _width + x;
            uint cell = (uint)_use[i] | ((uint)_legacy[i] << 8) | ((uint)(_owner[i] + 1) << 9);
            ref uint signature = ref signatures[(y / RegionSize) * _regionsX + x / RegionSize];
            signature = signature * 31u + cell + 1u;
        }
        // Après un chargement, on repart des numéros sauvegardés : une région qui n'a pas changé garde sa révision.
        if (_regionSignature is null && layout is { } saved && saved.IndexRegionSignature.Length == signatures.Length && saved.IndexRegionRevision.Length == signatures.Length)
        {
            _regionSignature = (uint[])saved.IndexRegionSignature.Clone();
            Array.Copy(saved.IndexRegionRevision, _regionRevision, signatures.Length);
            Revision = saved.IndexRevision;
        }
        bool first = _regionSignature is null;
        int changedRevision = 0;
        for (int r = 0; r < signatures.Length; r++)
        {
            if (!first && _regionSignature![r] == signatures[r])
                continue;
            if (changedRevision == 0)
                changedRevision = ++Revision;
            _regionRevision[r] = changedRevision;
        }
        _regionSignature = signatures;
        if (layout is not null)
        {
            layout.IndexRevision = Revision;
            layout.IndexRegionRevision = (int[])_regionRevision.Clone();
            layout.IndexRegionSignature = (uint[])signatures.Clone();
        }
    }

    private uint[]? _regionSignature;

    private void Mark(int x, int y, CellUse use, int owner, bool legacy)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            return;
        int i = y * _width + x;
        _use[i] |= use;
        if (use is CellUse.Building or CellUse.Field)
            _owner[i] = owner;
        if (legacy && use == CellUse.Building)
            _legacy[i] = 1;
    }

    private void MarkCourtyard(PlotReservation parcel)
    {
        for (int y = parcel.Y - parcel.Margin; y < parcel.Y + parcel.Height + parcel.Margin; y++)
        for (int x = parcel.X - parcel.Margin; x < parcel.X + parcel.Width + parcel.Margin; x++)
            if ((uint)x < (uint)_width && (uint)y < (uint)_height && !parcel.Contains(x, y))
                _use[y * _width + x] |= CellUse.Courtyard;
    }

    /// <summary>Vrai si aucune région touchée par le rectangle n'a changé d'occupation depuis la révision donnée.</summary>
    public bool UnchangedSince(int revision, int x0, int y0, int x1, int y1)
    {
        for (int ry = Math.Max(0, y0) / RegionSize; ry <= Math.Min(_height - 1, y1) / RegionSize; ry++)
        for (int rx = Math.Max(0, x0) / RegionSize; rx <= Math.Min(_width - 1, x1) / RegionSize; rx++)
            if (_regionRevision[ry * _regionsX + rx] > revision)
                return false;
        return true;
    }

    /// <summary>Marque qu'une région a changé (un changement local invalide ses abords, pas toute la carte).</summary>
    internal void Touch(int x0, int y0, int x1, int y1)
    {
        Revision++;
        for (int ry = Math.Max(0, y0) / RegionSize; ry <= Math.Min(_height - 1, y1) / RegionSize; ry++)
        for (int rx = Math.Max(0, x0) / RegionSize; rx <= Math.Min(_width - 1, x1) / RegionSize; rx++)
            _regionRevision[ry * _regionsX + rx] = Revision;
    }
}
