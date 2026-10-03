using System;
using GodColony.Simulation.Map;

namespace GodColony;

/// <summary>Outils de développement, utilisés seulement pour tester l'affichage.</summary>
public static class DevTools
{
    /// <summary>
    /// Creuse une carrière en gradins dans la plus haute montagne proche du centre,
    /// et renvoie la case centrale pour y placer la caméra.
    /// </summary>
    public static (int X, int Y) DigDemoQuarry(LocalMap map)
    {
        (int cx, int cy) = FindPeakNearCenter(map);
        for (int dy = -5; dy <= 5; dy++)
        for (int dx = -7; dx <= 7; dx++)
        {
            // Plus on est près du centre, plus on creuse profond : cela forme des gradins.
            int depth = 5 - Math.Max(Math.Abs(dx) * 5 / 7, Math.Abs(dy));
            int x = cx + dx, y = cy + dy;
            for (int i = 0; i < depth && map.CanMine(x, y); i++)
                map.Mine(x, y);
        }
        return (cx, cy);
    }

    private static (int, int) FindPeakNearCenter(LocalMap map)
    {
        (int, int) best = (map.Width / 2, map.Height / 2);
        float bestScore = float.MinValue;
        for (int y = 8; y < map.Height - 8; y++)
        for (int x = 8; x < map.Width - 8; x++)
        {
            if (!map.IsMountain(x, y)) continue;
            float distance = MathF.Sqrt((x - map.Width / 2f) * (x - map.Width / 2f) + (y - map.Height / 2f) * (y - map.Height / 2f));
            float score = map.GetElevation(x, y) * 10f - distance;
            if (score > bestScore)
            {
                bestScore = score;
                best = (x, y);
            }
        }
        return best;
    }
}
