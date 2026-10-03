using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Petites icônes en pixel art, mises en cache et utilisées par toute l'interface.</summary>
public static class ResourceIcons
{
    private static readonly Dictionary<string, ImageTexture> Cache = [];

    public static ImageTexture Get(ResourceType resource) => Get(resource.ToString());

    public static ImageTexture Get(string resource)
    {
        if (Cache.TryGetValue(resource, out var texture)) return texture;
        var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        Color dark = Color.Color8(48, 52, 45);
        switch (resource)
        {
            case "Food":
                Box(image, 3, 8, 11, 6, dark);
                Box(image, 4, 9, 9, 4, Color.Color8(178, 127, 67));
                Box(image, 4, 11, 9, 1, Color.Color8(111, 79, 45));
                foreach (int x in new[] { 4, 7, 10 })
                {
                    Box(image, x, 5, 4, 4, Color.Color8(101, 44, 48));
                    Box(image, x, 5, 3, 2, Color.Color8(212, 82, 85));
                    image.SetPixel(x, 5, Color.Color8(246, 148, 120));
                    image.SetPixel(x + 1, 4, Color.Color8(131, 168, 84));
                }
                break;
            case "Grain":
                foreach (int x in new[] { 4, 8, 12 })
                {
                    int top = x == 8 ? 1 : 3;
                    Box(image, x, top + 2, 1, 13 - top, Color.Color8(157, 116, 55));
                    for (int y = top; y < 9; y += 3)
                    {
                        Box(image, x - 2, y, 2, 2, Color.Color8(224, 177, 78));
                        Box(image, x + 1, y + 1, 2, 2, Color.Color8(248, 215, 125));
                    }
                }
                Box(image, 5, 12, 7, 2, Color.Color8(147, 79, 51));
                break;
            case "Wood":
                foreach (int y in new[] { 3, 9 })
                {
                    Box(image, 2, y, 12, 5, dark);
                    Box(image, 3, y + 1, 10, 3, Color.Color8(137, 88, 52));
                    Box(image, 4, y + 1, 6, 1, Color.Color8(193, 134, 73));
                    Box(image, 10, y + 1, 3, 3, Color.Color8(232, 185, 119));
                    image.SetPixel(11, y + 2, Color.Color8(151, 107, 63));
                }
                break;
            case "Stone":
            case "IronOre":
                for (int y = 3; y < 14; y++)
                for (int x = 2; x < 15; x++)
                {
                    if (x + y < 9 || x - y > 8 || y > 11 && x < 4) continue;
                    Color c = y < 6 ? Color.Color8(198, 205, 190)
                        : x < 7 ? Color.Color8(156, 174, 168) : Color.Color8(105, 126, 126);
                    if (y == 13 || x == 14) c = dark;
                    image.SetPixel(x, y, c);
                }
                if (resource == "IronOre")
                {
                    foreach (var p in new[] { new Vector2I(5, 6), new Vector2I(10, 8), new Vector2I(7, 11) })
                    {
                        Box(image, p.X, p.Y, 3, 2, Color.Color8(169, 91, 55));
                        image.SetPixel(p.X, p.Y, Color.Color8(228, 163, 91));
                    }
                }
                break;
            case "Charcoal":
                foreach (var p in new[] { new Vector2I(2, 9), new Vector2I(7, 4), new Vector2I(10, 10) })
                {
                    Box(image, p.X, p.Y, 4, 4, Color.Color8(40, 49, 51));
                    Box(image, p.X, p.Y, 3, 1, Color.Color8(107, 120, 113));
                    Box(image, p.X + 1, p.Y + 1, 2, 2, Color.Color8(65, 79, 78));
                }
                break;
            case "Iron":
                Box(image, 2, 7, 12, 6, Color.Color8(84, 110, 112));
                Box(image, 3, 5, 10, 3, Color.Color8(187, 209, 192));
                Box(image, 4, 4, 8, 2, Color.Color8(221, 232, 211));
                Box(image, 3, 8, 9, 2, Color.Color8(130, 163, 158));
                Box(image, 2, 12, 12, 1, Color.Color8(51, 71, 73));
                break;
            case "Tools":
                for (int i = 0; i < 10; i++) Box(image, 3 + i, 12 - i, 2, 2, Color.Color8(176, 125, 70));
                Box(image, 8, 2, 6, 4, Color.Color8(64, 91, 92));
                Box(image, 8, 2, 6, 2, Color.Color8(205, 220, 193));
                Box(image, 2, 3, 4, 3, Color.Color8(166, 187, 172));
                for (int i = 0; i < 8; i++) image.SetPixel(4 + i, 6 + i, Color.Color8(109, 87, 57));
                break;
        }
        texture = ImageTexture.CreateFromImage(image);
        Cache[resource] = texture;
        return texture;
    }

    public static string Name(ResourceType resource) => resource.ToString() switch
    {
        "Food" => "Nourriture", "Grain" => "Céréales", "Wood" => "Bois", "Stone" => "Pierre",
        "IronOre" => "Minerai de fer", "Charcoal" => "Charbon", "Iron" => "Fer", "Tools" => "Outils",
        _ => resource.ToString(),
    };

    private static void Box(Image image, int x, int y, int width, int height, Color color)
    {
        for (int py = y; py < y + height; py++)
        for (int px = x; px < x + width; px++) image.SetPixel(px, py, color);
    }
}
