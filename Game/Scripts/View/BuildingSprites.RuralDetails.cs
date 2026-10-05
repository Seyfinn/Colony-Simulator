using Godot;

namespace GodColony.View;

public static partial class BuildingSprites
{
    /// <summary>Une terre tassée aux bords irréguliers, avec des traces groupées plutôt qu'un bruit uniforme.</summary>
    private static void CourTexturee(PixelArt a, int x, int y, int largeur, int hauteur, Color terre)
    {
        a.Polygon(terre.Darkened(.16f), new(x + 5, y + 3), new(x + largeur - 6, y),
            new(x + largeur, y + hauteur - 5), new(x + largeur - 9, y + hauteur), new(x, y + hauteur - 3));
        a.Polygon(terre, new(x + 6, y + 4), new(x + largeur - 8, y + 2),
            new(x + largeur - 2, y + hauteur - 7), new(x + 5, y + hauteur - 4));
        for (int py = y + 6; py < y + hauteur - 6; py += 5)
        for (int px = x + 7; px < x + largeur - 7; px += 7)
        {
            int grain = (px * 13 + py * 7) % 11;
            if (grain < 5) a.Box(px, py, 2 + grain % 3, 1, terre.Darkened(.07f + grain * .01f));
            else if (grain > 8) { a.Dot(px, py, terre.Lightened(.14f)); a.Dot(px + 1, py + 1, terre.Darkened(.17f)); }
        }
    }

    private static void CaisseDetaillee(PixelArt a, int x, int y, int largeur, int hauteur)
    {
        a.Box(x + 3, y + hauteur - 1, largeur, 3, C(87, 83, 65));
        a.Box(x, y, largeur, hauteur, Ink);
        a.Box(x + 1, y + 1, largeur - 2, hauteur - 2, Wood);
        a.Polygon(WoodLight, new(x, y), new(x + 4, y - 4), new(x + largeur - 1, y - 3), new(x + largeur, y + 1));
        for (int i = 4; i < largeur - 1; i += 5)
        {
            a.Line(x + i, y + 1, x + i, y + hauteur - 2, WoodLight.Darkened(.18f));
            a.Line(x + i, y, x + i + 2, y - 3, Wood);
        }
        a.Box(x + 1, y + 2, largeur - 2, 2, WoodLight);
        a.Box(x + 1, y + hauteur - 4, largeur - 2, 2, WoodLight);
        a.Line(x + 2, y + hauteur - 4, x + largeur - 3, y + 3, WoodLight);
        foreach (int dx in new[] { 2, largeur - 3 })
        {
            a.Dot(x + dx, y + 2, Ink); a.Dot(x + dx, y + hauteur - 3, Ink);
        }
    }

    /// <summary>Auvent tendu, retombée de toile et comptoir en perspective ; les accessoires décrivent le mobilier, sans compter de stock fictif.</summary>
    private static void EtalDetaille(PixelArt a, int x, int y, Color toile, int modele)
    {
        a.Oval(x + 23, y + 37, 24, 5, C(113, 108, 83));
        a.Box(x + 4, y + 18, 35, 14, Wood.Darkened(.32f));
        for (int dx = 6; dx < 39; dx += 6) a.Line(x + dx, y + 19, x + dx, y + 31, Wood);
        Beam(a, x, y + 7, 3, 31); Beam(a, x + 41, y + 9, 3, 29);
        a.Line(x + 3, y + 17, x + 11, y + 24, WoodLight);
        a.Line(x + 41, y + 19, x + 34, y + 25, WoodLight);
        a.Polygon(Ink, new(x - 3, y + 17), new(x + 5, y), new(x + 37, y + 2), new(x + 47, y + 20), new(x + 43, y + 23), new(x - 2, y + 20));
        for (int i = 0; i < 6; i++)
        {
            Color couleur = i % 2 == 0 ? toile : C(226, 211, 167);
            int haut = x + 5 + i * 32 / 6, hautSuivant = x + 5 + (i + 1) * 32 / 6;
            int bas = x - 2 + i * 47 / 6, basSuivant = x - 2 + (i + 1) * 47 / 6;
            a.Polygon(couleur, new(haut, y + 2), new(hautSuivant + 1, y + 3), new(basSuivant + 1, y + 18), new(bas, y + 17));
            a.Line(haut, y + 3, bas + 1, y + 16, couleur.Lightened(.17f));
            a.Line(hautSuivant, y + 4, basSuivant, y + 17, couleur.Darkened(.2f));
            a.Box(bas + 1, y + 18, basSuivant - bas, 3 + i % 2, couleur.Darkened(.12f));
        }
        a.Line(x + 5, y + 1, x + 36, y + 3, C(239, 219, 170));
        a.Dot(x - 1, y + 17, StoneLight); a.Dot(x + 44, y + 19, StoneLight);
        // Plateau, épaisseur du bois, pieds et rivets restent lisibles à la résolution native.
        a.Box(x, y + 29, 44, 7, Ink); a.Box(x + 1, y + 30, 42, 5, Wood);
        a.Polygon(WoodLight, new(x - 1, y + 28), new(x + 4, y + 24), new(x + 40, y + 25), new(x + 45, y + 30));
        a.Line(x + 1, y + 30, x + 43, y + 30, C(215, 174, 110));
        for (int dx = 8; dx < 41; dx += 9) a.Line(x + dx, y + 31, x + dx, y + 34, Ink);
        a.Dot(x + 3, y + 32, StoneLight); a.Dot(x + 40, y + 32, StoneLight);
        if (modele == 0)
        {
            foreach (int dx in new[] { 9, 25 })
            {
                a.Oval(x + dx, y + 26, 7, 4, Wood); a.Oval(x + dx, y + 23, 7, 3, WoodLight);
                a.Oval(x + dx, y + 23, 5, 2, C(92, 77, 52));
                for (int t = -4; t <= 4; t += 3) a.Dot(x + dx + t, y + 27, C(205, 164, 100));
            }
            a.Line(x + 34, y + 26, x + 38, y + 21, Stone); a.Line(x + 33, y + 26, x + 37, y + 27, StoneLight);
        }
        else if (modele == 1)
        {
            foreach (int dx in new[] { 8, 20 })
            {
                a.Box(x + dx, y + 22, 10, 5, C(165, 103, 83));
                a.Box(x + dx, y + 22, 10, 1, C(213, 155, 117));
                a.Line(x + dx + 2, y + 25, x + dx + 8, y + 25, C(119, 81, 70));
            }
            a.Oval(x + 35, y + 23, 4, 5, StoneLight); a.Oval(x + 35, y + 23, 2, 3, Wood);
        }
        else
        {
            a.Line(x + 22, y + 17, x + 22, y + 28, Mortar);
            a.Line(x + 12, y + 19, x + 31, y + 20, Brass);
            a.Line(x + 12, y + 20, x + 10, y + 25, Mortar); a.Line(x + 31, y + 21, x + 33, y + 26, Mortar);
            a.Oval(x + 10, y + 25, 5, 2, Brass); a.Oval(x + 33, y + 26, 5, 2, Brass);
            a.Box(x + 18, y + 27, 8, 2, Wood);
        }
    }
}
