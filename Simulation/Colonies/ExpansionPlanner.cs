using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

public enum ExpansionKind { Camp = 0, Local = 1, Import = 2 }

/// <summary>Une manière d'obtenir un matériau : coût complet par unité (en heures de travail), unités qu'on peut en tirer sur l'horizon.</summary>
public sealed record ExpansionOption(ExpansionKind Kind, double UnitCostHours, int Units, int SiteId, int Region);

/// <summary>
/// Les décisions d'expansion d'une colonie : un camp, l'exploitation locale ou l'importation, comparés sur leur coût complet (installation, transport,
/// entretien, durée du gisement), la relocalisation de familles vers un camp viable, et le stade atteint par un établissement.
/// Les valeurs sont des réglages initiaux d'équilibrage ; elles ne sont pas validées par une campagne.
/// </summary>
public static class ExpansionPlanner
{
    /// <summary>Jours sur lesquels on amortit une installation et on estime la demande.</summary>
    public const int HorizonDays = 40;

    /// <summary>Un camp doit coûter au plus cette part de la meilleure autre solution (marge contre l'incertitude des estimations).</summary>
    public const double CampAdvantage = 0.8;

    /// <summary>Taille d'une équipe de fondation : le réglage du monde (<see cref="TerritorialRules.FoundersPerCamp"/>) vaut pour les décisions, ceci pour le coût d'un camp.</summary>
    public const int CampPeople = 4;

    /// <summary>Habitants dont le départ ne vide pas le village principal quand il relocalise une famille.</summary>
    public const int MinSourceForRelocation = 24;

    private const double WorkHoursPerDay = Trade.WorkHoursPerDay;

    /// <summary>Unités par jour que la colonie voudrait pour ce matériau (0 : aucune demande).</summary>
    public static double DailyDemand(Colony owner, ResourceType material)
    {
        int missing = material == ResourceType.IronOre ? ToolChain.Demand(owner).OreMissing
            : Math.Max(0, ExtendedIndustry.Target(owner, material) + owner.ExportInterest.GetValueOrDefault(material) - owner.Stock.Get(material));
        return missing <= 0 ? 0 : Math.Max(1.0, missing / 10.0);
    }

    private static double RouteDays(WorldState world, Colony owner, int from, int to) =>
        world.WorldMap.TravelRoute(from, to, Trade.HostileRegions(world, owner)) is { } route ? route.Cost / WorldMap.CaravanTilesPerDay : double.PositiveInfinity;

    /// <summary>Heures de travail d'un aller-retour de ravitaillement à deux, avec la charge utile qu'il ramène (en unités du matériau).</summary>
    private static (double TripHours, double UnitsPerTrip) HaulTrip(double routeDays, ResourceType material) =>
        (2 * (2 * routeDays + 0.5) * WorkHoursPerDay, Math.Max(1, 2 * 40 / ResourceCatalog.Weight(material)));

    /// <summary>L'installation d'un camp : le trajet des fondateurs et la dotation prélevée sur les stocks existants (jamais gratuite).</summary>
    public static double InstallationHours(Colony owner, double routeDays) =>
        CampPeople * routeDays * WorkHoursPerDay
        + 24 * Economy.Cost(owner, ResourceType.Wood) + 24 * Economy.Cost(owner, ResourceType.Grain) + 2 * Economy.Cost(owner, ResourceType.Tools);

    /// <summary>Le coût complet d'un camp au gisement connu : installation amortie, extraction, entretien des fondateurs et transport du produit.</summary>
    public static ExpansionOption? CampOption(WorldState world, Colony owner, Settlement source, DepositKnowledge site, double dailyDemand)
    {
        double days = RouteDays(world, owner, source.RegionTileIndex, site.Region);
        if (double.IsInfinity(days) || site.State is DepositObservation.Depleted or DepositObservation.Hint) return null;
        Deposit? truth = world.Regions.TryGetValue(site.Region, out RegionState? region) ? region.Deposits.FirstOrDefault(d => d.Id == site.SiteId) : null;
        int dailyLimit = truth?.DailyLimit ?? 8; // la limite quotidienne d'un gîte est connue de ceux qui le travaillent ; à défaut une valeur ordinaire
        double unitHours = Economy.Cost(owner, site.Material) * (truth?.Difficulty ?? 1f);
        double crewPerDay = CampPeople * WorkHoursPerDay / Math.Max(0.5, unitHours);
        double perDay = Math.Min(dailyLimit, crewPerDay);
        // Les réserves sont comptées avec prudence : le bas de la fourchette connue (un flux renouvelable n'a pas de fin).
        double reserve = site.EstimateMax == 0 ? double.PositiveInfinity : site.EstimateMin;
        double producibleDays = Math.Min(HorizonDays, reserve / Math.Max(0.1, perDay));
        int units = (int)Math.Min(dailyDemand * HorizonDays, perDay * producibleDays);
        if (units < 10 || producibleDays < 10) return null;
        double foodPerDay = CampPeople * Trade.TravelerNutritionPerDay / Stockpile.GrainMealValue * Economy.Cost(owner, ResourceType.Grain);
        (double tripHours, double perTrip) = HaulTrip(days, site.Material);
        double total = InstallationHours(owner, days) + units * unitHours + foodPerDay * (units / perDay) + units / perTrip * tripHours;
        return new ExpansionOption(ExpansionKind.Camp, total / units, units, site.SiteId, site.Region);
    }

    /// <summary>L'exploitation locale : un gîte connu de la région du village, avec l'installation d'une mine si ce matériau l'exige.</summary>
    public static ExpansionOption? LocalOption(WorldState world, Colony owner, Settlement source, ResourceType material, double dailyDemand)
    {
        DepositKnowledge? known = owner.DepositReports.Where(k => k.Region == source.RegionTileIndex && k.Material == material && k.State is DepositObservation.Surveyed or DepositObservation.Working)
            .OrderByDescending(k => k.EstimateMin).FirstOrDefault();
        if (known is null) return null;
        Deposit? truth = world.Regions.TryGetValue(known.Region, out RegionState? region) ? region.Deposits.FirstOrDefault(d => d.Id == known.SiteId) : null;
        double unitHours = Economy.Cost(owner, material) * (truth?.Difficulty ?? 1f);
        double reserve = known.EstimateMax == 0 ? double.PositiveInfinity : known.EstimateMin;
        int units = (int)Math.Min(dailyDemand * HorizonDays, Math.Min(reserve, (truth?.DailyLimit ?? 8) * (double)HorizonDays));
        if (units < 1) return null;
        double mine = ExtendedIndustry.NeedsMine(material) && !Civic.Has(owner, BuildingType.MineDepot)
            ? (new Building(BuildingType.MineDepot, 0, 0).WoodRequired * Economy.Cost(owner, ResourceType.Wood) + new Building(BuildingType.MineDepot, 0, 0).StoneRequired * Economy.Cost(owner, ResourceType.Stone)) : 0;
        return new ExpansionOption(ExpansionKind.Local, unitHours + mine / units, units, known.SiteId, known.Region);
    }

    /// <summary>L'importation : le meilleur prix connu et récent d'un fournisseur, plus le transport ; inconnue tant qu'aucune offre n'a été vue.</summary>
    public static ExpansionOption? ImportOption(WorldState world, Colony owner, Settlement source, ResourceType material, double dailyDemand)
    {
        ExpansionOption? best = null;
        foreach (SupplierMemory memory in Trade.Suppliers(world, owner))
        {
            if (memory.AgeDays(world.Clock.Ticks) > Trade.OfferLifetimeDays || memory.Offers.FirstOrDefault(o => o.Good == material && o.Available > 0) is not { } offer) continue;
            double days = RouteDays(world, owner, source.RegionTileIndex, world.WorldMap.TileOf(memory.Supplier));
            if (double.IsInfinity(days)) continue;
            (double tripHours, double perTrip) = HaulTrip(days, material);
            double unit = offer.SellPrice + tripHours / perTrip;
            int units = (int)Math.Min(dailyDemand * HorizonDays, offer.Available * (double)Math.Max(1, HorizonDays / 10));
            if (units > 0 && (best is null || unit < best.UnitCostHours)) best = new ExpansionOption(ExpansionKind.Import, unit, units, 0, world.WorldMap.TileOf(memory.Supplier));
        }
        return best;
    }

    /// <summary>
    /// Le meilleur camp à ouvrir pour ce matériau : celui dont le coût complet par unité est nettement inférieur à l'exploitation locale (pour ce qu'elle couvre)
    /// et à l'importation. Renvoie le gîte choisi, ou null si les autres solutions suffisent.
    /// </summary>
    public static DepositKnowledge? BestCampSite(WorldState world, Colony owner, Settlement source)
    {
        DepositKnowledge? choice = null;
        double bestMargin = 0;
        // L'argile, présente presque partout, ne justifie pas un camp : seuls les minerais et le charbon, concentrés dans quelques régions.
        foreach (ResourceType material in new[] { ResourceType.IronOre, ResourceType.MineralCoal, ResourceType.CopperOre, ResourceType.GoldOre })
        {
            double demand = DailyDemand(owner, material);
            if (demand <= 0) continue;
            ExpansionOption? local = LocalOption(world, owner, source, material, demand), import = ImportOption(world, owner, source, material, demand);
            // La meilleure autre solution : l'exploitation locale pour ce qu'elle couvre ; l'importation sinon.
            double alternative = Math.Min(local?.UnitCostHours ?? double.PositiveInfinity, import?.UnitCostHours ?? double.PositiveInfinity);
            bool localCovers = local is not null && local.Units >= demand * HorizonDays * 0.8;
            if (localCovers && local!.UnitCostHours <= alternative) continue;
            foreach (DepositKnowledge site in owner.DepositReports.Where(k => k.Material == material && k.Region != source.RegionTileIndex && k.State == DepositObservation.Surveyed)
                         .Where(k => !world.Settlements.Any(s => s.RegionTileIndex == k.Region && s.Status != SettlementStatus.Closed)).OrderBy(k => k.SiteId))
            {
                if (CampOption(world, owner, source, site, demand) is not { } camp) continue;
                // Sans autre solution connue, un camp se compare à ce que vaut le matériau pour la colonie qui ne peut pas l'obtenir (sa valeur d'usage, triplée : sans lui, ses outils ne se forgent pas).
                double reference = double.IsInfinity(alternative) ? Economy.Value(owner, material) * 3 : alternative;
                double margin = reference * CampAdvantage - camp.UnitCostHours;
                if (margin > bestMargin) { bestMargin = margin; choice = site; }
            }
        }
        return choice;
    }

    /// <summary>L'ordre des stades (camp, hameau, village) : on ne rétrograde jamais.</summary>
    public static int Rank(SettlementKind kind) => kind switch { SettlementKind.Village => 2, SettlementKind.Hamlet => 1, _ => 0 };

    /// <summary>Un gîte exploité dont la fin approche est signalé une seule fois : la colonie peut alors prospecter, importer ou déménager (voir <see cref="Prospection.WantedMaterials"/>).</summary>
    internal static void WatchExhaustion(WorldState world, Settlement settlement)
    {
        Colony owner = settlement.Owner;
        foreach (DepositKnowledge known in owner.DepositReports.Where(k => k.Region == settlement.RegionTileIndex && k.State == DepositObservation.Working
                     && k.EstimateMax > 0 && !k.Alerted && k.EstimateMax < 6 * 8))
        {
            known.Alerted = true;
            ColonyBrain.Say(owner, world.Clock, $"Le gisement de {ResourceCatalog.Name(known.Material)} de {settlement.Name} touche à sa fin : il en reste au plus {known.EstimateMax} unités.");
        }
    }

    /// <summary>Le stade qu'un établissement mérite d'après sa population, ses logements, ses services et ses activités .</summary>
    public static SettlementKind StageOf(Settlement place)
    {
        var people = place.Population.ToList();
        if (people.Count < 12 || people.Any(c => c.Home is null) || place.Stock.AvailableNutrition < people.Count * (decimal)Trade.TravelerNutritionPerDay * 3)
            return SettlementKind.Camp;
        int services = new[] { BuildingType.Well, BuildingType.Storehouse, BuildingType.Infirmary, BuildingType.Tavern, BuildingType.School, BuildingType.Market, BuildingType.Pen }
            .Count(type => place.Buildings.Any(b => b.Type == type && b.IsComplete));
        if (services < 1) return SettlementKind.Camp;
        int sectors = people.Where(c => c.Stage == LifeStage.Adult).Select(c => c.Sector).Where(s => s != WorkSector.Free).Distinct().Count();
        bool families = people.Count(c => c.Partner is not null) >= 4 || people.Any(c => c.Stage == LifeStage.Child);
        return people.Count >= 24 && services >= 2 && sectors >= 3 && families ? SettlementKind.Village : SettlementKind.Hamlet;
    }

    /// <summary>Une famille entière (un couple et ses enfants présents) que le village peut céder sans se vider, ou null.</summary>
    internal static List<Colonist>? PickFamily(Settlement source, int maxSize)
    {
        foreach (Colonist parent in source.Population.Where(c => c.Stage == LifeStage.Adult && c.Partner is not null && c.Id < c.Partner.Id).OrderBy(c => c.Id))
        {
            Colonist partner = parent.Partner!;
            var family = new List<Colonist> { parent, partner };
            family.AddRange(source.Population.Where(c => c.Stage != LifeStage.Adult && (c.Mother == parent || c.Father == parent || c.Mother == partner || c.Father == partner)));
            if (family.Count > maxSize || family.Any(c => !source.Population.Contains(c) || c.Ailment != Ailment.None || c.PregnancyFather is not null || c.PregnantUntilTicks is not null)) continue;
            // Des enfants d'un autre couple (veuvage, remariage) restent : on ne sépare pas une fratrie.
            if (family.Where(c => c.Stage != LifeStage.Adult).Any(c => (c.Mother is not null && !family.Contains(c.Mother) && source.Population.Contains(c.Mother))
                || (c.Father is not null && !family.Contains(c.Father) && source.Population.Contains(c.Father)))) continue;
            if (source.Population.Count(c => c.Sector is WorkSector.Food or WorkSector.Farm && !family.Contains(c)) < 2) continue;
            return family;
        }
        return null;
    }

    /// <summary>Un camp viable et bien tenu mais sans famille : le village principal y envoie une famille entière, avec son trajet, ses provisions et un lit à l'arrivée.</summary>
    internal static bool TryRelocateFamily(WorldState world, Colony owner, Settlement source, int minSource = MinSourceForRelocation)
    {
        if (source.Population.Count < minSource || source.Population.Any(c => c.Home is null)) return false;
        foreach (Settlement camp in owner.Settlements.Where(s => s != source && s.Status == SettlementStatus.Active && s.Kind != SettlementKind.Village).OrderBy(s => s.Id))
        {
            if (camp.Population.Count < CampPeople || camp.UnproductiveDays > 0) continue;
            int beds = camp.Buildings.Count(b => b.IsHut && b.IsComplete) * Building.HutCapacity - camp.Population.Count;
            if (beds < 3 || camp.Stock.AvailableNutrition < camp.Population.Count * (decimal)Trade.TravelerNutritionPerDay * 5) continue;
            List<Colonist>? family = PickFamily(source, Math.Min(5, beds));
            if (family is null) continue;
            if (TerritorialTravel.Depart(world, source, camp.RegionTileIndex, TerritorialPurpose.Relocation, new Dictionary<ResourceType, int>(),
                    family.Count, camp, family) is not null) return true;
        }
        return false;
    }
}
