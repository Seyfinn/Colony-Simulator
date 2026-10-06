using GodColony.Simulation.Map;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// La vie des chemins : les passages répétés usent la terre nue et font naître des sentiers ; sans passage, l'usure s'efface. L'aménagement en chemin de terre est
/// un travail facultatif (voir <see cref="RoadWorks"/>). Rien ici ne parcourt la carte entière : un passage touche une cellule, le jour ne touche que les cellules
/// dont l'échéance tombe, et l'usure est une fonction de la valeur et du jour de dernière mise à jour, jamais de la fréquence à laquelle on la lit.
/// </summary>
public static class RoadDevelopment
{
    /// <summary>En deçà de cette usure (en passages équivalents), un sentier non aménagé s'efface : une hystérésis, il faut 8 passages pour naître mais 4 suffisent à tenir.</summary>
    private const int KeepPasses = 4;

    /// <summary>Plus de passages équivalents que cela : l'usure d'une cellule n'est plus suivie (un sentier très fréquenté ne s'efface pas en un jour).</summary>
    private const int MaxTrackedDays = 40;

    /// <summary>Le facteur de perte quotidienne, en virgule fixe (Q16) : 0,85 par jour, tabulé pour que l'usure soit un calcul entier identique sur toute machine.</summary>
    private static readonly int[] DecayQ16 = BuildDecayTable();

    private static int[] BuildDecayTable()
    {
        var table = new int[MaxTrackedDays + 1];
        long factor = 1 << 16;
        long daily = (long)Math.Round((1.0 - SettlementRules.WearLossPerDay) * (1 << 16));
        for (int d = 0; d <= MaxTrackedDays; d++)
        {
            table[d] = (int)factor;
            factor = factor * daily >> 16;
        }
        return table;
    }

    /// <summary>L'usure d'une cellule à un jour donné, depuis sa dernière mise à jour. Aucune mutation : lire plus souvent ne change rien.</summary>
    public static int WearAt(LocalMap map, int cell, int today)
    {
        RoadLayer roads = map.Roads;
        return Decayed(roads.RawWear(cell), roads.WearDay(cell), today);
    }

    internal static int Decayed(int wear, int fromDay, int today)
    {
        int days = today - fromDay;
        if (days <= 0)
            return wear;
        if (days > MaxTrackedDays)
            return 0;
        return (int)((long)wear * DecayQ16[days] >> 16);
    }

    /// <summary>
    /// Un colon vient de finir un pas : le passage compte sur la cellule atteinte, une seule fois par pas terminé (jamais une interpolation graphique).
    /// Un sentier ne naît que sur une cellule éligible : terre nue, hors des bâtiments, cultures, eau, canaux, place et végétation protégée.
    /// </summary>
    internal static void OnStepCompleted(Colony colony, int toX, int toY)
    {
        LocalMap map = colony.Map;
        RoadLayer roads = map.Roads;
        if (map.IsRiver(toX, toY))
        {
            Bridges.OnRiverStep(colony, map, toX, toY);
            return;
        }
        if (!IsEligible(colony, map, toX, toY))
            return;
        int cell = toY * map.Width + toX;
        int today = (int)colony.Clock.TotalDays;
        int wear = Math.Min(RoadLayer.MaxWear, Decayed(roads.RawWear(cell), roads.WearDay(cell), today) + RoadLayer.Pass);
        roads.SetWear(cell, wear, today);
        roads.TouchedToday.Add(cell);
        if (roads.SurfaceAt(cell) != RoadSurface.None || wear < SettlementRules.TrailThreshold * RoadLayer.Pass)
            return;

        roads.SetSurface(cell, RoadSurface.Trail);
        Schedule(roads, cell, wear, today);
        map.NotifyRoadChanged(toX, toY);
        SettlementPlanner.Notify(colony, RetryEvents.Road);
    }

    private static bool IsEligible(Colony colony, LocalMap map, int x, int y)
    {
        if (!map.IsWalkable(x, y) || map.IsWaterway(x, y) || map.IsMountain(x, y))
            return false;
        if (map.GetFlora(x, y) is FloraType.Tree or FloraType.Bush)
            return false;
        return !colony.Spatial.Has(x, y, CellUse.Building | CellUse.Field | CellUse.Canal | CellUse.Plaza | CellUse.PublicSpace);
    }

    /// <summary>Planifie la vérification d'un sentier au jour où son usure, sans nouveau passage, tomberait sous le seuil de maintien (agenda déterministe).</summary>
    private static void Schedule(RoadLayer roads, int cell, int wear, int today)
    {
        int days = 1;
        while (days < MaxTrackedDays && Decayed(wear, today, today + days) >= KeepPasses * RoadLayer.Pass)
            days++;
        long due = today + days;
        if (!roads.ExpiryAgenda.TryGetValue(due, out List<int>? cells))
            roads.ExpiryAgenda[due] = cells = [];
        if (!cells.Contains(cell))
            cells.Add(cell);
    }

    /// <summary>
    /// Le jour commence : les sentiers dont l'échéance tombe aujourd'hui sont revérifiés (un sentier toujours fréquenté est reprogrammé, les autres s'effacent) ;
    /// les chemins de terre restent. Aucun balayage de la grille : seules les cellules de l'agenda du jour sont touchées.
    /// </summary>
    internal static void OnDayStart(Colony colony)
    {
        LocalMap map = colony.Map;
        RoadLayer roads = map.Roads;
        long today = colony.Clock.TotalDays;
        Bridges.OnDayStart(colony);
        roads.TouchedToday.Clear();
        RoadWorks.OnDayStart(colony);
        if (!roads.ExpiryAgenda.Remove(today, out List<int>? due))
            return;
        due.Sort();
        foreach (int cell in due)
        {
            if (roads.SurfaceAt(cell) != RoadSurface.Trail)
                continue;
            int wear = Decayed(roads.RawWear(cell), roads.WearDay(cell), (int)today);
            if (wear >= KeepPasses * RoadLayer.Pass)
            {
                Schedule(roads, cell, wear, (int)today);
                continue;
            }
            roads.SetSurface(cell, RoadSurface.None);
            roads.SetWear(cell, 0, (int)today);
            map.NotifyRoadChanged(cell % map.Width, cell / map.Width);
        }
    }
}
