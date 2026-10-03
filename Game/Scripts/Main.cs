using System;
using System.Globalization;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.View;

namespace GodColony;

/// <summary>
/// Point d'entrée du jeu : crée la simulation, l'affichage, la caméra et l'interface,
/// puis fait avancer la simulation selon la vitesse choisie.
/// </summary>
public partial class Main : Node2D
{
    private const int Seed = 12345;

    /// <summary>Limite de ticks par image, pour que le jeu ne fige pas si la simulation prend du retard.</summary>
    private const int MaxTicksPerFrame = 2000;

    /// <summary>Distance maximale (en cases) entre le clic et un colon pour le sélectionner.</summary>
    private const float SelectRadius = 0.8f;

    private WorldState _world = null!;
    private ColonistsView _colonistsView = null!;
    private CanvasModulate _daylight = null!;
    private PrayerPanel _prayerPanel = null!;
    private DayNightAmbience _ambience = null!;
    private Hud _hud = null!;

    private GameSpeed _speed = GameSpeed.Observation;
    private GameSpeed _speedBeforePause = GameSpeed.Observation;
    private double _pendingTicks;
    private Colonist? _selected;

    // Outils de développement, passés en ligne de commande après "--" :
    //   --capture=chemin.png   enregistre une capture puis quitte le jeu
    //   --advance-hours=N      fait avancer la simulation de N heures au démarrage
    //   --speed=1|4|30         choisit la vitesse pour vérifier les ambiances
    //   --demo-quarry          creuse une carrière de démonstration
    //   --demo-prayer          soumet une prière factice ; --open-prayers ouvre le détail
    //   --zoom=N               règle le zoom de la caméra (0.2 montre presque toute la carte)
    //   --select-first         sélectionne le premier colon
    //   --focus-fields         centre la caméra sur le premier champ
    private string? _capturePath;
    private int _framesBeforeCapture = 20;

    public override void _Ready()
    {
        _world = new WorldState(Seed);
        Colony colony = _world.Colonies[0];

        var mapView = new MapView();
        AddChild(mapView);
        mapView.Init(_world.Map);

        _colonistsView = new ColonistsView();
        AddChild(_colonistsView);
        _colonistsView.Init(_world);

        _daylight = new CanvasModulate();
        AddChild(_daylight);

        var camera = new CameraController
        {
            Position = new Vector2(colony.CampX + 0.5f, colony.CampY + 0.5f) * TerrainPainter.TileSize,
            Zoom = new Vector2(1.25f, 1.25f),
        };
        AddChild(camera);
        camera.MakeCurrent();

        // Une couche visuelle sous le HUD : aucun effet ne voile les contrôles.
        var atmosphereLayer = new CanvasLayer { Layer = 1 };
        AddChild(atmosphereLayer);
        _ambience = new DayNightAmbience();
        atmosphereLayer.AddChild(_ambience);
        _ambience.Init(_world.Map);

        _prayerPanel = new PrayerPanel();
        AddChild(_prayerPanel);
        _prayerPanel.Init(_world);

        _hud = new Hud();
        AddChild(_hud);
        _hud.SpeedRequested += SetSpeed;
        _hud.PauseRequested += TogglePause;
        _hud.SelectionClosed += () => Select(null);

        ApplyDevArguments(camera);
    }

    private void ApplyDevArguments(CameraController camera)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture="))
                _capturePath = arg["--capture=".Length..];
            else if (arg.StartsWith("--speed=") && int.TryParse(arg["--speed=".Length..], out int speed)
                && speed is 1 or 4 or 30)
                SetSpeed((GameSpeed)speed);
            else if (arg.StartsWith("--advance-hours="))
            {
                int ticks = (int)(float.Parse(arg["--advance-hours=".Length..], CultureInfo.InvariantCulture) * TimeConstants.TicksPerHour);
                for (int i = 0; i < ticks; i++)
                    _world.Step();
            }
            else if (arg == "--demo-prayer")
            {
                // Une prière factice, pour voir le panneau.
                _world.Colonies[0].Prayers.Ask(DecisionKind.Dam, "demo", "Construire un barrage sur la rivière ?",
                    "Nos champs manquent d'eau et le débit de la rivière est fort : un barrage formerait un lac en amont.", () => { }, _world.Clock);
            }
            else if (arg == "--open-prayers")
                _prayerPanel.Open = true;
            else if (arg.StartsWith("--zoom="))
                camera.Zoom = Vector2.One * float.Parse(arg["--zoom=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg == "--select-first")
                Select(_world.Colonies[0].Members[0]);
            else if (arg == "--focus-quarry" && _world.Colonies[0].Quarry is { } quarry)
            {
                // Centre la caméra sur la carrière et suit un mineur.
                camera.Position = new Vector2(quarry.X + 0.5f, quarry.Y + 0.5f) * TerrainPainter.TileSize;
                Select(_world.Colonies[0].Members.Find(m => m.Sector == WorkSector.Stone));
            }
            else if (arg == "--focus-fields" && _world.Colonies[0].Fields.Count > 0)
            {
                // Centre la caméra sur les champs.
                Field field = _world.Colonies[0].Fields[0];
                camera.Position = new Vector2(field.X + Field.Size / 2f, field.Y + Field.Size / 2f) * TerrainPainter.TileSize;
            }
            else if (arg == "--demo-quarry")
            {
                (int x, int y) = DevTools.DigDemoQuarry(_world.Map);
                camera.Position = new Vector2(x + 0.5f, y + 0.5f) * TerrainPainter.TileSize;
                camera.Zoom = new Vector2(1.5f, 1.5f);
            }
        }
    }

    public override void _Process(double delta)
    {
        _pendingTicks += delta * (int)_speed * TimeConstants.TicksPerSecond;
        int ticks = Math.Min((int)_pendingTicks, MaxTicksPerFrame);
        _pendingTicks -= ticks;
        for (int i = 0; i < ticks; i++)
            _world.Step();
        _colonistsView.Alpha = (float)Math.Clamp(_pendingTicks, 0, 1);

        GameSpeed atmosphereSpeed = _speed == GameSpeed.Pause ? _speedBeforePause : _speed;
        _ambience.Update(_world.Clock, atmosphereSpeed, _speed == GameSpeed.Pause, delta);
        _daylight.Color = _ambience.Tint;
        _colonistsView.AmbientEffectsEnabled = _ambience.DetailedEffects;

        UpdateHud();

        if (_capturePath is not null && --_framesBeforeCapture == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_capturePath);
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                OnLeftClick();
                break;
        }
    }

    /// <summary>Un clic sélectionne le colon le plus proche ; sinon, sur la roche, il mine (outil de test).</summary>
    private void OnLeftClick()
    {
        Vector2 mouse = GetGlobalMousePosition();
        Colonist? nearest = null;
        float bestDistance = SelectRadius * TerrainPainter.TileSize;
        foreach (Colony colony in _world.Colonies)
        foreach (Colonist colonist in colony.Members.Concat(colony.Transients))
        {
            // On vise le corps du colon, un peu au-dessus de ses pieds.
            Vector2 body = _colonistsView.DisplayPosition(colonist) - new Vector2(0, 12);
            float distance = body.DistanceTo(mouse);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = colonist;
            }
        }

        if (nearest is not null)
        {
            Select(nearest);
            return;
        }

        Select(null);
        (int x, int y) = TileUnderMouse();
        if (_world.Map.CanMine(x, y))
            _world.Map.Mine(x, y);
    }

    private void Select(Colonist? colonist)
    {
        _selected = colonist;
        _colonistsView.Selected = colonist;
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                TogglePause();
                break;
            case Key.Key1: SetSpeed(GameSpeed.Observation); break;
            case Key.Key2: SetSpeed(GameSpeed.Rapide); break;
            case Key.Key3: SetSpeed(GameSpeed.TresRapide); break;
            case Key.Escape: Select(null); break;
        }
    }

    private void SetSpeed(GameSpeed speed)
    {
        _speed = speed;
        if (speed != GameSpeed.Pause)
            _speedBeforePause = speed;
    }

    private void TogglePause()
    {
        if (_speed == GameSpeed.Pause)
            _speed = _speedBeforePause;
        else
        {
            _speedBeforePause = _speed;
            _speed = GameSpeed.Pause;
        }
    }

    private (int X, int Y) TileUnderMouse()
    {
        Vector2 world = GetGlobalMousePosition() / TerrainPainter.TileSize;
        return ((int)Mathf.Floor(world.X), (int)Mathf.Floor(world.Y));
    }

    private void UpdateHud()
    {
        GameClock clock = _world.Clock;
        _hud.SetStatus(clock, _speed);

        Colony colony = _world.Colonies[0];
        _hud.ShowColony(colony, clock);

        (int x, int y) = TileUnderMouse();
        LocalMap map = _world.Map;
        _hud.SetTileInfo(map.InBounds(x, y)
            ? $"Case ({x}, {y})  ·  altitude {map.GetElevation(x, y)}  ·  {SurfaceName(map.GetSurface(x, y))}{(map.CanMine(x, y) ? "  ·  minable" : "")}"
            : " ");

        _hud.ShowShares(colony);
        _hud.ShowThoughts(colony);

        // Un colon sélectionné qui a quitté la colonie n'a plus de fiche.
        if (_selected is not null && _selected.Transit == TransitState.None && !_selected.Colony.Members.Contains(_selected))
            Select(null);
        _hud.ShowColonist(_selected);
    }

    private static string SurfaceName(Surface surface) => surface switch
    {
        Surface.Water => "eau",
        Surface.River => "rivière",
        Surface.Grass => "herbe",
        Surface.Dirt => "terre",
        Surface.Sand => "sable",
        Surface.Stone => "roche",
        _ => "minerai de fer",
    };
}
