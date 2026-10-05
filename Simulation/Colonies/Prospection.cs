using GodColony.Simulation.Map;
using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// La connaissance des gisements se gagne par étapes. Une simple visite révèle les affleurements (des indices, très incertains) ; un sondage
/// fait par des habitants — d'autant plus profond que leur compétence Mining et leur temps sur place sont grands — confirme les gîtes jusqu'à
/// une certaine profondeur et resserre l'estimation ; l'exploitation affine encore. La colonie ne lit jamais la réserve cachée : elle garde
/// seulement une fourchette dont la largeur dépend de sa confiance.
/// </summary>
public static class Prospection
{
    /// <summary>Jours que des prospecteurs travaillent sur place avant de rentrer.</summary>
    public const int StayDays = 3;

    /// <summary>Profondeur la plus grande qu'un sondage atteint : les pierres précieuses et l'or ne se trouvent pas à l'affleurement.</summary>
    public const int MaxReach = 3;

    private static readonly float[] ReachThresholds = [4f, 12f, 30f];

    /// <summary>Travail de sondage d'un groupe sur un nombre de jours : chacun compte pour 1 plus un dixième de sa compétence Mining.</summary>
    public static float WorkOf(IEnumerable<Colonist> people, double days) =>
        (float)(people.Sum(p => 1f + p.Skills.Level(SkillType.Mining) / 10f) * days);

    /// <summary>Profondeur sondée par un travail cumulé (0 : rien de plus que les affleurements).</summary>
    public static int ReachOf(float work)
    {
        int reach = 0;
        while (reach < ReachThresholds.Length && work >= ReachThresholds[reach]) reach++;
        return reach;
    }

    /// <summary>
    /// Ce qu'un sondage de cette profondeur apprend d'un gîte, ou null s'il reste caché. Un gîte à l'affleurement qu'on n'a fait que voir reste un indice.
    /// La fourchette contient toujours la réserve restante, mais s'élargit quand la confiance est faible.
    /// </summary>
    internal static DepositKnowledge? Report(Deposit site, int reach, long ticks, string source)
    {
        if (site.Depth > Math.Max(0, reach)) return null;
        bool hint = reach <= 0;
        bool depleted = site.Mode == DepositMode.Finite && site.RemainingReserve == 0;
        float confidence = hint ? 0.2f : Math.Min(0.9f, 0.4f + 0.2f * (reach - site.Depth));
        float spread = 1f - confidence;
        bool flow = site.Mode == DepositMode.Permanent;
        return new DepositKnowledge
        {
            SiteId = site.Id, Region = site.Region, Material = site.Material,
            State = depleted ? DepositObservation.Depleted : hint ? DepositObservation.Hint : DepositObservation.Surveyed,
            Confidence = confidence, SurveyedDepth = reach,
            EstimateMin = flow || depleted ? 0 : (int)(site.RemainingReserve * (1 - 0.8f * spread)),
            EstimateMax = flow || depleted ? 0 : (int)Math.Ceiling(site.RemainingReserve * (1 + 1.2f * spread)),
            ObservedTicks = ticks, Source = source,
        };
    }

    /// <summary>Le renseignement s'ajoute ou remplace un renseignement moins sûr ; un gîte déjà exploité ou épuisé garde son état.</summary>
    internal static bool Learn(Colony colony, DepositKnowledge report)
    {
        DepositKnowledge? known = colony.DepositReports.FirstOrDefault(k => k.SiteId == report.SiteId);
        if (known is null) { colony.DepositReports.Add(report); return true; }
        if (known.State == DepositObservation.Working || known.State == DepositObservation.Depleted && report.State != DepositObservation.Depleted
            || report.Confidence < known.Confidence) return false;
        bool upgraded = known.State == DepositObservation.Hint && report.State != DepositObservation.Hint;
        known.State = report.State; known.EstimateMin = report.EstimateMin; known.EstimateMax = report.EstimateMax;
        known.Confidence = report.Confidence; known.SurveyedDepth = report.SurveyedDepth;
        known.ObservedTicks = report.ObservedTicks; known.Source = report.Source;
        return upgraded;
    }

    /// <summary>Un sondage de cette profondeur dans une région : les gîtes qu'il révèle sont inscrits aux connaissances de la colonie.</summary>
    internal static int Survey(WorldState world, Colony colony, RegionState region, int reach, string source)
    {
        if (!colony.VisitedRegions.Contains(region.TileIndex)) colony.VisitedRegions.Add(region.TileIndex);
        colony.RegionReach[region.TileIndex] = Math.Max(colony.RegionReach.GetValueOrDefault(region.TileIndex, -1), reach);
        int news = 0;
        foreach (Deposit site in region.Deposits)
            if (Report(site, reach, world.Clock.Ticks, source) is { } report && Learn(colony, report) && report.State != DepositObservation.Hint)
                news++;
        return news;
    }

    /// <summary>
    /// Chaque jour, les habitants d'un établissement explorent les environs : leur travail de sondage s'accumule lentement (les mineurs comptent le plus)
    /// et révèle peu à peu les gîtes de leur propre région, jusqu'à la profondeur que ce travail permet.
    /// </summary>
    internal static void LocalDaily(WorldState world, Settlement settlement)
    {
        Colony owner = settlement.Owner;
        var adults = settlement.Population.Where(c => c.Stage == LifeStage.Adult).ToList();
        if (adults.Count == 0 || settlement.RegionTileIndex < 0) return;
        float miners = WorkOf(adults.Where(c => c.Sector == WorkSector.Stone), 1);
        settlement.SurveyWork += 0.15f * miners + 0.02f * adults.Count;
        int reach = Math.Min(MaxReach, ReachOf(settlement.SurveyWork));
        RegionState region = world.VisitRegion(settlement.RegionTileIndex);
        if (reach <= owner.RegionReach.GetValueOrDefault(settlement.RegionTileIndex, -1)) return;
        int found = Survey(world, owner, region, reach, "Sondage des habitants");
        if (found > 0)
            ColonyBrain.Say(owner, world.Clock, $"Nos habitants ont sondé les environs : {found} gisement{(found > 1 ? "s sont reconnus" : " est reconnu")} près de {settlement.Name}.");
    }

    /// <summary>Le sondage d'une expédition : travail réel sur place (durée et compétences), appliqué à l'arrivée au retour.</summary>
    internal static void Report(WorldState world, Caravan trip)
    {
        RegionState region = world.VisitRegion(trip.TargetRegion);
        float days = (float)trip.WorkedTicks / TimeConstants.TicksPerDay;
        int reach = Math.Min(MaxReach, ReachOf(WorkOf(trip.Traders, days)));
        trip.SurveyReports = region.Deposits.Select(site => Report(site, reach, world.Clock.Ticks, "Prospection sur place"))
            .OfType<DepositKnowledge>().ToList();
        trip.SurveyReach = reach;
        foreach (Colonist person in trip.Traders) person.Skills.Practice(SkillType.Mining, days * 120f);
    }

    /// <summary>Les matériaux dont la colonie manque, ou dont les gîtes connus sont presque épuisés : ce que des prospecteurs iraient chercher.</summary>
    internal static HashSet<ResourceType> WantedMaterials(Colony owner)
    {
        var wanted = new HashSet<ResourceType>();
        foreach (ResourceType material in new[] { ResourceType.IronOre, ResourceType.MineralCoal, ResourceType.CopperOre, ResourceType.GoldOre,
                     ResourceType.Ruby, ResourceType.Sapphire, ResourceType.Emerald, ResourceType.Diamond, ResourceType.Clay })
        {
            int demand = material == ResourceType.IronOre ? ToolChain.Demand(owner).OreMissing
                : Math.Max(0, ExtendedIndustry.Target(owner, material) + owner.ExportInterest.GetValueOrDefault(material) - owner.Stock.Get(material));
            if (demand <= 0) continue;
            int remaining = owner.DepositReports.Where(k => k.Material == material && k.State != DepositObservation.Depleted)
                .Sum(k => k.EstimateMax == 0 && k.State != DepositObservation.Depleted ? 10 * demand : k.EstimateMax);
            if (remaining < 4 * demand) wanted.Add(material);
        }
        return wanted;
    }

    /// <summary>
    /// La région voisine qui vaut une expédition, ou −1. L'intérêt croît avec le relief (les grands gisements sont en montagne), avec le nombre de matériaux
    /// recherchés, décroît avec la distance et avec ce qui est déjà sondé en profondeur. Une région déjà sondée au maximum n'a plus rien à offrir.
    /// </summary>
    internal static int Choose(WorldState world, Colony owner, Settlement source)
    {
        // Un petit village ou un village qui n'est pas confortablement nourri garde tous ses bras : les expéditions durent plusieurs jours.
        if (source.Population.Count < world.Territory.MinProspectingPopulation || owner.Sensors is not { SurvivalAssured: true } sensors || sensors.FoodDays < SettlementRules.ComfortFoodDays) return -1;
        HashSet<ResourceType> wanted = WantedMaterials(owner);
        bool exploring = owner.VisitedRegions.Count < 3;
        if (wanted.Count == 0 && !exploring) return -1;
        HashSet<int> hostile = Trade.HostileRegions(world, owner);
        double best = 0.3;
        int choice = -1;
        foreach (int tile in owner.VisitedRegions.SelectMany(world.WorldMap.Grid.Neighbors).Concat(owner.VisitedRegions).Distinct().OrderBy(t => t))
        {
            WorldTile info = world.WorldMap.Grid[tile];
            if (tile == source.RegionTileIndex || !info.Habitable
                || world.Regions.TryGetValue(tile, out RegionState? known) && known.OwnerColonyId is int claimed && claimed != owner.Id) continue;
            bool visited = owner.VisitedRegions.Contains(tile);
            int reach = owner.RegionReach.GetValueOrDefault(tile, -1);
            if (visited && (reach >= MaxReach || wanted.Count == 0 || world.Settlements.Any(s => s.RegionTileIndex == tile && s.Status != SettlementStatus.Closed))
                || !visited && owner.VisitedRegions.Count >= world.Territory.MaxRecognizedRegions) continue;
            var route = world.WorldMap.TravelRoute(source.RegionTileIndex, tile, hostile);
            if (route is null) continue;
            double relief = info.Relief == Relief.Mountains ? 3 : info.Relief == Relief.Hills ? 1.5 : 0.5;
            double score = relief * (visited ? 0.4 * (MaxReach - reach) / MaxReach : 1) * (1 + 0.5 * wanted.Count) / (1 + route.Cost / 4);
            if (score > best) { best = score; choice = tile; }
        }
        return choice;
    }
}
