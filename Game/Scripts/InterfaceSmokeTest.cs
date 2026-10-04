using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony;

/// <summary>Vérification du parcours réel des contrôles Godot, lancée uniquement avec --smoke-menu.</summary>
public partial class Main
{
    private async void RunInterfaceSmokeTest()
    {
        try
        {
            await UiFrames(2);
            const string settingsPath = "res://.godot/interface-smoke-settings.cfg";
            var savedSettings = new GameSettings { AmbientEffects = false, CameraSensitivity = 1.7f, VSync = false };
            Require(savedSettings.Save(settingsPath) == Error.Ok, "Les paramètres doivent pouvoir être enregistrés.");
            var loadedSettings = GameSettings.Load(settingsPath);
            Require(!loadedSettings.AmbientEffects && !loadedSettings.VSync && Math.Abs(loadedSettings.CameraSensitivity - 1.7f) < 0.01f,
                "Les paramètres doivent être retrouvés au chargement.");
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(settingsPath));
            Require(_menu.IsOpen && _world is null, "L'accueil doit précéder la simulation.");
            Click(_menu, "Créer un monde");
            FindNamed<LineEdit>(_menu, "Graine").Text = "invalide";
            Click(_menu, "Créer et explorer");
            Require(_world is null, "Une graine invalide doit rester dans le formulaire.");
            FindNamed<LineEdit>(_menu, "Graine").Text = "12345";
            FindNamed<OptionButton>(_menu, "TailleMonde").Select(0);
            Click(_menu, "Créer et explorer");
            await UiFrames(6);
            Require(!_menu.IsOpen && _world!.Colonies.Count == 0 && _world.Map.Width == 128, "Créer un monde vierge depuis le formulaire.");

            // Les boutons émettent leurs vrais signaux, comme lors d'une activation à la souris ou au clavier.
            FindNamed<Button>(_worldPanel, "FonderColonie").EmitSignal(BaseButton.SignalName.Pressed);
            Require(_foundingPanel.IsOpen && _worldPanel.PickingSite, "Ouvrir la fondation sur la carte du monde.");
            long frozen = _world!.Clock.Ticks;
            await UiFrames(8);
            Require(_world.Clock.Ticks == frozen, "La fondation doit suspendre le temps.");
            FindNamed<LineEdit>(_foundingPanel, "NomColonie").Text = "Clairerive";
            var species = FindNamed<OptionButton>(_foundingPanel, "PeupleFondateur");
            species.Select(2); species.EmitSignal(OptionButton.SignalName.ItemSelected, 2);
            FindNamed<SpinBox>(_foundingPanel, "NombreFondateurs").Value = 12;
            var worldMap = Descendants(_worldPanel).OfType<WorldMapView>().Single();
            // Le pilote sans affichage ne dimensionne pas les contrôles ; ce rectangle permet d'y tester le même clic.
            if (worldMap.Size == Vector2.Zero) worldMap.Size = new Vector2(1164, 548);
            worldMap._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true,
                Position = worldMap.ScreenPositionOf(_world.WorldMap.SuggestTile(_foundingPanel.Species)),
            });
            Require(_foundingMap is not null && !_worldPanel.MapOpen, "Choisir une région par un clic sur la carte.");
            SelectFoundingSite(0, 0);
            Require(FindNamed<Button>(_foundingPanel, "ConfirmerFondation").Disabled, "Le bord doit désactiver la confirmation.");
            Click(_foundingPanel, "Emplacement conseillé");
            Require(!FindNamed<Button>(_foundingPanel, "ConfirmerFondation").Disabled, "Le site conseillé doit être constructible.");
            var chosen = _foundingSite!.Value;
            await UiFrames(3);
            SaveSmokeCapture("fondation");
            Click(_foundingPanel, "Fonder la colonie");
            Require(_world.Colonies.Count == 1 && !_foundingPanel.IsOpen, "La confirmation doit installer une seule colonie.");
            Colony colony = _world.Colonies[0];
            Require(colony.Name == "Clairerive" && colony.Species == Species.Elf && colony.Members.Count == 12,
                "Le nom, le peuple et les habitants choisis doivent être conservés.");
            Require(colony.CampX == chosen.X && colony.CampY == chosen.Y, "Le camp doit occuper la case choisie.");
            await UiFrames(6);
            SaveSmokeCapture("colonie");

            FindNamed<Button>(_hud, "MenuJeu").EmitSignal(BaseButton.SignalName.Pressed);
            frozen = _world.Clock.Ticks;
            await UiFrames(8);
            Require(_world.Clock.Ticks == frozen && !_camera.ControlsEnabled, "Le menu doit bloquer la simulation et la caméra.");
            Click(_menu, "Paramètres");
            Require(Descendants(_menu).OfType<CheckBox>().Count() == 3, "Les paramètres doivent être disponibles en partie.");
            _menu.Back(); Click(_menu, "Reprendre la partie");
            await UiFrames(8);
            Require(_world.Clock.Ticks > frozen, "La simulation doit reprendre à la fermeture du menu.");

            SetSpeed(GameSpeed.Pause);
            FindNamed<Button>(_worldPanel, "FonderColonie").EmitSignal(BaseButton.SignalName.Pressed);
            PreviewRegion(_world.WorldMap.SuggestTile(_foundingPanel.Species));
            Click(_foundingPanel, "Annuler");
            Require(_world.Colonies.Count == 1 && _foundingMap is null && _speed == GameSpeed.Pause,
                "Annuler doit conserver les colonies, leur carte et la vitesse précédente.");
            Require(ActiveMap == colony.Map && _colonistsView is not null, "L'annulation doit restaurer le terrain et les habitants observés.");

            OpenPauseMenu(); Click(_menu, "Retour à l'accueil");
            Require(!_hud.Visible && _menu.HasWorld, "L'accueil doit conserver la partie reprenable.");
            Click(_menu, "Reprendre la partie");
            Require(_hud.Visible && _world.Colonies[0] == colony, "La reprise doit conserver la même partie.");

            OpenPauseMenu(); Click(_menu, "Créer un monde");
            FindNamed<OptionButton>(_menu, "TailleMonde").Select(0);
            FindNamed<OptionButton>(_menu, "ColoniesInitiales").Select(2);
            Click(_menu, "Créer et explorer");
            var confirmation = Descendants(_menu).OfType<ConfirmationDialog>().Single(d => d.Visible);
            Require(confirmation.Visible && _world.Colonies[0] == colony, "La partie doit rester intacte avant confirmation de remplacement.");
            confirmation.Hide(); confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            await UiFrames(6);
            Require(_world.Colonies.Count == 2 && _world.Colonies[0] != colony && !_foundingPanel.IsOpen,
                "Le remplacement doit créer un nouveau monde et nettoyer l'ancienne interface.");
            _worldPanel.MapOpen = true;
            await UiFrames(4);
            SaveSmokeCapture("monde");
            GD.Print("INTERFACE_SMOKE_OK : accueil, graine invalide, monde vierge, fondation, pause, paramètres, annulation, reprise et remplacement.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"INTERFACE_SMOKE_FAILED : {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task UiFrames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void SaveSmokeCapture(string name)
    {
        string? arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--smoke-captures="));
        if (arg is not null) GetViewport().GetTexture().GetImage().SavePng($"{arg["--smoke-captures=".Length..]}/{name}.png");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child)) yield return nested;
        }
    }

    private static T FindNamed<T>(Node root, string name) where T : Node =>
        Descendants(root).OfType<T>().First(n => n.Name == name);

    private static void Click(Node root, string caption) => Descendants(root).OfType<Button>()
        .First(b => b.Text == caption && b.IsVisibleInTree()).EmitSignal(BaseButton.SignalName.Pressed);
}
