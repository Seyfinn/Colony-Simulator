using System.Text;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.Simulation.Tests;

/// <summary>Dessine un village en texte (bâtiments, champs, place, accès, sentiers) : l'outil d'inspection du plan, pour juger la forme d'un village sans lancer le jeu.</summary>
internal static class VillageAscii
{
    internal static string Draw(Colony colony, int margin = 6)
    {
        LocalMap map = colony.Map;
        SettlementLayout layout = colony.Layout;
        LocalSpatialIndex index = colony.Spatial;
        int minX = colony.CampX, maxX = colony.CampX, minY = colony.CampY, maxY = colony.CampY;
        foreach (Building b in colony.Buildings) { minX = Math.Min(minX, b.X); maxX = Math.Max(maxX, b.X + 1); minY = Math.Min(minY, b.Y); maxY = Math.Max(maxY, b.Y + 1); }
        foreach (Field f in colony.Fields) { minX = Math.Min(minX, f.X); maxX = Math.Max(maxX, f.X + 3); minY = Math.Min(minY, f.Y); maxY = Math.Max(maxY, f.Y + 3); }
        minX = Math.Max(0, minX - margin); minY = Math.Max(0, minY - margin);
        maxX = Math.Min(map.Width - 1, maxX + margin); maxY = Math.Min(map.Height - 1, maxY + margin);

        var sb = new StringBuilder();
        sb.AppendLine($"{colony.Name} — camp ({colony.CampX},{colony.CampY}) — {colony.Members.Count} habitants — fenêtre x {minX}..{maxX}, y {minY}..{maxY}");
        sb.AppendLine("quartiers : " + string.Join(", ", layout.Districts.Select(d => $"#{d.Id} {d.Kind}@({d.AnchorX},{d.AnchorY}) {layout.LoadOf(d)} parcelles")));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
                sb.Append(Glyph(colony, map, layout, index, x, y));
            sb.AppendLine();
        }
        sb.AppendLine("légende : @ feu, : place, H hutte (minuscule : chantier), # champ, ~ eau, ^ montagne, T arbre, ' buisson, + accès réservé, = sentier, _ route, * espace public");
        return sb.ToString();
    }

    private static char Letter(BuildingType type) => type switch
    {
        BuildingType.Hut => 'H', BuildingType.Kiln => 'K', BuildingType.Bloomery => 'B', BuildingType.Forge => 'F', BuildingType.Dam => 'D',
        BuildingType.Mill => 'M', BuildingType.Oven => 'O', BuildingType.Pen => 'P', BuildingType.Loom => 'L', BuildingType.Market => 'R',
        BuildingType.Infirmary => 'I', BuildingType.Storehouse => 'S', BuildingType.Well => 'W', BuildingType.Tavern => 'V', BuildingType.School => 'E',
        BuildingType.Cask => 'C', _ => '?',
    };

    private static char Glyph(Colony colony, LocalMap map, SettlementLayout layout, LocalSpatialIndex index, int x, int y)
    {
        if (x == colony.CampX && y == colony.CampY) return '@';
        foreach (Building b in colony.Buildings)
            if (b.Contains(x, y))
                return b.IsComplete ? Letter(b.Type) : char.ToLowerInvariant(Letter(b.Type));
        foreach (Field f in colony.Fields)
            if (f.Contains(x, y)) return '#';
        if (map.IsWater(x, y)) return '~';
        if (map.IsRiver(x, y)) return '≈';
        if (map.IsMountain(x, y)) return '^';
        int cell = layout.Cell(x, y);
        RoadSurface surface = map.Roads.SurfaceAt(cell);
        if (surface == RoadSurface.DirtRoad) return '_';
        if (surface == RoadSurface.Trail) return '=';
        CellUse use = index.UseAt(x, y);
        if ((use & CellUse.PublicSpace) != 0) return '*';
        if ((use & CellUse.Corridor) != 0) return '+';
        if ((use & CellUse.Plaza) != 0) return ':';
        return map.GetFlora(x, y) switch { FloraType.Tree => 'T', FloraType.Bush => '\'', _ => ' ' };
    }
}
