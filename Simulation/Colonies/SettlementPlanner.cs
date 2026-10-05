using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'orchestration de l'urbanisme : créer le plan d'un village à la fondation, y enregistrer chaque bâtiment ou champ avec son quartier, sa
/// parcelle et sa porte, décider d'une densification ou d'une expansion, accepter un projet d'un seul bloc ou le refuser sans effet partiel.
/// Le calcul d'un emplacement (<see cref="SitePlanner"/>) ne modifie jamais rien ; seule l'admission écrit dans le monde.
/// </summary>
public static partial class SettlementPlanner
{
    /// <summary>Côté de la place initiale protégée autour du feu : la clairière de 3 × 3 et son anneau d'accès.</summary>
    private const int PlazaRadius = 2;

    /// <summary>Un objet qui n'est qu'à cette distance (en cases) de l'enveloppe d'un quartier compatible lui est rattaché.</summary>
    private const int AdoptionReach = 14;

    /// <summary>
    /// Crée le plan d'un village tout neuf : la graine spatiale, le quartier civique autour de la clairière réellement occupée, la place et ses accès.
    /// Appelé une seule fois par fondation (y compris pour un schisme). Aucune industrie fictive, aucun champ hors saison, aucune route complète.
    /// </summary>
    internal static SettlementLayout InitializeLayout(Colony colony, LocalMap map)
    {
        uint seed = SettlementRules.Mix(unchecked((uint)map.Seed), colony.CampX, colony.CampY, SettlementRules.Version);
        var layout = new SettlementLayout(seed, map.Width, map.Height);
        colony.Layout = layout;
        layout.Districts.Add(new District(layout.NextDistrictId++, DistrictKind.Civic, colony.CampX, colony.CampY, colony.Clock.Ticks, -1));
        for (int dy = -PlazaRadius; dy <= PlazaRadius; dy++)
        for (int dx = -PlazaRadius; dx <= PlazaRadius; dx++)
            if (map.InBounds(colony.CampX + dx, colony.CampY + dy))
                layout.PlazaCells.Add(layout.Cell(colony.CampX + dx, colony.CampY + dy));
        layout.Districts[0].Status = DistrictStatus.Active;
        layout.Revision++;
        return layout;
    }

    /// <summary>
    /// Met le plan en accord avec les objets de la colonie : tout bâtiment ou champ sans identifiant (posé par une migration, un outil ou un test
    /// qui a modifié directement les listes) reçoit le sien, son quartier, sa parcelle et sa porte ; une parcelle dont l'occupant a disparu est libérée ;
    /// une parcelle dont l'occupant est achevé devient <see cref="ReservationState.Occupied"/>. Les identifiants sont attribués dans l'ordre des listes.
    /// </summary>
    internal static void Sync(Colony colony)
    {
        SettlementLayout layout = colony.Layout ?? InitializeLayout(colony, colony.Map);
        foreach (Building building in colony.Buildings)
            if (building.Id == 0)
                Adopt(colony, building);
        foreach (Field field in colony.Fields)
            if (field.Id == 0)
                Adopt(colony, field);

        foreach (PlotReservation parcel in layout.Parcels)
        {
            if (parcel.State == ReservationState.Released || parcel.Kind is ParcelKind.PublicSpace or ParcelKind.AccessCorridor)
                continue;
            bool exists;
            bool complete;
            if (parcel.Kind == ParcelKind.Building)
            {
                Building? building = colony.BuildingById(parcel.OccupantId);
                exists = building is not null;
                complete = building?.IsComplete == true;
            }
            else
            {
                exists = colony.FieldById(parcel.OccupantId) is not null;
                complete = exists;
            }

            if (!exists)
                Release(colony, parcel);
            else if (complete && parcel.State == ReservationState.Reserved)
            {
                parcel.State = ReservationState.Occupied;
                layout.Revision++;
            }
        }
    }

    /// <summary>Un bâtiment qui apparaît hors du circuit des propositions (migration, outil de développement) : on lui trouve sa place dans le plan.</summary>
    internal static void Adopt(Colony colony, Building building)
    {
        SettlementLayout layout = colony.Layout ?? InitializeLayout(colony, colony.Map);
        if (building.Id == 0)
            building.Id = layout.NextObjectId++;
        if (layout.ParcelById(building.ParcelId) is not null)
            return;

        BuildingPlacementProfile profile = BuildingPlacementProfile.For(building.Type);
        District? district = building.IsExtension ? layout.DistrictById(colony.BuildingById(building.ExtensionOfId)?.DistrictId ?? -1)
            : building.IsDam ? null : DistrictFor(layout, profile, building.X, building.Y, building.Width, building.Height);
        district ??= building.IsDam ? null : OpenDistrict(colony, layout, profile.Preferred, building.X, building.Y);

        var parcel = new PlotReservation(layout.NextParcelId++, district?.Id ?? -1, ParcelKind.Building,
            building.X, building.Y, building.Width, building.Height, colony.Clock.Ticks)
        {
            OccupantId = building.Id,
            BuildingType = building.Type,
            State = building.IsComplete ? ReservationState.Occupied : ReservationState.Reserved,
        };
        layout.Parcels.Add(parcel);
        building.ParcelId = parcel.Id;
        building.DistrictId = parcel.DistrictId;
        AssignDoor(colony, layout, building, parcel);
        Touch(layout, district, colony);
    }

    internal static void Adopt(Colony colony, Field field)
    {
        SettlementLayout layout = colony.Layout ?? InitializeLayout(colony, colony.Map);
        if (field.Id == 0)
            field.Id = layout.NextObjectId++;
        if (layout.ParcelById(field.ParcelId) is not null)
            return;

        District? district = DistrictFor(layout, null, field.X, field.Y, Field.Size, Field.Size, DistrictKind.Agricultural)
            ?? OpenDistrict(colony, layout, DistrictKind.Agricultural, field.X, field.Y);
        var parcel = new PlotReservation(layout.NextParcelId++, district.Id, ParcelKind.Field, field.X, field.Y, Field.Size, Field.Size, colony.Clock.Ticks)
        {
            OccupantId = field.Id,
            State = ReservationState.Occupied,
            Margin = 0,
        };
        layout.Parcels.Add(parcel);
        field.ParcelId = parcel.Id;
        field.DistrictId = district.Id;
        AssignFieldAccess(colony, layout, field, parcel);
        Touch(layout, district, colony);
    }

    /// <summary>Un objet disparaît (incendie, démolition, annulation) : sa parcelle est libérée ; ses accès partagés restent.</summary>
    internal static void Release(Colony colony, PlotReservation parcel)
    {
        SettlementLayout layout = colony.Layout;
        parcel.State = ReservationState.Released;
        parcel.OccupantId = -1;
        layout.Revision++;
        foreach (PlanRequest request in layout.Requests)
            request.Seen |= RetryEvents.Occupancy;
    }

    private static void Touch(SettlementLayout layout, District? district, Colony colony)
    {
        layout.Revision++;
        if (district is null)
            return;
        district.LastExpandedTicks = colony.Clock.Ticks;
        district.Status = DistrictStatus.Active;
    }

    // --- Quartiers ---

    /// <summary>La distance (Chebyshev, en cases) d'un point à l'enveloppe d'un quartier : 0 à l'intérieur.</summary>
    internal static int DistanceToDistrict(SettlementLayout layout, District district, int x, int y, int width, int height)
    {
        (int minX, int minY, int maxX, int maxY) = layout.EnvelopeOf(district);
        int dx = Math.Max(0, Math.Max(minX - (x + width - 1), x - maxX));
        int dy = Math.Max(0, Math.Max(minY - (y + height - 1), y - maxY));
        return Math.Max(dx, dy);
    }

    /// <summary>Le quartier existant qui accueille le mieux cet emplacement (la vocation préférée avant les vocations acceptées, puis le plus proche), ou null.</summary>
    internal static District? DistrictFor(SettlementLayout layout, BuildingPlacementProfile? profile, int x, int y, int width, int height, DistrictKind? only = null)
    {
        District? best = null;
        int bestRank = int.MaxValue;
        foreach (District district in layout.Districts)
        {
            bool preferred = only is { } kind ? district.Kind == kind : district.Kind == profile!.Preferred;
            if (!preferred && (only is not null || !profile!.Accepts(district.Kind)))
                continue;
            int distance = DistanceToDistrict(layout, district, x, y, width, height);
            if (distance > AdoptionReach)
                continue;
            int rank = (preferred ? 0 : 1000) + distance * 4 + district.Id % 4;
            if (rank < bestRank)
            {
                bestRank = rank;
                best = district;
            }
        }
        return best;
    }

    /// <summary>L'identifiant du quartier existant le plus proche d'un point (-1 s'il n'y en a aucun).</summary>
    internal static int NearestDistrictId(SettlementLayout layout, int x, int y)
    {
        int best = -1, bestDistance = int.MaxValue;
        foreach (District district in layout.Districts)
        {
            int distance = Math.Max(Math.Abs(district.AnchorX - x), Math.Abs(district.AnchorY - y));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = district.Id;
            }
        }
        return best;
    }

    /// <summary>Un quartier vient de naître avec son premier projet : sa vocation, son ancre, et le quartier dont il est issu (le plus proche).</summary>
    internal static District OpenDistrict(Colony colony, SettlementLayout layout, DistrictKind kind, int anchorX, int anchorY, int parentId = int.MinValue)
    {
        District? parent = parentId == int.MinValue ? null : layout.DistrictById(parentId);
        int bestDistance = int.MaxValue;
        if (parentId == int.MinValue)
            foreach (District other in layout.Districts)
            {
                int distance = Math.Max(Math.Abs(other.AnchorX - anchorX), Math.Abs(other.AnchorY - anchorY));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    parent = other;
                }
            }
        var district = new District(layout.NextDistrictId++, kind, anchorX, anchorY, colony.Clock.Ticks, parent?.Id ?? -1);
        layout.Districts.Add(district);
        layout.LastDistrictOpenedTicks = colony.Clock.Ticks;
        layout.Revision++;
        return district;
    }

    // --- Portes et accès ---

    /// <summary>Les huit cases d'accès possibles d'une emprise rectangulaire : quatre côtés, chacun avec ses cases voisines de l'emprise.</summary>
    internal static IEnumerable<(int EntryX, int EntryY, int AccessX, int AccessY, int Side)> DoorCandidates(int x, int y, int width, int height)
    {
        for (int i = 0; i < width; i++)
        {
            yield return (x + i, y + height - 1, x + i, y + height, 0);
            yield return (x + i, y, x + i, y - 1, 1);
        }
        for (int j = 0; j < height; j++)
        {
            yield return (x, y + j, x - 1, y + j, 2);
            yield return (x + width - 1, y + j, x + width, y + j, 3);
        }
    }

    /// <summary>
    /// Choisit la porte d'un objet déjà posé : la case d'accès praticable, hors de toute emprise, qui fait face au cœur du village. Sans porte
    /// possible, la parcelle reste traversable comme avant et l'anomalie est notée.
    /// </summary>
    private static void AssignDoor(Colony colony, SettlementLayout layout, Building building, PlotReservation parcel)
    {
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        if (building.IsExtension && building.HasDoor)
        {
            parcel.EntryX = building.EntryX; parcel.EntryY = building.EntryY;
            parcel.AccessX = building.AccessX; parcel.AccessY = building.AccessY;
            ReserveCell(layout, parcel, building.AccessX, building.AccessY);
            return;
        }
        if (building.IsDam)
        {
            // Le barrage se travaille depuis la berge : une case sèche voisine tient lieu de cellule de travail.
            foreach (var door in DoorCandidates(building.X, building.Y, building.Width, building.Height))
            {
                int ax = door.AccessX, ay = door.AccessY;
                if (map.InBounds(ax, ay) && map.IsWalkable(ax, ay) && !map.IsRiver(ax, ay) && !map.IsCanal(ax, ay)
                    && !index.Has(ax, ay, CellUse.Building | CellUse.Field | CellUse.Grave | CellUse.Canal))
                {
                    building.AccessX = parcel.AccessX = ax;
                    building.AccessY = parcel.AccessY = ay;
                    return;
                }
            }
            return;
        }

        (int EntryX, int EntryY, int AccessX, int AccessY, int Side)? best = null;
        float bestDistance = float.MaxValue;
        foreach (var door in DoorCandidates(building.X, building.Y, building.Width, building.Height))
        {
            if (!IsFreeAccessCell(colony, index, door.AccessX, door.AccessY, door.EntryX, door.EntryY))
                continue;
            float distance = (door.AccessX - colony.CampX) * (door.AccessX - colony.CampX) + (door.AccessY - colony.CampY) * (door.AccessY - colony.CampY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = door;
            }
        }

        if (best is not { } chosen)
        {
            parcel.LegacyOpen = true;
            layout.Anomalies.Add($"{Building.NameOf(building.Type)} ({building.X}, {building.Y}) : aucune porte praticable ; traversable comme avant.");
            return;
        }
        building.EntryX = parcel.EntryX = chosen.EntryX;
        building.EntryY = parcel.EntryY = chosen.EntryY;
        building.AccessX = parcel.AccessX = chosen.AccessX;
        building.AccessY = parcel.AccessY = chosen.AccessY;
        ReserveCell(layout, parcel, chosen.AccessX, chosen.AccessY);
    }

    private static void AssignFieldAccess(Colony colony, SettlementLayout layout, Field field, PlotReservation parcel)
    {
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        float bestDistance = float.MaxValue;
        (int X, int Y)? best = null;
        for (int i = -1; i <= Field.Size; i++)
        foreach ((int ax, int ay) in new[] { (field.X + i, field.Y - 1), (field.X + i, field.Y + Field.Size), (field.X - 1, field.Y + i), (field.X + Field.Size, field.Y + i) })
        {
            if (!map.InBounds(ax, ay) || !map.IsWalkable(ax, ay) || map.IsWaterway(ax, ay) || index.IsSolid(ax, ay))
                continue;
            if (ax >= field.X && ax < field.X + Field.Size && ay >= field.Y && ay < field.Y + Field.Size)
                continue;
            float distance = (ax - colony.CampX) * (ax - colony.CampX) + (ay - colony.CampY) * (ay - colony.CampY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (ax, ay);
            }
        }
        if (best is not { } access)
            return;
        field.AccessX = parcel.AccessX = access.X;
        field.AccessY = parcel.AccessY = access.Y;
        ReserveCell(layout, parcel, access.X, access.Y);
    }

    private static bool IsFreeAccessCell(Colony colony, LocalSpatialIndex index, int ax, int ay, int entryX, int entryY)
    {
        LocalMap map = colony.Map;
        if (!map.InBounds(ax, ay) || !map.IsWalkable(ax, ay) || map.IsWaterway(ax, ay) || map.IsMountain(ax, ay))
            return false;
        if (!map.CanStep(ax, ay, entryX, entryY) || !map.CanStep(entryX, entryY, ax, ay))
            return false;
        return !index.Has(ax, ay, CellUse.Building | CellUse.Canal | CellUse.Grave);
    }

    /// <summary>Un tronçon d'une cellule protège une case d'accès (la parcelle en reste propriétaire).</summary>
    private static void ReserveCell(SettlementLayout layout, PlotReservation parcel, int x, int y)
    {
        var segment = new RoadSegment(layout.NextSegmentId++, SegmentFunction.Access, RoadSurface.Trail, 0, 0);
        segment.Cells.Add(layout.Cell(x, y));
        segment.OwnerParcelIds.Add(parcel.Id);
        layout.RoadSegments.Add(segment);
        parcel.SegmentIds.Add(segment.Id);
    }
}
