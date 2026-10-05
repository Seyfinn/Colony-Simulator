using System.Security.Cryptography;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Le plan du village : déterminisme, sondes pures, demandes persistantes, invariants d'implantation et budget partagé.</summary>
public sealed class SettlementPlanningTests
{
    private static void Run(WorldState world, double days)
    {
        long ticks = (long)(days * TimeConstants.TicksPerDay);
        for (long i = 0; i < ticks; i++) world.Step();
    }

    private static string Fingerprint(WorldState world)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        StateGraph.Write(writer, world);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    [Fact]
    public void Meme_graine_memes_quartiers_parcelles_et_chemins()
    {
        var a = new WorldState(4242, 128, 128, startingColonists: 10);
        var b = new WorldState(4242, 128, 128, startingColonists: 10);
        Run(a, 30);
        Run(b, 30);
        Assert.Null(new WorldComparison().Difference(a.Colonies[0].Layout, b.Colonies[0].Layout));
        Assert.Null(new WorldComparison().Difference(a.Colonies[0].Map.Roads, b.Colonies[0].Map.Roads));
        Assert.NotEmpty(a.Colonies[0].Layout.Parcels);
    }

    [Fact]
    public void Une_sonde_de_site_ne_modifie_rien_et_ne_tire_aucun_hasard()
    {
        var world = new WorldState(4242, 128, 128, startingColonists: 10);
        Run(world, 3);
        Colony colony = world.Colonies[0];
        SettlementPlanner.Sync(colony);
        _ = colony.Spatial;
        string before = Fingerprint(world);
        int draw = world.Random.Next();
        var twin = new WorldState(4242, 128, 128, startingColonists: 10);
        Run(twin, 3);
        twin.Random.Next();

        foreach (BuildingType type in new[] { BuildingType.Hut, BuildingType.Kiln, BuildingType.Mill, BuildingType.Tavern })
            _ = Urbanism.FindSite(world.Map, colony, type);
        _ = Farming.FindFieldSite(world.Map, colony);

        Assert.Equal(Fingerprint(twin).Length, Fingerprint(world).Length);
        // L'état du monde (hors le tirage fait pour comparer) est inchangé : le plan, les objets et le prochain tirage sont les mêmes.
        Assert.Null(new WorldComparison().Difference(twin.Colonies[0].Layout, colony.Layout));
        Assert.Equal(twin.Random.Next(), world.Random.Next());
        _ = before; _ = draw;
    }

    [Fact]
    public void Une_demande_sans_terrain_attend_un_evenement_au_lieu_de_chercher_sans_cesse()
    {
        var world = new WorldState(10, 128, 128, startingColonists: 8, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        // La carte de cette graine n'a aucune eau vive à portée : un moulin est physiquement impossible.
        PlanRequest request = SettlementPlanner.RequestFor(colony, DevelopmentKind.Workshop, BuildingType.Mill, DevelopmentPriority.Production);
        for (int i = 0; i < 10 && SettlementPlanner.Poll(colony, request) == PlanningOutcome.Pending; i++)
            SettlementPlanningScheduler.Drain(world);
        Assert.NotEqual(PlanningOutcome.Pending, request.State);
        Assert.Equal(PlanningOutcome.WaitingForChange, request.State);
        Assert.Equal(PlacementFailureKind.NoSuitableTerrain, request.Failure);
        long searches = world.Planning.CompletedSearches;
        for (int hour = 0; hour < 24; hour++)
        {
            Assert.Equal(PlanningOutcome.WaitingForChange, SettlementPlanner.Poll(colony, request));
        }
        Assert.Equal(searches, world.Planning.CompletedSearches);
    }

    [Theory]
    [InlineData(4242)]
    [InlineData(777)]
    public void Aucune_superposition_la_place_protegee_et_chaque_porte_mene_au_camp(int seed)
    {
        var world = new WorldState(seed, 200, 200, startingColonists: 12);
        Run(world, 70);
        Colony colony = world.Colonies[0];
        SettlementLayout layout = colony.Layout;
        LocalMap map = world.Map;

        var occupied = new Dictionary<(int, int), string>();
        void Take(int x, int y, string what)
        {
            Assert.False(occupied.TryGetValue((x, y), out string? other), $"{what} chevauche {other} en ({x}, {y}).");
            occupied[(x, y)] = what;
        }
        foreach (Building b in colony.Buildings) foreach ((int x, int y) in b.Tiles) Take(x, y, $"{b.Type}#{b.Id}");
        foreach (Field f in colony.Fields) foreach (FieldPlot p in f.Plots) Take(p.X, p.Y, $"champ#{f.Id}");
        foreach (Grave g in colony.Graves.Where(g => g.X >= 0)) Take(g.X, g.Y, "tombe");

        foreach (int cell in layout.PlazaCells)
        {
            (int x, int y) = layout.Decode(cell);
            Assert.False(occupied.ContainsKey((x, y)), $"La place est occupée en ({x}, {y}).");
        }
        foreach (Building b in colony.Buildings.Where(b => !b.IsDam))
        {
            Assert.True(b.HasDoor, $"{b.Type} sans porte.");
            Assert.True(map.IsWalkable(b.AccessX, b.AccessY));
            Assert.False(b.Contains(b.AccessX, b.AccessY));
            Assert.NotNull(LocalNavigation(colony, b.AccessX, b.AccessY));
        }
        foreach (Building mill in colony.Buildings.Where(b => b.Type == BuildingType.Mill))
            Assert.True(Hydrology.MillFlow(map, mill) > 0f, "Un moulin sans débit.");
        foreach (Field f in colony.Fields)
            Assert.NotNull(LocalNavigation(colony, f.AccessX, f.AccessY));
    }

    private static object? LocalNavigation(Colony colony, int x, int y) =>
        GodColony.Simulation.Pathfinding.LocalNavigation.FindPath(colony, x, y, colony.GatherSpots[0].X, colony.GatherSpots[0].Y);

    [Fact]
    public void Seize_demandes_simultanees_respectent_le_plafond_global_d_operations()
    {
        var world = new WorldState(99, 128, 128, colonyCount: 8, startingColonists: 8);
        foreach (Colony colony in world.Colonies)
            foreach (BuildingType type in new[] { BuildingType.Hut, BuildingType.Kiln })
            {
                PlanRequest request = SettlementPlanner.RequestFor(colony, Urbanism.KindOf(type), type, DevelopmentPriority.Production);
                request.State = PlanningOutcome.Idle;
                SettlementPlanner.Poll(colony, request);
            }
        Assert.True(world.Planning.Jobs.Count >= 8);

        long expansions = world.Planning.Expansions, prefilters = world.Planning.Prefilters, cells = world.Planning.ValidationCells;
        while (world.Planning.Jobs.Count > 0 && world.Clock.Ticks < 40000)
        {
            world.Step();
            if (world.Clock.Ticks % SettlementRules.RendezvousTicks == 0)
            {
                Assert.InRange(world.Planning.Expansions - expansions, 0, SettlementRules.AStarExpansionBudget);
                Assert.InRange(world.Planning.Prefilters - prefilters, 0, SettlementRules.PrefilterBudget);
                Assert.InRange(world.Planning.ValidationCells - cells, 0, SettlementRules.ValidationCellBudget);
                Assert.True(world.Planning.Jobs.Count(j => j.Search is not null) <= SettlementRules.MaxActiveSearches);
                (expansions, prefilters, cells) = (world.Planning.Expansions, world.Planning.Prefilters, world.Planning.ValidationCells);
            }
        }
        Assert.Empty(world.Planning.Jobs);
    }
}
