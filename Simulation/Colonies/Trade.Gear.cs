using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'équipement des caravanes et les dangers de la route. Une colonie qui a des moyens et un vrai besoin améliore ses caravanes : des porteurs, puis une
/// charrette de bois, une charrette renforcée de fer, enfin des bêtes de trait (bœufs ou chevaux apprivoisés, qui partent avec la caravane et mangent du grain).
/// Plus de capacité, plus de vitesse. Sur la route, une interception reste très rare : elle dépend de la distance aux colonies, des prédateurs et de l'état de la route.
/// </summary>
public static partial class Trade
{
    /// <summary>Capacité : ×1 (porteurs), ×2 (charrette de bois), ×3 (charrette de fer), ×4 (bêtes de trait).</summary>
    public static int GearCapacityFactor(CaravanGear gear) => gear switch
    {
        CaravanGear.WoodCart => 2,
        CaravanGear.IronCart => 3,
        CaravanGear.Draft => 4,
        _ => 1,
    };

    /// <summary>Vitesse : ×1 (porteurs), ×0,9 (charrette de bois), ×1 (charrette de fer), ×1,2 avec des bœufs et ×1,5 avec des chevaux.</summary>
    public static float GearSpeedFactor(CaravanGear gear, ResourceType? draft = null) => gear switch
    {
        CaravanGear.WoodCart => 0.9f,
        CaravanGear.Draft => draft == ResourceType.Horses ? 1.5f : draft == ResourceType.Oxen ? 1.2f : 1.4f,
        _ => 1f,
    };

    /// <summary>Deux bêtes de trait font une équipe ; elles mangent chacune un grain par jour de voyage.</summary>
    public const int DraftTeam = 2;

    /// <summary>Grain gardé en réserve avant d'oser partir avec des bêtes de trait.</summary>
    private const int DraftGrainReserve = 20;

    /// <summary>Les bêtes de trait que le village principal peut atteler : des chevaux d'abord (plus rapides), sinon des bœufs.</summary>
    internal static ResourceType? DraftAvailable(Colony colony)
    {
        Settlement home = colony.PrimarySettlement;
        if (home.Horses >= DraftTeam) return ResourceType.Horses;
        return home.Oxen >= DraftTeam ? ResourceType.Oxen : null;
    }

    /// <summary>
    /// L'équipement réellement utilisable aujourd'hui : le niveau atteint par la colonie, ramené à une charrette de fer si les bêtes de trait manquent ou si le grain
    /// de leur ration n'est pas en réserve.
    /// </summary>
    public static CaravanGear EffectiveGear(Colony colony) => EffectiveGear(colony, out _);

    internal static CaravanGear EffectiveGear(Colony colony, out ResourceType? draft)
    {
        draft = null;
        if (colony.CaravanGear != CaravanGear.Draft)
            return colony.CaravanGear;
        draft = DraftAvailable(colony);
        if (draft is null || colony.PrimarySettlement.Stock.Available(ResourceType.Grain) < DraftGrainReserve)
        {
            draft = null;
            return CaravanGear.IronCart;
        }
        return CaravanGear.Draft;
    }

    /// <summary>
    /// La charge d'un voyage de ravitaillement interne est multipliée par l'équipement de la colonie ; les bêtes de trait, qui ne partent qu'avec une caravane de commerce,
    /// comptent ici comme une charrette renforcée de fer.
    /// </summary>
    internal static int SupplyCapacityFactor(Colony colony) => GearCapacityFactor((CaravanGear)Math.Min((int)EffectiveGear(colony), (int)CaravanGear.IronCart));

    /// <summary>La vitesse d'une caravane en route, d'après l'équipement figé à son départ.</summary>
    internal static float SpeedOf(Caravan trip) => GearSpeedFactor(trip.Gear, trip.DraftCount > 0 ? trip.DraftSpecies : null);

    /// <summary>Le grain que boivent les bêtes de trait pendant le voyage, ajouté aux provisions.</summary>
    internal static int DraftFeed(double tripDays) => (int)Math.Ceiling(tripDays) * DraftTeam;

    // ---------- Monter en gamme ----------

    /// <summary>Heures de gain moyen des trois derniers voyages, au-dessus duquel la colonie songe à l'équipement suivant, et part des voyages bridés par la capacité.</summary>
    internal static (double MeanGainHours, double LimitedShare) UpgradeThreshold(CaravanGear next) => next switch
    {
        CaravanGear.WoodCart => (30, 0.5),
        CaravanGear.IronCart => (60, 0.5),
        _ => (100, 0.6),
    };

    /// <summary>
    /// Chaque matin, au village principal : si la survie est assurée, que les derniers voyages ont rapporté gros et se sont heurtés à la capacité, et que le stock couvre le
    /// coût sans entamer les réserves de survie, la colonie équipe ses caravanes du niveau suivant.
    /// <list type="bullet">
    /// <item>Charrette de bois : 30 bois, un habitant d'un niveau de construction d'au moins 5.</item>
    /// <item>Charrette de fer : 10 fer et 2 outils.</item>
    /// <item>Bêtes de trait : deux bœufs ou deux chevaux apprivoisés (ils ne sont pas consommés : ils partent avec la caravane et reviennent).</item>
    /// </list>
    /// </summary>
    public static bool ConsiderGearUpgrade(WorldState world, Colony colony)
    {
        if (colony.CaravanGear == CaravanGear.Draft || colony.Sensors is not { SurvivalAssured: true } sensors || sensors.FoodDays < SettlementRules.ComfortFoodDays)
            return false;
        var recent = colony.Trades.Where(t => t.WeSent && t.GainHours is not null).TakeLast(3).ToList();
        if (recent.Count < 3)
            return false;
        CaravanGear next = colony.CaravanGear + 1;
        (double gain, double limited) = UpgradeThreshold(next);
        if (recent.Average(t => t.GainHours!.Value) < gain || recent.Count(t => t.CapacityLimited) < limited * recent.Count)
            return false;

        Stockpile stock = colony.PrimarySettlement.Stock;
        string what;
        switch (next)
        {
            case CaravanGear.WoodCart:
                if (stock.Available(ResourceType.Wood) < 30 + (int)ColonyBrain.HeatingTarget(colony, world.Clock.Season)
                    || !colony.PresentMembers.Any(m => m.Skills.Level(SkillType.Construction) >= 5f)
                    || !stock.TryTake(ResourceType.Wood, 30))
                    return false;
                what = "une charrette de bois";
                break;
            case CaravanGear.IronCart:
                if (stock.Available(ResourceType.Iron) < 10 || stock.Available(ResourceType.Tools) < 2)
                    return false;
                stock.TryTake(ResourceType.Iron, 10);
                stock.TryTake(ResourceType.Tools, 2);
                what = "une charrette renforcée de fer";
                break;
            default:
                if (DraftAvailable(colony) is not { } draft)
                    return false;
                what = $"des {GoodName(draft, 2)} de trait";
                break;
        }
        colony.CaravanGear = next;
        ColonyBrain.Say(colony, world.Clock, $"La colonie équipe ses caravanes de {what} : elles porteront davantage, et iront plus vite.");
        return true;
    }

    // ---------- Les dangers de la route ----------

    /// <summary>Risque d'interception par jour de marche sur l'arête (a, b) : 0,4 % de base.</summary>
    public const double BaseRisk = 0.004;

    /// <summary>La densité de prédateurs d'une case du monde, rapportée à ce que son biome peut en porter (0,7 si la région n'est pas encore connue).</summary>
    internal static double PredatorDensity(WorldState world, int tile)
    {
        if (!world.Regions.TryGetValue(tile, out RegionState? region))
            return 0.7;
        double capacity = Nature.WildSpeciesInfo.All.Where(Nature.WildSpeciesInfo.IsPredator).Sum(s => Nature.WildSpeciesInfo.BaseCap(region.Map.Biome, s));
        return capacity <= 0 ? 0 : Math.Min(1.5, region.Wildlife.Predators / capacity);
    }

    /// <summary>
    /// Le risque par jour de marche d'une arête : de base × l'éloignement des colonies (un trajet à moins de deux cases d'un établissement est bien plus sûr)
    /// × la menace des prédateurs × l'état de la route (chaque niveau d'aménagement enlève un quart du danger).
    /// </summary>
    public static double RouteRisk(WorldState world, int a, int b)
    {
        bool nearColony = world.Settlements.Any(s => s.Status != SettlementStatus.Closed
            && (world.WorldMap.Grid.Distance(s.RegionTileIndex, a) <= 2 || world.WorldMap.Grid.Distance(s.RegionTileIndex, b) <= 2));
        double wild = 1 - (nearColony ? 0.7 : 0);
        double predators = PredatorDensity(world, b);
        double road = 1 - 0.25 * world.WorldMap.Roads.LevelOf(a, b);
        return BaseRisk * wild * (0.5 + predators) * road;
    }

    /// <summary>
    /// La caravane vient de franchir l'arête (a, b) : une interception, rare, lui prend 10 à 40 % de ses marchandises (jamais ses pièces), blesse un porteur sur
    /// deux et peut coûter une bête de trait. Les biens et les pièces perdus sont comptés, jamais remis au stock ; la caravane continue avec ce qui lui reste.
    /// </summary>
    internal static bool CheckInterception(WorldState world, Caravan trip, int a, int b, double edgeCost)
    {
        if (trip.Purpose != TerritorialPurpose.Commerce || trip.Aborted)
            return false;
        double days = edgeCost / (WorldMap.CaravanTilesPerDay * SpeedOf(trip));
        if (world.Nature.NextDouble() >= RouteRisk(world, a, b) * days)
            return false;

        double share = 0.1 + 0.3 * world.Nature.NextDouble();
        int lostUnits = 0;
        foreach ((ResourceType good, int amount) in trip.Inventory.Amounts.Where(p => p.Key != ResourceType.Coins && p.Value > 0).OrderBy(p => p.Key).ToList())
        {
            int lost = Math.Min(amount, (int)Math.Ceiling(amount * share));
            if (lost > 0 && trip.Inventory.TryTake(good, lost, ResourceFlow.Loss))
                lostUnits += lost;
        }
        trip.Interceptions++;

        for (int i = 0; i < trip.Traders.Count; i += 2)
            Health.Injure(world, trip.Traders[i], "blessé par ceux qui ont attaqué la caravane", 0.01f);
        string lostBeast = "";
        if (trip.DraftCount > 0 && world.Nature.NextDouble() < 0.2)
        {
            trip.DraftCount--;
            lostBeast = " Une bête de trait n'est pas revenue.";
        }
        bool woods = world.WorldMap.Grid[b].Biome is World.Biome.TemperateForest or World.Biome.BorealForest or World.Biome.TropicalForest or World.Biome.Swamp;
        ColonyBrain.Say(trip.From, world.Clock, (woods || PredatorDensity(world, b) >= 0.5 ? "Des loups ont attaqué la caravane" : "Des brigands ont attaqué la caravane")
            + $" de {trip.From.Name} sur la route de {trip.To.Name} : une partie de la cargaison est perdue ({lostUnits} unités),la caravane poursuit son chemin.{lostBeast}");
        return true;
    }
}
