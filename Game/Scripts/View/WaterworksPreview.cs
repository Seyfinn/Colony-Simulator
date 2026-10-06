using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Scènes isolées utilisant les vrais rendus ; les données de démonstration ne sont jamais sauvegardées.</summary>
public partial class WaterworksPreview : Node2D
{
    private int _frames;
    private string _mode = "bridges";
    private string? _capture;
    private ColonistsView? _view;
    private OptionButton? _picker;
    private Hud? _hud;
    private WorldState _world = null!;
    private readonly List<(Building Mill, MillSide? Side, float Flow)> _mills = [];
    private string _before = "";
    private bool _scroll;
    private static readonly BindingFlags Acces = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Set(object obj, string name, object value) => obj.GetType().GetProperty(name, Acces)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Acces)!.Invoke(obj, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public override void _Ready()
    {
        try
        {
            GetWindow().Size = new Vector2I(1600, 900);
            TextureFilter = TextureFilterEnum.Nearest;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--preview=")) _mode = arg[10..];
                if (arg.StartsWith("--capture=")) _capture = arg[10..];
                if (arg == "--fallback") AssetLibrary.NativeFallbackForValidation = true;
                if (arg == "--scroll") _scroll = true;
                if (arg == "--export-waterworks")
                {
                    NatureArt.ExportBridges(); RemainingArt.ExportMillWheels(); BuildingSprites.ExportMillBodies();
                    AssetLibrary.Reload();
                }
            }
            Require(WaterEffects.MillPhase(1.25, 20, 0) == 1.25, "La roue tourne sans débit.");
            Require(WaterEffects.MillPhase(1.25, 0, 1) == 1.25, "Saut de pose ou pause ignorée.");
            Require(WaterEffects.MillPhase(0, .1, .5f) < WaterEffects.MillPhase(0, .1, 1), "Vitesse indépendante du débit.");
            Require(Math.Abs(WaterEffects.MillPhase(WaterEffects.MillPhase(0, .1, .25f), .1, .75f) - .6) < .00001, "Débit discontinu.");
            _world = new WorldState(12345, 64, 64, startingColonists: 8, colonyCount: 3, migration: false, lifecycle: false, trade: false);
            if (_mode == "places") { Places(); return; }
            Colony colony = _world.Colonies[0];
            var map = (LocalMap)Activator.CreateInstance(typeof(LocalMap), Acces, null, [64, 64, 12345], null)!;
            for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++)
                Call(map, "SetGenerated", x, y, 5, SoilType.Grass, FloraType.None, 0f, .5f);
            Set(colony, "Map", map);
            colony.Buildings.Clear(); colony.PrimarySettlement.BridgeSites.Clear();
            Set(colony.PrimarySettlement, "CampX", 55); Set(colony.PrimarySettlement, "CampY", 55);
            typeof(Settlement).GetField("LayoutState", Acces)!.SetValue(colony.PrimarySettlement, null);
            var wildlife = _world.Regions[colony.PrimarySettlement.RegionTileIndex].Wildlife;
            wildlife.Hives.Clear(); wildlife.Mushrooms.Clear(); wildlife.Herbs.Clear();
            // Les habitants sont hors du cadrage : leurs décisions ne sont pas exécutées.
            foreach (Colonist colonist in colony.Members)
                foreach (string coordinate in new[] { "X", "Y", "PrevX", "PrevY" }) Set(colonist, coordinate, 55f);
            if (_mode == "bridges") Bridges(colony, map); else Mills(colony, map);
            var terrain = new MapView(); terrain.Init(map); AddChild(terrain);
            _view = new ColonistsView { Alpha = 1, AmbientEffectsEnabled = false };
            _view.Init(_world, colony); AddChild(_view);
            AddChild(new Camera2D { Position = new Vector2(21, 14) * 32, Zoom = Vector2.One * 1.5f });
            _before = Snapshot();
            var heading = new CanvasLayer(); AddChild(heading);
            var title = new Label { Text = (_mode == "bridges" ? "Ponts · tabliers, culées et chantiers" : "Moulins · côtés de l'eau motrice et accès") + (AssetLibrary.NativeFallbackForValidation ? " · secours natif" : " · PNG"), Position = new Vector2(24, 16) };
            title.AddThemeFontSizeOverride("font_size", 24); heading.AddChild(title);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Label(string text, int x, int y)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y) * 32, ZIndex = 2 };
        label.AddThemeColorOverride("font_color", ArtDirection.Charcoal); label.AddThemeFontSizeOverride("font_size", 12); AddChild(label);
    }

    private void Bridges(Colony colony, LocalMap map)
    {
        foreach (var (x, y, length, horizontal, built, title) in new[]
        {
            (8, 8, 3, true, 3, "Est–ouest · 3 cases"), (18, 8, 3, false, 3, "Nord–sud · 3 cases"),
            (29, 8, 1, true, 1, "Une case · est–ouest"), (8, 17, 1, false, 1, "Une case · nord–sud"),
            (16, 17, 6, true, 6, "Long · 6 cases"), (28, 16, 6, true, 2, "Chantier · 2 / 6 cases"),
            (24, 8, 3, false, 1, "Chantier · 1 / 3")
        })
        {
            var site = new BridgeSite(); Set(site, "Horizontal", horizontal);
            int step = horizontal ? 1 : map.Width, first = y * map.Width + x;
            for (int i = 0; i < length; i++)
            {
                int cell = first + i * step; site.Cells.Add(cell);
                int cx = cell % map.Width, cy = cell / map.Width;
                // Eau perpendiculaire au tablier, limitée aux seules cases du franchissement.
                for (int j = -2; j <= 2; j++)
                {
                    int wx = cx + (horizontal ? 0 : j), wy = cy + (horizontal ? j : 0);
                    Call(map, "SetRiver", wx, wy, wx + (!horizontal && j < 2 ? 1 : 0), wy + (horizontal && j < 2 ? 1 : 0), length);
                }
                if (i < built) { site.BuiltCells.Add(cell); Call(map.Roads, "SetSurface", cell, RoadSurface.Bridge); }
            }
            site.Landings.AddRange([first - step, first + length * step]);
            foreach (int bank in site.Landings) { Call(map.Roads, "SetSurface", bank, RoadSurface.Trail); Call(map.Roads, "SetSurface", bank + (bank < first ? -step : step), RoadSurface.Trail); }
            colony.PrimarySettlement.BridgeSites.Add(site); Label(title, x - 1, y + (horizontal ? 1 : length + 1));
            Require(site.IsComplete == (built == length), "Chantier incohérent.");
        }
    }

    private void Mills(Colony colony, LocalMap map)
    {
        foreach (var (x, y, side, kind, title) in new[]
        {
            (7, 8, MillSide.East, "river", "Est · débit 1"), (15, 8, MillSide.West, "river", "Ouest · débit 0,35"),
            (23, 8, MillSide.North, "river", "Nord · roue derrière"), (31, 8, MillSide.South, "river", "Sud · accès dégagé"),
            (8, 17, MillSide.East, "canal", "Canal en eau"), (19, 17, MillSide.West, "reservoir", "Retenue de barrage"),
            (30, 17, MillSide.East, "dry", "Sans débit · coursier à sec")
        })
        {
            var mill = new Building(BuildingType.Mill, x, y); Set(mill, "Progress", 1f); colony.Buildings.Add(mill);
            int wx = side == MillSide.West ? x - 1 : side == MillSide.East ? x + mill.Width : x + 2;
            int wy = side == MillSide.North ? y - 1 : side == MillSide.South ? y + mill.Height : y + 1;
            for (int i = -2; i <= 2; i++)
            {
                int cx = wx + (side is MillSide.North or MillSide.South ? i : 0), cy = wy + (side is MillSide.East or MillSide.West ? i : 0);
                if (kind is "canal" or "dry") { map.DigCanal(cx, cy); if (kind == "canal") map.FillCanal(cx, cy); }
                else
                {
                    bool lateral = side is MillSide.East or MillSide.West;
                    Call(map, "SetRiver", cx, cy, cx + (lateral ? 0 : 1), cy + (lateral ? 1 : 0), 1);
                    if (side == MillSide.West && kind == "river") map.ReduceFlow(cx, cy, .35f);
                }
            }
            if (kind == "reservoir")
            {
                map.Flood(Enumerable.Range(wy - 2, 5).Select(cy => (wx, cy)), 6);
                var dam = new Building(BuildingType.Dam, wx - 4, wy + 3); Set(dam, "Progress", 1f); colony.Buildings.Add(dam);
            }
            var water = Hydrology.MillWater(map, mill);
            Require(kind == "dry" ? water == (0, null) : water.Side == side && water.Flow > 0, "Orientation du moulin incohérente : " + title);
            _mills.Add((mill, water.Side, water.Flow)); Label(title, x - 1, y + mill.Height + 1);
        }
    }

    private void Places()
    {
        for (int empire = 0; empire < _world.Colonies.Count; empire++)
        {
            Colony colony = _world.Colonies[empire];
            for (int i = 0; i < 4; i++)
            {
                var place = (Settlement)Activator.CreateInstance(typeof(Settlement), Acces, null, [colony, 20, 20, new List<(int, int)>()], null)!;
                Set(place, "Id", 100 + empire * 10 + i); Set(place, "Map", colony.Map); Set(place, "Kind", SettlementKind.Hamlet);
                if (i == 2) Set(place, "Status", SettlementStatus.Evacuating);
                if (i == 3) Set(place, "Status", SettlementStatus.Closed);
                colony.Settlements.Add(place);
            }
        }
        _hud = new Hud(); AddChild(_hud); _hud.ShowColony(_world.Colonies[0], _world.Clock, _world.Colonies);
        _picker = (OptionButton)typeof(Hud).GetField("_placePicker", Acces)!.GetValue(_hud)!;
        Require(_picker.ItemCount == 15 && Enumerable.Range(0, 15).Count(_picker.IsItemDisabled) == 3, "Liste ou colonies fermées incorrectes.");
        Require(Enumerable.Range(0, 15).Count(i => _picker.GetItemText(i).StartsWith("Évacuation")) == 3, "Mention d'évacuation absente.");
    }

    private string Snapshot() => string.Join('|', _world.Colonies[0].PrimarySettlement.BridgeSites.Select(s => $"{s.Horizontal}:{string.Join(',', s.Cells)}:{string.Join(',', s.BuiltCells)}:{string.Join(',', s.Landings)}"))
        + string.Join('|', _mills.Select(m => $"{m.Mill.X},{m.Mill.Y}:{Hydrology.MillWater(_world.Colonies[0].Map, m.Mill)}"));

    public override void _Process(double delta)
    {
        try
        {
            _frames++;
            if (_view is not null) _view.WaterAnimationTime += delta;
            if (_picker is not null && _frames == 10)
            {
                _picker.ShowPopup();
            }
            if (_picker is not null && _frames == 30)
            {
                // Un rafraîchissement courant ne referme pas la liste ouverte.
                _hud!.ShowColony(_world.Colonies[0], _world.Clock, _world.Colonies);
                if (_scroll) Call(_hud, "ShowPlaces", _world.Colonies[0], _world.Colonies[2].Settlements[3], _world.Colonies);
                Require(_picker.GetPopup().Visible, "Le rafraîchissement ferme le menu.");
                Require(_picker.Size.X <= 251 && _picker.GetPopup().Size.Y <= 340, "Menu hors limites.");
            }
            if (_picker is not null && _scroll && _frames == 45) _picker.GetPopup().ScrollToItem(_picker.ItemCount - 1);
            if (_frames < 90) return;
            if (_view is not null) Require(_before == Snapshot(), "Le rendu a modifié les ouvrages.");
            if (_capture is not null) Require(GetViewport().GetTexture().GetImage().SavePng(_capture) == Error.Ok, "Capture impossible.");
            GD.Print($"WATERWORKS_VISUALS_OK : {_mode}, secours={AssetLibrary.NativeFallbackForValidation}, défilement={_scroll}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
