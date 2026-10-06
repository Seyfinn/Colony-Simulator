using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Nature;

/// <summary>
/// Les prédateurs attaquent les enclos, les voyageurs et les travailleurs isolés : en général une blessure, rarement la mort. Ils sont plus pressants l'hiver
/// et quand le gibier manque. Un enclos renforcé et des chiens réduisent les pertes. Les herbivores, eux, piétinent les champs proches.
/// </summary>
public static class Predation
{
    /// <summary>Un prédateur plus loin que cela du camp ne menace pas encore les enclos.</summary>
    public const float ThreatRadius = 35f;

    private const float RaidChance = 0.02f;
    private const float FieldReach = 8f;
    private const float EncounterRange = 3f;
    private const float ProtectionRange = 4f;

    /// <summary>La menace brute : les prédateurs proches, selon leur nombre et leur faim, plus pressante l'hiver et quand le gibier manque ; un alpha pèse double.</summary>
    public static float Raw(Colony colony, Season season)
    {
        Settlement place = colony.LocalSettlement;
        float sum = 0f;
        foreach (WildHerd herd in place.Herds.Where(h => h.IsPredator && h.Count > 0))
            if (Wildlife.Distance(herd.X, herd.Y, place.CampX, place.CampY) <= ThreatRadius)
                sum += herd.Count * (0.2f + herd.Hunger) * (herd.IsAlpha ? 2f : 1f);
        return sum * (season == Season.Hiver ? 1.5f : 1f) * (Wildlife.GameAbundance(place) < 30f ? 1.5f : 1f);
    }

    /// <summary>La pression ressentie par la colonie, de 0 à 100.</summary>
    public static float Pressure(Colony colony, Season season) => Math.Min(100f, Raw(colony, season) * 12f);

    /// <summary>Les dégâts des herbivores aux champs, de 0 à 100 : les hardes de proies à moins de huit cases d'un champ.</summary>
    public static float CropRaidPressure(Colony colony)
    {
        Settlement place = colony.LocalSettlement;
        if (place.Fields.Count == 0)
            return 0f;
        return Math.Min(100f, 10f * place.Herds.Count(h => !h.IsPredator && h.Count > 0 && NearField(place, h)));
    }

    private static bool NearField(Settlement place, WildHerd herd) =>
        place.Fields.Any(f => Wildlife.Distance(herd.X, herd.Y, f.X + f.Size / 2f, f.Y + f.Size / 2f) <= FieldReach);

    /// <summary>
    /// Chaque matin : la chance qu'une attaque touche l'enclos (menace × 2 %, ×0,3 si l'enclos est renforcé, ×0,5 avec un chien) ; elle emporte une à trois bêtes,
    /// les plus nombreuses d'abord, jamais la dernière paire. Les herbivores voisins des champs les piétinent.
    /// </summary>
    public static void DailyRaids(WorldState world, Colony colony)
    {
        Settlement place = colony.LocalSettlement;
        float raw = Raw(colony, world.Clock.Season);
        if (raw > 0f && Husbandry.Animals(colony) > 0)
        {
            float chance = raw * RaidChance * (place.PenReinforced ? 0.3f : 1f) * (colony.Stock.Get(ResourceType.Dogs) > 0 ? 0.5f : 1f);
            if (world.Nature.NextSingle() < chance)
                Raid(world, colony, place);
        }

        foreach (WildHerd herd in place.Herds.Where(h => !h.IsPredator && h.Count > 0 && NearField(place, h)))
            foreach (Field field in place.Fields.Where(f => Wildlife.Distance(herd.X, herd.Y, f.X + f.Size / 2f, f.Y + f.Size / 2f) <= FieldReach))
                foreach (FieldPlot plot in field.Plots.Where(p => p.Stage != CropStage.Fallow))
                    plot.Trampled = MathF.Min(1f, plot.Trampled + 0.1f);
    }

    private static void Raid(WorldState world, Colony colony, Settlement place)
    {
        int toTake = 1 + world.Nature.Next(3), lost = 0;
        ResourceType? species = null;
        for (int i = 0; i < toTake; i++)
        {
            ResourceType? target = Husbandry.Species.Where(s => Husbandry.Count(colony, s) > Husbandry.BreedingCore)
                .OrderByDescending(s => Husbandry.Count(colony, s)).Cast<ResourceType?>().FirstOrDefault();
            if (target is not { } victim)
                break;
            Husbandry.Lose(colony, victim, 1);
            species ??= victim;
            lost++;
        }
        if (lost == 0)
            return;
        if (place.Herds.Where(h => h.IsPredator).OrderBy(h => Wildlife.Distance(h.X, h.Y, place.CampX, place.CampY)).FirstOrDefault() is { } fed)
            fed.Hunger = 0f;
        ColonyBrain.Say(colony, world.Clock, $"Des prédateurs ont attaqué l'enclos pendant la nuit : {lost} bête{(lost > 1 ? "s" : "")} perdue{(lost > 1 ? "s" : "")}. "
            + (place.PenReinforced ? "" : "Un enclos renforcé les découragerait."));
    }

    /// <summary>
    /// Chaque heure : un colon à moins de trois cases d'une harde qui le traque est protégé s'il n'est pas seul (deux colons à moins de quatre cases)
    /// ou s'il chasse avec un chien ; sinon le prédateur l'attaque selon son danger (triple pour un alpha) et le blesse, rarement mortellement.
    /// </summary>
    public static void HourlyEncounters(WorldState world, Colony colony)
    {
        Settlement place = colony.LocalSettlement;
        List<WildHerd> stalkers = place.Herds.Where(h => h.IsPredator && h.Count > 0 && h.State == HerdState.Stalking).ToList();
        if (stalkers.Count == 0)
            return;
        List<Colonist> colonists = place.PresentColonists.ToList();
        foreach (Colonist colonist in colonists.OrderBy(c => c.Id))
        {
            if (colonist.Ailment == Ailment.Injured || colonist.Activity is null && Wildlife.Distance(colonist.X, colonist.Y, place.CampX, place.CampY) <= 6f)
                continue;
            WildHerd? attacker = stalkers.FirstOrDefault(h => Wildlife.Distance(h.X, h.Y, colonist.X, colonist.Y) <= EncounterRange);
            if (attacker is null)
                continue;
            bool withDog = colonist.Activity is { Kind: ActivityKind.Hunt or ActivityKind.GreatHunt } && colony.Stock.Get(ResourceType.Dogs) > 0;
            bool company = colonists.Count(o => o != colonist && Wildlife.Distance(o.X, o.Y, colonist.X, colonist.Y) <= ProtectionRange) >= 1;
            if (withDog || company)
                continue;
            float chance = WildSpeciesInfo.Danger(attacker.Species) * (attacker.IsAlpha ? 3f : 1f) * 0.1f;
            if (world.Nature.NextSingle() < chance)
            {
                Health.Injure(world, colonist, $"attaqué par {(attacker.Species == WildSpecies.Bear ? "un ours" : "un loup")}", 0.01f);
                attacker.Hunger = 0f;
                attacker.State = HerdState.Roaming;
            }
        }
    }
}
