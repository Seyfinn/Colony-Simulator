using Godot;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.View;

/// <summary>Les mêmes milieux habillent le terrain, les habitants et l'architecture.</summary>
public static class BiomeVisuals
{
    public static WoodlandBiome At(LocalMap map, int x, int y) => map.InBounds(x, y)
        ? TreeDistribution.BiomeAt(map, x, y) : RegionalEnvironment(map.Biome, WoodlandBiome.TemperatePlain);

    /// <summary>Le biome du monde fixe le climat ; les berges et les hauteurs restent des milieux locaux.</summary>
    public static WoodlandBiome RegionalEnvironment(Biome region, WoodlandBiome local) => region switch
    {
        Biome.IceSheet or Biome.Tundra => WoodlandBiome.Highland,
        Biome.Desert or Biome.Steppe or Biome.Savanna => local == WoodlandBiome.WetBank
            ? local : WoodlandBiome.Dryland,
        Biome.BorealForest or Biome.TropicalForest => local is WoodlandBiome.Highland or WoodlandBiome.WetBank
            ? local : WoodlandBiome.CoolForest,
        Biome.Swamp => WoodlandBiome.WetBank,
        _ => local,
    };

    public static Color Clothing(WoodlandBiome biome, int id, Color plain)
    {
        Color color = biome switch
        {
            WoodlandBiome.Dryland => (id % 3) switch
            {
                0 => Color.Color8(219, 197, 148), 1 => Color.Color8(181, 120, 84), _ => Color.Color8(179, 157, 99),
            },
            WoodlandBiome.CoolForest => (id % 3) switch
            {
                0 => Color.Color8(91, 126, 96), 1 => Color.Color8(109, 142, 124), _ => Color.Color8(146, 132, 93),
            },
            WoodlandBiome.Highland => (id % 3) switch
            {
                0 => Color.Color8(100, 131, 153), 1 => Color.Color8(139, 129, 153), _ => Color.Color8(123, 142, 150),
            },
            WoodlandBiome.WetBank => (id % 3) switch
            {
                0 => Color.Color8(67, 141, 151), 1 => Color.Color8(125, 164, 154), _ => Color.Color8(177, 145, 87),
            },
            _ => plain,
        };
        return id % 2 == 0 ? color.Lightened(0.06f) : color;
    }

    public static void Dress(Image image, WoodlandBiome biome, int id, Color cloth, Color skin, int frame)
    {
        void Box(int x, int y, int width, int height, Color color)
        {
            for (int py = y; py < y + height; py++)
            for (int px = x; px < x + width; px++) image.SetPixel(px, py, color);
        }
        Color trim = Color.Color8(224, 205, 163), leather = Color.Color8(108, 74, 48);
        int swing = frame == 1 ? 1 : frame == 3 ? -1 : 0;
        switch (biome)
        {
            case WoodlandBiome.Dryland:
                // Chapeau de paille ou foulard de lin, chemise légère et manches roulées.
                if (id % 2 == 0)
                {
                    Box(5, 0, 6, 3, Color.Color8(205, 169, 101));
                    Box(6, 0, 4, 1, Color.Color8(236, 205, 140));
                    Box(5, 2, 6, 1, leather); Box(2, 3, 12, 1, Color.Color8(231, 194, 118));
                }
                else
                {
                    Box(4, 1, 8, 2, trim); Box(5, 0, 6, 1, trim.Lightened(0.12f));
                    Box(11, 3, 1, 4, trim.Darkened(0.15f));
                }
                foreach (int side in new[] { 0, 1 })
                {
                    int x = side == 0 ? 2 : 12, y = 13 + (side == 0 ? swing : -swing);
                    Box(x, y, 2, 1, trim); Box(x, y + 1, 2, 4, skin);
                }
                Box(6, 11, 1, 5, trim); Box(9, 11, 1, 5, cloth.Darkened(0.3f));
                break;
            case WoodlandBiome.CoolForest:
                // Cape courte, bonnet et écharpe nouée, sans masquer les mains animées.
                Box(5, 0, 6, 2, cloth.Darkened(0.18f)); Box(5, 1, 5, 1, cloth.Lightened(0.18f));
                Box(5, 9, 6, 2, Color.Color8(191, 157, 103)); Box(9, 11, 2, 3, Color.Color8(157, 119, 77));
                Box(4, 12, 1, 5, cloth.Darkened(0.35f)); Box(10, 12, 2, 6, cloth.Darkened(0.28f));
                Box(10, 12, 1, 5, cloth); Box(5, 11, 2, 1, trim);
                break;
            case WoodlandBiome.Highland:
                // Capuche bordée de laine et manteau épais fermé par une broche.
                Box(5, 0, 6, 1, cloth.Darkened(0.35f)); Box(4, 1, 8, 2, cloth);
                Box(5, 1, 6, 1, cloth.Lightened(0.2f));
                Box(3, 3, 2, 5, cloth.Darkened(0.15f)); Box(11, 3, 2, 5, cloth.Darkened(0.35f));
                Box(4, 4, 1, 4, trim); Box(11, 4, 1, 4, trim.Darkened(0.15f));
                Box(4, 10, 8, 2, trim); Box(5, 11, 6, 1, trim.Darkened(0.15f));
                Box(5, 12, 1, 5, cloth.Lightened(0.22f)); Box(10, 12, 2, 6, cloth.Darkened(0.28f));
                Box(8, 12, 1, 5, leather); image.SetPixel(8, 12, Color.Color8(231, 190, 91));
                Box(5, 17, 6, 1, trim.Darkened(0.12f));
                break;
            case WoodlandBiome.WetBank:
                // Gilet huilé, chemise rayée et cuissardes, pratiques au bord de l'eau.
                Box(5, 0, 6, 2, Color.Color8(143, 118, 73)); Box(4, 2, 8, 1, Color.Color8(209, 171, 97));
                Box(5, 11, 2, 6, leather); Box(10, 11, 1, 6, leather.Darkened(0.2f));
                foreach (int y in new[] { 12, 14, 16 }) Box(7, y, 3, 1, trim);
                for (int y = 19; y <= 22; y++)
                for (int x = 3; x <= 12; x++)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.A > 0 && c != Color.Color8(45, 43, 38)) image.SetPixel(x, y, Color.Color8(66, 79, 64));
                }
                break;
            default:
                // Tunique de travail, couture, ceinture et petite besace.
                Box(7, 11, 1, 5, cloth.Lightened(0.15f));
                image.SetPixel(8, 12, trim); image.SetPixel(8, 14, trim);
                Box(9, 16, 2, 3, leather); image.SetPixel(9, 16, trim);
                break;
        }
    }
}
