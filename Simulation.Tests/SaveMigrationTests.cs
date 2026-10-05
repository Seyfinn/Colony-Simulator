using System.Security.Cryptography;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;

namespace GodColony.Simulation.Tests;

/// <summary>Le format v1 (figé avant la refonte du village) reste lisible : migration sans perte, sans déplacement, sans route offerte.</summary>
public sealed class SaveMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GodColony-migration-tests-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(_directory, "world.gcsave");

    public SaveMigrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string Fingerprint(WorldState world)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        StateGraph.Write(writer, world);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static string Layout(Colony colony) =>
        string.Join(" ", colony.Buildings.Select(b => $"{b.Type}@{b.X},{b.Y}")) + " | " + string.Join(" ", colony.Fields.Select(f => $"@{f.X},{f.Y}"));

    [Theory]
    [InlineData("v1-solo.gcsave", 16300, 1, 6, "Hut@115,41 Hut@115,44 Hut@107,41 Pen@107,44 Storehouse@115,37 | @110,36 @110,44")]
    [InlineData("v1-duo.gcsave", 9300, 2, 12, "Storehouse@94,75 Tavern@83,86 Hut@85,72 Hut@80,85 | @83,75 @80,79")]
    public void Une_partie_v1_se_charge_sans_rien_perdre_ni_deplacer(string name, long ticks, int colonies, int population, string firstColony)
    {
        SaveInfo info = WorldSave.ReadInfo(Fixture(name));
        Assert.Equal((ticks, colonies, population), (info.Ticks, info.Colonies, info.Population));

        WorldState world = WorldSave.Load(Fixture(name)).World;
        Assert.Equal(ticks, world.Clock.Ticks);
        Assert.Equal(population, world.Colonies.Sum(c => c.PresentMembers.Count));
        Assert.All(world.Caravans.SelectMany(c => c.Traders), t => Assert.Contains(t, t.Colony.Members));
        Assert.Equal(firstColony, Layout(world.Colonies[0]));
        Assert.NotNull(world.Planning);
        foreach (Colony colony in world.Colonies)
        {
            SettlementLayout layout = colony.Layout;
            // Chaque objet existant a son identifiant, sa parcelle et son quartier ; aucun n'est déplacé.
            Assert.All(colony.Buildings, b =>
            {
                Assert.True(b.Id > 0);
                PlotReservation parcel = Assert.IsType<PlotReservation>(layout.ParcelById(b.ParcelId));
                Assert.Equal((b.X, b.Y), (parcel.X, parcel.Y));
                Assert.Equal(b.IsComplete ? ReservationState.Occupied : ReservationState.Reserved, parcel.State);
                Assert.NotNull(layout.DistrictById(b.DistrictId));
            });
            Assert.All(colony.Fields, f => Assert.NotNull(layout.ParcelById(f.ParcelId)));
            // Aucune route offerte : la couche routière naît vierge.
            for (int cell = 0; cell < colony.Map.Roads.Length; cell++)
                Assert.Equal(RoadSurface.None, colony.Map.Roads.SurfaceAt(cell));
            Assert.Contains(layout.Districts, d => d.Kind == DistrictKind.Civic);
        }
    }

    [Fact]
    public void La_partie_migree_se_sauvegarde_en_v2_et_continue_exactement_comme_en_continu()
    {
        WorldState migrated = WorldSave.Load(Fixture("v1-duo.gcsave")).World;
        WorldSave.Save(SavePath, migrated);
        WorldState reloaded = WorldSave.Load(SavePath).World;
        Assert.Equal(Fingerprint(migrated), Fingerprint(reloaded));
        for (int i = 0; i < 4000; i++)
        {
            migrated.Step();
            reloaded.Step();
        }
        Assert.Null(new WorldComparison().Difference(migrated, reloaded));
    }

    [Fact]
    public void Un_schema_inconnu_reste_refuse()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("v1-solo.gcsave"));
        // En-tête : « GODCOLONY », la version (4 octets), puis l'empreinte du schéma (longueur sur un octet, 64 caractères hexadécimaux).
        int hash = 9 + 4 + 1;
        bytes[hash] = bytes[hash] == (byte)'A' ? (byte)'B' : (byte)'A';
        File.WriteAllBytes(SavePath, bytes);
        Assert.Throws<InvalidDataException>(() => WorldSave.Load(SavePath));
        Assert.Throws<InvalidDataException>(() => WorldSave.ReadInfo(SavePath));
    }

    [Fact]
    public void Une_version_v1_declaree_avec_un_autre_schema_est_refusee()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("v1-solo.gcsave"));
        bytes[9] = 2; // la version devient 2 : l'empreinte v1 ne correspond plus au schéma courant
        File.WriteAllBytes(SavePath, bytes);
        Assert.Throws<InvalidDataException>(() => WorldSave.Load(SavePath));
    }
}
