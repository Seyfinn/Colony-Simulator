using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using Xunit.Abstractions;

namespace GodColony.Simulation.Tests;

public class KnowledgeTests(ITestOutputHelper output)
{
    private static WorldState Peoples(int count = 1, int colonists = 10, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false, colonyCount: count, trade: false);

    [Fact]
    public void Chaque_peuple_part_avec_ses_propres_savoirs()
    {
        WorldState world = Peoples(count: 4);
        Colony humans = world.Colonies[0], dwarves = world.Colonies[1], elves = world.Colonies[2], orcs = world.Colonies[3];

        Assert.All(world.Colonies, c => Assert.True(Knowledge.Has(c, Discovery.Agriculture)));
        Assert.False(Knowledge.Has(humans, Discovery.Metallurgy));
        Assert.True(Knowledge.Has(dwarves, Discovery.Metallurgy));
        Assert.True(Knowledge.Has(elves, Discovery.Medicine));
        Assert.True(Knowledge.Has(orcs, Discovery.Husbandry));
        Assert.Equal(Age.Wood, Knowledge.AgeOf(humans));
        Assert.Equal(Age.Iron, Knowledge.AgeOf(dwarves));
    }

    [Fact]
    public void Un_atelier_dont_on_ignore_le_savoir_attend_qu_on_l_ait_decouvert()
    {
        WorldState world = Peoples();
        Colony colony = world.Colonies[0];
        colony.IronSeen = true;
        colony.Stock.TryTake(ResourceType.Tools, colony.Stock.Get(ResourceType.Tools));

        // Le fer est là, mais les humains ne savent pas le travailler : pas de charbonnière, ils étudient la métallurgie.
        Assert.Equal(BuildingType.Kiln, ToolChain.NextWorkshopToBuild(colony));
        Assert.Null(Crafting.NextWorkshopToBuild(colony, colony.Map));
        Assert.Equal(Discovery.Metallurgy, Knowledge.Wish(world, colony));
        Assert.Equal(Discovery.Metallurgy, Knowledge.ChooseResearch(world, colony));

        Knowledge.Discover(colony, Discovery.Metallurgy, world.Clock);
        Assert.Equal(BuildingType.Kiln, Crafting.NextWorkshopToBuild(colony, colony.Map));
        Assert.Equal(Age.Iron, Knowledge.AgeOf(colony));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("Métallurgie"));
    }

    [Fact]
    public void Le_village_passe_les_batiments_qu_il_ne_sait_pas_encore_batir()
    {
        WorldState world = Peoples(colonists: 12);
        Colony colony = world.Colonies[0];
        // Douze habitants : enclos, puits, entrepôt (bientôt), infirmerie, marché, taverne… Mais sans médecine, pas d'infirmerie.
        foreach (BuildingType done in new[] { BuildingType.Pen, BuildingType.Well })
            Urbanism.BuildInstantly(colony.Map, colony, done);
        var candidates = Civic.Candidates(colony).ToList();
        Assert.Contains(BuildingType.Infirmary, candidates);
        Assert.False(Knowledge.Allows(colony, BuildingType.Infirmary));
        Assert.NotEqual(BuildingType.Infirmary, Civic.NextToBuild(colony));

        Knowledge.Discover(colony, Discovery.Medicine, world.Clock);
        Assert.Equal(BuildingType.Infirmary, Civic.NextToBuild(colony));
    }

    [Fact]
    public void On_etudie_d_abord_ce_que_demande_un_savoir()
    {
        WorldState world = Peoples();
        Colony colony = world.Colonies[0];
        // L'écriture demande le commerce ; l'hydraulique demande l'irrigation (la maçonnerie est déjà connue des humains).
        Assert.Equal(Discovery.Commerce, Knowledge.StepToward(colony, Discovery.Writing));
        Assert.Equal(Discovery.Irrigation, Knowledge.StepToward(colony, Discovery.Hydraulics));
        Assert.False(Knowledge.CanResearch(colony, Discovery.Writing));
        Assert.True(Knowledge.CanResearch(colony, Discovery.Commerce));
        Assert.Null(Knowledge.StepToward(colony, Discovery.Masonry));
    }

    [Fact]
    public void Le_savoir_s_accumule_chaque_jour_jusqu_a_la_decouverte()
    {
        WorldState world = Peoples();
        Colony colony = world.Colonies[0];
        float daily = Knowledge.DailyPoints(colony);
        Assert.InRange(daily, 0.6f, 2.5f);

        Discovery? target = Knowledge.ChooseResearch(world, colony);
        Assert.NotNull(target);
        float cost = Knowledge.Info(target.Value).Cost;
        int days = 0;
        while (!Knowledge.Has(colony, target.Value) && days < 100)
        {
            Knowledge.Daily(world, colony);
            days++;
        }
        Assert.True(Knowledge.Has(colony, target.Value));
        Assert.InRange(days, (int)(cost / daily) - 1, (int)Math.Ceiling(cost / daily) + 1);

        // Une école accélère tout.
        Urbanism.BuildInstantly(colony.Map, colony, BuildingType.School);
        Assert.Equal(daily * Knowledge.SchoolFactor, Knowledge.DailyPoints(colony), 3);
    }

    [Fact]
    public void Les_caravanes_font_circuler_les_savoirs_deux_fois_plus_vite_entre_allies()
    {
        WorldState world = Peoples(count: 2);
        Colony humans = world.Colonies[0], dwarves = world.Colonies[1];
        float cost = Knowledge.Info(Discovery.Metallurgy).Cost;

        Knowledge.Share(world, humans, dwarves);
        Assert.Equal(Knowledge.TradeShare * cost, Knowledge.Progress(humans, Discovery.Metallurgy), 3);
        // Les nains apprennent l'élevage des humains de la même façon.
        Assert.True(Knowledge.Progress(dwarves, Discovery.Husbandry) > 0f);

        Diplomacy.SealAlliance(world, humans, dwarves);
        Knowledge.Share(world, humans, dwarves);
        Assert.Equal(3 * Knowledge.TradeShare * cost, Knowledge.Progress(humans, Discovery.Metallurgy), 3);
        for (int visit = 0; visit < 10 && !Knowledge.Has(humans, Discovery.Metallurgy); visit++)
            Knowledge.Share(world, humans, dwarves);
        Assert.True(Knowledge.Has(humans, Discovery.Metallurgy));
        Assert.Contains(humans.Thoughts, t => t.Text.Contains("marchands de " + dwarves.Name));
    }

    [Fact]
    public void Les_savoirs_du_bourg_donnent_leurs_avantages()
    {
        WorldState world = Peoples();
        Colony colony = world.Colonies[0];
        (int x, int y) = (colony.CampX + 3, colony.CampY + 3);
        int before = Farming.YieldAt(colony, colony.Map, x, y);
        Colonist someone = colony.Members[0];
        float sickness = Health.SicknessChancePerDay(colony, someone, world.Clock);

        foreach (Discovery discovery in new[] { Discovery.CropRotation, Discovery.Herbalism, Discovery.Fortification })
            Knowledge.Discover(colony, discovery, world.Clock);

        Assert.Equal(before + Knowledge.CropRotationBonus, Farming.YieldAt(colony, colony.Map, x, y));
        Assert.Equal(sickness * Knowledge.HerbalismFactor, Health.SicknessChancePerDay(colony, someone, world.Clock), 5);
        Assert.Equal(Age.Town, Knowledge.AgeOf(colony));
    }

    [Fact]
    public void En_quelques_annees_une_colonie_progresse_d_age_en_age()
    {
        var world = new WorldState(2024, startingColonists: 10);
        Colony colony = world.Colonies[0];
        for (long i = 0; i < 4L * TimeConstants.TicksPerYear; i++)
            world.Step();
        output.WriteLine($"{colony.Members.Count} habitants, {Knowledge.AgeName(Knowledge.AgeOf(colony))} : {string.Join(", ", colony.Known.Keys.Select(Knowledge.Name))}");

        // Les humains partent avec trois savoirs ; seuls, sans caravane pour en apprendre, ils en découvrent au moins trois de plus.
        Assert.True(colony.Known.Count >= 6, $"Seulement {colony.Known.Count} savoirs en quatre ans.");
        Assert.True(Knowledge.AgeOf(colony) >= Age.Iron);
        // Tout ce qui est bâti était connu.
        Assert.All(colony.Buildings, b => Assert.True(Knowledge.Allows(colony, b.Type), $"{b.Type} bâti sans le savoir"));
    }
}
