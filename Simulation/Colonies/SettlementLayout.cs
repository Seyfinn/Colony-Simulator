namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'état du développement d'un village : ses quartiers, ses parcelles, ses projets et ses tracés. Il décrit <b>où</b> et <b>pourquoi</b> ;
/// les objets (<see cref="Building"/>, <see cref="Field"/>, <see cref="Canal"/>) restent la source de vérité de ce qui existe, et la couche routière
/// de la carte celle de ce qui est réalisé. Tout ce qui est dérivé (occupation de la grille, entrées, services) se reconstruit au chargement.
/// </summary>
public sealed class SettlementLayout
{
    internal SettlementLayout(uint seed, int mapWidth, int mapHeight)
    {
        Seed = seed;
        RulesVersion = SettlementRules.Version;
        Width = mapWidth;
        Height = mapHeight;
    }

    /// <summary>La graine spatiale, tirée de données immuables (graine de la carte et position du camp) : jamais de hasard partagé.</summary>
    public uint Seed { get; }

    /// <summary>Version des règles avec lesquelles les décisions ont été prises.</summary>
    public int RulesVersion { get; internal set; }

    /// <summary>Dimensions de la carte, pour décoder les index de cellule (<c>y × Width + x</c>).</summary>
    public int Width { get; }
    public int Height { get; }

    public List<District> Districts { get; } = [];
    public List<PlotReservation> Parcels { get; } = [];
    public List<DevelopmentProject> Projects { get; } = [];
    public List<RoadSegment> RoadSegments { get; } = [];

    /// <summary>Les compteurs d'identifiants : déterministes, jamais dépendants de l'ordre d'itération d'un dictionnaire.</summary>
    internal int NextDistrictId { get; set; } = 1;
    internal int NextParcelId { get; set; } = 1;
    internal int NextProjectId { get; set; } = 1;
    internal int NextSegmentId { get; set; } = 1;

    /// <summary>Identifiant local des bâtiments et des champs (un seul compteur pour les deux).</summary>
    internal int NextObjectId { get; set; } = 1;

    /// <summary>Révision spatiale : elle augmente à chaque changement d'occupation, de parcelle ou de tracé. Elle sert à périmer les propositions.</summary>
    public int Revision { get; internal set; }

    /// <summary>
    /// L'état des révisions d'occupation de la grille (le compteur, la révision et la signature de chaque région de 16 × 16 cases) : le cache de la grille se reconstruit
    /// au chargement, mais ses numéros de révision servent à périmer des propositions et des recherches sauvegardées ; ils doivent donc survivre au rechargement.
    /// </summary>
    internal int IndexRevision { get; set; }
    internal int[] IndexRegionRevision { get; set; } = [];
    internal uint[] IndexRegionSignature { get; set; } = [];

    /// <summary>Dernière ouverture d'un quartier de confort (ticks), pour espacer les nouveaux quartiers.</summary>
    internal long LastDistrictOpenedTicks { get; set; } = long.MinValue / 2;

    /// <summary>Cellules de la place initiale (la clairière et ses accès), protégées de toute construction.</summary>
    public List<int> PlazaCells { get; } = [];

    /// <summary>Les écarts hérités d'une ancienne sauvegarde (accès refusé par les règles actuelles, etc.), pour le diagnostic.</summary>
    public List<string> Anomalies { get; } = [];

    /// <summary>Les besoins persistants qui attendent un site (voir <see cref="SettlementPlanner"/>).</summary>
    public List<PlanRequest> Requests { get; } = [];

    internal int Cell(int x, int y) => y * Width + x;
    internal (int X, int Y) Decode(int cell) => (cell % Width, cell / Width);
    internal bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public District? DistrictById(int id)
    {
        foreach (District district in Districts)
            if (district.Id == id)
                return district;
        return null;
    }

    public PlotReservation? ParcelById(int id)
    {
        foreach (PlotReservation parcel in Parcels)
            if (parcel.Id == id)
                return parcel;
        return null;
    }

    public DevelopmentProject? ProjectById(int id)
    {
        foreach (DevelopmentProject project in Projects)
            if (project.Id == id)
                return project;
        return null;
    }

    public RoadSegment? SegmentById(int id)
    {
        foreach (RoadSegment segment in RoadSegments)
            if (segment.Id == id)
                return segment;
        return null;
    }

    /// <summary>Les parcelles qui occupent ou réservent encore du terrain.</summary>
    public IEnumerable<PlotReservation> ActiveParcels => Parcels.Where(p => p.State != ReservationState.Released);

    public IEnumerable<PlotReservation> ParcelsOf(District district) => ActiveParcels.Where(p => p.DistrictId == district.Id);

    /// <summary>Le premier quartier de cette vocation (dans l'ordre de création), s'il existe.</summary>
    public District? FirstOf(DistrictKind kind)
    {
        foreach (District district in Districts)
            if (district.Kind == kind)
                return district;
        return null;
    }

    /// <summary>
    /// L'emprise d'un quartier, dérivée des objets réellement associés (jamais mémorisée) : le rectangle englobant de ses parcelles
    /// occupées ou réservées. Un quartier encore vide se réduit à son ancre.
    /// </summary>
    public (int MinX, int MinY, int MaxX, int MaxY) EnvelopeOf(District district)
    {
        int minX = district.AnchorX, minY = district.AnchorY, maxX = district.AnchorX, maxY = district.AnchorY;
        foreach (PlotReservation parcel in ParcelsOf(district))
        {
            if (parcel.Kind == ParcelKind.AccessCorridor)
                continue;
            minX = Math.Min(minX, parcel.X);
            minY = Math.Min(minY, parcel.Y);
            maxX = Math.Max(maxX, parcel.X + parcel.Width - 1);
            maxY = Math.Max(maxY, parcel.Y + parcel.Height - 1);
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>Le nombre de parcelles de bâtiment ou de champ d'un quartier, occupées ou réservées.</summary>
    public int LoadOf(District district) => ParcelsOf(district).Count(p => p.Kind is ParcelKind.Building or ParcelKind.Field);
}
