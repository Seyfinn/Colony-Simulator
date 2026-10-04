using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'élevage et le textile : un enclos (ou plusieurs, quand le troupeau déborde) abrite des poules, des moutons et des vaches. Les poules donnent des œufs, les vaches du lait
/// (tous deux se mangent), les moutons de la laine ; le métier à tisser en fait des vêtements, qui protègent du froid et des fièvres.
/// Les vaches tirent aussi la charrue : les champs se travaillent plus vite.
/// L'été les bêtes paissent ; l'hiver (et pendant une sécheresse) il faut leur donner du grain, sinon elles dépérissent.
/// Les produits s'accumulent à l'enclos jusqu'à ce qu'un colon vienne les ramasser.
///
/// <b>Reproduction</b> : il faut au moins une paire pour que l'espèce se multiplie, et le troupeau croît avec le nombre de paires.
/// <b>Abattage</b> : une bête abattue donne énormément de viande (<see cref="MeatYield"/>) mais ne pond plus, ne donne plus de lait ni de laine et ne
/// se reproduit plus : c'est un coût d'opportunité. La colonie ne se résout donc à abattre que pour un vrai besoin ou un vrai surplus
/// (voir <see cref="PlanSlaughter"/>), jamais au point de briser la dernière paire de reproducteurs.
///
/// Chaque espèce est plus ou moins répandue selon la région (<see cref="Abundance"/>) : là où elle est rare, le troupeau de départ est maigre
/// ou absent, il se multiplie lentement et la bête coûte une fortune. Ce sont donc leurs produits (œufs, lait, laine, vêtements) que les colonies
/// s'échangent : une région sans vaches achète son lait à celle qui en a. Les bêtes elles-mêmes ne s'achètent qu'à prix d'or, et seule une colonie
/// très prospère (<see cref="LivestockWealthFactor"/>) y songe ; celle dont l'enclos déborde de petits les garde pour ces rares acheteurs.
/// </summary>
public static class Husbandry
{
    /// <summary>Poules et moutons qu'un enclos peut abriter (chaque espèce séparément) ; il y a moins de place pour les vaches.</summary>
    public const int PenCapacity = 8;
    public const int CowsPerPen = 4;

    /// <summary>Jeunes bêtes qu'une colonie garde en réserve, à vendre, quand son enclos est plein.</summary>
    public const int MaxSpareAnimals = 4;

    /// <summary>Une colonie ne songe à acheter une bête que si elle a en caisse ce multiple de son prix.</summary>
    public const float LivestockWealthFactor = 2.5f;

    /// <summary>
    /// Au-delà de ce stock, on ne se donne plus la peine de ramasser le produit : il reste à l'enclos, qui le garde sans qu'il se gâte,
    /// plutôt que de pourrir au grenier.
    /// </summary>
    private const int EggsCeiling = 24, MilkCeiling = 16, WoolCeiling = 40;

    /// <summary>Repas que donne une bête abattue : une poule 6, un mouton 20, une vache 60.</summary>
    public static int MeatYield(ResourceType species) => species switch
    {
        ResourceType.Chickens => 6,
        ResourceType.Sheep => 20,
        _ => 60,
    };

    /// <summary>Reproducteurs qu'on garde toujours : une paire par espèce.</summary>
    public const int BreedingCore = 2;

    /// <summary>On ne songe à l'abattage de besoin que sous ce nombre de jours de vivres, et l'on vise alors <see cref="EmergencyTargetDays"/> jours.</summary>
    public const float EmergencyFoodDays = 2f, EmergencyTargetDays = 4f;

    /// <summary>Bêtes qu'on abat au plus chaque jour, par espèce : on ne vide pas l'enclos en une matinée.</summary>
    private static int DailyLimit(ResourceType species) => species switch
    {
        ResourceType.Chickens => 3,
        ResourceType.Sheep => 2,
        _ => 1,
    };

    /// <summary>Un vêtement dure environ trois ans.</summary>
    private const float ClothesLifeDays = 60f;

    /// <summary>Vitesse de travail des champs gagnée par vache attelée (jusqu'à quatre).</summary>
    private const float PloughBonusPerCow = 0.06f;

    /// <summary>Deux laines tissent un vêtement.</summary>
    public static readonly Recipe Weaving = new(BuildingType.Loom, [(ResourceType.Wool, 2)], ResourceType.Clothes, 1, 20f);

    public static readonly ResourceType[] Species = [ResourceType.Chickens, ResourceType.Sheep, ResourceType.Cows];

    public static bool IsLivestock(ResourceType good) => good is ResourceType.Chickens or ResourceType.Sheep or ResourceType.Cows;

    // --- Régions ---

    /// <summary>
    /// À quel point l'espèce est répandue dans le biome : 1 est l'ordinaire, 1,5 et plus l'abondance, 0,3 et moins la rareté.
    /// Les poules aiment les prairies et les forêts douces, les moutons la steppe et les pays froids,
    /// les vaches l'herbe grasse ; le désert, la glace et la jungle en manquent.
    /// </summary>
    public static float Abundance(Biome biome, ResourceType species) => (biome, species) switch
    {
        (Biome.Grassland, ResourceType.Chickens) => 1.4f,
        (Biome.Grassland, ResourceType.Sheep) => 1.2f,
        (Biome.Grassland, ResourceType.Cows) => 1.6f,
        (Biome.TemperateForest, ResourceType.Chickens) => 1.3f,
        (Biome.TemperateForest, ResourceType.Sheep) => 0.8f,
        (Biome.TemperateForest, ResourceType.Cows) => 1.2f,
        (Biome.Steppe, ResourceType.Chickens) => 0.5f,
        (Biome.Steppe, ResourceType.Sheep) => 1.7f,
        (Biome.Steppe, ResourceType.Cows) => 0.8f,
        (Biome.Savanna, ResourceType.Chickens) => 1.2f,
        (Biome.Savanna, ResourceType.Sheep) => 0.6f,
        (Biome.Savanna, ResourceType.Cows) => 0.5f,
        (Biome.TropicalForest, ResourceType.Chickens) => 1.5f,
        (Biome.TropicalForest, ResourceType.Sheep) => 0.25f,
        (Biome.TropicalForest, ResourceType.Cows) => 0.25f,
        (Biome.Swamp, ResourceType.Chickens) => 1.0f,
        (Biome.Swamp, ResourceType.Sheep) => 0.25f,
        (Biome.Swamp, ResourceType.Cows) => 0.35f,
        (Biome.Desert, ResourceType.Chickens) => 0.4f,
        (Biome.Desert, ResourceType.Sheep) => 0.8f,
        (Biome.Desert, ResourceType.Cows) => 0.15f,
        (Biome.Tundra, ResourceType.Chickens) => 0.3f,
        (Biome.Tundra, ResourceType.Sheep) => 1.1f,
        (Biome.Tundra, ResourceType.Cows) => 0.3f,
        (Biome.BorealForest, ResourceType.Chickens) => 0.5f,
        (Biome.BorealForest, ResourceType.Sheep) => 1.1f,
        (Biome.BorealForest, ResourceType.Cows) => 0.7f,
        (_, ResourceType.Chickens) => 0.2f,
        (_, ResourceType.Sheep) => 0.4f,
        _ => 0.1f,
    };

    public static float Abundance(Colony colony, ResourceType species) => Abundance(colony.Map.Biome, species);

    private static int HerdBase(ResourceType species) => species switch
    {
        ResourceType.Chickens => 4,
        ResourceType.Sheep => 3,
        _ => 2,
    };

    /// <summary>Le troupeau qu'on trouve dans la région quand l'enclos s'ouvre : fourni là où l'espèce abonde, absent là où elle manque.</summary>
    public static int StartingHerd(Biome biome, ResourceType species) => (int)MathF.Round(HerdBase(species) * Abundance(biome, species));

    /// <summary>
    /// Le prix relatif d'une bête dans la région : une espèce rare coûte jusqu'à trois fois plus, une espèce abondante un peu moins.
    /// Les prix de base sont déjà très élevés (voir <see cref="Economy.BaselineCost"/>).
    /// </summary>
    public static float CostFactor(Biome biome, ResourceType species) => Math.Clamp(1f / Abundance(biome, species), 0.8f, 3f);

    /// <summary>La colonie est assez riche pour acheter cette bête, à son prix de fortune.</summary>
    public static bool CanAffordLivestock(Colony colony, ResourceType species) =>
        colony.Stock.Get(ResourceType.Coins) >= LivestockWealthFactor * Economy.Cost(colony, species);

    // --- Troupeau ---

    public static int Pens(Colony colony) => colony.Buildings.Count(b => b.Type == BuildingType.Pen && b.IsComplete);

    public static int PerPen(ResourceType species) => species == ResourceType.Cows ? CowsPerPen : PenCapacity;

    /// <summary>Capacité des enclos pour les poules et les moutons (voir <see cref="CapacityOf"/> pour une espèce donnée).</summary>
    public static int Capacity(Colony colony) => Pens(colony) * PenCapacity;

    public static int CapacityOf(Colony colony, ResourceType species) => Pens(colony) * PerPen(species);

    public static int Count(Colony colony, ResourceType species) => species switch
    {
        ResourceType.Chickens => colony.Chickens,
        ResourceType.Sheep => colony.Sheep,
        _ => colony.Cows,
    };

    private static void SetCount(Colony colony, ResourceType species, int count)
    {
        switch (species)
        {
            case ResourceType.Chickens: colony.Chickens = count; break;
            case ResourceType.Sheep: colony.Sheep = count; break;
            default: colony.Cows = count; break;
        }
    }

    public static int Animals(Colony colony) => colony.Chickens + colony.Sheep + colony.Cows;

    /// <summary>Une vache mange pour deux : la ration d'hiver se compte en « bouches ».</summary>
    private static int Mouths(Colony colony) => colony.Chickens + colony.Sheep + 2 * colony.Cows;

    /// <summary>L'enclos est achevé : les premières bêtes de la région s'y installent.</summary>
    public static void OnPenBuilt(Colony colony, GameClock clock)
    {
        if (Animals(colony) > 0)
        {
            if (Pens(colony) > 1)
                ColonyBrain.Say(colony, clock, $"Un enclos de plus est achevé : la colonie peut garder jusqu'à {CapacityOf(colony, ResourceType.Chickens)} poules, "
                    + $"{CapacityOf(colony, ResourceType.Sheep)} moutons et {CapacityOf(colony, ResourceType.Cows)} vaches.");
            return;
        }
        Biome biome = colony.Map.Biome;
        foreach (ResourceType species in Species)
            SetCount(colony, species, Math.Min(StartingHerd(biome, species), CapacityOf(colony, species)));

        string herd = string.Join(", ", Species.Where(s => Count(colony, s) > 0).Select(s => $"{Count(colony, s)} {Trade.GoodName(s, Count(colony, s))}"));
        string missing = string.Join(" et ", Species.Where(s => Count(colony, s) == 0).Select(s => Trade.GoodName(s, 2)));
        ColonyBrain.Say(colony, clock, (herd.Length > 0 ? $"L'enclos est achevé : {herd} s'y installent." : "L'enclos est achevé, mais il est vide.")
            + (missing.Length > 0 ? $" Il n'y a pas de {missing} dans la région : leurs produits viendront des voisines, car les bêtes elles-mêmes coûtent une fortune." : ""));
    }

    /// <summary>Chaque matin : accueillir les bêtes achetées, nourrir le troupeau, le voir se multiplier, laisser les produits s'accumuler ; les vêtements s'usent.</summary>
    public static void Daily(Colony colony, GameClock clock)
    {
        WearClothes(colony, clock);
        if (Pens(colony) == 0)
            return;
        Season season = clock.Season;

        // Les bêtes achetées ou nées en surplus entrent à l'enclos dès qu'il y a de la place.
        foreach (ResourceType species in Species)
        {
            int take = Math.Min(CapacityOf(colony, species) - Count(colony, species), colony.Stock.Get(species));
            if (take > 0 && colony.Stock.TryTake(species, take))
                SetCount(colony, species, Count(colony, species) + take);
        }

        // Hiver et sécheresse : plus d'herbe, il faut du grain. Faute de quoi une bête dépérit.
        bool fed = true;
        if (season == Season.Hiver || colony.DroughtDaysLeft > 0)
        {
            int grain = (Mouths(colony) + 5) / 6;
            if (grain > 0 && !colony.Stock.TryTake(ResourceType.Grain, grain))
            {
                fed = false;
                // Une bête qu'on ne peut plus nourrir est abattue plutôt que de dépérir : on sauve au moins sa viande.
                ResourceType starving = Species.OrderByDescending(s => Count(colony, s)).First();
                if (Count(colony, starving) > 0)
                {
                    SetCount(colony, starving, Count(colony, starving) - 1);
                    colony.Stock.Add(ResourceType.Meat, MeatYield(starving));
                    ColonyBrain.Say(colony, clock, $"Faute de grain pour les bêtes, nous abattons {Indefinite(starving)} plutôt que de la laisser dépérir : {MeatYield(starving)} repas de viande.");
                }
            }
        }

        // Elles se multiplient hors de l'hiver (les moutons et les vaches seulement au printemps et en été), d'autant plus vite
        // que l'espèce est répandue dans la région. Enclos plein : les petits sont gardés pour la vente.
        if (fed && season != Season.Hiver)
        {
            Breed(colony, ResourceType.Chickens, 0.12f);
            if (season is Season.Printemps or Season.Ete)
            {
                Breed(colony, ResourceType.Sheep, 0.07f);
                Breed(colony, ResourceType.Cows, 0.045f);
            }
        }

        int penChickens = Pens(colony) * PenCapacity;
        float laying = season == Season.Hiver ? 0.3f : 1f;
        colony.EggsReady = Math.Min(colony.EggsReady + colony.Chickens * 0.5f * laying, penChickens * 1.5f);
        colony.WoolReady = Math.Min(colony.WoolReady + colony.Sheep * 0.2f, penChickens * 0.75f);
        colony.MilkReady = Math.Min(colony.MilkReady + colony.Cows * 0.8f * (season == Season.Hiver ? 0.4f : 1f), Pens(colony) * CowsPerPen * 2f);

        PlanSlaughter(colony, clock);
    }

    private static void Breed(Colony colony, ResourceType species, float rate)
    {
        float growth = species switch
        {
            ResourceType.Chickens => colony.ChickenGrowth,
            ResourceType.Sheep => colony.SheepGrowth,
            _ => colony.CowGrowth,
        };
        // Il faut une paire : une bête seule ne donne rien, deux en donnent autant que deux bêtes d'un grand troupeau.
        growth += Count(colony, species) / 2 * 2f * rate * Abundance(colony, species);
        while (growth >= 1f)
        {
            growth -= 1f;
            if (Count(colony, species) < CapacityOf(colony, species))
                SetCount(colony, species, Count(colony, species) + 1);
            else if (colony.Stock.Get(species) < MaxSpareAnimals)
                colony.Stock.Add(species, 1);
        }
        switch (species)
        {
            case ResourceType.Chickens: colony.ChickenGrowth = growth; break;
            case ResourceType.Sheep: colony.SheepGrowth = growth; break;
            default: colony.CowGrowth = growth; break;
        }
    }

    private static void WearClothes(Colony colony, GameClock clock)
    {
        int clothes = colony.Stock.Get(ResourceType.Clothes);
        if (clothes == 0)
            return;
        colony.ClothesWear += clothes / ClothesLifeDays;
        if (colony.ClothesWear >= 1f)
        {
            colony.ClothesWear -= 1f;
            colony.Stock.TryTake(ResourceType.Clothes, 1);
        }
    }

    /// <summary>Les vaches attelées accélèrent les labours, les semailles et la moisson.</summary>
    public static float PloughFactor(Colony colony) => 1f + PloughBonusPerCow * Math.Min(colony.Cows, CowsPerPen);

    /// <summary>Y a-t-il quelque chose à ramasser à l'enclos ?</summary>
    public static bool WorkPending(Colony colony) =>
        Pens(colony) > 0 && (Ready(colony.WoolReady, colony, ResourceType.Wool, WoolCeiling)
            || Ready(colony.EggsReady, colony, ResourceType.Eggs, EggsCeiling) || Ready(colony.MilkReady, colony, ResourceType.Milk, MilkCeiling));

    private static bool Ready(float waiting, Colony colony, ResourceType product, int ceiling) => waiting >= 1f && colony.Stock.Get(product) < ceiling;

    public static Building? PenSite(Colony colony) => colony.Buildings.FirstOrDefault(b => b.Type == BuildingType.Pen && b.IsComplete);

    /// <summary>L'enclos achevé le plus proche d'une case : la colonie peut en avoir plusieurs.</summary>
    public static Building? NearestPen(Colony colony, int x, int y) => colony.Buildings
        .Where(b => b.Type == BuildingType.Pen && b.IsComplete)
        .OrderBy(b => Math.Abs(b.X - x) + Math.Abs(b.Y - y)).FirstOrDefault();

    /// <summary>Ramasse le produit de l'enclos : le lait d'abord (il tourne vite), puis la laine, puis les œufs. Renvoie ce que le colon rapporte au camp.</summary>
    internal static (ResourceType Type, int Amount)? Collect(Colony colony)
    {
        if (Ready(colony.MilkReady, colony, ResourceType.Milk, MilkCeiling))
        {
            int milk = (int)Math.Min(colony.MilkReady, 6f);
            colony.MilkReady -= milk;
            return (ResourceType.Milk, milk);
        }
        if (Ready(colony.WoolReady, colony, ResourceType.Wool, WoolCeiling))
        {
            int wool = (int)Math.Min(colony.WoolReady, 4f);
            colony.WoolReady -= wool;
            return (ResourceType.Wool, wool);
        }
        if (Ready(colony.EggsReady, colony, ResourceType.Eggs, EggsCeiling))
        {
            int eggs = (int)Math.Min(colony.EggsReady, 6f);
            colony.EggsReady -= eggs;
            return (ResourceType.Eggs, eggs);
        }
        return null;
    }

    // --- Plusieurs enclos ---

    /// <summary>Un enclos pour huit habitants, au plus six.</summary>
    public const int MembersPerPen = 8, MaxPens = 6;

    /// <summary>
    /// Une espèce déborde : son enclos est plein et les petits en réserve sont déjà quatre, faute de place et d'acheteur (les bêtes valent trop cher).
    /// </summary>
    public static bool Saturated(Colony colony) =>
        Pens(colony) > 0 && Species.Any(s => Count(colony, s) >= CapacityOf(colony, s) && colony.Stock.Get(s) >= MaxSpareAnimals);

    /// <summary>
    /// La colonie bâtit un enclos de plus quand les premiers débordent, qu'elle est assez grande pour s'en occuper (un enclos pour huit habitants, six au plus),
    /// que les vivres sont confortables et que son grain suffit à nourrir un troupeau plus grand l'hiver. Un seul chantier à la fois.
    /// </summary>
    public static bool WantsAnotherPen(Colony colony)
    {
        int pens = colony.Buildings.Count(b => b.Type == BuildingType.Pen);
        return pens > 0 && pens == Pens(colony) && pens < Math.Clamp(colony.Members.Count / MembersPerPen, 1, MaxPens)
            && Saturated(colony) && colony.Stock.Get(ResourceType.Grain) >= Mouths(colony) * 2 && (colony.Sensors?.FoodDays ?? 9f) >= 4f;
    }

    // --- L'abattage ---

    public static string Indefinite(ResourceType species) => species switch
    {
        ResourceType.Chickens => "une poule",
        ResourceType.Sheep => "un mouton",
        _ => "une vache",
    };

    /// <summary>Bêtes qu'on peut abattre sans toucher aux reproducteurs : les petits en réserve d'abord, puis ce qui dépasse la paire.</summary>
    public static int Available(Colony colony, ResourceType species) => colony.Stock.Get(species) + Math.Max(0, Count(colony, species) - BreedingCore);

    public static bool CanSlaughter(Colony colony, ResourceType species) => Available(colony, species) > 0;

    public static bool SlaughterPending(Colony colony) => colony.SlaughterOrders.Values.Sum() > 0;

    /// <summary>L'espèce dont un colon doit s'occuper maintenant, ou null (un ordre n'est confié qu'à un colon à la fois).</summary>
    public static ResourceType? NextSlaughter(Colony colony)
    {
        foreach ((ResourceType species, int orders) in colony.SlaughterOrders)
        {
            int busy = colony.Members.Count(m => m.Activity is { Kind: ActivityKind.Slaughter } activity && activity.Species == species);
            if (orders - busy > 0 && CanSlaughter(colony, species))
                return species;
        }
        return null;
    }

    /// <summary>Abat une bête : un petit en réserve d'abord (c'est le surplus), sinon une bête du troupeau au-delà de la paire. Renvoie la viande obtenue.</summary>
    internal static int Slaughter(Colony colony, ResourceType species)
    {
        if (!colony.Stock.TryTake(species, 1))
        {
            if (Count(colony, species) <= BreedingCore)
                return 0;
            SetCount(colony, species, Count(colony, species) - 1);
        }
        if (colony.SlaughterOrders.TryGetValue(species, out int orders))
        {
            if (orders <= 1)
                colony.SlaughterOrders.Remove(species);
            else
                colony.SlaughterOrders[species] = orders - 1;
        }
        return MeatYield(species);
    }

    private static int Order(Colony colony, ResourceType species, int wanted)
    {
        int already = colony.SlaughterOrders.GetValueOrDefault(species);
        int count = Math.Min(wanted, Math.Min(Available(colony, species), DailyLimit(species)) - already);
        if (count <= 0)
            return 0;
        colony.SlaughterOrders[species] = already + count;
        return count;
    }

    /// <summary>
    /// Chaque matin, la colonie décide s'il faut abattre, et quoi. Une bête abattue ne produira plus rien et ne se reproduira plus : on n'abat donc que dans trois cas.
    /// <list type="number">
    /// <item><b>Un petit en trop</b> : l'enclos est plein et les petits en réserve sont déjà quatre, sans acheteur (les bêtes valent trop cher) ;
    /// leur croissance serait perdue. À condition que la viande serve : mangée dans la journée ou salée (sans sel, elle pourrirait pour rien).</item>
    /// <item><b>L'hiver qui vient</b> : à l'automne, si le grain ne suffit pas à nourrir tout le troupeau jusqu'au printemps, on abat les bouches
    /// en trop, en commençant par celles dont la perte coûte le moins par bouche épargnée (sous la même condition sur la viande).</item>
    /// <item><b>Un vrai besoin</b> : il reste moins de deux jours de vivres ; on vise quatre jours et l'on abat les bêtes qui rapportent le plus de viande
    /// pour ce qu'elles valent (prix de remplacement par repas), en laissant toujours la paire de reproducteurs.</item>
    /// </list>
    /// </summary>
    public static void PlanSlaughter(Colony colony, GameClock clock)
    {
        colony.SlaughterOrders.Clear();
        if (Pens(colony) == 0)
            return;
        float dailyMeals = Math.Max(1, colony.Members.Count) * ColonyBrain.MealsPerColonistPerDay;
        float foodDays = colony.Stock.FoodUnits / dailyMeals;

        // La viande utile : ce que la colonie mangera pendant le délai où la viande fraîche se garde (3 jours, 7 avec un entrepôt),
        // plus ce que son sel peut conserver, moins ce qu'elle a déjà.
        float usable = dailyMeals * Civic.MeatShelfDays(colony) + colony.Stock.Get(ResourceType.Salt) * Civic.MeatPerSalt - colony.Stock.Get(ResourceType.Meat);

        // 1. Les petits que personne n'achètera ni ne logera.
        bool surplus = false;
        foreach (ResourceType species in Species)
            if (Count(colony, species) >= CapacityOf(colony, species) && colony.Stock.Get(species) >= MaxSpareAnimals
                && MeatYield(species) <= usable && Order(colony, species, 1) > 0)
            {
                surplus = true;
                usable -= MeatYield(species);
            }

        // 2. L'automne : le grain ne nourrira pas tout le troupeau l'hiver.
        bool winter = false;
        if (clock.Season == Season.Automne)
        {
            int feedable = colony.Stock.Get(ResourceType.Grain) * 6 / TimeConstants.DaysPerSeason;
            int excess = Mouths(colony) - feedable;
            foreach (ResourceType species in Species.OrderBy(s => Economy.Cost(colony, s) / (s == ResourceType.Cows ? 2 : 1)))
            {
                if (excess <= 0)
                    break;
                int mouths = species == ResourceType.Cows ? 2 : 1;
                int killed = Order(colony, species, Math.Min((excess + mouths - 1) / mouths, (int)(usable / MeatYield(species))));
                excess -= killed * mouths;
                usable -= killed * MeatYield(species);
                winter |= killed > 0;
            }
        }

        // 3. La faim : la bête la moins chère à remplacer par repas obtenu.
        bool hunger = false;
        if (foodDays < EmergencyFoodDays)
        {
            float deficit = dailyMeals * EmergencyTargetDays - colony.Stock.FoodUnits;
            foreach (ResourceType species in Species.OrderBy(s => Economy.Cost(colony, s) / MeatYield(s)))
            {
                if (deficit <= 0f)
                    break;
                int killed = Order(colony, species, (int)MathF.Ceiling(deficit / MeatYield(species)));
                deficit -= killed * MeatYield(species);
                hunger |= killed > 0;
            }
        }

        if (!SlaughterPending(colony))
            return;
        string what = string.Join(" et ", colony.SlaughterOrders.Select(o => $"{o.Value} {Trade.GoodName(o.Key, o.Value)}"));
        ColonyBrain.Say(colony, clock, hunger ? $"Il ne reste que {foodDays:0.#} jours de vivres : nous abattons {what} pour nourrir la colonie."
            : winter ? $"L'hiver approche et le grain ne nourrira pas tout le troupeau : nous abattons {what}."
            : surplus ? $"L'enclos déborde de petits que personne n'achète : nous abattons {what} pour la viande."
            : $"Nous abattons {what}.");
    }

    // --- Le textile ---

    /// <summary>Un vêtement pour chacun, plus ceux que les voisines achèteraient.</summary>
    public static int ClothesTarget(Colony colony) => colony.Members.Count + colony.ExportInterest.GetValueOrDefault(ResourceType.Clothes);

    /// <summary>La part de la colonie qui a de quoi s'habiller chaudement, de 0 à 1.</summary>
    public static float ClothesCoverage(Colony colony)
    {
        int people = colony.Members.Count;
        return people == 0 ? 0f : Math.Min(1f, colony.Stock.Get(ResourceType.Clothes) / (float)people);
    }

    /// <summary>Tisser dès qu'il y a de la laine et que tout le monde n'est pas encore habillé.</summary>
    public static Building? PickLoomJob(Colony colony)
    {
        if (colony.Workshops(BuildingType.Loom).FirstOrDefault() is not { } loom)
            return null;
        Stockpile stock = colony.Stock;
        if (stock.Get(ResourceType.Wool) < Weaving.Inputs[0].Amount)
            return null;
        return stock.Get(ResourceType.Clothes) + Crafting.Pending(colony, ResourceType.Clothes) < ClothesTarget(colony) ? loom : null;
    }
}
