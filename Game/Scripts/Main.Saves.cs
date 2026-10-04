using System;
using Godot;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony;

public partial class Main
{
    private SaveSlots _saves = null!;
    private void SaveWorld(int slot)
    {
        if (_world is null) return;
        if (_foundingPanel.IsOpen) { Notify("Terminez ou annulez la fondation avant de sauvegarder."); return; }
        try
        {
            var view = new SavedView(_observed, _camera.Position.X, _camera.Position.Y, _camera.Zoom.X,
                _speed, _speedBeforePause, _selected?.Id);
            WorldSave.Save(_saves.PathFor(slot), _world, view);
            string message = slot == SaveSlots.QuickSlot ? "Sauvegarde rapide enregistrée." : $"Partie sauvegardée dans l'emplacement {slot + 1}.";
            if (_menu.IsOpen) _menu.ShowSaveResult(message); else Notify(message);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"Sauvegarde impossible : {exception.Message}");
            const string message = "Impossible d'enregistrer la partie. Vérifiez l'espace disque et les droits d'accès. La sauvegarde précédente est conservée.";
            if (_menu.IsOpen) _menu.ShowSaveResult(message, true); else Notify(message);
        }
    }

    private void LoadWorld(int slot, bool backup)
    {
        _pendingTicks = 0;
        if (_world is not null) _camera.ControlsEnabled = false;
        _menu.RunFileOperation("Chargement de la partie…", () =>
        {
            try
            {
                // Le fichier est entièrement vérifié et restauré avant de remplacer les vues de la partie courante.
                LoadedWorld loaded = WorldSave.Load(_saves.PathFor(slot, backup));
                SavedView view = loaded.Info.View;
                var options = new WorldCreationOptions(loaded.World.Seed, loaded.World.Map.Width, loaded.World.Colonies.Count,
                    8, true, true, true, view.Speed);
                // Connue avant la construction des vues : une partie en pause dans la vue chiffrée s'y recharge directement.
                _speedBeforePause = view.SpeedBeforePause;
                StartWorld(options, restored: loaded.World, observed: view.Observed);
                _camera.Position = _cameraBeforeStats = new Vector2(view.CameraX, view.CameraY);
                _camera.Zoom = Vector2.One * view.Zoom;
                if (view.SelectedColonist is { } id && _world!.Colonies.Count > 0)
                    Select(Observed.Members.Find(c => c.Id == id) ?? Observed.Transients.Find(c => c.Id == id));
                _hudCooldown = 0;
                Notify(backup ? "Version précédente chargée." : "Partie chargée.");
            }
            catch (Exception exception)
            {
                GD.PrintErr($"Chargement impossible : {exception.Message}");
                _menu.ShowLoadError("Impossible de charger cette sauvegarde : fichier absent, endommagé ou incompatible. La partie actuelle est conservée.");
            }
        });
    }
}
