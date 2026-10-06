using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Nature;

/// <summary>Une grande chasse collective contre un alpha : qui y va, où en est le rassemblement, depuis quand.</summary>
public sealed class GreatHuntState
{
    public int HerdId { get; internal set; }
    public List<int> HunterIds { get; internal set; } = [];
    public List<int> Arrived { get; internal set; } = [];
    public long StartTicks { get; internal set; }
}

/// <summary>
/// La chasse : les habitants chassent quand la viande rapportée par heure de travail l'emporte sur la cueillette et la pêche (la comparaison se fait dans
/// <see cref="ColonistAI"/>, avec les mêmes mesures). Chaque chasse a une chance de réussir selon l'habileté, les chiens et les outils ; la surchasse fait fuir
/// et décroître le gibier. Quand un alpha terrorise la région, les plus téméraires se rassemblent pour une grande chasse.
/// </summary>
public static class Hunting
{
    /// <summary>En dessous de ce gibier (sur 100), on ne chasse plus : on laisse la région se repeupler au lieu de la vider.</summary>
    public const float MinAbundance = 30f;

    /// <summary>Portée de marche d'une chasse, en cases de la carte locale.</summary>
    public const int Reach = 60;

    /// <summary>Distance maximale entre le chasseur et la harde pour tirer : la harde a pu bouger depuis son départ.</summary>
    public const float ShotRange = 8f;

    private const float AlphaPressure = 60f;
    private const int MinHunters = 4, MaxHunters = 8;
    private const long GreatHuntWaitTicks = (long)(2 * TimeConstants.TicksPerDay);
    private const long GreatHuntGatherTicks = TimeConstants.TicksPerDay;

    /// <summary>Les proies que ce colon peut viser, de la plus proche à la plus lointaine.</summary>
    public static IEnumerable<WildHerd> Candidates(Colony colony, Colonist colonist) =>
        (colony.Sensors?.GameAbundance ?? 100f) < MinAbundance ? []
        : colony.LocalSettlement.Herds
            .Where(h => !h.IsPredator && h.Count > 0 && (h.ReservedBy == 0 || h.ReservedBy == colonist.Id)
                && Math.Abs(h.X - colonist.X) + Math.Abs(h.Y - colonist.Y) <= Reach)
            .OrderBy(h => Math.Abs(h.X - colonist.X) + Math.Abs(h.Y - colonist.Y));

    /// <summary>Chance qu'une chasse réussisse : 35 %, plus 3 % par niveau d'habileté, 15 % avec un chien, 10 % avec des outils de fer.</summary>
    public static float SuccessChance(Colony colony, Colonist colonist) =>
        Math.Clamp(0.35f + 0.03f * colonist.Skills.Level(SkillType.Hunting)
            + (colony.Stock.Get(ResourceType.Dogs) > 0 ? 0.15f : 0f) + (colony.Stock.Get(ResourceType.Tools) > 0 ? 0.1f : 0f), 0.05f, 0.95f);

    /// <summary>Les repas qu'on espère d'une chasse (viande × chance de réussite) : c'est ce que le colon compare à la cueillette.</summary>
    public static float ExpectedMeals(Colony colony, Colonist colonist, WildHerd herd) =>
        WildSpeciesInfo.Meat(herd.Species) * SuccessChance(colony, colonist);

    /// <summary>Le colon arrive : la harde est-elle toujours là, à portée, et libre ? Si oui, il la réserve.</summary>
    public static bool CanBegin(Colony colony, Colonist colonist, Activity activity)
    {
        if (Wildlife.HerdById(colony.LocalSettlement, activity.HerdId) is not { Count: > 0 } herd
            || Wildlife.Distance(colonist.X, colonist.Y, herd.X, herd.Y) > ShotRange)
            return false;
        if (activity.Kind == ActivityKind.Hunt)
        {
            if (herd.ReservedBy != 0 && herd.ReservedBy != colonist.Id)
                return false;
            herd.ReservedBy = colonist.Id;
        }
        return true;
    }

    /// <summary>Libère la harde réservée par ce colon (fin ou abandon de sa chasse ou de sa capture).</summary>
    public static void Release(Colony colony, Colonist colonist, Activity activity)
    {
        if (activity.HerdId != 0 && Wildlife.HerdById(colony.LocalSettlement, activity.HerdId) is { } herd && herd.ReservedBy == colonist.Id)
            herd.ReservedBy = 0;
    }

    /// <summary>
    /// Résout une chasse : en cas de réussite le colon rapporte la viande (les peaux vont directement au stock, comme à l'abattage) ; la harde fuit dans tous les cas.
    /// Un sanglier ou un ours acculé peut blesser.
    /// </summary>
    public static void Resolve(WorldState world, Colonist colonist, Activity activity)
    {
        Colony colony = colonist.Colony;
        Settlement place = colony.LocalSettlement;
        if (Wildlife.HerdById(place, activity.HerdId) is not { Count: > 0 } herd)
            return;
        herd.ReservedBy = 0;
        if (Wildlife.Distance(colonist.X, colonist.Y, herd.X, herd.Y) > ShotRange)
            return;
        if (world.Nature.NextSingle() < SuccessChance(colony, colonist))
        {
            int kills = herd.Species is WildSpecies.Boar or WildSpecies.Deer && colonist.Skills.Level(SkillType.Hunting) > 10f && herd.Count >= 2 ? 2 : 1;
            kills = Math.Min(kills, herd.Count);
            herd.Count -= kills;
            herd.Young = Math.Min(herd.Young, herd.Count);
            colonist.Carrying = (ResourceType.Meat, WildSpeciesInfo.Meat(herd.Species) * kills);
            if (WildSpeciesInfo.Hides(herd.Species) * kills is > 0 and var hides)
                colony.Stock.Add(ResourceType.Hides, hides);
            if (herd.Count <= 0)
                place.Herds.Remove(herd);
        }
        herd.State = HerdState.Fleeing;
        if (herd.Species is WildSpecies.Boar or WildSpecies.Bear && world.Nature.NextSingle() < 0.03f)
            Health.Injure(world, colonist, $"blessé par {(herd.Species == WildSpecies.Boar ? "un sanglier acculé" : "un ours")}", 0.01f);
    }

    // --- La grande chasse ---

    /// <summary>
    /// Un alpha terrorise la région (grande pression des prédateurs) : les adultes valides les plus téméraires, puis les meilleurs chasseurs (quatre à huit),
    /// sont désignés. Ils partent dès qu'ils sont libres ; ceux qui arrivent à temps affrontent l'alpha.
    /// </summary>
    public static void PlanGreatHunt(Colony colony, GameClock clock)
    {
        Settlement place = colony.LocalSettlement;
        if (place.GreatHunt is not null || colony.Sensors is not { PredatorPressure: > AlphaPressure }
            || clock.Ticks - place.LastGreatHuntTicks < GreatHuntWaitTicks
            || place.Herds.FirstOrDefault(h => h.IsAlpha && h.Count > 0) is not { } alpha)
            return;
        List<Colonist> hunters = colony.PresentMembers
            .Where(c => c.Stage == LifeStage.Adult && c.Ailment == Ailment.None && !c.IsSleeping && c.Transit == TransitState.None)
            .OrderByDescending(c => c.Personality[Axis.Audace]).ThenByDescending(c => c.Skills.Level(SkillType.Hunting)).ThenBy(c => c.Id)
            .Take(MaxHunters).ToList();
        if (hunters.Count < MinHunters)
            return;
        place.GreatHunt = new GreatHuntState { HerdId = alpha.Id, HunterIds = hunters.Select(h => h.Id).ToList(), StartTicks = clock.Ticks };
        ColonyBrain.Say(colony, clock, $"{(alpha.Species == WildSpecies.Bear ? "L'ours" : "Le loup")} « {alpha.AlphaName} » menace trop la colonie : "
            + $"{hunters.Count} chasseurs, les plus téméraires, se préparent à le traquer ensemble.");
    }

    /// <summary>Le colon est-il désigné pour la grande chasse et n'y est-il pas déjà arrivé ?</summary>
    public static bool IsSummoned(Colony colony, Colonist colonist) =>
        colony.LocalSettlement.GreatHunt is { } hunt && hunt.HunterIds.Contains(colonist.Id) && !hunt.Arrived.Contains(colonist.Id);

    /// <summary>Où retrouver l'alpha (null si la grande chasse n'a plus de cible).</summary>
    public static WildHerd? GreatHuntTarget(Colony colony) =>
        colony.LocalSettlement.GreatHunt is { } hunt ? Wildlife.HerdById(colony.LocalSettlement, hunt.HerdId) : null;

    /// <summary>Un chasseur est arrivé : quand tous les chasseurs encore valides sont là, l'alpha est affronté.</summary>
    public static void Arrived(WorldState world, Colonist colonist)
    {
        Colony colony = colonist.Colony;
        if (colony.LocalSettlement.GreatHunt is not { } hunt)
            return;
        if (!hunt.Arrived.Contains(colonist.Id))
            hunt.Arrived.Add(colonist.Id);
        if (hunt.HunterIds.All(id => hunt.Arrived.Contains(id) || colony.PresentMembers.All(m => m.Id != id)))
            ResolveGreatHunt(world, colony);
    }

    /// <summary>Chaque jour : une grande chasse qui n'a pas rassemblé ses chasseurs en un jour s'engage avec ceux qui sont là, ou s'abandonne.</summary>
    public static void Daily(WorldState world, Colony colony)
    {
        Settlement place = colony.LocalSettlement;
        if (place.GreatHunt is not { } hunt)
            return;
        if (Wildlife.HerdById(place, hunt.HerdId) is not { Count: > 0 })
        {
            place.GreatHunt = null;
            return;
        }
        if (world.Clock.Ticks - hunt.StartTicks < GreatHuntGatherTicks)
            return;
        if (hunt.Arrived.Count >= 2)
            ResolveGreatHunt(world, colony);
        else
        {
            place.GreatHunt = null;
            place.LastGreatHuntTicks = world.Clock.Ticks;
        }
    }

    /// <summary>
    /// L'affrontement : la force des chasseurs réunis (1 + 5 % par niveau chacun, ×1,4 avec des outils, ×1,2 avec des chiens) contre le danger de l'alpha.
    /// Victoire : l'alpha tombe, viande et peaux au stock, prestige pour la colonie, renom pour les chasseurs (davantage pour le meilleur).
    /// Défaite : un ou deux blessés, l'alpha reste.
    /// </summary>
    public static void ResolveGreatHunt(WorldState world, Colony colony)
    {
        Settlement place = colony.LocalSettlement;
        if (place.GreatHunt is not { } hunt)
            return;
        place.GreatHunt = null;
        place.LastGreatHuntTicks = world.Clock.Ticks;
        List<Colonist> party = hunt.Arrived.Select(id => colony.PresentMembers.FirstOrDefault(m => m.Id == id)).OfType<Colonist>().OrderBy(c => c.Id).ToList();
        if (Wildlife.HerdById(place, hunt.HerdId) is not { Count: > 0 } alpha || party.Count == 0)
            return;
        float force = party.Sum(c => 1f + 0.05f * c.Skills.Level(SkillType.Hunting))
            * (colony.Stock.Get(ResourceType.Tools) > 0 ? 1.4f : 1f) * (colony.Stock.Get(ResourceType.Dogs) > 0 ? 1.2f : 1f);
        string what = alpha.Species == WildSpecies.Bear ? "l'ours" : "le loup";
        if (world.Nature.NextSingle() < force / (force + WildSpeciesInfo.AlphaDanger(alpha.Species)))
        {
            place.Herds.Remove(alpha);
            colony.Stock.Add(ResourceType.Meat, WildSpeciesInfo.Meat(alpha.Species) * Math.Max(1, alpha.Count));
            if (WildSpeciesInfo.Hides(alpha.Species) > 0)
                colony.Stock.Add(ResourceType.Hides, WildSpeciesInfo.Hides(alpha.Species) * Math.Max(1, alpha.Count));
            colony.Prestige += 10;
            Colonist killer = party.OrderByDescending(c => c.Skills.Level(SkillType.Hunting)).ThenBy(c => c.Id).First();
            foreach (Colonist hunter in party)
                hunter.Renown += hunter == killer ? 10f : 5f;
            ColonyBrain.Say(colony, world.Clock, $"{killer.Name} a abattu {what} « {alpha.AlphaName} » : {party.Count} chasseurs sont rentrés en héros, la colonie respire.");
        }
        else
        {
            int wounded = Math.Min(party.Count, 1 + world.Nature.Next(2));
            foreach (Colonist hurt in party.OrderBy(c => c.Id).Take(wounded))
                Health.Injure(world, hurt, $"blessé par {what} « {alpha.AlphaName} »", 0.01f);
            alpha.State = HerdState.Roaming;
            ColonyBrain.Say(colony, world.Clock, $"La grande chasse a échoué : {what} « {alpha.AlphaName} » a repoussé les chasseurs, {wounded} sont blessés.");
        }
    }
}
