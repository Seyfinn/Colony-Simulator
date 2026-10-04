using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les denrées de négoce : chaque région produit une denrée qu'elle seule a en abondance (le sel des déserts et des steppes,
/// les épices des savanes et des marais, le bois dur des forêts), et manque des deux autres. Le marché où l'on troque avec
/// les nomades de la région fournit la denrée locale ; les caravanes apportent les autres.
/// Le sel conserve les vivres, les épices relèvent le moral, le bois dur brûle trois fois plus longtemps que le bois.
/// </summary>
public static class Specialties
{
    public static readonly ResourceType[] Goods = [ResourceType.Salt, ResourceType.Spices, ResourceType.Hardwood];

    /// <summary>La denrée que produit la région du biome.</summary>
    public static ResourceType NativeOf(Biome biome) => biome switch
    {
        Biome.Tundra or Biome.Steppe or Biome.Desert or Biome.IceSheet or Biome.Ocean => ResourceType.Salt,
        Biome.Grassland or Biome.Savanna or Biome.Swamp => ResourceType.Spices,
        _ => ResourceType.Hardwood,
    };

    public static ResourceType NativeOf(Colony colony) => NativeOf(colony.Map.Biome);

    public static bool IsSpecialty(ResourceType good) => Goods.Contains(good);

    /// <summary>Recette du marché : on y troque deux unités de la denrée locale, sans matière première (du travail seulement).</summary>
    public static Recipe RecipeFor(Colony colony) => new(BuildingType.Market, [], NativeOf(colony), 2, 10f);

    /// <summary>Stock de la denrée locale qu'on cherche à tenir : de quoi vendre aux voisines.</summary>
    public static int NativeTarget(Colony colony) => 6 + colony.ExportInterest.GetValueOrDefault(NativeOf(colony));

    /// <summary>Ce que la colonie veut garder de chaque denrée qu'elle ne produit pas : un peu moins d'une unité par habitant.</summary>
    public static float ImportNeed(Colony colony) => Math.Max(2f, colony.Members.Count * 0.4f);

    public static string Name(ResourceType good) => good switch
    {
        ResourceType.Salt => "sel",
        ResourceType.Spices => "épices",
        _ => "bois dur",
    };

    /// <summary>Le marché travaille tant que le stock de la denrée locale n'a pas atteint son objectif.</summary>
    public static Building? PickMarketJob(Colony colony)
    {
        if (colony.Workshops(BuildingType.Market).FirstOrDefault() is not { } market)
            return null;
        ResourceType native = NativeOf(colony);
        int stock = colony.Stock.Get(native) + Crafting.Pending(colony, native) * 2;
        return stock < NativeTarget(colony) ? market : null;
    }

    /// <summary>Niveau moyen en négoce des colons qui partiraient en caravane : un bon marchand rapporte plus au voyage.</summary>
    public static float TraderLevel(IEnumerable<Colonist> traders)
    {
        var list = traders.ToList();
        return list.Count == 0 ? 0f : list.Average(t => t.Skills.Level(SkillType.Trading));
    }

    /// <summary>Chaque matin : on consomme un peu de sel (conservation) et d'épices (repas relevés).</summary>
    public static void Daily(Colony colony)
    {
        int people = colony.Members.Count;
        if (people == 0)
            return;
        colony.SaltUse += people / 15f;
        if (colony.SaltUse >= 1f)
        {
            colony.SaltUse -= 1f;
            colony.Stock.TryTake(ResourceType.Salt, 1);
        }
        colony.SpiceUse += people / 20f;
        if (colony.SpiceUse >= 1f)
        {
            colony.SpiceUse -= 1f;
            colony.Stock.TryTake(ResourceType.Spices, 1);
        }
    }

    /// <summary>Le sel tient les vivres : on en a, et c'est assez pour la colonie.</summary>
    public static bool Salted(Colony colony) => colony.Stock.Get(ResourceType.Salt) > 0;

    /// <summary>Un repas épicé : de quoi relever le moral.</summary>
    public static bool Spiced(Colony colony) => colony.Stock.Get(ResourceType.Spices) > 0;

    /// <summary>Le bois dur remplace trois bûches au feu de camp : le nombre de bois qu'il épargne.</summary>
    public const int WoodPerHardwood = 3;
}
