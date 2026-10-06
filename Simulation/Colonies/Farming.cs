using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'agriculture : on défriche des champs près du camp, on sème au printemps, les céréales poussent
/// pendant 8,75 jours en climat doux, puis on moissonne. Ce qui n'est pas rentré avant l'hiver gèle sur pied.
/// </summary>
public static class Farming
{
    /// <summary>Facteur commun à la durée de pousse et au rendement : une attente accrue de 25 % donne une récolte accrue de 25 %.</summary>
    public const float CropCycleFactor = 1.25f;

    /// <summary>Adaptation minimale d'une parcelle pour qu'on y plante de la vigne (voir <see cref="VineSuitability"/>).</summary>
    public const float MinVineSuitability = 0.45f;

    /// <summary>Rendement de référence d'une parcelle mûre, avant le facteur de durée du cycle.</summary>
    public const int PlotYield = 4;

    /// <summary>Bonus de référence pour une berge fertile (à moins de deux cases de l'eau), avant le facteur de durée du cycle.</summary>
    public const int BankBonus = 1;

    /// <summary>Ce que rapporte la parcelle de cette case : plus sur les berges.</summary>
    public static int YieldAt(LocalMap map, int x, int y) => YieldAt(map, x, y, 0);

    /// <summary>Ce que rapporte la parcelle à cette colonie, assolement compris, avec le même facteur que la pousse.</summary>
    public static int YieldAt(Colony colony, LocalMap map, int x, int y) =>
        YieldAt(map, x, y, Knowledge.Has(colony, Discovery.CropRotation) ? Knowledge.CropRotationBonus : 0);

    private static int YieldAt(LocalMap map, int x, int y, int cropRotationBonus)
    {
        int reference = (int)MathF.Round(PlotYield * map.SoilRichness)
            + (map.IsFertileBank(x, y) ? BankBonus : 0)
            + (map.IsIrrigated(x, y) ? IrrigationBonus : 0) + cropRotationBonus;
        // Tous les bonus suivent le cycle ; un seul arrondi final conserve les récoltes en céréales entières.
        return (int)MathF.Round(reference * CropCycleFactor);
    }

    /// <summary>Bonus de référence pour une parcelle irriguée, avant le facteur de durée du cycle.</summary>
    public const int IrrigationBonus = 2;

    /// <summary>Jours de pousse entre le semis et la maturité (la pousse s'arrête en hiver).</summary>
    public const float GrowthDays = 7f * CropCycleFactor;

    public const float SowSeconds = 1.5f;
    public const float HarvestSeconds = 2.5f;

    /// <summary>Parcelles à cultiver par colon, pour couvrir toute son année si la nourriture sauvage coûte très cher.</summary>
    private const float FullCoveragePlotsPerColonist = 8f;
    private const float DefaultCoverage = 0.6f;
    /// <summary>Coût (heures par unité) de la nourriture sauvage à partir duquel on couvre toute l'année par les champs.</summary>
    private const float WildFoodCostForFullCoverage = 4f;

    private const int MinDistanceFromFire = 4;
    private const int MaxDistanceFromFire = 16;
    private const float TreePenalty = 1.5f;
    private const float BushPenalty = 2.5f;

    /// <summary>Une berge fertile compte comme du défrichage en moins : on préfère cultiver près de l'eau.</summary>
    private const float BankPreference = 0.35f;

    /// <summary>
    /// L'adaptation d'une parcelle à la vigne, de 0 (impossible) à 1 (idéale) : elle craint le grand froid, la sécheresse non irriguée et l'humidité excessive.
    /// Elle ne change aucune règle du blé : c'est un filtre de plantation et un facteur de récolte propres à la vigne.
    /// </summary>
    public static float VineSuitability(Map.LocalMap map, int x, int y)
    {
        float warmth = 1f - Math.Clamp((Climate.ColdSeverity(map.Biome) - 0.15f) / 0.5f, 0f, 1f);
        float dryness = Math.Clamp((World.BiomeInfo.Of(map.Biome).DryShare - 0.2f) / 0.5f, 0f, 1f);
        float drought = 1f - dryness * (map.IsIrrigated(x, y) ? 0.25f : 1f);
        float damp = map.Biome is World.Biome.TropicalForest or World.Biome.Swamp ? 0.5f : 1f;
        return Math.Clamp(warmth * drought * damp, 0f, 1f);
    }

    /// <summary>Habitants présents au-delà desquels les nouveaux champs passent à 6 × 6, puis à 8 × 8 : un campement défriche petit, un village développé voit grand.</summary>
    private const int MediumFieldPopulation = 16;
    private const int LargeFieldPopulation = 32;

    /// <summary>
    /// Le côté des nouveaux champs de la colonie : il croît avec sa population (les bras disponibles pour semer et moissonner), les champs déjà ouverts gardent le leur.
    /// </summary>
    public static int FieldSizeFor(Colony colony)
    {
        int people = colony.PresentMembers.Count;
        return people > LargeFieldPopulation ? Field.MaxSize : people > MediumFieldPopulation ? 6 : Field.BaseSize;
    }

    /// <summary>Le nombre de parcelles cultivables de la colonie, tous champs confondus.</summary>
    public static int PlotCount(Colony colony) => colony.Fields.Sum(f => f.Plots.Count);

    public static IEnumerable<FieldPlot> Plots(Colony colony) => colony.Fields.SelectMany(f => f.Plots);

    public static FieldPlot? PlotAt(Colony colony, int x, int y) =>
        colony.Fields.FirstOrDefault(f => f.Contains(x, y))?.Plots.First(p => p.X == x && p.Y == y);

    public static bool IsSowingSeason(Season season) => season == Season.Printemps;

    /// <summary>Combien de parcelles la colonie veut cultiver : de plus en plus si la nourriture sauvage lui coûte cher.</summary>
    public static int TargetPlots(Colony colony)
    {
        float coverage = colony.Labor.HoursPerUnit(ResourceType.Food) is { } wildCost
            ? Math.Clamp((float)wildCost / WildFoodCostForFullCoverage, 0.4f, 1f)
            : DefaultCoverage;
        return (int)MathF.Ceiling(colony.PresentMembers.Count * FullCoveragePlotsPerColonist * coverage);
    }

    /// <summary>
    /// Place libre pour un champ (4 × 4 à 8 × 8, voir <see cref="FieldSizeFor"/>) : plat, de bonne terre (la fertilité des berges et l'irrigation comptent), le moins boisé possible, à portée d'un dépôt (le camp
    /// ou un entrepôt achevé) : un aller-retour parcelle → dépôt de 4 secondes visé, 6 au plus. La recherche va de groupe de champs en groupe de champs. Sonde pure.
    /// </summary>
    public static (int X, int Y)? FindFieldSite(LocalMap map, Colony colony)
    {
        PlacementProposal? proposal = SettlementPlanner.Probe(colony, DevelopmentKind.Field, null, urgent: false, out _);
        return proposal is null ? null : (proposal.X, proposal.Y);
    }

    /// <summary>Défriche l'emplacement (arbres, buissons et souches) et y trace un champ.</summary>
    public static Field PlanField(LocalMap map, Colony colony, int x, int y)
    {
        // L'emplacement est revalidé (terrain, accès, tracé) ; un outil qui insiste sur un site refusé obtient son champ, enregistré comme un écart historique.
        if (SettlementPlanner.PlanAt(colony, DevelopmentKind.Field, null, x, y).Field is { } planned)
            return planned;
        Field field = OpenField(map, colony, x, y, FieldSizeFor(colony));
        SettlementPlanner.Adopt(colony, field);
        return field;
    }

    /// <summary>La création brute : défrichage des parcelles (arbres, buissons, souches) et ouverture du champ. Seule l'admission d'un projet (ou une migration) l'appelle.</summary>
    internal static Field OpenField(LocalMap map, Colony colony, int x, int y, int size = Field.BaseSize)
    {
        var field = new Field(x, y, size);
        foreach (FieldPlot plot in field.Plots)
            map.ClearFlora(plot.X, plot.Y);
        colony.Fields.Add(field);
        return field;
    }

    /// <summary>Combien de colons faut-il, pour finir les semailles ou la moisson à temps ?</summary>
    public static int WorkersNeeded(Colony colony, GameClock clock)
    {
        const float secondsPerPlotSowing = 4.5f;
        const float secondsPerPlotHarvesting = 6f;
        const float workSecondsPerDay = 15f;

        int fallow = 0, ripe = 0;
        foreach (FieldPlot plot in Plots(colony))
        {
            if (plot.Stage == CropStage.Fallow) fallow++;
            else if (plot.Stage == CropStage.Ripe) ripe++;
        }

        // Le cycle complet compte les trajets : récolter une parcelle, la porter au dépôt le plus proche, revenir (la marche s'ajoute à la durée de la récolte).
        float roundTrip = AverageDepotRoundTripSeconds(colony);

        float seconds = 0f;
        if (IsSowingSeason(clock.Season) && fallow > 0)
        {
            float daysLeft = Math.Max(1, TimeConstants.DaysPerSeason - clock.DayOfYear);
            seconds += fallow * (secondsPerPlotSowing + roundTrip / Math.Max(1, PlotCount(colony) / Math.Max(1, colony.Fields.Count))) / (daysLeft * workSecondsPerDay);
        }
        if (ripe > 0 && clock.Season != Season.Hiver)
        {
            // La moisson ne traîne pas : même s'il reste du temps avant le gel, on la boucle en quelques jours.
            float daysLeft = Math.Clamp(3 * TimeConstants.DaysPerSeason - clock.DayOfYear, 1, 3);
            seconds += ripe * (secondsPerPlotHarvesting + roundTrip) / (daysLeft * workSecondsPerDay);
        }
        return (int)MathF.Ceiling(seconds);
    }

    /// <summary>L'aller-retour moyen d'un champ à son dépôt, en secondes de simulation (à vol d'oiseau, avec un détour) : 0 sans champ.</summary>
    public static float AverageDepotRoundTripSeconds(Colony colony)
    {
        if (colony.Fields.Count == 0)
            return 0f;
        float total = 0f;
        foreach (Field field in colony.Fields)
        {
            ServicePoint? depot = SettlementServices.Nearest(colony, ServiceUse.Stock, field.X + field.Size / 2, field.Y + field.Size / 2);
            total += depot is null ? 0f : 2f * 1.2f * SettlementServices.DistanceTo(colony, depot, field.X + field.Size / 2, field.Y + field.Size / 2) / SettlementRules.WalkTilesPerSecond;
        }
        return total / colony.Fields.Count;
    }

    /// <summary>
    /// Chaque jour, les semis poussent (sauf en hiver) ; au premier jour d'hiver, tout ce qui n'a pas été
    /// moissonné gèle. Renvoie le nombre de parcelles perdues.
    /// </summary>
    public static int DailyUpdate(Colony colony, GameClock clock)
    {
        int lost = 0;
        // Sécheresse : les parcelles non irriguées ne poussent plus (à moitié seulement si la colonie a un puits).
        float drought = colony.DroughtDaysLeft > 0 ? (Civic.Has(colony, BuildingType.Well) ? 0.5f : 0f) : 1f;
        // Les régions froides ont une saison de culture plus courte : la pousse y est plus lente.
        float cold = 1f - 0.3f * Math.Clamp(Climate.ColdSeverity(colony.Map.Biome) - 0.3f, 0f, 0.7f) / 0.7f;
        bool winterStarts = clock.Season == Season.Hiver && clock.DayOfSeason == 1;
        foreach (FieldPlot plot in Plots(colony))
        {
            if (plot.Crop == CropKind.Grapes)
            {
                long since = clock.Ticks - Math.Max(plot.PlantedTicks, plot.LastHarvestTicks);
                plot.Growth = Math.Clamp(since / (float)TimeConstants.TicksPerYear, 0, 1);
                if (plot.Stage == CropStage.Growing && plot.Growth >= 1 && clock.Season == Season.Automne) plot.Stage = CropStage.Ripe;
                continue;
            }
            if (plot.Stage == CropStage.Fallow)
                continue;
            if (winterStarts)
            {
                plot.Stage = CropStage.Fallow;
                plot.Growth = 0f;
                lost++;
            }
            else if (plot.Stage == CropStage.Growing && clock.Season != Season.Hiver)
            {
                float dryFactor = colony.Map.IsIrrigated(plot.X, plot.Y) ? 1f : drought;
                plot.Growth = MathF.Min(1f, plot.Growth + dryFactor * cold / GrowthDays);
                if (plot.Growth >= 1f)
                    plot.Stage = CropStage.Ripe;
            }
        }
        return lost;
    }
}
