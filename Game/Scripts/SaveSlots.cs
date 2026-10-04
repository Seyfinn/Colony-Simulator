using System;
using System.IO;
using GodColony.Simulation.Persistence;

namespace GodColony;

public sealed record SaveSlot(int Index, bool Exists, SaveInfo? Info, string? Error, bool BackupAvailable);

/// <summary>Trois emplacements manuels et un emplacement rapide dans les données utilisateur du jeu.</summary>
internal sealed class SaveSlots(string directory)
{
    public const int ManualCount = 3;
    public const int QuickSlot = 3;
    public string DirectoryPath { get; } = directory;
    public string PathFor(int slot, bool backup = false)
    {
        if (slot is < 0 or > QuickSlot) throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(DirectoryPath, slot == QuickSlot ? "quick.gcsave" : $"world-{slot + 1}.gcsave") + (backup ? ".bak" : "");
    }
    public SaveSlot Read(int slot)
    {
        string path = PathFor(slot);
        bool backup = File.Exists(path + ".bak");
        if (!File.Exists(path)) return new SaveSlot(slot, false, null, null, backup);
        try { return new SaveSlot(slot, true, WorldSave.ReadInfo(path), null, backup); }
        catch (Exception) { return new SaveSlot(slot, true, null, "Sauvegarde endommagée ou incompatible.", backup); }
    }
}
