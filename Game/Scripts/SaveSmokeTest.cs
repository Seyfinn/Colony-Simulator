using System;
using System.IO;
using System.Linq;
using Godot;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony;

/// <summary>Parcours des vrais contrôles de sauvegarde ; les fichiers de test restent dans le cache du projet.</summary>
public partial class Main
{
    private async void RunSaveSmokeTest()
    {
        string directory = ProjectSettings.GlobalizePath("res://.godot/save-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            await UiFrames(2);
            _saves = new SaveSlots(directory);
            _menu.ReadSaveSlot = _saves.Read;
            Click(_menu, "Charger une partie");
            Require(FindNamed<Button>(_menu, "Charger0").Disabled, "Un emplacement vide ne doit pas être chargeable.");
            _menu.Back();
            StartWorld(new WorldCreationOptions(12345, 128, 2, 8, true, true, true, GameSpeed.Pause));
            for (int i = 0; i < 3200; i++) _world.Step();
            ObserveColony(1);
            _camera.Position = new Vector2(240, 480); _camera.Zoom = Vector2.One * 2;
            Select(Observed.Members[0]);
            int selected = _selected!.Id;
            string name = _selected.FullName;
            long firstTicks = _world.Clock.Ticks;
            OpenPauseMenu(); Click(_menu, "Sauvegarder la partie");
            FindNamed<Button>(_menu, "Sauvegarder0").EmitSignal(BaseButton.SignalName.Pressed);
            Require(_saves.Read(0).Info?.Ticks == firstTicks, "Le bouton doit enregistrer le monde sur disque.");

            _world.Step();
            FindNamed<Button>(_menu, "Sauvegarder0").EmitSignal(BaseButton.SignalName.Pressed);
            var confirmation = Descendants(_menu).OfType<ConfirmationDialog>().Single(d => d.Visible);
            Require(WorldSave.Load(_saves.PathFor(0)).World.Clock.Ticks == firstTicks, "L'écrasement doit attendre la confirmation.");
            _menu.Back();
            Require(!confirmation.Visible, "Échap doit annuler le remplacement.");
            FindNamed<Button>(_menu, "Sauvegarder0").EmitSignal(BaseButton.SignalName.Pressed);
            ConfirmVisibleSaveDialog();
            long savedTicks = _world.Clock.Ticks;
            Require(_saves.Read(0).BackupAvailable && WorldSave.Load(_saves.PathFor(0, true)).World.Clock.Ticks == firstTicks,
                "La version précédente doit pouvoir être récupérée.");
            await UiFrames(3);
            SaveSmokeCapture("sauvegardes");
            ResumeWorld();
            _Input(new InputEventKey { Pressed = true, Keycode = Key.F5 });
            Require(_saves.Read(SaveSlots.QuickSlot).Info?.Ticks == savedTicks, "F5 doit enregistrer une sauvegarde rapide.");

            StartWorld(new WorldCreationOptions(7, 128, 0, 8, false, false, false, GameSpeed.Pause));
            _saves = new SaveSlots(directory); _menu.ReadSaveSlot = _saves.Read;
            OpenPauseMenu(); Click(_menu, "Charger une partie");
            Require(!FindNamed<Button>(_menu, "Charger0").Disabled, "Les sauvegardes doivent être retrouvées depuis un nouveau gestionnaire.");
            await UiFrames(3);
            SaveSmokeCapture("chargement");
            var before = _world;
            FindNamed<Button>(_menu, "Charger0").EmitSignal(BaseButton.SignalName.Pressed);
            _menu.Back();
            Require(ReferenceEquals(before, _world), "Annuler le chargement doit conserver la partie.");
            FindNamed<Button>(_menu, "Charger0").EmitSignal(BaseButton.SignalName.Pressed);
            ConfirmVisibleSaveDialog();
            await UiFrames(8);
            Require(!_menu.IsOpen && _world.Seed == 12345 && _world.Clock.Ticks == savedTicks && _world.Colonies.Count == 2,
                "Le chargement doit restaurer le monde et sa date exacte.");
            Require(_observed == 1 && _camera.Position == new Vector2(240, 480) && _camera.Zoom == Vector2.One * 2
                && _speed == GameSpeed.Pause && _selected?.Id == selected && _selected.FullName == name,
                "La caméra, la vitesse, la colonie observée et l'habitant sélectionné doivent revenir.");

            before = _world;
            byte[] valid = File.ReadAllBytes(_saves.PathFor(0));
            File.WriteAllBytes(_saves.PathFor(0), valid.Take(valid.Length / 2).ToArray());
            OpenPauseMenu(); LoadWorld(0, false);
            await UiFrames(8);
            Require(ReferenceEquals(before, _world) && _menu.IsOpen && !_menu.IsBusy,
                "Un fichier endommagé doit laisser le monde intact et rendre le menu utilisable.");
            FindNamed<Button>(_menu, "ChargerSecours0").EmitSignal(BaseButton.SignalName.Pressed);
            ConfirmVisibleSaveDialog();
            await UiFrames(8);
            Require(!_menu.IsOpen && _world.Clock.Ticks == firstTicks, "Le bouton de secours doit restaurer la version précédente.");
            _Input(new InputEventKey { Pressed = true, Keycode = Key.F9 });
            ConfirmVisibleSaveDialog();
            await UiFrames(8);
            Require(!_menu.IsOpen && _world.Clock.Ticks == savedTicks, "F9 doit restaurer la sauvegarde rapide après confirmation.");

            // Une partie enregistrée en pause dans la vue chiffrée s'y retrouve au chargement, sans repeindre la carte.
            SetSpeed(GameSpeed.Fulgurante);
            TogglePause();
            await UiFrames(2);
            _Input(new InputEventKey { Pressed = true, Keycode = Key.F5 });
            long statsTicks = _world.Clock.Ticks;
            SetSpeed(GameSpeed.Observation);
            await UiFrames(4);
            Require(!_statsShown && _mapView is not null, "Quitter la vue chiffrée doit rendre la carte.");
            _Input(new InputEventKey { Pressed = true, Keycode = Key.F9 });
            ConfirmVisibleSaveDialog();
            await UiFrames(8);
            Require(!_menu.IsOpen && _world.Clock.Ticks == statsTicks && _statsShown && _mapView is null
                && _speed == GameSpeed.Pause && _speedBeforePause == GameSpeed.Fulgurante,
                "Une partie enregistrée en vue chiffrée doit s'y retrouver au chargement, sans peindre la carte.");
            GD.Print("SAVE_SMOKE_OK : emplacements, écrasement confirmé, copie de secours, reprise complète, annulation, fichier invalide, F5, F9 et vue chiffrée.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"SAVE_SMOKE_FAILED : {exception}");
            GetTree().Quit(1);
        }
        finally
        {
            for (int slot = 0; slot <= SaveSlots.QuickSlot; slot++)
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                {
                    string path = Path.Combine(directory, slot == SaveSlots.QuickSlot ? "quick.gcsave" : $"world-{slot + 1}.gcsave") + suffix;
                    if (File.Exists(path)) File.Delete(path);
                }
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    private void ConfirmVisibleSaveDialog()
    {
        var confirmation = Descendants(_menu).OfType<ConfirmationDialog>().Single(d => d.Visible);
        confirmation.Hide(); confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
    }
}
