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
        ResourceType.Wool, ResourceType.Clothes, ResourceType.Salt, ResourceType.Spices, ResourceType.Hardwood,
        ResourceType.Eggs, ResourceType.Milk, ResourceType.SaltedMeat, ResourceType.Beer,
        ResourceType.Chickens, ResourceType.Sheep, ResourceType.Cows,
        ResourceType.MineralCoal, ResourceType.Clay, ResourceType.Pottery, ResourceType.CopperOre, ResourceType.Copper, ResourceType.Copperware,
        ResourceType.Flax, ResourceType.Linen, ResourceType.Hides, ResourceType.Leather, ResourceType.Shoes, ResourceType.Grapes, ResourceType.Wine,
        ResourceType.GoldOre, ResourceType.Gold, ResourceType.Ruby, ResourceType.Sapphire, ResourceType.Emerald, ResourceType.Diamond, ResourceType.Jewelry,
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
        ResourceType.Bread => 6.7,
        ResourceType.Flour => 4.6,
        ResourceType.Wood => 0.5,
        ResourceType.Stone => 3.5,
        ResourceType.IronOre => 5.5,
        ResourceType.Charcoal => 5.0,
        ResourceType.Iron => 48.0,
        ResourceType.Tools => 129.0,
        ResourceType.Wool => 5.0,
        ResourceType.Clothes => 25.0,
        ResourceType.Eggs => 4.0,
        ResourceType.SaltedMeat => 3.0,
        // Une fournée : 50 céréales (50 × 1,5 h) et deux heures de travail, pour 40 chopes.
        ResourceType.Beer => (Cuisine.BeerRecipe.Inputs[0].Amount * 1.5 + 2.0) / Cuisine.BeerRecipe.OutputAmount,
        ResourceType.Milk => 5.0,
        ResourceType.Salt => 4.0,
        ResourceType.Spices => 6.0,
        ResourceType.Hardwood => 5.0,
        // Les bêtes valent une fortune : on échange leurs produits, pas elles (voir Husbandry.LivestockWealthFactor).
        ResourceType.Chickens => 100.0,
        ResourceType.Sheep => 250.0,
        ResourceType.Cows => 600.0,
        _ => (int)good >= 27 ? ResourceCatalog.ReferenceCost(good) : 1.0,
    };

    /// <summary>Heures de travail que coûte une unité à cette colonie : son registre, ou à défaut la référence majorée.</summary>
    public static double Cost(Colony colony, ResourceType good) =>
        // Une bête coûte plus ou moins cher selon que son espèce est rare ou répandue dans la région : le travail n'y est pour rien.
        Husbandry.IsLivestock(good) ? BaselineCost(good) * Husbandry.CostFactor(colony.Map.Biome, good)
        : colony.Labor.HoursPerUnit(good) ?? BaselineCost(good) * UnprovenFactor;

    /// <summary>La quantité que la colonie voudrait avoir en réserve pour ses propres besoins (0 si le bien ne lui sert à rien).</summary>
    public static float Need(Colony colony, ResourceType good)
    {
        if ((int)good >= 27) return ExtendedIndustry.Target(colony, good);
        float daily = Math.Max(1, colony.PresentMembers.Count) * ColonyBrain.MealsPerColonistPerDay;
        ChainDemand iron = ToolChain.Demand(colony);
        return good switch
        {
            ResourceType.Grain => daily * GrainDays,
            ResourceType.Bread => colony.Buildings.Any(b => b.Type == BuildingType.Oven) ? FoodChain.Demand(colony).BreadTarget : 0f,
            ResourceType.Flour => colony.Buildings.Any(b => b.Type == BuildingType.Mill) ? FoodChain.Demand(colony).FlourTarget : 0f,
            ResourceType.Wood => ColonyBrain.HeatingTarget(colony, colony.Clock.Season) + 10f + Offerings.Need(colony, ResourceType.Wood),
            ResourceType.Stone => ColonyBrain.StoneReserveTarget + Offerings.Need(colony, ResourceType.Stone),
            // Réserves de fabrication ; un surplus de minerai peut ravitailler les autres établissements.
            ResourceType.IronOre => iron.Active ? Math.Max(0, iron.IronTarget - colony.Stock.Get(ResourceType.Iron)) * 3 : 0f,
            ResourceType.Charcoal => Math.Max(iron.CharcoalTarget, ExtendedIndustry.FuelTarget(colony)),
            ResourceType.Iron => iron.Active ? iron.IronTarget : 0f,
            ResourceType.Tools => ToolChain.ToolsWanted(colony),
            // Le textile : de la laine pour le métier à tisser, et un vêtement pour chacun.
            ResourceType.Wool => colony.Buildings.Any(b => b.Type == BuildingType.Loom)
                ? Math.Max(0, 2 * (Husbandry.ClothesTarget(colony) - colony.Stock.Get(ResourceType.Clothes))) : 0f,
            ResourceType.Clothes => Husbandry.ClothesTarget(colony),
            // Les produits de l'élevage : une petite réserve que la colonie aimerait avoir, le reste se vend.
            ResourceType.Eggs or ResourceType.Milk => Math.Max(2f, colony.PresentMembers.Count * 0.6f),
            // La viande salée : trois jours de repas en réserve, le reste se vend.
            ResourceType.SaltedMeat => daily * 3f,
            // La bière : une chope par habitant, mais seulement pour une colonie qui a une taverne.
            ResourceType.Beer => colony.Buildings.Any(b => b.Type == BuildingType.Tavern) ? colony.PresentMembers.Count : 0f,
            // Les bêtes : de quoi remplir l'enclos, mais seule une colonie très riche songe à en acheter.
            ResourceType.Chickens or ResourceType.Sheep or ResourceType.Cows =>
                Husbandry.CanAffordLivestock(colony, good) ? Math.Max(0, Husbandry.CapacityOf(colony, good) - Husbandry.Count(colony, good)) : 0f,
            // Les denrées de négoce : la région produit la sienne et manque des deux autres.
            ResourceType.Salt or ResourceType.Spices or ResourceType.Hardwood =>
                Specialties.NativeOf(colony) == good ? 0f : Specialties.ImportNeed(colony),
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
        (int)Math.Max(0f, colony.Stock.Available(good) - Need(colony, good) * 1.25f);

    /// <summary>Ce qui lui manque pour ses besoins.</summary>
    public static int Shortage(Colony colony, ResourceType good) =>
        (int)Math.Max(0f, MathF.Ceiling(Need(colony, good) - colony.Stock.Get(good)));

    /// <summary>
    /// La négociation d'un bien du vendeur vers l'acheteur : combien d'unités s'échangent, et à quel prix.
    /// Chaque unité est vendue tant que l'acheteur lui accorde plus de valeur que le vendeur (marge comprise).
    /// </summary>
    public static Clearing Clear(Colony seller, Colony buyer, ResourceType good, int maxUnits)
    {
        int held = seller.Stock.Available(good), owned = buyer.Stock.Available(good);
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
        int held = colony.Stock.Available(good), units = 0;
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
