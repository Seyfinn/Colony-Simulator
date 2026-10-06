using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Nature;

namespace GodColony.View;

/// <summary>Contrôle reproductible des hardes, des enclos, des cueillettes, des ponts et des couleurs des royaumes.</summary>
public partial class NaturePreview : Node2D
{
    private string? _capture;
    private int _frames = 90;
    private Func<Vector2>? _hover;
    private ColonistsView? _village;
    private WorldMapView? _worldMap;
    private bool _hudHover;
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(1600, 900);
        TextureFilter = TextureFilterEnum.Nearest;
        bool worldMap = false;
        bool roads = false;
        string hover = "";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture=")) _capture = arg[10..];
            if (arg == "--fallback") AssetLibrary.NativeFallbackForValidation = true;
            if (arg == "--export") NatureArt.Export();
            if (arg == "--world") worldMap = true;
            if (arg == "--roads") roads = true;
            if (arg.StartsWith("--hover=")) hover = arg[8..];
        }
        var world = new WorldState(12345, 128, 128, startingColonists: 8, colonyCount: 4, migration: false, lifecycle: false, trade: false);
        if (worldMap)
        {
            for (int pair = 0; pair < 2; pair++)
            {
                Colony a = world.Colonies[pair * 2], b = world.Colonies[pair * 2 + 1];
                var realm = new Realm();
                Set(realm, "Id", pair + 1); Set(realm, "Name", "Royaume de " + a.Name); Set(realm, "ColorIndex", pair);
                Set(realm, "CapitalColonyId", a.Id); realm.MemberColonyIds.AddRange([a.Id, b.Id]);
                Set(a, "RealmId", pair + 1); Set(b, "RealmId", pair + 1);
                world.Realms.Add(realm); Leadership.Elect(world, a); Leadership.Elect(world, b); Realms.ElectKing(world, realm);
            }
            if (roads)
            {
                Colony from = world.Colonies[0], to = world.Colonies[1];
                WorldRoute route = world.WorldMap.Route(from, to)!;
                from.VisitedRegions.AddRange(route.Tiles.Where(t => !from.VisitedRegions.Contains(t)));
                MethodInfo improve = world.WorldMap.Roads.GetType().GetMethod("Improve", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (int i = 1; i < route.Tiles.Count; i++)
                {
                    improve.Invoke(world.WorldMap.Roads, [route.Tiles[i - 1], route.Tiles[i], 2]);
                    if (i % 2 == 0) improve.Invoke(world.WorldMap.Roads, [route.Tiles[i - 1], route.Tiles[i], 2]);
                }
                foreach (TerritorialPurpose purpose in Enum.GetValues<TerritorialPurpose>().Where(p => p != TerritorialPurpose.Commerce))
                {
                    long now = world.Clock.Ticks, offset = 100 * (int)purpose;
                    var trip = (Caravan)Activator.CreateInstance(typeof(Caravan), BindingFlags.Instance | BindingFlags.NonPublic, null,
                        [from, to, new List<Colonist>(), Array.Empty<TradeLine>(), 0d, now - offset, now + 800 - offset, now + 1600 - offset], null)!;
                    Set(trip, "Purpose", purpose); Set(trip, "TargetRegion", world.WorldMap.TileOf(to)); Set(trip, "Route", route);
                    var point = route.At((int)purpose / 8f);
                    int index = route.Tiles.ToList().IndexOf(point.From);
                    Set(trip, "RouteIndex", index); Set(trip, "SegmentTravelCost", (double)((route.Cumulative[index + 1] - route.Cumulative[index]) * point.T));
                    trip.Cargo[ResourceType.Wood] = 12; world.Caravans.Add(trip);
                }
            }
            var control = new WorldMapView { Size = new Vector2(1600, 900) };
            _worldMap = control;
            control.Init(world); AddChild(control);
            if (hover == "chief") _hover = () => control.ScreenPositionOf(world.WorldMap.TileOf(world.Colonies[1]));
            if (hover == "caravan")
            {
                Colony from = world.Colonies[0], to = world.Colonies[1]; long now = world.Clock.Ticks;
                var trip = (Caravan)Activator.CreateInstance(typeof(Caravan), BindingFlags.Instance | BindingFlags.NonPublic, null,
                    [from, to, new List<Colonist>(), Array.Empty<TradeLine>(), 0d, now - 50, now + 50, now + 150], null)!;
                trip.Cargo[ResourceType.Wood] = 12; Set(trip, "Coins", 30); Set(trip, "Gear", CaravanGear.IronCart);
                world.Caravans.Add(trip);
                _hover = () =>
                {
                    var position = world.WorldMap.Route(from, to)!.At(trip.RoutePosition(now));
                    return control.ScreenPositionOf(position.From).Lerp(control.ScreenPositionOf(position.To), position.T);
                };
            }
            return;
        }
        Colony colony = world.Colonies[0]; LocalMap map = colony.Map; Settlement place = colony.PrimarySettlement;
        MethodInfo ground = typeof(LocalMap).GetMethod("SetGenerated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo river = typeof(LocalMap).GetMethod("SetRiver", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++) ground.Invoke(map, [x, y, 5, SoilType.Grass, FloraType.None, 0f, .5f]);
        colony.Buildings.Clear(); place.Herds.Clear();
        for (int i = 0; i < WildSpeciesInfo.All.Length; i++)
        {
            WildSpecies species = WildSpeciesInfo.All[i]; int x = 10 + i % 5 * 8, y = 10 + i / 5 * 7;
            var herd = new WildHerd();
            Set(herd, "Id", i + 1); Set(herd, "Species", species); Set(herd, "Count", species == WildSpecies.Bear ? 1 : 3); Set(herd, "Young", species == WildSpecies.Bear ? 0 : 1);
            Set(herd, "X", x + .5f); Set(herd, "PrevX", x + .5f); Set(herd, "Y", y + .5f); Set(herd, "PrevY", y + .5f);
            Set(herd, "State", species == WildSpecies.Wolf ? HerdState.Stalking : HerdState.Grazing); Set(herd, "IsAlpha", species == WildSpecies.Bear);
            place.Herds.Add(herd);
            AddChild(new Label { Text = WildSpeciesInfo.Name(species), Position = new Vector2(x * 32 - 24, y * 32 + 16), ZIndex = 1, Modulate = ArtDirection.Charcoal });
        }
        Building pen = Urbanism.PlanBuilding(map, colony, BuildingType.Pen, 10, 23); Set(pen, "Progress", 1f);
        Set(place, "Chickens", 3); Set(place, "Sheep", 2); Set(place, "Cows", 2); Set(place, "Horses", 2); Set(place, "Oxen", 2);
        var taming = new TamingAnimal(); Set(taming, "Species", ResourceType.Horses); Set(taming, "IsYoung", true); place.Taming.Add(taming);
        var nature = world.Regions[place.RegionTileIndex].Wildlife;
        nature.Hives.Clear(); nature.Mushrooms.Clear(); nature.Herbs.Clear();
        nature.Hives[24 * map.Width + 21] = 2; nature.Mushrooms[24 * map.Width + 23] = 3; nature.Herbs[24 * map.Width + 25] = 2;
        for (int y = 21; y < 32; y++) river.Invoke(map, [34, y, 34, y + 1, 1]);
        var bridge = new BridgeSite(); Set(bridge, "Id", 1); bridge.Cells.AddRange([25 * map.Width + 34, 28 * map.Width + 34]);
        bridge.BuiltCells.Add(25 * map.Width + 34); place.BridgeSites.Add(bridge);
        typeof(RoadLayer).GetMethod("SetSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(map.Roads, [25 * map.Width + 34, RoadSurface.Bridge]);
        foreach (Colonist colonist in colony.Members) { Set(colonist, "X", 28.5f); Set(colonist, "Y", 25.5f); Set(colonist, "PrevX", 28.5f); Set(colonist, "PrevY", 25.5f); }
        Set(colony.Members[0], "UsingCart", true); Set(colony.Members[0], "Carrying", ((ResourceType Type, int Amount)?)(ResourceType.Wood, 12));
        colony.Stock.Add(ResourceType.Dogs, 1);
        Set(colony.Members[1], "X", 30.5f); Set(colony.Members[1], "PrevX", 30.5f);
        Set(colony.Members[1], "Activity", new Activity(ActivityKind.Hunt, 30, 25, 100));
        var terrain = new MapView(); terrain.Init(map); AddChild(terrain);
        var view = new ColonistsView { Alpha = 1, AmbientEffectsEnabled = false }; view.Init(world, colony); AddChild(view); _village = view;
        AddChild(new Camera2D { Position = new Vector2(26, 20) * 32, Zoom = Vector2.One * 1.1f });
        if (hover == "herd") _hover = () => GetViewport().GetCanvasTransform() * new Vector2(34.5f * 32, 10.5f * 32 - 8);
        if (hover == "porter") _hover = () => GetViewport().GetCanvasTransform() * new Vector2(28.5f * 32, 25.5f * 32 - 4);
        if (hover == "hud")
        {
            Leadership.Elect(world, colony);
            Set(colony, "RealmId", 1);
            var hud = new Hud(); AddChild(hud); hud.ShowColony(colony, world.Clock);
            _hudHover = true;
            _hover = () => ((Control)typeof(Hud).GetField("_colonyMeta", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hud)!).GetGlobalRect().GetCenter();
        }
        foreach ((string label, Vector2 at) in new[] { ("Enclos et apprivoisement", new Vector2(10, 22)), ("Ruches · champignons · herbes", new Vector2(20, 23)), ("Porteur · chasse et chien", new Vector2(27, 24)), ("Pont · chantier", new Vector2(34, 23)) })
            AddChild(new Label { Text = label, Position = at * 32, Modulate = ArtDirection.Charcoal });
    }
    public override void _Process(double delta)
    {
        if (_frames <= 60 && _hover is not null)
        {
            if (_worldMap is not null) _worldMap.HoverPositionForValidation = _hover();
            if (_village is not null) _village.HoverPositionForValidation = _village.GetGlobalTransformWithCanvas().AffineInverse() * _hover();
            if (_hudHover) GetViewport().PushInput(new InputEventMouseMotion { Position = _hover(), GlobalPosition = _hover() }, true);
        }
        if (--_frames != 0) return;
        if (_capture is not null) GetViewport().GetTexture().GetImage().SavePng(_capture);
        GetTree().Quit();
    }
}
