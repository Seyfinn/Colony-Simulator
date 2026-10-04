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
    private bool _mapBeforeFounding;
    private Label? _notification;
    private double _notificationTime;
    private LocalMap ActiveMap => _foundingMap ?? (_world.Colonies.Count == 0 ? _world.Map : Observed.Map);

    private void InitMenus()
    {
        DisplayServer.WindowSetMinSize(new Vector2I(1100, 700));
        _settings = GameSettings.Load();
        _settings.Apply();
        _menu = new GameMenu();
        _menu.Init(_settings);
        AddChild(_menu);
        _menu.WorldRequested += options => StartWorld(options);
        _menu.ResumeRequested += ResumeWorld;
        _menu.HomeRequested += HideWorldInterface;
        _menu.SettingsChanged += ApplySettings;

        string[] args = OS.GetCmdlineUserArgs();
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

    private void StartWorld(WorldCreationOptions options, bool development = false)
    {
        // Retirer les vues débranche leurs événements ; le menu est conservé entre deux parties.
        foreach (Node child in GetChildren())
        {
            if (child == _menu) continue;
            RemoveChild(child); child.QueueFree();
        }
        _colonistsView = null; _selected = null; _foundingMap = null; _foundingSite = null;
        _notification = null; _notificationTime = 0;
        _pendingTicks = 0; _hudCooldown = 0;
        BuildWorld(options, development);
        _menu.HasWorld = true; _menu.Close();
        _camera.ControlsEnabled = true;
    }

    private void ApplySettings()
    {
        if (_camera is not null) _camera.Sensitivity = _settings.CameraSensitivity;
        if (_ambience is not null) _ambience.Visible = _settings.AmbientEffects;
    }

    private void HideWorldInterface()
    {
        if (_world is null) return;
        _hud.Hide(); _worldPanel.Hide(); _prayerPanel.Hide(); _foundingPanel.Hide();
        _camera.ControlsEnabled = false;
    }

    private void ResumeWorld()
    {
        if (_world is null) return;
        _menu.Close();
        _hud.Show(); _worldPanel.Show(); _prayerPanel.Show(); _foundingPanel.Show();
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
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_menu.IsOpen) _menu.Back();
        else if (_foundingPanel.IsOpen) CancelFounding();
        else if (_worldPanel.MapOpen) _worldPanel.MapOpen = false;
        else if (_worldPanel.Open) _worldPanel.Open = false;
        else if (_selected is not null) Select(null);
        else OpenPauseMenu();
        GetViewport().SetInputAsHandled();
    }

    private void BeginFounding()
    {
        if (_menu.IsOpen || _foundingPanel.IsOpen) return;
        if (_world.Colonies.Count >= WorldState.MaxPlayerColonies) { Notify("Le monde accueille déjà 16 colonies."); return; }
        Select(null);
        _mapBeforeFounding = _worldPanel.MapOpen;
        _worldPanel.Open = false; _pendingTicks = 0;
        _hud.SetSpeedControlsEnabled(false);
        _foundingPanel.Open(_world.Colonies.Count + 1);
        _worldPanel.SetNavigationEnabled(false);
        ChooseRegion();
    }

    private void ChooseRegion()
    {
        if (_foundingMap is not null) RestoreObservedMap();
        _foundingSite = null; _foundingPreview.Hide();
        _foundingPanel.ShowRegionStage();
        _worldPanel.PickingSite = true; _camera.ControlsEnabled = false;
    }

    private void PreviewRegion(int tile)
    {
        if (!_foundingPanel.IsOpen || !_world.WorldMap.CanSettle(tile, out _)) return;
        _foundingTile = tile;
        _foundingMap = _world.GenerateColonyMap(tile);
        DisplayMap(_foundingMap, null);
        _worldPanel.PickingSite = false; _worldPanel.MapOpen = false;
        _foundingPanel.ShowTerrainStage(_world.WorldMap.Grid[tile].Describe().ToLowerInvariant());
        _foundingPreview.SelectedTile = null; _foundingPreview.Show();
        (int cx, int cy) = ColonyFounder.FindCampSite(_foundingMap);
        _camera.Position = new Vector2(cx + 0.5f, cy + 0.5f) * TerrainPainter.TileSize;
        _camera.Zoom = Vector2.One * 1.25f; _camera.ControlsEnabled = true;
        _hudCooldown = 0;
    }

    private void SelectFoundingSite(int x, int y)
    {
        if (_foundingMap is null) return;
        bool valid = ColonyFounder.CanFoundAt(_foundingMap, x, y, out string reason);
        _foundingSite = new Vector2I(x, y);
        _foundingPreview.SelectedTile = _foundingSite; _foundingPreview.SelectedValid = valid;
        _foundingPanel.SetSite(x, y, valid, reason); _foundingPreview.QueueRedraw();
    }

    private void SelectSuggestedSite()
    {
        if (_foundingMap is null) return;
        (int x, int y) = ColonyFounder.FindCampSite(_foundingMap);
        SelectFoundingSite(x, y);
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
        RemoveChild(_mapView); _mapView.QueueFree();
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
        _camera.ControlsEnabled = !_menu.IsOpen && !_worldPanel.MapOpen;
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
            if (_notificationTime <= 0) _notification.Hide();
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
            _notification = MenuStyle.Text(root, "", 16, ArtDirection.Brass);
            _notification.AnchorLeft = 0.5f; _notification.AnchorRight = 0.5f;
            _notification.OffsetLeft = -350; _notification.OffsetRight = 350; _notification.OffsetTop = 270;
            _notification.HorizontalAlignment = HorizontalAlignment.Center;
        }
        _notification.Text = message; _notification.Show(); _notificationTime = 5;
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
