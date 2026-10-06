using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using Xunit;

namespace GodColony.Simulation.Tests;

/// <summary>Services partagés : places comptées en route comme sur place, trajet plafonné, effets réservés aux usagers présents, saturation qui motive un site de plus.</summary>
public sealed class CivicServicesTests
{
    private static (WorldState World, Colony Colony) Village(int people = 24)
    {
        var world = new WorldState(12345, startingColonists: people, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        colony.Stock.Add(ResourceType.Bread, 3000);
        colony.Stock.Add(ResourceType.Wood, 600);
        colony.Sensors = ColonyBrain.Sense(colony, world.Clock);
        return (world, colony);
    }

    private static Building Site(Colony colony, BuildingType type)
    {
        Building site = Urbanism.BuildInstantly(colony.Map, colony, type)!;
        Assert.NotNull(site);
        return site;
    }

    private static void Place(Colonist colonist, float x, float y) { colonist.X = colonist.PrevX = x; colonist.Y = colonist.PrevY = y; }

    private static Activity Go(Colonist colonist, ActivityKind kind, Building site, bool started = false) =>
        colonist.Activity = new Activity(kind, site.X, site.Y, 100) { Building = site, Started = started };

    [Fact]
    public void L_ecole_accueille_huit_eleves_en_route_comme_sur_place_et_refuse_le_neuvieme()
    {
        var (_, colony) = Village();
        Building school = Site(colony, BuildingType.School);
        List<Colonist> pupils = colony.Members.Take(9).ToList();
        pupils.ForEach(p => Place(p, school.X + 0.5f, school.Y + 3.5f));

        for (int i = 0; i < 8; i++)
        {
            Assert.Contains(school, CivicServices.Candidates(colony, pupils[i], CivicUse.Study));
            Go(pupils[i], ActivityKind.Study, school, started: i % 2 == 0); // les uns sont arrivés, les autres en route : tous comptent
        }
        Assert.Equal(8, CivicServices.Occupancy(colony, school, CivicUse.Study));
        Assert.Empty(CivicServices.Candidates(colony, pupils[8], CivicUse.Study));

        pupils[3].Activity = null; // l'activité qui disparaît libère sa place
        Assert.Equal(7, CivicServices.Occupancy(colony, school, CivicUse.Study));
        Assert.Contains(school, CivicServices.Candidates(colony, pupils[8], CivicUse.Study));
    }

    [Fact]
    public void Un_service_trop_loin_n_est_pas_propose_et_plusieurs_quartiers_partagent_le_meme_site()
    {
        var (_, colony) = Village();
        Building tavern = Site(colony, BuildingType.Tavern);
        Colonist near = colony.Members[0], other = colony.Members[1], far = colony.Members[2];
        Place(near, tavern.X + 0.5f, tavern.Y + 4.5f);
        Place(other, tavern.X - 6.5f, tavern.Y + 2.5f); // un autre quartier, à portée lui aussi
        Place(far, tavern.X + 0.5f + 80, tavern.Y + 0.5f);
        Assert.Equal([tavern], CivicServices.Candidates(colony, near, CivicUse.Relax));
        Assert.Equal([tavern], CivicServices.Candidates(colony, other, CivicUse.Relax));
        Assert.Empty(CivicServices.Candidates(colony, far, CivicUse.Relax)); // aucun service à distance

        // Huit places, deux quartiers : la neuvième personne attendra, quel que soit son quartier.
        foreach (Colonist c in colony.Members.Skip(3).Take(8))
        {
            Place(c, tavern.X + 0.5f, tavern.Y + 4.5f);
            Go(c, ActivityKind.Relax, tavern);
        }
        Assert.Empty(CivicServices.Candidates(colony, near, CivicUse.Relax));
        Assert.Empty(CivicServices.Candidates(colony, other, CivicUse.Relax));
    }

    [Fact]
    public void Les_soins_ne_profitent_qu_aux_patients_installes_et_un_seul_soignant_travaille_par_infirmerie()
    {
        var (world, colony) = Village();
        Building infirmary = Site(colony, BuildingType.Infirmary);
        Colonist present = colony.Members[0], elsewhere = colony.Members[1], healer = colony.Members[2], second = colony.Members[3];
        foreach (Colonist patient in new[] { present, elsewhere })
            Health.Fall(colony, patient, Ailment.Sick, 40, world.Clock, "");
        Go(present, ActivityKind.Relax, infirmary, started: true); // installé à l'infirmerie
        Place(elsewhere, infirmary.X + 0.5f + 60, infirmary.Y + 0.5f); // malade chez lui, loin
        Assert.Equal([present], CivicServices.PatientsAt(colony, infirmary));

        Place(healer, infirmary.X + 0.5f, infirmary.Y + 3.5f); Place(second, infirmary.X + 1.5f, infirmary.Y + 3.5f);
        Assert.Contains(infirmary, CivicServices.Candidates(colony, healer, CivicUse.Heal));
        Go(healer, ActivityKind.Heal, infirmary);
        Assert.Empty(CivicServices.Candidates(colony, second, CivicUse.Heal)); // un soignant actif par bâtiment

        Health.Treat(colony, CivicServices.PatientsAt(colony, infirmary));
        Assert.True(present.Treated);
        Assert.False(elsewhere.Treated);
        Assert.Equal(40, elsewhere.AilmentHours);
        Assert.True(present.AilmentHours < 40);
    }

    [Fact]
    public void Le_soin_sans_patient_ne_consomme_pas_les_plantes()
    {
        var (_, colony) = Village();
        colony.Stock.Add(ResourceType.Herbs, 2);
        Health.Treat(colony, []);
        Assert.Equal(2, colony.Stock.Get(ResourceType.Herbs));
    }

    [Fact]
    public void Quatre_lits_par_infirmerie_et_le_repos_de_base_reste_possible_hors_batiment()
    {
        var (world, colony) = Village();
        Building infirmary = Site(colony, BuildingType.Infirmary);
        List<Colonist> sick = colony.Members.Take(5).ToList();
        foreach (Colonist s in sick) { Health.Fall(colony, s, Ailment.Sick, 20, world.Clock, ""); Place(s, infirmary.X + 0.5f, infirmary.Y + 3.5f); }
        for (int i = 0; i < 4; i++) Go(sick[i], ActivityKind.Relax, infirmary);
        Assert.Empty(CivicServices.Candidates(colony, sick[4], CivicUse.Recover)); // plein : il se repose près du feu, comme sans infirmerie

        int before = sick[4].AilmentHours;
        Health.Hourly(world, colony);
        Assert.Equal(before - 1, sick[4].AilmentHours); // la convalescence ordinaire continue partout
    }

    [Fact]
    public void Une_saturation_de_cinq_jours_demande_un_site_de_plus_une_seule_fois_et_les_soins_prennent_le_pas_sur_le_confort()
    {
        var (world, colony) = Village();
        Building school = Site(colony, BuildingType.School);
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        for (int day = 0; day < ScaleRules.ServiceDays; day++)
        {
            ledger.Today.ServiceDenied[CivicUse.Study] = 3;
            ledger.Today.ServiceServed[CivicUse.Study] = 2;
            ledger.Close(day + 1);
        }
        ServiceCoverage coverage = CivicServices.Coverage(colony, CivicUse.Study);
        Assert.Equal((1, 8, 15, 10, ServiceGap.Saturated), (coverage.Sites, coverage.Capacity, coverage.Denied5Days, coverage.Served5Days, coverage.Gap));
        Assert.True(CivicServices.NeedsAdditionalSite(colony, CivicUse.Study));
        Assert.Contains(BuildingType.School, Civic.Candidates(colony));

        // Un chantier du même type est déjà ouvert : aucun doublon de demande.
        colony.Buildings.Add(new Building(BuildingType.School, school.X + 8, school.Y) { Progress = 0.2f });
        Assert.False(CivicServices.NeedsAdditionalSite(colony, CivicUse.Study));
        Assert.DoesNotContain(BuildingType.School, Civic.Candidates(colony));

        // Les soins ne sont jamais un « site de plus » par le simple refus d'un soignant : un seul soignant par infirmerie, c'est le contrat.
        Assert.False(CivicServices.NeedsAdditionalSite(colony, CivicUse.Heal));
        Assert.True(world.Clock.Ticks >= 0);
    }

    [Fact]
    public void Sans_saturation_ni_manque_de_couverture_aucun_site_supplementaire_n_est_demande()
    {
        var (_, colony) = Village();
        Site(colony, BuildingType.Tavern);
        SettlementScaleLedger ledger = colony.LocalSettlement.ScaleLedger;
        for (int day = 0; day < ScaleRules.ServiceDays; day++)
        {
            ledger.Today.ServiceDenied[CivicUse.Relax] = 1;
            ledger.Today.ServiceServed[CivicUse.Relax] = 9;
            ledger.Close(day + 1);
        }
        Assert.Equal(ServiceGap.None, CivicServices.Coverage(colony, CivicUse.Relax).Gap);
        Assert.False(CivicServices.NeedsAdditionalSite(colony, CivicUse.Relax));
    }

    [Fact]
    public void Les_compteurs_de_service_survivent_a_la_sauvegarde()
    {
        var (world, colony) = Village();
        Site(colony, BuildingType.School);
        CivicServices.Note(colony, CivicUse.Study, served: false);
        CivicServices.Note(colony, CivicUse.Study, served: true);
        string path = Path.Combine(Path.GetTempPath(), $"GodColony-services-{Guid.NewGuid():N}.gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            ScaleDay today = loaded.Colonies[0].LocalSettlement.ScaleLedger.Today;
            Assert.Equal((1, 1), (today.ServiceDenied[CivicUse.Study], today.ServiceServed[CivicUse.Study]));
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
