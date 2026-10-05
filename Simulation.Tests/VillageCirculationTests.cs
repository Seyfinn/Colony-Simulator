using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Chemins, entrées, services : les sentiers naissent des passages, les maisons se contournent, les dépôts se font où l'on est.</summary>
public sealed class VillageCirculationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "GodColony-circulation-" + Guid.NewGuid().ToString("N") + ".gcsave");
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    private static WorldState Closed(int seed = 4242) => new(seed, 128, 128, startingColonists: 10, migration: false, lifecycle: false);

    private static void Run(WorldState world, double days)
    {
        long ticks = (long)(days * TimeConstants.TicksPerDay);
        for (long i = 0; i < ticks; i++) world.Step();
    }

    /// <summary>Une cellule de terre nue, loin de tout : là où seuls nos passages comptent.</summary>
    private static (int X, int Y) BareFarCell(Colony colony)
    {
        LocalMap map = colony.Map;
        for (int r = 30; r < 60; r++)
        for (int dx = -r; dx <= r; dx += 3)
        {
            int x = colony.CampX + dx, y = colony.CampY + r;
            if (map.InBounds(x, y) && map.IsWalkable(x, y) && !map.IsWaterway(x, y) && !map.IsMountain(x, y) && map.GetFlora(x, y) == FloraType.None
                && !colony.Spatial.IsSolid(x, y))
                return (x, y);
        }
        throw new InvalidOperationException("Aucune cellule nue.");
    }

    [Fact]
    public void Des_passages_repetes_font_naitre_un_sentier_seulement_sur_terre_nue()
    {
        WorldState world = Closed();
        Colony colony = world.Colonies[0];
        (int x, int y) = BareFarCell(colony);
        for (int i = 0; i < SettlementRules.TrailThreshold - 1; i++)
            RoadDevelopment.OnStepCompleted(colony, x, y);
        Assert.Equal(RoadSurface.None, colony.Map.Roads.SurfaceAt(y * colony.Map.Width + x));
        RoadDevelopment.OnStepCompleted(colony, x, y);
        Assert.Equal(RoadSurface.Trail, colony.Map.Roads.SurfaceAt(y * colony.Map.Width + x));

        // Ni dans un bâtiment, ni sur une culture, ni sur l'eau, ni sur la place.
        (int fx, int fy) = (colony.Fields[0].X, colony.Fields[0].Y);
        Building? building = colony.Buildings.FirstOrDefault();
        var forbidden = new List<(int, int)> { (fx, fy), (colony.CampX, colony.CampY) };
        if (building is not null) forbidden.Add((building.X, building.Y));
        for (int wy = 0; wy < colony.Map.Height && forbidden.Count < 6; wy++)
            if (colony.Map.IsWater(5, wy)) forbidden.Add((5, wy));
        foreach ((int px, int py) in forbidden)
        {
            for (int i = 0; i < 20; i++) RoadDevelopment.OnStepCompleted(colony, px, py);
            Assert.Equal(RoadSurface.None, colony.Map.Roads.SurfaceAt(py * colony.Map.Width + px));
        }
    }

    [Fact]
    public void L_usure_ne_depend_pas_de_la_frequence_de_lecture_et_un_sentier_oublie_s_efface()
    {
        WorldState world = Closed();
        Colony colony = world.Colonies[0];
        (int x, int y) = BareFarCell(colony);
        int cell = y * colony.Map.Width + x;
        for (int i = 0; i < 12; i++) RoadDevelopment.OnStepCompleted(colony, x, y);
        int today = (int)world.Clock.TotalDays;
        int direct = RoadDevelopment.WearAt(colony.Map, cell, today + 5);
        for (int i = 0; i < 100; i++) _ = RoadDevelopment.WearAt(colony.Map, cell, today + 2);
        Assert.Equal(direct, RoadDevelopment.WearAt(colony.Map, cell, today + 5));
        Assert.True(direct < 12 * RoadLayer.Pass);

        Run(world, 14);
        Assert.Equal(RoadSurface.None, colony.Map.Roads.SurfaceAt(cell));
    }

    [Fact]
    public void Un_chemin_de_terre_amenage_ne_disparait_pas_et_accelere_la_marche()
    {
        WorldState world = Closed();
        Colony colony = world.Colonies[0];
        (int x, int y) = BareFarCell(colony);
        int cell = y * colony.Map.Width + x;
        float bare = TraversalCost.StepSeconds(colony.Map, x - 1, y, x, y);
        RoadWorks.CompleteCell(colony, x, y, segmentId: 0);
        Assert.True(TraversalCost.StepSeconds(colony.Map, x - 1, y, x, y) < bare * 0.75f);
        Run(world, 25);
        Assert.Equal(RoadSurface.DirtRoad, colony.Map.Roads.SurfaceAt(cell));
        // Aucun bonus sur l'eau : le coût d'une rivière ou d'un canal ne dépend pas de la couche routière.
        for (int wy = 0; wy < colony.Map.Height; wy++)
        for (int wx = 0; wx < colony.Map.Width; wx++)
            if (colony.Map.IsRiver(wx, wy))
            {
                Assert.True(TraversalCost.StepSeconds(colony.Map, wx - 1, wy, wx, wy) >= LocalMap.RiverMoveCost * LocalMap.RuggednessMin / SettlementRules.WalkTilesPerSecond - 0.001f);
                return;
            }
    }

    [Fact]
    public void Aucun_chemin_ne_traverse_une_maison_tierce()
    {
        var world = new WorldState(4242, 128, 128, startingColonists: 12);
        Run(world, 40);
        Colony colony = world.Colonies[0];
        LocalSpatialIndex index = colony.Spatial;
        LocalMap map = colony.Map;
        var rng = new Random(5);
        int tested = 0;
        for (int attempt = 0; attempt < 400 && tested < 120; attempt++)
        {
            (int sx, int sy) = (colony.CampX + rng.Next(-30, 31), colony.CampY + rng.Next(-30, 31));
            (int gx, int gy) = (colony.CampX + rng.Next(-30, 31), colony.CampY + rng.Next(-30, 31));
            if (!map.InBounds(sx, sy) || !map.InBounds(gx, gy) || !map.IsWalkable(sx, sy) || !map.IsWalkable(gx, gy)) continue;
            NavPath? path = LocalNavigation.FindPath(colony, sx, sy, gx, gy);
            if (path is null) continue;
            tested++;
            foreach ((int x, int y) in path.Cells)
                if (index.BlocksWalking(x, y))
                {
                    int owner = index.OwnerAt(x, y);
                    Assert.True(owner == path.StartBuildingId || owner == path.GoalBuildingId, $"Le chemin traverse le bâtiment {owner}.");
                }
        }
        Assert.True(tested > 30);
    }

    [Fact]
    public void On_entre_et_on_sort_d_une_hutte_par_sa_porte_depuis_chacun_des_quatre_lits()
    {
        WorldState world = Closed();
        Colony colony = world.Colonies[0];
        Building hut = Urbanism.BuildInstantly(world.Map, colony, BuildingType.Hut)!;
        Assert.True(hut.HasDoor);
        foreach ((int bx, int by) in hut.Tiles)
        {
            NavPath path = LocalNavigation.FindPath(colony, bx, by, colony.CampX + 3, colony.CampY + 3)!;
            Assert.NotNull(path);
            Assert.Equal(hut.Id, path.StartBuildingId);
            // Les seules cases du bâtiment sont l'entrée (puis la porte, hors emprise).
            Assert.True(path.Cells.Count(c => hut.Contains(c.X, c.Y)) <= 1);
            Assert.Contains((hut.AccessX, hut.AccessY), path.Cells);
            // Et le retour au lit passe par la même porte.
            NavPath back = LocalNavigation.FindPath(colony, colony.CampX + 3, colony.CampY + 3, bx, by)!;
            Assert.Equal((bx, by), back.Cells[^1]);
            Assert.Contains((hut.AccessX, hut.AccessY), back.Cells);
        }
    }

    [Fact]
    public void Un_entrepot_en_chantier_n_est_pas_un_lieu_de_depot_et_un_entrepot_acheve_l_est()
    {
        WorldState world = Closed();
        Colony colony = world.Colonies[0];
        Assert.Single(SettlementServices.Points(colony), p => p.Provides(ServiceUse.Stock));
        Building site = Urbanism.PlanBuilding(world.Map, colony, BuildingType.Storehouse, colony.CampX + 10, colony.CampY - 6);
        Assert.Single(SettlementServices.Points(colony), p => p.Provides(ServiceUse.Stock));
        site.Progress = 1f;
        Assert.Equal(2, SettlementServices.Points(colony).Count(p => p.Provides(ServiceUse.Stock)));
        Assert.DoesNotContain(SettlementServices.Points(colony), p => p.Provides(ServiceUse.Meal) && !p.Provides(ServiceUse.Meet));
    }

    [Fact]
    public void Une_recolte_se_depose_une_seule_fois_a_l_entrepot_le_plus_proche_pas_au_camp()
    {
        WorldState world = Closed();
        Colony colony = world.Colonies[0];
        // Un entrepôt loin du camp, sur le premier emplacement libre à douze cases ou plus.
        (int sx, int sy) = (0, 0);
        for (int r = 12; r < 30 && sx == 0; r++)
        for (int dx = -r; dx <= r && sx == 0; dx++)
            if (PlacementChecks.Building(world.Map, colony.Spatial, colony.CampX + dx, colony.CampY + r, 2, 2) == PlacementChecks.Verdict.Ok)
                (sx, sy) = (colony.CampX + dx, colony.CampY + r);
        Building store = Urbanism.PlanBuilding(world.Map, colony, BuildingType.Storehouse, sx, sy);
        store.Progress = 1f;
        Colonist worker = colony.Members[0];
        (int ax, int ay) = (store.AccessX, store.AccessY);
        Assert.True(Math.Max(Math.Abs(ax - colony.CampX), Math.Abs(ay - colony.CampY)) > 8, "L'entrepôt est trop près du camp pour ce test.");
        worker.X = ax + 0.5f; worker.Y = ay + 0.5f; worker.PrevX = worker.X; worker.PrevY = worker.Y;
        worker.Activity = null;
        worker.Carrying = (ResourceType.Wood, 5);
        worker.WorkCycleStartTicks = -1;
        int before = colony.Stock.Get(ResourceType.Wood);
        int guard = 0;
        while (worker.Carrying is not null && guard++ < 400)
            world.Step();
        Assert.Null(worker.Carrying);
        Assert.InRange(Math.Max(Math.Abs(worker.TileX - ax), Math.Abs(worker.TileY - ay)), 0, 2);
        Assert.True(colony.Stock.Get(ResourceType.Wood) >= before + 5);
    }

    [Fact]
    public void Une_partie_sauvegardee_au_milieu_d_une_recherche_continue_comme_en_continu()
    {
        var world = new WorldState(4242, 128, 128, startingColonists: 12);
        bool saved = false;
        for (int i = 0; i < 40 * TimeConstants.TicksPerDay && !saved; i++)
        {
            world.Step();
            saved = world.Planning.Jobs.Any(j => j.Search is { Status: SearchStatus.Running, Expanded: > 0 });
        }
        Assert.True(saved, "Aucune recherche d'accès n'a été vue en cours.");
        WorldSave.Save(_path, world);
        WorldState loaded = WorldSave.Load(_path).World;
        _ = world.SupplierMemories; _ = loaded.SupplierMemories; // une liste paresseuse d'un autre chantier : on la matérialise des deux côtés
        for (int i = 0; i < 3000; i++)
        {
            world.Step();
            loaded.Step();
        }
        Assert.Null(new WorldComparison().Difference(world, loaded));
    }
}

/// <summary>L'aménagement des chemins est facultatif : jamais en crise, jamais avant un chantier de survie, possible sans aucun chantier de bâtiment.</summary>
public sealed class RoadWorksTests
{
    private static WorldState Prosperous()
    {
        var world = new WorldState(4242, 128, 128, startingColonists: 14, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        colony.Stock.Add(ResourceType.Food, 400);
        colony.Stock.Add(ResourceType.Wood, 200);
        for (int i = 0; i < TimeConstants.TicksPerDay * 3; i++) world.Step();
        return world;
    }

    private static RoadSegment BusiestSegment(Colony colony)
    {
        RoadSegment segment = colony.Layout.RoadSegments.OrderByDescending(s => s.Cells.Count).First();
        int day = (int)colony.Clock.TotalDays;
        foreach (int cell in segment.Cells)
            colony.Map.Roads.SetWear(cell, 30 * RoadLayer.Pass, day);
        return segment;
    }

    [Fact]
    public void Un_axe_tres_frequente_devient_un_projet_puis_un_chemin_de_terre_sans_aucun_chantier_de_batiment()
    {
        WorldState world = Prosperous();
        Colony colony = world.Colonies[0];
        foreach (Building site in colony.ConstructionSites.ToList()) colony.Buildings.Remove(site);
        RoadSegment segment = BusiestSegment(colony);
        Assert.True(RoadWorks.IsAllowed(colony));
        RoadWorks.OnDayStart(colony);
        DevelopmentProject project = Assert.IsType<DevelopmentProject>(RoadWorks.ActiveProject(colony));
        Assert.Equal(segment.Id, Assert.Single(project.SegmentIds));

        Colonist worker = colony.Workers.First();
        Activity? activity = RoadWorks.NextActivity(worker, 1f, idleHands: true);
        Assert.NotNull(activity);
        Assert.Equal(segment.Id, activity!.SegmentId);
        Assert.True(activity.Kind is ActivityKind.BuildRoad or ActivityKind.ClearAccess or ActivityKind.Chop);
        // Au plus 10 % des travailleurs (arrondi inférieur), et un seul bras en temps libre si le plafond vaut zéro.
        Assert.Equal((int)(colony.Workers.Count() * SettlementRules.RoadWorkerShare), RoadWorks.WorkerCap(colony));
    }

    [Fact]
    public void En_crise_aucun_amenagement_n_est_ouvert_ni_affecte()
    {
        WorldState world = Prosperous();
        Colony colony = world.Colonies[0];
        BusiestSegment(colony);
        RoadWorks.OnDayStart(colony);
        Assert.NotNull(RoadWorks.ActiveProject(colony));

        colony.Stock.TryTake(ResourceType.Food, colony.Stock.Get(ResourceType.Food), ResourceFlow.Loss);
        colony.Stock.TryTake(ResourceType.Fish, colony.Stock.Get(ResourceType.Fish), ResourceFlow.Loss);
        ColonyBrain.Think(colony, colony.Map, colony.Clock);
        Assert.False(RoadWorks.IsAllowed(colony));
        Assert.True(RoadWorks.IsPaused(colony));
        Assert.Null(RoadWorks.NextActivity(colony.Workers.First(), 1f, idleHands: true));
        // Le projet garde sa parcelle et sa progression : il reprendra quand la crise sera passée.
        Assert.NotNull(RoadWorks.ActiveProject(colony));
    }
}

/// <summary>Plusieurs peuples, plusieurs terrains, un incendie : le plan reste borné, personne n'est piégé, et la perte d'un dépôt ne casse rien.</summary>
public sealed class VillageSoakTests
{
    [Fact]
    public void Quatre_peuples_vivent_sans_explosion_des_listes_ni_colon_pris_dans_une_maison()
    {
        var world = new WorldState(31, 128, 128, colonyCount: 4, startingColonists: 10);
        for (int i = 0; i < 40 * TimeConstants.TicksPerDay; i++)
        {
            world.Step();
            if (i % TimeConstants.TicksPerDay != 0) continue;
            Assert.True(world.Planning.Jobs.Count <= 4 * 8, "La file de recherches grossit sans fin.");
            foreach (Colony colony in world.Colonies)
            {
                Assert.True(colony.Layout.Parcels.Count <= 3 * (colony.Buildings.Count + colony.Fields.Count) + 10);
                Assert.True(colony.Layout.RoadSegments.Count <= 3 * (colony.Buildings.Count + colony.Fields.Count) + 10);
                foreach (Colonist c in colony.Members)
                    Assert.True(colony.Map.IsWalkable(c.TileX, c.TileY));
            }
        }
        // Chaque colonie a un quartier civique, et des habitants.
        Assert.All(world.Colonies, c => { Assert.Contains(c.Layout.Districts, d => d.Kind == DistrictKind.Civic); Assert.NotEmpty(c.Members); });
    }

    [Fact]
    public void L_incendie_d_un_entrepot_isole_libere_sa_parcelle_et_le_camp_reste_le_depot()
    {
        var world = new WorldState(4242, 128, 128, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Building store = Urbanism.BuildInstantly(world.Map, colony, BuildingType.Storehouse)!;
        Assert.Equal(2, SettlementServices.Points(colony).Count(p => p.Provides(ServiceUse.Stock)));
        PlotReservation parcel = colony.Layout.ParcelById(store.ParcelId)!;
        Assert.Equal(ReservationState.Occupied, parcel.State);

        Civic.Burn(colony, store, colony.Clock);
        Assert.Equal(ReservationState.Released, parcel.State);
        Assert.Single(SettlementServices.Points(colony), p => p.Provides(ServiceUse.Stock));
        for (int i = 0; i < 2 * TimeConstants.TicksPerDay; i++) world.Step();
        Assert.NotEmpty(colony.Members);
    }
}

public sealed class VillageMeasuresTests
{
    [Fact]
    public void L_emprise_active_depasse_le_voisinage_du_feu_et_les_trajets_restent_courts()
    {
        var world = new WorldState(4242, 200, 200, startingColonists: 12);
        for (int i = 0; i < 60 * TimeConstants.TicksPerDay; i++) world.Step();
        Colony colony = world.Colonies[0];
        Assert.InRange(VillageMeasures.ActiveDiameter(colony), 20, 100);
        Assert.True(VillageMeasures.HabitationCores(colony) >= 1);
        Assert.InRange(VillageMeasures.AverageHomeToMealSeconds(colony), 0f, SettlementRules.HomeToMealSeconds * 2f);
    }
}
