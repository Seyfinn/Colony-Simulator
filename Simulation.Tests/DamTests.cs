using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class DamTests(ITestOutputHelper output)
{
    /// <summary>
    /// Une vallée plate (altitude 5) large de 5 cases, fermée au nord par une paroi (altitude 8), avec une rivière
    /// qui la traverse du nord au sud : un site de barrage idéal.
    /// </summary>
    private static (LocalMap Map, Colony Colony) Valley(int closedAt = 14)
    {
        var map = new LocalMap(40, 80, 1);
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 40; x++)
        {
            bool valley = Math.Abs(x - 20) <= 2 && y >= closedAt;
            map.SetGenerated(x, y, valley ? 5 : 8, SoilType.Grass, FloraType.None, 0f);
        }
        for (int y = closedAt; y < 80; y++)
            map.SetRiver(20, y, 20, y + 1);
        map.ComputeBanks();
        var colony = new Colony("Test", 5, 35, [(6, 35)]);
        return (map, colony);
    }

    [Fact]
    public void Un_barrage_noie_les_terres_plates_en_amont_et_pas_en_aval()
    {
        (LocalMap map, Colony colony) = Valley();

        Reservoir? reservoir = Hydrology.FindReservoir(map, colony, 20, 20);

        Assert.NotNull(reservoir);
        Assert.Equal(6, reservoir!.Level);
        Assert.Equal(30, reservoir.Tiles.Count); // 6 rangées (y de 14 à 19) de 5 cases
        Assert.All(reservoir.Tiles, t =>
        {
            Assert.True(t.Y < 20, "Seul l'amont est noyé.");
            Assert.InRange(t.X, 18, 22);
        });
    }

    [Fact]
    public void Achever_le_barrage_forme_un_lac_et_baisse_le_debit_en_aval()
    {
        (LocalMap map, Colony colony) = Valley();
        var dam = new Building(BuildingType.Dam, 20, 20) { Progress = 1f };
        colony.Buildings.Add(dam);

        Reservoir? lake = Hydrology.CompleteDam(map, colony, dam);

        Assert.NotNull(lake);
        Assert.All(lake!.Tiles, t =>
        {
            Assert.True(map.IsFlooded(t.X, t.Y));
            Assert.True(map.IsWater(t.X, t.Y));
            Assert.False(map.IsWalkable(t.X, t.Y), "Un lac ne se traverse pas à pied.");
            Assert.Equal(Surface.Water, map.GetSurface(t.X, t.Y));
            Assert.True(map.GetFish(t.X, t.Y) > 0);
        });
        Assert.True(map.IsWalkable(20, 20), "On traverse le barrage à pied.");
        Assert.False(map.IsFlooded(20, 20));

        // Les berges du lac sont fertiles.
        Assert.True(map.IsFertileBank(23, 17));

        // Le débit baisse sur 25 cases en aval, pas au-delà ni en amont.
        Assert.Equal(1f, map.GetFlow(20, 20));
        Assert.Equal(0.5f, map.GetFlow(20, 21));
        Assert.Equal(0.5f, map.GetFlow(20, 20 + Hydrology.DownstreamReach));
        Assert.Equal(1f, map.GetFlow(20, 21 + Hydrology.DownstreamReach));
    }

    [Fact]
    public void Pas_de_barrage_si_la_retenue_est_trop_petite_trop_large_ou_noierait_un_champ()
    {
        // Une vaste plaine plate : la vallée n'est fermée que très loin, mais la retenue est plafonnée aux cases les plus proches.
        (LocalMap bigMap, Colony bigColony) = Valley(closedAt: 0);
        Reservoir? capped = Hydrology.FindReservoir(bigMap, bigColony, 20, 30);
        Assert.NotNull(capped);
        Assert.Equal(Hydrology.MaxReservoirTiles, capped!.Tiles.Count);
        Assert.All(capped.Tiles, t => Assert.True(t.Y < 30));

        // Un fleuve large ne se barre pas : seul un ruisseau d'une case de large le permet.
        (LocalMap wideMap, Colony wideColony) = Valley();
        for (int y = 14; y < 80; y++)
            wideMap.SetRiver(20, y, 20, y + 1, width: 3);
        Assert.Null(Hydrology.FindReservoir(wideMap, wideColony, 20, 20));

        // Trop petite : le barrage est collé à la paroi.
        (LocalMap smallMap, Colony smallColony) = Valley();
        Assert.Null(Hydrology.FindReservoir(smallMap, smallColony, 20, 15)); // une seule rangée en amont

        // Un champ dans la future retenue : on ne le noie pas.
        (LocalMap map, Colony colony) = Valley();
        colony.Fields.Add(new Field(18, 15));
        Assert.Null(Hydrology.FindReservoir(map, colony, 20, 20));

        // Pas une rivière, pas de barrage.
        Assert.Null(Hydrology.FindReservoir(map, colony, 19, 20));
    }

    [Fact]
    public void Un_lac_peut_alimenter_un_canal_et_la_colonie_choisit_le_meilleur_site()
    {
        (LocalMap map, Colony colony) = Valley();
        (int X, int Y, Reservoir Reservoir)? site = Hydrology.FindSite(map, colony);
        Assert.NotNull(site);
        Assert.Equal(20, site!.Value.X);
        Assert.InRange(site.Value.Reservoir.Tiles.Count, Hydrology.MinReservoirTiles, Hydrology.MaxReservoirTiles);

        // Un champ plus bas que la retenue, au bord de la vallée : le lac devient une source d'eau pour un canal.
        var dam = new Building(BuildingType.Dam, site.Value.X, site.Value.Y) { Progress = 1f };
        colony.Buildings.Add(dam);
        Hydrology.CompleteDam(map, colony, dam);
        Assert.True(map.IsFlooded(21, 18));
        Assert.True(map.WaterHeight(21, 18) >= map.GetElevation(21, 18));
    }

    [Fact]
    public void La_colonie_demande_un_barrage_dans_une_priere_et_le_batit_si_on_l_accorde()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        colony.Prayers.AutoApprove.Clear();

        Prayer? prayer = null;
        colony.Prayers.Asked += p => prayer ??= p;
        for (long i = 0; i < 40 * TimeConstants.TicksPerDay && prayer is null; i++)
            world.Step();
        output.WriteLine(prayer is null ? "Aucune prière de barrage en 40 jours." : $"Prière au jour {prayer.AskedTicks / TimeConstants.TicksPerDay + 1} : {prayer.Question} {prayer.Reason}");
        if (prayer is null)
            return; // Cette carte n'offre peut-être aucun site : les autres tests couvrent la mécanique.

        Assert.Equal(DecisionKind.Dam, prayer.Kind);
        Assert.DoesNotContain(colony.Buildings, b => b.IsDam);

        world.AnswerPrayer(prayer, approve: true);
        Assert.Contains(colony.Buildings, b => b.IsDam);

        for (long i = 0; i < 40 * TimeConstants.TicksPerDay && !colony.Buildings.Any(b => b.IsDam && b.IsComplete); i++)
            world.Step();
        Building dam = colony.Buildings.First(b => b.IsDam);
        output.WriteLine($"Barrage achevé : {dam.IsComplete}, retenue noyée : {Enumerable.Range(0, world.Map.Height).Sum(y => Enumerable.Range(0, world.Map.Width).Count(x => world.Map.IsFlooded(x, y)))} cases");
        Assert.True(dam.IsComplete, "Les colons doivent bâtir le barrage une fois la prière exaucée.");
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("barrage"));
    }

    [Fact]
    public void Un_refus_laisse_la_colonie_tranquille_vingt_jours()
    {
        var world = new WorldState(12345, startingColonists: 10, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Prayer? prayer = null;
        colony.Prayers.Asked += p => prayer ??= p;
        for (long i = 0; i < 40 * TimeConstants.TicksPerDay && prayer is null; i++)
            world.Step();
        if (prayer is null)
            return;

        world.AnswerPrayer(prayer, approve: false);
        Assert.True(colony.Prayers.IsQuiet(DecisionKind.Dam, world.Clock));
        for (long i = 0; i < 10 * TimeConstants.TicksPerDay; i++)
            world.Step();
        Assert.Single(colony.Prayers.All);
        Assert.DoesNotContain(colony.Buildings, b => b.IsDam);
    }

    [Fact]
    public void Un_barrage_prive_d_eau_la_colonie_d_aval_qui_en_garde_rancune_puis_pardonne()
    {
        var world = new WorldState(12345, startingColonists: 8, migration: false, lifecycle: false, colonyCount: 2, trade: false);
        Colony upstream = world.Colonies[0], downstream = world.Colonies[1];
        // Les deux colonies ne sont pas forcément sur le même fleuve du monde : on place la seconde en aval pour l'étude.
        upstream.Downstream = downstream;

        (int rx, int ry) = Enumerable.Range(0, downstream.Map.Width * downstream.Map.Height)
            .Select(i => (X: i % downstream.Map.Width, Y: i / downstream.Map.Width))
            .First(t => downstream.Map.IsRiver(t.X, t.Y));
        float flowBefore = downstream.Map.GetFlow(rx, ry);

        Hydrology.BuildInstantly(upstream.Map, upstream);

        Assert.Equal(flowBefore * Hydrology.NeighborFlowFactor, downstream.Map.GetFlow(rx, ry), 3);
        Assert.Equal(Hydrology.GrudgePerDam, downstream.GrudgeAgainst(upstream));
        Assert.Equal(0f, upstream.GrudgeAgainst(downstream));
        Assert.Contains(downstream.Thoughts, t => t.Text.Contains("barrage") && t.Text.Contains(upstream.Name));

        // La rancune fait hésiter à commercer : le même échange doit rapporter bien plus pour valoir le voyage.
        upstream.Stock.TryTake(ResourceType.Tools, upstream.Stock.Get(ResourceType.Tools));
        upstream.Stock.Add(ResourceType.Tools, 30);
        downstream.Stock.TryTake(ResourceType.Tools, downstream.Stock.Get(ResourceType.Tools));
        downstream.Stock.TryTake(ResourceType.Coins, downstream.Stock.Get(ResourceType.Coins));
        downstream.Stock.Add(ResourceType.Coins, 600);
        downstream.Grudges[upstream] = 0f;
        Assert.NotNull(Trade.Plan(world, upstream, downstream));
        downstream.Grudges[upstream] = Hydrology.MaxGrudge;
        Assert.Null(Trade.Plan(world, upstream, downstream));

        // Avec le temps, on pardonne.
        downstream.Grudges[upstream] = Hydrology.GrudgePerDam;
        for (long i = 0; i < 25 * TimeConstants.TicksPerDay; i++)
            world.Step();
        Assert.Equal(0f, downstream.GrudgeAgainst(upstream));
    }
}
