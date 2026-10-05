using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Pathfinding;

/// <summary>
/// Le coût d'un pas, partagé par le pathfinder, le mouvement des colons et les budgets du planificateur : la durée réelle d'une arête, en secondes de
/// simulation à vitesse ×1. Longueur du pas (la diagonale est plus longue), coût du terrain sec ou du franchissement, surface routière réalisée et pénalité de
/// montée. Une route n'accélère que la terre sèche : ni la rivière, ni un canal en eau, ni la forêt.
/// </summary>
public static class TraversalCost
{
    public const float Diagonal = 1.41421356f;

    /// <summary>Monter d'un niveau coûte l'équivalent d'une demi-case de marche de plus.</summary>
    public const float UphillCells = 0.5f;

    private const float SecondsPerCell = 1f / SettlementRules.WalkTilesPerSecond;

    /// <summary>Durée du pas de (fromX, fromY) à (toX, toY), en secondes : toujours strictement positive.</summary>
    public static float StepSeconds(LocalMap map, int fromX, int fromY, int toX, int toY)
    {
        bool diagonal = fromX != toX && fromY != toY;
        float terrain = map.MoveCost(toX, toY);
        float road = terrain == 1f ? map.Roads.CostFactor(toY * map.Width + toX) : 1f;
        float cells = (diagonal ? Diagonal : 1f) * terrain * road * map.Ruggedness(toX, toY);
        if (map.GetElevation(toX, toY) > map.GetElevation(fromX, fromY))
            cells += UphillCells;
        return cells * SecondsPerCell;
    }

    /// <summary>Même durée, en ticks (fractionnaire : le mouvement l'accumule d'un tick à l'autre).</summary>
    public static float StepTicks(LocalMap map, int fromX, int fromY, int toX, int toY) =>
        StepSeconds(map, fromX, fromY, toX, toY) * TimeConstants.TicksPerSecond;

    /// <summary>
    /// La durée minimale d'un pas orthogonal : celle d'une route de terre en terrain plat. L'heuristique octile d'un A* doit être multipliée par cette
    /// durée pour rester admissible ; une heuristique non réduite surestimerait les distances dès que des routes existent.
    /// </summary>
    public const float MinStepSeconds = SettlementRules.DirtRoadCost * LocalMap.RuggednessMin * SecondsPerCell;

    /// <summary>La distance octile entre deux cases, en cases (la plus courte possible en huit directions sur terrain plat).</summary>
    public static float Octile(int dx, int dy)
    {
        dx = Math.Abs(dx);
        dy = Math.Abs(dy);
        return dx + dy + (Diagonal - 2f) * Math.Min(dx, dy);
    }
}
