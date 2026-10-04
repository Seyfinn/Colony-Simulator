using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Persistence;

/// <summary>Vue à retrouver au chargement ; les coordonnées sont en pixels, indépendantes de Godot.</summary>
public sealed record SavedView(int Observed = 0, float CameraX = 0, float CameraY = 0, float Zoom = 1.25f,
    GameSpeed Speed = GameSpeed.Observation, GameSpeed SpeedBeforePause = GameSpeed.Observation, int? SelectedColonist = null);
public sealed record SaveInfo(string Name, DateTimeOffset SavedAt, int Seed, long Ticks, int Colonies, int Population, SavedView View);
public sealed record LoadedWorld(WorldState World, SaveInfo Info);

/// <summary>Fichier versionné, compressé et vérifié avant remplacement de la partie courante.</summary>
public static class WorldSave
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GODCOLONY");
    private const int Version = 1;
    private const int MaxFileBytes = 32 * 1024 * 1024;
    private const int MaxStateBytes = 128 * 1024 * 1024;
    private static string Schema => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StateGraph.Schema)));

    public static SaveInfo Save(string path, WorldState world, SavedView? view = null)
    {
        var info = new SaveInfo(world.Colonies.Count == 0 ? "Monde vierge" : world.Colonies[0].Name,
            DateTimeOffset.UtcNow, world.Seed, world.Clock.Ticks, world.Colonies.Count,
            world.Colonies.Sum(c => c.Members.Count), view ?? new SavedView());
        using var state = new MemoryStream();
        using (var writer = new BinaryWriter(state, Encoding.UTF8, leaveOpen: true)) StateGraph.Write(writer, world);
        if (state.Length > MaxStateBytes) throw new InvalidDataException("Le monde est trop volumineux pour être sauvegardé.");
        byte[] payload = state.ToArray();
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(Magic); writer.Write(Version); writer.Write(Schema);
                    writer.Write(JsonSerializer.Serialize(info));
                    writer.Write(payload.Length); writer.Write(SHA256.HashData(payload));
                }
                using (var compressed = new BrotliStream(file, CompressionLevel.Fastest, leaveOpen: true)) compressed.Write(payload);
                file.Flush(flushToDisk: true);
                if (file.Length > MaxFileBytes) throw new InvalidDataException("Le fichier de sauvegarde est trop volumineux.");
            }
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(temporary, path, overwrite: true);
            return info;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static SaveInfo ReadInfo(string path)
    {
        using var file = Open(path);
        using var reader = new BinaryReader(file);
        return Header(reader);
    }

    public static LoadedWorld Load(string path)
    {
        using var file = Open(path);
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        SaveInfo info = Header(reader);
        int length = reader.ReadInt32();
        if (length is <= 0 or > MaxStateBytes) throw new InvalidDataException("Taille de sauvegarde invalide.");
        byte[] checksum = reader.ReadBytes(32);
        if (checksum.Length != 32) throw new EndOfStreamException("Sauvegarde incomplète.");
        byte[] payload = new byte[length];
        using (var compressed = new BrotliStream(file, CompressionMode.Decompress, leaveOpen: true))
        {
            compressed.ReadExactly(payload);
            if (compressed.ReadByte() != -1) throw new InvalidDataException("La sauvegarde contient des données inattendues.");
        }
        if (!CryptographicOperations.FixedTimeEquals(checksum, SHA256.HashData(payload)))
            throw new InvalidDataException("La sauvegarde est endommagée.");
        using var state = new MemoryStream(payload, writable: false);
        using var stateReader = new BinaryReader(state);
        WorldState world = StateGraph.Read(stateReader);
        if (state.Position != state.Length || world.Seed != info.Seed || world.Clock.Ticks != info.Ticks || world.Colonies.Count != info.Colonies)
            throw new InvalidDataException("Le résumé de la sauvegarde ne correspond pas au monde.");
        return new LoadedWorld(world, info);
    }

    private static FileStream Open(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > MaxFileBytes) { file.Dispose(); throw new InvalidDataException("Taille de fichier invalide."); }
        return file;
    }

    private static SaveInfo Header(BinaryReader reader)
    {
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Ce fichier n'est pas une sauvegarde GodColony.");
        if (reader.ReadInt32() != Version || reader.ReadString() != Schema)
            throw new InvalidDataException("Cette sauvegarde provient d'une version incompatible du jeu.");
        string json = reader.ReadString();
        if (json.Length > 8192) throw new InvalidDataException("Résumé de sauvegarde invalide.");
        SaveInfo info = JsonSerializer.Deserialize<SaveInfo>(json) ?? throw new InvalidDataException("Résumé de sauvegarde absent.");
        SavedView view = info.View ?? throw new InvalidDataException("Vue sauvegardée absente.");
        if (info.Name.Length > 80 || info.Ticks < 0 || info.Colonies is < 0 or > WorldState.MaxPlayerColonies || info.Population < 0
            || !float.IsFinite(view.CameraX) || !float.IsFinite(view.CameraY) || !float.IsFinite(view.Zoom) || view.Zoom is < 0.25f or > 6f
            || (int)view.Speed is not (0 or 1 or 4 or 30) || (int)view.SpeedBeforePause is not (1 or 4 or 30))
            throw new InvalidDataException("Résumé de sauvegarde invalide.");
        return info;
    }
}
