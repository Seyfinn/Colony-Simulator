using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

public static partial class SettlementPlanner
{
    public const int MaxExtensions = 3;

    /// <summary>Un marché prospère gagne progressivement des étals, à mesure que le village et ses échanges grandissent.</summary>
    internal static bool WantsMarketExtension(Colony colony)
    {
        int extensions = colony.Buildings.Count(b => b.Type == BuildingType.Market && b.IsExtension);
        return Civic.Has(colony, BuildingType.Market) && Knowledge.Allows(colony, BuildingType.Market)
            && colony.PresentMembers.Count >= 20 + extensions * 6
            && (colony.Trades.Count > 0 || colony.ExportInterest.GetValueOrDefault(Specialties.NativeOf(colony)) > 0);
    }

    /// <summary>Un chantier de six cases prolonge un enclos ou un marché achevé, sans fermer le bâtiment principal.</summary>
    internal static Building? PlanExtension(Colony colony, BuildingType type, Building? principalChoisi = null)
    {
        if (type is not (BuildingType.Pen or BuildingType.Market) || colony.ConstructionSites.Any()) return null;
        Sync(colony);
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        foreach (Building principal in colony.Buildings.Where(b => b.Type == type && b.IsComplete && !b.IsExtension
            && (principalChoisi is null || b == principalChoisi)).ToArray())
        {
            Building[] ensemble = colony.Buildings.Where(b => b == principal || b.ExtensionOfId == principal.Id).ToArray();
            if (ensemble.Length > MaxExtensions) continue;
            // Chaque ajout touche un module déjà achevé ; aucun tirage aléatoire ne déplace les décisions du village.
            foreach (Building voisin in ensemble.Where(b => b.IsComplete))
            foreach ((int x, int y) in new[] {
                (voisin.X + voisin.Width, voisin.Y), (voisin.X - 2, voisin.Y),
                (voisin.X, voisin.Y + voisin.Height), (voisin.X, voisin.Y - 3),
                (voisin.X + voisin.Width - 2, voisin.Y + voisin.Height), (voisin.X + voisin.Width - 2, voisin.Y - 3) })
            {
                if (!ExtensionFits(colony, ensemble, x, y)) continue;
                foreach (var porte in PlacementChecks.FreeDoors(map, index, x, y, 2, 3)
                    .OrderBy(p => (p.AccessX - colony.CampX) * (p.AccessX - colony.CampX) + (p.AccessY - colony.CampY) * (p.AccessY - colony.CampY)))
                {
                    var path = colony.Pathfinder.FindPath(colony.CampX, colony.CampY, porte.AccessX, porte.AccessY);
                    if (path is null || path.Any(p => p.X >= x && p.X < x + 2 && p.Y >= y && p.Y < y + 3)) continue;
                    var extension = new Building(type, x, y) { Width = 2, Height = 3, ExtensionOfId = principal.Id,
                        EntryX = porte.EntryX, EntryY = porte.EntryY, AccessX = porte.AccessX, AccessY = porte.AccessY };
                    colony.Buildings.Add(extension);
                    Adopt(colony, extension);
                    return extension;
                }
            }
        }
        return null;
    }

    private static bool ExtensionFits(Colony colony, Building[] ensemble, int x, int y)
    {
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        if (x < 1 || y < 1 || x + 2 >= map.Width || y + 3 >= map.Height) return false;
        int elevation = map.GetElevation(x, y);
        for (int ty = y; ty < y + 3; ty++)
        for (int tx = x; tx < x + 2; tx++)
            if (!map.IsWalkable(tx, ty) || map.IsWaterway(tx, ty) || map.IsMountain(tx, ty)
                || map.GetElevation(tx, ty) != elevation || map.GetFlora(tx, ty) is FloraType.Tree or FloraType.Bush
                || index.IsSolid(tx, ty) || index.Has(tx, ty, CellUse.Corridor)) return false;

        foreach (PlotReservation parcelle in colony.Layout.Parcels.Where(p => p.State != ReservationState.Released))
        {
            if (parcelle.Kind == ParcelKind.Building && ensemble.Any(b => b.ParcelId == parcelle.Id)) continue;
            int marge = parcelle.Kind is ParcelKind.Building or ParcelKind.Field ? Math.Max(1, parcelle.Margin) : 0;
            if (x < parcelle.X + parcelle.Width + marge && x + 2 > parcelle.X - marge
                && y < parcelle.Y + parcelle.Height + marge && y + 3 > parcelle.Y - marge) return false;
        }
        // Les tombes peuvent ne pas avoir de parcelle ; on garde aussi leur passage libre.
        for (int ty = y - 1; ty <= y + 3; ty++)
        for (int tx = x - 1; tx <= x + 2; tx++)
            if (index.Has(tx, ty, CellUse.Grave | CellUse.Field)
                || (index.Has(tx, ty, CellUse.Building) && !ensemble.Any(b => b.Contains(tx, ty)))) return false;
        return true;
    }
}
