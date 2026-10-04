using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.View;

/// <summary>Contrôle isolé des fleuves : peinture réelle, parallélisme, limites des morceaux et mise à jour de l'eau.</summary>
public partial class RiverbankPreview : Node2D
{
    private string? _capture;
    private int _frames = 30;
    private readonly List<(LocalMap Map, ulong Before)> _maps = [];
    private const int Wide = 22, High = 10;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--capture=")) _capture = arg[10..];
        try
        {
            RiverTiles.Preload();
            LabelAt("Berges des grands fleuves", new Vector2(32, 14), 27);
            LabelAt("Sable humide, eau peu profonde, galets et végétation selon le climat", new Vector2(32, 53), 16);
            int i = 0;
            foreach (Biome biome in new[] { Biome.Grassland, Biome.Desert, Biome.BorealForest, Biome.Swamp })
            {
                BiomeInfo info = BiomeInfo.Of(biome);
                MapStyle style = MapStyle.Temperate with { Biome = biome, ForestBias = info.ForestBias, DryShare = info.DryShare };
                LocalMap map = MapGenerator.Generate(200, 200, 42, style);
                var (x, y) = FindBank(map);
                VerifyPaint(map, x, y);
                _maps.Add((map, Fingerprint(map)));
                Vector2 origin = new(32 + i % 2 * 784, 98 + i / 2 * 386);
                LabelAt(info.Name, origin, 20);
                AddChild(new Patch { Map = map, X0 = x, Y0 = y, Position = origin + new Vector2(0, 34),
                    Size = new Vector2(Wide * 32, High * 32), ClipContents = true });
                if (i == 0) VerifyRefresh(MapGenerator.Generate(200, 200, 42, style), x, y);
                i++;
            }
            GD.Print("RIVERBANKS_OK : déterminisme, peinture parallèle, raccords et repeinture après eau/canal ; données inchangées.");
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); SetProcess(false); }
    }

    private static (int X, int Y) FindBank(LocalMap map)
    {
        var best = (X: 8, Y: 8);
        int bestScore = -1;
        for (int y = 4; y < map.Height - High - 4; y += 3)
        for (int x = 4; x < map.Width - Wide - 4; x += 3)
        {
            int water = 0, bank = 0, trees = 0;
            for (int dy = 0; dy < High; dy++)
            for (int dx = 0; dx < Wide; dx++)
            {
                int ax = x + dx, ay = y + dy;
                if (map.IsWideRiver(ax, ay)) water++;
                else if (!map.HasWater(ax, ay) && !map.IsMountain(ax, ay)
                    && (map.IsWideRiver(ax - 1, ay) || map.IsWideRiver(ax + 1, ay)
                        || map.IsWideRiver(ax, ay - 1) || map.IsWideRiver(ax, ay + 1))) bank++;
                if (map.GetFlora(ax, ay) == FloraType.Tree) trees++;
            }
            int score = bank * 8 + Math.Min(water, 65) - Math.Abs(water - 65) - trees;
            if (water > 20 && bank > 8 && score > bestScore) { bestScore = score; best = (x, y); }
        }
        if (bestScore < 0) throw new InvalidOperationException("Aucune berge de fleuve dans la carte de contrôle.");
        return best;
    }

    private static void VerifyPaint(LocalMap map, int x, int y)
    {
        ulong before = Fingerprint(map);
        byte[] full = TerrainPainter.Paint(map, x, y, Wide, High);
        byte[] repeat = TerrainPainter.Paint(map, x, y, Wide, High);
        if (!full.AsSpan().SequenceEqual(repeat)) throw new InvalidOperationException("Peinture non déterministe.");
        var halves = new byte[2][];
        Parallel.For(0, 2, i => halves[i] = TerrainPainter.Paint(map, x + i * (Wide / 2), y, Wide / 2, High));
        for (int row = 0; row < High * 32; row++)
        for (int i = 0; i < 2; i++)
            if (!full.AsSpan((row * Wide * 32 + i * Wide / 2 * 32) * 4, Wide / 2 * 32 * 4)
                .SequenceEqual(halves[i].AsSpan(row * Wide / 2 * 32 * 4, Wide / 2 * 32 * 4)))
                throw new InvalidOperationException("Raccord de morceau ou peinture parallèle incorrect.");
        if (before != Fingerprint(map)) throw new InvalidOperationException("La peinture a modifié les données.");
    }

    private static void VerifyRefresh(LocalMap map, int x, int y)
    {
        byte[] partial = TerrainPainter.Paint(map, x, y, Wide, High);
        var touched = new HashSet<(int X, int Y)>();
        map.TileChanged += (ax, ay) =>
        {
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (ax + dx >= x && ax + dx < x + Wide && ay + dy >= y && ay + dy < y + High)
                    touched.Add((ax + dx, ay + dy));
        };
        var banks = new List<(int X, int Y)>();
        for (int ay = y + 2; ay < y + High - 2; ay++)
        for (int ax = x + 2; ax < x + Wide - 2; ax++)
            if (!map.HasWater(ax, ay) && !map.IsMountain(ax, ay) &&
                (map.IsWideRiver(ax - 1, ay) || map.IsWideRiver(ax + 1, ay))) banks.Add((ax, ay));
        if (banks.Count < 2) throw new InvalidOperationException("Pas assez de berges pour vérifier la mise à jour.");
        foreach (var bank in banks.Take(2)) map.Flood([bank], map.GetElevation(bank.X, bank.Y) + 1);
        var canal = banks[^1];
        map.DigCanal(canal.X, canal.Y); map.FillCanal(canal.X, canal.Y);
        foreach (var tile in touched)
            TerrainPainter.PaintTile(map, tile.X, tile.Y, partial, Wide * 32, (tile.X - x) * 32, (tile.Y - y) * 32);
        if (!partial.AsSpan().SequenceEqual(TerrainPainter.Paint(map, x, y, Wide, High)))
            throw new InvalidOperationException("La repeinture locale laisse une ancienne berge.");
    }

    private static ulong Fingerprint(LocalMap map)
    {
        ulong hash = 14695981039346656037;
        void Add(int value) => hash = unchecked((hash ^ (uint)value) * 1099511628211);
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            Add(map.GetElevation(x, y)); Add((int)map.GetSoil(x, y)); Add((int)map.GetFlora(x, y));
            Add(BitConverter.SingleToInt32Bits(map.GetFloraGrowth(x, y))); Add(BitConverter.SingleToInt32Bits(map.GetMoisture(x, y)));
            Add(map.HasWater(x, y) ? 1 : 0); Add(map.IsCanal(x, y) ? 1 : 0); Add(map.IsCanalWet(x, y) ? 1 : 0);
            Add(map.RiverWidth(x, y)); Add(map.GetBerries(x, y)); Add(map.GetFish(x, y));
            Add(BitConverter.SingleToInt32Bits(map.GetFlow(x, y)));
        }
        return hash;
    }

    private sealed partial class Patch : Control
    {
        public LocalMap Map = null!;
        public int X0, Y0;
        private ImageTexture _terrain = null!;
        public override void _Ready()
        {
            TextureFilter = TextureFilterEnum.Nearest;
            _terrain = ImageTexture.CreateFromImage(Image.CreateFromData(Wide * 32, High * 32, false, Image.Format.Rgba8,
                TerrainPainter.Paint(Map, X0, Y0, Wide, High)));
        }
        public override void _Draw()
        {
            DrawTexture(_terrain, Vector2.Zero);
            for (int y = 0; y < High; y++)
            for (int x = 0; x < Wide; x++)
            {
                Vector2 origin = new(x * 32, y * 32);
                EnvironmentDetails.Draw(this, Map, X0 + x, Y0 + y, origin);
                FloraPainter.Draw(this, Map, X0 + x, Y0 + y, origin, shadow: true);
            }
        }
    }

    private void LabelAt(string text, Vector2 origin, int size)
    {
        var label = new Label { Text = text, Position = origin };
        label.AddThemeFontOverride("font", ArtDirection.BodyFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", ArtDirection.Cream);
        AddChild(label);
    }

    public override void _Process(double delta)
    {
        if (--_frames != 0) return;
        foreach (var (map, before) in _maps)
            if (before != Fingerprint(map)) { GD.PushError("Le décor a modifié les données."); GetTree().Quit(1); return; }
        if (_capture is not null && GetViewport().GetTexture().GetImage().SavePng(_capture) != Error.Ok)
        { GD.PushError("Capture impossible."); GetTree().Quit(1); return; }
        GetTree().Quit();
    }
}
