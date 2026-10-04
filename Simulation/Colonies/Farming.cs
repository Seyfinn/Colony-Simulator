using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'agriculture : on défriche des champs près du camp, on sème au printemps, les céréales poussent
/// pendant sept jours, puis on moissonne. Ce qui n'est pas rentré avant l'hiver gèle sur pied.
/// </summary>
public static class Farming
{
    /// <summary>Céréales rapportées par une parcelle mûre.</summary>
    public const int PlotYield = 4;

    /// <summary>Céréales de plus pour une parcelle sur une berge fertile (à moins de deux cases de l'eau).</summary>
    public const int BankBonus = 1;

    /// <summary>Ce que rapporte la parcelle de cette case : plus sur les berges.</summary>
    public static int YieldAt(LocalMap map, int x, int y) =>
        (int)MathF.Round(PlotYield * map.SoilRichness) + (map.IsFertileBank(x, y) ? BankBonus : 0) + (map.IsIrrigated(x, y) ? IrrigationBonus : 0);

    /// <summary>Céréales de plus pour une parcelle qu'un canal irrigue.</summary>
    public const int IrrigationBonus = 2;

    /// <summary>Jours de pousse entre le semis et la maturité (la pousse s'arrête en hiver).</summary>
    public const float GrowthDays = 7f;

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
        return (int)MathF.Ceiling(colony.Members.Count * FullCoveragePlotsPerColonist * coverage);
    }

    /// <summary>Place libre pour un champ de 4 × 4 : plat, de bonne terre, le moins boisé possible, près du camp.</summary>
    public static (int X, int Y)? FindFieldSite(LocalMap map, Colony colony)
    {
        (int X, int Y)? best = null;
        float bestScore = float.MaxValue;

        for (int dy = -MaxDistanceFromFire; dy <= MaxDistanceFromFire; dy++)
        for (int dx = -MaxDistanceFromFire; dx <= MaxDistanceFromFire; dx++)
        {
            int x = colony.CampX + dx, y = colony.CampY + dy;
            float distance = MathF.Sqrt((dx + Field.Size / 2f) * (dx + Field.Size / 2f) + (dy + Field.Size / 2f) * (dy + Field.Size / 2f));
            if (distance < MinDistanceFromFire || distance > MaxDistanceFromFire)
                continue;
            if (FieldCost(map, colony, x, y) is not { } clearing)
                continue;

            float score = distance + clearing;
            if (score < bestScore)
            {
                bestScore = score;
                best = (x, y);
            }
        }
        return best;
    }

    /// <summary>Le coût de défrichage d'un emplacement (arbres et buissons à arracher), ou null s'il est inutilisable.</summary>
    private static float? FieldCost(LocalMap map, Colony colony, int x, int y)
    {
        if (!map.InBounds(x, y) || !map.InBounds(x + Field.Size - 1, y + Field.Size - 1))
            return null;
        int elevation = map.GetElevation(x, y);
        float clearing = 0f;
        for (int ty = y; ty < y + Field.Size; ty++)
        for (int tx = x; tx < x + Field.Size; tx++)
        {
            if (!map.IsWalkable(tx, ty) || map.IsWaterway(tx, ty) || colony.CanalTiles.Contains((tx, ty))
                || map.IsMountain(tx, ty) || map.GetElevation(tx, ty) != elevation)
                return null;
            if (map.GetSoil(tx, ty) == SoilType.Sand)
                return null;
            if (map.IsFertileBank(tx, ty))
                clearing -= BankPreference;
            clearing += map.GetFlora(tx, ty) switch
            {
                FloraType.Tree => TreePenalty,
                FloraType.Bush => BushPenalty,
                _ => 0f,
            };
        }

        // Les champs touchent leurs voisins, mais laissent un passage autour des bâtiments.
        foreach (Building other in colony.Buildings)
            if (x < other.X + other.Width + 1 && x + Field.Size > other.X - 1 && y < other.Y + other.Height + 1 && y + Field.Size > other.Y - 1)
                return null;
        foreach (Grave grave in colony.Graves)
            if (grave.X >= x && grave.X < x + Field.Size && grave.Y >= y && grave.Y < y + Field.Size)
                return null;
        foreach (Field other in colony.Fields)
            if (x < other.X + Field.Size && x + Field.Size > other.X && y < other.Y + Field.Size && y + Field.Size > other.Y)
                return null;
        return clearing;
    }

    /// <summary>Défriche l'emplacement (arbres, buissons et souches) et y trace un champ.</summary>
    public static Field PlanField(LocalMap map, Colony colony, int x, int y)
    {
        var field = new Field(x, y);
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

        float seconds = 0f;
        if (IsSowingSeason(clock.Season) && fallow > 0)
        {
            float daysLeft = Math.Max(1, TimeConstants.DaysPerSeason - clock.DayOfYear);
            seconds += fallow * secondsPerPlotSowing / (daysLeft * workSecondsPerDay);
        }
        if (ripe > 0 && clock.Season != Season.Hiver)
        {
            // La moisson ne traîne pas : même s'il reste du temps avant le gel, on la boucle en quelques jours.
            float daysLeft = Math.Clamp(3 * TimeConstants.DaysPerSeason - clock.DayOfYear, 1, 3);
            seconds += ripe * secondsPerPlotHarvesting / (daysLeft * workSecondsPerDay);
        }
        return (int)MathF.Ceiling(seconds);
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
