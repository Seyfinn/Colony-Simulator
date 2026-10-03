namespace GodColony.Simulation.Colonies;

/// <summary>Le résultat d'une négociation sur un bien : combien d'unités changent de main, à quel prix l'unité, et ce que cela rapporte.</summary>
public sealed record Clearing(int Units, double UnitPrice, double GainHours)
{
    public static readonly Clearing None = new(0, 0, 0);
}

/// <summary>
/// L'économie vue par une colonie : ce que lui coûte chaque bien en heures de travail, ce qu'elle en a de trop ou
/// de pas assez, et donc ce qu'il vaut pour elle. Deux colonies qui n'estiment pas un bien de la même façon ont
/// intérêt à l'échanger : c'est la base du commerce.
///
/// La monnaie commune est ancrée sur le travail : une pièce vaut à peu près une heure de travail d'un colon.
///
/// Chaque unité supplémentaire vaut moins que la précédente (c'est la valeur marginale) : une colonie qui n'a plus
/// de bois paie cher la première bûche, une colonie qui en regorge la brade. L'échange se conclut tant que
/// l'acheteur y voit plus de valeur que le vendeur ; le prix est le milieu des deux valeurs de la dernière unité échangée,
/// si bien que chaque unité vendue rapporte aux deux colonies.
/// </summary>
public static class Economy
{
    /// <summary>Les biens qu'on échange (la nourriture sauvage pourrit en route ; la monnaie n'est pas un bien).</summary>
    public static readonly ResourceType[] Tradable =
    [
        ResourceType.Grain, ResourceType.Bread, ResourceType.Flour, ResourceType.Wood, ResourceType.Stone,
        ResourceType.IronOre, ResourceType.Charcoal, ResourceType.Iron, ResourceType.Tools,
    ];

    /// <summary>Un bien qu'on n'a jamais produit coûterait plus cher que la normale à qui s'y mettrait : on l'apprend sur le tas.</summary>
    private const double UnprovenFactor = 1.5;

    /// <summary>Jours de repas de céréales qu'on veut garder en grenier avant de les trouver abondantes.</summary>
    private const float GrainDays = 6f;

    /// <summary>Un bien moins rare que ce multiple du besoin ne vaut plus que le plancher du prix.</summary>
    private const double GlutFloor = 0.6;

    /// <summary>Écart de valeur minimal (en proportion) pour qu'un échange vaille la peine.</summary>
    public const double MinValueGap = 0.15;

    /// <summary>Coût de référence (heures par unité) d'un bien qu'une colonie n'a jamais produit : ce que coûtent les autres colonies en moyenne.</summary>
    public static double BaselineCost(ResourceType good) => good switch
    {
        ResourceType.Grain => 1.5,
        ResourceType.Bread => 4.5,
        ResourceType.Flour => 3.4,
        ResourceType.Wood => 0.5,
        ResourceType.Stone => 3.5,
        ResourceType.IronOre => 5.5,
        ResourceType.Charcoal => 3.2,
        ResourceType.Iron => 30.0,
        ResourceType.Tools => 70.0,
        _ => 1.0,
    };

    /// <summary>Heures de travail que coûte une unité à cette colonie : son registre, ou à défaut la référence majorée.</summary>
    public static double Cost(Colony colony, ResourceType good) =>
        colony.Labor.HoursPerUnit(good) ?? BaselineCost(good) * UnprovenFactor;

    /// <summary>La quantité que la colonie voudrait avoir en réserve pour ses propres besoins (0 si le bien ne lui sert à rien).</summary>
    public static float Need(Colony colony, ResourceType good)
    {
        float daily = Math.Max(1, colony.Members.Count) * ColonyBrain.MealsPerColonistPerDay;
        ChainDemand iron = ToolChain.Demand(colony);
        return good switch
        {
            ResourceType.Grain => daily * GrainDays,
            ResourceType.Bread => colony.Buildings.Any(b => b.Type == BuildingType.Oven) ? FoodChain.Demand(colony).BreadTarget : 0f,
            ResourceType.Flour => colony.Buildings.Any(b => b.Type == BuildingType.Mill) ? FoodChain.Demand(colony).FlourTarget : 0f,
            ResourceType.Wood => ColonyBrain.HeatingTarget(colony, colony.Clock.Season) + 10f,
            ResourceType.Stone => ColonyBrain.StoneReserveTarget,
            // Pour le minerai, le fer et le charbon : ce qu'il manque à la chaîne des outils, en plus de ce qu'on a déjà.
            ResourceType.IronOre => iron.Active ? colony.Stock.Get(ResourceType.IronOre) + iron.OreMissing : 0f,
            ResourceType.Charcoal => iron.Active ? iron.CharcoalTarget : 0f,
            ResourceType.Iron => iron.Active ? iron.IronTarget : 0f,
            ResourceType.Tools => ToolChain.ToolsWanted(colony),
            _ => 0f,
        };
    }

    /// <summary>
    /// Rareté pour un stock donné : 2 quand on n'en a pas du tout alors qu'on en a besoin, 1 quand on a juste
    /// ce qu'il faut, et jusqu'à 0,6 quand on en regorge.
    /// </summary>
    private static double ScarcityAt(double stock, float need)
    {
        double ratio = stock / need;
        return ratio < 1 ? 1 + (1 - ratio) : Math.Max(GlutFloor, 1 - 0.2 * (ratio - 1));
    }

    /// <summary>Ce que vaut, pour cette colonie, d'avoir une unité de plus quand elle en a déjà <paramref name="stock"/> : 0 si le bien ne lui sert à rien.</summary>
    public static double UseValue(Colony colony, ResourceType good, int stock)
    {
        float need = Need(colony, good);
        return need <= 0f ? 0.0 : Cost(colony, good) * ScarcityAt(stock, need);
    }

    /// <summary>Ce que la colonie perd en cédant une unité alors qu'elle en détient <paramref name="stock"/>.</summary>
    public static double KeepValue(Colony colony, ResourceType good, int stock)
    {
        float need = Need(colony, good);
        return Cost(colony, good) * (need <= 0f ? GlutFloor : ScarcityAt(stock - 1, need));
    }

    /// <summary>Rareté actuelle du bien pour la colonie (voir <see cref="ScarcityAt"/>) ; 1 si elle n'en a pas l'usage et n'en a pas.</summary>
    public static double Scarcity(Colony colony, ResourceType good)
    {
        float need = Need(colony, good);
        int stock = colony.Stock.Get(good);
        if (need <= 0f)
            return stock > 0 ? GlutFloor : 1.0;
        return ScarcityAt(stock, need);
    }

    /// <summary>Ce que vaut une unité du bien pour cette colonie, en heures de travail (donc en pièces) : son coût fois sa rareté.</summary>
    public static double Value(Colony colony, ResourceType good) => Cost(colony, good) * Scarcity(colony, good);

    /// <summary>Ce que la colonie peut vendre sans se priver : le stock au-delà d'un quart de plus que ses besoins.</summary>
    public static int Surplus(Colony colony, ResourceType good) =>
        (int)Math.Max(0f, colony.Stock.Get(good) - Need(colony, good) * 1.25f);

    /// <summary>Ce qui lui manque pour ses besoins.</summary>
    public static int Shortage(Colony colony, ResourceType good) =>
        (int)Math.Max(0f, MathF.Ceiling(Need(colony, good) - colony.Stock.Get(good)));

    /// <summary>
    /// La négociation d'un bien du vendeur vers l'acheteur : combien d'unités s'échangent, et à quel prix.
    /// Chaque unité est vendue tant que l'acheteur lui accorde plus de valeur que le vendeur (marge comprise).
    /// </summary>
    public static Clearing Clear(Colony seller, Colony buyer, ResourceType good, int maxUnits)
    {
        int held = seller.Stock.Get(good), owned = buyer.Stock.Get(good);
        int units = 0;
        double lastSeller = 0, lastBuyer = 0, gain = 0;
        for (int k = 0; k < maxUnits && held - k > 0; k++)
        {
            double sellerValue = KeepValue(seller, good, held - k);
            double buyerValue = UseValue(buyer, good, owned + k);
            if (buyerValue <= sellerValue * (1 + MinValueGap) + 0.01)
                break;
            units++;
            gain += buyerValue - sellerValue;
            (lastSeller, lastBuyer) = (sellerValue, buyerValue);
        }
        return units == 0 ? Clearing.None : new Clearing(units, (lastSeller + lastBuyer) / 2, gain);
    }

    /// <summary>Combien d'unités à ce prix la colonie vendrait-elle, au plus ? (Jamais en dessous de ce que lui coûte chaque unité cédée.)</summary>
    public static int UnitsWillingToSell(Colony colony, ResourceType good, double price, int maxUnits)
    {
        int held = colony.Stock.Get(good), units = 0;
        while (units < maxUnits && held - units > 0 && KeepValue(colony, good, held - units) <= price + 0.01)
            units++;
        return units;
    }

    /// <summary>Combien d'unités à ce prix la colonie achèterait-elle, au plus ? (Jamais au-dessus de ce qu'elles valent pour elle.)</summary>
    public static int UnitsWillingToBuy(Colony colony, ResourceType good, double price, int maxUnits)
    {
        int owned = colony.Stock.Get(good), units = 0;
        while (units < maxUnits && UseValue(colony, good, owned + units) >= price - 0.01)
            units++;
        return units;
    }
}
