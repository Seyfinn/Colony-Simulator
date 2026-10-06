using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Nature;

namespace GodColony.View;

/// <summary>Sprites dessinés pixel par pixel, à la résolution native, comme les bêtes du village.</summary>
public static class NatureArt
{
    private static readonly Dictionary<string, ImageTexture> Cache = [];
    private static readonly Color Ink = Color.Color8(45, 53, 48), Cream = Color.Color8(245, 233, 201), Wood = Color.Color8(133, 91, 57);
    private static ImageTexture Load(string path, Func<PixelArt> source)
    {
        string key = path + AssetLibrary.NativeFallbackForValidation;
        if (!Cache.TryGetValue(key, out var texture)) Cache[key] = texture = AssetLibrary.Get(path) ?? source().Texture();
        return texture;
    }
    public static ImageTexture Animal(WildSpecies species) => Load($"animals/wild_{species.ToString().ToLowerInvariant()}_0.png", () => AnimalSource(species));
    public static ImageTexture Dog => Load("animals/dog_0.png", () => AnimalSource(WildSpecies.Wolf, dog: true));
    public static ImageTexture Cart => Load("animals/cart_0.png", CartSource);
    public static ImageTexture Bridge => Load("terrain/bridge.png", BridgeSource);

    /// <summary>Un seul tablier par ouvrage ; seuls les travaux déclarés reçoivent des planches.</summary>
    public static void DrawBridge(CanvasItem target, LocalMap map, BridgeSite site)
    {
        if (site.Cells.Count == 0) return;
        int[] cells = site.Cells.OrderBy(c => site.Horizontal ? c % map.Width : c / map.Width).ToArray();
        int mask = 0;
        for (int i = 0; i < cells.Length; i++) if (site.BuiltCells.Contains(cells[i])) mask |= 1 << i;
        string key = $"pont:{cells.Length}:{mask}:{AssetLibrary.NativeFallbackForValidation}";
        if (!Cache.TryGetValue(key, out var deck))
        {
            bool complete = site.IsComplete;
            deck = complete ? AssetLibrary.Get($"terrain/bridge_deck_{cells.Length}.png") : null;
            Cache[key] = deck ??= BridgeDeckSource(cells.Length, mask).Texture();
        }
        Vector2 at = new Vector2(cells[0] % map.Width, cells[0] / map.Width) * 32;
        target.DrawSetTransform(at + (site.Horizontal ? Vector2.Zero : new Vector2(32, 0)), site.Horizontal ? 0 : Mathf.Pi / 2);
        target.DrawTexture(deck, Vector2.Zero);
        target.DrawSetTransform(Vector2.Zero);
        for (int end = 0; end < site.Landings.Count; end++)
        {
            int cell = site.Landings[end];
            Vector2 bank = new Vector2(cell % map.Width, cell / map.Width) * 32;
            // Le raccord va du centre du sentier jusqu'à l'arête de la berge.
            target.DrawSetTransform(bank + new Vector2(16, 16), (site.Horizontal ? 0 : Mathf.Pi / 2) + (end == 0 ? 0 : Mathf.Pi));
            target.DrawTexture(Load("terrain/bridge_landing.png", BridgeLandingSource), new Vector2(-16, -16));
            target.DrawSetTransform(Vector2.Zero);
        }
    }

    internal static PixelArt BridgeDeckSource(int length, int mask)
    {
        int width = length * 32;
        var a = new PixelArt(width, 32);
        for (int cell = 0; cell < length; cell++)
        {
            int left = cell * 32;
            bool built = (mask & (1 << cell)) != 0;
            if (built)
            {
                a.Box(left, 7, 32, 19, Ink); a.Box(left, 8, 32, 16, Wood);
                for (int x = left; x < left + 32; x++)
                    if (x % 5 == 0) { a.Line(x, 8, x, 23, Ink); a.Line(x + 1, 8, x + 1, 23, ArtDirection.Brass); }
            }
            else
            {
                // Pieux et contreventements sur toute la ligne du chantier, sans faux plancher.
                a.Line(left + 4, 8, left + 27, 25, Wood); a.Line(left + 4, 25, left + 27, 8, Wood);
                a.Box(left + 12, 13, 8, 3, Cream.Darkened(.4f));
            }
            foreach (int x in new[] { left + 3, left + 27 })
                foreach (int y in new[] { 5, 24 }) { a.Box(x, y, 3, 7, Ink); a.Box(x, y, 2, 5, Wood); a.Box(x, y, 3, 1, Cream.Darkened(.25f)); }
        }
        foreach (int y in new[] { 5, 24 })
        {
            a.Box(0, y, width, 2, Ink); a.Box(0, y, width, 1, Cream.Darkened(.35f));
        }
        return a;
    }

    private static PixelArt BridgeLandingSource()
    {
        var a = new PixelArt(32, 32);
        Color stone = Color.Color8(153, 160, 141);
        a.Polygon(Ink, new(13, 11), new(24, 4), new(32, 4), new(32, 29), new(24, 29), new(13, 21));
        a.Polygon(Wood, new(14, 12), new(26, 7), new(32, 7), new(32, 24), new(26, 24), new(14, 20));
        for (int x = 16; x < 32; x += 5) a.Line(x, 11, x, 21, ArtDirection.Brass);
        foreach (int y in new[] { 3, 24 })
        {
            a.Box(24, y, 8, 6, Ink); a.Box(25, y, 7, 4, stone);
            a.Line(25, y, 31, y, Cream.Darkened(.2f)); a.Line(28, y + 1, 28, y + 3, Ink);
        }
        return a;
    }

    public static void ExportBridges()
    {
        for (int length = 1; length <= Bridges.MaxCells; length++)
            BridgeDeckSource(length, (1 << length) - 1).Image.SavePng(ProjectSettings.GlobalizePath($"res://Assets/terrain/bridge_deck_{length}.png"));
        BridgeLandingSource().Image.SavePng(ProjectSettings.GlobalizePath("res://Assets/terrain/bridge_landing.png"));
    }

    private static PixelArt AnimalSource(WildSpecies species, bool dog = false)
    {
        var a = new PixelArt(32, 32);
        bool small = species is WildSpecies.Rabbit or WildSpecies.Junglefowl;
        int ground = 30, body = small ? 24 : 20, head = small ? 23 : 25;
        Color coat = species switch
        {
            WildSpecies.Rabbit => Color.Color8(170, 140, 104), WildSpecies.Deer => Color.Color8(173, 119, 70),
            WildSpecies.Boar => Color.Color8(100, 76, 61), WildSpecies.Wolf => dog ? Color.Color8(188, 156, 109) : Color.Color8(148, 111, 101),
            WildSpecies.Bear => Color.Color8(131, 65, 47), WildSpecies.Junglefowl => Color.Color8(186, 112, 58),
            WildSpecies.Mouflon => Color.Color8(192, 171, 129), WildSpecies.Aurochs => Color.Color8(89, 68, 52),
            _ => Color.Color8(155, 94, 55),
        };
        int rx = small ? 5 : species == WildSpecies.Bear ? 10 : 9, ry = small ? 4 : species == WildSpecies.Bear ? 8 : 6;
        a.Oval(15, ground, small ? 7 : 12, 1, new Color(Ink, .2f));
        foreach (int x in small ? new[] { 13, 19 } : new[] { 8, 12, 20, 24 })
        {
            a.Box(x, body + ry - 2, 2, ground - body - ry + 2, Ink);
            a.Box(x, body + ry - 2, 1, ground - body - ry, coat.Darkened(.2f));
        }
        a.Oval(14, body, rx + 1, ry + 1, Ink); a.Oval(14, body - 1, rx, ry, coat);
        a.Oval(12, body - 3, rx - 2, Math.Max(2, ry - 2), coat.Lightened(.14f));
        int hy = small ? 21 : species is WildSpecies.Horse or WildSpecies.Deer ? 13 : 18;
        if (species is WildSpecies.Horse or WildSpecies.Deer)
        {
            a.Polygon(Ink, new(19, 21), new(20, 10), new(26, 10), new(25, 23));
            a.Polygon(coat, new(20, 20), new(21, 11), new(25, 11), new(24, 22));
        }
        a.Oval(head, hy, small ? 3 : 5, small ? 3 : 4, Ink);
        a.Oval(head, hy - 1, small ? 2 : 4, small ? 2 : 3, coat);
        a.Dot(head + 1, hy - 2, Ink); a.Dot(head + 3, hy + 1, coat.Darkened(.35f));
        switch (species)
        {
            case WildSpecies.Rabbit:
                a.Box(21, 11, 3, 9, Ink); a.Box(25, 12, 3, 8, Ink);
                a.Box(22, 12, 1, 7, Cream); a.Box(26, 13, 1, 6, Cream); a.Oval(8, 23, 2, 2, Cream); break;
            case WildSpecies.Junglefowl:
                a.Polygon(Ink, new(8, 25), new(5, 14), new(12, 20));
                a.Line(7, 17, 10, 24, Wood); a.Box(22, 16, 4, 2, Color.Color8(188, 64, 55));
                a.Box(26, 20, 3, 2, ArtDirection.Brass); a.Line(12, 23, 17, 25, Cream); break;
            case WildSpecies.Deer:
                foreach (int x in new[] { 21, 26 })
                {
                    a.Line(x, 10, x - 2, 3, Wood); a.Line(x - 1, 6, x - 5, 4, Wood); a.Line(x - 1, 6, x + 1, 2, Wood);
                }
                a.Oval(9, 20, 2, 2, Cream); break;
            case WildSpecies.Boar:
                a.Line(7, 12, 19, 13, Ink); a.Box(27, 20, 3, 3, coat.Lightened(.25f));
                a.Line(27, 23, 29, 20, Cream); a.Box(23, 11, 3, 4, Ink); break;
            case WildSpecies.Mouflon:
                a.Oval(23, 14, 4, 4, Wood); a.Oval(23, 14, 2, 2, Cream); a.Box(24, 15, 2, 3, Wood);
                foreach (int x in new[] { 8, 13, 18 }) a.Oval(x, 15, 3, 2, coat.Lightened(.2f)); break;
            case WildSpecies.Aurochs:
                a.Line(22, 14, 20, 9, Cream); a.Line(27, 14, 29, 9, Cream);
                a.Box(27, 20, 3, 2, coat.Lightened(.25f)); a.Line(3, 19, 2, 27, Ink); break;
            case WildSpecies.Horse:
                a.Line(20, 9, 19, 20, Ink); a.Box(22, 5, 2, 5, Ink); a.Box(26, 6, 2, 4, Ink);
                a.Line(5, 17, 2, 25, Ink); a.Line(2, 25, 2, 28, Ink); a.Box(27, 13, 3, 3, coat.Darkened(.2f)); break;
            case WildSpecies.Wolf:
                a.Polygon(Ink, new(21, 15), new(22, dog ? 14 : 8), new(26, 15));
                a.Polygon(coat, new(22, 14), new(23, dog ? 14 : 10), new(25, 14));
                a.Box(27, 17, 4, 3, coat); a.Line(4, 18, 1, dog ? 12 : 23, coat);
                if (dog) a.Box(22, 15, 3, 6, Wood); break;
            case WildSpecies.Bear:
                a.Oval(23, 12, 2, 2, Ink); a.Oval(28, 13, 2, 2, Ink);
                a.Box(27, 19, 3, 3, coat.Lightened(.25f)); break;
        }
        return a;
    }

    private static PixelArt CartSource()
    {
        var a = new PixelArt(32, 32);
        a.Box(5, 9, 22, 15, Ink); a.Box(6, 10, 20, 12, Wood);
        for (int row = 11; row < 22; row += 4) a.Line(7, row, 25, row, ArtDirection.Brass);
        a.Box(8, 8, 16, 4, Color.Color8(170, 132, 83));
        foreach (int x in new[] { 6, 25 }) { a.Oval(x, 24, 4, 5, Ink); a.Oval(x, 24, 2, 3, Wood); a.Dot(x, 24, Cream); }
        a.Line(12, 23, 10, 31, Wood); a.Line(20, 23, 22, 31, Wood);
        return a;
    }

    private static PixelArt BridgeSource()
    {
        var a = new PixelArt(32, 32);
        a.Box(0, 5, 32, 22, Ink); a.Box(0, 7, 32, 18, Wood);
        for (int x = 0; x < 32; x += 5) { a.Line(x, 7, x, 24, Ink); a.Line(x + 1, 8, x + 1, 23, ArtDirection.Brass); }
        foreach (int y in new[] { 5, 25 }) a.Box(0, y, 32, 2, Cream.Darkened(.4f));
        return a;
    }

    /// <summary>Exporte uniquement les nouveaux éléments demandés ; aucune image existante n'est remplacée.</summary>
    public static void Export()
    {
        void Save(Image image, string path)
        {
            string file = ProjectSettings.GlobalizePath("res://Assets/" + path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (!File.Exists(file) && image.SavePng(file) != Error.Ok) throw new IOException("Échec de l'export : " + path);
        }
        foreach (WildSpecies species in WildSpeciesInfo.All) Save(AnimalSource(species).Image, $"animals/wild_{species.ToString().ToLowerInvariant()}_0.png");
        Save(AnimalSource(WildSpecies.Wolf, dog: true).Image, "animals/dog_0.png");
        Save(CartSource().Image, "animals/cart_0.png"); Save(BridgeSource().Image, "terrain/bridge.png");
        foreach (ResourceType resource in new[] { ResourceType.Honey, ResourceType.Wax, ResourceType.Mushrooms, ResourceType.Herbs, ResourceType.Horses, ResourceType.Oxen, ResourceType.Dogs, ResourceType.Carts })
            Save(ResourceIcons.Get(resource).GetImage(), $"icons/{resource.ToString().ToLowerInvariant()}.png");
    }
}
