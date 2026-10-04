using System;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Time;
using GodColony.View;

namespace GodColony;

/// <summary>
/// La vue chiffrée, à la vitesse ×200 : les vues de la carte et des habitants sont libérées (textures comprises),
/// la simulation ne prévient plus personne quand une case change, et un tableau de chiffres et de courbes les remplace.
/// La simulation reçoit alors bien plus de temps à chaque image. En repartant à ×1, ×4 ou ×30, la carte est repeinte
/// telle qu'elle est devenue.
/// </summary>
public partial class Main
{
    /// <summary>
    /// Temps de calcul accordé à la simulation à chaque image en vue chiffrée. Il n'y a presque rien à dessiner :
    /// quand la machine peine à tenir ×200, le tableau se contente d'une trentaine d'images par seconde.
    /// </summary>
    private const double StatsSimulationBudgetMs = 25;

    /// <summary>Durée sur laquelle on mesure la vitesse réellement atteinte.</summary>
    private const double PaceWindowSeconds = 0.5;

    private ColonyHistory _history = new();
    private StatsPanel? _statsPanel;
    private bool _statsShown;
    private long _statsStartDay;
    private int _observedBeforeStats;
    private Vector2 _cameraBeforeStats;

    /// <summary>La dernière vitesse avec la carte : on la retrouve en quittant la vue chiffrée.</summary>
    private GameSpeed _mapSpeed = GameSpeed.Observation;

    private double _paceMultiplier, _paceTicks, _paceSeconds;

    /// <summary>Les premières images de la vue chiffrée (vues libérées, tableau construit) ne comptent pas dans la mesure.</summary>
    private const double PaceWarmupSeconds = 1;
    private double _paceWarmup;

    /// <summary>Change à chaque passage en vue chiffrée ou retour à la carte : un repeint en attente devenu inutile est abandonné.</summary>
    private int _mapRestoreGeneration;

    /// <summary>La vitesse choisie (ou celle qui reprendra après la pause) est-elle celle de la vue chiffrée ?</summary>
    private bool StatsMode => _world is not null && (_speed == GameSpeed.Pause ? _speedBeforePause : _speed) == GameSpeed.Fulgurante;

    /// <summary>Une nouvelle partie : rien n'est encore affiché en vue chiffrée et les courbes repartent de zéro.</summary>
    private void ResetStatsMode()
    {
        _statsPanel = null;
        _statsShown = false;
        _history = new ColonyHistory();
    }

    /// <summary>Accorde l'affichage avec la vitesse : la vue chiffrée à ×200 (même en pause), la carte sinon.</summary>
    private void SyncStatsMode()
    {
        if (_speed is not (GameSpeed.Pause or GameSpeed.Fulgurante)) _mapSpeed = _speed;
        if (StatsMode == _statsShown) return;
        if (StatsMode) EnterStatsMode();
        else LeaveStatsMode();
    }

    private void EnterStatsMode()
    {
        _statsShown = true;
        _mapRestoreGeneration++;
        Select(null);
        _observedBeforeStats = _observed;
        _cameraBeforeStats = _camera.Position;
        _statsStartDay = _world.Clock.TotalDays;
        _paceMultiplier = (int)GameSpeed.Fulgurante; _paceTicks = 0; _paceSeconds = 0; _paceWarmup = PaceWarmupSeconds;
        if (_mapView is not null) { RemoveChild(_mapView); _mapView.QueueFree(); _mapView = null; }
        if (_colonistsView is not null) { RemoveChild(_colonistsView); _colonistsView.QueueFree(); _colonistsView = null; }
        _ambience.Visible = false;
        _camera.ControlsEnabled = false;
        _hud.SetMapTools(false);
        if (_statsPanel is null)
        {
            _statsPanel = new StatsPanel();
            _statsPanel.Init(_world);
            AddChild(_statsPanel);
            _statsPanel.ObserveRequested += ObserveColony;
        }
        _statsPanel.SetBusy(null);
        _statsPanel.Show();
        _hudCooldown = 0;
    }

    private void LeaveStatsMode()
    {
        _statsShown = false;
        _hud.SetMapTools(true);
        _hudCooldown = 0;
        // La carte est repeinte telle que les années l'ont changée : cela prend un moment (≈ 1,5 s pour 200 × 200 cases),
        // qu'on annonce d'abord, comme le menu pour un chargement. Un retour en vue chiffrée ou une autre partie l'annule.
        _statsPanel?.SetBusy("La carte se repeint telle que les années l'ont changée…");
        int generation = ++_mapRestoreGeneration;
        WorldState world = _world;
        Callable.From((Action)(async () =>
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (generation != _mapRestoreGeneration || world != _world) return;
            _statsPanel?.Hide();
            if (_foundingMap is not null) return;
            RestoreObservedMap();
            if (_observed == _observedBeforeStats) _camera.Position = _cameraBeforeStats;
            ApplySettings();
            _hudCooldown = 0;
        })).CallDeferred();
    }

    /// <summary>Quitte la vue chiffrée pour la dernière vitesse avec carte, en restant en pause si le jeu l'était.</summary>
    private void ReturnToMap()
    {
        if (!_statsShown) return;
        if (_speed == GameSpeed.Pause) _speedBeforePause = _mapSpeed;
        else _speed = _mapSpeed;
        SyncStatsMode();
    }

    /// <summary>
    /// Compte les ticks joués pour afficher la vitesse réelle (elle peut passer sous ×200 sur une machine qui peine).
    /// La mesure est lissée : un à-coup isolé (une sauvegarde, la mise en place de la vue) ne fait pas croire que la machine décroche.
    /// </summary>
    private void MeasurePace(int ticks, double delta, bool stopped)
    {
        if (!_statsShown) return;
        if (stopped) { _paceTicks = 0; _paceSeconds = 0; return; }
        if (_paceWarmup > 0) { _paceWarmup -= delta; return; }
        _paceTicks += ticks;
        _paceSeconds += delta;
        if (_paceSeconds < PaceWindowSeconds) return;
        double measured = _paceTicks / _paceSeconds / TimeConstants.TicksPerSecond;
        _paceMultiplier = (_paceMultiplier + measured) / 2;
        _paceTicks = 0; _paceSeconds = 0;
    }

    private void ShowStats()
    {
        int population = _world.Colonies.Sum(c => c.Members.Count);
        _hud.ShowUnsettled("Vue chiffrée",
            $"{_world.Colonies.Count} colonie{(_world.Colonies.Count > 1 ? "s" : "")} · {population:N0} habitants · la carte est en veille pendant que le temps file");
        _hud.SetTileInfo("Vue chiffrée : ni terrain ni habitants à dessiner, la simulation a presque tout le processeur.   1, 2 ou 3 : retour à la carte.");
        bool stopped = _speed == GameSpeed.Pause || _menu.IsOpen || _foundingPanel.IsOpen;
        _statsPanel!.Refresh(_world, _history, _observed,
            new StatsPace(_paceMultiplier, stopped, _world.Clock.TotalDays, _statsStartDay));
    }
}
