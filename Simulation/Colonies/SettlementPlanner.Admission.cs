using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Le résultat d'une admission : l'objet créé (bâtiment ou champ), ou la raison du refus. Un refus n'a laissé aucun effet partiel.</summary>
public readonly record struct CommitResult(bool Success, Building? Building, Field? Field, string Reason)
{
    internal static CommitResult Failed(string reason) => new(false, null, null, reason);
}

public static partial class SettlementPlanner
{
    // --- Les demandes persistantes ---

    /// <summary>
    /// La demande de cette fonction : créée une fois, puis retrouvée par sa clé. Une demande décrit ce qui manque, jamais une coordonnée ; elle garde sa priorité,
    /// son urgence et son motif d'attente. Ne lance aucune recherche.
    /// </summary>
    internal static PlanRequest RequestFor(Colony colony, DevelopmentKind kind, BuildingType? type, DevelopmentPriority priority, bool urgent = false, int hintX = -1, int hintY = -1)
    {
        SettlementLayout layout = colony.Layout;
        string key = KeyFor(kind, type);
        PlanRequest? request = layout.Requests.FirstOrDefault(r => r.Key == key);
        if (request is null)
        {
            request = new PlanRequest(key, kind, type, colony.Clock.Ticks);
            layout.Requests.Add(request);
        }
        request.Priority = priority;
        request.Urgent = urgent;
        request.HintX = hintX;
        request.HintY = hintY;
        return request;
    }

    internal static string KeyFor(DevelopmentKind kind, BuildingType? type) => kind switch
    {
        DevelopmentKind.Housing => "hut",
        DevelopmentKind.Field => "field",
        _ => "building:" + (type?.ToString() ?? "none"),
    };

    /// <summary>
    /// Des événements utiles sont survenus : chaque demande en attente d'un tel événement est réveillée (au prochain examen), les autres ne bougent pas.
    /// Une naissance qui ne change pas la demande de lits, la maturation d'une baie ailleurs sur la carte ne passent jamais ici.
    /// </summary>
    internal static void Notify(Colony colony, RetryEvents events)
    {
        foreach (PlanRequest request in colony.Layout.Requests)
            request.Seen |= events;
    }

    /// <summary>
    /// Le terrain a-t-il déjà dit non à ce type de bâtiment, sans qu'aucun événement n'autorise un nouvel essai ? Une lecture du résultat spatial en cache, jamais une
    /// recherche : c'est ce qui permet aux producteurs de demandes (la chaîne du blé, le civique) de rester des règles économiques.
    /// </summary>
    internal static bool IsKnownImpossible(Colony colony, BuildingType type)
    {
        string key = KeyFor(Urbanism.KindOf(type), type);
        PlanRequest? request = colony.Layout.Requests.FirstOrDefault(r => r.Key == key);
        return request is { State: PlanningOutcome.WaitingForChange } waiting
            && (waiting.Seen & waiting.RetryOn) == 0
            && colony.Clock.Ticks - waiting.LastSearchTicks < 3L * TimeConstants.TicksPerDay;
    }

    /// <summary>
    /// L'état d'une demande, sans jamais chercher de site sur place : une proposition prête et encore valide (<see cref="PlanningOutcome.Ready"/>), une recherche en file
    /// (<see cref="PlanningOutcome.Pending"/>), ou une impossibilité connue qui attend un événement (<see cref="PlanningOutcome.WaitingForChange"/>). Une demande oubliée est mise en
    /// file ; une proposition périmée repart en file sans recherche synchrone.
    /// </summary>
    internal static PlanningOutcome Poll(Colony colony, PlanRequest request)
    {
        SettlementPlanningState? planning = colony.Planning;
        switch (request.State)
        {
            case PlanningOutcome.Ready when request.Proposal is { } proposal:
                if (StillValid(colony, proposal))
                    return PlanningOutcome.Ready;
                request.Proposal = null;
                request.State = PlanningOutcome.Idle;
                break;
            case PlanningOutcome.Pending when request.JobId >= 0:
                if (planning?.Jobs.Any(j => j.Id == request.JobId) == true)
                    return PlanningOutcome.Pending;
                request.JobId = -1;
                request.State = PlanningOutcome.Idle;
                break;
            case PlanningOutcome.WaitingForChange:
                // Une impossibilité connue : on la retente quand un événement pertinent est survenu, ou, très espacé, par une reprise quotidienne.
                bool daily = colony.Clock.Ticks - request.LastSearchTicks >= 3L * TimeConstants.TicksPerDay;
                if ((request.Seen & request.RetryOn) == 0 && !daily)
                    return PlanningOutcome.WaitingForChange;
                request.State = PlanningOutcome.Idle;
                break;
        }

        Sync(colony);
        request.Seen = RetryEvents.None;
        request.LastSearchTicks = colony.Clock.Ticks;
        int jobId = planning is null ? 0 : planning.NextJobId++;
        PlanningJob job = new(jobId, colony, request, colony.Clock.Ticks, colony.Layout.Revision, colony.Map.TerrainRevision);
        request.JobId = job.Id;
        if (planning is null)
        {
            // Une colonie bâtie à la main, hors d'un monde : pas de planificateur à cadence fixe, la recherche se fait sur place.
            SitePlanner.Advance(job, SitePlanner.Budget.Unlimited());
            if (job.Search is not null)
                Pathfinding.IncrementalPathSearch.Release(job.Search);
            Deliver(job);
            return request.State;
        }
        planning.Jobs.Add(job);
        request.State = PlanningOutcome.Pending;
        return PlanningOutcome.Pending;
    }

    /// <summary>Une recherche terminée remet son résultat à sa demande (appelé par le planificateur du monde).</summary>
    internal static void Deliver(PlanningJob job)
    {
        if (job.Kind == DevelopmentKind.RoadShortcut)
        {
            RoadShortcuts.Deliver(job);
            return;
        }
        Colony colony = job.Owner;
        PlanRequest? request = colony.Layout.Requests.FirstOrDefault(r => r.Key == job.RequestKey);
        if (request is null || request.JobId != job.Id)
            return;
        request.JobId = -1;
        if (job.Stage == PlanStage.Ready && job.Result is { } proposal)
        {
            request.Proposal = proposal;
            request.State = PlanningOutcome.Ready;
            request.Failure = null;
            return;
        }
        request.Proposal = null;
        request.State = PlanningOutcome.WaitingForChange;
        request.Failure = job.Failure;
        request.RetryOn = RetryOnFor(job.Failure ?? PlacementFailureKind.NoSpace);
        request.Seen = RetryEvents.None;
    }

    /// <summary>Quels événements autorisent un nouvel essai après cette impossibilité.</summary>
    private static RetryEvents RetryOnFor(PlacementFailureKind failure) => failure switch
    {
        PlacementFailureKind.NoSuitableTerrain => RetryEvents.Terrain | RetryEvents.Season,
        PlacementFailureKind.NoSpace => RetryEvents.Occupancy,
        PlacementFailureKind.NoAccess => RetryEvents.Occupancy | RetryEvents.Terrain | RetryEvents.Road,
        PlacementFailureKind.TravelBudgetExceeded => RetryEvents.Service | RetryEvents.Occupancy | RetryEvents.Road,
        PlacementFailureKind.NoCompatibleDistrict => RetryEvents.Occupancy | RetryEvents.Knowledge,
        PlacementFailureKind.PrerequisiteMissing => RetryEvents.Knowledge | RetryEvents.Service,
        _ => RetryEvents.Occupancy,
    };

    // --- Les sondes synchrones (outils de développement et tests : jamais le chemin normal d'une pensée) ---

    /// <summary>
    /// Cherche tout de suite le meilleur emplacement d'une fonction, sans budget et sans rien modifier au monde (ni plan, ni hasard). Le résultat est une proposition,
    /// ou null si le terrain n'en offre aucune ; <paramref name="failure"/> dit alors pourquoi.
    /// </summary>
    public static PlacementProposal? Probe(Colony colony, DevelopmentKind kind, BuildingType? type, bool urgent, out PlacementFailureKind? failure, int hintX = -1, int hintY = -1)
    {
        Sync(colony);
        var request = new PlanRequest(KeyFor(kind, type), kind, type, colony.Clock.Ticks)
        {
            Urgent = urgent,
            Priority = urgent ? DevelopmentPriority.Housing : DevelopmentPriority.Comfort,
            HintX = hintX,
            HintY = hintY,
        };
        var job = new PlanningJob(0, colony, request, colony.Clock.Ticks, colony.Layout.Revision, colony.Map.TerrainRevision);
        SitePlanner.Advance(job, SitePlanner.Budget.Unlimited());
        if (job.Search is not null)
            Pathfinding.IncrementalPathSearch.Release(job.Search);
        failure = job.Failure;
        return job.Stage == PlanStage.Ready ? job.Result : null;
    }

    // --- L'admission ---

    /// <summary>
    /// Admet tout de suite une proposition venue d'une sonde (outil de développement, test) : même revalidation, même écriture atomique que l'admission d'une demande.
    /// </summary>
    public static CommitResult CommitNow(Colony colony, PlacementProposal proposal, DevelopmentPriority priority = DevelopmentPriority.Production)
    {
        Sync(colony);
        return StillValid(colony, proposal) ? Commit(colony, proposal, priority) : CommitResult.Failed("la proposition est périmée");
    }

    /// <summary>
    /// Ouvre un projet à l'emplacement demandé, après l'avoir revalidé avec le profil du type et son accès (les contraintes physiques restent impératives).
    /// Un échec ne laisse aucune trace : à l'appelant de décider s'il insiste.
    /// </summary>
    internal static CommitResult PlanAt(Colony colony, DevelopmentKind kind, BuildingType? type, int x, int y, DevelopmentPriority priority = DevelopmentPriority.Production)
    {
        Sync(colony);
        PlacementProposal? proposal = SitePlanner.ProposeAt(colony, kind, type, x, y);
        return proposal is null ? CommitResult.Failed("emplacement refusé") : Commit(colony, proposal, priority);
    }

    /// <summary>
    /// Une proposition est-elle encore valable ? Si rien n'a changé dans la région qu'elle utilise (occupation et terrain), oui sans autre vérification. Sinon on revalide
    /// l'emprise, la porte, le tracé et l'hydraulique sur place : jamais une recherche globale.
    /// </summary>
    internal static bool StillValid(Colony colony, PlacementProposal proposal)
    {
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        SettlementLayout layout = colony.Layout;
        (int minX, int minY, int maxX, int maxY) = Bounds(layout, proposal);
        if (index.UnchangedSince(proposal.EvaluatedRevision, minX, minY, maxX, maxY)
            && map.TerrainUnchangedSince(proposal.EvaluatedTerrainRevision, minX, minY, maxX, maxY))
            return true;

        bool isField = proposal.Kind == DevelopmentKind.Field;
        PlacementChecks.Verdict verdict = isField
            ? PlacementChecks.Field(map, index, proposal.X, proposal.Y, proposal.Width)
            : PlacementChecks.Building(map, index, proposal.X, proposal.Y, proposal.Width, proposal.Height);
        if (verdict != PlacementChecks.Verdict.Ok)
            return false;
        if (proposal.Type is { } type && BuildingPlacementProfile.For(type).NeedsFlow && Hydrology.MillFlow(map, new Building(type, proposal.X, proposal.Y)) <= 0f)
            return false;
        if (!isField && !PlacementChecks.IsAccessCell(map, index, proposal.AccessX, proposal.AccessY, proposal.EntryX, proposal.EntryY))
            return false;
        if (!PlacementChecks.PathStillWalkable(map, index, layout, proposal.PathCells, (proposal.X, proposal.Y, proposal.Width, proposal.Height)))
            return false;
        if (proposal.HasPublicSpace)
            for (int ty = proposal.PublicY; ty < proposal.PublicY + proposal.PublicHeight; ty++)
            for (int tx = proposal.PublicX; tx < proposal.PublicX + proposal.PublicWidth; tx++)
                if (index.IsSolid(tx, ty) || index.Has(tx, ty, CellUse.Corridor | CellUse.Courtyard) || map.GetFlora(tx, ty) is FloraType.Tree or FloraType.Bush)
                    return false;
        proposal.EvaluatedRevision = index.Revision;
        proposal.EvaluatedTerrainRevision = map.TerrainRevision;
        return true;
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) Bounds(SettlementLayout layout, PlacementProposal proposal)
    {
        int minX = proposal.X - 2, minY = proposal.Y - 2, maxX = proposal.X + proposal.Width + 1, maxY = proposal.Y + proposal.Height + 1;
        foreach (int cell in proposal.PathCells)
        {
            (int x, int y) = layout.Decode(cell);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        if (proposal.HasPublicSpace)
        {
            minX = Math.Min(minX, proposal.PublicX - 1);
            minY = Math.Min(minY, proposal.PublicY - 1);
            maxX = Math.Max(maxX, proposal.PublicX + proposal.PublicWidth);
            maxY = Math.Max(maxY, proposal.PublicY + proposal.PublicHeight);
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Admet la proposition prête d'une demande : tout se prépare avant de s'appliquer, rien n'est déduit du stock pour la simple réservation du terrain (les livraisons
    /// de matériaux restent celles du chantier). Un échec ne laisse aucun effet partiel et renvoie la demande en file.
    /// </summary>
    internal static CommitResult TryCommit(Colony colony, PlanRequest request)
    {
        if (request.Proposal is not { } proposal)
            return CommitResult.Failed("aucune proposition prête");
        Sync(colony);
        if (!StillValid(colony, proposal))
        {
            request.Proposal = null;
            request.State = PlanningOutcome.Idle;
            return CommitResult.Failed("la proposition est périmée");
        }
        CommitResult result = Commit(colony, proposal, request.Priority);
        request.Proposal = null;
        request.State = PlanningOutcome.Idle;
        request.Failure = null;
        request.Seen = RetryEvents.None;
        return result;
    }

    /// <summary>
    /// L'écriture : quartier (s'il naît avec ce premier projet), parcelle, espace public, tracé d'accès, projet, puis l'objet lui-même par la sémantique économique
    /// actuelle (défrichage et chantier d'un bâtiment, ouverture immédiate d'un champ). Les identifiants sont attribués dans un ordre fixe.
    /// </summary>
    private static CommitResult Commit(Colony colony, PlacementProposal p, DevelopmentPriority priority)
    {
        SettlementLayout layout = colony.Layout;
        LocalMap map = colony.Map;
        long now = colony.Clock.Ticks;
        bool isField = p.Kind == DevelopmentKind.Field;

        District district = ResolveDistrict(colony, layout, p, now);
        ParcelKind kind = isField ? ParcelKind.Field : ParcelKind.Building;
        var parcel = new PlotReservation(layout.NextParcelId++, district.Id, kind, p.X, p.Y, p.Width, p.Height, now)
        {
            BuildingType = p.Type,
            Margin = p.Margin,
            EntryX = p.EntryX,
            EntryY = p.EntryY,
            AccessX = p.AccessX,
            AccessY = p.AccessY,
            State = isField ? ReservationState.Occupied : ReservationState.Reserved,
        };
        layout.Parcels.Add(parcel);

        var segment = new RoadSegment(layout.NextSegmentId++, SegmentFunction.Access, RoadSurface.Trail, (int)priority, now);
        segment.Cells.AddRange(p.PathCells);
        segment.OwnerParcelIds.Add(parcel.Id);
        layout.RoadSegments.Add(segment);
        parcel.SegmentIds.Add(segment.Id);

        if (p.HasPublicSpace)
        {
            var space = new PlotReservation(layout.NextParcelId++, district.Id, ParcelKind.PublicSpace, p.PublicX, p.PublicY, p.PublicWidth, p.PublicHeight, now);
            layout.Parcels.Add(space);
        }

        var project = new DevelopmentProject(layout.NextProjectId++, p.Kind, priority, parcel.Id, now, p.RequestKey);
        project.SegmentIds.Add(segment.Id);
        project.ClearCells.AddRange(p.ClearCells);
        parcel.ProjectId = project.Id;
        segment.ActiveProjectId = -1;
        layout.Projects.Add(project);

        Building? building = null;
        Field? field = null;
        if (isField)
        {
            field = Farming.OpenField(map, colony, p.X, p.Y, p.Width);
            field.Id = layout.NextObjectId++;
            field.DistrictId = district.Id;
            field.ParcelId = parcel.Id;
            field.AccessX = p.AccessX;
            field.AccessY = p.AccessY;
            parcel.OccupantId = field.Id;
            project.OccupantId = field.Id;
            // Un champ garde son ouverture immédiate : son projet d'implantation s'achève ici, sans progression agricole artificielle.
            project.State = ProjectState.Completed;
        }
        else
        {
            building = Urbanism.Raise(map, colony, p.Type ?? BuildingType.Hut, p.X, p.Y);
            building.Id = layout.NextObjectId++;
            building.DistrictId = district.Id;
            building.ParcelId = parcel.Id;
            building.EntryX = p.EntryX;
            building.EntryY = p.EntryY;
            building.AccessX = p.AccessX;
            building.AccessY = p.AccessY;
            parcel.OccupantId = building.Id;
            project.OccupantId = building.Id;
        }

        district.LastExpandedTicks = now;
        district.Status = DistrictStatus.Active;
        layout.Revision++;
        foreach (PlanRequest other in layout.Requests)
            other.Seen |= RetryEvents.Occupancy;
        return new CommitResult(true, building, field, "");
    }

    /// <summary>Le quartier d'une proposition : celui qu'elle vise, un voisin compatible pour un projet détaché (un champ près d'un dépôt), ou un quartier neuf avec ce premier projet.</summary>
    private static District ResolveDistrict(Colony colony, SettlementLayout layout, PlacementProposal p, long now)
    {
        if (p.DistrictId >= 0 && layout.DistrictById(p.DistrictId) is { } existing)
            return existing;
        if (!p.ForceNewDistrict)
        {
            District? joined = DistrictFor(layout, null, p.X, p.Y, p.Width, p.Height, p.NewKind);
            if (joined is not null)
                return joined;
        }
        return OpenDistrict(colony, layout, p.NewKind, p.X, p.Y, p.ParentDistrictId);
    }

    // --- Achèvement et disparition ---

    /// <summary>Un bâtiment ou un champ est achevé : sa parcelle devient occupée, son projet est achevé, les services se mettent à jour et les demandes en attente d'un service sont réveillées.</summary>
    internal static void OnObjectCompleted(Colony colony, Building building)
    {
        SettlementLayout layout = colony.Layout;
        if (building.Id == 0)
            Adopt(colony, building);
        if (layout.ParcelById(building.ParcelId) is { } parcel)
        {
            parcel.State = ReservationState.Occupied;
            if (layout.ProjectById(parcel.ProjectId) is { } project)
            {
                project.State = ProjectState.Completed;
                parcel.ProjectId = -1;
            }
        }
        layout.Revision++;
        RetryEvents events = RetryEvents.Occupancy | (building.Type is BuildingType.Storehouse or BuildingType.Tavern or BuildingType.Hut ? RetryEvents.Service : RetryEvents.None);
        Notify(colony, events);
    }

    /// <summary>Un bâtiment disparaît (incendie, démolition) : sa parcelle est libérée, ses accès partagés restent, les activités sont réévaluées par le reste du jeu.</summary>
    internal static void OnObjectRemoved(Colony colony, Building building)
    {
        SettlementLayout layout = colony.Layout;
        if (layout.ParcelById(building.ParcelId) is { State: not ReservationState.Released } parcel)
        {
            if (layout.ProjectById(parcel.ProjectId) is { } project)
                project.State = ProjectState.Cancelled;
            Release(colony, parcel);
        }
        Notify(colony, RetryEvents.Occupancy | RetryEvents.Service);
    }
}
