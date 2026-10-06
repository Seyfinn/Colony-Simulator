using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Nature;

/// <summary>
/// La nature sauvage d'une région : sa population par espèce, la gêne que lui cause l'homme, et ses ressources sauvages épuisables
/// (ruches, champignons, plantes médicinales, rangées par numéro de case de la carte locale).
/// </summary>
public sealed class RegionWildlife
{
    /// <summary>Bêtes de chaque espèce dans la région, hors celles que des hardes visibles empruntent (voir <see cref="WildHerd"/>).</summary>
    public Dictionary<WildSpecies, int> Population { get; } = [];

    /// <summary>La croissance fractionnaire en attente, par espèce : un dixième de lapin ne naît pas, dix dixièmes oui.</summary>
    internal Dictionary<WildSpecies, float> Pending { get; } = [];

    /// <summary>Gêne causée par l'homme (défrichement, bâtiments, présence), de 0 à 1 : elle réduit la capacité d'accueil.</summary>
    public float Disturbance { get; internal set; }

    /// <summary>Bêtes parties passer l'hiver ailleurs, qui reviennent au printemps.</summary>
    internal Dictionary<WildSpecies, int> Away { get; } = [];

    /// <summary>Bêtes d'une région voisine hébergées pour l'hiver (comprises dans <see cref="Population"/>), rendues au printemps.</summary>
    internal Dictionary<WildSpecies, int> Guests { get; } = [];

    /// <summary>Moment du dernier alpha apparu dans la région (pas plus d'un par an).</summary>
    public long LastAlphaTicks { get; internal set; } = long.MinValue / 2;

    /// <summary>Ressources sauvages : numéro de case → portions restantes.</summary>
    public Dictionary<int, int> Hives { get; } = [];
    public Dictionary<int, int> Mushrooms { get; } = [];
    public Dictionary<int, int> Herbs { get; } = [];

    public const int MaxHive = 3, MaxMushroom = 3, MaxHerb = 2;

    public int PopulationOf(WildSpecies species) => Population.GetValueOrDefault(species);

    public int Prey => WildSpeciesInfo.All.Where(s => !WildSpeciesInfo.IsPredator(s)).Sum(PopulationOf);
    public int Predators => WildSpeciesInfo.All.Where(WildSpeciesInfo.IsPredator).Sum(PopulationOf);

    /// <summary>La population de départ : sept dixièmes de la capacité d'accueil du biome, les ressources sauvages tirées de la graine de la carte (sans hasard de partie).</summary>
    internal static RegionWildlife Create(LocalMap map)
    {
        var wildlife = new RegionWildlife();
        foreach (WildSpecies species in WildSpeciesInfo.All)
            wildlife.Population[species] = (int)MathF.Round(WildSpeciesInfo.BaseCap(map.Biome, species) * 0.7f);
        wildlife.PlantResources(map);
        return wildlife;
    }

    /// <summary>Un hachage stable d'une case : sert à poser les ruches, les champignons et les herbes sans consommer le hasard de la partie.</summary>
    internal static uint Hash(int seed, int x, int y, int salt)
    {
        uint h = unchecked((uint)seed * 2654435761u + (uint)x * 40503u + (uint)y * 9973u + (uint)salt * 668265263u);
        h ^= h >> 15; h = unchecked(h * 2246822519u); h ^= h >> 13; h = unchecked(h * 3266489917u); h ^= h >> 16;
        return h;
    }

    private void PlantResources(LocalMap map)
    {
        Biome biome = map.Biome;
        bool woods = biome is Biome.TemperateForest or Biome.TropicalForest or Biome.BorealForest or Biome.Swamp;
        bool hiveBiome = biome is Biome.TemperateForest or Biome.TropicalForest;
        bool meadow = biome is Biome.Grassland or Biome.Steppe or Biome.Savanna or Biome.TemperateForest;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (!map.IsWalkable(x, y) || map.IsRiver(x, y))
                continue;
            int index = y * map.Width + x;
            FloraType flora = map.GetFlora(x, y);
            if (hiveBiome && flora == FloraType.Tree && Hash(map.Seed, x, y, 1) % 110 == 0)
                Hives[index] = 1 + (int)(Hash(map.Seed, x, y, 4) % MaxHive);
            else if (woods && flora is FloraType.None or FloraType.Stump && Hash(map.Seed, x, y, 2) % 70 == 0)
                Mushrooms[index] = 1 + (int)(Hash(map.Seed, x, y, 5) % MaxMushroom);
            else if (meadow && flora == FloraType.None && Hash(map.Seed, x, y, 3) % 90 == 0)
                Herbs[index] = 1 + (int)(Hash(map.Seed, x, y, 6) % MaxHerb);
        }
    }
}
