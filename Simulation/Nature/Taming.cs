using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Nature;

/// <summary>Une bête capturée qu'on apprivoise à l'enclos : elle devient domestique au bout de <see cref="Required"/> jours de soins.</summary>
public sealed class TamingAnimal
{
    /// <summary>La forme domestique visée (poule, mouton, vache, cheval, bœuf, chien).</summary>
    public ResourceType Species { get; internal set; }
    public bool IsYoung { get; internal set; }

    /// <summary>Jours de soins accumulés ; elle recule quand on la néglige.</summary>
    public float Progress { get; internal set; }
    public int Required { get; internal set; }
    public long CapturedTicks { get; internal set; }
}

/// <summary>La lignée d'une espèce dans une colonie : ses bêtes nées en enclos deviennent plus dociles et plus productives, génération après génération.</summary>
public sealed class LivestockLine
{
    public ResourceType Species { get; internal set; }

    /// <summary>De 0 à 1 : facilite la capture et abrège l'apprivoisement des bêtes de cette espèce.</summary>
    public float Docility { get; internal set; }

    /// <summary>Multiplie les œufs, le lait et la laine (jusqu'à 1,5).</summary>
    public float Yield { get; internal set; } = 1f;
    public int Generations { get; internal set; }
    public string Name { get; internal set; } = "";

    public const float MaxYield = 1.5f;
}

/// <summary>
/// Capturer, apprivoiser, élever. Tous les animaux sont sauvages au départ : l'enclos achevé est vide. On capture une bête (un jeune de préférence), on la soigne et
/// on la nourrit à l'enclos quelques jours, puis elle y entre. Un loup apprivoisé devient un chien, qui réduit les attaques et aide à la chasse.
/// </summary>
public static class Taming
{
    private const float CapturedPerFeed = 4f;

    /// <summary>La forme sauvage d'une bête domestique : celle qui retourne à la nature si l'animal s'enfuit.</summary>
    public static WildSpecies WildFormOf(ResourceType species) => species switch
    {
        ResourceType.Chickens => WildSpecies.Junglefowl,
        ResourceType.Sheep => WildSpecies.Mouflon,
        ResourceType.Horses => WildSpecies.Horse,
        ResourceType.Dogs => WildSpecies.Wolf,
        _ => WildSpecies.Aurochs,
    };

    public static int InTraining(Colony colony, ResourceType species) => colony.LocalSettlement.Taming.Count(t => t.Species == species);

    /// <summary>Y a-t-il de la place à l'enclos pour une bête de plus de cette espèce (celles en apprivoisement comptent) ?</summary>
    public static bool HasSpace(Colony colony, ResourceType species) =>
        Husbandry.Pens(colony) > 0
        && (species == ResourceType.Dogs || Husbandry.Count(colony, species) + InTraining(colony, species) < Husbandry.CapacityOf(colony, species));

    /// <summary>Ce que devient une bête capturée : un adulte d'aurochs est dressé au joug (bœuf) dès que la colonie a de quoi faire un troupeau de vaches.</summary>
    private static ResourceType FormOf(Colony colony, WildHerd herd, bool young)
    {
        ResourceType form = WildSpeciesInfo.DomesticForm(herd.Species)!.Value;
        return form == ResourceType.Cows && !young && Husbandry.Count(colony, ResourceType.Cows) + InTraining(colony, ResourceType.Cows) >= Husbandry.BreedingCore
            ? ResourceType.Oxen : form;
    }

    /// <summary>La harde est-elle domestiquable (et la colonie a-t-elle de la place pour la bête) ?</summary>
    private static bool Fits(Colony colony, WildHerd herd) =>
        !herd.IsAlpha && herd.Count > 0 && WildSpeciesInfo.DomesticForm(herd.Species) is { } form
        && HasSpace(colony, FormOf(colony, herd, herd.Young > 0));

    /// <summary>Une harde domestiquable est en vue : la colonie bâtit un enclos sans attendre d'avoir des bêtes à y mettre.</summary>
    public static bool SeesDomesticable(Colony colony) =>
        colony.LocalSettlement.Herds.Any(h => !h.IsAlpha && h.Count > 0 && WildSpeciesInfo.DomesticForm(h.Species) is not null);

    /// <summary>L'enclos existe et une harde domestiquable est en vue : on peut capturer.</summary>
    public static bool Opportunity(Colony colony) => Husbandry.Pens(colony) > 0 && colony.LocalSettlement.Herds.Any(h => Fits(colony, h));

    /// <summary>Les bêtes à capturer, les jeunes d'abord, puis les espèces absentes de l'enclos, puis les plus proches.</summary>
    public static IEnumerable<WildHerd> Targets(Colony colony, Colonist colonist) =>
        colony.LocalSettlement.Herds
            .Where(h => Fits(colony, h) && (h.ReservedBy == 0 || h.ReservedBy == colonist.Id)
                && Math.Abs(h.X - colonist.X) + Math.Abs(h.Y - colonist.Y) <= Hunting.Reach)
            .OrderByDescending(h => h.Young > 0)
            .ThenBy(h => Husbandry.Count(colony, FormOf(colony, h, h.Young > 0)) + InTraining(colony, FormOf(colony, h, h.Young > 0)) > 0)
            .ThenBy(h => Math.Abs(h.X - colonist.X) + Math.Abs(h.Y - colonist.Y));

    public static float Docility(Colony colony, ResourceType species) =>
        colony.LocalSettlement.Lines.TryGetValue(species, out LivestockLine? line) ? line.Docility : 0f;

    public static float CaptureChance(Colony colony, Colonist colonist, WildHerd herd) =>
        Math.Clamp(0.25f + 0.02f * colonist.Skills.Level(SkillType.Husbandry) + (herd.Young > 0 ? 0.25f : 0f)
            + 0.1f * Docility(colony, FormOf(colony, herd, herd.Young > 0)), 0.05f, 0.95f);

    public static bool CanBegin(Colony colony, Colonist colonist, Activity activity)
    {
        if (Wildlife.HerdById(colony.LocalSettlement, activity.HerdId) is not { } herd || !Fits(colony, herd)
            || Wildlife.Distance(colonist.X, colonist.Y, herd.X, herd.Y) > Hunting.ShotRange
            || herd.ReservedBy != 0 && herd.ReservedBy != colonist.Id)
            return false;
        herd.ReservedBy = colonist.Id;
        return true;
    }

    /// <summary>Résout une capture : en cas de réussite la bête quitte la harde pour l'enclos, où elle sera apprivoisée ; sinon la harde fuit, et une bête vigoureuse peut blesser.</summary>
    public static void ResolveCapture(WorldState world, Colonist colonist, Activity activity)
    {
        Colony colony = colonist.Colony;
        Settlement place = colony.LocalSettlement;
        if (Wildlife.HerdById(place, activity.HerdId) is not { Count: > 0 } herd)
            return;
        herd.ReservedBy = 0;
        if (!Fits(colony, herd) || Wildlife.Distance(colonist.X, colonist.Y, herd.X, herd.Y) > Hunting.ShotRange)
            return;
        bool young = herd.Young > 0;
        ResourceType form = FormOf(colony, herd, young);
        if (world.Nature.NextSingle() < CaptureChance(colony, colonist, herd))
        {
            bool first = InTraining(colony, form) + Husbandry.Count(colony, form) + colony.Stock.Get(form) == 0;
            herd.Count--;
            if (young) herd.Young--;
            int days = WildSpeciesInfo.TamingDays(herd.Species, young);
            place.Taming.Add(new TamingAnimal
            {
                Species = form, IsYoung = young, Required = Math.Max(1, (int)MathF.Round(days * (1f - 0.3f * Docility(colony, form)))),
                CapturedTicks = world.Clock.Ticks,
            });
            if (herd.Count <= 0)
                place.Herds.Remove(herd);
            if (first)
                ColonyBrain.Say(colony, world.Clock, form == ResourceType.Dogs
                    ? "Un loup a été capturé : on le nourrit à l'enclos, peut-être deviendra-t-il le compagnon des chasseurs."
                    : $"Une première bête sauvage est capturée ({Trade.GoodName(form, 1)}) : il faudra la soigner à l'enclos pour l'apprivoiser.");
        }
        else
        {
            herd.State = HerdState.Fleeing;
            if (herd.Species is WildSpecies.Aurochs or WildSpecies.Horse or WildSpecies.Wolf && world.Nature.NextSingle() < 0.05f)
                Health.Injure(world, colonist, $"blessé en capturant {(herd.Species == WildSpecies.Wolf ? "un loup" : "une bête sauvage")}", 0.01f);
        }
    }

    public static void Release(Colony colony, Colonist colonist, Activity activity) => Hunting.Release(colony, colonist, activity);

    /// <summary>Y a-t-il des bêtes à soigner aujourd'hui à l'enclos ?</summary>
    public static bool WorkPending(Colony colony) => colony.LocalSettlement is { TamedToday: false, Taming.Count: > 0 };

    /// <summary>Un colon soigne les bêtes capturées : elles progresseront aujourd'hui.</summary>
    public static void Care(Colony colony) => colony.LocalSettlement.TamedToday = true;

    /// <summary>
    /// Chaque jour : les bêtes soignées (et nourries : l'herbe l'été, le grain l'hiver ou pendant une sécheresse) avancent d'un jour ;
    /// les autres reculent, et une bête qui tombe à zéro peut s'enfuir. Celles qui sont prêtes entrent à l'enclos, ou au stock si l'enclos est plein.
    /// </summary>
    public static void Daily(WorldState world, Colony colony)
    {
        Settlement place = colony.LocalSettlement;
        bool tended = place.TamedToday;
        place.TamedToday = false;
        if (place.Taming.Count == 0)
            return;
        bool needsGrain = world.Clock.Season == Season.Hiver || colony.DroughtDaysLeft > 0;
        bool fed = tended && (!needsGrain || colony.Stock.TryTake(ResourceType.Grain, (int)MathF.Ceiling(place.Taming.Count / CapturedPerFeed)));
        foreach (TamingAnimal animal in place.Taming.ToList())
        {
            animal.Progress = fed ? animal.Progress + 1f : MathF.Max(0f, animal.Progress - 0.5f);
            if (animal.Progress >= animal.Required)
            {
                place.Taming.Remove(animal);
                Graduate(world, colony, animal);
            }
            else if (animal.Progress <= 0f && world.Nature.NextSingle() < 0.1f)
            {
                place.Taming.Remove(animal);
                if (place.Wildlife is { } wild)
                {
                    WildSpecies back = WildFormOf(animal.Species);
                    wild.Population[back] = wild.PopulationOf(back) + 1;
                }
                ColonyBrain.Say(colony, world.Clock, $"Faute de soins, {Trade.GoodName(animal.Species, 1)} capturé s'est enfui de l'enclos.");
            }
        }
    }

    private static void Graduate(WorldState world, Colony colony, TamingAnimal animal)
    {
        ResourceType species = animal.Species;
        bool first = colony.Stock.Get(species) + Husbandry.Count(colony, species) == 0;
        if (species == ResourceType.Dogs)
            colony.Stock.Add(ResourceType.Dogs, 1);
        else
            Husbandry.AddTamed(colony, species);
        LivestockLine line = LineOf(colony, species);
        line.Generations = Math.Max(line.Generations, 1);
        if (first)
            ColonyBrain.Say(colony, world.Clock, species == ResourceType.Dogs
                ? "Le loup est devenu chien : il suivra les chasseurs et gardera les enclos."
                : $"La première bête est apprivoisée : {Trade.GoodName(species, 1)} s'installe à l'enclos.");
    }

    public static LivestockLine LineOf(Colony colony, ResourceType species)
    {
        if (!colony.LocalSettlement.Lines.TryGetValue(species, out LivestockLine? line))
            colony.LocalSettlement.Lines[species] = line = new LivestockLine { Species = species, Name = $"{Trade.GoodName(species, 2)} de {colony.Name}" };
        return line;
    }

    /// <summary>Une naissance en enclos : la lignée devient un peu plus docile et plus productive.</summary>
    public static void OnBirth(Colony colony, ResourceType species)
    {
        LivestockLine line = LineOf(colony, species);
        line.Docility += 0.01f * (1f - line.Docility);
        line.Yield = MathF.Min(LivestockLine.MaxYield, line.Yield + 0.005f);
        line.Generations++;
    }

    /// <summary>Le rendement de la lignée (1 sans lignée) : il multiplie les œufs, le lait et la laine.</summary>
    public static float Yield(Colony colony, ResourceType species) =>
        colony.LocalSettlement.Lines.TryGetValue(species, out LivestockLine? line) ? line.Yield : 1f;
}
