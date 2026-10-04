using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.View;

/// <summary>Scénarios graphiques isolés, données de démonstration explicites et horloge figée.</summary>
public partial class VillagePreview : Node2D
{
    private string? _capture;
    private string _mode = "gallery";
    private int _frame;
    private WorldState? _world;
    private Dictionary<ResourceType, int> _stock = [];
    private ColonistsView? _view;
    private string _fingerprint = "";
    private EconomyDashboard? _dashboard;
    private readonly List<Label> _milestoneRows = [];
    private bool _fallback;

    public override void _Ready()
    {
        try
        {
            TextureFilter = TextureFilterEnum.Nearest;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--export-village") VillageArt.ExportAll();
                if (arg.StartsWith("--capture=")) _capture = arg[10..];
                if (arg.StartsWith("--preview=")) _mode = arg[10..];
                if (arg == "--compact") { GetWindow().Size = new Vector2I(1100, 700); GetWindow().ContentScaleSize = Vector2I.Zero; }
                if (arg == "--fallback") { _fallback = true; AssetLibrary.NativeFallbackForValidation = true; }
            }
            ValidateArt();
            if (_mode == "gallery") Gallery(); else Village();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void ValidateArt()
    {
        void Check(Texture2D image, int w, int h) => Require(image.GetWidth() == w && image.GetHeight() == h, "Taille incorrecte.");
        foreach (string good in VillageArt.Goods) Check(ResourceIcons.Get(good), 16, 16);
        Check(ResourceIcons.Get("Milestone"), 16, 16);
        foreach (string state in new[] {"sick", "injured", "boosted"}) Check(VillageArt.Status(state), 12, 12);
        foreach (string building in VillageArt.Buildings) Check(BuildingSprites.Get(building), 64, 80);
        foreach (string kind in new[] {"chicken", "sheep", "cow"})
        {
            var hashes = new HashSet<string>();
            for (int i = 0; i < 4; i++)
            {
                Texture2D frame = VillageArt.Animal(kind, i);
                Check(frame, kind == "chicken" ? 12 : kind == "sheep" ? 16 : 24, kind == "chicken" ? 12 : kind == "sheep" ? 14 : 18);
                hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.GetImage().GetData())));
            }
            Require(hashes.Count == 4, "Animation sans quatre poses distinctes.");
        }
        for (int i = 0; i < 4; i++) Check(VillageArt.Event("building_fire", i), 64, 80);
        Check(VillageArt.Event("building_ashes"), 64, 80);
    }

    private void Gallery()
    {
        Label("Village : denrées et états · pixels natifs, puis agrandis", 24, 15);
        int i = 0;
        foreach (string good in VillageArt.Goods.Append("Milestone"))
        {
            int x = 24 + i % 8 * 195, y = 55 + i / 8 * 112;
            Sprite(ResourceIcons.Get(good), x, y, 3); Sprite(ResourceIcons.Get(good), x + 60, y + 18, 1); Label(good, x, y + 52, 13); i++;
        }
        Label("Architecture : enclos vide, textile, marché, soins, stockage, eau, repos, école", 24, 283);
        i = 0;
        foreach (string kind in VillageArt.Buildings) { Sprite(BuildingSprites.Get(kind), 24 + i * 195, 320, 2); Label(kind, 24 + i * 195, 485, 14); i++; }
        Label("Bêtes : quatre poses chacune · santé : fièvre / blessure / ragoût", 24, 535);
        i = 0;
        foreach (string kind in new[] {"chicken", "sheep", "cow"})
        { for (int f = 0; f < 4; f++) Sprite(VillageArt.Animal(kind, f), 24 + i * 350 + f * 70, 580, 2); i++; }
        i = 0; foreach (string state in new[] {"sick", "injured", "boosted"}) { Sprite(VillageArt.Status(state), 1110 + i * 140, 580, 3); Label(state, 1110 + i * 140, 627, 14); i++; }
        Label("Feu et cendres : quatre poses de feu, une ruine", 24, 680);
        for (int f = 0; f < 4; f++) Sprite(VillageArt.Event("building_fire", f), 24 + f * 160, 720, 2);
        Sprite(VillageArt.Event("building_ashes"), 700, 720, 2);
    }

    private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    private void Village()
    {
        _world = new WorldState(42, startingColonists: 24, migration: false, lifecycle: false, trade: false);
        Colony colony = _world.Colonies[0];
        colony.Buildings.Clear();
        int cx = colony.CampX, cy = colony.CampY;
        for (int i = 0; i < 8; i++)
        {
            var b = new Building(Enum.Parse<BuildingType>(VillageArt.Buildings[i]), cx - 8 + i % 4 * 5, cy - 4 + i / 4 * 5);
            Set(b, "Progress", 1f); colony.Buildings.Add(b);
        }
        var pen = colony.Buildings[0];
        Set(colony, "Chickens", _mode == "empty" ? 0 : 6); Set(colony, "Sheep", _mode == "empty" ? 0 : 4); Set(colony, "Cows", _mode == "empty" ? 0 : 2);
        Set(colony, "EggsReady", 3f); Set(colony, "MilkReady", 2f);
        Require(ColonistsView.AnimalsInPen(colony, pen, ResourceType.Cows) == (_mode == "empty" ? 0 : 2), "Effectif incorrect.");
        var secondPen = new Building(BuildingType.Pen, cx - 8, cy + 5); Set(secondPen, "Progress", 1f); colony.Buildings.Add(secondPen);
        Require(ColonistsView.AnimalsInPen(colony, secondPen, ResourceType.Cows) == 0, "Bêtes dupliquées dans le deuxième enclos.");
        Set(colony, "Cows", 6); Require(ColonistsView.AnimalsInPen(colony, secondPen, ResourceType.Cows) == 2, "Troupeau non réparti."); Set(colony, "Cows", _mode == "empty" ? 0 : 2);
        if (_mode == "winter")
        {
            Set(colony.Map, "Biome", Biome.BorealForest); Set(colony, "ColdSnapDaysLeft", 2);
            for (int tick = 0; tick < 15 * TimeConstants.TicksPerDay + 350; tick++) _world.Clock.Advance();
        }
        else
        {
            for (int tick = 0; tick < 350; tick++) _world.Clock.Advance();
            if (_mode == "drought") Set(colony, "DroughtDaysLeft", 2);
        }
        var savedBiome = colony.Map.Biome;
        Set(colony.Map, "Biome", Biome.Desert); Require(ColonistsView.SnowStrength(colony, Season.Hiver) == 0, "Neige en désert.");
        Set(colony.Map, "Biome", savedBiome);
        for (int i = 0; i < colony.Members.Count; i++)
        {
            var member = colony.Members[i]; float x = cx - 7 + i % 8 * 2, y = cy + 4 + i / 8 * 2;
            Set(member, "X", x); Set(member, "PrevX", x); Set(member, "Y", y); Set(member, "PrevY", y); Set(member, "Home", null!);
            Building b = colony.Buildings[i % 8];
            ActivityKind kind = (i % 8) switch {0 => ActivityKind.Tend, 1 => ActivityKind.Craft, 2 => ActivityKind.Craft, 3 => ActivityKind.Heal, 6 => ActivityKind.Relax, 7 => ActivityKind.Study, _ => ActivityKind.Eat};
            var activity = new Activity(kind, (int)x, (int)y, 100) { Building = b, Meal = i % 8 == 4 ? ResourceType.Cake : ResourceType.Stew };
            activity.Started = true; Set(member, "Activity", activity);
            if (i == 1) Health.Fall(colony, member, Ailment.Sick, 48, _world.Clock, "");
            if (i == 2) Health.Fall(colony, member, Ailment.Injured, 48, _world.Clock, "");
            if (i == 3 || i == 1) Set(member, "BoostUntilTicks", _world.Clock.Ticks + 900);
        }
        int tx = Math.Clamp(cx - 17, 0, colony.Map.Width - 35), ty = Math.Clamp(cy - 12, 0, colony.Map.Height - 25);
        int width = 35, height = 25;
        var ground = ImageTexture.CreateFromImage(Image.CreateFromData(width * 32, height * 32, false, Image.Format.Rgba8, TerrainPainter.Paint(colony.Map, tx, ty, width, height)));
        AddChild(new Sprite2D { Texture = ground, Centered = false, Position = new Vector2(tx, ty) * 32 });
        var camera = new Camera2D { Position = new Vector2(cx + 0.5f, cy + 1) * 32, Zoom = new Vector2(2.2f, 2.2f) }; AddChild(camera); camera.MakeCurrent();
        _view = new ColonistsView(); AddChild(_view); _view.Init(_world, colony); _view.Selected = colony.Members[1];
        if (_mode is "events" or "ashes")
        {
            Civic.Burn(colony, colony.Buildings[4], _world.Clock);
            typeof(Colony).GetMethod("RecordEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(colony, [new RecentEvent(ColonyEventKind.Raid, cx - 12, cy, _world.Clock.Ticks - 140, ColonyEventOutcome.Repelled)]);
            Events.Peddler(_world, colony);
            if (_mode == "ashes") for (int i = 0; i < 200; i++) _world.Clock.Advance();
        }
        var hud = new Hud(); AddChild(hud); hud.SetStatus(_world.Clock, GameSpeed.Pause); hud.ShowColony(colony, _world.Clock);
        if (_mode == "food") Descendants(hud).OfType<Button>().Single(b => b.Name == "ToggleFoodDetails").EmitSignal(BaseButton.SignalName.Pressed);
        if (_mode == "milestones")
        {
            var panel = new WorldPanel(); panel.Init(_world); AddChild(panel); panel.Open = true;
            _dashboard = Descendants(panel).OfType<EconomyDashboard>().Single();
            colony.Achievements["flock"] = _world.Clock.Ticks;
            _dashboard.Refresh(_world, colony); _dashboard.ShowMilestones();
        }
        _stock = Enum.GetValues<ResourceType>().ToDictionary(g => g, g => colony.Stock.Get(g));
        _fingerprint = MapFingerprint(colony.Map);
    }

    private static string MapFingerprint(LocalMap map)
    {
        var bytes = new List<byte>();
        for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++)
        { bytes.Add((byte)map.GetSurface(x, y)); bytes.Add((byte)map.GetFlora(x, y)); bytes.Add((byte)map.GetElevation(x, y)); }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
    }
    private static IEnumerable<Node> Descendants(Node node) { yield return node; foreach (Node child in node.GetChildren()) foreach (Node descendant in Descendants(child)) yield return descendant; }
    public override void _Process(double delta)
    {
        try
        {
            _frame++;
            if (_frame == 8 && _world is not null) _world.Colonies[0].Achievements["cake"] = _world.Clock.Ticks;
            if (_frame < 45) return;
            if (_world is not null)
            {
                Colony colony = _world.Colonies[0];
                Require(_fingerprint == MapFingerprint(colony.Map), "L'affichage a modifié le terrain.");
                Require(Descendants(_view!).OfType<SeasonGround>().Single().DrawCount <= 3, "Le calque saisonnier est repeint à chaque image.");
                foreach (var (good, amount) in _stock) Require(colony.Stock.Get(good) == amount, "L'affichage a modifié un stock.");
                _dashboard?.Refresh(_world, colony);
            }
            GD.Print($"VILLAGE_VISUALS_OK : {_mode}, secours={_fallback}, formats, effectifs, répartition, terrain et stocks.");
            if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok) throw new InvalidOperationException("Capture impossible.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void Label(string text, int x, int y, int size = 19) { var label = new Label { Text = text, Position = new Vector2(x, y) }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", ArtDirection.Cream); AddChild(label); }
    private void Sprite(Texture2D texture, int x, int y, int scale) => AddChild(new Sprite2D { Texture = texture, Centered = false, Position = new Vector2(x, y), Scale = Vector2.One * scale });
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
