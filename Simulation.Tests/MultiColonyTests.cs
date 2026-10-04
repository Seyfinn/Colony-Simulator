using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class MultiColonyTests(ITestOutputHelper output)
{
    private static WorldState TwoPeoples(int colonists = 10, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false, colonyCount: 2);

    private static float MountainShare(LocalMap map)
    {
        int mountain = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.IsMountain(x, y)) mountain++;
        return mountain / (float)(map.Width * map.Height);
    }

    [Fact]
    public void Chaque_colonie_a_sa_propre_carte_son_espece_et_son_camp()
    {
        WorldState world = TwoPeoples();
        Colony humans = world.Colonies[0], dwarves = world.Colonies[1];

        Assert.NotSame(humans.Map, dwarves.Map);
        Assert.NotSame(humans.Pathfinder, dwarves.Pathfinder);
        Assert.Same(humans.Map, world.Map);
        Assert.Equal(Species.Human, humans.Species);
        Assert.Equal(Species.Dwarf, dwarves.Species);
        Assert.Equal("Première colonie", humans.Name);
        Assert.Equal("Colonie naine", dwarves.Name);
        Assert.All(dwarves.Members, m => Assert.Equal(Species.Dwarf, m.Species));

        // Les nains vivent dans les montagnes : bien plus de roche que sur la carte des humains.
        float humanMountains = MountainShare(humans.Map), dwarfMountains = MountainShare(dwarves.Map);
        output.WriteLine($"Montagnes : humains {humanMountains:P0}, nains {dwarfMountains:P0}");
        Assert.True(dwarfMountains > humanMountains * 1.5f);

        // Chaque colon se déplace sur la carte de sa colonie.
        Assert.All(dwarves.Members, m => Assert.True(dwarves.Map.IsWalkable(m.TileX, m.TileY)));
    }

    [Fact]
    public void Les_talents_et_les_caracteres_suivent_l_espece()
    {
        WorldState world = TwoPeoples(colonists: 10);
        // Plusieurs colonies de chaque espèce pour lisser le hasard.
        var humans = new List<Colonist>();
        var dwarves = new List<Colonist>();
        for (int seed = 1; seed <= 6; seed++)
        {
            var w = new WorldState(seed, startingColonists: 10, migration: false, lifecycle: false, colonyCount: 2);
            humans.AddRange(w.Colonies[0].Members);
            dwarves.AddRange(w.Colonies[1].Members);
        }

        Assert.True(dwarves.Average(c => c.Skills.Talent(SkillType.Mining)) > humans.Average(c => c.Skills.Talent(SkillType.Mining)) + 0.15f);
        Assert.True(dwarves.Average(c => c.Skills.Talent(SkillType.Smithing)) > humans.Average(c => c.Skills.Talent(SkillType.Smithing)) + 0.15f);
        Assert.True(dwarves.Average(c => c.Skills.Talent(SkillType.Farming)) < humans.Average(c => c.Skills.Talent(SkillType.Farming)));
        Assert.True(dwarves.Average(c => c.Personality[Axis.Attachement]) > humans.Average(c => c.Personality[Axis.Attachement]) + 0.2f);
        Assert.NotNull(world);
    }

    [Fact]
    public void Un_nain_vit_deux_fois_plus_longtemps_et_garde_son_enfance_deux_fois_plus_longtemps()
    {
        WorldState world = TwoPeoples();
        Colony colony = world.Colonies[1];
        Colonist dwarf = colony.Members[0];

        // 8 ans de nain = 4 ans d'humain : encore un adolescent, alors qu'un humain de 8 ans serait un adulte.
        dwarf.GetType().GetProperty(nameof(Colonist.BirthTicks))!.SetValue(dwarf, colony.Clock.Ticks - 8L * TimeConstants.TicksPerYear);
        Assert.Equal(LifeStage.Teen, dwarf.Stage);
        Assert.Equal(4f, dwarf.EquivalentAge, 2);

        // La vieillesse arrive à 34 ans de nain (17 ans d'humain) et la mort est certaine à 46.
        Assert.Equal(0f, Lifecycle.OldAgeDeathChance(33f / Species.Dwarf.LifespanScale));
        Assert.Equal(1f, Lifecycle.OldAgeDeathChance(46f / Species.Dwarf.LifespanScale));
    }

    [Fact]
    public void Deux_colonies_vivent_une_annee_sans_se_gener_chacune_dans_son_monde()
    {
        WorldState world = TwoPeoples(colonists: 8);
        var watches = world.Colonies.ToDictionary(c => c, _ => new StarvationWatch());
        for (long i = 0; i < TimeConstants.TicksPerYear; i++)
        {
            world.Step();
            if (i % TimeConstants.TicksPerDay == 0)
                foreach (Colony c in world.Colonies)
                    watches[c].Observe(c);
        }

        foreach (Colony colony in world.Colonies)
        {
            output.WriteLine($"{colony.Name} : {colony.Members.Count} colons, nourriture {colony.Stock.FoodUnits}, bois {colony.Stock.Get(ResourceType.Wood)}, " +
                             $"pierre {colony.Stock.Get(ResourceType.Stone)}, minerai {colony.Stock.Get(ResourceType.IronOre)}, humeur {colony.AverageMood:P0}");
            // Les marchands en caravane ne sont pas au camp : on les compte, personne ne doit manquer.
            int away = world.Caravans.Where(c => c.From == colony).Sum(c => c.Traders.Count);
            Assert.Equal(8, colony.Members.Count + away);
            Assert.Null(watches[colony].Victim);
        }
    }
}
