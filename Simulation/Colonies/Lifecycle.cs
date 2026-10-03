using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Une tombe au cimetière de la colonie. X et Y valent -1 si aucune place n'a été trouvée.</summary>
public sealed record Grave(string FullName, Sex Sex, float AgeYears, string Cause, long Ticks, int X, int Y);

/// <summary>
/// Le cycle de la vie : on s'attache et on forme un couple (un homme et une femme, pour qu'il puisse avoir des enfants),
/// on met des enfants au monde quand la colonie en a les moyens, on vieillit, et on meurt de vieillesse ou de faim.
/// Pas d'accident ni de maladie : seule la vie suit son cours.
/// </summary>
public static class Lifecycle
{
    // Couples
    public const float CoupleAffinity = 45f;
    public const float CoupleCompatibility = 0.4f;

    // Naissances
    public const float PregnancyDays = 5f;

    /// <summary>Un couple attend au moins un an entre deux naissances.</summary>
    public const float MinBirthIntervalYears = 1f;
    public const float FertileUntilAge = 14f;

    /// <summary>Chance par jour qu'un couple éligible conçoive, dans une colonie parfaitement prospère.</summary>
    public const float ConceptionChancePerDay = 0.07f;

    // Mort
    public const float OldAgeStart = 17f;
    public const float OldAgeCertain = 23f;
    public const int StarvationWarningHours = 24;
    public const int StarvationDeathHours = 48;

    /// <summary>
    /// La prospérité vue par les couples, de 0 à 1 : réserves de nourriture, toits disponibles, humeur générale.
    /// En temps de crise, on renonce à avoir des enfants.
    /// </summary>
    public static float Prosperity(Colony colony, GameClock clock)
    {
        ColonySensors sensors = ColonyBrain.Sense(colony, clock);
        float food = Math.Clamp((sensors.FoodDays - 3f) / 5f, 0f, 1f);
        float housing = 1f - 0.8f * Math.Clamp((sensors.Homeless - 2f) / 4f, 0f, 1f);
        float mood = Math.Clamp((colony.AverageMood - 0.35f) / 0.3f, 0f, 1f);
        return food * housing * mood;
    }

    /// <summary>Chance de mourir de vieillesse dans la journée, selon l'âge : nulle avant 17 ans, certaine à 23.</summary>
    public static float OldAgeDeathChance(float ageYears)
    {
        if (ageYears < OldAgeStart)
            return 0f;
        if (ageYears >= OldAgeCertain)
            return 1f;
        float progress = (ageYears - OldAgeStart) / (OldAgeCertain - OldAgeStart);
        return 0.5f * progress * progress * progress;
    }

    /// <summary>Chaque heure : celui qui n'a rien mangé depuis deux jours meurt de faim.</summary>
    public static void Hourly(WorldState world, Colony colony)
    {
        foreach (Colonist colonist in colony.Members.ToList())
        {
            if (colonist.Needs.Food > 0.001f)
            {
                colonist.StarvedHours = 0;
                colonist.StarvationWarned = false;
                continue;
            }

            colonist.StarvedHours++;
            if (colonist.StarvedHours >= StarvationDeathHours)
                Die(world, colonist, "faim");
            else if (colonist.StarvedHours >= StarvationWarningHours && !colonist.StarvationWarned)
            {
                colonist.StarvationWarned = true;
                ColonyBrain.Say(colony, world.Clock, $"{colonist.Name} n'a rien mangé depuis un jour et s'affaiblit : sans nourriture, ce sera la fin.");
            }
        }
    }

    /// <summary>Chaque jour : de nouveaux couples, des naissances, des grossesses, et ceux que l'âge emporte.</summary>
    public static void Daily(WorldState world, Colony colony)
    {
        FormCouples(world, colony);
        GiveBirths(world, colony);
        Conceive(world, colony);
        foreach (Colonist colonist in colony.Members.ToList())
            if (world.Random.NextSingle() < OldAgeDeathChance(colonist.EquivalentAge))
                Die(world, colonist, "vieillesse");
    }

    /// <summary>
    /// Le sexe d'un nouveau-né ou d'un voyageur : un tirage au sort, légèrement penché en faveur du sexe le moins
    /// représenté dans la colonie. Sans cela, le hasard finit par laisser beaucoup de femmes sans conjoint (ou l'inverse),
    /// et comme on ne forme de couples qu'entre un homme et une femme, la colonie cesserait de grandir.
    /// </summary>
    public static Sex ChooseSex(Colony colony, Random random)
    {
        var everyone = colony.Members.Concat(colony.Transients).ToList();
        int females = everyone.Count(m => m.Sex == Sex.Female), males = everyone.Count - females;
        float tilt = 0.4f * (males - females) / Math.Max(6, everyone.Count);
        float chanceOfGirl = Math.Clamp(0.5f + tilt, 0.2f, 0.8f);
        return random.NextSingle() < chanceOfGirl ? Sex.Female : Sex.Male;
    }

    // --- Couples ---

    /// <summary>
    /// Un homme et une femme adultes, libres, qui s'entendent très bien (et ne sont pas de la même famille), se mettent en ménage.
    /// </summary>
    public static void FormCouples(WorldState world, Colony colony)
    {
        List<Colonist> singles = colony.Members
            .Where(m => m.Stage == LifeStage.Adult && m.Partner is null)
            .ToList();
        var candidates = new List<(float Affinity, Colonist Woman, Colonist Man)>();
        foreach (Colonist woman in singles.Where(m => m.Sex == Sex.Female))
        foreach (Colonist man in singles.Where(m => m.Sex == Sex.Male))
        {
            float affinity = Relations.Affinity(woman, man);
            if (affinity >= CoupleAffinity && !Colonist.AreKin(woman, man)
                && Personality.Compatibility(woman.Personality, man.Personality) >= CoupleCompatibility)
                candidates.Add((affinity, woman, man));
        }

        foreach ((float _, Colonist woman, Colonist man) in candidates.OrderByDescending(c => c.Affinity))
        {
            if (woman.Partner is not null || man.Partner is not null)
                continue;
            woman.Partner = man;
            man.Partner = woman;
            MoveTogether(colony, woman, man);
            ColonyBrain.Say(colony, world.Clock, $"{woman.Name} et {man.Name} forment un couple.");
        }
    }

    /// <summary>Un couple dort sous le même toit, si une hutte a de la place pour les deux.</summary>
    private static void MoveTogether(Colony colony, Colonist a, Colonist b)
    {
        if (a.Home is not null && a.Home == b.Home)
            return;
        if (a.Home is { } homeA && homeA.Residents.Count < Building.HutCapacity)
            MoveIn(b, homeA);
        else if (b.Home is { } homeB && homeB.Residents.Count < Building.HutCapacity)
            MoveIn(a, homeB);
        colony.FillVacancies();
    }

    private static void MoveIn(Colonist colonist, Building building)
    {
        colonist.Home?.Residents.Remove(colonist);
        colonist.Home = building;
        building.Residents.Add(colonist);
    }

    // --- Naissances ---

    /// <summary>Les femmes en âge d'avoir un enfant, en couple, peuvent tomber enceintes (plus ou moins selon la prospérité).</summary>
    private static void Conceive(WorldState world, Colony colony)
    {
        float prosperity = Prosperity(colony, world.Clock);
        long now = world.Clock.Ticks;
        long due = now + (long)(PregnancyDays * TimeConstants.TicksPerDay);

        foreach (Colonist woman in colony.Members.Where(m => m.Sex == Sex.Female))
        {
            if (!CanConceive(woman, due))
                continue;
            if (world.Random.NextSingle() >= ConceptionChancePerDay * prosperity * woman.Species.Fertility)
                continue;
            woman.PregnantUntilTicks = due;
            woman.PregnancyFather = woman.Partner;
            ColonyBrain.Say(colony, world.Clock, $"{woman.Name} attend un enfant de {woman.Partner!.Name}.");
        }
    }

    /// <summary>
    /// En couple, adulte et assez jeune, pas déjà enceinte, et au moins un an entre deux naissances.
    /// <paramref name="dueTicks"/> est le moment où l'enfant naîtrait.
    /// </summary>
    public static bool CanConceive(Colonist woman, long dueTicks) =>
        woman.Stage == LifeStage.Adult
        && woman.EquivalentAge <= FertileUntilAge
        && woman.Partner is { Stage: LifeStage.Adult }
        && woman.PregnantUntilTicks is null
        && dueTicks - woman.LastBirthTicks >= (long)(MinBirthIntervalYears * woman.Species.LifespanScale * TimeConstants.TicksPerYear);

    private static void GiveBirths(WorldState world, Colony colony)
    {
        long now = world.Clock.Ticks;
        foreach (Colonist mother in colony.Members.Where(m => m.PregnantUntilTicks is { } due && due <= now).ToList())
            GiveBirth(world, mother);
    }

    /// <summary>Un enfant naît : il hérite du talent et du caractère de ses parents, et porte le nom de son père.</summary>
    public static Colonist GiveBirth(WorldState world, Colonist mother)
    {
        Colony colony = mother.Colony;
        Colonist? father = mother.PregnancyFather;
        Sex sex = ChooseSex(colony, world.Random);
        string name = Names.Pick(sex, world.Random, colony.Members.Concat(colony.Transients).Select(m => m.Name));
        Skills fatherSkills = father?.Skills ?? mother.Skills;
        Personality fatherPersonality = father?.Personality ?? mother.Personality;

        var child = new Colonist(world.NextColonistId(), name, sex, colony,
            Skills.Inherit(world.Random, mother.Skills, fatherSkills), mother.X, mother.Y)
        {
            Mother = mother,
            Father = father,
            Surname = father?.Surname ?? mother.Surname,
            Species = mother.Species,
            BirthTicks = world.Clock.Ticks,
            Personality = Personality.Inherit(world.Random, mother.Personality, fatherPersonality),
        };
        child.Needs.Food = child.Needs.Rest = child.Needs.Leisure = child.Needs.Social = 0.8f;
        child.Needs.Comfort = 0.5f;
        child.Sector = WorkSector.Free;

        mother.PregnantUntilTicks = null;
        mother.PregnancyFather = null;
        mother.LastBirthTicks = world.Clock.Ticks;
        mother.Children.Add(child);
        father?.Children.Add(child);
        colony.Members.Add(child);

        // Le bébé dort dans la hutte de sa mère, s'il y a de la place.
        if (mother.Home is { } home && home.Residents.Count < Building.HutCapacity)
            MoveIn(child, home);
        colony.FillVacancies();
        colony.AssignSectors();

        string born = sex == Sex.Female ? "une fille" : "un fils";
        ColonyBrain.Say(colony, world.Clock, father is null
            ? $"{mother.Name} met au monde {born}, {name}."
            : $"{mother.Name} et {father.Name} ont {born} : {name} {child.Surname}.");
        return child;
    }

    // --- Mort ---

    /// <summary>
    /// Un colon meurt. La colonie le pleure (surtout ses proches), l'enterre, perd son savoir et libère sa place en hutte.
    /// </summary>
    public static void Die(WorldState world, Colonist colonist, string cause)
    {
        Colony colony = colonist.Colony;
        float age = colonist.AgeYears;
        ColonistAI.DetachFromColony(colonist);

        // Le deuil : le conjoint le plus durement, puis les enfants et les parents, les frères et sœurs, les amis.
        if (colonist.Partner is { } partner)
        {
            partner.Partner = null;
            partner.Needs.Grief = 1f;
        }
        foreach (Colonist other in colony.Members)
        {
            float grief = 0f;
            if (colonist.Children.Contains(other) || colonist.Mother == other || colonist.Father == other)
                grief = 0.7f;
            else if (Colonist.AreKin(colonist, other))
                grief = 0.4f;
            else if (colonist.Friends.Contains(other.Id))
                grief = 0.4f;
            other.Needs.Grief = Math.Max(other.Needs.Grief, grief);
        }
        colonist.PregnantUntilTicks = null;
        colonist.PregnancyFather = null;
        colonist.Partner = null;

        (int x, int y) = Urbanism.FindGraveSite(colony.Map, colony) ?? (-1, -1);
        colony.Graves.Add(new Grave(colonist.FullName, colonist.Sex, age, cause, world.Clock.Ticks, x, y));

        bool female = colonist.Sex == Sex.Female;
        ColonyBrain.Say(colony, world.Clock, cause == "faim"
            ? $"{colonist.Name} {colonist.Surname} est mort{(female ? "e" : "")} de faim, à {age:0} ans."
            : $"{colonist.Name} {colonist.Surname} s'éteint de vieillesse, à {age:0} ans.");
    }
}
