using System;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Validation des ouvrages avec les vraies vues de terrain et de colonie.</summary>
public partial class LocalArtPreview : Node2D
{
    private ColonistsView _people = null!;
    private string? _capture;
    private int _frames = 40;
    private double _time;
    public override void _Ready()
    {
        var world = new WorldState(42, startingColonists: 10, colonyCount: 4, migration: false, lifecycle: false, trade: false);
        var colony = world.Colonies[0];
        Building? mill = null;
        foreach (var candidate in world.Colonies)
        {
            mill = Urbanism.BuildInstantly(candidate.Map, candidate, BuildingType.Mill);
            if (mill is not null) { colony = candidate; break; }
        }
        if (mill is null) { GD.PushError("Aucun site de moulin dans la fixture."); GetTree().Quit(1); SetProcess(false); return; }
        bool dam = false;
        for (int dy = -3; dy <= 3 && !dam; dy++)
        for (int dx = -3; dx <= 3 && !dam; dx++)
        {
            int x = mill.X + dx, y = mill.Y + dy;
            if (!colony.Map.InBounds(x, y) || !colony.Map.IsRiver(x, y)) continue;
            Urbanism.PlanBuilding(colony.Map, colony, BuildingType.Dam, x, y);
            dam = true;
        }
        if (!dam) throw new InvalidOperationException("Aucune rivière au moulin.");
        var map = new MapView();
        map.Init(colony.Map);
        AddChild(map);
        _people = new ColonistsView { Alpha = 1, AmbientEffectsEnabled = false };
        _people.Init(world, colony);
        AddChild(_people);
        AddChild(new Camera2D { Position = new Vector2(mill.X + 1, mill.Y) * TerrainPainter.TileSize, Zoom = new Vector2(4, 4) });
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--capture=")) _capture = arg["--capture=".Length..];
    }
    public override void _Process(double delta)
    {
        _time += delta;
        _people.WaterAnimationTime = _time;
        if (_capture is null || --_frames != 0) return;
        GetViewport().GetTexture().GetImage().SavePng(_capture);
        GetTree().Quit();
    }
}
