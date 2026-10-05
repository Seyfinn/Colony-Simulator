using GodColony.Simulation.Colonies;

namespace GodColony.Simulation.Persistence;

/// <summary>
/// Migration d'un monde v1 vers le plan du village (v2) et validation d'un plan lu depuis un fichier. La migration préserve le terrain, les habitants,
/// les stocks, les relations, les chantiers et les états aléatoires ; elle ne déplace aucune maison, ne construit aucune route gratuitement et ne
/// consomme aucun tirage global. Les anciens quartiers restent compacts : leur transformation passe par les extensions futures.
/// </summary>
internal static class SettlementMigration
{
    internal static void MigrateV1(WorldState world)
    {
        world.Planning ??= new SettlementPlanningState();
        world.Map.EnsureV2Data();
        foreach (Colony colony in world.Colonies)
        {
            colony.Map.EnsureV2Data();
            // Les identifiants vont aux objets existants dans leur ordre stable (celui des listes), le cœur se crée au camp, puis les objets
            // se regroupent par proximité et par fonction ; une porte impossible laisse la parcelle traversable comme avant, avec une anomalie notée.
            SettlementPlanner.InitializeLayout(colony, colony.Map);
            SettlementPlanner.Sync(colony);
        }
    }

    /// <summary>Un plan lu depuis un fichier est vérifié comme tout le reste : identifiants uniques, coordonnées dans la carte, liens valides, tracés continus.</summary>
    internal static void ValidateLayout(Colony colony)
    {
        SettlementLayout layout = colony.Layout ?? throw new InvalidDataException("Le plan du village est absent.");
        int w = colony.Map.Width, h = colony.Map.Height;
        if (layout.Width != w || layout.Height != h || layout.RulesVersion < 1)
            throw new InvalidDataException("Le plan du village ne correspond pas à la carte.");
        if (layout.Districts.Count > 4096 || layout.Parcels.Count > 65536 || layout.RoadSegments.Count > 65536 || layout.Projects.Count > 65536)
            throw new InvalidDataException("Le plan du village est trop volumineux.");

        var districts = new HashSet<int>();
        foreach (District district in layout.Districts)
        {
            if (!districts.Add(district.Id) || district.Id >= layout.NextDistrictId || !layout.InBounds(district.AnchorX, district.AnchorY))
                throw new InvalidDataException("Quartier invalide.");
        }
        foreach (District district in layout.Districts)
            if (district.ParentDistrictId != -1 && !districts.Contains(district.ParentDistrictId))
                throw new InvalidDataException("Quartier parent invalide.");

        var segments = new HashSet<int>();
        foreach (RoadSegment segment in layout.RoadSegments)
        {
            if (!segments.Add(segment.Id) || segment.Id >= layout.NextSegmentId || segment.Cells.Count == 0)
                throw new InvalidDataException("Tracé invalide.");
            int previous = -1;
            foreach (int cell in segment.Cells)
            {
                if (cell < 0 || cell >= w * h)
                    throw new InvalidDataException("Cellule de tracé hors de la carte.");
                if (previous >= 0 && Math.Max(Math.Abs(cell % w - previous % w), Math.Abs(cell / w - previous / w)) != 1)
                    throw new InvalidDataException("Tracé discontinu.");
                previous = cell;
            }
        }

        var parcels = new HashSet<int>();
        foreach (PlotReservation parcel in layout.Parcels)
        {
            if (!parcels.Add(parcel.Id) || parcel.Id >= layout.NextParcelId
                || !layout.InBounds(parcel.X, parcel.Y) || !layout.InBounds(parcel.X + parcel.Width - 1, parcel.Y + parcel.Height - 1)
                || (parcel.DistrictId != -1 && !districts.Contains(parcel.DistrictId)))
                throw new InvalidDataException("Parcelle invalide.");
            foreach (int id in parcel.SegmentIds)
                if (!segments.Contains(id))
                    throw new InvalidDataException("Accès de parcelle invalide.");
        }
        foreach (RoadSegment segment in layout.RoadSegments)
            foreach (int owner in segment.OwnerParcelIds)
                if (!parcels.Contains(owner))
                    throw new InvalidDataException("Propriétaire de tracé invalide.");

        var objects = new HashSet<int>();
        foreach (Building building in colony.Buildings)
        {
            if (building.Id <= 0 || building.Id >= layout.NextObjectId || !objects.Add(building.Id)
                || (building.ParcelId != -1 && !parcels.Contains(building.ParcelId)))
                throw new InvalidDataException("Bâtiment non enregistré dans le plan du village.");
        }
        foreach (Field field in colony.Fields)
        {
            if (field.Id <= 0 || field.Id >= layout.NextObjectId || !objects.Add(field.Id)
                || (field.ParcelId != -1 && !parcels.Contains(field.ParcelId)))
                throw new InvalidDataException("Champ non enregistré dans le plan du village.");
        }

        var projects = new HashSet<int>();
        foreach (DevelopmentProject project in layout.Projects)
            if (!projects.Add(project.Id) || project.Id >= layout.NextProjectId || (project.ParcelId != -1 && !parcels.Contains(project.ParcelId)))
                throw new InvalidDataException("Projet invalide.");
    }
}
