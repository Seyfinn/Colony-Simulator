using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// L'inclinaison du chef d'une colonie, tirée de ses axes de personnalité : chacune va de -1 à +1. Elle ne fait que moduler (de 20 % au plus) les décisions habituelles ;
/// il n'y a ni lois ni taxes.
/// </summary>
/// <param name="Prudence">Attachement moins audace : veut de plus gros stocks de vivres.</param>
/// <param name="Bellicisme">Audace plus tempérament : se fâche plus vite avec un voisin hostile.</param>
/// <param name="Commerce">Sociabilité plus curiosité : accepte des échanges moins rentables, s'allie plus volontiers.</param>
/// <param name="Batisseur">Ardeur plus ambition : fonde ses camps plus souvent.</param>
public readonly record struct ChiefStance(float Prudence, float Bellicisme, float Commerce, float Batisseur)
{
    public static readonly ChiefStance Neutral = new(0f, 0f, 0f, 0f);
}

/// <summary>
/// Le chef de chaque colonie : élu par tous ses adultes, réélu tous les deux ans, à sa mort ou à son départ, ou après une crise grave. Le renom (grandes chasses, victoires,
/// ouvrages d'offrande, maîtrise d'un métier, âge adulte) pèse dans le vote, avec l'amitié, la rivalité, la parenté et l'entente des caractères. Le chef n'impose rien :
/// sa personnalité infléchit seulement les seuils des décisions habituelles. Les décisions graves (alliance, guerre, paix) restent des prières au joueur.
/// Tout le hasard de l'élection vient de <see cref="WorldState.Politics"/>.
/// </summary>
public static class Leadership
{
    /// <summary>Un mandat dure deux ans.</summary>
    public const long TermTicks = 2L * TimeConstants.TicksPerYear;

    /// <summary>Candidats en lice : les trois adultes au plus fort renom (le chef sortant s'y ajoute s'il est éligible).</summary>
    public const int Candidates = 3;

    /// <summary>Jours consécutifs d'humeur très basse qui déclenchent une élection de crise (au plus une par an).</summary>
    public const int LowMoodDaysForCrisis = 3;
    public const float LowMood = 0.3f;

    private const float RenownLossPerDay = 0.01f;
    private const float RenownPerAdultDay = 0.15f;
    private const float RenownPerMasteryDay = 0.1f;
    private const float MasteryLevel = 15f;

    /// <summary>Un chef modère les décisions de 20 % au plus : une inclinaison de ±1 vaut ±15 % (±10 points d'opinion).</summary>
    public const float StanceEffect = 0.15f;

    public static Colonist? ChiefOf(Colony colony) =>
        colony.ChiefId == 0 ? null : colony.Members.FirstOrDefault(m => m.Id == colony.ChiefId);

    /// <summary>L'inclinaison du chef (lecture pure, jamais sauvegardée) ; neutre sans chef.</summary>
    public static ChiefStance Stance(Colony colony)
    {
        if (ChiefOf(colony) is not { } chief)
            return ChiefStance.Neutral;
        Personality p = chief.Personality;
        return new ChiefStance((p[Axis.Attachement] - p[Axis.Audace]) / 2f, (p[Axis.Audace] + p[Axis.Temperament]) / 2f,
            (p[Axis.Sociabilite] + p[Axis.Curiosite]) / 2f, (p[Axis.Ardeur] + p[Axis.Ambition]) / 2f);
    }

    // ---------- Chaque matin ----------

    /// <summary>
    /// Appelé chaque jour à l'heure de la diplomatie, avant elle : le renom évolue, puis une élection a lieu si le chef manque (mort, parti), si son mandat est échu
    /// ou si la colonie traverse une crise (humeur très basse trois jours de suite, famine, défaite de guerre récente), au plus une élection de crise par an.
    /// </summary>
    public static void Daily(WorldState world, Colony colony)
    {
        if (colony.Members.Count == 0)
            return;
        UpdateRenown(colony);
        colony.LowMoodDays = colony.AverageMood < LowMood ? colony.LowMoodDays + 1 : 0;
        long now = world.Clock.Ticks;

        bool missing = ChiefOf(colony) is not { } chief || chief.Stage == LifeStage.Child;
        bool expired = now >= colony.NextElectionTicks;
        bool crisis = colony.LowMoodDays >= LowMoodDaysForCrisis
            || colony.Sensors is { FoodDays: < 0.5f }
            || colony.LastDefeatTicks is { } defeat && now - defeat < 3 * TimeConstants.TicksPerDay;
        bool crisisAllowed = crisis && now - (colony.LastCrisisElectionTicks ?? long.MinValue / 2) >= TimeConstants.TicksPerYear && colony.ElectionCount > 0;
        if (!missing && !expired && !crisisAllowed)
            return;
        if (crisisAllowed && !missing && !expired)
            colony.LastCrisisElectionTicks = now;
        Elect(world, colony);
    }

    /// <summary>Le renom perd 1 % par jour ; l'âge adulte et la maîtrise d'un métier (niveau 15 et plus) en donnent un peu.</summary>
    private static void UpdateRenown(Colony colony)
    {
        foreach (Colonist colonist in colony.Members)
        {
            float gain = colonist.Stage is LifeStage.Adult or LifeStage.Elder ? RenownPerAdultDay : 0f;
            gain += RenownPerMasteryDay * Skills.All.Count(skill => colonist.Skills.Level(skill) >= MasteryLevel);
            colonist.Renown = (colonist.Renown + gain) * (1f - RenownLossPerDay);
        }
    }

    // ---------- L'élection ----------

    /// <summary>
    /// Tous les adultes votent (les anciens aussi). Chaque électeur choisit, parmi les candidats, celui qui lui plaît le plus : renom, plus 10 points pour un ami, moins 15
    /// pour un rival, plus huit fois l'entente des caractères, plus cinq pour un parent, plus un peu de bruit. À égalité, le renom puis l'identifiant tranchent.
    /// </summary>
    public static Colonist? Elect(WorldState world, Colony colony)
    {
        long now = world.Clock.Ticks;
        List<Colonist> electors = colony.Members.Where(m => m.Stage is LifeStage.Adult or LifeStage.Elder).OrderBy(m => m.Id).ToList();
        if (electors.Count == 0)
        {
            colony.ChiefId = 0;
            colony.NextElectionTicks = now + TermTicks / 4;
            return null;
        }
        Colonist? outgoing = ChiefOf(colony);
        List<Colonist> candidates = electors.OrderByDescending(m => m.Renown).ThenByDescending(m => m.AgeYears).ThenBy(m => m.Id).Take(Candidates).ToList();
        if (outgoing is not null && electors.Contains(outgoing) && !candidates.Contains(outgoing))
            candidates.Add(outgoing);
        candidates = candidates.OrderBy(c => c.Id).ToList();

        var votes = candidates.ToDictionary(c => c, _ => 0);
        foreach (Colonist voter in electors)
        {
            Colonist? choice = null;
            float best = float.MinValue;
            foreach (Colonist candidate in candidates)
            {
                float score = candidate.Renown + (voter.Friends.Contains(candidate.Id) ? 10f : 0f) - (voter.Rivals.Contains(candidate.Id) ? 15f : 0f)
                    + 8f * Personality.Compatibility(voter.Personality, candidate.Personality) + (Colonist.AreKin(voter, candidate) ? 5f : 0f)
                    + (world.Politics.NextSingle() * 6f - 3f);
                if (score > best) { best = score; choice = candidate; }
            }
            votes[choice!]++;
        }
        Colonist winner = candidates.OrderByDescending(c => votes[c]).ThenByDescending(c => c.Renown).ThenBy(c => c.Id).First();
        bool changed = colony.ChiefId != winner.Id;
        colony.ChiefId = winner.Id;
        if (changed || colony.ElectionCount == 0)
            colony.ChiefSinceTicks = now;
        colony.NextElectionTicks = now + TermTicks;
        colony.ElectionCount++;
        ColonyBrain.Say(colony, world.Clock, changed || colony.ElectionCount == 1
            ? $"{winner.Name} est élu{(winner.Sex == Sex.Female ? "e" : "")} chef avec {votes[winner]} voix sur {electors.Count}."
            : $"{winner.Name} est réélu{(winner.Sex == Sex.Female ? "e" : "")} chef avec {votes[winner]} voix sur {electors.Count}.");
        return winner;
    }

    // ---------- Lecture ----------

    /// <summary>La fiche du chef en quelques mots (nom, âge, traits notables, renom, depuis quand), pour l'infobulle d'une colonie.</summary>
    public static string Describe(Colony colony, GameClock clock)
    {
        if (ChiefOf(colony) is not { } chief)
            return "pas de chef";
        string traits = string.Join(", ", chief.Personality.NotableTraits(chief.Sex));
        float years = (clock.Ticks - colony.ChiefSinceTicks) / (float)TimeConstants.TicksPerYear;
        return $"{chief.FullName}, {chief.AgeYears:0} ans{(traits.Length > 0 ? $" ({traits})" : "")}, renom {chief.Renown:0}, chef depuis {years:0.#} an{(years >= 2 ? "s" : "")}";
    }
}
