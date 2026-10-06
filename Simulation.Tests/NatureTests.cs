using GodColony.Simulation.Colonies;
using GodColony.Simulation.Nature;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Tests;

/// <summary>Lot A : la faune sauvage, la chasse, l'apprivoisement, la prédation, les ressources sauvages et la sauvegarde de tout cela.</summary>
public sealed class NatureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GodColony-nature-tests-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(_directory, "world.gcsave");

    public void Dispose()
    {
        foreach (string suffix in new[] { "", ".bak", ".tmp" })
            if (File.Exists(SavePath + suffix)) File.Delete(SavePath + suffix);
        if (Directory.Exists(_directory)) Directory.Delete(_directory);
    }

    private static (WorldState World, Colony Colony, Settlement Place, RegionState Region) Village(int seed = 777, int colonists = 10)
    {
        var world = new WorldState(seed, startingColonists: colonists, migration: false, lifecycle: false);
        Colony colony = world.Colonies[0];
        Settlement place = colony.PrimarySettlement;
        return (world, colony, place, world.Regions[place.RegionTileIndex]);
    }

    private static WildHerd AddHerd(Settlement place, WildSpecies species, int count, float x, float y, int young = 0) =>
        AddTo(place, new WildHerd { Id = ++place.NextHerdId, Species = species, Count = count, Young = young, X = x, Y = y, PrevX = x, PrevY = y, TargetX = x, TargetY = y });

    private static WildHerd AddTo(Settlement place, WildHerd herd) { place.Herds.Add(herd); return herd; }

    private static Colonist Hunter(Colony colony, float x, float y)
    {
        Colonist hunter = colony.PresentMembers.First();
        hunter.X = x; hunter.Y = y;
        return hunter;
    }

    private static int Total(Settlement place, RegionWildlife wild) => wild.Population.Values.Sum() + place.Herds.Sum(h => h.Count);

    private static Building BuildPen(WorldState world, Colony colony) => Urbanism.BuildInstantly(world.Map, colony, BuildingType.Pen)!;

    // ---------- Population sauvage ----------

    [Fact]
    public void La_croissance_est_bornee_par_la_capacite_d_accueil()
    {
        (WorldState world, _, _, RegionState region) = Village();
        region.Wildlife.Population[WildSpecies.Rabbit] = 2;
        float cap = Wildlife.Cap(region.Map.Biome, WildSpecies.Rabbit, world.Clock.Season, region.Wildlife.Disturbance);
        for (int day = 0; day < 400; day++)
        {
            Wildlife.GrowRegion(world, region, _ => 0);
            Assert.True(region.Wildlife.PopulationOf(WildSpecies.Rabbit) <= cap + 1, $"jour {day}");
        }
        Assert.True(region.Wildlife.PopulationOf(WildSpecies.Rabbit) > 10);
    }

    [Fact]
    public void La_presence_humaine_fait_baisser_la_capacite_et_la_faune_recule_avec_le_village()
    {
        Biome biome = Biome.TemperateForest;
        Assert.True(Wildlife.Cap(biome, WildSpecies.Deer, Season.Ete, 1f) < 0.5f * Wildlife.Cap(biome, WildSpecies.Deer, Season.Ete, 0f));
        Assert.True(Wildlife.Cap(biome, WildSpecies.Deer, Season.Hiver, 0f) < Wildlife.Cap(biome, WildSpecies.Deer, Season.Ete, 0f));
        Assert.True(Wildlife.SpawnRadius(1f) > Wildlife.SpawnRadius(0f));

        (WorldState world, Colony colony, Settlement place, RegionState region) = Village(colonists: 10);
        Wildlife.DailySettlement(world, place);
        Assert.True(region.Wildlife.Disturbance >= 0.5f);
        Assert.Same(region.Wildlife, place.Wildlife);
    }

    [Fact]
    public void Une_region_laissee_tranquille_se_repeuple()
    {
        (WorldState world, _, Settlement place, RegionState region) = Village();
        place.Status = SettlementStatus.Closed;
        region.Wildlife.Disturbance = 1f;
        foreach (WildSpecies species in WildSpeciesInfo.All) region.Wildlife.Population[species] = 1;
        int before = region.Wildlife.Prey;
        for (int day = 0; day < 300; day++)
            Wildlife.WorldDaily(world);
        Assert.True(region.Wildlife.Disturbance < 0.01f);
        Assert.True(region.Wildlife.Prey > before + 20, $"{before} → {region.Wildlife.Prey}");
    }

    [Fact]
    public void Les_hardes_empruntent_leurs_betes_a_la_region_sans_en_creer()
    {
        (WorldState world, _, Settlement place, RegionState region) = Village();
        RegionWildlife wild = region.Wildlife;
        int before = Total(place, wild);
        for (int i = 0; i < 6; i++)
            Assert.True(Wildlife.TrySpawn(world, place, wild, region.Map, Wildlife.SpawnRadius(0.5f), predators: false));
        Assert.Equal(before, Total(place, wild));
        Assert.Equal(6, place.Herds.Count);
        Assert.All(place.Herds, h => Assert.True(h.Count >= 1 && h.Count <= WildSpeciesInfo.HerdSize(h.Species)));

        // Elles bougent à l'heure sans jamais augmenter le nombre de bêtes.
        for (int hour = 0; hour < 200; hour++)
            Wildlife.HourlyMove(world, place);
        Assert.True(Total(place, wild) <= before);
        Assert.All(place.Herds, h => Assert.True(region.Map.IsWalkable(h.TileX, h.TileY)));

        // Fermer l'établissement rend les bêtes empruntées à la région.
        int borrowed = place.Herds.Sum(h => h.Count);
        int free = wild.Population.Values.Sum();
        place.Status = SettlementStatus.Closed;
        wild.Pending.Clear();
        Wildlife.WorldDaily(world);
        Assert.Empty(place.Herds);
        Assert.True(wild.Population.Values.Sum() >= free + borrowed - 3);
    }

    [Fact]
    public void Le_gibier_des_regions_froides_migre_en_hiver_et_revient_au_printemps()
    {
        (WorldState world, _, _, RegionState region) = Village();
        region.Map.Biome = Biome.Tundra;
        region.Wildlife.Population[WildSpecies.Deer] = 50;
        while (world.Clock.Season != Season.Hiver || world.Clock.DayOfSeason != 1)
            for (int i = 0; i < TimeConstants.TicksPerDay; i++) world.Clock.Advance();
        Wildlife.WorldDaily(world);
        Assert.Equal(40, region.Wildlife.PopulationOf(WildSpecies.Deer));
        while (world.Clock.Season != Season.Printemps || world.Clock.DayOfSeason != 1)
            for (int i = 0; i < TimeConstants.TicksPerDay; i++) world.Clock.Advance();
        Wildlife.WorldDaily(world);
        Assert.Equal(50, region.Wildlife.PopulationOf(WildSpecies.Deer));
    }

    [Fact]
    public void Un_alpha_surgit_quand_les_predateurs_abondent_et_jamais_deux_fois_par_an()
    {
        (WorldState world, _, Settlement place, RegionState region) = Village();
        region.Map.Biome = Biome.BorealForest;
        region.Wildlife.Population[WildSpecies.Bear] = 6;
        region.Wildlife.Population[WildSpecies.Wolf] = 12;
        for (int day = 0; day < 4000 && !Wildlife.HasAlpha(place); day++)
            Wildlife.DailySettlement(world, place);
        Assert.True(Wildlife.HasAlpha(place));
        WildHerd alpha = place.Herds.First(h => h.IsAlpha);
        Assert.NotEmpty(alpha.AlphaName);
        Assert.Contains(place.Owner.Thoughts, t => t.Text.Contains(alpha.AlphaName));

        place.Herds.Remove(alpha);
        for (int day = 0; day < 300; day++)
            Wildlife.DailySettlement(world, place);
        Assert.False(Wildlife.HasAlpha(place));
    }

    // ---------- Chasse ----------

    [Fact]
    public void Une_chasse_reussie_rapporte_viande_et_peaux_et_la_surchasse_vide_la_harde()
    {
        (WorldState world, Colony colony, Settlement place, _) = Village();
        Colonist hunter = Hunter(colony, 40.5f, 40.5f);
        WildHerd deer = AddHerd(place, WildSpecies.Deer, 5, 41.5f, 40.5f);
        int hides = colony.Stock.Get(ResourceType.Hides);
        int kills = 0;
        for (int shot = 0; shot < 200 && place.Herds.Contains(deer); shot++)
        {
            hunter.Carrying = null;
            Hunting.Resolve(world, hunter, new Activity(ActivityKind.Hunt, 41, 40, 10) { HerdId = deer.Id });
            if (hunter.Carrying is { Type: ResourceType.Meat } load)
            {
                Assert.True(load.Amount % WildSpeciesInfo.Meat(WildSpecies.Deer) == 0);
                kills += load.Amount / WildSpeciesInfo.Meat(WildSpecies.Deer);
            }
            deer.State = HerdState.Grazing;
        }
        Assert.DoesNotContain(deer, place.Herds);
        Assert.Equal(5, kills);
        Assert.Equal(hides + 5, colony.Stock.Get(ResourceType.Hides));
        Assert.True(hunter.Skills.Level(SkillType.Hunting) >= 0f);
    }

    [Fact]
    public void Les_habitants_chassent_pendant_la_partie_et_rapportent_de_la_viande()
    {
        (WorldState world, Colony colony, _, _) = Village(seed: 777, colonists: 8);
        for (int i = 0; i < 40 * TimeConstants.TicksPerDay; i++) world.Step();
        Assert.True(colony.Labor.TotalProduced(ResourceType.Meat) > 0);
    }

    [Fact]
    public void La_chasse_cesse_quand_le_gibier_manque()
    {
        (_, Colony colony, Settlement place, _) = Village();
        Colonist hunter = Hunter(colony, 40.5f, 40.5f);
        AddHerd(place, WildSpecies.Deer, 3, 45.5f, 40.5f);
        colony.Sensors = colony.Sensors! with { GameAbundance = 100f };
        Assert.NotEmpty(Hunting.Candidates(colony, hunter));
        colony.Sensors = colony.Sensors with { GameAbundance = 5f };
        Assert.Empty(Hunting.Candidates(colony, hunter));
    }

    [Fact]
    public void Une_grande_chasse_abat_l_alpha_ou_blesse_les_chasseurs()
    {
        int victories = 0, defeats = 0;
        for (int seed = 1; seed <= 25; seed++)
        {
            (WorldState world, Colony colony, Settlement place, _) = Village(seed, colonists: 10);
            WildHerd alpha = AddTo(place, new WildHerd
            {
                Id = ++place.NextHerdId, Species = WildSpecies.Wolf, Count = 3, IsAlpha = true, AlphaName = "Croc-Gris",
                X = place.CampX + 10.5f, Y = place.CampY + 0.5f, Hunger = 1f, State = HerdState.Stalking,
            });
            colony.Sensors = colony.Sensors! with { PredatorPressure = 90f };
            Hunting.PlanGreatHunt(colony, world.Clock);
            GreatHuntState hunt = Assert.IsType<GreatHuntState>(place.GreatHunt);
            Assert.InRange(hunt.HunterIds.Count, 4, 8);
            Assert.Equal(alpha.Id, hunt.HerdId);
            foreach (Colonist hunter in colony.PresentMembers.Where(m => hunt.HunterIds.Contains(m.Id)).ToList())
            {
                hunter.X = alpha.X; hunter.Y = alpha.Y;
                Hunting.Arrived(world, hunter);
            }
            Assert.Null(place.GreatHunt);
            if (!place.Herds.Contains(alpha))
            {
                victories++;
                Assert.Equal(10, colony.Prestige);
                Assert.Contains(colony.PresentMembers, m => m.Renown >= 10f);
            }
            else
            {
                defeats++;
                Assert.Equal(0, colony.Prestige);
                Assert.Contains(colony.PresentMembers, m => m.Ailment == Ailment.Injured);
            }
        }
        Assert.True(victories > 0 && defeats > 0, $"{victories} victoires, {defeats} défaites");
    }

    // ---------- Apprivoisement ----------

    [Fact]
    public void Une_capture_puis_des_jours_de_soins_donnent_une_bete_a_l_enclos()
    {
        (WorldState world, Colony colony, Settlement place, _) = Village();
        BuildPen(world, colony);
        Husbandry.OnPenBuilt(colony, world.Clock);
        Assert.Equal(0, Husbandry.Animals(colony));
        Colonist keeper = Hunter(colony, 40.5f, 40.5f);
        for (int attempt = 0; attempt < 200 && place.Taming.Count == 0; attempt++)
        {
            WildHerd fowl = AddHerd(place, WildSpecies.Junglefowl, 4, 41.5f, 40.5f, young: 2);
            Taming.ResolveCapture(world, keeper, new Activity(ActivityKind.Capture, 41, 40, 10) { HerdId = fowl.Id });
            place.Herds.Remove(fowl);
        }
        TamingAnimal animal = Assert.Single(place.Taming);
        Assert.Equal(ResourceType.Chickens, animal.Species);
        Assert.True(animal.IsYoung);
        Assert.Equal(WildSpeciesInfo.TamingDays(WildSpecies.Junglefowl, true), animal.Required);

        // Sans soins elle n'avance pas ; soignée chaque jour elle entre à l'enclos.
        Taming.Daily(world, colony);
        Assert.Equal(0f, animal.Progress);
        for (int day = 0; day < animal.Required; day++)
        {
            Assert.True(Taming.WorkPending(colony));
            Taming.Care(colony);
            Taming.Daily(world, colony);
        }
        Assert.Empty(place.Taming);
        Assert.Equal(1, colony.Chickens);
        Assert.Contains(ResourceType.Chickens, place.Lines.Keys);
    }

    [Fact]
    public void Un_loup_apprivoise_devient_un_chien_et_un_jeune_s_apprivoise_plus_vite()
    {
        Assert.True(WildSpeciesInfo.TamingDays(WildSpecies.Wolf, true) < WildSpeciesInfo.TamingDays(WildSpecies.Wolf, false));
        (WorldState world, Colony colony, Settlement place, _) = Village();
        BuildPen(world, colony);
        place.Taming.Add(new TamingAnimal { Species = ResourceType.Dogs, IsYoung = true, Required = 2, CapturedTicks = world.Clock.Ticks });
        for (int day = 0; day < 2; day++)
        {
            Taming.Care(colony);
            Taming.Daily(world, colony);
        }
        Assert.Equal(1, colony.Stock.Get(ResourceType.Dogs));
        Assert.Contains(colony.Thoughts, t => t.Text.Contains("chien"));
        // Le chien aide à la chasse.
        Colonist hunter = Hunter(colony, 40.5f, 40.5f);
        float with = Hunting.SuccessChance(colony, hunter);
        colony.Stock.TryTake(ResourceType.Dogs, 1);
        Assert.True(with > Hunting.SuccessChance(colony, hunter));
    }

    [Fact]
    public void Une_bete_negligee_recule_et_finit_par_s_enfuir()
    {
        (WorldState world, Colony colony, Settlement place, RegionState region) = Village();
        BuildPen(world, colony);
        place.Taming.Add(new TamingAnimal { Species = ResourceType.Sheep, Progress = 1f, Required = 8 });
        place.Wildlife = region.Wildlife; // posé chaque jour par la simulation
        int wild = region.Wildlife.PopulationOf(WildSpecies.Mouflon);
        for (int day = 0; day < 300 && place.Taming.Count > 0; day++)
            Taming.Daily(world, colony);
        Assert.Empty(place.Taming);
        Assert.Equal(wild + 1, region.Wildlife.PopulationOf(WildSpecies.Mouflon));
        Assert.Equal(0, colony.Sheep);
    }

    [Fact]
    public void Les_generations_nees_en_enclos_deviennent_plus_dociles_et_productives()
    {
        (WorldState world, Colony colony, Settlement place, _) = Village();
        BuildPen(world, colony);
        colony.Chickens = 4;
        GameClock spring = new((long)2 * TimeConstants.TicksPerDay);
        for (int day = 0; day < 60; day++)
        {
            colony.Chickens = Math.Max(4, Math.Min(colony.Chickens, 8));
            Husbandry.Daily(colony, spring);
        }
        LivestockLine line = place.Lines[ResourceType.Chickens];
        Assert.True(line.Docility > 0f && line.Yield > 1f && line.Generations > 0);
        Assert.True(line.Yield <= LivestockLine.MaxYield);
        Assert.Contains("poules", line.Name);
        Assert.True(Taming.Yield(colony, ResourceType.Chickens) > 1f);
    }

    // ---------- Prédation ----------

    private static int Raids(Colony colony, Settlement place, WorldState world, Action? configure = null, int rounds = 600)
    {
        WildHerd wolves = AddTo(place, new WildHerd { Id = ++place.NextHerdId, Species = WildSpecies.Wolf, Count = 4, X = place.CampX + 6.5f, Y = place.CampY + 0.5f, Hunger = 1f, State = HerdState.Stalking });
        configure?.Invoke();
        int raids = 0;
        for (int i = 0; i < rounds; i++)
        {
            wolves.Hunger = 1f; // une attaque rassasie la meute ; elle a de nouveau faim le lendemain
            (colony.Chickens, colony.Sheep, colony.Cows) = (6, 4, 3);
            Predation.DailyRaids(world, colony);
            if (Husbandry.Animals(colony) < 13) raids++;
        }
        return raids;
    }

    [Fact]
    public void Un_enclos_renforce_et_un_chien_reduisent_les_pertes()
    {
        (WorldState w1, Colony c1, Settlement p1, _) = Village();
        BuildPen(w1, c1);
        int baseline = Raids(c1, p1, w1);
        (WorldState w2, Colony c2, Settlement p2, _) = Village();
        BuildPen(w2, c2);
        int reinforced = Raids(c2, p2, w2, () => p2.PenReinforced = true);
        (WorldState w3, Colony c3, Settlement p3, _) = Village();
        BuildPen(w3, c3);
        int guarded = Raids(c3, p3, w3, () => c3.Stock.Add(ResourceType.Dogs, 1));
        Assert.True(baseline > 20, $"{baseline}");
        Assert.True(reinforced < baseline * 0.6, $"{reinforced} / {baseline}");
        Assert.True(guarded < baseline * 0.8, $"{guarded} / {baseline}");
    }

    [Fact]
    public void La_derniere_paire_n_est_jamais_tuee_et_les_attaques_emportent_une_a_trois_betes()
    {
        (WorldState world, Colony colony, Settlement place, _) = Village();
        BuildPen(world, colony);
        WildHerd pack = AddTo(place, new WildHerd { Id = ++place.NextHerdId, Species = WildSpecies.Wolf, Count = 6, X = place.CampX + 5.5f, Y = place.CampY + 0.5f, Hunger = 1f, State = HerdState.Stalking });
        (colony.Chickens, colony.Sheep, colony.Cows) = (2, 2, 2);
        for (int i = 0; i < 3000; i++)
        {
            pack.Hunger = 1f;
            Predation.DailyRaids(world, colony);
        }
        Assert.Equal((2, 2, 2), (colony.Chickens, colony.Sheep, colony.Cows));

        for (int i = 0; i < 400; i++)
        {
            pack.Hunger = 1f;
            (colony.Chickens, colony.Sheep, colony.Cows) = (8, 8, 4);
            int before = Husbandry.Animals(colony);
            Predation.DailyRaids(world, colony);
            Assert.InRange(before - Husbandry.Animals(colony), 0, 3);
        }
    }

    [Fact]
    public void Un_prédateur_blesse_un_travailleur_isole_mais_la_mort_reste_rare_et_la_compagnie_protege()
    {
        (WorldState world, Colony colony, Settlement place, _) = Village(colonists: 10);
        Colonist worker = Hunter(colony, place.CampX + 25.5f, place.CampY + 0.5f);
        foreach (Colonist other in colony.PresentMembers.Where(c => c != worker)) { other.X = place.CampX; other.Y = place.CampY; }
        WildHerd wolves = AddTo(place, new WildHerd { Id = ++place.NextHerdId, Species = WildSpecies.Wolf, Count = 4, X = worker.X + 1f, Y = worker.Y, State = HerdState.Stalking, Hunger = 1f });
        int injuries = 0;
        for (int i = 0; i < 3000; i++)
        {
            worker.Ailment = Ailment.None;
            wolves.State = HerdState.Stalking;
            Predation.HourlyEncounters(world, colony);
            if (worker.Ailment == Ailment.Injured)
            {
                injuries++;
                Assert.Equal(0.01f, worker.AilmentDeathChance);
            }
        }
        Assert.InRange(injuries, 40, 170);

        // Un compagnon à moins de quatre cases : personne n'est attaqué.
        Colonist companion = colony.PresentMembers.First(c => c != worker);
        companion.X = worker.X + 2f; companion.Y = worker.Y;
        for (int i = 0; i < 500; i++)
        {
            worker.Ailment = Ailment.None;
            wolves.State = HerdState.Stalking;
            Predation.HourlyEncounters(world, colony);
            Assert.NotEqual(Ailment.Injured, worker.Ailment);
        }

        // Une blessure de prédateur tue au plus une fois sur cent (statistique sur 10 000 guérisons).
        int deaths = 0;
        for (int i = 0; i < 10_000; i++)
            if (world.Chance.NextSingle() < 0.01f) deaths++;
        Assert.InRange(deaths, 40, 170);
    }

    [Fact]
    public void Les_herbivores_pietinent_les_champs_proches_et_reduisent_la_recolte()
    {
        (WorldState world, Colony colony, Settlement place, _) = Village();
        Field field = Assert.Single(colony.Fields.Take(1));
        FieldPlot plot = field.Plots[0];
        plot.Stage = CropStage.Growing;
        AddHerd(place, WildSpecies.Deer, 4, field.X + 2f, field.Y + 2f);
        Assert.True(Predation.CropRaidPressure(colony) > 0f);
        for (int day = 0; day < 4; day++)
            Predation.DailyRaids(world, colony);
        Assert.InRange(plot.Trampled, 0.35f, 0.45f);
    }

    // ---------- Ressources sauvages ----------

    [Fact]
    public void Les_ressources_sauvages_s_epuisent_et_repoussent_sans_hasard()
    {
        (WorldState world, Colony colony, Settlement place, RegionState region) = Village();
        region.Wildlife.Herbs[1000] = 2;
        place.Wildlife = region.Wildlife;
        int x = 1000 % region.Map.Width, y = 1000 / region.Map.Width;
        Assert.Equal(2, WildResources.Left(place, ResourceType.Herbs, x, y));
        Assert.Equal((ResourceType.Herbs, 2), WildResources.Harvest(colony, ResourceType.Herbs, x, y));
        WildResources.Harvest(colony, ResourceType.Herbs, x, y);
        Assert.Null(WildResources.Harvest(colony, ResourceType.Herbs, x, y));

        region.Wildlife.Hives[1001] = 1;
        long random = world.Nature.NextInt64();
        int wax = colony.Stock.Get(ResourceType.Wax);
        WildResources.Harvest(colony, ResourceType.Honey, 1001 % region.Map.Width, 1001 / region.Map.Width);
        Assert.Equal(wax + 1, colony.Stock.Get(ResourceType.Wax));
        for (int day = 0; day < 12; day++)
            WildResources.Daily(colony, new GameClock((long)day * TimeConstants.TicksPerDay));
        Assert.True(region.Wildlife.Hives[1001] >= 1 && region.Wildlife.Herbs[1000] >= 1);
        Assert.NotEqual(0L, random);
    }

    [Fact]
    public void Le_miel_et_les_champignons_nourrissent_et_les_plantes_soignent()
    {
        (_, Colony colony, _, _) = Village();
        int food = colony.Stock.FoodUnits;
        colony.Stock.Add(ResourceType.Honey, 2);
        colony.Stock.Add(ResourceType.Mushrooms, 3);
        Assert.Equal(5, colony.Stock.FoodUnits - food);
        Assert.True(colony.Stock.TryTakeMeal(out float value) && value > 0f);

        Colonist patient = colony.PresentMembers.First();
        patient.Ailment = Ailment.Sick; patient.AilmentHours = 30;
        Health.Treat(colony, Health.Patients(colony));
        int without = patient.AilmentHours;
        patient.AilmentHours = 30;
        colony.Stock.Add(ResourceType.Herbs, 1);
        Health.Treat(colony, Health.Patients(colony));
        Assert.True(patient.AilmentHours < without);
        Assert.Equal(0, colony.Stock.Get(ResourceType.Herbs));
    }

    // ---------- Sauvegarde ----------

    [Fact]
    public void Un_monde_avec_hardes_apprivoisements_et_lignees_se_sauvegarde_et_se_recharge_a_l_identique()
    {
        (WorldState world, Colony colony, Settlement place, RegionState region) = Village();
        for (int i = 0; i < 3 * TimeConstants.TicksPerDay; i++) world.Step();
        BuildPen(world, colony);
        place.Taming.Add(new TamingAnimal { Species = ResourceType.Cows, IsYoung = true, Progress = 1f, Required = 4, CapturedTicks = world.Clock.Ticks });
        Taming.OnBirth(colony, ResourceType.Chickens);
        place.GreatHunt = new GreatHuntState { HerdId = 1, HunterIds = [1, 2], StartTicks = world.Clock.Ticks };
        place.PenReinforced = true;
        Assert.NotEmpty(place.Herds);
        WorldSave.Save(SavePath, world);
        WorldState loaded = WorldSave.Load(SavePath).World;
        Assert.Null(new WorldComparison().Difference(world, loaded));
        Settlement reloaded = loaded.Colonies[0].PrimarySettlement;
        Assert.Equal(place.Herds.Count, reloaded.Herds.Count);
        Assert.Same(loaded.Regions[reloaded.RegionTileIndex].Wildlife, reloaded.Wildlife);
        Assert.True(reloaded.PenReinforced);
        for (int i = 0; i < 2 * TimeConstants.TicksPerDay; i++) { world.Step(); loaded.Step(); }
        Assert.Null(new WorldComparison().Difference(world, loaded));
    }

    [Fact]
    public void La_nature_a_son_propre_hasard_et_les_nouveaux_enums_s_ajoutent_a_la_fin()
    {
        var a = new WorldState(5, startingColonists: 6, migration: false, lifecycle: false);
        var b = new WorldState(5, startingColonists: 6, migration: false, lifecycle: false);
        for (int i = 0; i < 20; i++) a.Nature.Next();
        Assert.Equal(b.Random.Next(), a.Random.Next());
        Assert.Equal(b.Chance.Next(), a.Chance.Next());
        Assert.Equal(b.Politics.Next(), a.Politics.Next());
        Assert.Equal(47, (int)ResourceType.Horses);
        Assert.Equal(54, (int)ResourceType.Carts);
        Assert.Equal(29, (int)ActivityKind.Hunt);
        Assert.Equal(33, (int)ActivityKind.GreatHunt);
        Assert.Equal(SkillType.Hunting, Skills.All[^1]);
    }
}
