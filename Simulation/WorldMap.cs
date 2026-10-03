using GodColony.Simulation.Colonies;

namespace GodColony.Simulation;

/// <summary>
/// La carte du monde, réduite à l'essentiel pour l'instant : où se trouve chaque colonie, et donc combien de temps
/// met une caravane à aller de l'une à l'autre. (Plus tard : hexagones, biomes, frontières, routes.)
/// </summary>
public sealed class WorldMap
{
    /// <summary>Cases du monde qu'une caravane parcourt par jour de jeu.</summary>
    public const float CaravanTilesPerDay = 6f;

    /// <summary>Rayon (en cases du monde) du cercle où l'on place les colonies.</summary>
    private const float RingRadius = 5.5f;

    private readonly Dictionary<Colony, (float X, float Y)> _positions = [];

    /// <summary>Place une colonie sur le cercle du monde : la <paramref name="index"/>-ième sur <paramref name="count"/>.</summary>
    internal void Place(Colony colony, int index, int count)
    {
        float angle = 2f * MathF.PI * index / Math.Max(1, count);
        _positions[colony] = (RingRadius * MathF.Cos(angle), RingRadius * MathF.Sin(angle));
    }

    public (float X, float Y) PositionOf(Colony colony) => _positions[colony];

    /// <summary>Distance à vol d'oiseau entre deux colonies, en cases du monde.</summary>
    public float Distance(Colony a, Colony b)
    {
        (float ax, float ay) = _positions[a];
        (float bx, float by) = _positions[b];
        return MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }

    /// <summary>Jours de marche d'une caravane entre deux colonies.</summary>
    public float TravelDays(Colony a, Colony b) => Distance(a, b) / CaravanTilesPerDay;
}
