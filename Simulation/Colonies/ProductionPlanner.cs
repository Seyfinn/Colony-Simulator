using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// La spécialisation d'une colonie : parmi les biens qu'elle sait réellement produire (son registre de travail le prouve), elle retient ceux qui rapportent le
/// plus de surplus par heure de travail, compte tenu de ce que ses voisins solvables paieraient, du transport et de ses propres besoins. Le choix est lissé :
/// une filière n'en remplace une autre que si elle vaut nettement mieux pendant plusieurs jours, et la survie prime toujours.
/// Les mentions « agricole », « minière »… ne sont que des descriptions déduites (voir <see cref="EconomicProfile"/>) : elles n'accordent aucun bonus.
/// </summary>
public static class ProductionPlanner
{
    /// <summary>Nombre de filières d'exportation qu'une colonie entretient à la fois.</summary>
    public const int FocusSize = 2;

    /// <summary>Une filière en remplace une autre si son surplus par heure est supérieur d'au moins ce facteur…</summary>
    public const double SwitchGain = 1.15;

    /// <summary>…pendant ce nombre de jours de suite.</summary>
    public const int SwitchDays = 3;

    /// <summary>Surplus par heure de travail minimal pour qu'un bien mérite une filière d'exportation.</summary>
    public const double MinScore = 0.15;

    /// <summary>Marge de stockage, d'usure et de pertes ajoutée au coût de production.</summary>
    private const double HandlingMargin = 0.05;

    /// <summary>Le coût de transport d'une unité vers un partenaire (heures de travail), selon la charge réelle d'une caravane et la durée du trajet.</summary>
    public static double TransportPerUnit(WorldState world, Colony from, Colony to, ResourceType good)
    {
        double days = world.WorldMap.TravelDays(from, to);
        double perTrip = Math.Max(1.0, Trade.CapacityOf(from, to) / ResourceCatalog.Weight(good));
        return Trade.TradersPerCaravan * (2 * days + 0.5) * Trade.WorkHoursPerDay / perTrip;
    }

    /// <summary>Ce que rapporte une unité de ce bien : le meilleur prix connu d'un acheteur solvable (transport déduit), ou la valeur d'usage locale si la colonie en manque.</summary>
    public static double Revenue(WorldState world, Colony colony, ResourceType good, IEnumerable<Colony> partners)
    {
        double best = 0;
        foreach (Colony partner in partners)
        {
            SupplierMemory? memory = Trade.Suppliers(world, colony).FirstOrDefault(m => m.Supplier == partner);
            MarketOffer? offer = memory?.Offers.FirstOrDefault(o => o.Good == good);
            if (memory is null || offer is null || memory.AgeDays(world.Clock.Ticks) > Trade.OfferLifetimeDays || offer.Wanted <= 0 || memory.BuyingBudget < offer.BuyPrice) continue;
            best = Math.Max(best, offer.BuyPrice - TransportPerUnit(world, colony, partner, good));
        }
        if (Economy.Need(colony, good) > colony.Stock.Get(good)) best = Math.Max(best, Economy.UseValue(colony, good, colony.Stock.Get(good)));
        return best;
    }

    /// <summary>Le surplus par heure de travail d'une filière : (recette − coût complet) ÷ coût en travail ; négatif si elle ne paie pas.</summary>
    public static double Score(WorldState world, Colony colony, ResourceType good, IEnumerable<Colony> partners)
    {
        double labor = Economy.Cost(colony, good);
        return labor <= 0 ? double.NegativeInfinity : (Revenue(world, colony, good, partners) - labor * (1 + HandlingMargin)) / labor;
    }

    /// <summary>Un bien que la colonie sait réellement produire : son registre de travail l'a déjà vu naître. L'hypothèse reste prudente : pas d'invention de filière.</summary>
    public static bool CanProduce(Colony colony, ResourceType good) => colony.Labor.HoursPerUnit(good) is not null && !Husbandry.IsLivestock(good);

    /// <summary>
    /// Revue quotidienne : calcule le surplus de chaque filière connue, puis fait évoluer lentement la liste des filières d'exportation. En crise, aucune filière
    /// d'exportation n'est entretenue (la survie passe avant).
    /// </summary>
    internal static void Revise(WorldState world, Colony colony, IReadOnlyList<Colony> partners)
    {
        if (ExtendedIndustry.Crisis(colony)) { colony.FocusGoods.Clear(); colony.FocusChallenger = -1; colony.FocusChallengerDays = 0; return; }
        var scores = new Dictionary<ResourceType, double>();
        foreach (ResourceType good in Economy.Tradable.Where(g => CanProduce(colony, g)))
            scores[good] = Score(world, colony, good, partners);
        // Une filière qui ne rapporte plus ou que la colonie ne sait plus produire sort de la liste à la revue suivante.
        colony.FocusGoods.RemoveAll(g => scores.GetValueOrDefault(g, double.NegativeInfinity) < MinScore);
        List<ResourceType> ranked = scores.Where(p => p.Value >= MinScore).OrderByDescending(p => p.Value).ThenBy(p => (int)p.Key).Select(p => p.Key).ToList();
        foreach (ResourceType good in ranked.Where(g => !colony.FocusGoods.Contains(g)).Take(Math.Max(0, FocusSize - colony.FocusGoods.Count)).ToList())
            colony.FocusGoods.Add(good);
        // Quand la liste est pleine, un nouveau venu doit valoir nettement mieux que le plus faible, plusieurs jours de suite.
        ResourceType? candidate = null;
        foreach (ResourceType good in ranked)
            if (!colony.FocusGoods.Contains(good)) { candidate = good; break; }
        if (candidate is not { } best || colony.FocusGoods.Count < FocusSize)
        {
            colony.FocusChallenger = -1; colony.FocusChallengerDays = 0;
            return;
        }
        ResourceType weakest = colony.FocusGoods.OrderBy(g => scores[g]).ThenBy(g => (int)g).First();
        if (scores[best] < scores[weakest] * SwitchGain) { colony.FocusChallenger = -1; colony.FocusChallengerDays = 0; return; }
        colony.FocusChallengerDays = colony.FocusChallenger == (int)best ? colony.FocusChallengerDays + 1 : 1;
        colony.FocusChallenger = (int)best;
        if (colony.FocusChallengerDays < SwitchDays) return;
        colony.FocusGoods.Remove(weakest);
        colony.FocusGoods.Add(best);
        colony.FocusChallenger = -1; colony.FocusChallengerDays = 0;
        ColonyBrain.Say(colony, world.Clock, $"La colonie réoriente sa production : {ResourceCatalog.Name(best)} rapporte désormais plus que {ResourceCatalog.Name(weakest)}.");
    }
}
