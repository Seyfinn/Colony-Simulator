using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.View;

namespace GodColony;

public partial class Main
{
    private GameSettings _settings = null!;
    private GameMenu _menu = null!;
    private ColonyCreationPanel _foundingPanel = null!;
    private FoundingPreview _foundingPreview = null!;
    private LocalMap? _foundingMap;
    private Vector2I? _foundingSite;
    private int _foundingTile = -1;

    /// <summary>Les régions puis les camps conseillés, et le rang de celui qui est proposé (-1 : aucun encore) ; chaque clic passe au suivant.</summary>
    private IReadOnlyList<int> _regionSuggestions = [];
    private Species? _regionSuggestionsFor;
    private int _regionSuggestion = -1;
    private IReadOnlyList<(int X, int Y)> _siteSuggestions = [];
    private int _siteSuggestion = -1;
    private const int SuggestionCount = 5;
    private bool _mapBeforeFounding;
    private Label? _notification;
    private PanelContainer? _notificationCard;
    private double _notificationTime;
    private LocalMap ActiveMap => _foundingMap ?? (_world.Colonies.Count == 0 ? _world.Map : Observed.Map);

    private void InitMenus()
    {
        DisplayServer.WindowSetMinSize(new Vector2I(1100, 700));
        _settings = GameSettings.Load();
        _settings.Apply();
        _saves = new SaveSlots(ProjectSettings.GlobalizePath("user://saves"));
        _menu = new GameMenu();
        _menu.Init(_settings);
        _menu.ReadSaveSlot = _saves.Read;
        AddChild(_menu);
        _menu.WorldRequested += options => StartWorld(options);
        _menu.ResumeRequested += ResumeWorld;
        _menu.HomeRequested += HideWorldInterface;
        _menu.SettingsChanged += ApplySettings;
        _menu.SaveRequested += SaveWorld;
        _menu.LoadRequested += LoadWorld;

        string[] args = OS.GetCmdlineUserArgs();
        if (args.Contains("--smoke-saves"))
        {
            Callable.From(RunSaveSmokeTest).CallDeferred();
            return;
        }
        if (args.Contains("--smoke-menu"))
        {
            Callable.From(RunInterfaceSmokeTest).CallDeferred();
            return;
        }
        bool menuOnly = args.Contains("--menu") || args.Contains("--menu-world") || args.Contains("--menu-settings");
        if (args.Length > 0 && !menuOnly)
        {
            bool empty = args.Contains("--empty-world");
            StartWorld(new WorldCreationOptions(Seed, 200, empty ? 0 : ColonyCount, 8, true, true, true, GameSpeed.Observation), development: true);
            if (args.Contains("--demo-foundation"))
            {
                BeginFounding(); PreviewRegion(_world.WorldMap.SuggestTile(_foundingPanel.Species)); SelectSuggestedSite();
            }
        }
        else foreach (string arg in args)
            if (arg.StartsWith("--capture=")) _capturePath = arg["--capture=".Length..];
        if (args.Contains("--menu-world")) _menu.ShowCreation();
        if (args.Contains("--menu-settings")) _menu.ShowSettings();
    }

    private void StartWorld(WorldCreationOptions options, bool development = false, WorldState? restored = null, int observed = 0)
    {
        // Retirer les vues débranche leurs événements ; le menu est conservé entre deux parties.
        foreach (Node child in GetChildren())
        {
            if (child == _menu) continue;
            RemoveChild(child); child.QueueFree();
        }
        _colonistsView = null; _mapView = null; _selected = null; _foundingMap = null; _foundingSite = null;
        _notification = null; _notificationCard = null; _notificationTime = 0;
        _pendingTicks = 0; _hudCooldown = 0;
        ResetStatsMode();
        BuildWorld(options, development, restored, observed);
        _menu.HasWorld = true; _menu.Close();
        _camera.ControlsEnabled = true;
    }

    private void ApplySettings()
    {
        if (_camera is not null) _camera.Sensitivity = _settings.CameraSensitivity;
        if (_ambience is not null) _ambience.Visible = _settings.AmbientEffects && !_statsShown;
    }

    private void HideWorldInterface()
    {
        if (_world is null) return;
        _hud.Hide(); _worldPanel.Hide(); _prayerPanel.Hide(); _foundingPanel.Hide(); _statsPanel?.Hide();
        _camera.ControlsEnabled = false;
    }

    private void ResumeWorld()
    {
        if (_world is null) return;
        _menu.Close();
        _hud.Show(); _worldPanel.Show(); _prayerPanel.Show(); _foundingPanel.Show();
        if (_statsShown) _statsPanel?.Show();
        _camera.ControlsEnabled = !_worldPanel.MapOpen;
        _pendingTicks = 0; _hudCooldown = 0;
    }

    private void OpenPauseMenu()
    {
        if (_world is null) return;
        if (_foundingPanel.IsOpen) CancelFounding();
        _hud.CancelRename();
        _pendingTicks = 0; _camera.ControlsEnabled = false;
        _menu.ShowPause();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } shortcut && shortcut.Keycode is Key.F5 or Key.F9)
        {
            if (!_menu.IsBusy)
            {
                if (shortcut.Keycode == Key.F5) SaveWorld(SaveSlots.QuickSlot);
                else { if (_world is not null) OpenPauseMenu(); _menu.RequestQuickLoad(); }
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_menu.IsOpen) _menu.Back();
        else if (_foundingPanel.IsOpen) CancelFounding();
        else if (_hud.IsRenaming) _hud.CancelRename();
        else if (_hud.HelpOpen) _hud.CloseHelp();
        else if (_prayerPanel.Open) _prayerPanel.Open = false;
        else if (_worldPanel.MapOpen) _worldPanel.MapOpen = false;
        else if (_worldPanel.Open) _worldPanel.Open = false;
        else if (_worldPanel.CivilizationOpen) _worldPanel.CivilizationOpen = false;
        else if (_selected is not null) Select(null);
        else OpenPauseMenu();
        GetViewport().SetInputAsHandled();
    }

    private void BeginFounding()
    {
        if (_menu.IsOpen || _foundingPanel.IsOpen) return;
        if (_world.Colonies.Count >= WorldState.MaxPlayerColonies) { Notify("Le monde accueille déjà 16 colonies."); return; }
        // On choisit le camp sur le terrain : la vue chiffrée laisse d'abord la place à la carte.
        ReturnToMap();
        Select(null);
        _mapBeforeFounding = _worldPanel.MapOpen;
        _worldPanel.Open = false; _worldPanel.CivilizationOpen = false; _pendingTicks = 0;
        _hud.SetSpeedControlsEnabled(false);
        _foundingPanel.Open(_world.Colonies.Count + 1);
        _worldPanel.SetNavigationEnabled(false);
        ChooseRegion();
    }

    private void ChooseRegion()
    {
        if (_foundingMap is not null) RestoreObservedMap();
        _foundingSite = null; _foundingPreview.Hide();
        _regionSuggestion = -1; _worldPanel.ShowSuggestions([], -1);
        _foundingPanel.ShowRegionStage();
        _worldPanel.PickingSite = true; _camera.ControlsEnabled = false;
    }

    private void PreviewRegion(int tile)
    {
        if (!_foundingPanel.IsOpen || !_world.WorldMap.CanSettle(tile, out _)) return;
        _foundingTile = tile;
        _worldPanel.ShowSuggestions([], -1);
        _foundingMap = _world.GenerateColonyMap(tile);
        DisplayMap(_foundingMap, null);
        _worldPanel.PickingSite = false; _worldPanel.MapOpen = false;
        _foundingPanel.ShowTerrainStage(_world.WorldMap.Grid[tile].Describe().ToLowerInvariant());
        _foundingPreview.SelectedTile = null; _foundingPreview.Show();
        _siteSuggestions = ColonyFounder.FindCampSites(_foundingMap, SuggestionCount); _siteSuggestion = -1;
        _foundingPreview.Suggestions = [.. _siteSuggestions.Select(s => new Vector2I(s.X, s.Y))];
        (int cx, int cy) = _siteSuggestions[0];
        _camera.Position = new Vector2(cx + 0.5f, cy + 0.5f) * TerrainPainter.TileSize;
        _camera.Zoom = Vector2.One * 1.25f; _camera.ControlsEnabled = true;
        _hudCooldown = 0;
    }

    private void SelectFoundingSite(int x, int y)
    {
        if (_foundingMap is null) return;
        // Un clic libre sort de la liste des conseils : le suivant redémarre au meilleur.
        if (_siteSuggestion >= 0 && _siteSuggestions[_siteSuggestion] != (x, y)) _siteSuggestion = -1;
        bool valid = ColonyFounder.CanFoundAt(_foundingMap, x, y, out string reason);
        _foundingSite = new Vector2I(x, y);
        _foundingPreview.SelectedTile = _foundingSite; _foundingPreview.SelectedValid = valid;
        _foundingPanel.SetSite(x, y, valid, reason); _foundingPreview.QueueRedraw();
    }

    /// <summary>Propose le camp conseillé suivant (au terrain) ou la région conseillée suivante (sur la carte du monde).</summary>
    private void SelectSuggestedSite()
    {
        if (!_foundingPanel.IsOpen) return;
        if (_foundingMap is null) SuggestRegion();
        else SuggestCamp();
    }

    private void SuggestRegion()
    {
        Species species = _foundingPanel.Species;
        if (_regionSuggestionsFor != species || _regionSuggestion < 0)
        {
            _regionSuggestions = _world.WorldMap.SuggestTiles(species, SuggestionCount);
            _regionSuggestionsFor = species; _regionSuggestion = -1;
        }
        if (_regionSuggestions.Count == 0) return;
        _regionSuggestion = (_regionSuggestion + 1) % _regionSuggestions.Count;
        int tile = _regionSuggestions[_regionSuggestion];
        _worldPanel.ShowSuggestions(_regionSuggestions, tile);
        _foundingPanel.SetSuggestedRegion(_regionSuggestion + 1, _regionSuggestions.Count, _world.WorldMap.Grid[tile].Describe().ToLowerInvariant());
    }

    private void SuggestCamp()
    {
        if (_foundingMap is null || _siteSuggestions.Count == 0) return;
        _siteSuggestion = (_siteSuggestion + 1) % _siteSuggestions.Count;
        (int x, int y) = _siteSuggestions[_siteSuggestion];
        bool valid = ColonyFounder.CanFoundAt(_foundingMap, x, y, out string reason);
        _foundingSite = new Vector2I(x, y);
        _foundingPreview.SelectedTile = _foundingSite; _foundingPreview.SelectedValid = valid;
        _foundingPanel.SetSite(x, y, valid, reason, _siteSuggestion + 1, _siteSuggestions.Count);
        _foundingPreview.QueueRedraw();
        _camera.Position = new Vector2(x + 0.5f, y + 0.5f) * TerrainPainter.TileSize;
    }

    private void ConfirmFounding()
    {
        if (_foundingMap is null || _foundingSite is not { } site) return;
        if (!_world.TryFoundColony(_foundingMap, site.X, site.Y, _foundingPanel.ColonyName, _foundingPanel.Species,
            _foundingPanel.Founders, _foundingTile, out Colony? colony, out string reason))
        {
            _foundingPanel.SetError(reason); return;
        }
        _foundingMap = null; _foundingSite = null;
        _foundingPanel.Close(); _foundingPreview.Hide();
        _worldPanel.PickingSite = false; _worldPanel.MapOpen = false;
        _worldPanel.SetNavigationEnabled(true); _hud.SetSpeedControlsEnabled(true);
        // La carte affichée est déjà la bonne : seul le camp et ses habitants doivent apparaître.
        _observed = _world.Colonies.Count - 1; _worldPanel.Observed = _observed;
        _colonistsView = new ColonistsView();
        AddChild(_colonistsView); _colonistsView.Init(_world, colony!);
        _hud.ResetColony(); _hudCooldown = 0; _pendingTicks = 0;
        Notify(reason);
    }

    private void CancelFounding()
    {
        if (!_foundingPanel.IsOpen) return;
        if (_foundingMap is not null) RestoreObservedMap();
        _foundingSite = null;
        _foundingPanel.Close(); _foundingPreview.Hide();
        _worldPanel.PickingSite = false; _worldPanel.MapOpen = _mapBeforeFounding;
        _worldPanel.SetNavigationEnabled(true); _hud.SetSpeedControlsEnabled(true);
        _camera.ControlsEnabled = !_worldPanel.MapOpen;
        _pendingTicks = 0; _hudCooldown = 0;
    }

    private void RestoreObservedMap()
    {
        _foundingMap = null;
        Colony? colony = _world.Colonies.Count == 0 ? null : Observed;
        DisplayMap(colony?.Map ?? _world.Map, colony);
        _camera.Position = new Vector2((colony?.CampX ?? _world.Map.Width / 2) + 0.5f,
            (colony?.CampY ?? _world.Map.Height / 2) + 0.5f) * TerrainPainter.TileSize;
    }

    private void DisplayMap(LocalMap map, Colony? colony)
    {
        _camera.WorldBounds = new Rect2(Vector2.Zero, new Vector2(map.Width, map.Height) * TerrainPainter.TileSize);
        if (_mapView is not null) { RemoveChild(_mapView); _mapView.QueueFree(); }
        if (_colonistsView is not null) { RemoveChild(_colonistsView); _colonistsView.QueueFree(); _colonistsView = null; }
        _mapView = new MapView(); AddChild(_mapView); MoveChild(_mapView, 0); _mapView.Init(map);
        if (colony is not null)
        {
            _colonistsView = new ColonistsView(); AddChild(_colonistsView); _colonistsView.Init(_world, colony);
        }
        _ambience.Init(map); _hud.ResetColony(); _hudCooldown = 0;
    }

    private void UpdateFoundingPreview()
    {
        _camera.ControlsEnabled = !_menu.IsOpen && !_worldPanel.MapOpen && !_statsShown;
        if (_foundingMap is not null && !_menu.IsOpen)
        {
            (int x, int y) = TileUnderMouse();
            _foundingPreview.HoverTile = new Vector2I(x, y);
            _foundingPreview.Valid = ColonyFounder.CanFoundAt(_foundingMap, x, y, out _);
            _foundingPreview.QueueRedraw();
        }
        if (_notification is not null && _notificationTime > 0)
        {
            _notificationTime -= GetProcessDeltaTime();
            if (_notificationTime <= 0) _notificationCard?.Hide();
        }
    }

    private void Notify(string message)
    {
        if (_notification is null)
        {
            var layer = new CanvasLayer { Layer = 16 };
            AddChild(layer);
            var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _notificationCard = new PanelContainer { Name = "Notification", MouseFilter = Control.MouseFilterEnum.Ignore };
            root.AddChild(_notificationCard);
            _notificationCard.AddThemeStyleboxOverride("panel", MenuStyle.Surface(12));
            _notificationCard.AnchorTop = _notificationCard.AnchorBottom = 1;
            _notificationCard.OffsetTop = -130; _notificationCard.OffsetBottom = -70;
            _notificationCard.GrowVertical = Control.GrowDirection.Begin;
            void ResizeNotification()
            {
                float left = 452;
                float right = root.Size.X - InterfaceLayout.SideWidth(root.Size) - 32;
                float width = System.Math.Clamp(right - left, 240, 560);
                _notificationCard.OffsetLeft = (left + right - width) / 2;
                _notificationCard.OffsetRight = _notificationCard.OffsetLeft + width;
            }
            root.Resized += ResizeNotification;
            ResizeNotification();
            _notification = MenuStyle.Text(_notificationCard, "", 14, ArtDirection.Brass, true);
            _notification.HorizontalAlignment = HorizontalAlignment.Center;
        }
        _notification.Text = message; _notificationCard!.Show(); _notificationTime = 5;
    }

    private void CaptureFrame()
    {
        if (_capturePath is not null && --_framesBeforeCapture == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_capturePath);
            GetTree().Quit();
        }
    }
}
