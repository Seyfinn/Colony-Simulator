using GodColony.Simulation.Time;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

/// <summary>Le mal dont souffre un colon : une fièvre, ou une blessure reçue au travail ou lors d'une attaque.</summary>
public enum Ailment { None, Sick, Injured }

/// <summary>
/// Le climat de la région : les biomes froids ont des hivers rigoureux (plus de bois, plus de fièvres), les biomes arides
/// connaissent des sécheresses l'été (les champs non irrigués ne poussent plus). Un puits et des vêtements en atténuent les effets.
/// </summary>
public static class Climate
{
    /// <summary>Rigueur de l'hiver du biome, de 0 (doux) à 1 et plus (glacial).</summary>
    public static float ColdSeverity(Biome biome) => biome switch
    {
        Biome.IceSheet => 1.2f,
        Biome.Tundra => 1.0f,
        Biome.BorealForest => 0.8f,
        Biome.Steppe => 0.2f,
        Biome.Grassland or Biome.TemperateForest => 0.1f,
        _ => 0f,
    };

    /// <summary>Chance par jour d'été qu'une sécheresse éclate, selon l'aridité du biome.</summary>
    public static float DroughtChancePerDay(Biome biome) => biome switch
    {
        Biome.Desert => 0.15f,
        Biome.Savanna => 0.08f,
        Biome.Steppe => 0.07f,
        Biome.Grassland => 0.03f,
        Biome.TemperateForest => 0.01f,
        _ => 0f,
    };

    public const int ColdSnapDays = 3;
    public const int DroughtDays = 3;
    private const float ColdSnapWoodFactor = 1.8f;

    /// <summary>Multiplicateur du bois brûlé : les régions froides chauffent davantage, une vague de froid presque deux fois plus.</summary>
    public static float WoodFactor(Colony colony, Season season)
    {
        if (!ColonyBrain.IsColdSeason(season))
            return 1f;
        float factor = 1f + 0.5f * ColdSeverity(colony.Map.Biome);
        return season == Season.Hiver && colony.ColdSnapDaysLeft > 0 ? factor * ColdSnapWoodFactor : factor;
    }

    /// <summary>Chaque matin : une vague de froid ou une sécheresse peut éclater, une autre se terminer.</summary>
    public static void Daily(WorldState world, Colony colony)
    {
        Biome biome = colony.Map.Biome;
        GameClock clock = world.Clock;

        if (colony.ColdSnapDaysLeft > 0)
        {
            if (--colony.ColdSnapDaysLeft == 0)
                ColonyBrain.Say(colony, clock, "La vague de froid passe : l'hiver redevient supportable.");
        }
        else if (clock.Season == Season.Hiver && world.Chance.NextSingle() < ColdSeverity(biome) * 0.10f)
        {
            colony.ColdSnapDaysLeft = ColdSnapDays;
            ColonyBrain.Say(colony, clock, "Un hiver rigoureux s'abat sur la région : il faudra beaucoup plus de bois, et les fièvres guettent.");
        }

        if (colony.DroughtDaysLeft > 0)
        {
            if (--colony.DroughtDaysLeft == 0)
                ColonyBrain.Say(colony, clock, "La sécheresse prend fin : la terre respire.");
        }
        else if (clock.Season == Season.Ete && world.Chance.NextSingle() < DroughtChancePerDay(biome))
        {
            colony.DroughtDaysLeft = DroughtDays;
            ColonyBrain.Say(colony, clock, Civic.Has(colony, BuildingType.Well)
                ? "La sécheresse s'installe : le puits nous sauve, mais les champs non irrigués poussent à peine."
                : "La sécheresse s'installe : les champs non irrigués ne poussent plus. Un puits ou un canal aiderait.");
        }
    }
}

/// <summary>
/// La santé : fièvres (surtout en hiver, quand on a faim, froid ou qu'on dort dehors) et blessures de travail.
/// Un malade se repose ; sans soins il guérit seul, avec un petit risque de mourir. L'infirmerie double la guérison,
/// et un guérisseur qui passe soigner écarte tout danger de mort.
/// </summary>
public static class Health
{
    private const float BaseSicknessPerDay = 0.008f;

    /// <summary>Chance qu'un geste de travail blesse son auteur (plus élevée chez un débutant).</summary>
    public const float InjuryChancePerAction = 0.003f;

    private const float SickDeathChance = 0.03f;
    private const float InjuryDeathChance = 0.04f;
    private const int TreatmentHours = 6;

    public static IEnumerable<Colonist> Patients(Colony colony) => colony.Members.Where(m => m.Ailment != Ailment.None);

    public static int PatientCount(Colony colony) => colony.Members.Count(m => m.Ailment != Ailment.None);

    /// <summary>Chance qu'un colon tombe malade dans la journée.</summary>
    public static float SicknessChancePerDay(Colony colony, Colonist colonist, GameClock clock)
    {
        float chance = BaseSicknessPerDay;
        bool winter = clock.Season == Season.Hiver;
        chance *= winter ? 2.5f : clock.Season == Season.Automne ? 1.4f : 1f;
        chance *= 1f + 0.5f * Climate.ColdSeverity(colony.Map.Biome) * (winter ? 1f : 0f);
        if (colony.Map.Biome == Biome.Swamp)
            chance *= 1.8f;
        if (winter && colony.ColdSnapDaysLeft > 0)
            chance *= 1.8f;
        if (colonist.Home is null && ColonyBrain.IsColdSeason(clock.Season))
            chance *= 1.8f;
        if (colonist.Needs.Food < 0.3f)
            chance *= 1.6f;
        if (ColonyBrain.IsColdSeason(clock.Season))
            chance *= 1f - 0.4f * Husbandry.ClothesCoverage(colony);
        if (Civic.Has(colony, BuildingType.Well))
            chance *= 0.5f;
        if (Knowledge.Has(colony, Discovery.Herbalism))
            chance *= Knowledge.HerbalismFactor;
        if (colonist.Stage == LifeStage.Elder) chance *= 1.5f;
        else if (colonist.Stage == LifeStage.Child) chance *= 1.3f;
        // Une épidémie se propage : chaque malade en plus augmente le risque pour les autres.
        chance *= 1f + 0.25f * Math.Min(4, PatientCount(colony));
        return chance;
    }

    /// <summary>Chaque matin : qui tombe malade aujourd'hui ?</summary>
    public static void Daily(WorldState world, Colony colony)
    {
        foreach (Colonist colonist in colony.Members.ToList())
        {
            if (colonist.Ailment != Ailment.None)
                continue;
            if (world.Chance.NextSingle() < SicknessChancePerDay(colony, colonist, world.Clock))
            {
                colony.IllnessCases++;
                Fall(colony, colonist, Ailment.Sick, 36 + world.Chance.Next(0, 36), world.Clock,
                    $"{colonist.Name} est pris{(colonist.Sex == Sex.Female ? "e" : "")} de fièvre et doit garder le lit.");
            }
        }
    }

    /// <summary>Un geste de travail peut blesser : plus facilement un débutant, un audacieux ou quelqu'un qui n'a pas d'outils.</summary>
    public static void MaybeInjure(WorldState world, Colonist colonist, SkillType skill)
    {
        if (colonist.Ailment != Ailment.None)
            return;
        float chance = InjuryChancePerAction * (colonist.Skills.Level(skill) < 5f ? 1.6f : 1f)
            * (1f + 0.3f * colonist.Personality[Axis.Audace]);
        if (world.Chance.NextSingle() < chance)
            Fall(colonist.Colony, colonist, Ailment.Injured, 60 + world.Chance.Next(0, 36), world.Clock,
                $"{colonist.Name} s'est blessé{(colonist.Sex == Sex.Female ? "e" : "")} en travaillant.");
    }

    public static void Fall(Colony colony, Colonist colonist, Ailment kind, int hours, GameClock clock, string thought)
    {
        colonist.Ailment = kind;
        colonist.AilmentHours = hours;
        colonist.Treated = false;
        colonist.Needs.Illness = kind == Ailment.Sick ? 0.6f : 0.8f;
        // La colonie ne radote pas : une mention par jour au plus.
        if (thought.Length > 0 && clock.TotalDays != colony.LastHealthThoughtDay)
        {
            colony.LastHealthThoughtDay = clock.TotalDays;
            ColonyBrain.Say(colony, clock, thought);
        }
    }

    /// <summary>Chaque heure, la convalescence avance (deux fois plus vite à l'infirmerie) ; au bout, on guérit ou l'on succombe.</summary>
    public static void Hourly(WorldState world, Colony colony)
    {
        foreach (Colonist colonist in colony.Members.ToList())
        {
            if (colonist.Ailment == Ailment.None)
                continue;
            bool atInfirmary = colonist.Activity is { Kind: ActivityKind.Relax, Started: true, Building.Type: BuildingType.Infirmary };
            colonist.AilmentHours -= atInfirmary ? 2 : 1;
            if (colonist.AilmentHours > 0)
                continue;

            Ailment kind = colonist.Ailment;
            colonist.Ailment = Ailment.None;
            colonist.Needs.Illness = 0f;
            float death = colonist.Treated ? 0f : kind == Ailment.Sick ? SickDeathChance : InjuryDeathChance;
            if (colonist.Stage is LifeStage.Elder or LifeStage.Child)
                death *= 2f;
            if (colonist.Needs.Food < 0.3f)
                death *= 1.5f;
            colonist.Treated = false;
            if (death > 0f && world.Chance.NextSingle() < death)
                Lifecycle.Die(world, colonist, kind == Ailment.Sick ? "maladie" : "blessure");
        }
    }

    /// <summary>Un guérisseur passe à l'infirmerie : tous les malades sont soignés, leur convalescence raccourcit.</summary>
    public static void Treat(Colony colony)
    {
        foreach (Colonist patient in Patients(colony))
        {
            patient.Treated = true;
            patient.AilmentHours = Math.Max(1, patient.AilmentHours - TreatmentHours);
        }
    }

    public static string Describe(Colonist colonist) => colonist.Ailment switch
    {
        Ailment.Sick => "Malade (fièvre)",
        Ailment.Injured => "Blessé",
        _ => "",
    };
}
