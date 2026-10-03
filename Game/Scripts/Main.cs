using System;
using Godot;
using GodColony.Simulation;
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

    private WorldState _world = null!;
    private CanvasModulate _daylight = null!;
    private Hud _hud = null!;

    private GameSpeed _speed = GameSpeed.Observation;
    private GameSpeed _speedBeforePause = GameSpeed.Observation;
    private double _pendingTicks;

    // Outil de développement : "-- --capture=chemin.png" enregistre une capture puis quitte le jeu.
    private string? _capturePath;
    private int _framesBeforeCapture = 20;

    public override void _Ready()
    {
        _world = new WorldState(Seed);

        var mapView = new MapView();
        AddChild(mapView);
        mapView.Init(_world.Map);

        _daylight = new CanvasModulate();
        AddChild(_daylight);

        var camera = new CameraController
        {
            Position = new Vector2(_world.Map.Width, _world.Map.Height) * TerrainPainter.TileSize / 2f,
            Zoom = new Vector2(1.5f, 1.5f),
        };
        AddChild(camera);
        camera.MakeCurrent();

        _hud = new Hud();
        AddChild(_hud);

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture="))
                _capturePath = arg["--capture=".Length..];
            if (arg == "--demo-quarry")
            {
                (int x, int y) = DevTools.DigDemoQuarry(_world.Map);
                camera.Position = new Vector2(x + 0.5f, y + 0.5f) * TerrainPainter.TileSize;
                camera.Zoom = new Vector2(3f, 3f);
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

        // Nuit bleutée, jour normal.
        float light = _world.Clock.Daylight;
        _daylight.Color = new Color(0.32f, 0.36f, 0.55f).Lerp(Colors.White, light);

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
                (int x, int y) = TileUnderMouse();
                if (_world.Map.CanMine(x, y))
                    _world.Map.Mine(x, y);
                break;
        }
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                if (_speed == GameSpeed.Pause)
                    _speed = _speedBeforePause;
                else
                {
                    _speedBeforePause = _speed;
                    _speed = GameSpeed.Pause;
                }
                break;
            case Key.Key1: _speed = GameSpeed.Observation; break;
            case Key.Key2: _speed = GameSpeed.Rapide; break;
            case Key.Key3: _speed = GameSpeed.TresRapide; break;
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
        string speed = _speed switch
        {
            GameSpeed.Pause => "en pause",
            GameSpeed.Observation => "observation ×1",
            GameSpeed.Rapide => "rapide ×4",
            _ => "très rapide ×30",
        };
        _hud.SetStatus($"An {clock.Year}  ·  {SeasonName(clock.Season)}, jour {clock.DayOfSeason}/5  ·  {clock.Hour:00}:{clock.Minute:00}  ·  {speed}");

        (int x, int y) = TileUnderMouse();
        LocalMap map = _world.Map;
        _hud.SetTileInfo(map.InBounds(x, y)
            ? $"Case ({x}, {y})  ·  altitude {map.GetElevation(x, y)}  ·  {SurfaceName(map.GetSurface(x, y))}{(map.CanMine(x, y) ? "  ·  minable" : "")}"
            : " ");
    }

    private static string SeasonName(Season season) => season switch
    {
        Season.Printemps => "Printemps",
        Season.Ete => "Été",
        Season.Automne => "Automne",
        _ => "Hiver",
    };

    private static string SurfaceName(Surface surface) => surface switch
    {
        Surface.Water => "eau",
        Surface.Grass => "herbe",
        Surface.Dirt => "terre",
        Surface.Sand => "sable",
        Surface.Stone => "roche",
        _ => "minerai de fer",
    };
}
