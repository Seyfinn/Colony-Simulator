using System;
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
    private Hud _hud = null!;

    private GameSpeed _speed = GameSpeed.Observation;
    private GameSpeed _speedBeforePause = GameSpeed.Observation;
    private double _pendingTicks;
    private Colonist? _selected;

    // Outils de développement, passés en ligne de commande après "--" :
    //   --capture=chemin.png   enregistre une capture puis quitte le jeu
    //   --advance-hours=N      fait avancer la simulation de N heures au démarrage
    //   --demo-quarry          creuse une carrière de démonstration
    //   --select-first         sélectionne le premier colon
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
            Zoom = new Vector2(2.5f, 2.5f),
        };
        AddChild(camera);
        camera.MakeCurrent();

        _hud = new Hud();
        AddChild(_hud);

        ApplyDevArguments(camera);
    }

    private void ApplyDevArguments(CameraController camera)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture="))
                _capturePath = arg["--capture=".Length..];
            else if (arg.StartsWith("--advance-hours="))
            {
                int ticks = (int)(float.Parse(arg["--advance-hours=".Length..]) * TimeConstants.TicksPerHour);
                for (int i = 0; i < ticks; i++)
                    _world.Step();
            }
            else if (arg == "--select-first")
                Select(_world.Colonies[0].Members[0]);
            else if (arg == "--demo-quarry")
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
        _colonistsView.Alpha = (float)Math.Clamp(_pendingTicks, 0, 1);

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
        foreach (Colonist colonist in colony.Members)
        {
            // On vise le corps du colon, un peu au-dessus de ses pieds.
            Vector2 body = _colonistsView.DisplayPosition(colonist) - new Vector2(0, 6);
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
            case Key.Escape: Select(null); break;
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

        Colony colony = _world.Colonies[0];
        _hud.SetColony($"{colony.Name}  ·  {colony.Members.Count} colons  ·  nourriture {colony.Stock.Get(ResourceType.Food)}  ·  humeur {colony.AverageMood * 100:0} %");

        (int x, int y) = TileUnderMouse();
        LocalMap map = _world.Map;
        _hud.SetTileInfo(map.InBounds(x, y)
            ? $"Case ({x}, {y})  ·  altitude {map.GetElevation(x, y)}  ·  {SurfaceName(map.GetSurface(x, y))}{(map.CanMine(x, y) ? "  ·  minable" : "")}"
            : " ");

        _hud.ShowColonist(_selected);
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
