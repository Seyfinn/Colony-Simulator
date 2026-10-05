using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.World;

/// <summary>
/// Les routes mondiales : le niveau d'aménagement de chaque arête entre deux régions voisines, et le nombre de passages de caravanes qui l'ont empruntée.
/// Un niveau plus haut raccourcit le trajet (coût de marche réduit) ; il se construit avec du travail et des matériaux réels (voir <see cref="PlanImprovement"/>),
/// jamais sur une arête que la mer ou les sommets rendent impraticable. Les habitants investissent d'abord sur les trajets les plus fréquentés.
/// </summary>
public sealed class WorldRoadNetwork
{
    /// <summary>Jours de travail d'une équipe sur site pour aménager une arête.</summary>
    public const int WorkDays = 2;

    /// <summary>Multiple des passages observés sur lequel le gain d'une route est estimé (valeur d'équilibrage initiale).</summary>
    public const int UseHorizonFactor = 3;

    /// <summary>Personnes de l'équipe.</summary>
    public const int Crew = 4;

    private Dictionary<long, int>? _levels, _use;

    /// <summary>Compteur qui change à chaque aménagement : les itinéraires calculés avant lui se recalculent.</summary>
    public int Revision { get; private set; }

    private Dictionary<long, int> Levels => _levels ??= [];
    private Dictionary<long, int> Use => _use ??= [];

    public static long Key(int a, int b) => a < b ? (long)a * 1_000_000 + b : (long)b * 1_000_000 + a;

    public int LevelOf(int a, int b) => Levels.GetValueOrDefault(Key(a, b));
    public int UseOf(int a, int b) => Use.GetValueOrDefault(Key(a, b));

    /// <summary>Facteur de coût de marche d'une arête selon son niveau.</summary>
    public static float CostFactor(int level) => level switch { 1 => 0.75f, >= 2 => 0.55f, _ => 1f };

    internal void RecordUse(int a, int b)
    {
        long key = Key(a, b);
        Use[key] = Use.GetValueOrDefault(key) + 1;
    }

    /// <summary>Monte l'arête d'un niveau ; faux si elle est déjà au niveau maximal.</summary>
    internal bool Improve(int a, int b, int maxLevel)
    {
        long key = Key(a, b);
        if (Levels.GetValueOrDefault(key) >= maxLevel) return false;
        Levels[key] = Levels.GetValueOrDefault(key) + 1;
        Revision++;
        return true;
    }

    /// <summary>Les arêtes aménagées, pour l'affichage (de la plus basse case à la plus haute).</summary>
    public IEnumerable<(int A, int B, int Level)> Built => Levels.OrderBy(p => p.Key).Select(p => ((int)(p.Key / 1_000_000), (int)(p.Key % 1_000_000), p.Value));

    /// <summary>Matériaux et travail pour passer l'arête au niveau suivant : proportionnels à la peine du terrain traversé.</summary>
    public static (int Wood, int Stone) Materials(float stepCost, int nextLevel) =>
        ((int)MathF.Ceiling(5 * nextLevel * Math.Max(1f, stepCost)), (int)MathF.Ceiling(9 * nextLevel * Math.Max(1f, stepCost)));

    /// <summary>
    /// Le village principal décide d'aménager une arête fréquentée proche de lui quand le temps gagné sur les voyages attendus vaut plus que ce que
    /// coûtent l'équipe et les matériaux. Ne part pas en crise, ni sans les matériaux en surplus, ni si une autre mission est en cours.
    /// </summary>
    internal static bool PlanImprovement(WorldState world, Colony owner, Settlement source)
    {
        TerritorialRules rules = world.Territory;
        if (rules.MaxRoadLevel <= 0 || owner.Sensors is not { SurvivalAssured: true } sensors || sensors.FoodDays < SettlementRules.ComfortFoodDays
            || source.Population.Count < rules.MinProspectingPopulation) return false;
        WorldRoadNetwork roads = world.WorldMap.Roads;
        (int A, int B, int Use, int Level, float Step, double Ratio)? best = null;
        foreach (long key in roads.Use.Keys.Where(k => roads.Use[k] >= rules.RoadUseThreshold).OrderBy(k => k))
        {
            int a = (int)(key / 1_000_000), b = (int)(key % 1_000_000), level = roads.Levels.GetValueOrDefault(key);
            if (level >= rules.MaxRoadLevel) continue;
            float step = world.WorldMap.StepCostOf(a, b);
            if (!float.IsFinite(step) || !float.IsFinite(world.WorldMap.Grid[a].TravelCost) && !float.IsFinite(world.WorldMap.Grid[b].TravelCost)) continue;
            // Les habitants n'investissent que près de chez eux : l'une des extrémités est à une journée du village.
            int near = world.WorldMap.Grid.Distance(source.RegionTileIndex, a) <= world.WorldMap.Grid.Distance(source.RegionTileIndex, b) ? a : b;
            if (world.WorldMap.Grid.Distance(source.RegionTileIndex, near) > 2) continue;
            (int wood, int stone) = Materials(step, level + 1);
            double cost = wood * Economy.Cost(owner, ResourceType.Wood) + stone * Economy.Cost(owner, ResourceType.Stone)
                + Crew * (WorkDays + 1) * Trade.WorkHoursPerDay;
            // Chaque passage gagne la différence de coût de marche ; une route dure longtemps : on compte trois fois les passages déjà observés.
            double saved = UseHorizonFactor * roads.Use[key] * (CostFactor(level) - CostFactor(level + 1)) * step / WorldMap.CaravanTilesPerDay * Trade.TradersPerCaravan * Trade.WorkHoursPerDay;
            double ratio = saved / cost;
            if (ratio >= 1.0 && (best is null || ratio > best.Value.Ratio)) best = (near, near == a ? b : a, roads.Use[key], level, step, ratio);
        }
        if (best is not { } edge) return false;
        return TerritorialTravel.DepartRoadWork(world, source, edge.A, edge.B, edge.Level + 1, Materials(edge.Step, edge.Level + 1)) is not null;
    }
}
