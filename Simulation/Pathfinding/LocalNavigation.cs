using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.Simulation.Pathfinding;

/// <summary>Un chemin complet : ses cases (sans la case de départ), les bâtiments dont l'intérieur est parcouru au départ et à l'arrivée, et sa durée.</summary>
public sealed class NavPath
{
    internal NavPath(List<(int X, int Y)> cells, int startBuildingId, int goalBuildingId, float seconds)
    {
        Cells = cells;
        StartBuildingId = startBuildingId;
        GoalBuildingId = goalBuildingId;
        Seconds = seconds;
    }

    public List<(int X, int Y)> Cells { get; }

    /// <summary>Le bâtiment qu'on quitte et celui où l'on entre (0 : aucun) : leurs cases intérieures sont les seules emprises que ce chemin traverse.</summary>
    public int StartBuildingId { get; }
    public int GoalBuildingId { get; }

    /// <summary>La durée du trajet, en secondes de simulation à vitesse ×1.</summary>
    public float Seconds { get; }

    public int Count => Cells.Count;
}

/// <summary>
/// Adapte le pathfinder aux occupations et aux entrées. Les chemins extérieurs contournent les emprises ; on ne traverse un bâtiment que par sa porte : un petit
/// trajet intérieur du lit (ou de la case visée) à l'entrée, la porte, puis le trajet extérieur ; à l'arrivée, l'inverse. Les cases du bâtiment de départ ou de
/// destination ne servent <b>qu'à ces portions intérieures</b>, jamais de raccourci à travers un autre bâtiment. Une parcelle ancienne (sans porte) reste traversable comme avant.
/// </summary>
public static class LocalNavigation
{
    /// <summary>Le chemin de (startX, startY) à (goalX, goalY), ou null s'il n'y en a pas. <paramref name="maxStep"/> vaut plus de 1 seulement pour un colon coincé.</summary>
    public static NavPath? FindPath(Colony colony, int startX, int startY, int goalX, int goalY, int maxStep = 1)
    {
        LocalMap map = colony.Map;
        LocalSpatialIndex index = colony.Spatial;
        var cells = new List<(int X, int Y)>();
        int fromX = startX, fromY = startY;
        int startBuilding = 0, goalBuilding = 0;

        // On part de l'intérieur d'un bâtiment à porte : on ressort par là.
        if (index.BlocksWalking(startX, startY) && colony.BuildingById(index.OwnerAt(startX, startY)) is { HasDoor: true } origin)
        {
            startBuilding = origin.Id;
            if (origin.EntryX >= 0 && (startX != origin.EntryX || startY != origin.EntryY))
                cells.Add((origin.EntryX, origin.EntryY));
            cells.Add((origin.AccessX, origin.AccessY));
            (fromX, fromY) = (origin.AccessX, origin.AccessY);
        }

        // On vise l'intérieur d'un bâtiment à porte : on arrive à sa porte, puis on entre.
        int toX = goalX, toY = goalY;
        List<(int X, int Y)>? entering = null;
        if (index.BlocksWalking(goalX, goalY) && colony.BuildingById(index.OwnerAt(goalX, goalY)) is { HasDoor: true } target)
        {
            goalBuilding = target.Id;
            (toX, toY) = (target.AccessX, target.AccessY);
            if (target.EntryX >= 0)
            {
                entering = [(target.EntryX, target.EntryY)];
                if (goalX != target.EntryX || goalY != target.EntryY)
                    entering.Add((goalX, goalY));
            }
            else
                entering = [];
        }

        List<(int X, int Y)>? outside;
        if (fromX == toX && fromY == toY)
            outside = [];
        else
            outside = colony.Pathfinder.FindPath(fromX, fromY, toX, toY, maxStep);
        if (outside is null)
            return null;
        cells.AddRange(outside);
        if (entering is not null)
            cells.AddRange(entering);

        // Un but sans aucune case à parcourir : on y est déjà.
        float seconds = 0f;
        (int px, int py) = (startX, startY);
        foreach ((int x, int y) in cells)
        {
            seconds += TraversalCost.StepSeconds(map, px, py, x, y);
            (px, py) = (x, y);
        }
        return new NavPath(cells, startBuilding, goalBuilding, seconds);
    }

    /// <summary>
    /// Un chemin planifié est-il encore valable après un changement d'occupation ? Une case d'emprise n'y est permise que si elle appartient au bâtiment de départ ou
    /// de destination (ses portions intérieures).
    /// </summary>
    public static bool StillValid(LocalSpatialIndex index, List<(int X, int Y)> path, int fromIndex, int startBuildingId, int goalBuildingId)
    {
        for (int i = fromIndex; i < path.Count; i++)
        {
            (int x, int y) = path[i];
            if (!index.BlocksWalking(x, y))
                continue;
            int owner = index.OwnerAt(x, y);
            if (owner != startBuildingId && owner != goalBuildingId)
                return false;
        }
        return true;
    }
}
