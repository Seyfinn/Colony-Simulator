using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class IrrigationTests(ITestOutputHelper output)
{
    private static WorldState Closed(int colonists, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false);

    [Fact]
    public void Un_canal_creuse_irrigue_les_terres_autour_et_augmente_la_recolte()
    {
        WorldState world = Closed(8);
        LocalMap map = world.Map;
        (int x, int y) = (world.Colonies[0].CampX + 8, world.Colonies[0].CampY - 6);
        Assert.False(map.IsIrrigated(x, y));

        map.DigCanal(x, y);
        Assert.True(map.IsCanal(x, y));
        Assert.False(map.IsCanalWet(x, y));
        Assert.False(map.IsIrrigated(x, y), "Un fossé à sec n'irrigue rien.");
        Assert.Equal(Surface.Dirt, map.GetSurface(x, y));

        map.FillCanal(x, y);
        Assert.True(map.IsCanalWet(x, y));
        Assert.Equal(Surface.River, map.GetSurface(x, y));
        Assert.True(map.IsIrrigated(x + LocalMap.IrrigationReach, y));
        Assert.False(map.IsIrrigated(x + LocalMap.IrrigationReach + 1, y));
        Assert.Equal((int)MathF.Round((Farming.PlotYield + Farming.IrrigationBonus + (map.IsFertileBank(x, y) ? Farming.BankBonus : 0)) * Farming.CropCycleFactor),
            Farming.YieldAt(map, x, y));
    }

    [Fact]
    public void L_eau_n_avance_que_dans_un_fosse_continu_depuis_la_source()
    {
        var tiles = new List<(int X, int Y)> { (10, 10), (10, 11), (10, 12), (10, 13) };
        var canal = new Canal(tiles, new Field(10, 14));

        Assert.Empty(canal.MarkDug(10, 12));   // pas de source creusée : rien ne coule
        Assert.Empty(canal.MarkDug(10, 13));
        Assert.Equal(0, canal.Flowing);

        Assert.Single(canal.MarkDug(10, 10));  // la source : l'eau avance d'une case
        Assert.Equal(1, canal.Flowing);

        // La case manquante comblée, l'eau court d'un coup jusqu'au bout.
        List<(int X, int Y)> filled = canal.MarkDug(10, 11);
        Assert.Equal(3, filled.Count);
        Assert.Equal(4, canal.Flowing);
        Assert.True(canal.IsComplete);
    }

    [Fact]
    public void Le_trace_part_de_l_eau_descend_jusqu_au_champ_et_evite_les_batiments()
    {
        WorldState world = Closed(10);
        Colony colony = world.Colonies[0];
        LocalMap map = world.Map;

        Canal? canal = Irrigation.PlanBest(map, colony);
        Assert.NotNull(canal);
        var tiles = canal!.Tiles;
        output.WriteLine($"Canal de {tiles.Count} cases vers le champ ({canal.Target.X}, {canal.Target.Y}) : " +
                         string.Join(" ", tiles.Select(t => $"({t.X},{t.Y})@{map.GetElevation(t.X, t.Y)}")));
        Assert.InRange(tiles.Count, 1, Irrigation.MaxLength);

        // Chaque case touche la suivante, et l'eau ne remonte jamais.
        for (int i = 1; i < tiles.Count; i++)
        {
            Assert.Equal(1, Math.Abs(tiles[i].X - tiles[i - 1].X) + Math.Abs(tiles[i].Y - tiles[i - 1].Y));
            int drop = map.GetElevation(tiles[i - 1].X, tiles[i - 1].Y) - map.GetElevation(tiles[i].X, tiles[i].Y);
            Assert.InRange(drop, 0, 1);
        }

        // La première case touche une rivière au moins aussi haute ; la dernière borde le champ.
        (int sx, int sy) = tiles[0];
        Assert.Contains(new[] { (1, 0), (-1, 0), (0, 1), (0, -1) },
            d => map.InBounds(sx + d.Item1, sy + d.Item2) && map.IsRiver(sx + d.Item1, sy + d.Item2)
                 && map.GetElevation(sx + d.Item1, sy + d.Item2) >= map.GetElevation(sx, sy));
        (int ex, int ey) = tiles[^1];
        Assert.Contains(new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }, d => canal.Target.Contains(ex + d.Item1, ey + d.Item2));

        // Ni sur un champ, ni sur un bâtiment, ni sur l'eau.
        Assert.All(tiles, t =>
        {
            Assert.DoesNotContain(colony.Fields, f => f.Contains(t.X, t.Y));
            Assert.DoesNotContain(colony.Buildings, b => b.Contains(t.X, t.Y));
            Assert.False(map.IsWaterway(t.X, t.Y));
        });
    }

    [Fact]
    public void Aucun_canal_si_le_champ_est_plus_haut_que_toute_eau()
    {
        WorldState world = Closed(10);
        Colony colony = world.Colonies[0];
        // Sans rivière à portée, pas de route (on ne remonte jamais l'eau).
        var far = new Field(2, 2);
        colony.Fields.Clear();
        colony.Fields.Add(far);
        Assert.Null(Irrigation.FindRoute(world.Map, colony, far));
    }

    [Fact]
    public void La_colonie_creuse_un_canal_irrigue_ses_champs_et_recolte_davantage()
    {
        WorldState world = Closed(10);
        Colony colony = world.Colonies[0];
        int maxIrrigated = 0;
        int maxYield = 0;
        bool announcedStart = false, announcedDone = false;

        for (int day = 1; day <= 60; day++)
        {
            for (long i = 0; i < TimeConstants.TicksPerDay; i++)
                world.Step();
            // Les pensées les plus anciennes s'effacent (trente au plus) : on les guette au fil des jours.
            announcedStart |= colony.Thoughts.Any(t => t.Text.Contains("nous creusons un canal"));
            announcedDone |= colony.Thoughts.Any(t => t.Text.Contains("canal est achevé"));
            maxIrrigated = Math.Max(maxIrrigated, Farming.Plots(colony).Count(p => world.Map.IsIrrigated(p.X, p.Y)));
            foreach (FieldPlot plot in Farming.Plots(colony))
                maxYield = Math.Max(maxYield, Farming.YieldAt(world.Map, plot.X, plot.Y));
            if (day % 10 == 0)
                output.WriteLine($"J{day}: canaux {colony.Canals.Count} (creusés {colony.Canals.Sum(c => c.DugCount)}/{colony.Canals.Sum(c => c.Tiles.Count)}), " +
                                 $"parcelles irriguées {Farming.Plots(colony).Count(p => world.Map.IsIrrigated(p.X, p.Y))}/{Farming.Plots(colony).Count()}");
        }
        output.WriteLine("Coûts : " + ColonyBrain.CostSummary(colony.Labor));
        foreach (Thought thought in colony.Thoughts.Where(t => t.Text.Contains("canal")))
            output.WriteLine($"  J{thought.Ticks / TimeConstants.TicksPerDay + 1}  {thought.Text}");

        Assert.NotEmpty(colony.Canals);
        Assert.Contains(colony.Canals, c => c.IsComplete);
        Assert.True(maxIrrigated > 0, "Des parcelles doivent être irriguées.");
        Assert.True(maxYield >= Farming.PlotYield + Farming.IrrigationBonus);
        Assert.NotNull(colony.Labor.HoursPerCanalTile);
        Assert.True(announcedStart && announcedDone, "La colonie doit annoncer le début et la fin des travaux.");
    }

    [Fact]
    public void Les_canaux_sont_a_gue_et_barrent_les_champs_et_les_huttes()
    {
        WorldState world = Closed(10);
        Colony colony = world.Colonies[0];
        Canal canal = Irrigation.PlanBest(world.Map, colony)!;
        colony.Canals.Add(canal);
        foreach ((int x, int y) in canal.Tiles)
            colony.CanalTiles.Add((x, y));

        for (int i = 0; i < 10; i++)
        {
            if (Urbanism.FindHutSite(world.Map, colony) is not { } site)
                break;
            Building hut = Urbanism.PlanHut(world.Map, colony, site.X, site.Y);
            Assert.All(hut.Tiles, t => Assert.DoesNotContain(t, canal.Tiles));
        }
        Assert.All(canal.Tiles, t => Assert.True(world.Map.IsWalkable(t.X, t.Y)));
    }
}
