using Godot;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;
using Noise = GodColony.Simulation.Generation.Noise;

namespace GodColony.View;

/// <summary>
/// Dessine un arbre, un buisson ou une souche. Partagé entre les morceaux de carte (qui dessinent toute la végétation
/// d'un coup) et la vue des colons (qui redessine, dans l'ordre de profondeur, ce qui passe devant eux).
/// </summary>
public static class FloraPainter
{
    private const int Tile = TerrainPainter.TileSize;

    /// <summary>Le pied de la plante, en pixels depuis le coin de sa case : légèrement décalé pour éviter un effet de grille.</summary>
    private static Vector2 FootOffset(int x, int y)
    {
        var jitter = new Vector2(
            (Noise.Hash01(x, y, 3, 0) - 0.5f) * 16f,
            (Noise.Hash01(x, y, 4, 0) - 0.5f) * 8f);
        return (new Vector2(Tile / 2f, Tile - 6) + jitter).Round();
    }

    /// <summary>Hauteur (en pixels depuis le haut de la carte) du pied de la plante : sert à la trier avec les colons.</summary>
    public static float FootY(int x, int y) => y * Tile + FootOffset(x, y).Y;

    /// <summary>Apparence choisie selon le milieu d'origine, stable pendant la croissance et la repousse.</summary>
    public static int TreeStyleAt(LocalMap map, int x, int y) => TreeDistribution.StyleAt(map, x, y);

    /// <summary>Petites différences indépendantes du milieu et conservées à la repousse.</summary>
    public static int TreeDetailAt(LocalMap map, int x, int y) => System.Math.Min(
        (int)(Noise.Hash01(x, y, 139, map.Seed) * SpriteFactory.TreeDetailVariantCount), SpriteFactory.TreeDetailVariantCount - 1);

    /// <param name="tileOrigin">Position en pixels du coin haut gauche de la case, dans le repère où l'on dessine.</param>
    public static void Draw(CanvasItem canvas, LocalMap map, int x, int y, Vector2 tileOrigin, bool shadow)
    {
        FloraType flora = map.GetFlora(x, y);
        if (flora == FloraType.None)
            return;

        Texture2D sprite = flora switch
        {
            FloraType.Tree => SpriteFactory.TreeVariant(TreeStyleAt(map, x, y), TreeDetailAt(map, x, y)),
            FloraType.Stump => SpriteFactory.Stump,
            _ => map.GetBerries(x, y) > 0 ? SpriteFactory.Bush : SpriteFactory.BushEmpty,
        };
        float scale = flora == FloraType.Stump ? 1f : 0.3f + 0.7f * map.GetFloraGrowth(x, y);
        // Les mêmes plantes restent récoltables ; seules leurs silhouettes deviennent plus basses en toundra.
        if (flora != FloraType.Stump && map.Biome is Biome.Tundra or Biome.IceSheet) scale *= 0.72f;
        Vector2 size = sprite.GetSize() * scale;
        Vector2 bottomCenter = tileOrigin + FootOffset(x, y);
        if (shadow)
        {
            Vector2 shadowSize = new(size.X * 0.75f, flora == FloraType.Tree ? 6 * scale : 3 * scale);
            canvas.DrawRect(new Rect2(bottomCenter - new Vector2(shadowSize.X / 2, shadowSize.Y / 2) + new Vector2(3, 0), shadowSize),
                new Color(0.12f, 0.19f, 0.15f, 0.2f));
        }
        canvas.DrawTextureRect(sprite, new Rect2(bottomCenter - new Vector2(size.X / 2f, size.Y), size), false);
    }
}
