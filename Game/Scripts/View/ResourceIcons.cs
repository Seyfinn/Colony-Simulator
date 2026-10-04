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

        // Une icône fournie dans Game/Assets/icons/ prime sur le dessin en code (voir le cahier des charges).
        if (AssetLibrary.Get($"icons/{resource.ToLowerInvariant()}.png") is { } provided)
            return Cache[resource] = provided;

        if (System.Array.IndexOf(WorkshopArt.Goods, resource) >= 0)
            return Cache[resource] = WorkshopArt.Icon(resource);

        if (System.Array.IndexOf(VillageArt.Goods, resource) >= 0 || resource == "Milestone")
            return Cache[resource] = VillageArt.Icon(resource);

        var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        Color dark = Color.Color8(48, 52, 45);
        switch (resource)
        {
            case "Fish":
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
            case "Flour":
                // Un sac de farine noué.
                Box(image, 3, 5, 10, 9, Color.Color8(197, 178, 140));
                Box(image, 4, 6, 8, 7, Color.Color8(238, 230, 205));
                Box(image, 5, 3, 6, 3, Color.Color8(197, 178, 140));
                Box(image, 5, 5, 6, 1, Color.Color8(130, 98, 62));
                Box(image, 6, 9, 4, 1, Color.Color8(209, 198, 168));
                break;
            case "Bread":
                // Un pain rond bien doré.
                Oval16(image, 8, 9, 6, 4, Color.Color8(116, 70, 41));
                Oval16(image, 8, 8, 5, 4, Color.Color8(199, 134, 70));
                Box(image, 5, 6, 2, 1, Color.Color8(240, 190, 115));
                Box(image, 8, 5, 2, 1, Color.Color8(240, 190, 115));
                Box(image, 10, 7, 2, 1, Color.Color8(240, 190, 115));
                break;
            case "Coins":
                foreach (var p in new[] { new Vector2I(3, 9), new Vector2I(8, 8), new Vector2I(5, 4) })
                {
                    Box(image, p.X, p.Y, 6, 4, Color.Color8(124, 90, 38));
                    Box(image, p.X, p.Y, 6, 3, Color.Color8(226, 190, 90));
                    Box(image, p.X + 1, p.Y, 3, 1, Color.Color8(250, 232, 156));
                }
                break;
            case "Eggs":
                Box(image, 2, 10, 12, 4, Color.Color8(147, 109, 62));
                Box(image, 3, 11, 10, 2, Color.Color8(196, 158, 92));
                foreach (int x in new[] { 4, 8, 11 })
                {
                    Oval16(image, x, 8, 2, 3, Color.Color8(197, 176, 140));
                    Oval16(image, x, 7, 2, 3, Color.Color8(246, 236, 210));
                    image.SetPixel(x - 1, 6, Color.Color8(255, 252, 240));
                }
                break;
            case "Wool":
                Oval16(image, 8, 9, 6, 5, Color.Color8(168, 163, 150));
                foreach (var tuft in new[] { new Vector2I(5, 7), new Vector2I(9, 6), new Vector2I(7, 9), new Vector2I(11, 9), new Vector2I(4, 10) })
                    Oval16(image, tuft.X, tuft.Y, 3, 3, Color.Color8(245, 242, 230));
                Box(image, 6, 4, 3, 1, Color.Color8(255, 253, 244));
                break;
            case "Clothes":
                Box(image, 5, 3, 6, 11, dark);
                Box(image, 6, 4, 4, 9, Color.Color8(176, 74, 62));
                Box(image, 1, 4, 5, 4, dark); Box(image, 10, 4, 5, 4, dark);
                Box(image, 2, 5, 4, 2, Color.Color8(176, 74, 62)); Box(image, 10, 5, 4, 2, Color.Color8(176, 74, 62));
                Box(image, 7, 3, 2, 1, Color.Color8(236, 220, 190));
                Box(image, 6, 10, 4, 1, Color.Color8(226, 190, 90));
                break;
            case "Salt":
                Oval16(image, 8, 11, 6, 3, Color.Color8(150, 165, 176));
                Oval16(image, 8, 10, 5, 3, Color.Color8(238, 242, 244));
                foreach (var crystal in new[] { new Vector2I(6, 6), new Vector2I(9, 5), new Vector2I(8, 8) })
                {
                    Box(image, crystal.X, crystal.Y, 3, 3, Color.Color8(214, 225, 232));
                    Box(image, crystal.X, crystal.Y, 2, 2, Color.Color8(252, 253, 255));
                }
                break;
            case "Spices":
                foreach (var (bowl, tint) in new[] { (3, Color.Color8(196, 62, 44)), (8, Color.Color8(222, 150, 48)), (12, Color.Color8(206, 176, 62)) })
                {
                    Box(image, bowl - 2, 10, 5, 4, dark);
                    Box(image, bowl - 1, 10, 3, 3, Color.Color8(138, 96, 60));
                    Oval16(image, bowl, 9, 2, 2, tint);
                    image.SetPixel(bowl - 1, 8, Color.Color8(255, 236, 190));
                }
                break;
            case "Hardwood":
                foreach (int y in new[] { 3, 9 })
                {
                    Box(image, 2, y, 12, 5, dark);
                    Box(image, 3, y + 1, 10, 3, Color.Color8(112, 58, 40));
                    Box(image, 4, y + 1, 6, 1, Color.Color8(160, 92, 58));
                    Box(image, 10, y + 1, 3, 3, Color.Color8(196, 126, 84));
                    image.SetPixel(11, y + 2, Color.Color8(128, 70, 48));
                }
                break;
            case "Milk":
                Box(image, 5, 2, 6, 3, dark); Box(image, 6, 3, 4, 1, Color.Color8(176, 110, 70));
                Box(image, 4, 5, 8, 9, dark); Box(image, 5, 5, 6, 8, Color.Color8(246, 248, 244));
                Box(image, 5, 5, 2, 8, Color.Color8(255, 255, 255)); Box(image, 9, 5, 2, 8, Color.Color8(206, 218, 222));
                Box(image, 5, 9, 6, 2, Color.Color8(133, 171, 200));
                break;
            case "Chickens":
                Oval16(image, 7, 9, 5, 4, Color.Color8(166, 92, 54)); Oval16(image, 6, 8, 3, 2, Color.Color8(214, 142, 86));
                Box(image, 2, 6, 2, 4, Color.Color8(98, 58, 40));
                Box(image, 10, 4, 4, 4, Color.Color8(166, 92, 54)); Box(image, 10, 3, 2, 1, Color.Color8(206, 56, 52));
                Box(image, 13, 6, 2, 1, Color.Color8(232, 176, 64)); image.SetPixel(12, 5, dark);
                Box(image, 6, 13, 1, 2, Color.Color8(214, 168, 64)); Box(image, 9, 13, 1, 2, Color.Color8(214, 168, 64));
                break;
            case "Sheep":
                Box(image, 4, 12, 1, 3, dark); Box(image, 10, 12, 1, 3, dark);
                Oval16(image, 7, 8, 5, 4, Color.Color8(176, 172, 158));
                foreach (var tuft in new[] { new Vector2I(4, 7), new Vector2I(7, 6), new Vector2I(10, 8), new Vector2I(6, 10) })
                    Oval16(image, tuft.X, tuft.Y, 3, 3, Color.Color8(244, 241, 230));
                Box(image, 11, 6, 4, 5, dark); Box(image, 12, 7, 3, 3, Color.Color8(78, 70, 64));
                break;
            case "Cows":
                Box(image, 2, 6, 10, 6, dark); Box(image, 3, 7, 8, 4, Color.Color8(244, 240, 230));
                Box(image, 4, 7, 3, 3, Color.Color8(52, 52, 56)); Box(image, 8, 9, 3, 2, Color.Color8(52, 52, 56));
                Box(image, 11, 4, 4, 5, dark); Box(image, 12, 5, 3, 3, Color.Color8(244, 240, 230));
                Box(image, 13, 7, 2, 2, Color.Color8(222, 150, 150));
                image.SetPixel(11, 3, Color.Color8(230, 214, 170)); image.SetPixel(14, 3, Color.Color8(230, 214, 170));
                foreach (int x in new[] { 3, 6, 9, 11 }) Box(image, x, 12, 1, 3, dark);
                break;
            case "Meat":
            case "SaltedMeat":
                // Un jambon : chair rosée, couenne sombre, os clair ; la viande salée est brunie et piquetée de cristaux.
                Oval16(image, 7, 9, 6, 4, resource == "Meat" ? Color.Color8(128, 52, 48) : Color.Color8(112, 66, 52));
                Oval16(image, 6, 8, 4, 3, resource == "Meat" ? Color.Color8(206, 98, 88) : Color.Color8(168, 102, 78));
                Box(image, 11, 6, 3, 2, Color.Color8(238, 226, 200)); Box(image, 13, 4, 2, 4, Color.Color8(238, 226, 200));
                Box(image, 4, 6, 3, 1, Color.Color8(238, 160, 150));
                if (resource == "SaltedMeat")
                    foreach (var crystal in new[] { new Vector2I(4, 9), new Vector2I(7, 7), new Vector2I(8, 11), new Vector2I(10, 9), new Vector2I(5, 12) })
                        image.SetPixel(crystal.X, crystal.Y, Color.Color8(250, 252, 255));
                break;
            case "Beer":
                // Une chope : bière ambrée, mousse crémeuse, anse sombre.
                Box(image, 3, 4, 8, 10, dark); Box(image, 4, 6, 6, 7, Color.Color8(226, 168, 52));
                Box(image, 4, 4, 6, 3, Color.Color8(252, 244, 222)); Box(image, 3, 3, 3, 2, Color.Color8(252, 244, 222));
                Box(image, 4, 8, 2, 5, Color.Color8(246, 208, 108));
                Box(image, 11, 6, 3, 1, dark); Box(image, 13, 6, 1, 5, dark); Box(image, 11, 10, 3, 1, dark);
                break;
            case "Cake":
                Box(image, 3, 10, 10, 4, dark); Box(image, 4, 10, 8, 3, Color.Color8(214, 158, 98));
                Box(image, 3, 7, 10, 3, dark); Box(image, 4, 7, 8, 2, Color.Color8(250, 236, 236));
                Box(image, 4, 9, 8, 1, Color.Color8(240, 150, 170));
                Box(image, 8, 4, 1, 3, Color.Color8(238, 226, 140)); image.SetPixel(8, 3, Color.Color8(255, 170, 60));
                image.SetPixel(5, 6, Color.Color8(196, 52, 60)); image.SetPixel(11, 6, Color.Color8(196, 52, 60));
                break;
            case "Stew":
                Box(image, 2, 8, 12, 6, dark); Box(image, 3, 9, 10, 4, Color.Color8(130, 92, 64));
                Box(image, 3, 7, 10, 2, Color.Color8(168, 92, 52));
                foreach (var chunk in new[] { new Vector2I(4, 7), new Vector2I(8, 7), new Vector2I(11, 8) })
                    Box(image, chunk.X, chunk.Y, 2, 1, Color.Color8(228, 168, 108));
                image.SetPixel(6, 4, Color.Color8(222, 226, 222)); image.SetPixel(9, 3, Color.Color8(222, 226, 222)); image.SetPixel(7, 2, Color.Color8(222, 226, 222));
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
        "Food" => "Nourriture", "Fish" => "Poisson", "Grain" => "Céréales", "Wood" => "Bois", "Stone" => "Pierre",
        "IronOre" => "Minerai de fer", "Charcoal" => "Charbon", "Iron" => "Fer", "Tools" => "Outils",
        "Flour" => "Farine", "Bread" => "Pain", "Coins" => "Pièces",
        "Eggs" => "Œufs", "Milk" => "Lait", "Meat" => "Viande", "SaltedMeat" => "Viande salée", "Cake" => "Gâteau", "Beer" => "Bière", "Stew" => "Ragoût", "Chickens" => "Poules", "Sheep" => "Moutons", "Cows" => "Vaches", "Wool" => "Laine", "Clothes" => "Vêtements", "Salt" => "Sel", "Spices" => "Épices", "Hardwood" => "Bois dur",
        _ => resource.ToString(),
    };

    private static void Oval16(Image image, int cx, int cy, int rx, int ry, Color color)
    {
        for (int y = cy - ry; y <= cy + ry; y++)
        for (int x = cx - rx; x <= cx + rx; x++)
        {
            float dx = (x - cx) / (float)rx, dy = (y - cy) / (float)ry;
            if (dx * dx + dy * dy <= 1f && x >= 0 && y >= 0 && x < 16 && y < 16)
                image.SetPixel(x, y, color);
        }
    }

    private static void Box(Image image, int x, int y, int width, int height, Color color)
    {
        for (int py = y; py < y + height; py++)
        for (int px = x; px < x + width; px++) image.SetPixel(px, py, color);
    }
}
