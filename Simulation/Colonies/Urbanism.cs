using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'urbanisme de la colonie : où placer ses bâtiments. Pour l'instant, des huttes en anneau autour du feu,
/// sur un terrain plat et dégagé, en laissant des passages entre elles.
/// </summary>
public static class Urbanism
{
    private const int MinDistanceFromFire = 3;
    private const int MaxDistanceFromFire = 14;

    /// <summary>Renvoie la case en haut à gauche d'un emplacement libre pour une hutte de 2 × 2, ou null s'il n'y en a pas.</summary>
    public static (int X, int Y)? FindHutSite(LocalMap map, Colony colony) => FindSite(map, colony, MinDistanceFromFire);

    /// <summary>Un atelier se tient un peu plus loin du feu que les huttes : fumée et étincelles.</summary>
    public static (int X, int Y)? FindWorkshopSite(LocalMap map, Colony colony) => FindSite(map, colony, WorkshopMinDistance);

    private const int WorkshopMinDistance = 5;

    private static (int X, int Y)? FindSite(LocalMap map, Colony colony, int minDistance)
    {
        (int X, int Y)? best = null;
        float bestScore = float.MaxValue;

        for (int dy = -MaxDistanceFromFire; dy <= MaxDistanceFromFire; dy++)
        for (int dx = -MaxDistanceFromFire; dx <= MaxDistanceFromFire; dx++)
        {
            int x = colony.CampX + dx, y = colony.CampY + dy;
            if (!IsBuildable(map, colony, x, y))
                continue;

            // Au plus près du feu, sans empiéter sur le cercle où l'on mange et se détend.
            float distance = MathF.Sqrt((dx + 0.5f) * (dx + 0.5f) + (dy + 0.5f) * (dy + 0.5f));
            if (distance < minDistance)
                continue;
            if (distance < bestScore)
            {
                bestScore = distance;
                best = (x, y);
            }
        }
        return best;
    }

    private static bool IsBuildable(LocalMap map, Colony colony, int x, int y)
    {
        int elevation = map.InBounds(x, y) ? map.GetElevation(x, y) : -1;
        for (int ty = y; ty < y + 2; ty++)
        for (int tx = x; tx < x + 2; tx++)
        {
            if (!map.IsWalkable(tx, ty) || map.IsMountain(tx, ty) || map.GetElevation(tx, ty) != elevation)
                return false;
            if (map.GetFlora(tx, ty) is FloraType.Tree or FloraType.Bush)
                return false;
        }

        // On garde une case de passage autour de chaque bâtiment, et on ne bâtit pas sur le cercle du feu.
        foreach (Building other in colony.Buildings)
            if (x < other.X + other.Width + 1 && x + 2 > other.X - 1 && y < other.Y + other.Height + 1 && y + 2 > other.Y - 1)
                return false;
        foreach (Grave grave in colony.Graves)
            if (grave.X >= x - 1 && grave.X <= x + 2 && grave.Y >= y - 1 && grave.Y <= y + 2)
                return false;
        foreach (Field field in colony.Fields)
            if (x < field.X + Field.Size + 1 && x + 2 > field.X - 1 && y < field.Y + Field.Size + 1 && y + 2 > field.Y - 1)
                return false;
        return true;
    }

    /// <summary>
    /// Une case pour une tombe : le cimetière se tient à l'écart du camp et grandit autour de la première tombe,
    /// sur un terrain dégagé, loin des huttes et des champs.
    /// </summary>
    public static (int X, int Y)? FindGraveSite(LocalMap map, Colony colony)
    {
        Grave? first = colony.Graves.FirstOrDefault(g => g.X >= 0);
        (int cx, int cy) = first is not null ? (first.X, first.Y) : (colony.CampX - 10, colony.CampY + 5);

        (int X, int Y)? best = null;
        int bestDistance = int.MaxValue;
        for (int dy = -18; dy <= 18; dy++)
        for (int dx = -18; dx <= 18; dx++)
        {
            int x = colony.CampX + dx, y = colony.CampY + dy;
            int fromCamp = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (fromCamp < 8 || !IsGraveTile(map, colony, x, y))
                continue;
            int distance = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (x, y);
            }
        }
        return best;
    }

    private static bool IsGraveTile(LocalMap map, Colony colony, int x, int y)
    {
        if (!map.InBounds(x, y) || !map.IsWalkable(x, y) || map.IsMountain(x, y) || map.GetFlora(x, y) != FloraType.None)
            return false;
        foreach (Grave grave in colony.Graves)
            if (Math.Max(Math.Abs(grave.X - x), Math.Abs(grave.Y - y)) < 2)
                return false;
        foreach (Building building in colony.Buildings)
            if (x >= building.X - 1 && x <= building.X + building.Width && y >= building.Y - 1 && y <= building.Y + building.Height)
                return false;
        foreach (Field field in colony.Fields)
            if (x >= field.X - 1 && x <= field.X + Field.Size && y >= field.Y - 1 && y <= field.Y + Field.Size)
                return false;
        return true;
    }

    /// <summary>Ouvre un chantier : on dégage le terrain (souches comprises) et on pose les fondations.</summary>
    public static Building PlanHut(LocalMap map, Colony colony, int x, int y) => PlanBuilding(map, colony, BuildingType.Hut, x, y);

    public static Building PlanBuilding(LocalMap map, Colony colony, BuildingType type, int x, int y)
    {
        var building = new Building(type, x, y);
        foreach ((int tx, int ty) in building.Tiles)
            map.ClearFlora(tx, ty);
        colony.Buildings.Add(building);
        return building;
    }
}
