using Godot;

namespace GodColony.View;

/// <summary>
/// Fabrique de petits sprites en pixel art, dessinés par le code.
/// Ce sont des graphismes provisoires, à remplacer plus tard par de vrais dessins.
/// </summary>
public static class SpriteFactory
{
    private static ImageTexture? _tree;
    private static ImageTexture? _bush;

    /// <summary>Arbre de 16 × 24 pixels : le tronc touche le bas de la case, le feuillage déborde au-dessus.</summary>
    public static ImageTexture Tree => _tree ??= BuildTree();

    public static ImageTexture Bush => _bush ??= BuildBush();

    private static ImageTexture BuildTree()
    {
        var image = Image.CreateEmpty(16, 24, false, Image.Format.Rgba8);
        Color trunk = Color.Color8(96, 66, 40);
        Color trunkDark = Color.Color8(72, 48, 30);
        for (int y = 15; y < 24; y++)
        {
            image.SetPixel(7, y, trunk);
            image.SetPixel(8, y, trunkDark);
        }

        Color leafDark = Color.Color8(38, 84, 40);
        Color leaf = Color.Color8(52, 108, 48);
        Color leafLight = Color.Color8(78, 138, 58);
        for (int y = 0; y < 18; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = x - 7.5f, dy = y - 8.5f;
            float d = dx * dx / 56f + dy * dy / 72f;
            if (d > 1f) continue;
            // Lumière venant d'en haut à gauche, ombre en bas à droite.
            float light = -dx * 0.6f - dy * 0.8f;
            Color c = light > 3f ? leafLight : light < -3.5f || d > 0.85f ? leafDark : leaf;
            if ((x * 7 + y * 13) % 11 == 0) c = leafDark;
            image.SetPixel(x, y, c);
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildBush()
    {
        var image = Image.CreateEmpty(12, 10, false, Image.Format.Rgba8);
        Color leaf = Color.Color8(60, 112, 50);
        Color leafDark = Color.Color8(44, 88, 40);
        Color berry = Color.Color8(190, 40, 52);
        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 12; x++)
        {
            float dx = x - 5.5f, dy = y - 5f;
            if (dx * dx / 36f + dy * dy / 25f > 1f) continue;
            image.SetPixel(x, y, dy > 1.5f ? leafDark : leaf);
        }
        foreach ((int x, int y) in new[] { (3, 3), (7, 2), (5, 5), (8, 6), (2, 6) })
            image.SetPixel(x, y, berry);
        return ImageTexture.CreateFromImage(image);
    }
}
