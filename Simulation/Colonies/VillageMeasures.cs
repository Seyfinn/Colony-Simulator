using GodColony.Simulation.Pathfinding;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les mesures d'un village, lisibles par l'affichage et les tests, sans aucune règle de plus : l'emprise habitée, le nombre de noyaux, les principaux temps de trajet.
/// L'emprise comprend les logements, services, ateliers et champs actifs ; une longue piste vers une carrière ne suffit pas à déclarer le village étendu.
/// </summary>
public static class VillageMeasures
{
    /// <summary>Le rectangle des bâtiments achevés et des champs (null s'il n'y en a pas) : (minX, minY, maxX, maxY).</summary>
    public static (int MinX, int MinY, int MaxX, int MaxY)? ActiveEnvelope(Colony colony)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        void Add(int x0, int y0, int x1, int y1)
        {
            minX = Math.Min(minX, x0); minY = Math.Min(minY, y0); maxX = Math.Max(maxX, x1); maxY = Math.Max(maxY, y1);
        }
        foreach (Building b in colony.Buildings)
            if (b.IsComplete && !b.IsDam)
                Add(b.X, b.Y, b.X + b.Width - 1, b.Y + b.Height - 1);
        foreach (Field f in colony.Fields)
            Add(f.X, f.Y, f.X + Field.Size - 1, f.Y + Field.Size - 1);
        return minX == int.MaxValue ? null : (minX, minY, maxX, maxY);
    }

    /// <summary>Le diamètre de l'emprise active, en cases (le plus grand des deux côtés).</summary>
    public static int ActiveDiameter(Colony colony) =>
        ActiveEnvelope(colony) is { } e ? Math.Max(e.MaxX - e.MinX, e.MaxY - e.MinY) + 1 : 0;

    /// <summary>Le nombre de noyaux d'habitat : les quartiers qui abritent au moins une hutte.</summary>
    public static int HabitationCores(Colony colony) => DistrictPlanner.HutGroups(colony).Count;

    /// <summary>Le temps moyen, en secondes de simulation à vitesse ×1 (à vol d'oiseau, avec un détour), d'une hutte achevée au lieu de repas le plus proche.</summary>
    public static float AverageHomeToMealSeconds(Colony colony)
    {
        float total = 0f;
        int huts = 0;
        foreach (Building hut in colony.Buildings)
        {
            if (!hut.IsHut || !hut.IsComplete)
                continue;
            ServicePoint? meal = SettlementServices.Nearest(colony, ServiceUse.Meal, hut.X, hut.Y);
            if (meal is null)
                continue;
            total += SettlementServices.DistanceTo(colony, meal, hut.X, hut.Y) * 1.2f / SettlementRules.WalkTilesPerSecond;
            huts++;
        }
        return huts == 0 ? 0f : total / huts;
    }

    /// <summary>Les lieux de service actuellement disponibles (repas, rencontre, dépôt), pour que la vue les dessine sans rien inventer.</summary>
    public static IReadOnlyList<ServicePoint> Services(Colony colony) => SettlementServices.Points(colony);
}
