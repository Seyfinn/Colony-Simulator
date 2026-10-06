using System;
using System.Linq;
using System.Reflection;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Décor de validation seulement : les vues du jeu lisent des faits de simulation préparés, sans faire avancer le monde.</summary>
public partial class ScalePreview : Node2D
{
    private string? _capture;
    private int _frames = 50;
    private WorldState _world = null!;
    private string _before = "";
    internal static void Set(object target, string property, object value) => target.GetType().GetProperty(property,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(1600, 900);
        bool export = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture=")) _capture = arg[10..];
            if (arg == "--fallback") AssetLibrary.NativeFallbackForValidation = true;
            if (arg == "--export-scale") export = true;
        }
        _world = new WorldState(42, 128, 128, startingColonists: 8, colonyCount: 1, migration: false, lifecycle: false, trade: false);
        Colony colony = _world.Colonies[0]; LocalMap map = colony.Map;
        MethodInfo ground = typeof(LocalMap).GetMethod("SetGenerated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++)
            ground.Invoke(map, [x, y, 5, SoilType.Grass, FloraType.None, 0f, .5f]);
        colony.Buildings.Clear(); colony.Fields.Clear();
        Building At(BuildingType type, int x, int y, float progress = 1)
        {
            Building b = Urbanism.PlanBuilding(map, colony, type, x, y);
            Set(b, "Progress", progress); Set(b, "WoodDelivered", b.WoodRequired); Set(b, "StoneDelivered", b.StoneRequired);
            return b;
        }
        Building oven = At(BuildingType.Oven, 13, 13), mill = At(BuildingType.Mill, 24, 13);
        Building Module(Building principal, int x, float progress)
        {
            var b = new Building(principal.Type, x, principal.Y);
            Set(b, "Id", 900 + x); Set(b, "ExtensionOfId", principal.Id); Set(b, "Width", 2); Set(b, "Height", 3);
            Set(b, "Progress", progress); Set(b, "WoodDelivered", b.WoodRequired); Set(b, "StoneDelivered", b.StoneRequired);
            colony.Buildings.Add(b); return b;
        }
        Building ovenModule = Module(oven, 17, 1), millModule = Module(mill, 28, .55f);
        At(BuildingType.Mint, 13, 23); Building shrine = At(BuildingType.Shrine, 20, 23);
        At(BuildingType.Mint, 29, 23, .55f); At(BuildingType.Shrine, 35, 23, .55f);
        At(BuildingType.School, 35, 19);
        for (int i = 0; i < 5; i++)
        {
            var day = new ScaleDay(); Set(day, "Day", i); day.ServiceDenied[CivicUse.Study] = 5; day.ServiceServed[CivicUse.Study] = 1;
            colony.PrimarySettlement.ScaleLedger.Days.Add(day);
        }
        typeof(Building).GetMethod("StoreOutput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(oven, [ResourceType.Bread, 18, 12d]);
        typeof(Building).GetMethod("StoreOutput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mill, [ResourceType.Flour, 6, 5d]);
        for (int i = 0; i < 5; i++)
        {
            var monument = new Monument(); Set(monument, "Id", i + 1); Set(monument, "SettlementId", colony.PrimarySettlement.Id);
            Set(monument, "Model", (OfferingModel)i); Set(monument, "X", 12 + i * 3); Set(monument, "Y", 29);
            foreach (var material in OfferingTemplate.Of(monument.Model).Materials) monument.Materials[material.Type] = material.Amount;
            colony.Monuments.Add(monument);
        }
        var field = new Field(35, 13); Set(field, "Id", 700); colony.Fields.Add(field);
        foreach (FieldPlot plot in field.Plots) { Set(plot, "Stage", CropStage.Ripe); Set(plot, "Growth", 1f); }
        var effect = new DivineEffect(); Set(effect, "Id", 1); Set(effect, "WishId", 1); Set(effect, "Kind", DivineEffectKind.HarvestYield);
        Set(effect, "Status", DivineEffectStatus.Active); Set(effect, "TargetId", field.Id); Set(effect, "SettlementId", colony.PrimarySettlement.Id);
        Set(effect, "ExpiresTicks", _world.Clock.Ticks + 10 * TimeConstants.TicksPerDay);
        foreach (FieldPlot plot in field.Plots.Take(8))
        {
            var blessed = new BlessedPlot(); Set(blessed, "X", plot.X); Set(blessed, "Y", plot.Y);
            Set(blessed, "State", BlessedPlotState.Pending); effect.Plots.Add(blessed);
        }
        colony.DivineEffects.Add(effect);
        var champion = new DivineEffect(); Set(champion, "Id", 2); Set(champion, "Kind", DivineEffectKind.ChampionStrength);
        Set(champion, "TargetId", colony.Members[1].Id); Set(champion, "ExpiresTicks", effect.ExpiresTicks); colony.DivineEffects.Add(champion);
        for (int i = 0; i < colony.Members.Count; i++)
        {
            Colonist person = colony.Members[i]; float x = 17 + i * 2, y = 20;
            Set(person, "X", x); Set(person, "PrevX", x); Set(person, "Y", y); Set(person, "PrevY", y);
        }
        Set(colony.Members[0], "UsingCart", true); Set(colony.Members[0], "Carrying", (ResourceType.Bread, 12));
        Set(colony.Members[2], "Carrying", (ResourceType.Flour, 6));
        var pickup = new Activity(ActivityKind.CollectWorkshopOutput, oven.X, oven.Y, 30) { Building = oven, Started = true };
        Set(colony.Members[3], "Activity", pickup);
        var project = (DevelopmentProject)Activator.CreateInstance(typeof(DevelopmentProject), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [990, DevelopmentKind.RoadShortcut, DevelopmentPriority.Production, -1, _world.Clock.Ticks, "raccord de démonstration"], null)!;
        var segment = (RoadSegment)Activator.CreateInstance(typeof(RoadSegment), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [991, SegmentFunction.Link, RoadSurface.DirtRoad, 1, _world.Clock.Ticks], null)!;
        project.SegmentIds.Add(segment.Id); colony.Layout.Projects.Add(project); colony.Layout.RoadSegments.Add(segment);
        MethodInfo surface = typeof(RoadLayer).GetMethod("SetSurface", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int x = 13; x < 31; x++) { int cell = 18 * map.Width + x; segment.Cells.Add(cell); if (x < 20) surface.Invoke(map.Roads, [cell, RoadSurface.DirtRoad]); }
        if (export) Export(ovenModule, millModule, shrine);
        var oldWish = new DivineWish(); Set(oldWish, "Id", 100); Set(oldWish, "Status", DivineWishStatus.Fulfilled); Set(oldWish, "Outcome", "Ancienne bénédiction."); colony.Wishes.Add(oldWish);
        var terrain = new MapView(); terrain.Init(map); AddChild(terrain);
        var view = new ColonistsView { Alpha = 1, AmbientEffectsEnabled = false }; view.Init(_world, colony); AddChild(view);
        ValidateNotices(view, colony);
        AddChild(new Camera2D { Position = new Vector2(25, 21) * 32, Zoom = Vector2.One * 1.5f });
        var layer = new CanvasLayer(); AddChild(layer);
        var title = new Label { Text = "Ateliers et pouvoirs · sorties, modules, chantiers et monuments", Position = new Vector2(25, 18) };
        title.AddThemeFontSizeOverride("font_size", 26); title.AddThemeColorOverride("font_color", ArtDirection.Charcoal); layer.AddChild(title);
        foreach (var (text, x, y) in new[] { ("Four et module achevé · pain à livrer", 13, 17), ("Moulin · extension en chantier", 24, 17),
            ("Frappe", 13, 26), ("Sanctuaire", 20, 27), ("Chantiers", 29, 27), ("Récolte bénie : 8 parcelles", 35, 17), ("Les cinq monuments réels", 12, 31) })
        {
            var label = new Label { Text = text, Position = new Vector2(x, y) * 32 }; label.AddThemeFontSizeOverride("font_size", 11);
            label.AddThemeColorOverride("font_color", ArtDirection.Charcoal); AddChild(label);
        }
        _before = Fingerprint();
    }

    private static void Export(Building oven, Building mill, Building shrine)
    {
        foreach (WoodlandBiome biome in Enum.GetValues<WoodlandBiome>())
        {
            string suffix = biome.ToString().ToLowerInvariant();
            foreach (var (building, name) in new[] { (oven, "oven"), (mill, "mill_body"), (shrine, "shrine") })
                BuildingSprites.OuvrageSource(building, biome).Image.SavePng(ProjectSettings.GlobalizePath($"res://Assets/buildings/{name}_{suffix}_{(building.IsExtension ? "extension" : "large")}.png"));
            var mint = new PixelArt(64, 80); BuildingSprites.Mint(mint, biome);
            mint.Image.SavePng(ProjectSettings.GlobalizePath($"res://Assets/buildings/mint_{suffix}.png"));
        }
    }

    private static void ValidateNotices(ColonistsView view, Colony colony)
    {
        VillageNotices notices = view.GetChildren().OfType<VillageNotices>().Single();
        var pending = (System.Collections.Generic.Queue<(string Title, long Ticks)>)typeof(VillageNotices)
            .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notices)!;
        Label text = (Label)typeof(VillageNotices).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notices)!;
        notices._Process(0);
        if (pending.Count != 0 || text.Text.Length != 0) throw new InvalidOperationException("Une ancienne bénédiction revient en alerte.");
        for (int i = 0; i < 2; i++)
        {
            var wish = new DivineWish(); Set(wish, "Id", 101 + i); Set(wish, "Status", DivineWishStatus.Fulfilled);
            Set(wish, "Outcome", $"Nouvelle bénédiction {i + 1}."); colony.Wishes.Add(wish);
        }
        notices._Process(0);
        if (pending.Count != 1 || !text.Text.Contains("bénédiction 1")) throw new InvalidOperationException("La file perd la première alerte.");
        notices._Process(6);
        if (pending.Count != 0 || !text.Text.Contains("bénédiction 2")) throw new InvalidOperationException("La file perd la deuxième alerte.");
        notices._Process(6);
        GD.Print("DIVINE_NOTICES_OK : deux alertes successives, aucune ancienne bénédiction rejouée.");
    }

    public override void _Process(double delta)
    {
        // Les collections paresseuses des vues existantes sont initialisées lors de la première image.
        if (_frames == 45) _before = Fingerprint();
        if (--_frames != 0) return;
        if (_before != Fingerprint()) throw new InvalidOperationException("Le rendu a modifié la simulation.");
        if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok) throw new InvalidOperationException("Capture impossible.");
        GD.Print("SCALE_MAP_OK : simulation intacte, sorties, modules, chantiers, pouvoirs et monuments."); GetTree().Quit();
    }

    internal static void SeedMeasures(Colony colony, GameClock clock)
    {
        Settlement place = colony.PrimarySettlement;
        foreach (BuildingType type in new[] { BuildingType.School, BuildingType.Infirmary, BuildingType.Tavern, BuildingType.Mint, BuildingType.Shrine })
            Urbanism.BuildInstantly(colony.Map, colony, type);
        Building? oven = colony.Buildings.FirstOrDefault(b => b.Type == BuildingType.Oven && b.IsComplete);
        if (oven is null) return;
        long day = clock.TotalDays;
        place.ScaleLedger.CapacitySince[oven.Id] = day - 10;
        place.ScaleLedger.Days.Clear();
        for (int i = 0; i < 10; i++)
        {
            var measure = new ScaleDay(); Set(measure, "Day", day - 10 + i);
            measure.WorkTicks[oven.Id] = (int)((.45 + .04 * i) * WorkshopCapacity.WorkdayTicks);
            measure.Produced[ResourceType.Bread] = 10 + i; measure.FuelUsed[ResourceType.Wood] = 4;
            measure.SlotRefusals[oven.Id] = 2; measure.ServiceServed[CivicUse.Study] = 5; measure.ServiceDenied[CivicUse.Study] = 4;
            Set(measure, "CartPeak", 3); place.ScaleLedger.Days.Add(measure);
        }
        place.ScaleLedger.Verdicts[oven.Id] = ExtensionVerdict.TooLittleUse;
        Set(place.GrowthState, "HousingDays", 6); Set(place.GrowthState, "DiscontentDays", 3);
        typeof(Building).GetMethod("StoreOutput", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(oven, [ResourceType.Bread, 18, 12d]);
        foreach (DivineWishStatus status in Enum.GetValues<DivineWishStatus>())
        {
            var wish = new DivineWish(); Set(wish, "Id", (int)status + 1); Set(wish, "Status", status); Set(wish, "SettlementId", place.Id);
            Set(wish, "Description", "Autel de récolte · pierre et travail offerts.");
            Set(wish, "Outcome", status switch { DivineWishStatus.Refused => "La bénédiction a été refusée.", DivineWishStatus.TargetInvalid => "Le champ visé n'existe plus : rien n'a été béni.", DivineWishStatus.Fulfilled => "La prochaine moisson des parcelles bénies rendra un quart de plus.", DivineWishStatus.AlreadyBlessed => "Le champ était déjà béni : aucun effet ajouté.", _ => "Le joueur n'a pas encore répondu." });
            if (status is DivineWishStatus.Fulfilled or DivineWishStatus.AlreadyBlessed) Set(wish, "AcceptedTicks", (long?)clock.Ticks);
            colony.Wishes.Add(wish);
        }
        var effect = new DivineEffect(); Set(effect, "Id", 1); Set(effect, "Kind", DivineEffectKind.HarvestYield);
        Set(effect, "SettlementId", place.Id); Set(effect, "ExpiresTicks", clock.Ticks + 10 * TimeConstants.TicksPerDay);
        for (int i = 0; i < 6; i++)
        {
            var plot = new BlessedPlot(); Set(plot, "State", i < 3 ? BlessedPlotState.Pending : i == 5 ? BlessedPlotState.Invalid : BlessedPlotState.Consumed);
            Set(plot, "Gained", i is 3 or 4 ? 2 : 0); effect.Plots.Add(plot);
        }
        colony.DivineEffects.Add(effect);
    }

    private string Fingerprint()
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream);
        typeof(GodColony.Simulation.Persistence.WorldSave).Assembly.GetType("GodColony.Simulation.Persistence.StateGraph")!
            .GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [writer, _world]);
        return Convert.ToBase64String(stream.ToArray());
    }
}
