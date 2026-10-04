using Godot;

namespace GodColony.View;

public static partial class BuildingSprites
{
    /// <summary>Mêmes silhouettes et points d'attache que le dessin natif, patine assortie au village.</summary>
    internal static PixelArt WorkshopSource(string kind, WoodlandBiome biome = WoodlandBiome.TemperatePlain)
    {
        bool dam = kind is "Dam" or "DamSide";
        var a = new PixelArt(dam ? 32 : 64, dam ? 48 : 80);
        switch (kind)
        {
            case "Hut": Cottage(a, biome); break;
            case "Kiln": Kiln(a, biome); break;
            case "Bloomery": Bloomery(a, biome); break;
            case "Forge": Forge(a, biome); break;
            case "Mill": Mill(a, biome); break;
            case "Oven": Bakery(a, biome); break;
            case "Dam": Dam(a); break;
            case "DamSide": SideDam(a); break;
        }
        if (!dam)
        {
            // La même assise que T-017 ; la roue reste intégralement dans son calque indépendant.
            for (int x = 9; x < (kind == "Mill" ? 42 : 57); x += 7) { a.Box(x, 76, 5, 2, StoneLight); a.Dot(x + 5, 77, Mortar); }
            if (kind == "Hut") { a.Box(4, 68, 7, 5, Wood); a.Box(5, 67, 5, 2, C(115, 132, 113)); a.Dot(7, 66, C(183, 69, 63)); }
            // Les petites facettes claires sont des pixels entiers, jamais du bruit lissé.
            for (int y = 20; y < 70; y++) for (int x = 3; x < 61; x++)
            {
                Color pixel = a.Image.GetPixel(x, y);
                if (pixel.A == 1 && pixel == Wood && (x * 11 + y * 7) % 53 == 0) a.Dot(x, y, Wood.Lightened(0.12f));
            }
        }
        return a;
    }

    internal static PixelArt CaskSource(string state)
    {
        var a = new PixelArt(64, 80);
        a.Box(4, 70, 56, 7, Ink); a.Box(5, 70, 54, 6, Wood);
        for (int y = 70; y < 76; y += 2) a.Line(5, y, 58, y, WoodLight);
        foreach (var (cx, cy, rx, ry) in new[] {(22, 56, 15, 17), (47, 62, 12, 13)})
        {
            a.Oval(cx, cy, rx, ry, Ink); a.Oval(cx, cy - 1, rx - 1, ry - 1, Wood);
            a.Oval(cx - 3, cy - 2, rx - 6, ry - 6, WoodLight);
            for (int x = cx - rx + 4; x < cx + rx - 2; x += 5) a.Line(x, cy - ry + 7, x, cy + ry - 3, Wood.Darkened(0.18f));
            foreach (int offset in new[] {-ry / 2, ry / 2}) { a.Box(cx - rx + 2, cy + offset, 2 * rx - 3, 3, Soot); a.Line(cx - rx + 3, cy + offset, cx + rx - 3, cy + offset, Stone); a.Dot(cx - rx + 5, cy + offset + 1, Brass); }
            a.Oval(cx, cy - ry + 3, rx - 3, 3, Ink); a.Oval(cx, cy - ry + 2, rx - 4, 2, WoodLight); a.Line(cx - rx + 5, cy - ry + 2, cx + rx - 5, cy - ry + 2, Wood);
        }
        // L'ouverture du fût principal reste visible quand il ne contient aucune fournée.
        a.Oval(22, 41, 3, 2, Ink);
        if (state == "empty") { a.Oval(22, 41, 2, 1, Soot); a.Box(30, 73, 4, 2, WoodLight); }
        else { a.Oval(22, 40, 2, 1, Wood); a.Dot(21, 39, WoodLight); }
        if (state == "brewing") { a.Box(42, 61, 6, 5, CreamColor); a.Box(44, 62, 2, 3, Brass); }
        if (state == "ready")
        {
            a.Box(34, 62, 7, 2, Brass); a.Box(39, 63, 2, 5, Brass); a.Box(36, 59, 2, 4, Soot); a.Line(34, 59, 39, 59, StoneLight);
            a.Box(49, 68, 8, 7, Ink); a.Box(50, 69, 6, 5, Brass); a.Box(50, 69, 2, 4, C(241, 204, 109)); a.Box(57, 70, 3, 4, Ink); a.Box(58, 71, 1, 2, Brass); a.Box(49, 67, 8, 3, CreamColor);
        }
        return a;
    }
}
