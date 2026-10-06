using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les contraintes physiques impératives d'un emplacement : toute l'emprise est testée (jamais sa seule case d'origine), les bornes avant tout accès à un
/// tableau, puis le relief, l'eau, la flore et les occupations. Aucun bonus ne compense un emplacement impossible. Ces fonctions sont pures : elles servent à la fois
/// au préfiltrage des candidats, à la validation d'une proposition avant son admission et aux outils.
/// </summary>
internal static class PlacementChecks
{
    internal enum Verdict { Ok, Terrain, Space }

    /// <summary>
    /// L'emprise d'un bâtiment ordinaire : dans la carte avec une case de marge pour la porte, terrain sec et plat hors montagne, sans arbre ni buisson,
    /// hors des emprises, champs, canaux, place, espaces publics, corridors et cours des autres, et un anneau libre d'une case autour des autres bâtiments,
    /// champs (on laisse un passage).
    /// </summary>
    internal static Verdict Building(LocalMap map, LocalSpatialIndex index, int x, int y, int width, int height)
    {
        if (x < 1 || y < 1 || x + width >= map.Width || y + height >= map.Height)
            return Verdict.Terrain;
        int elevation = map.GetElevation(x, y);
        for (int ty = y; ty < y + height; ty++)
        for (int tx = x; tx < x + width; tx++)
        {
            if (!map.IsWalkable(tx, ty) || map.IsWaterway(tx, ty) || map.IsMountain(tx, ty) || map.GetElevation(tx, ty) != elevation)
                return Verdict.Terrain;
            if (map.GetFlora(tx, ty) is FloraType.Tree or FloraType.Bush)
                return Verdict.Terrain;
            if (index.IsSolid(tx, ty) || index.Has(tx, ty, CellUse.Corridor | CellUse.Courtyard))
                return Verdict.Space;
        }
        return RingFree(index, x, y, width, height, CellUse.Building | CellUse.Field) ? Verdict.Ok : Verdict.Space;
    }

    /// <summary>
    /// L'emprise d'un champ de côté <c>size</c> (4 à 8) : plate, sèche, de bonne terre (ni sable, ni montagne, ni eau), hors des occupations. Arbres et buissons sont permis
    /// (défrichés à l'ouverture, leur coût compte dans le choix) ; un anneau d'une case reste libre autour des bâtiments, et les champs peuvent se toucher.
    /// </summary>
    internal static Verdict Field(LocalMap map, LocalSpatialIndex index, int x, int y, int size)
    {
        if (x < 1 || y < 1 || x + size >= map.Width || y + size >= map.Height)
            return Verdict.Terrain;
        int elevation = map.GetElevation(x, y);
        for (int ty = y; ty < y + size; ty++)
        for (int tx = x; tx < x + size; tx++)
        {
            if (!map.IsWalkable(tx, ty) || map.IsWaterway(tx, ty) || map.IsMountain(tx, ty) || map.GetElevation(tx, ty) != elevation
                || map.GetSoil(tx, ty) == SoilType.Sand)
                return Verdict.Terrain;
            if (index.IsSolid(tx, ty) || index.Has(tx, ty, CellUse.Corridor | CellUse.Courtyard))
                return Verdict.Space;
        }
        return RingFree(index, x, y, size, size, CellUse.Building) ? Verdict.Ok : Verdict.Space;
    }

    /// <summary>L'anneau d'une case autour de l'emprise ne porte aucun des usages interdits.</summary>
    private static bool RingFree(LocalSpatialIndex index, int x, int y, int width, int height, CellUse forbidden)
    {
        for (int ty = y - 1; ty <= y + height; ty++)
        for (int tx = x - 1; tx <= x + width; tx++)
        {
            bool inside = tx >= x && tx < x + width && ty >= y && ty < y + height;
            if (!inside && index.Has(tx, ty, forbidden))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Les cases d'accès possibles d'une emprise de bâtiment : praticables (même relief et mêmes règles de diagonale que les mouvements ordinaires), à sec,
    /// hors de toute emprise, champ ou canal. La capacité de secours à escalader des hauteurs ne sert jamais à valider un accès.
    /// </summary>
    internal static IEnumerable<(int EntryX, int EntryY, int AccessX, int AccessY, int Side)> FreeDoors(LocalMap map, LocalSpatialIndex index, int x, int y, int width, int height)
    {
        foreach (var door in SettlementPlanner.DoorCandidates(x, y, width, height))
            if (IsAccessCell(map, index, door.AccessX, door.AccessY, door.EntryX, door.EntryY))
                yield return door;
    }

    internal static bool IsAccessCell(LocalMap map, LocalSpatialIndex index, int ax, int ay, int entryX, int entryY)
    {
        if (!map.InBounds(ax, ay) || !map.IsWalkable(ax, ay) || map.IsWaterway(ax, ay) || map.IsMountain(ax, ay))
            return false;
        if (!map.CanStep(ax, ay, entryX, entryY) || !map.CanStep(entryX, entryY, ax, ay))
            return false;
        return !index.Has(ax, ay, CellUse.Building | CellUse.Canal | CellUse.Field);
    }

    /// <summary>Les accès d'un champ : une case de son pourtour, praticable, libre de toute occupation.</summary>
    internal static IEnumerable<(int AccessX, int AccessY)> FieldAccesses(LocalMap map, LocalSpatialIndex index, int x, int y, int size)
    {
        for (int i = 0; i < size; i++)
        {
            foreach ((int ax, int ay, int ex, int ey) in new[] { (x + i, y + size, x + i, y + size - 1), (x + i, y - 1, x + i, y), (x - 1, y + i, x, y + i), (x + size, y + i, x + size - 1, y + i) })
                if (map.InBounds(ax, ay) && map.IsWalkable(ax, ay) && !map.IsWaterway(ax, ay) && !map.IsMountain(ax, ay)
                    && map.CanStep(ax, ay, ex, ey) && map.CanStep(ex, ey, ax, ay)
                    && !index.Has(ax, ay, CellUse.Building | CellUse.Canal | CellUse.Field))
                    yield return (ax, ay);
        }
    }

    /// <summary>L'écart libre (nombre de cases entre deux emprises, Chebyshev) ; 0 si elles se touchent ou se chevauchent.</summary>
    internal static int Gap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
    {
        int dx = Math.Max(0, Math.Max(bx - (ax + aw), ax - (bx + bw)));
        int dy = Math.Max(0, Math.Max(by - (ay + ah), ay - (by + bh)));
        return Math.Max(dx, dy);
    }

    /// <summary>Un tracé d'accès est encore praticable : chaque case est marchable, sans emprise ni champ, et chaque pas respecte le relief et les diagonales.</summary>
    internal static bool PathStillWalkable(LocalMap map, LocalSpatialIndex index, SettlementLayout layout, IReadOnlyList<int> cells, (int X, int Y, int Width, int Height)? future)
    {
        int previous = -1;
        foreach (int cell in cells)
        {
            (int x, int y) = layout.Decode(cell);
            if (!map.InBounds(x, y) || !map.IsWalkable(x, y)
                || ((index.UseAt(cell) & (CellUse.Building | CellUse.Field)) != 0 && !index.IsLegacyOpen(x, y)))
                return false;
            if (future is { } f && x >= f.X && y >= f.Y && x < f.X + f.Width && y < f.Y + f.Height)
                return false;
            if (previous >= 0)
            {
                (int px, int py) = layout.Decode(previous);
                if (!map.CanStep(px, py, x, y))
                    return false;
                if (px != x && py != y && (!map.CanStep(px, py, x, py) || !map.CanStep(px, py, px, y)))
                    return false;
            }
            previous = cell;
        }
        return true;
    }
}
