using System;
using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

public partial class ColonistsView
{
    private readonly System.Collections.Generic.Dictionary<CivicUse, ServiceCoverage> _coverage = [];
    private double _coverageAt = -1;

    private void RefreshServiceCoverage()
    {
        if (_time - _coverageAt < 1) return;
        _coverageAt = _time;
        foreach (CivicUse use in new[] { CivicUse.Study, CivicUse.Recover, CivicUse.Relax })
            _coverage[use] = CivicServices.Coverage(_colony, use);
    }
    private void DrawWorkshopConnection(Building module)
    {
        if (!module.IsExtension || module.Type is not (BuildingType.Oven or BuildingType.Mill)) return;
        Building? principal = _colony.Buildings.FirstOrDefault(b => b.Id == module.ExtensionOfId);
        if (principal is null) return;
        Vector2 center = new Vector2(module.X + module.Width / 2f, module.Y + module.Height / 2f) * Tile;
        Vector2 nearest = new(Math.Clamp(center.X, principal.X * Tile, (principal.X + principal.Width) * Tile),
            Math.Clamp(center.Y, principal.Y * Tile, (principal.Y + principal.Height) * Tile));
        Vector2 edge = new(Math.Clamp(nearest.X, module.X * Tile, (module.X + module.Width) * Tile),
            Math.Clamp(nearest.Y, module.Y * Tile, (module.Y + module.Height) * Tile));
        Vector2 direction = (nearest - center).Normalized();
        DrawLine(edge - direction * 6, nearest + direction * 6, ArtDirection.Charcoal, 7);
        DrawLine(edge - direction * 6, nearest + direction * 6, module.IsComplete ? Color.Color8(161, 115, 66) : ArtDirection.Brass, 3);
    }

    private void DrawWorkshopOutput(Building building, Vector2 basePoint)
    {
        if (building.OutputStock is null || building.IsExtension) return;
        int slot = 0;
        foreach (var (good, amount) in Enum.GetValues<ResourceType>().Select(g => (Good: g, Amount: building.OutputUnits(g))).Where(p => p.Amount > 0))
        {
            Vector2 at = basePoint + new Vector2(8 + slot++ * 25, -9);
            DrawRect(new Rect2(at + new Vector2(-2, 7), new Vector2(24, 3)), Color.Color8(161, 115, 66));
            DrawLine(at + new Vector2(0, 10), at + new Vector2(0, 15), Color.Color8(161, 115, 66), 2);
            DrawLine(at + new Vector2(20, 10), at + new Vector2(20, 15), Color.Color8(161, 115, 66), 2);
            int count = amount <= 4 ? 1 : amount <= 12 ? 2 : 3;
            for (int i = 0; i < count; i++)
                DrawTextureRect(ResourceIcons.Get(good), new Rect2(at + new Vector2(i * 6, -i * 3), new Vector2(10, 10)), false);
        }
    }

    private void DrawServiceGap(Building building, Vector2 basePoint)
    {
        CivicUse? use = building.Type switch { BuildingType.School => CivicUse.Study, BuildingType.Infirmary => CivicUse.Recover, BuildingType.Tavern => CivicUse.Relax, _ => null };
        if (use is null) return;
        ServiceGap gap = _coverage.TryGetValue(use.Value, out var coverage) ? coverage.Gap : ServiceGap.None;
        if (gap == ServiceGap.None) return;
        Vector2 at = basePoint + new Vector2(building.Width * Tile - 10, -12);
        DrawCircle(at, 5, ArtDirection.Charcoal);
        if (gap == ServiceGap.Saturated) DrawRect(new Rect2(at - new Vector2(2, 2), new Vector2(4, 4)), DashboardStyle.Warning);
        else { DrawLine(at - new Vector2(2, 2), at + new Vector2(2, 2), DashboardStyle.Warning, 2); DrawLine(at + new Vector2(-2, 2), at + new Vector2(2, -2), DashboardStyle.Warning, 2); }
    }

    private void DrawShortcutWorks()
    {
        foreach (DevelopmentProject project in _colony.Layout.Projects.Where(p => p.Kind == DevelopmentKind.RoadShortcut
            && p.State is not (ProjectState.Completed or ProjectState.Cancelled)))
        foreach (RoadSegment segment in _colony.Layout.RoadSegments.Where(s => s.Function == SegmentFunction.Link && project.SegmentIds.Contains(s.Id)))
        foreach (int cell in segment.Cells)
        {
            if (_colony.Map.Roads.SurfaceAt(cell) == RoadSurface.DirtRoad) continue;
            Vector2 at = new Vector2(cell % _colony.Map.Width, cell / _colony.Map.Width) * Tile;
            DrawLine(at + new Vector2(9, 9), at + new Vector2(12, 12), new Color(ArtDirection.Brass, .7f), 2);
            DrawLine(at + new Vector2(20, 20), at + new Vector2(23, 23), new Color(ArtDirection.Brass, .7f), 2);
        }
    }

    private void DrawMonument(Monument monument)
    {
        Vector2 at = new Vector2(monument.X + .5f, monument.Y + 1) * Tile;
        Color material = monument.Materials.ContainsKey(ResourceType.Gold) ? ArtDirection.Brass : Color.Color8(175, 184, 163);
        DrawRect(new Rect2(at + new Vector2(-10, -7), new Vector2(20, 5)), material.Darkened(.25f));
        DrawRect(new Rect2(at + new Vector2(-7, -13), new Vector2(14, 6)), material);
        if (monument.Model == OfferingModel.SimpleAltar) { DrawRect(new Rect2(at + new Vector2(-11, -16), new Vector2(22, 4)), material.Lightened(.2f)); return; }
        DrawRect(new Rect2(at + new Vector2(-4, -30), new Vector2(8, 18)), material);
        DrawCircle(at + new Vector2(0, -34), 4, material.Lightened(.15f));
        bool champion = monument.Model is OfferingModel.ChampionStatue or OfferingModel.CrownedChampionStatue;
        DrawLine(at + new Vector2(4, -27), at + new Vector2(10, champion ? -38 : -19), material, 3);
        if (champion) DrawLine(at + new Vector2(10, -38), at + new Vector2(10, -16), material.Lightened(.2f), 2);
        else DrawTextureRect(ResourceIcons.Get(ResourceType.Grain), new Rect2(at + new Vector2(4, -26), new Vector2(10, 10)), false);
        if (monument.Model == OfferingModel.CrownedChampionStatue) DrawRect(new Rect2(at + new Vector2(-5, -41), new Vector2(10, 3)), ArtDirection.Brass);
        if (monument.Model == OfferingModel.GrandHarvestStatue) DrawRect(new Rect2(at + new Vector2(-13, -3), new Vector2(26, 3)), material);
        int inset = 0;
        foreach (ResourceType gem in monument.Materials.Keys.Where(OfferingTemplate.IsGem).OrderBy(g => g))
        {
            Color color = gem switch { ResourceType.Ruby => Color.Color8(192, 87, 74), ResourceType.Sapphire => Color.Color8(85, 142, 180), ResourceType.Emerald => Color.Color8(92, 167, 117), _ => ArtDirection.Cream };
            DrawRect(new Rect2(at + new Vector2(-5 + inset++ * 3, -10), new Vector2(2, 2)), color);
        }
    }
}
