using System;
using System.Diagnostics;
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

    /// <summary>Nombre de peuples au départ : humains, nains, elfes et orques, chacun sur sa carte.</summary>
    private const int ColonyCount = 4;

    /// <summary>
    /// Temps de calcul accordé à la simulation à chaque image. Si la machine ne suit pas (vitesse ×30 sur de grosses
    /// colonies, par exemple), le temps du jeu ralentit un peu au lieu de figer l'image pour rattraper son retard.
    /// </summary>
    private const double SimulationBudgetMs = 10;

    /// <summary>
    /// Retard maximal gardé en réserve, en secondes de jeu : au-delà (après un gel de la fenêtre, par exemple),
    /// on l'oublie plutôt que de le rattraper par à-coups, image après image.
    /// </summary>
    private const double MaxBacklogSeconds = 0.25;

    /// <summary>Distance maximale (en cases) entre le clic et un colon pour le sélectionner.</summary>
    private const float SelectRadius = 0.8f;

    private WorldState _world = null!;
    private MapView _mapView = null!;
    private ColonistsView _colonistsView = null!;
    private CameraController _camera = null!;
    private WorldPanel _worldPanel = null!;
    private int _observed;

    /// <summary>La colonie qu'on observe : sa carte est affichée, son HUD est montré.</summary>
    private Colony Observed => _world.Colonies[_observed];
    private CanvasModulate _daylight = null!;
    private PrayerPanel _prayerPanel = null!;
    private DayNightAmbience _ambience = null!;
    private Hud _hud = null!;

    private GameSpeed _speed = GameSpeed.Observation;
    private GameSpeed _speedBeforePause = GameSpeed.Observation;
    private double _pendingTicks;
    private Colonist? _selected;

    /// <summary>L'interface se met à jour dix fois par seconde : assez pour l'œil, bien moins de travail qu'à chaque image.</summary>
    private const double HudRefreshSeconds = 0.1;
    private double _hudCooldown;

    // Outils de développement, passés en ligne de commande après "--" :
    //   --capture=chemin.png   enregistre une capture puis quitte le jeu
    //   --advance-hours=N      fait avancer la simulation de N heures au démarrage
    //   --speed=1|4|30         choisit la vitesse pour vérifier les ambiances
    //   --demo-quarry          creuse une carrière de démonstration
    //   --colony=N             observe la colonie numéro N (0 = humains, 1 = nains) ; --open-world ouvre le panneau du monde
    //   --demo-prayer          soumet une prière factice ; --open-prayers ouvre le détail
    //   --auto-dam / --focus-dam   accorde d'office les barrages / centre la caméra sur le barrage
    //   --zoom=N               règle le zoom de la caméra (0.2 montre presque toute la carte)
    //   --select-first         sélectionne le premier colon
    //   --focus-fields         centre la caméra sur le premier champ
    //   --perf=N               mesure N images (durée, part de la simulation), affiche le résumé puis quitte
    private string? _capturePath;
    private int _framesBeforeCapture = 20;
    private PerfProbe? _perf;

    public override void _Ready()
    {
        _world = new WorldState(Seed, colonyCount: ColonyCount);
        Colony colony = Observed;

        _mapView = new MapView();
        AddChild(_mapView);
        _mapView.Init(colony.Map);

        _colonistsView = new ColonistsView();
        AddChild(_colonistsView);
        _colonistsView.Init(_world, colony);

        _daylight = new CanvasModulate();
        AddChild(_daylight);

        var camera = new CameraController
        {
            Position = new Vector2(colony.CampX + 0.5f, colony.CampY + 0.5f) * TerrainPainter.TileSize,
            Zoom = new Vector2(1.25f, 1.25f),
        };
        AddChild(camera);
        camera.MakeCurrent();
        _camera = camera;

        // Une couche visuelle sous le HUD : aucun effet ne voile les contrôles.
        var atmosphereLayer = new CanvasLayer { Layer = 1 };
        AddChild(atmosphereLayer);
        _ambience = new DayNightAmbience();
        atmosphereLayer.AddChild(_ambience);
        _ambience.Init(colony.Map);

        _prayerPanel = new PrayerPanel();
        AddChild(_prayerPanel);
        _prayerPanel.Init(_world);

        _worldPanel = new WorldPanel();
        AddChild(_worldPanel);
        _worldPanel.Init(_world);
        _worldPanel.ColonyRequested += ObserveColony;

        _hud = new Hud();
        AddChild(_hud);
        _hud.SpeedRequested += SetSpeed;
        _hud.PauseRequested += TogglePause;
        _hud.SelectionClosed += () => Select(null);

        ApplyDevArguments(camera);
    }

    /// <summary>Passe à l'observation d'une autre colonie : on reconstruit sa carte et sa vue, et la caméra la rejoint.</summary>
    private void ObserveColony(int index)
    {
        if (index == _observed || index < 0 || index >= _world.Colonies.Count)
            return;
        _observed = index;
        _worldPanel.Observed = index;
        Select(null);
        Colony colony = Observed;

        _mapView.QueueFree();
        _colonistsView.QueueFree();
        _mapView = new MapView();
        AddChild(_mapView);
        MoveChild(_mapView, 0);
        _mapView.Init(colony.Map);
        _colonistsView = new ColonistsView();
        AddChild(_colonistsView);
        MoveChild(_colonistsView, 1);
        _colonistsView.Init(_world, colony);

        _ambience.Init(colony.Map);
        _camera.Position = new Vector2(colony.CampX + 0.5f, colony.CampY + 0.5f) * TerrainPainter.TileSize;
        _hudCooldown = 0;
    }

    private void ApplyDevArguments(CameraController camera)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture="))
                _capturePath = arg["--capture=".Length..];
            else if (arg.StartsWith("--perf=") && int.TryParse(arg["--perf=".Length..], out int frames) && frames > 0)
                _perf = new PerfProbe(frames);
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
                Observed.Prayers.Ask(DecisionKind.Dam, "demo", "Construire un barrage sur la rivière ?",
                    "Nos champs manquent d'eau et le débit de la rivière est fort : un barrage formerait un lac en amont.", () => { }, _world.Clock);
            }
            else if (arg == "--demo-workshops")
            {
                foreach (BuildingType type in new[] { BuildingType.Hut, BuildingType.Kiln, BuildingType.Bloomery, BuildingType.Forge, BuildingType.Mill, BuildingType.Oven })
                    Urbanism.BuildInstantly(Observed.Map, Observed, type);
            }
            else if (arg == "--demo-dam")
                Hydrology.BuildInstantly(Observed.Map, Observed);
            else if (arg.StartsWith("--colony=") && int.TryParse(arg["--colony=".Length..], out int colonyIndex))
                ObserveColony(colonyIndex);
            else if (arg == "--open-world")
                _worldPanel.Open = true;
            else if (arg == "--open-map")
                _worldPanel.MapOpen = true;
            else if (arg == "--auto-dam")
                Observed.Prayers.AutoApprove.Add(DecisionKind.Dam);
            else if (arg == "--focus-dam" && Observed.Buildings.Find(b => b.IsDam) is { } dam)
                camera.Position = new Vector2(dam.X + 0.5f, dam.Y + 0.5f) * TerrainPainter.TileSize;
            else if (arg == "--open-prayers")
                _prayerPanel.Open = true;
            else if (arg.StartsWith("--zoom="))
                camera.Zoom = Vector2.One * float.Parse(arg["--zoom=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg == "--select-first")
                Select(Observed.Members[0]);
            else if (arg == "--focus-quarry" && Observed.Quarry is { } quarry)
            {
                // Centre la caméra sur la carrière et suit un mineur.
                camera.Position = new Vector2(quarry.X + 0.5f, quarry.Y + 0.5f) * TerrainPainter.TileSize;
                Select(Observed.Members.Find(m => m.Sector == WorkSector.Stone));
            }
            else if (arg == "--focus-fields" && Observed.Fields.Count > 0)
            {
                // Centre la caméra sur les champs.
                Field field = Observed.Fields[0];
                camera.Position = new Vector2(field.X + Field.Size / 2f, field.Y + Field.Size / 2f) * TerrainPainter.TileSize;
            }
            else if (arg == "--demo-quarry")
            {
                (int x, int y) = DevTools.DigDemoQuarry(Observed.Map);
                camera.Position = new Vector2(x + 0.5f, y + 0.5f) * TerrainPainter.TileSize;
                camera.Zoom = new Vector2(1.5f, 1.5f);
            }
        }
    }

    public override void _Process(double delta)
    {
        double ticksPerSecond = (int)_speed * TimeConstants.TicksPerSecond;
        _pendingTicks = Math.Min(_pendingTicks + delta * ticksPerSecond, ticksPerSecond * MaxBacklogSeconds + 1);
        long simulationStart = Stopwatch.GetTimestamp();
        long budget = (long)(SimulationBudgetMs / 1000 * Stopwatch.Frequency);
        while (_pendingTicks >= 1)
        {
            _world.Step();
            _pendingTicks--;
            if (Stopwatch.GetTimestamp() - simulationStart > budget)
                break;
        }
        double simulationMs = Stopwatch.GetElapsedTime(simulationStart).TotalMilliseconds;
        _colonistsView.Alpha = (float)Math.Clamp(_pendingTicks, 0, 1);

        GameSpeed atmosphereSpeed = _speed == GameSpeed.Pause ? _speedBeforePause : _speed;
        _ambience.Update(_world.Clock, atmosphereSpeed, _speed == GameSpeed.Pause, delta);
        _daylight.Color = _ambience.Tint;
        _colonistsView.AmbientEffectsEnabled = _ambience.DetailedEffects;
        _colonistsView.WaterAnimationTime = _ambience.AnimationTime;

        _hudCooldown -= delta;
        if (_hudCooldown <= 0)
        {
            _hudCooldown = HudRefreshSeconds;
            UpdateHud();
        }

        if (_capturePath is not null && --_framesBeforeCapture == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_capturePath);
            GetTree().Quit();
        }
        if (_perf?.Frame(simulationMs) == true)
        {
            GD.Print(_perf.Summary());
            _perf = null;
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
        foreach (Colony colony in new[] { Observed })
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
        if (Observed.Map.CanMine(x, y))
            Observed.Map.Mine(x, y);
    }

    private void Select(Colonist? colonist)
    {
        _selected = colonist;
        _colonistsView.Selected = colonist;
        _hudCooldown = 0;
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
        _hudCooldown = 0;
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
        _hudCooldown = 0;
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

        Colony colony = Observed;
        _hud.ShowColony(colony, clock);

        (int x, int y) = TileUnderMouse();
        LocalMap map = Observed.Map;
        _hud.SetTileInfo(map.InBounds(x, y)
            ? $"Case ({x}, {y})  ·  altitude {map.GetElevation(x, y)}  ·  {SurfaceName(map.GetSurface(x, y))}{(map.CanMine(x, y) ? "  ·  minable" : "")}"
            : " ");

        _hud.ShowShares(colony);
        _hud.ShowThoughts(colony);

        // Un colon sélectionné qui a quitté la colonie n'a plus de fiche.
        if (_selected is not null && _selected.Transit == TransitState.None && !_selected.Colony.Members.Contains(_selected))
            Select(null);
        _hud.ShowColonist(_selected, _selected is null ? WoodlandBiome.TemperatePlain
            : BiomeVisuals.At(Observed.Map, _selected.TileX, _selected.TileY));
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
