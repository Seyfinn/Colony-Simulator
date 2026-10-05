using System;
using System.Reflection;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Comparaison des emprises avec les véritables vues du jeu, y compris les chantiers et les deux sens de courant.</summary>
public partial class MajorBuildingsPreview : Node2D
{
    private string? _capture;
    private int _frames = 45;
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(1600, 900);
        TextureFilter = TextureFilterEnum.Nearest;
        bool extensions = false;
        bool details = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture=")) _capture = arg[10..];
            if (arg == "--fallback") AssetLibrary.NativeFallbackForValidation = true;
            if (arg == "--extensions") extensions = true;
            if (arg == "--details") details = true;
        }
        var world = new WorldState(42, 128, 128, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        LocalMap map = colony.Map;
        MethodInfo terrain = typeof(LocalMap).GetMethod("SetGenerated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo river = typeof(LocalMap).GetMethod("SetRiver", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++) terrain.Invoke(map, [x, y, 5, SoilType.Grass, FloraType.None, 0f, 0.5f]);
        if (!extensions && !details)
        {
            for (int y = 5; y < 31; y++) river.Invoke(map, [41, y, 41, y + 1, 3]);
            for (int x = 25; x < 39; x++) river.Invoke(map, [x, 27, x + 1, 27, 3]);
        }
        colony.Buildings.Clear();

        var terrainView = new MapView(); terrainView.Init(map); AddChild(terrainView);
        var view = new ColonistsView { Alpha = 1, AmbientEffectsEnabled = false };
        view.Init(world, colony); AddChild(view);
        AddChild(new Camera2D { Position = new Vector2(details ? 26 : extensions ? 31 : 27, details ? 18 : extensions ? 21 : 20) * 32,
            Zoom = Vector2.One * (details ? 1.6f : 1.15f) });
        Building BuildingAt(BuildingType type, int x, int y, float progress = 1)
        {
            Building building = Urbanism.PlanBuilding(map, colony, type, x, y);
            Set(building, "Progress", progress);
            Set(building, "WoodDelivered", building.WoodRequired);
            Set(building, "StoneDelivered", building.StoneRequired);
            Texture2D image = BuildingSprites.For(building);
            if (image.GetWidth() != building.Width * 32 || image.GetHeight() != building.Height * 32 + 16)
                throw new InvalidOperationException("La silhouette ne suit pas l'emprise : " + type);
            var label = new Label { Text = $"{Building.NameOf(type)} · {building.Width} × {building.Height}\n{building.WoodRequired} bois · {building.StoneRequired} pierres",
                Position = new Vector2(building.X, building.Y + building.Height) * 32 + new Vector2(0, 6) };
            label.AddThemeFontSizeOverride("font_size", 13); label.AddThemeColorOverride("font_color", ArtDirection.Charcoal);
            if (!extensions) AddChild(label);
            return building;
        }
        if (details)
        {
            BuildingAt(BuildingType.MineDepot, 13, 12);
            BuildingAt(BuildingType.Pen, 24, 12);
            BuildingAt(BuildingType.Market, 34, 12);
            BuildingAt(BuildingType.MineDepot, 13, 21, .55f);
            foreach (var (type, x) in new[] { (BuildingType.Pen, 24), (BuildingType.Market, 34) })
            {
                Building principal = BuildingAt(type, x, 21);
                // La galerie expose les mêmes annexes et les mêmes textures que le jeu.
                MethodInfo agrandir = typeof(SettlementPlanner).GetMethod("PlanExtension", BindingFlags.Static | BindingFlags.NonPublic)!;
                Building mine = colony.Buildings.Find(b => b.Type == BuildingType.MineDepot && !b.IsComplete)!;
                Set(mine, "Progress", 1f);
                Building annexe = (Building?)agrandir.Invoke(null, [colony, type, principal])
                    ?? throw new InvalidOperationException("Aucune place pour l'annexe de la galerie.");
                Set(annexe, "Progress", 1f); Set(mine, "Progress", .55f);
            }
            Set(colony, "Chickens", Husbandry.Capacity(colony)); Set(colony, "Sheep", 7); Set(colony, "Cows", 4);
        }
        else if (extensions)
        {
            MethodInfo agrandir = typeof(SettlementPlanner).GetMethod("PlanExtension", BindingFlags.Static | BindingFlags.NonPublic)!;
            for (int etape = 0; etape < 3; etape++)
            foreach (BuildingType type in new[] { BuildingType.Pen, BuildingType.Market })
            {
                int x = 14 + etape * 14, y = type == BuildingType.Pen ? 13 : 26;
                Building principal = BuildingAt(type, x, y);
                int ajouts = etape == 2 ? 3 : etape;
                for (int ajout = 0; ajout < ajouts; ajout++)
                {
                    Building annexe = (Building?)agrandir.Invoke(null, [colony, type, principal])
                        ?? throw new InvalidOperationException("Aucune place pour l'extension de démonstration.");
                    Set(annexe, "Progress", etape == 1 ? .55f : 1f);
                    Set(annexe, "WoodDelivered", annexe.WoodRequired); Set(annexe, "StoneDelivered", annexe.StoneRequired);
                    if (BuildingSprites.For(annexe).GetWidth() != 64) throw new InvalidOperationException("Extension mal dimensionnée.");
                }
                // La démonstration suivante n'est pas bloquée par le chantier montré au milieu.
                foreach (Building chantier in colony.Buildings) if (!chantier.IsComplete) Set(chantier, "Progress", 1f);
                string texte = etape switch { 0 => "Bâtiment initial", 1 => "Une extension", _ => "Trois extensions" };
                var label = new Label { Text = Building.NameOf(type) + " · " + texte,
                    Position = new Vector2(x - 3, type == BuildingType.Pen ? 20 : 21) * 32 };
                label.AddThemeFontSizeOverride("font_size", 16); label.AddThemeColorOverride("font_color", ArtDirection.Charcoal); AddChild(label);
            }
            // Rétablir les deux chantiers intermédiaires après la pose des autres exemples.
            foreach (Building annexe in colony.Buildings)
                if (annexe.IsExtension && colony.Buildings.Find(b => b.Id == annexe.ExtensionOfId)?.X == 28) Set(annexe, "Progress", .55f);
            Set(colony, "Chickens", Husbandry.Capacity(colony)); Set(colony, "Sheep", 12); Set(colony, "Cows", 6);
            GD.Print("Extensions : capacités réelles et chantiers accolés validés.");
        }
        else
        {
        BuildingAt(BuildingType.Hut, 9, 10);
        BuildingAt(BuildingType.Well, 13, 11);
        BuildingAt(BuildingType.Forge, 17, 10);
        BuildingAt(BuildingType.Storehouse, 23, 9);
        BuildingAt(BuildingType.Market, 30, 9);
        BuildingAt(BuildingType.Pen, 9, 16);
        BuildingAt(BuildingType.Mill, 16, 16);
        BuildingAt(BuildingType.MineDepot, 23, 16);
        BuildingAt(BuildingType.MineDepot, 9, 24, 0.55f);
        BuildingAt(BuildingType.Dam, 41, 18);
        BuildingAt(BuildingType.Dam, 32, 27);
        }
        for (int i = 0; i < colony.Members.Count; i++)
        {
            Colonist person = colony.Members[i];
            float x = 24.5f + i % 4, y = (details ? 18f : 22.5f) + i / 4;
            Set(person, "X", x); Set(person, "PrevX", x); Set(person, "Y", y); Set(person, "PrevY", y);
        }
        var layer = new CanvasLayer(); AddChild(layer);
        var title = new Label { Text = details ? "Mines, enclos et marchés · matériaux et détails" : extensions ? "Des bâtiments qui grandissent avec le village" : "Des équipements aux grands ouvrages", Position = new Vector2(30, 18) };
        title.AddThemeFontSizeOverride("font_size", 28); title.AddThemeColorOverride("font_color", ArtDirection.Charcoal); layer.AddChild(title);
        GD.Print("Grandes emprises : silhouettes, chantiers et orientations validés.");
    }

    public override void _Process(double delta)
    {
        if (_capture is null || --_frames != 0) return;
        Error result = GetViewport().GetTexture().GetImage().SavePng(_capture);
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
