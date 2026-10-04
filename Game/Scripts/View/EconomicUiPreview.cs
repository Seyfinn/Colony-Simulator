using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Contrôle des vrais panneaux avec des données de démonstration, sans charger ni modifier une sauvegarde.</summary>
public partial class EconomicUiPreview : Node2D
{
    private WorldState _world = null!;
    private Hud _hud = null!;
    private WorldPanel _panel = null!;
    private string _mode = "economy";
    private string? _capture;
    private int _frame;
    private bool _compact;
    private ScrollContainer _scroll = null!;
    private ulong _childId;
    private int _scrollPosition;
    private Dictionary<ResourceType, int> _stock = [];
    private Dictionary<WorkSector, float> _shares = [];

    public override void _Ready()
    {
        try
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--capture=")) _capture = arg[10..];
                if (arg.StartsWith("--preview=")) _mode = arg[10..];
                if (arg == "--compact") _compact = true;
            }
            if (_compact) { GetWindow().Size = new Vector2I(1100, 700); GetWindow().ContentScaleSize = Vector2I.Zero; }
            _world = new WorldState(42, colonyCount: 4, startingColonists: 24, migration: false, lifecycle: false, trade: false);
            Colony colony = _world.Colonies[0];
            SeedDemonstration(colony);
            // Le fond utilise le même peintre que le terrain en jeu.
            int x = Math.Clamp(colony.CampX - 25, 0, colony.Map.Width - 50);
            int y = Math.Clamp(colony.CampY - 15, 0, colony.Map.Height - 30);
            var texture = ImageTexture.CreateFromImage(Image.CreateFromData(1600, 960, false, Image.Format.Rgba8,
                TerrainPainter.Paint(colony.Map, x, y, 50, 30)));
            AddChild(new Sprite2D { Texture = texture, Centered = false, TextureFilter = TextureFilterEnum.Nearest });
            _hud = new Hud(); AddChild(_hud);
            _panel = new WorldPanel(); _panel.Init(_world); AddChild(_panel);
            _hud.SetStatus(_world.Clock, GameSpeed.Pause); _hud.ShowColony(colony, _world.Clock);
            bool economy = _mode is "economy" or "commerce";
            _panel.Open = economy; _hud.SetOverlayState(false, economy);
            if (!economy) Named<Button>(_hud, "Production").EmitSignal(BaseButton.SignalName.Pressed);
            if (_mode == "commerce") Named<EconomyDashboard>(_panel, "TableauEconomie").ShowCommerce(true);
            _scroll = Named<ScrollContainer>(economy ? _panel : _hud, economy ? "DefilementEconomie" : "DefilementProduction");
            _stock = Enum.GetValues<ResourceType>().ToDictionary(g => g, g => colony.Stock.Get(g));
            _shares = new Dictionary<WorkSector, float>(colony.WorkShares);
        }
        catch (Exception error) { Fail(error); }
    }

    private void SeedDemonstration(Colony colony)
    {
        foreach (ResourceType good in Enum.GetValues<ResourceType>()) colony.Stock.TryTake(good, colony.Stock.Get(good));
        foreach (var (good, amount) in new[] { (ResourceType.Coins, 248), (ResourceType.Food, 180), (ResourceType.Grain, 32),
            (ResourceType.Bread, 18), (ResourceType.Flour, 4), (ResourceType.Wood, 145), (ResourceType.Stone, 72),
            (ResourceType.IronOre, 16), (ResourceType.Iron, 3), (ResourceType.Tools, 6), (ResourceType.Wool, 2) })
            colony.Stock.Add(good, amount);
        foreach (var (good, hours, units) in new[] { (ResourceType.Food, 72d, 120), (ResourceType.Grain, 63d, 90),
            (ResourceType.Wood, 36d, 60), (ResourceType.Stone, 56d, 40), (ResourceType.Charcoal, 28d, 20),
            (ResourceType.Iron, 48d, 12), (ResourceType.Tools, 30d, 5), (ResourceType.Bread, 24d, 40) })
            colony.Labor.Record(good, hours, units);
        colony.Labor.RecordHut(24); colony.Labor.RecordCanalTile(3);
        foreach (BuildingType type in new[] { BuildingType.Mill, BuildingType.Oven, BuildingType.Kiln, BuildingType.Bloomery, BuildingType.Forge, BuildingType.Market })
            Urbanism.BuildInstantly(colony.Map, colony, type);
        float[] shares = [0.20f, 0.15f, 0.10f, 0.10f, 0.15f, 0.25f, 0.05f];
        for (int i = 0; i < shares.Length; i++) colony.WorkShares[WorkSectors.All[i]] = shares[i];
        colony.AssignSectors();
        Colony partner = _world.Colonies[1];
        TradeLine[] lines = [new(ResourceType.Wood, 20, 1.5, true), new(ResourceType.Tools, 3, 6, false)];
        colony.Trades.Add(new TradeRecord(0, partner.Name, lines, 12, true));
        colony.Trades.Add(new TradeRecord(0, _world.Colonies[2].Name, [new(ResourceType.Stone, 12, 2, true)], 24, true));
        Trade.Depart(_world, new TradePlan(colony, partner, lines, 38, 12, 2));
        for (int i = 0; i < 240; i++) _world.Clock.Advance();
        if (_mode == "active")
        {
            for (int i = 0; i < 5000; i++)
            {
                _world.Step();
                if (colony.Members.Any(m => m.Activity is { Kind: ActivityKind.Craft, Started: true })) break;
            }
            for (int i = 0; i < 8; i++) _world.Step();
            Require(colony.Members.Any(m => m.Activity is { Kind: ActivityKind.Craft, Started: true }), "Aucune fabrication démarrée dans le scénario de contrôle.");
        }
    }

    public override void _Process(double delta)
    {
        try
        {
            _frame++;
            if (_frame == 3) _panel._Process(1);
            _hud.ShowShares(_world.Colonies[0]);
            if (_frame == 12)
            {
                _scroll.ScrollVertical = 120;
            }
            if (_frame == 18)
            {
                _scrollPosition = _scroll.ScrollVertical; _childId = _scroll.GetChild(0).GetInstanceId();
            }
            if (_frame == 35)
            {
                Require(_scrollPosition == _scroll.ScrollVertical && _childId == _scroll.GetChild(0).GetInstanceId(), "Le rafraîchissement déplace le défilement.");
                Colony colony = _world.Colonies[0];
                Require(_stock.All(p => colony.Stock.Get(p.Key) == p.Value) && _shares.All(p => colony.WorkShares[p.Key] == p.Value), "L'affichage a modifié la simulation.");
                if (_panel.Open)
                {
                    Require(Named<Label>(_panel, "StockValueWood").Text == colony.Stock.Get(ResourceType.Wood).ToString("N0"), "Le stock affiché est incorrect.");
                    var bar = Named<ProgressBar>(_panel, "StockNeedWood");
                    double expected = Math.Clamp(colony.Stock.Get(ResourceType.Wood) / Economy.Need(colony, ResourceType.Wood), 0, 1);
                    Require(Math.Abs(bar.Value - expected) <= 0.001, "La jauge de besoin est incorrecte.");
                    Named<Button>(_panel, "CommerceEconomie").EmitSignal(BaseButton.SignalName.Pressed);
                    Named<Button>(_panel, "StocksEconomie").EmitSignal(BaseButton.SignalName.Pressed);
                    if (_mode == "commerce") Named<Button>(_panel, "CommerceEconomie").EmitSignal(BaseButton.SignalName.Pressed);
                }
                else
                {
                    if (_mode == "active")
                    {
                        var jobs = colony.Members.Select(m => m.Activity).OfType<Activity>()
                            .Where(a => a.Kind == ActivityKind.Craft && a.Started && a.Building is not null).GroupBy(a => a.Building!.Type);
                        foreach (var group in jobs)
                        {
                            Require(Named<Label>(_hud, $"RecipeState{group.Key}").Text.Contains("en fabrication"), "La fabrication commencée n'est pas signalée.");
                            double progress = group.Average(a => Math.Clamp(a.ElapsedTicks / a.DurationTicks, 0, 1));
                            Require(Math.Abs(Named<ProgressBar>(_hud, $"RecipeProgress{group.Key}").Value - progress) <= 0.001, "La jauge de fabrication est incorrecte.");
                        }
                    }
                    else
                    {
                        Require(Named<Control>(_hud, "RecipeForge").TooltipText.Contains("3 en stock"), "La recette ne reprend pas les matières premières réelles.");
                        Require(Named<Label>(_hud, "RecipeStateForge").Text.Contains("Manque"), "Le manque de matières n'est pas signalé.");
                        Require(!Named<ProgressBar>(_hud, "RecipeProgressForge").Visible, "Un atelier arrêté affiche une fabrication.");
                    }
                    Named<Button>(_hud, "CollapseJournal").EmitSignal(BaseButton.SignalName.Pressed);
                    Require(!_scroll.Visible, "Le panneau replié reste visible.");
                    Named<Button>(_hud, "CollapseJournal").EmitSignal(BaseButton.SignalName.Pressed);
                    Require(_scroll.Visible, "Le panneau ne se déplie pas.");
                }
                _scroll.ScrollVertical = _mode switch { "recipes" or "active" => 320, "costs" => 1000, _ => 0 };
            }
            if (_frame == 50)
            {
                var panel = Named<Control>(_panel.Open ? _panel : _hud, _panel.Open ? "PanneauEconomie" : "Journal");
                Require(panel.GetGlobalRect().End.X <= GetViewportRect().Size.X && panel.GetGlobalRect().Position.Y >= InterfaceLayout.For(GetViewportRect().Size).ContentTop - 1,
                    "Le panneau déborde sur les contrôles de navigation.");
                GD.Print($"ECONOMY_PRODUCTION_UI_OK : {_mode}, {GetViewportRect().Size}, données exactes, simulation intacte, onglets et défilement stables.");
                if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok) throw new InvalidOperationException("Capture impossible.");
                GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren()) foreach (Node descendant in Descendants(child)) yield return descendant;
    }
    private static T Named<T>(Node root, string name) where T : Node => Descendants(root).OfType<T>().Single(n => n.Name == name);
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
}
