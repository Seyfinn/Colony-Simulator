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

/// <summary>Scène isolée de livraison T-027 à T-030, jamais instanciée pendant une partie.</summary>
public partial class CivilizationPreview : Node2D
{
    private int _frame;
    private string _mode = "gallery";
    private string? _capture;
    private WorldState _world = null!;
    private CivilizationPanel? _panel;
    private ScrollContainer? _scroll;
    private ulong _card;
    private string _before = "";
    private bool _fallback;

    public override void _Ready()
    {
        try
        {
            TextureFilter = TextureFilterEnum.Nearest;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--export-civilization") CivilizationArt.Export();
                if (arg.StartsWith("--preview=")) _mode = arg[10..];
                if (arg.StartsWith("--capture=")) _capture = arg[10..];
                if (arg == "--compact") { GetWindow().Size = new Vector2I(1100, 700); GetWindow().ContentScaleSize = Vector2I.Zero; }
                if (arg == "--fallback") { _fallback = true; AssetLibrary.NativeFallbackForValidation = true; }
            }
            ValidateAssets();
            _world = new WorldState(12345, colonyCount: 4, startingColonists: 14, migration: false, lifecycle: false, trade: false);
            Colony c = _world.Colonies[0], enemy = _world.Colonies[1], ally = _world.Colonies[2];
            // Données explicites de démonstration, établies une seule fois avant toute vérification de la vue.
            c.Known.Clear(); c.Known[Discovery.Agriculture] = 0; c.Known[Discovery.Metallurgy] = 0;
            Set(c, "Researching", Discovery.Husbandry);
            c.Opinions[enemy] = -70; enemy.Opinions[c] = -45;
            c.Opinions[ally] = 70; ally.Opinions[c] = 55;
            Diplomacy.DeclareWar(_world, c, enemy); Diplomacy.SealAlliance(_world, c, ally);
            var party = Warfare.Depart(_world, c, enemy);
            Require(party is not null, "Pas de bande de contrôle.");
            Require(party!.Warriors.All(w => CivilizationArt.IsWarrior(_world, w)), "Identification des guerriers.");
            Require(!CivilizationArt.IsWarrior(_world, enemy.Members[0]), "Civil pris pour un guerrier.");
            if (_mode == "return") Set(party, "State", WarPartyState.Returning);
            _before = Snapshot();
            if (_mode == "gallery") Gallery();
            else if (_mode is "map" or "return") Map();
            else if (_mode == "local") Local();
            else Panel();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Set(object target, string property, object value) => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    private static string Hash(Image image) => Convert.ToHexString(SHA256.HashData(image.GetData()));
    private void ValidateAssets()
    {
        var distinct = new HashSet<string>();
        foreach (Discovery d in Enum.GetValues<Discovery>())
        {
            var image = CivilizationArt.KnowledgeIcon(d).GetImage();
            Require(image.GetWidth() == 16 && image.GetHeight() == 16, "Taille : " + d);
            for (int i = 0; i < 16; i++) Require(image.GetPixel(i, 0).A == 0 && image.GetPixel(i, 15).A == 0 && image.GetPixel(0, i).A == 0 && image.GetPixel(15, i).A == 0, "Marge : " + d);
            Require(Hash(image) == Hash(CivilizationArt.KnowledgeSource(d).Image), "PNG différent du secours : " + d);
            distinct.Add(Hash(image));
        }
        Require(distinct.Count == 19, "Savoirs indiscernables.");
        Require(Enumerable.Range(0, 4).Select(i => Hash(CivilizationArt.WarbandSource(i).Image)).Distinct().Count() == 4, "Poses identiques.");
        for (int i = 0; i < 4; i++)
        {
            if (!_fallback)
            {
                var frame = AssetLibrary.Get($"world/warband_{i}.png")!;
                Require(frame.GetWidth() == 24 && frame.GetHeight() == 16 && Hash(frame.GetImage()) == Hash(CivilizationArt.WarbandSource(i).Image), "Bande : " + i);
            }
        }
        Require(Hash(CivilizationArt.WarbandFrame(25).GetImage()) == Hash(CivilizationArt.WarbandFrame(25).GetImage()), "Animation en pause.");
        Require(CivilizationArt.WarPact().GetWidth() == 16, "Pacte.");
    }

    private void Gallery()
    {
        Label("Savoirs et peuples en marche", 28, 18, 26);
        int i = 0;
        foreach (Discovery d in Enum.GetValues<Discovery>())
        {
            int x = 30 + i % 5 * 306, y = 78 + i / 5 * 110;
            Sprite(CivilizationArt.KnowledgeIcon(d), x, y, 3);
            Sprite(CivilizationArt.KnowledgeIcon(d), x + 60, y + 18, 1);
            Label(Knowledge.Name(d), x + 88, y + 12, 17); i++;
        }
        Label("Bande de guerriers · quatre poses · aller / retour", 28, 532, 20);
        for (int f = 0; f < 4; f++) { Sprite(CivilizationArt.WarbandSource(f).Texture(), 30 + f * 150, 580, 4); Label($"Pose {f + 1}", 30 + f * 150, 656, 14); }
        var mirrored = new Sprite2D { Texture = CivilizationArt.WarbandFrame(0), FlipH = true, Centered = false, Position = new Vector2(680, 580), Scale = Vector2.One * 4 }; AddChild(mirrored);
        Sprite(CivilizationArt.WarPact(), 850, 580, 4); Label("Guerre", 850, 656, 14);
        Label("Guerriers locaux · les quatre peuples gardent leurs proportions et leurs costumes", 28, 725, 20);
        var equipment = new EquipmentGallery { Position = new Vector2(40, 780), Scale = Vector2.One * 2 }; AddChild(equipment);
    }

    private void Panel()
    {
        var root = new PanelContainer { Position = new Vector2(24, 24), Size = new Vector2(Math.Clamp(GetWindow().Size.X * 0.49f, 600, 740), GetWindow().Size.Y - 48) };
        root.AddThemeStyleboxOverride("panel", MenuStyle.Box(DashboardStyle.Readout, MenuStyle.Edge, 16)); AddChild(root);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(_scroll);
        _panel = new CivilizationPanel(); _scroll.AddChild(_panel);
        _panel.Refresh(_world, _world.Colonies[0]);
        if (_mode == "relations") { _panel.ShowRelations(); _panel.Refresh(_world, _world.Colonies[0]); }
        _card = Descendants(_panel).OfType<PanelContainer>().Single(n => n.Name == "SavoirAgriculture").GetInstanceId();
    }

    private void Map()
    {
        // Au retour, placer la bande à mi-chemin, sans faire avancer ni combattre la simulation.
        var party = _world.WarParties[0];
        long target = _mode == "return" ? party.ArriveTicks + (party.ArriveTicks - party.DepartTicks) / 2 : (party.ArriveTicks + party.DepartTicks) / 2;
        if (_mode == "return") Set(party, "ReturnTicks", party.ArriveTicks + party.ArriveTicks - party.DepartTicks);
        while (_world.Clock.Ticks < target) _world.Clock.Advance();
        _before = Snapshot();
        var map = new WorldMapView { Position = new Vector2(20, 20), Size = new Vector2(GetWindow().Size.X - 40, GetWindow().Size.Y - 40) };
        AddChild(map); map.Init(_world);
    }

    private void Local()
    {
        var colony = _world.Colonies[0];
        int cx = colony.CampX, cy = colony.CampY;
        int tx = Math.Clamp(cx - 18, 0, colony.Map.Width - 36), ty = Math.Clamp(cy - 12, 0, colony.Map.Height - 28);
        var ground = ImageTexture.CreateFromImage(Image.CreateFromData(36 * 32, 28 * 32, false, Image.Format.Rgba8, TerrainPainter.Paint(colony.Map, tx, ty, 36, 28)));
        AddChild(new Sprite2D { Texture = ground, Centered = false, Position = new Vector2(tx, ty) * 32 });
        var camera = new Camera2D { Position = new Vector2(cx, cy) * 32, Zoom = Vector2.One * 2 }; AddChild(camera); camera.MakeCurrent();
        var view = new ColonistsView(); AddChild(view); view.Init(_world, colony);
    }

    private string Snapshot() => _world.Clock.Ticks + "|" + string.Join("|", _world.Colonies.Select(c =>
        c.Name + ":" + c.Researching + ":" + string.Join(",", c.Known) + ":" + string.Join(",", c.Opinions) + ":" + string.Join(",", c.Grudges)
        + ":" + string.Join(",", Enum.GetValues<ResourceType>().Select(r => c.Stock.Get(r)))
        + ":" + string.Join(",", c.Members.Concat(c.Transients).Select(m => $"{m.Id}/{m.X}/{m.Y}/{m.Transit}"))))
        + "|" + string.Join(",", _world.Pacts.Select(p => $"{p.Kind}/{p.SinceTicks}/{p.UntilTicks}"))
        + "|" + string.Join(",", _world.WarParties.Select(p => $"{p.State}/{p.Warriors.Count}/{p.RoutePosition(_world.Clock.Ticks)}/{p.Victory}"));

    public override void _Process(double delta)
    {
        try
        {
            _frame++;
            if (_panel is not null)
            {
                _panel.Refresh(_world, _world.Colonies[0]);
                if (_frame == 8)
                {
                    var colony = _world.Colonies[0];
                    Require(Descendants(_panel).OfType<TextureRect>().Count(n => n.Name.ToString().StartsWith("IconeSavoir")) == 19, "Icônes manquantes.");
                    foreach (Discovery d in Enum.GetValues<Discovery>())
                    {
                        var card = Descendants(_panel).OfType<PanelContainer>().Single(n => n.Name == $"Savoir{d}");
                        Require(card.TooltipText.Contains(Knowledge.Info(d).Effect), "Effet absent.");
                        var icon = Descendants(card).OfType<TextureRect>().Single();
                        bool available = colony.Known.ContainsKey(d) || colony.Researching == d || Knowledge.CanResearch(colony, d);
                        Require((icon.Modulate == Colors.White) == available, "Icône verrouillée : " + d);
                    }
                    var relations = Descendants(_panel).OfType<Button>().Single(n => n.Name == "OngletRelations");
                    relations.EmitSignal(BaseButton.SignalName.Pressed); _panel.Refresh(_world, colony);
                    Require(_panel.ShowingRelations && Descendants(_panel).OfType<PanelContainer>().Count(n => n.Name.ToString().StartsWith("Relation")) == 3, "Relations absentes.");
                    var relation = Descendants(_panel).OfType<PanelContainer>().Single(n => n.Name == "Relation0");
                    Require(Descendants(relation).OfType<Label>().Any(n => n.Text.Contains("En guerre")), "État guerre perdu.");
                    Descendants(_panel).OfType<Button>().Single(n => n.Name == "OngletSavoirs").EmitSignal(BaseButton.SignalName.Pressed);
                    _panel.Refresh(_world, colony);
                    if (_mode == "relations") relations.EmitSignal(BaseButton.SignalName.Pressed);
                }
            }
            if (_frame < 30) return;
            Require(_before == Snapshot(), "Données changées par la vue.");
            if (_panel is not null) Require(_card == Descendants(_panel).OfType<PanelContainer>().Single(n => n.Name == "SavoirAgriculture").GetInstanceId(), "Cartes reconstruites.");
            GD.Print($"CIVILIZATION_VISUALS_OK : {_mode}, secours={_fallback}, 24 PNG, états, onglets, guerriers, données conservées.");
            if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok) throw new InvalidOperationException("Capture impossible.");
            GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }

    private sealed partial class EquipmentGallery : Node2D
    {
        public override void _Draw()
        {
            foreach (PeopleLook look in Enum.GetValues<PeopleLook>())
            {
                var appearance = new ColonistAppearance(0, look, WoodlandBiome.TemperatePlain);
                Vector2 feet = new(32 + (int)look * 140, 38);
                var frames = PeoplesSprites.Get(appearance);
                Vector2 offset = new(-frames[0].GetWidth() / 2f, -frames[0].GetHeight());
                DrawTexture(frames[0], feet + offset);
                CivilizationArt.DrawEquipment(this, feet, appearance, false, 0);
                feet.X += 46; DrawTexture(frames[1], feet + offset);
                CivilizationArt.DrawEquipment(this, feet, appearance, true, 1);
            }
        }
    }
    private static IEnumerable<Node> Descendants(Node n) { yield return n; foreach (Node child in n.GetChildren()) foreach (Node d in Descendants(child)) yield return d; }
    private void Label(string text, int x, int y, int size) { var label = new Label { Text = text, Position = new Vector2(x, y) }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", ArtDirection.Cream); AddChild(label); }
    private void Sprite(Texture2D t, int x, int y, int scale) => AddChild(new Sprite2D { Texture = t, Centered = false, Position = new Vector2(x, y), Scale = Vector2.One * scale });
    private static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private void Fail(Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
}
