using System.Security.Cryptography;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class SaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GodColony-save-tests-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(_directory, "world.gcsave");
    public void Dispose()
    {
        foreach (string suffix in new[] { "", ".bak", ".tmp" })
            if (File.Exists(SavePath + suffix)) File.Delete(SavePath + suffix);
        if (Directory.Exists(_directory)) Directory.Delete(_directory);
    }
    private static string Fingerprint(WorldState world)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        StateGraph.Write(writer, world);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Le_fichier_restitue_tout_le_monde_et_ses_references(int colonies)
    {
        var world = new WorldState(12345, 128, 128, colonyCount: colonies);
        for (int i = 0; i < 3200; i++) world.Step();
        var view = new SavedView(CameraX: 234, CameraY: 567, Zoom: 2);
        SaveInfo info = WorldSave.Save(SavePath, world, view);
        LoadedWorld loaded = WorldSave.Load(SavePath);
        Assert.Equal(info, WorldSave.ReadInfo(SavePath));
        Assert.Equal(view, loaded.Info.View);
        Assert.Equal(Fingerprint(world), Fingerprint(loaded.World));
        Assert.NotSame(world, loaded.World);
        foreach (Colony colony in loaded.World.Colonies)
        {
            Assert.Contains(colony.Species, Species.All);
            Assert.Same(loaded.World.Clock, colony.Clock);
            Assert.All(colony.Members, c => Assert.Same(colony, c.Colony));
            Assert.NotNull(colony.Pathfinder);
        }
    }

    [Fact]
    public void La_partie_chargee_continue_exactement_comme_la_partie_originale()
    {
        var world = new WorldState(2026, 128, 128, colonyCount: 2);
        for (int i = 0; i < 8500; i++) world.Step();
        WorldSave.Save(SavePath, world);
        WorldState loaded = WorldSave.Load(SavePath).World;
        Assert.Equal(Fingerprint(world), Fingerprint(loaded));
        for (int i = 0; i < 6000; i++)
        {
            world.Step(); loaded.Step();
            if (i % 100 == 0)
            {
                // Une insertion après suppression peut changer l'ordre interne d'un HashSet sans changer la partie.
                string? difference = new WorldComparison().Difference(world, loaded);
                Assert.True(difference is null, $"Divergence au tick {i + 1} après chargement : {difference}");
            }
        }
        Assert.Null(new WorldComparison().Difference(world, loaded));
    }

    [Fact]
    public void Les_liens_familiaux_et_les_caravanes_ne_sont_pas_dupliques()
    {
        var world = new WorldState(12345, colonyCount: 2);
        Colony from = world.Colonies[0], to = world.Colonies[1];
        Colonist mother = from.Members[0], father = from.Members[1];
        mother.TryRename("Aélia", "Clairerive");
        mother.Partner = father; father.Partner = mother;
        mother.PregnancyFather = father; mother.PregnantUntilTicks = world.Clock.Ticks + 2000;
        Urbanism.BuildInstantly(from.Map, from, BuildingType.Hut);
        var caravan = new Caravan(from, to, new List<Colonist> { mother, father }, new TradeLine[] { new(ResourceType.Wood, 3, 4, true) },
            2, world.Clock.Ticks, world.Clock.Ticks + 100, world.Clock.Ticks + 300);
        caravan.Coins = 40;
        caravan.Cargo[ResourceType.Wood] = 3;
        world.RegisterTrip(caravan);
        world.Caravans.Add(caravan);
        WorldSave.Save(SavePath, world);
        WorldState loaded = WorldSave.Load(SavePath).World;
        Colony restored = loaded.Colonies[0];
        Assert.Equal("Aélia Clairerive", restored.Members[0].FullName);
        Assert.Same(restored.Members[1], restored.Members[0].Partner);
        Assert.Same(restored.Members[0], restored.Members[1].Partner);
        Assert.Same(restored.Members[1], restored.Members[0].PregnancyFather);
        Assert.Same(restored, loaded.Caravans[0].From);
        Assert.Same(loaded.Colonies[1], loaded.Caravans[0].To);
        Assert.Same(restored.Members[0], loaded.Caravans[0].Traders[0]);
        Assert.Equal(40, loaded.Caravans[0].Coins);
        Assert.Equal(Fingerprint(world), Fingerprint(loaded));
    }

    [Fact]
    public void Une_colonie_fondee_par_le_joueur_reste_sur_sa_case_et_le_monde_peut_encore_accueillir_des_fondateurs()
    {
        var world = new WorldState(12345, 128, 128, colonyCount: 0);
        int tile = world.WorldMap.SuggestTile(Species.Elf);
        LocalMap map = world.GenerateColonyMap(tile);
        var site = ColonyFounder.FindCampSite(map);
        Assert.True(world.TryFoundColony(map, site.X, site.Y, "Clairerive", Species.Elf, 12, tile, out _, out _));
        WorldSave.Save(SavePath, world);
        WorldState loaded = WorldSave.Load(SavePath).World;
        Colony colony = Assert.Single(loaded.Colonies);
        Assert.Equal("Clairerive", colony.Name);
        Assert.Same(Species.Elf, colony.Species);
        Assert.Equal(tile, loaded.WorldMap.TileOf(colony));
        Assert.Equal((site.X, site.Y), (colony.CampX, colony.CampY));
        int nextTile = loaded.WorldMap.SuggestTile(Species.Orc);
        LocalMap nextMap = loaded.GenerateColonyMap(nextTile);
        var nextSite = ColonyFounder.FindCampSite(nextMap);
        Assert.True(loaded.TryFoundColony(nextMap, nextSite.X, nextSite.Y, "Brumeforge", Species.Orc, 8, nextTile, out _, out _));
        int[] ids = loaded.Colonies.SelectMany(c => c.Members).Select(c => c.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Le_terrain_modifie_et_les_prieres_restent_disponibles()
    {
        var world = new WorldState(12345);
        Colony colony = world.Colonies[0];
        for (int y = 0; y < world.Map.Height; y++)
        for (int x = 0; x < world.Map.Width; x++)
            if (world.Map.CanMine(x, y)) { world.Map.Mine(x, y); break; }
        colony.Prayers.Ask(DecisionKind.Dam, "demo", "Un barrage ?", "Demande de démonstration", () => { }, world.Clock);
        WorldSave.Save(SavePath, world);
        WorldState loaded = WorldSave.Load(SavePath).World;
        Assert.Equal(Fingerprint(world), Fingerprint(loaded));
        Prayer prayer = Assert.Single(loaded.Colonies[0].Prayers.Pending);
        loaded.AnswerPrayer(prayer, true);
        Assert.Equal(PrayerStatus.Approved, prayer.Status);
    }

    [Fact]
    public void Une_sauvegarde_ecrasee_conserve_la_version_precedente()
    {
        var world = new WorldState(12345, colonyCount: 0);
        long before = world.Clock.Ticks;
        WorldSave.Save(SavePath, world);
        world.Step(); WorldSave.Save(SavePath, world);
        Assert.Equal(before, WorldSave.Load(SavePath + ".bak").World.Clock.Ticks);
        Assert.Equal(before + 1, WorldSave.Load(SavePath).World.Clock.Ticks);
        Assert.False(File.Exists(SavePath + ".tmp"));
    }

    [Theory]
    [InlineData(GameSpeed.Fulgurante, GameSpeed.Fulgurante)]
    [InlineData(GameSpeed.Pause, GameSpeed.Fulgurante)]
    public void Une_partie_sauvegardee_en_vue_chiffree_se_recharge(GameSpeed speed, GameSpeed beforePause)
    {
        var world = new WorldState(12345, colonyCount: 0);
        var view = new SavedView(Speed: speed, SpeedBeforePause: beforePause);
        WorldSave.Save(SavePath, world, view);
        Assert.Equal(view, WorldSave.Load(SavePath).Info.View);
    }

    [Fact]
    public void Un_echec_d_ecriture_conserve_la_sauvegarde_precedente()
    {
        var world = new WorldState(12345, colonyCount: 0);
        WorldSave.Save(SavePath, world);
        long before = world.Clock.Ticks;
        using (var locked = new FileStream(SavePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            world.Step();
            Assert.ThrowsAny<IOException>(() => WorldSave.Save(SavePath, world));
        }
        Assert.Equal(before, WorldSave.Load(SavePath).World.Clock.Ticks);
        Assert.False(File.Exists(SavePath + ".tmp"));
    }

    [Fact]
    public void Une_priere_chargee_peut_encore_ouvrir_le_chantier_du_barrage()
    {
        var world = new WorldState(12345, 128, 128, colonyCount: 0);
        LocalMap map = world.Map;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            map.SetGenerated(x, y, Math.Abs(x - 20) <= 2 && y >= 14 ? 5 : 8, SoilType.Grass, FloraType.None, 0);
        for (int y = 14; y < map.Height; y++) map.SetRiver(20, y, 20, y + 1);
        var colony = new Colony("Vallée", 5, 35, [(6, 35)]) { Map = map, Clock = world.Clock };
        int tile = world.WorldMap.SuggestTile(Species.Human);
        world.WorldMap.PlaceAt(colony, tile);
        world.Colonies.Add(colony);
        world.RegisterSettlement(colony, tile);
        Assert.NotNull(Hydrology.FindReservoir(map, colony, 20, 20));
        colony.Prayers.Ask(DecisionKind.Dam, "20,20", "Un barrage ?", "Un lac pour les cultures.",
            () => ColonyBrain.ApplyDamDecision(colony, map, world.Clock, 20, 20), world.Clock);
        WorldSave.Save(SavePath, world);
        WorldState loaded = WorldSave.Load(SavePath).World;
        loaded.AnswerPrayer(Assert.Single(loaded.Colonies[0].Prayers.Pending), true);
        Building dam = Assert.Single(loaded.Colonies[0].Buildings);
        Assert.True(dam.IsDam);
        Assert.Equal((20, 20), (dam.RiverX, dam.RiverY));
        WorldSave.Save(SavePath, loaded);
        Assert.Equal(Fingerprint(loaded), Fingerprint(WorldSave.Load(SavePath).World));
    }

    [Fact]
    public void Une_sauvegarde_tronquee_ou_modifiee_est_refusee()
    {
        var world = new WorldState(12345, colonyCount: 0);
        WorldSave.Save(SavePath, world);
        byte[] original = File.ReadAllBytes(SavePath);
        File.WriteAllBytes(SavePath, original.Take(original.Length / 2).ToArray());
        Assert.ThrowsAny<Exception>(() => WorldSave.Load(SavePath));
        original[^15] ^= 0xFF;
        File.WriteAllBytes(SavePath, original);
        Assert.ThrowsAny<Exception>(() => WorldSave.Load(SavePath));
        Assert.Equal(300, world.Clock.Ticks);
    }
}
