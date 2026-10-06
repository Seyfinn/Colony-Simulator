using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Nature;

/// <summary>
/// Les ressources sauvages épuisables, en plus des baies et du poisson : les ruches (miel et cire) dans les forêts douces, les champignons (à l'automne) dans les forêts,
/// les plantes médicinales dans les prairies. Chaque emplacement porte quelques portions qui repoussent lentement ; leur place vient de la graine de la carte.
/// </summary>
public static class WildResources
{
    /// <summary>Ce que rapporte une portion récoltée : trois parts de miel, deux de champignons, deux brins d'herbes.</summary>
    public static int Yield(ResourceType kind) => kind switch
    {
        ResourceType.Honey => 3,
        _ => 2,
    };

    public static readonly ResourceType[] Kinds = [ResourceType.Honey, ResourceType.Mushrooms, ResourceType.Herbs];

    public static float GatherSeconds(ResourceType kind) => kind == ResourceType.Honey ? 3f : 2f;

    private static Dictionary<int, int>? Patches(RegionWildlife wild, ResourceType kind) => kind switch
    {
        ResourceType.Honey => wild.Hives,
        ResourceType.Mushrooms => wild.Mushrooms,
        ResourceType.Herbs => wild.Herbs,
        _ => null,
    };

    public static int Left(Settlement place, ResourceType kind, int x, int y) =>
        place.Wildlife is { } wild && Patches(wild, kind) is { } patches ? patches.GetValueOrDefault(y * place.Map.Width + x) : 0;

    /// <summary>Les emplacements d'une ressource à portée du centre, avec leurs portions, hors cases déjà réservées.</summary>
    public static IEnumerable<(int X, int Y, int Left)> Near(Colony colony, ResourceType kind, int centerX, int centerY, int radius)
    {
        Settlement place = colony.LocalSettlement;
        if (place.Wildlife is not { } wild || Patches(wild, kind) is not { } patches)
            return [];
        int width = place.Map.Width;
        return patches.Where(p => p.Value > 0)
            .Select(p => (X: p.Key % width, Y: p.Key / width, Left: p.Value))
            .Where(p => Math.Abs(p.X - centerX) <= radius && Math.Abs(p.Y - centerY) <= radius && !colony.Reserved.Contains((p.X, p.Y)))
            .OrderBy(p => (p.X - centerX) * (p.X - centerX) + (p.Y - centerY) * (p.Y - centerY)).ThenBy(p => p.Y).ThenBy(p => p.X);
    }

    /// <summary>On récolte une portion : le colon rapporte la ressource ; les ruches donnent aussi de la cire, directement au stock.</summary>
    public static (ResourceType Type, int Amount)? Harvest(Colony colony, ResourceType kind, int x, int y)
    {
        Settlement place = colony.LocalSettlement;
        if (place.Wildlife is not { } wild || Patches(wild, kind) is not { } patches)
            return null;
        int key = y * place.Map.Width + x;
        if (patches.GetValueOrDefault(key) <= 0)
            return null;
        patches[key]--;
        if (kind == ResourceType.Honey)
            colony.Stock.Add(ResourceType.Wax, 1);
        return (kind, Yield(kind));
    }

    /// <summary>
    /// Chaque jour, les portions repoussent : une ruche reprend un rayon tous les quatre jours, les herbes une portion tous les six, les champignons tous les deux jours mais seulement
    /// à l'automne. Aucun hasard : le décalage entre emplacements vient de leur numéro.
    /// </summary>
    public static void Daily(Colony colony, GameClock clock)
    {
        if (colony.LocalSettlement.Wildlife is not { } wild)
            return;
        long day = clock.TotalDays;
        Regrow(wild.Hives, RegionWildlife.MaxHive, day, 4);
        Regrow(wild.Herbs, RegionWildlife.MaxHerb, day, 6);
        if (clock.Season == Season.Automne)
            Regrow(wild.Mushrooms, RegionWildlife.MaxMushroom, day, 2);
    }

    private static void Regrow(Dictionary<int, int> patches, int max, long day, int period)
    {
        foreach (int key in patches.Keys.ToList())
            if (patches[key] < max && (day + key) % period == 0)
                patches[key]++;
    }
}
