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
            Click(_foundingPanel, "Emplacement conseillé");
            int firstRegion = worldMap.CurrentSuggestion;
            Click(_foundingPanel, "Emplacement conseillé");
            Require(firstRegion >= 0 && worldMap.CurrentSuggestion >= 0 && worldMap.CurrentSuggestion != firstRegion && _foundingMap is null,
                "Chaque clic sur « Emplacement conseillé » doit proposer une autre région sans quitter la carte du monde.");
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
            if (_siteSuggestions.Count > 1)
            {
                Click(_foundingPanel, "Emplacement conseillé");
                Require(_foundingSite!.Value != chosen, "Un deuxième clic doit proposer un autre camp conseillé.");
                chosen = _foundingSite!.Value;
            }
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
            await VerifyObservationInterface();

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
            _worldPanel.MapOpen = false;
            GetViewport().GuiReleaseFocus();
            PressObservationKey(Key.Tab);
            Require(_observed == 1, "Tab doit observer la colonie suivante.");
            PressObservationKey(Key.Tab);
            Require(_observed == 0, "Tab doit revenir à la première colonie après la dernière.");
            GD.Print("INTERFACE_SMOKE_OK : menus, fondation, sauvegarde des paramètres, reprise, remplacement, raccourcis, saisie protégée, panneaux et économie stable.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"INTERFACE_SMOKE_FAILED : {exception}");
            GetTree().Quit(1);
        }
    }

    private void PressObservationKey(Key key) => _UnhandledInput(new InputEventKey { Pressed = true, Keycode = key });

    private async Task VerifyObservationInterface()
    {
        SetSpeed(GameSpeed.Pause);
        var savedFood = new Dictionary<ResourceType, int>();
        foreach (var type in new[] { ResourceType.Food, ResourceType.Fish, ResourceType.Grain, ResourceType.Bread, ResourceType.Flour })
        {
            savedFood[type] = Observed.Stock.Get(type);
            Observed.Stock.TryTake(type, savedFood[type]);
        }
        Observed.Stock.Add(ResourceType.Food, 10);
        Observed.Stock.Add(ResourceType.Fish, 2);
        Observed.Stock.Add(ResourceType.Grain, 4);
        Observed.Stock.Add(ResourceType.Bread, 1);
        Observed.Stock.Add(ResourceType.Flour, 5);
        await UiFrames(3);
        Require(FindNamed<Label>(_hud, "FoodTotal").Text == 10.45m.ToString("0.##"), "Le total doit pondérer les aliments par leur valeur nutritive.");
        Require(!Descendants(_hud).Any(n => n.Name == "ResourceGrain" || n.Name == "ResourceBread" || n.Name == "ResourceFlour" || n.Name == "ResourceFish"),
            "Tous les aliments doivent partager une seule case.");
        var foodToggle = FindNamed<Button>(_hud, "ToggleFoodDetails");
        var foodDetails = FindNamed<PanelContainer>(_hud, "FoodDetails");
        Require(!foodDetails.Visible, "Le détail doit démarrer replié.");
        foodToggle.EmitSignal(BaseButton.SignalName.Pressed);
        await UiFrames(3);
        Require(foodDetails.Visible && FindNamed<Label>(_hud, "FoodDetailFish").Text.StartsWith("2 ×"), "Le clic doit ouvrir le stock détaillé, poisson compris.");
        Require(foodDetails.GlobalPosition.Y >= foodToggle.GlobalPosition.Y + foodToggle.Size.Y, "Le détail doit s'ouvrir sous la case.");
        SaveSmokeCapture("nourriture");
        Observed.Stock.Add(ResourceType.Fish, 1);
        await UiFrames(3);
        Require(FindNamed<Label>(_hud, "FoodTotal").Text == 11.05m.ToString("0.##")
            && FindNamed<Label>(_hud, "FoodDetailFish").Text.StartsWith("3 ×"), "Le total et le détail doivent suivre les variations du stock.");
        foodToggle.EmitSignal(BaseButton.SignalName.Pressed);
        Require(!foodDetails.Visible, "Un second clic doit replier le stock.");
        foodToggle.EmitSignal(BaseButton.SignalName.Pressed);
        _hud._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Require(!foodDetails.Visible, "Échap doit fermer le détail.");
        foodToggle.EmitSignal(BaseButton.SignalName.Pressed);
        _hud._Input(new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left, Position = Vector2.Zero });
        Require(!foodDetails.Visible, "Un clic ailleurs doit fermer le détail.");
        foreach (var (type, amount) in savedFood)
        {
            Observed.Stock.TryTake(type, Observed.Stock.Get(type));
            Observed.Stock.Add(type, amount);
        }
        GetViewport().GuiReleaseFocus();
        PressObservationKey(Key.M);
        Require(_worldPanel.MapOpen, "M doit ouvrir la carte du monde.");
        PressObservationKey(Key.E);
        Require(_worldPanel.Open && !_worldPanel.MapOpen, "L'économie et la carte doivent s'exclure.");
        await UiFrames(35);
        var scroll = FindNamed<ScrollContainer>(_worldPanel, "DefilementEconomie");
        ulong contentId = scroll.GetChild(0).GetInstanceId();
        scroll.ScrollVertical = 30;
        int position = scroll.ScrollVertical;
        await UiFrames(35);
        Require(scroll.GetChild(0).GetInstanceId() == contentId && scroll.ScrollVertical == position,
            "Actualiser l'économie doit conserver les contrôles et le défilement.");
        SaveSmokeCapture("economie");
        _Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Require(!_worldPanel.Open && !_menu.IsOpen, "Échap doit fermer l'économie sans ouvrir la pause.");

        Select(Observed.Members[0]);
        await UiFrames(8);
        FindNamed<Button>(_hud, "RenameColonist").EmitSignal(BaseButton.SignalName.Pressed);
        var firstName = FindNamed<LineEdit>(_hud, "ColonistFirstName");
        firstName.GrabFocus();
        PressObservationKey(Key.M);
        PressObservationKey(Key.Key3);
        Require(!_worldPanel.MapOpen && _speed == GameSpeed.Pause,
            "Saisir un nom ne doit pas déclencher les raccourcis de jeu.");
        _Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Require(!_hud.IsRenaming && _selected is not null,
            "Échap pendant le renommage doit conserver l'habitant sélectionné.");
        GetViewport().GuiReleaseFocus();
        _camera.Position = Vector2.Zero;
        FindNamed<Button>(_hud, "Recentrer").EmitSignal(BaseButton.SignalName.Pressed);
        Require(_camera.Position == _colonistsView!.DisplayPosition(_selected!), "Recentrer doit rejoindre l'habitant sélectionné.");
        Select(null);
        PressObservationKey(Key.C);
        Require(_camera.Position == new Vector2(Observed.CampX + 0.5f, Observed.CampY + 0.5f) * View.TerrainPainter.TileSize,
            "Sans sélection, C doit rejoindre le camp.");
        _camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        _camera._UnhandledInput(new InputEventMouseMotion { Relative = -Vector2.One * 100000 });
        _camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Require(_camera.Position == _camera.WorldBounds.End, "Un grand glissement doit rester sur le terrain.");
        PressObservationKey(Key.C);

        PressObservationKey(Key.H);
        Require(_hud.HelpOpen, "H doit ouvrir les commandes.");
        _Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Require(!_hud.HelpOpen && !_menu.IsOpen, "Échap doit fermer les commandes.");

        for (int i = 0; i < 8; i++)
            Observed.Prayers.Ask(DecisionKind.Dam, $"interface-{i}", "Construire un barrage ?", "Une longue liste de prières doit rester accessible dans le panneau.", () => { }, _world.Clock);
        Select(Observed.Members[0]);
        PressObservationKey(Key.P);
        Require(_prayerPanel.Open && _selected is null, "Ouvrir les prières doit libérer leur emplacement à droite.");
        await UiFrames(8);
        var prayerScroll = FindNamed<ScrollContainer>(_prayerPanel, "DefilementPrieres");
        if (prayerScroll.Size.Y > 0)
        {
            Require(prayerScroll.GetVScrollBar().MaxValue > prayerScroll.Size.Y, "Plusieurs prières doivent pouvoir défiler.");
            prayerScroll.ScrollVertical = 100;
            Require(prayerScroll.ScrollVertical > 0, "Les prières hors écran doivent rester accessibles.");
            prayerScroll.ScrollVertical = 0;
        }
        SaveSmokeCapture("prieres");
        _Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Require(!_prayerPanel.Open && !_menu.IsOpen, "Échap doit fermer les prières.");
        foreach (var prayer in Observed.Prayers.Pending.ToArray()) _world.AnswerPrayer(prayer, false);
        SetSpeed(GameSpeed.Observation);
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
