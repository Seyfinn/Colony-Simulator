using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Contrôles isolés de T-023 à T-026 ; stocks et historiques de démonstration explicites.</summary>
public partial class WorkshopPreview : Node2D
{
    private string _mode = "gallery";
    private string? _capture;
    private bool _fallback;
    private int _frame;
    private WorldState? _world;
    private StatsPanel? _stats;
    private ColonyHistory? _history;
    private Dictionary<ResourceType, int>[] _stocks = [];
    private string _mapBefore = "";
    private ulong _cardId;

    public override void _Ready()
    {
        try
        {
            TextureFilter = TextureFilterEnum.Nearest;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--export-workshops") WorkshopArt.ExportAll();
                if (arg.StartsWith("--preview=")) _mode = arg[10..];
                if (arg.StartsWith("--capture=")) _capture = arg[10..];
                if (arg == "--compact") { GetWindow().Size = new Vector2I(1100, 700); GetWindow().ContentScaleSize = Vector2I.Zero; }
                if (arg == "--fallback") { _fallback = true; AssetLibrary.NativeFallbackForValidation = true; }
            }
            if (_mode is "bridges" or "mills" or "places") { AddChild(new WaterworksPreview()); return; }
            ValidateArt();
            if (_mode == "gallery") Gallery();
            else if (_mode == "local") Local();
            else Stats();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    private static string Hash(Image image) => Convert.ToHexString(SHA256.HashData(image.GetData()));
    private void ValidateArt()
    {
        foreach (string good in WorkshopArt.Goods)
        {
            Image image = ResourceIcons.Get(good).GetImage();
            Require(image.GetWidth() == 16 && image.GetHeight() == 16, "Dimension d'icône.");
            for (int i = 0; i < 16; i++) Require(image.GetPixel(i, 0).A == 0 && image.GetPixel(i, 15).A == 0 && image.GetPixel(0, i).A == 0 && image.GetPixel(15, i).A == 0, "Marge d'icône : " + good);
            Require(Hash(image) == Hash(WorkshopArt.IconSource(good).Image), "PNG différent de sa source : " + good);
        }
        foreach (string kind in WorkshopArt.Buildings)
        {
            var texture = BuildingSprites.Get(kind);
            bool dam = kind is "Dam" or "DamSide";
            Require(texture.GetWidth() == (dam ? 32 : 64) && texture.GetHeight() == (dam ? 48 : 80), "Dimension de bâtiment.");
            Require(Hash(texture.GetImage()) == Hash(BuildingSprites.WorkshopSource(kind).Image), "PNG différent de sa source : " + kind);
        }
        var cask = new Building(BuildingType.Cask, 0, 0);
        Require(WorkshopArt.CaskState(cask, 100) == "empty", "Fût vide.");
        Set(cask, "BrewReadyTicks", 100L);
        Require(WorkshopArt.CaskState(cask, 99) == "brewing" && WorkshopArt.CaskState(cask, 100) == "ready" && WorkshopArt.CaskState(cask, 101) == "ready", "Seuil de fermentation.");
        var hashes = new HashSet<string>();
        foreach (string state in new[] {"empty", "brewing", "ready"})
        {
            Image image = WorkshopArt.Cask(state).GetImage();
            Require(image.GetWidth() == 64 && image.GetHeight() == 80, "Dimension du fût.");
            Require(Hash(image) == Hash(BuildingSprites.CaskSource(state).Image), "État du fût différent de sa source.");
            hashes.Add(Hash(image));
        }
        Require(hashes.Count == 3, "États du fût identiques.");
        Require(WorkshopArt.Bubbles(50).SequenceEqual(WorkshopArt.Bubbles(50)) && !WorkshopArt.Bubbles(50).SequenceEqual(WorkshopArt.Bubbles(59)), "Pause des bulles.");
        foreach (var (a, b) in new[] {("Flour", "Salt"), ("IronOre", "Stone"), ("Charcoal", "IronOre"), ("Bread", "Cake"), ("Beer", "Milk")})
            Require(Hash(ResourceIcons.Get(a).GetImage()) != Hash(ResourceIcons.Get(b).GetImage()), "Denrées confondues.");
        Require(RemainingArt.WheelFrame(0).GetWidth() == 24 && BuildingSprites.DamConstruction(0.5f, true).GetHeight() == 48, "Contrat des superpositions.");
    }

    private void Gallery()
    {
        Label("Métiers et provisions · T-023 / T-024 / T-025", 24, 14, 24);
        int i = 0;
        foreach (string good in WorkshopArt.Goods)
        {
            int x = 24 + i % 6 * 260, y = 62 + i / 6 * 94;
            Sprite(ResourceIcons.Get(good), x, y, 3); Sprite(ResourceIcons.Get(good), x + 62, y + 18, 1);
            Label(ResourceIcons.Name(Enum.Parse<ResourceType>(good)), x + 92, y + 12, 15); i++;
        }
        Label("Architecture · silhouettes conservées, roue indépendante", 24, 259, 20);
        i = 0;
        foreach (string kind in WorkshopArt.Buildings)
        {
            int x = 24 + i % 4 * 390, y = 303 + i / 4 * 192;
            Sprite(BuildingSprites.Get(kind), x, y, 2);
            Label(kind, x + 140, y + 60, 16);
            if (kind == "Mill") Sprite(RemainingArt.WheelFrame(0), x + 80, y + 80, 2);
            if (kind == "Dam" || kind == "DamSide") Sprite(BuildingSprites.DamConstruction(0.5f, kind == "DamSide"), x + 210, y, 2);
            i++;
        }
        foreach (var (state, x, title) in new[] {("empty", 24, "Vide · bonde ouverte"), ("brewing", 414, "Fermentation · bulles"), ("ready", 804, "Bière prête · chope pleine")})
        {
            Sprite(WorkshopArt.Cask(state), x, 695, 2); Label(title, x + 140, 762, 15);
            if (state == "brewing") foreach (Vector2 p in WorkshopArt.Bubbles(36)) AddChild(new Polygon2D { Polygon = [p * 2, p * 2 + new Vector2(4, 0), p * 2 + new Vector2(4, 4), p * 2 + new Vector2(0, 4)], Position = new Vector2(x, 695), Color = ArtDirection.Cream });
        }
        Sprite(ResourceIcons.Get("Salt"), 1260, 720, 3); Sprite(ResourceIcons.Get("Cake"), 1340, 720, 3); Sprite(ResourceIcons.Get("Milk"), 1420, 720, 3);
        Label("Repères T-014", 1260, 790, 15);
    }

    private void Local()
    {
        _world = new WorldState(42, startingColonists: 12, migration: false, lifecycle: false, trade: false);
        var colony = _world.Colonies[0]; colony.Buildings.Clear();
        int cx = colony.CampX, cy = colony.CampY;
        for (int i = 0; i < 6; i++)
        {
            var building = new Building(Enum.Parse<BuildingType>(WorkshopArt.Buildings[i]), cx - 11 + i % 3 * 8, cy - 6 + i / 3 * 7);
            Set(building, "Progress", 1f); colony.Buildings.Add(building);
        }
        for (int i = 0; i < 3; i++)
        {
            var cask = new Building(BuildingType.Cask, cx - 7 + i * 8, cy + 9);
            Set(cask, "Progress", 1f); if (i > 0) Set(cask, "BrewReadyTicks", _world.Clock.Ticks + (i == 1 ? 100L : 1L));
            colony.Buildings.Add(cask);
        }
        for (int tick = 0; tick < 36; tick++) _world.Clock.Advance();
        var states = colony.Buildings.Where(b => b.Type == BuildingType.Cask).Select(b => WorkshopArt.CaskState(b, _world.Clock.Ticks));
        Require(states.SequenceEqual(new[] {"empty", "brewing", "ready"}), "La vue locale ne montre pas les trois états.");
        int tx = Math.Clamp(cx - 18, 0, colony.Map.Width - 36), ty = Math.Clamp(cy - 12, 0, colony.Map.Height - 28);
        var ground = ImageTexture.CreateFromImage(Image.CreateFromData(36 * 32, 28 * 32, false, Image.Format.Rgba8, TerrainPainter.Paint(colony.Map, tx, ty, 36, 28)));
        AddChild(new Sprite2D { Texture = ground, Centered = false, Position = new Vector2(tx, ty) * 32 });
        var camera = new Camera2D { Position = new Vector2(cx + 0.5f, cy + 1) * 32, Zoom = new Vector2(1.15f, 1.15f) }; AddChild(camera); camera.MakeCurrent();
        var view = new ColonistsView(); AddChild(view); view.Init(_world, colony);
        var hud = new Hud(); AddChild(hud); hud.SetStatus(_world.Clock, GameSpeed.Pause); hud.ShowColony(colony, _world.Clock);
        Descendants(hud).OfType<Button>().Single(b => b.Name == "CollapseJournal").EmitSignal(BaseButton.SignalName.Pressed);
        Snapshot();
    }

    private void Stats()
    {
        _world = new WorldState(42, colonyCount: 4, startingColonists: 12, migration: false, lifecycle: false, trade: false);
        _history = new ColonyHistory(); _history.Observe(_world);
        // Flux de démonstration consignés par le véritable comptable, sans courbes inventées dans l'affichage.
        for (int day = 0; day < 60; day++)
        {
            for (int i = 0; i < _world.Colonies.Count; i++)
            {
                var stock = _world.Colonies[i].Stock;
                stock.Add(ResourceType.Food, 18 + (day + i * 3) % 9);
                stock.TryTake(ResourceType.Food, 15 + (day + i) % 8);
                stock.Add(ResourceType.Coins, 2 + i * 3);
            }
            for (int tick = 0; tick < TimeConstants.TicksPerDay; tick++) _world.Clock.Advance();
            _history.Observe(_world);
        }
        _stats = new StatsPanel(); _stats.Init(_world); AddChild(_stats);
        RefreshStats();
        var hud = new Hud(); AddChild(hud); hud.SetStatus(_world.Clock, GameSpeed.Pause);
        hud.ShowUnsettled("Vue chiffrée · démonstration", "Historique de contrôle · simulation en pause");
        Snapshot();
    }

    private void Snapshot()
    {
        _stocks = _world!.Colonies.Select(c => Enum.GetValues<ResourceType>().ToDictionary(g => g, g => c.Stock.Get(g))).ToArray();
        _mapBefore = MapHash();
    }
    private string MapHash()
    {
        var bytes = new List<byte>();
        foreach (var colony in _world!.Colonies) for (int y = 0; y < colony.Map.Height; y++) for (int x = 0; x < colony.Map.Width; x++)
        { bytes.Add((byte)colony.Map.GetSurface(x, y)); bytes.Add((byte)colony.Map.GetFlora(x, y)); bytes.Add((byte)colony.Map.GetElevation(x, y)); }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
    private void RefreshStats() => _stats!.Refresh(_world!, _history!, 0, new StatsPace(200, true, _world!.Clock.TotalDays, 0));
    private static IEnumerable<Node> Descendants(Node node) { yield return node; foreach (Node child in node.GetChildren()) foreach (Node descendant in Descendants(child)) yield return descendant; }
    public override void _Process(double delta)
    {
        if (_mode is "bridges" or "mills" or "places") return;
        try
        {
            _frame++;
            if (_stats is not null)
            {
                RefreshStats();
                if (_frame == 6) _cardId = Descendants(_stats).OfType<GridContainer>().Single(c => c.Name == "CartesColonies").GetChild(0).GetInstanceId();
                if (_frame == 12)
                {
                    foreach (ColonyMetric metric in Enum.GetValues<ColonyMetric>())
                    {
                        Descendants(_stats).OfType<Button>().Single(b => b.Name == $"Courbe{metric}").EmitSignal(BaseButton.SignalName.Pressed);
                        Require(_stats.Metric == metric, "Choix de courbe ignoré.");
                    }
                    Descendants(_stats).OfType<Button>().Single(b => b.Name == "CourbeFoodDays").EmitSignal(BaseButton.SignalName.Pressed);
                    var first = Descendants(_stats).OfType<Sparkline>().First(c => c.Name == "CourbeColonie");
                    first._GuiInput(new InputEventMouseMotion { Position = new Vector2(first.Size.X / 2, 30) });
                    if (_mode == "comparison") Descendants(_stats).OfType<ScrollContainer>().Single(c => c.Name == "DefilementColonies").ScrollVertical = 10000;
                }
            }
            if (_frame < 45) return;
            if (_world is not null)
            {
                Require(_mapBefore == MapHash(), "Terrain modifié par le dessin.");
                for (int i = 0; i < _stocks.Length; i++) foreach (var (good, amount) in _stocks[i]) Require(_world.Colonies[i].Stock.Get(good) == amount, "Stock modifié par le dessin.");
            }
            if (_stats is not null)
            {
                Require(_cardId == Descendants(_stats).OfType<GridContainer>().Single(c => c.Name == "CartesColonies").GetChild(0).GetInstanceId(), "Cartes reconstruites à chaque rafraîchissement.");
                int observed = -1; _stats.ObserveRequested += index => observed = index;
                Descendants(_stats).OfType<Button>().First(b => b.Name == "ObserverColonie").EmitSignal(BaseButton.SignalName.Pressed);
                Require(observed == 0, "Observer pointe vers une autre colonie.");
                Require(_history!.World.Count == 61 && _history.Of(_world!.Colonies[0]).Count == 61, "Historique modifié.");
            }
            GD.Print($"WORKSHOP_VISUALS_OK : {_mode}, secours={_fallback}, icônes, bâtiments, fût, pause, données et commandes.");
            if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok) throw new InvalidOperationException("Capture impossible.");
            GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private void Label(string text, int x, int y, int size) { var label = new Label { Text = text, Position = new Vector2(x, y) }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", ArtDirection.Cream); AddChild(label); }
    private void Sprite(Texture2D texture, int x, int y, int scale) => AddChild(new Sprite2D { Texture = texture, Centered = false, Position = new Vector2(x, y), Scale = Vector2.One * scale });
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
}
