using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.View;

/// <summary>Livraison et contrôle isolés de T-012/T-013, avec les peintres et le contrôle réellement utilisés en jeu.</summary>
public partial class BiomeArtPreview : Node2D
{
    private string? _capture;
    private string _mode = "atlas";
    private bool _overview;
    private int _frames = 40;
    private WorldMapView? _worldView;
    private readonly List<(LocalMap Map, ulong Before)> _maps = [];

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        string[] args = OS.GetCmdlineUserArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("--capture=")) _capture = arg[10..];
            if (arg.StartsWith("--preview=")) _mode = arg[10..];
        }
        _overview = args.Contains("--overview");
        try
        {
            if (args.Contains("--export-biomes")) Export();
            VerifyImages();
            VerifyRegionalRendering();
            if (_mode == "world")
            {
                var world = new WorldState(42, colonyCount: 4, startingColonists: 10, migration: false, lifecycle: false, trade: false);
                _worldView = new WorldMapView { Position = new Vector2(32, 24), Size = new Vector2(1536, 852), Observed = 0 };
                _worldView.Init(world);
                AddChild(_worldView);
            }
            else if (_mode == "local") BuildLocalGallery();
            else BuildAtlas();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString()); GetTree().Quit(1); SetProcess(false);
        }
    }

    private static void Export()
    {
        string root = ProjectSettings.GlobalizePath("res://Assets/world/");
        Directory.CreateDirectory(root);
        foreach (Biome biome in Enum.GetValues<Biome>())
            Save(WorldBiomeArt.Hex(biome), Path.Combine(root, $"hex_{WorldMapView.BiomeKey(biome)}.png"));
        foreach (var (relief, key) in new[] { (Relief.Hills, "hills"), (Relief.Mountains, "mountains"), (Relief.Impassable, "peaks") })
            Save(WorldBiomeArt.ReliefImage(relief), Path.Combine(root, $"relief_{key}.png"));
        AssetLibrary.Reload();
    }

    private static void Save(Image image, string path)
    {
        if (image.SavePng(path) != Error.Ok) throw new IOException("Impossible d'exporter " + path);
    }

    private static void VerifyImages()
    {
        foreach (Biome biome in Enum.GetValues<Biome>())
        {
            string key = $"world/hex_{WorldMapView.BiomeKey(biome)}.png";
            Image image = (AssetLibrary.Get(key) ?? throw new InvalidOperationException("Image absente : " + key)).GetImage();
            VerifyFrame(image);
            for (int y = 0; y < 37; y++)
            for (int x = 0; x < 32; x++)
                if (image.GetPixel(x, y).A != (WorldBiomeArt.Inside(x, y) ? 1 : 0))
                    throw new InvalidOperationException("Découpe ou opacité incorrecte : " + key);
        }
        foreach (string relief in new[] { "hills", "mountains", "peaks" })
        {
            Image image = (AssetLibrary.Get($"world/relief_{relief}.png") ?? throw new InvalidOperationException("Relief absent.")).GetImage();
            VerifyFrame(image);
            if (image.GetPixel(0, 0).A != 0 || image.GetPixel(31, 36).A != 0)
                throw new InvalidOperationException("Fond de relief non transparent.");
        }
        GD.Print("T-012 : 14 PNG RGBA8 32 × 37 chargés ; hexagones opaques, coins transparents, aucune bordure ajoutée.");
    }

    private static void VerifyFrame(Image image)
    {
        if (image.GetWidth() != 32 || image.GetHeight() != 37 || image.GetFormat() != Image.Format.Rgba8)
            throw new InvalidOperationException("Cadre d'hexagone incorrect.");
    }

    private static void VerifyRegionalRendering()
    {
        ulong? geometry = null;
        foreach (Biome biome in Enum.GetValues<Biome>())
        {
            MapStyle style = MapStyle.Temperate with { Biome = biome, RiverCount = 0 };
            LocalMap map = MapGenerator.Generate(48, 48, 77, style);
            ulong before = Fingerprint(map);
            geometry ??= before;
            if (geometry != before) throw new InvalidOperationException("Les cartes de contrôle n'ont pas la même géométrie.");
            byte[] pixels = TerrainPainter.Paint(map, 18, 18, 4, 4);
            int pines = 0, acacias = 0, trees = 0;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.GetFlora(x, y) == FloraType.Tree)
                {
                    trees++;
                    int appearance = TreeDistribution.StyleAt(map, x, y);
                    if (appearance == 2) pines++;
                    if (appearance == 4) acacias++;
                }
            if (biome == Biome.BorealForest && pines < trees * 0.9f)
                throw new InvalidOperationException("La taïga n'est pas dominée par les conifères.");
            if (biome is Biome.Desert or Biome.Savanna && acacias < trees * 0.85f)
                throw new InvalidOperationException("Les essences de climat sec sont incorrectes.");
            if (Fingerprint(map) != before) throw new InvalidOperationException("Le rendu a modifié la simulation.");
            LocalMap repeat = MapGenerator.Generate(48, 48, 77, style);
            if (!pixels.AsSpan().SequenceEqual(TerrainPainter.Paint(repeat, 18, 18, 4, 4)))
                throw new InvalidOperationException("Terrain non déterministe.");
            for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
                if (TreeDistribution.StyleAt(map, x, y) != TreeDistribution.StyleAt(repeat, x, y))
                    throw new InvalidOperationException("Essences non déterministes.");
        }
        GD.Print("T-013 : 11 biomes, rendu déterministe, géométrie/sols/flore/eau inchangés ; conifères et acacias dominants contrôlés.");
    }

    private static ulong Fingerprint(LocalMap map)
    {
        ulong hash = 14695981039346656037;
        void Add(int value) => hash = unchecked((hash ^ (uint)value) * 1099511628211);
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            Add(map.GetElevation(x, y)); Add((int)map.GetSoil(x, y)); Add((int)map.GetFlora(x, y));
            Add(BitConverter.SingleToInt32Bits(map.GetFloraGrowth(x, y)));
            Add(BitConverter.SingleToInt32Bits(map.GetMoisture(x, y)));
            Add(map.HasWater(x, y) ? 1 : 0); Add(map.RiverWidth(x, y)); Add(map.GetBerries(x, y));
        }
        return hash;
    }

    private void BuildAtlas()
    {
        LabelAt("Les biomes du monde", new Vector2(36, 18), 28, ArtDirection.Cream);
        LabelAt("Pixel art à taille native · onze sols et trois reliefs", new Vector2(36, 55), 16, ArtDirection.Sage);
        int i = 0;
        foreach (Biome biome in Enum.GetValues<Biome>())
        {
            Vector2 origin = new(40 + i % 6 * 254, 96 + i / 6 * 242);
            Texture2D texture = AssetLibrary.Get($"world/hex_{WorldMapView.BiomeKey(biome)}.png")!;
            SpriteAt(texture, origin + new Vector2(20, 36), 4);
            SpriteAt(texture, origin + new Vector2(178, 96), 1);
            SpriteAt(texture, origin + new Vector2(186, 153), 13 / 32f);
            LabelAt(BiomeInfo.Of(biome).Name, origin, 19, ArtDirection.Cream);
            i++;
        }
        LabelAt("Reliefs superposés au biome", new Vector2(40, 594), 22, ArtDirection.Brass);
        int column = 0;
        foreach (var (key, name) in new[] { ("hills", "Collines"), ("mountains", "Montagnes"), ("peaks", "Sommets enneigés") })
        {
            Vector2 origin = new(58 + column++ * 490, 654);
            Texture2D ground = AssetLibrary.Get("world/hex_grassland.png")!;
            Texture2D relief = AssetLibrary.Get($"world/relief_{key}.png")!;
            SpriteAt(ground, origin, 4); SpriteAt(relief, origin, 4);
            SpriteAt(ground, origin + new Vector2(165, 85), 1); SpriteAt(relief, origin + new Vector2(165, 85), 1);
            LabelAt(name, origin + new Vector2(215, 50), 19, ArtDirection.Cream);
        }
    }

    private void BuildLocalGallery()
    {
        LabelAt("Paysages locaux", new Vector2(32, 16), 28, ArtDirection.Cream);
        LabelAt("Neuf biomes habitables · sols et essences d'arbres", new Vector2(32, 55), 16, ArtDirection.Sage);
        WorldGrid grid = WorldGenerator.Generate(42);
        int i = 0;
        foreach (Biome biome in Enum.GetValues<Biome>().Where(b => BiomeInfo.Of(b).Habitable))
        {
            WorldTile tile = grid.Tiles.Where(t => t.Biome == biome).OrderBy(t => t.Relief).First();
            LocalMap map = MapGenerator.Generate(200, 200, 42, MapStyle.For(tile));
            _maps.Add((map, Fingerprint(map)));
            (int x, int y) = ScenicPatch(map);
            Vector2 origin = new(32 + i % 3 * 512, 96 + i / 3 * 260);
            LabelAt(BiomeInfo.Of(biome).Name, origin, 19, ArtDirection.Cream);
            var patch = new LocalPatch { Map = map, X0 = x, Y0 = y, Position = origin + new Vector2(0, 30), Size = new Vector2(480, 208), ClipContents = true };
            AddChild(patch);
            i++;
        }
    }

    private static (int X, int Y) ScenicPatch(LocalMap map)
    {
        (int X, int Y) best = (20, 20);
        float score = float.MinValue;
        int targetTrees = Math.Clamp((int)((0.32f + 1.6f * BiomeInfo.Of(map.Biome).ForestBias) * 60), 3, 36);
        for (int y = 5; y < map.Height - 10; y += 4)
        for (int x = 5; x < map.Width - 18; x += 4)
        {
            int grass = 0, dirt = 0, trees = 0, water = 0, rock = 0;
            for (int dy = 0; dy < 7; dy++)
            for (int dx = 0; dx < 15; dx++)
            {
                Surface surface = map.GetSurface(x + dx, y + dy);
                if (surface == Surface.Grass) grass++;
                if (surface == Surface.Dirt || surface == Surface.Sand) dirt++;
                if (surface == Surface.Water) water++;
                if (surface == Surface.Stone || surface == Surface.IronOre) rock++;
                if (map.GetFlora(x + dx, y + dy) == FloraType.Tree) trees++;
            }
            float candidate = Math.Min(grass, 32) * 0.5f + Math.Min(dirt, 32) * 0.5f
                + Math.Min(trees, targetTrees) * 2 - Math.Abs(trees - targetTrees) * 0.6f - rock * 0.6f
                + (map.Biome == Biome.Swamp ? Math.Min(water, 35) * 1.5f : -water * 0.5f);
            if (candidate > score) { score = candidate; best = (x, y); }
        }
        return best;
    }

    private sealed partial class LocalPatch : Control
    {
        public LocalMap Map = null!;
        public int X0, Y0;
        private ImageTexture _terrain = null!;
        public override void _Ready()
        {
            TextureFilter = TextureFilterEnum.Nearest;
            _terrain = ImageTexture.CreateFromImage(Image.CreateFromData(480, 224, false, Image.Format.Rgba8,
                TerrainPainter.Paint(Map, X0, Y0, 15, 7)));
        }
        public override void _Draw()
        {
            DrawTexture(_terrain, Vector2.Zero);
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 15; x++)
            {
                Vector2 origin = new(x * 32, y * 32);
                EnvironmentDetails.Draw(this, Map, X0 + x, Y0 + y, origin);
                FloraPainter.Draw(this, Map, X0 + x, Y0 + y, origin, shadow: true);
            }
        }
    }

    private void SpriteAt(Texture2D texture, Vector2 position, float scale) =>
        AddChild(new Sprite2D { Texture = texture, Position = position, Centered = false, Scale = Vector2.One * scale });

    private void LabelAt(string text, Vector2 position, int size, Color color)
    {
        var label = new Label { Text = text, Position = position };
        label.AddThemeFontOverride("font", ArtDirection.BodyFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        AddChild(label);
    }

    public override void _Process(double delta)
    {
        --_frames;
        if (_overview && _frames == 28 && _worldView is not null)
            for (int i = 0; i < 12; i++)
                _worldView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = _worldView.Size / 2 });
        if (_capture is null || _frames != 0) return;
        foreach (var (map, before) in _maps)
            if (Fingerprint(map) != before) { GD.PushError("L'affichage de la flore a modifié la carte."); GetTree().Quit(1); return; }
        Save(GetViewport().GetTexture().GetImage(), _capture);
        GetTree().Quit();
    }
}
