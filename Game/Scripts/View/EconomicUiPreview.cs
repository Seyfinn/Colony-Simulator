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
    private Settlement? _place;
    private Hud _hud = null!;
    private WorldPanel _panel = null!;
    private WorldMapView? _routePreview;
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
            if (_mode is "territories" or "camp" or "roads") SeedTerritorialDemonstration(colony);
            if (_mode is "camp" or "roads") for (int tick=0;tick<1000;tick++) _world.Step();
            _place = _mode is "camp" or "roads" ? colony.Settlements.Last() : colony.PrimarySettlement;
            using var local = _place.Observe();
            if (_mode == "roads") SeedRoadDemonstration(colony);
            if (_mode == "assets") { BuildAssetGallery(); return; }
            if (_mode == "routes")
            {
                _routePreview = new WorldMapView { Size = GetViewportRect().Size };
                _routePreview.Init(_world); AddChild(_routePreview);
                return;
            }
            // Le fond utilise le même peintre que le terrain en jeu.
            int x = Math.Clamp(colony.CampX - 25, 0, colony.Map.Width - 50);
            int y = Math.Clamp(colony.CampY - 15, 0, colony.Map.Height - 30);
            var texture = ImageTexture.CreateFromImage(Image.CreateFromData(1600, 960, false, Image.Format.Rgba8,
                TerrainPainter.Paint(colony.Map, x, y, 50, 30)));
            AddChild(new Sprite2D { Texture = texture, Centered = false, TextureFilter = TextureFilterEnum.Nearest });
            _hud = new Hud(); AddChild(_hud);
            _panel = new WorldPanel(); _panel.Init(_world); AddChild(_panel);
            _panel.ObservedSettlementId = _place.Id;
            if (_mode is "camp" or "roads")
            {
                var villagers = new ColonistsView { Position = new Vector2(-x * 32,-y * 32) };
                AddChild(villagers); villagers.Init(_world,colony);
            }
            _hud.SetStatus(_world.Clock, GameSpeed.Pause); _hud.ShowColony(colony, _world.Clock);
            bool economy = _mode is "economy" or "commerce" or "suppliers" or "graphs" or "territories" or "camp";
            _panel.Open = economy; _hud.SetOverlayState(false, economy);
            if (!economy && _mode != "roads") Named<Button>(_hud, "Production").EmitSignal(BaseButton.SignalName.Pressed);
            if (_mode == "roads") _hud.ToggleJournal();
            if (_mode is "commerce" or "suppliers") Named<EconomyDashboard>(_panel, "TableauEconomie").ShowCommerce(true);
            if (_mode == "territories") Named<EconomyDashboard>(_panel, "TableauEconomie").ShowTerritories();
            if (_mode == "graphs")
            {
                var history = new ResourceHistory(); history.Observe(_world);
                for (int day = 0; day < 60; day++)
                {
                    colony.Stock.Add(ResourceType.Food, 12 + day % 9);
                    colony.Stock.TryTake(ResourceType.Food, 8 + day % 6);
                    if (day % 11 == 0) colony.Stock.Add(ResourceType.Food, 20, ResourceFlow.Purchase);
                    if (day % 9 == 0) colony.Stock.TryTake(ResourceType.Food, 14, ResourceFlow.Sale);
                    for (int tick = 0; tick < TimeConstants.TicksPerDay; tick++) _world.Clock.Advance();
                    history.Observe(_world);
                }
                _panel.ResourceHistory = history; _panel.ShowResourceGraphs();
            }
            _scroll = Named<ScrollContainer>(economy ? _panel : _hud, economy ? "DefilementEconomie" : "DefilementProduction");
            _stock = Enum.GetValues<ResourceType>().ToDictionary(g => g, g => colony.Stock.Get(g));
            _shares = new Dictionary<WorkSector, float>(colony.WorkShares);
        }
        catch (Exception error) { Fail(error); }
    }

    private void SeedRoadDemonstration(Colony colony)
    {
        // Données du décor uniquement : le peintre lit ensuite la vraie couche routière, comme en jeu.
        var setter = typeof(GodColony.Simulation.Map.RoadLayer).GetMethod("SetSurface",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        void Road(int dx,int dy,RoadSurface surface)
        {
            int x=colony.CampX+dx,y=colony.CampY+dy;
            if (!colony.Map.InBounds(x,y) || !colony.Map.IsWalkable(x,y) || colony.Map.HasWater(x,y)
                || colony.Buildings.Any(b=>x>=b.X && x<b.X+b.Width && y>=b.Y && y<b.Y+b.Height)
                || colony.Fields.Any(f=>x>=f.X && x<f.X+Field.Size && y>=f.Y && y<f.Y+Field.Size)) return;
            colony.Map.ClearFlora(x,y); setter.Invoke(colony.Map.Roads,[y*colony.Map.Width+x,surface]);
        }
        for(int dx=-9;dx<=13;dx++) Road(dx,2,RoadSurface.DirtRoad);
        for(int dy=-5;dy<=7;dy++) Road(6,dy,RoadSurface.DirtRoad);
        for(int step=0;step<9;step++) Road(6+step,7+step,RoadSurface.Trail);
    }

    private void SeedTerritorialDemonstration(Colony colony)
    {
        foreach(int tile in _world.WorldMap.ClosedPassages.ToArray()) _world.WorldMap.SetPassageClosed(tile,false);
        colony.Stock.Add(ResourceType.Grain,200); colony.Stock.Add(ResourceType.Wood,80); colony.Stock.Add(ResourceType.Tools,10);
        foreach(WorkSector sector in WorkSectors.All) colony.WorkShares[sector] = sector == WorkSector.Free ? 1 : 0;
        colony.AssignSectors();
        int target = _world.WorldMap.Grid.Neighbors(colony.PrimarySettlement.RegionTileIndex)
            .First(t => _world.WorldMap.Grid[t].Habitable && !_world.Settlements.Any(s => s.RegionTileIndex == t));
        Caravan? trip = TerritorialTravel.Depart(_world,colony.PrimarySettlement,target,TerritorialPurpose.Foundation,
            new Dictionary<ResourceType,int> { [ResourceType.Wood] = 24, [ResourceType.Grain] = 24 },4);
        Require(trip is not null,"La fondation du décor n'a pas pu partir.");
        for(int hour=0;hour<240 && trip!.State != CaravanState.Home;hour++)
        { for(int tick=0;tick<TimeConstants.TicksPerHour;tick++) _world.Clock.Advance(); Trade.Hourly(_world); }
        Require(colony.Settlements.Count == 2,"Le camp de démonstration n'est pas arrivé.");
    }

    private void BuildAssetGallery()
    {
        var background = new ColorRect { Color = ArtDirection.Charcoal, Size = GetViewportRect().Size }; AddChild(background);
        var margin = new MarginContainer { Position = new Vector2(30,24), Size = GetViewportRect().Size - new Vector2(60,48), Theme = MenuStyle.Theme() };
        AddChild(margin); var content = MenuStyle.Column(margin,14);
        DashboardStyle.Text(content,"NOUVELLES FILIÈRES",22,DashboardStyle.Gold);
        DashboardStyle.Text(content,"Ressources, équipement et ateliers · rendu procédural du jeu",13,DashboardStyle.Muted);
        var goods = new GridContainer { Columns = 7 }; goods.AddThemeConstantOverride("h_separation",12); goods.AddThemeConstantOverride("v_separation",10); content.AddChild(goods);
        foreach(ResourceType good in Enum.GetValues<ResourceType>().Where(g => (int)g >= 27))
        {
            var card = DashboardStyle.Card(goods,8); card.CustomMinimumSize = new Vector2(130,72);
            var column = MenuStyle.Column(card,6); DashboardStyle.Icon(column,good,28); DashboardStyle.Text(column,ResourceIcons.Name(good),11,DashboardStyle.Ink,true);
        }
        var workshops = DashboardStyle.Row(content,28);
        foreach(BuildingType type in new[] { BuildingType.MineDepot,BuildingType.PotteryKiln,BuildingType.Tannery,BuildingType.Goldsmith })
        {
            var column = MenuStyle.Column(workshops,8); column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            DashboardStyle.Picture(column,BuildingSprites.For(new Building(type,0,0)),90);
            DashboardStyle.Text(column,Building.NameOf(type),14,DashboardStyle.Gold);
        }
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
        partner.Stock.Add(ResourceType.Wood, 100); partner.Stock.Add(ResourceType.Grain, 160);
        Caravan? meeting = Trade.Depart(_world, new TradePlan(colony, partner, lines, 38, 12, 2));
        Require(meeting is not null, "Le voyage de démonstration n'a pas pu partir.");
        for (int tick = 0; tick < 9000 && meeting!.State != CaravanState.Home; tick++)
        {
            int previousHour = _world.Clock.Hour; _world.Clock.Advance();
            if (_world.Clock.Hour != previousHour) Trade.Hourly(_world);
        }
        Require(_world.SupplierMemories.Any(m => m.Observer == colony), "Aucun renseignement rapporté.");
        Caravan? waiting = Trade.Depart(_world, new TradePlan(colony, partner, lines, 38, 12, 2));
        Require(waiting is not null, "Le second voyage n'a pas pu partir.");
        foreach (int tile in _world.WorldMap.Grid.Neighbors(_world.WorldMap.TileOf(colony)))
            _world.WorldMap.SetPassageClosed(tile, true);
        for (int tick = 0; tick < TimeConstants.TicksPerHour + 1; tick++) _world.Clock.Advance();
        Trade.Hourly(_world);
        colony.Stock.Reserve("Chantier de démonstration", ResourceType.Wood, 12, 20,
            _world.Clock.Ticks + 20 * TimeConstants.TicksPerDay, _world.Clock.Ticks);
        for (int i = 0; i < 240; i++) _world.Clock.Advance();
        if (_mode is "fermentation" or "deliveries")
        {
            // Ce décor de contrôle ne dépend pas du placement automatique encore en chantier.
            foreach (BuildingType type in new[] { BuildingType.Tavern, BuildingType.Cask })
            {
                Building building = Urbanism.BuildInstantly(colony.Map, colony, type)
                    ?? Urbanism.PlanBuilding(colony.Map, colony, type, colony.CampX + 12, colony.CampY + (type == BuildingType.Cask ? 14 : 10));
                typeof(Building).GetProperty(nameof(Building.Progress))!.SetValue(building, 1f);
                typeof(SettlementPlanner).GetMethod("OnObjectCompleted", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { colony, building });
            }
            Building cask = colony.Buildings.First(b => b.Type == BuildingType.Cask && b.IsComplete);
            double brewCost = 2;
            foreach (var input in Cuisine.BeerRecipe.Inputs)
            {
                colony.Stock.Add(input.Type, input.Amount);
                Require(colony.Stock.TryTake(input.Type, input.Amount, ResourceFlow.Usage), "Intrants du fût indisponibles.");
                brewCost += input.Amount * Economy.Cost(colony, input.Type);
            }
            Cuisine.StartBrewing(colony, cask, brewCost, _world.Clock);
            for (int tick = 0; tick < 2 * TimeConstants.TicksPerDay; tick++) _world.Clock.Advance();
            for (int tick = 0; tick < 5000; tick++)
            {
                _world.Step();
                if (colony.Members.Any(m => m.Carrying is not null && m.CarryingTo is null)) break;
            }
            Require(colony.Members.Any(m => m.Carrying is not null && m.CarryingTo is null), "Aucune livraison locale dans le scénario.");
        }
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
            using var local = _place?.Observe();
            if (_mode == "roads")
            {
                if (_frame == 40)
                {
                    Require(_stock.All(p=>_world.Colonies[0].Stock.Get(p.Key)==p.Value),"Le dessin des chemins a modifié les stocks.");
                    if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok) throw new InvalidOperationException("Capture des chemins impossible.");
                    GD.Print("VILLAGE_ROADS_UI_OK : chemins, sentiers, espaces publics et accès locaux."); GetTree().Quit();
                }
                return;
            }
            if (_mode == "assets")
            {
                if (_frame == 20)
                {
                    if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok) throw new InvalidOperationException("Capture des filières impossible.");
                    GD.Print("TERRITORIAL_ASSETS_UI_OK : 20 icônes et quatre ateliers procéduraux."); GetTree().Quit();
                }
                return;
            }
            if (_routePreview is not null)
            {
                _routePreview.QueueRedraw();
                if (_frame == 12)
                {
                    if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok)
                        throw new InvalidOperationException("Capture de carte impossible.");
                    GD.Print("COMMERCE_ROUTES_UI_OK : itinéraire physique et passages fermés.");
                    GetTree().Quit();
                }
                return;
            }
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
                    double expected = Math.Clamp(colony.Stock.Available(ResourceType.Wood) / Economy.Need(colony, ResourceType.Wood), 0, 1);
                    Require(Math.Abs(bar.Value - expected) <= 0.001, "La jauge de besoin est incorrecte.");
                    Named<Button>(_panel, "CommerceEconomie").EmitSignal(BaseButton.SignalName.Pressed);
                    Named<Button>(_panel, "StocksEconomie").EmitSignal(BaseButton.SignalName.Pressed);
                    if (_mode is "commerce" or "suppliers") Named<Button>(_panel, "CommerceEconomie").EmitSignal(BaseButton.SignalName.Pressed);
                    if (_mode == "territories") Named<EconomyDashboard>(_panel, "TableauEconomie").ShowTerritories();
                    if (_mode == "graphs")
                    {
                        _panel.ShowResourceGraphs();
                        var graph = Named<ResourceChart>(_panel, "CourbeRessources");
                        Require(graph.SampleCount == 60, "Le graphe ne reprend pas les relevés.");
                        var resource = Named<OptionButton>(_panel, "RessourceGraphique");
                        resource.Select(2); resource.EmitSignal(OptionButton.SignalName.ItemSelected, 2);
                        Require(Named<ResourceGraphs>(_panel, "GraphiquesRessources").Good == ResourceType.Wood, "Le choix de ressource est ignoré.");
                        resource.Select(0); resource.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
                        var period = Named<OptionButton>(_panel, "PeriodeGraphique");
                        period.Select(0); period.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
                        Require(graph.SampleCount == 20, "Le choix de période est ignoré.");
                        period.Select(1); period.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
                        graph._GuiInput(new InputEventMouseMotion { Position = graph.Size / 2 });
                        Named<CheckButton>(_panel, "FluxSale").ButtonPressed = false;
                        Named<CheckButton>(_panel, "FluxSale").ButtonPressed = true;
                    }
                }
                else
                {
                    if (_mode is "fermentation" or "deliveries")
                    {
                        Building cask = colony.Buildings.First(b => b.Type == BuildingType.Cask && b.IsBrewing);
                        double progress = Math.Clamp(1 - (cask.BrewReadyTicks - _world.Clock.Ticks) / (double)(Cuisine.BrewDays * TimeConstants.TicksPerDay), 0, 1);
                        Require(Named<Label>(_hud, "RecipeStateCask").Text.Contains("fût(s)"), "La fermentation n'est pas signalée.");
                        Require(Math.Abs(Named<ProgressBar>(_hud, "RecipeProgressCask").Value - progress) < 0.001, "La jauge de fermentation est incorrecte.");
                        var load = colony.Members.First(m => m.Carrying is not null && m.CarryingTo is null).Carrying!.Value;
                        Require(Named<Label>(_hud, $"LivraisonLocale{load.Type}").Text.Length > 0, "La livraison locale manque.");
                    }
                    else if (_mode == "active")
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
                _scroll.ScrollVertical = _mode switch { "recipes" or "active" => 440, "costs" => 1100,
                    "fermentation" => 900, "deliveries" => 230, "suppliers" => 300, "territories" => 90, _ => 0 };
            }
            if (_frame == 50)
            {
                if (_mode == "graphs")
                {
                    var graph = Named<ResourceChart>(_panel, "CourbeRessources");
                    Require(graph.GetGlobalRect().End.Y <= _panel.GetViewport().GetVisibleRect().Size.Y - 60,
                        "La courbe et ses axes doivent être visibles sans défilement.");
                }
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
