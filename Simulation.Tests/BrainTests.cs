using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class BrainTests(ITestOutputHelper output)
{
    private static (WorldState World, Colony Colony) ColonyWith(int food, int wood, int stone)
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Set(colony, ResourceType.Food, food);
        Set(colony, ResourceType.Wood, wood);
        Set(colony, ResourceType.Stone, stone);
        return (world, colony);
    }

    /// <summary>Bâtit instantanément assez de huttes pour loger tout le monde.</summary>
    private static void HouseEveryone(WorldState world, Colony colony)
    {
        foreach (Building site in colony.ConstructionSites.ToList())
            colony.Buildings.Remove(site);
        while (colony.Homeless > 0)
        {
            (int x, int y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
            Building hut = Urbanism.PlanHut(world.Map, colony, x, y);
            hut.Progress = 1f;
            colony.FillVacancies();
        }
    }

    private static void Set(Colony colony, ResourceType type, int amount)
    {
        colony.Stock.TryTake(type, colony.Stock.Get(type));
        colony.Stock.Add(type, amount);
    }

    private static void ThinkSeveralHours(Colony colony, WorldState world, GameClock clock)
    {
        for (int i = 0; i < 8; i++)
            ColonyBrain.Think(colony, world.Map, clock);
    }

    [Fact]
    public void En_cas_de_famine_tout_le_monde_part_chercher_a_manger_et_la_carriere_attend()
    {
        (WorldState world, Colony colony) = ColonyWith(food: 0, wood: 200, stone: 0);
        ThinkSeveralHours(colony, world, world.Clock);

        Assert.True(colony.WorkShares[WorkSector.Food] > 0.6f);
        Assert.True(colony.WorkShares[WorkSector.Stone] < 0.01f, "Pas de carrière tant que la survie n'est pas assurée.");
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("nourriture"));
    }

    [Fact]
    public void Dans_l_abondance_la_colonie_prend_du_temps_libre()
    {
        (WorldState world, Colony colony) = ColonyWith(food: 1000, wood: 1000, stone: 1000);
        HouseEveryone(world, colony);
        // Une colonie prospère garde des lits d'avance : on lui en donne, pour qu'aucun chantier ne vienne troubler le test.
        (int x, int y) = Urbanism.FindHutSite(world.Map, colony)!.Value;
        Urbanism.PlanHut(world.Map, colony, x, y).Progress = 1f;
        colony.Fields.Clear(); // les champs réclameraient des bras pour les semailles : on teste ici la pyramide seule
        colony.Stock.Add(ResourceType.Tools, 100); // la colonie est équipée : ni fer à chercher, ni outils à forger
        colony.IronSeen = true;
        ThinkSeveralHours(colony, world, world.Clock);

        Assert.True(colony.WorkShares[WorkSector.Free] > 0.9f);
        Assert.True(colony.Members.Count(m => m.Sector == WorkSector.Free) >= 18);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("temps libre"));
    }

    [Fact]
    public void Quand_la_survie_est_assuree_la_colonie_ouvre_la_carriere()
    {
        (WorldState world, Colony colony) = ColonyWith(food: 1000, wood: 1000, stone: 0);
        HouseEveryone(world, colony);
        ThinkSeveralHours(colony, world, world.Clock);

        Assert.True(colony.WorkShares[WorkSector.Stone] > 0.3f);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("carrière"));
    }

    [Fact]
    public void Tant_que_des_colons_dorment_dehors_la_carriere_attend_et_on_construit()
    {
        (WorldState world, Colony colony) = ColonyWith(food: 1000, wood: 1000, stone: 0);
        ThinkSeveralHours(colony, world, world.Clock);

        Assert.True(colony.WorkShares[WorkSector.Construction] > 0.2f);
        Assert.True(colony.WorkShares[WorkSector.Stone] < 0.01f);
        Assert.NotEmpty(colony.ConstructionSites);
    }

    [Fact]
    public void En_automne_la_colonie_anticipe_l_hiver_en_coupant_du_bois()
    {
        (WorldState world, Colony colony) = ColonyWith(food: 1000, wood: 30, stone: 1000);
        var autumn = new GameClock(TimeConstants.TicksPerDay * 10 + TimeConstants.TicksPerDay / 2);
        Assert.Equal(Season.Automne, autumn.Season);

        ThinkSeveralHours(colony, world, autumn);

        output.WriteLine(string.Join("\n", colony.Thoughts.Select(t => t.Text)));
        Assert.True(colony.WorkShares[WorkSector.Wood] > 0.2f);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("hiver"));
    }

    [Fact]
    public void Sans_bois_le_feu_s_eteint_en_hiver()
    {
        (WorldState _, Colony colony) = ColonyWith(food: 100, wood: 0, stone: 0);
        var winter = new GameClock(TimeConstants.TicksPerDay * 15 + TimeConstants.TicksPerDay * 20 / 24);
        ColonyBrain.LightFire(colony, winter);

        Assert.False(colony.FireLit);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("feu"));
    }

    [Fact]
    public void Sur_une_annee_entiere_la_colonie_survit_seule()
    {
        var world = new WorldState(12345, startingColonists: 20, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        int coldNightsWithoutFire = 0;
        var watch = new StarvationWatch();

        for (long i = 0; i < TimeConstants.TicksPerYear; i++)
        {
            world.Step();
            if (i % TimeConstants.TicksPerDay == 0)
                watch.Observe(colony);
            if (world.Clock.Hour == 21 && world.Clock.Minute == 0 && ColonyBrain.IsColdSeason(world.Clock.Season) && !colony.FireLit)
                coldNightsWithoutFire++;
        }

        output.WriteLine($"Stock : nourriture {colony.Stock.Get(ResourceType.Food)}, bois {colony.Stock.Get(ResourceType.Wood)}, " +
                         $"pierre {colony.Stock.Get(ResourceType.Stone)}, fer {colony.Stock.Get(ResourceType.IronOre)}, " +
                         $"humeur {colony.AverageMood:P0}, nuits froides sans feu {coldNightsWithoutFire}");
        foreach (WorkSector sector in WorkSectors.All)
            output.WriteLine($"  {sector} : {colony.WorkShares[sector]:P0}");
        foreach (Thought thought in colony.Thoughts)
            output.WriteLine($"  [jour {thought.Ticks / TimeConstants.TicksPerDay}] {thought.Text}");

        output.WriteLine($"Huttes : {colony.Buildings.Count(b => b.IsComplete)} achevées, {colony.ConstructionSites.Count()} en chantier, {colony.Homeless} colons dehors");

        Assert.Null(watch.Victim);
        Assert.Equal(0, coldNightsWithoutFire);
        Assert.True(colony.Labor.TotalProduced(ResourceType.Stone) > 0, "La carrière devrait avoir été exploitée à un moment.");
        Assert.True(colony.Buildings.Count(b => b.IsComplete) >= 4, "La colonie devrait s'être bâti des huttes.");
        Assert.True(colony.Homeless <= 4, "Presque tout le monde devrait dormir à l'abri au bout d'un an.");
    }
}
