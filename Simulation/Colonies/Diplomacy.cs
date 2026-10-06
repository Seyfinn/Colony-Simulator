using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Un pacte entre deux colonies : une alliance, une guerre (déclarée par <see cref="Pact.A"/>) ou une trêve.</summary>
public enum PactKind { Alliance, War, Truce }

public sealed class Pact
{
    internal Pact(Colony a, Colony b, PactKind kind, long sinceTicks, long untilTicks = long.MaxValue)
    {
        A = a;
        B = b;
        Kind = kind;
        SinceTicks = sinceTicks;
        UntilTicks = untilTicks;
    }

    /// <summary>La colonie qui a proposé l'alliance ou déclaré la guerre.</summary>
    public Colony A { get; }
    public Colony B { get; }
    public PactKind Kind { get; }
    public long SinceTicks { get; }

    /// <summary>Fin prévue (pour une trêve) ; <see cref="long.MaxValue"/> sinon.</summary>
    public long UntilTicks { get; }

    public bool Involves(Colony colony) => A == colony || B == colony;
    public bool Between(Colony a, Colony b) => (A == a && B == b) || (A == b && B == a);
    public Colony Other(Colony colony) => colony == A ? B : A;
}

public enum WarPartyState { Outbound, Returning }

/// <summary>
/// Une bande de guerriers en marche vers une colonie ennemie : elle traverse la carte du monde comme une caravane,
/// livre bataille à l'arrivée, puis rentre avec ses blessés et son butin (ou sans les siens tombés au combat).
/// </summary>
public sealed class WarParty
{
    public int Id { get; internal set; }
    internal WarParty(Colony from, Colony to, List<Colonist> warriors, int weapons, long departTicks, long arriveTicks)
    {
        From = from;
        To = to;
        Warriors = warriors;
        Weapons = weapons;
        DepartTicks = departTicks;
        ArriveTicks = arriveTicks;
        ReturnTicks = arriveTicks + (arriveTicks - departTicks);
    }

    public Colony From { get; }
    public Colony To { get; }
    public List<Colonist> Warriors { get; }

    /// <summary>Outils de fer emportés comme armes (ils reviennent avec les survivants).</summary>
    public int Weapons { get; internal set; }

    public long DepartTicks { get; }
    public long ArriveTicks { get; }
    public long ReturnTicks { get; internal set; }
    public WarPartyState State { get; internal set; } = WarPartyState.Outbound;

    /// <summary>Vrai après une victoire, faux après une défaite, null avant la bataille (ou si la paix l'a annulée).</summary>
    public bool? Victory { get; internal set; }

    /// <summary>Le butin rapporté (pièces comprises).</summary>
    public Dictionary<ResourceType, int> Loot { get; } = [];

    /// <summary>Où se trouve la bande sur la route, de 0 (chez elle) à 1 (chez l'ennemi).</summary>
    public float RoutePosition(long nowTicks)
    {
        if (State == WarPartyState.Outbound)
            return Math.Clamp((nowTicks - DepartTicks) / (float)Math.Max(1, ArriveTicks - DepartTicks), 0f, 1f);
        long backStart = ReturnTicks - (ArriveTicks - DepartTicks);
        return Math.Clamp(1f - (nowTicks - backStart) / (float)Math.Max(1, ReturnTicks - backStart), 0f, 1f);
    }
}

/// <summary>
/// Les relations entre colonies. Chacune a une opinion des autres (de -100 à +100) qui tend chaque jour vers ce que justifient
/// leurs rapports : peuples qui s'apprécient ou se méprisent, échanges de caravanes, voisinage et querelles de frontière, rancunes
/// (un barrage, une attaque), richesse enviée, alliance ou guerre, racines communes après un schisme. Les colonies pacifiques
/// envoient des présents pour apaiser un voisin. Les décisions graves passent par une prière au joueur : sceller une alliance,
/// déclarer la guerre, faire la paix. La guerre se fait par bandes de guerriers qui marchent sur la carte du monde.
/// Le hasard de la politique a son propre générateur (<see cref="WorldState.Politics"/>).
/// </summary>
public static class Diplomacy
{
    /// <summary>Heure à laquelle les colonies pensent à leurs voisines (après le commerce).</summary>
    public const int PlanningHour = 10;

    public const float MinOpinion = -100f, MaxOpinion = 100f;

    /// <summary>Points d'opinion qui rejoignent chaque jour la valeur justifiée par les rapports des deux colonies.</summary>
    public const float OpinionDriftPerDay = 3f;

    public const float HostileOpinion = -50f;
    public const float WaryOpinion = -25f;
    public const float CordialOpinion = 25f;
    public const float FriendlyOpinion = 60f;

    /// <summary>Opinion qu'il faut avoir d'une voisine pour lui proposer une alliance (elle doit en penser presque autant).</summary>
    public const float AllianceOpinion = 50f;

    /// <summary>Une alliance se défait quand l'un des deux n'en pense plus que cela.</summary>
    public const float AllianceBreakOpinion = 15f;

    public const int MaxAlliances = 2;
    public const int TruceDays = 20;
    public const int MinPopulationForWar = 10;
    public const int MinHealthyAdultsForWar = 6;
    public const float MaxWarTravelDays = 8f;
    public const int DaysBetweenWarParties = 4;
    public const int MinWarriors = 2, MaxWarriors = 8;
    public const int LootPerWarrior = 10;

    /// <summary>Distance (en cases du monde) à laquelle deux colonies se disputent leurs terres.</summary>
    public const int BorderDistance = 5;
    public const float BorderQuarrelChancePerDay = 0.03f;
    public const float QuarrelGrudge = 0.6f;
    public const float AttackGrudge = 0.5f;

    private const float GiftChancePerDay = 0.06f;
    private const int GiftIntervalDays = 10;
    private const int MinCoinsForGift = 300;
    private const float GiftOpinion = 12f;
    private const float TradeOpinion = 3f;
    private const float AttackOpinion = 15f;

    private const int AllianceRefusalCooldownDays = 20;
    private const int WarRefusalCooldownDays = 20;
    private const int PeaceRefusalCooldownDays = 6;
    private const int MinWarDaysBeforePeace = 6;
    private const int WarDaysBeforeForcedPeace = 15;

    // ---------- Consultation ----------

    public static Pact? PactBetween(WorldState world, Colony a, Colony b) => world.Pacts.FirstOrDefault(p => p.Between(a, b));

    /// <summary>Alliés par un pacte, ou membres d'un même royaume (qui se traitent en alliés : pas de guerre entre eux, des renforts, le passage libre).</summary>
    public static bool AreAllied(WorldState world, Colony a, Colony b) =>
        PactBetween(world, a, b) is { Kind: PactKind.Alliance } || Realms.SameRealm(world, a, b);
    /// <summary>
    /// Le droit de passage d'un voyageur chez un hôte : toujours chez soi et chez ses alliés, jamais en guerre, et refusé à ceux dont l'hôte pense du mal
    /// (opinion au plus égale au seuil du monde). Le droit ne crée pas de marchandise ni ne permet d'installer un camp (propriété distincte).
    /// </summary>
    public static bool PassageAllowed(WorldState world, Colony traveler, Colony host) =>
        traveler == host || AreAllied(world, traveler, host)
        || !AtWar(world, traveler, host) && host.OpinionOf(traveler) > world.Territory.PassageRefusalOpinion;

    public static bool AtWar(WorldState world, Colony a, Colony b) => PactBetween(world, a, b) is { Kind: PactKind.War };

    /// <summary>Les alliés d'une colonie : ceux de ses pactes et les autres membres de son royaume.</summary>
    public static IEnumerable<Colony> Allies(WorldState world, Colony colony) =>
        PactAllies(world, colony).Concat(Realms.Of(world, colony) is { } realm ? Realms.Members(world, realm).Where(m => m != colony) : []).Distinct();

    /// <summary>Les alliés d'un pacte d'alliance seulement (les membres d'un royaume ne comptent pas dans la limite d'alliances).</summary>
    private static IEnumerable<Colony> PactAllies(WorldState world, Colony colony) =>
        world.Pacts.Where(p => p.Kind == PactKind.Alliance && p.Involves(colony)).Select(p => p.Other(colony));

    public static IEnumerable<Colony> Enemies(WorldState world, Colony colony) =>
        world.Pacts.Where(p => p.Kind == PactKind.War && p.Involves(colony)).Select(p => p.Other(colony));

    public static bool AtWarWithAnyone(WorldState world, Colony colony) => world.Pacts.Any(p => p.Kind == PactKind.War && p.Involves(colony));

    /// <summary>L'opinion en un mot.</summary>
    public static string Attitude(float opinion) => opinion switch
    {
        <= HostileOpinion => "Hostile",
        <= WaryOpinion => "Méfiante",
        < CordialOpinion => "Neutre",
        < FriendlyOpinion => "Cordiale",
        _ => "Amicale",
    };

    /// <summary>Ce que deux peuples pensent l'un de l'autre, d'instinct.</summary>
    public static float SpeciesAffinity(Species a, Species b)
    {
        if (a == b)
            return 15f;
        static int Rank(Species s) => s == Species.Human ? 0 : s == Species.Dwarf ? 1 : s == Species.Elf ? 2 : 3;
        return (Math.Min(Rank(a), Rank(b)), Math.Max(Rank(a), Rank(b))) switch
        {
            (0, 1) => 5f,    // humains et nains
            (0, 2) => 5f,    // humains et elfes
            (0, 3) => -10f,  // humains et orques
            (1, 2) => -15f,  // nains et elfes
            (1, 3) => -20f,  // nains et orques
            _ => -25f,       // elfes et orques
        };
    }

    /// <summary>Le tempérament moyen des adultes : positif chez un peuple belliqueux, négatif chez un peuple pacifique.</summary>
    public static float Temperament(Colony colony) => AverageAxis(colony, Axis.Temperament);

    private static float AverageAxis(Colony colony, Axis axis)
    {
        float sum = 0f;
        int count = 0;
        foreach (Colonist member in colony.PresentMembers)
            if (member.Stage is LifeStage.Adult or LifeStage.Elder)
            {
                sum += member.Personality[axis];
                count++;
            }
        return count == 0 ? 0f : sum / count;
    }

    /// <summary>
    /// Ce qui fait l'opinion de <paramref name="a"/> envers <paramref name="b"/>, raison par raison (pour l'expliquer au joueur) ;
    /// leur somme est la valeur vers laquelle l'opinion tend.
    /// </summary>
    public static List<(string Reason, float Value)> OpinionFactors(WorldState world, Colony a, Colony b)
    {
        var factors = new List<(string, float)>();
        void Add(string reason, float value)
        {
            if (MathF.Abs(value) >= 0.5f)
                factors.Add((reason, value));
        }

        float temperament = Temperament(a);
        Add(a.Species == b.Species ? "Même peuple" : $"{a.Species.Plural} et {b.Species.Plural}", SpeciesAffinity(a.Species, b.Species));
        Add(temperament >= 0.15f ? "Notre tempérament belliqueux" : temperament <= -0.15f ? "Notre tempérament pacifique" : "Notre tempérament",
            -15f * temperament);

        long since = world.Clock.Ticks - 2L * TimeConstants.TicksPerYear;
        // Chaque colonie garde la trace des caravanes envoyées comme reçues.
        int trades = a.Trades.Count(t => t.Partner == b.Name && t.Ticks >= since);
        Add("Nos échanges de caravanes", Math.Min(25f, 5f * trades));
        Add("Nos rancunes (barrage, querelles, attaques)", -25f * a.GrudgeAgainst(b));

        int distance = world.WorldMap.Grid.Distance(world.WorldMap.TileOf(a), world.WorldMap.TileOf(b));
        if (distance <= BorderDistance)
            Add("Nos terres se touchent", -(BorderDistance + 1 - distance) * 5f * (1f + 0.5f * temperament));

        int ours = a.Stock.Get(ResourceType.Coins), theirs = b.Stock.Get(ResourceType.Coins);
        if (temperament > 0.2f && theirs >= 2 * ours + 200)
            Add("Leur richesse fait des envieux", -10f);
        // Un peuple belliqueux méprise le voisin faible qu'il pourrait soumettre.
        if (temperament > 0.2f && Warfare.DefenseEstimate(world, a, null) >= 1.5f * Math.Max(1f, Warfare.DefenseEstimate(world, b, a)))
            Add("Leur faiblesse nous tente", -15f);

        switch (PactBetween(world, a, b)?.Kind)
        {
            case PactKind.Alliance: Add("Notre alliance", 25f); break;
            case PactKind.War: Add("La guerre", -40f); break;
            case PactKind.Truce: Add("La trêve, encore fraîche", -10f); break;
        }
        if (Knowledge.Has(a, Discovery.Diplomacy))
            Add("Notre diplomatie", 8f);
        if (a.Parent == b || b.Parent == a)
            Add("Nos racines communes", 25f);
        return factors;
    }

    public static float TargetOpinion(WorldState world, Colony a, Colony b) =>
        Math.Clamp(OpinionFactors(world, a, b).Sum(f => f.Value), MinOpinion, MaxOpinion);

    private static void Shift(Colony a, Colony b, float amount) =>
        a.Opinions[b] = Math.Clamp(a.OpinionOf(b) + amount, MinOpinion, MaxOpinion);

    private static void AddGrudge(Colony a, Colony b, float amount) =>
        a.Grudges[b] = Math.Min(Hydrology.MaxGrudge, a.GrudgeAgainst(b) + amount);

    // ---------- Chaque jour ----------

    /// <summary>Chaque matin : les opinions évoluent, les colonies se querellent, offrent des présents, prient pour la paix ou la guerre.</summary>
    public static void Daily(WorldState world)
    {
        CleanUp(world);
        List<Colony> living = world.Colonies.Where(c => c.PresentMembers.Count > 0).ToList();
        if (living.Count < 2)
            return;

        foreach (Colony a in living)
        foreach (Colony b in living)
        {
            if (a == b)
                continue;
            float target = TargetOpinion(world, a, b);
            float opinion = a.OpinionOf(b);
            a.Opinions[b] = opinion + Math.Clamp(target - opinion, -OpinionDriftPerDay, OpinionDriftPerDay);
        }

        foreach (Colony colony in living)
            colony.WarWeariness = AtWarWithAnyone(world, colony)
                ? colony.WarWeariness + 1f
                : Math.Max(0f, colony.WarWeariness - 2f);

        BreakAlliances(world);
        BorderQuarrels(world, living);
        foreach (Colony colony in living)
        {
            SendGifts(world, colony, living);
            ProposeAlliance(world, colony, living);
            ConsiderWar(world, colony, living);
            ConsiderPeace(world, colony);
            Warfare.LaunchParty(world, colony);
        }
    }

    /// <summary>Les trêves expirent ; une colonie éteinte ne fait plus ni guerre ni alliance.</summary>
    private static void CleanUp(WorldState world)
    {
        long now = world.Clock.Ticks;
        foreach (Pact pact in world.Pacts.ToList())
        {
            bool extinct = pact.A.PresentMembers.Count == 0 || pact.B.PresentMembers.Count == 0;
            if (extinct || now >= pact.UntilTicks)
            {
                world.Pacts.Remove(pact);
                if (!extinct && pact.Kind == PactKind.Truce)
                {
                    ColonyBrain.Say(pact.A, world.Clock, $"La trêve avec {pact.B.Name} prend fin.");
                    ColonyBrain.Say(pact.B, world.Clock, $"La trêve avec {pact.A.Name} prend fin.");
                }
            }
        }
    }

    private static void BreakAlliances(WorldState world)
    {
        foreach (Pact pact in world.Pacts.Where(p => p.Kind == PactKind.Alliance).ToList())
        {
            if (pact.A.OpinionOf(pact.B) >= AllianceBreakOpinion && pact.B.OpinionOf(pact.A) >= AllianceBreakOpinion)
                continue;
            world.Pacts.Remove(pact);
            ColonyBrain.Say(pact.A, world.Clock, $"Notre alliance avec {pact.B.Name} est rompue : la confiance n'y est plus.");
            ColonyBrain.Say(pact.B, world.Clock, $"Notre alliance avec {pact.A.Name} est rompue : la confiance n'y est plus.");
        }
    }

    /// <summary>Entre voisines, des bergers ou des bûcherons se disputent les terres : une rancune naît, surtout chez les belliqueux.</summary>
    private static void BorderQuarrels(WorldState world, List<Colony> living)
    {
        for (int i = 0; i < living.Count; i++)
        for (int j = i + 1; j < living.Count; j++)
        {
            Colony a = living[i], b = living[j];
            int distance = world.WorldMap.Grid.Distance(world.WorldMap.TileOf(a), world.WorldMap.TileOf(b));
            if (distance > BorderDistance || AreAllied(world, a, b))
                continue;
            float chance = BorderQuarrelChancePerDay * (1f + Math.Max(0f, Temperament(a) + Temperament(b)))
                * (BorderDistance + 1 - distance) / 2f;
            if (world.Politics.NextSingle() >= chance)
                continue;
            (Colony wronged, Colony other) = world.Politics.Next(2) == 0 ? (a, b) : (b, a);
            AddGrudge(wronged, other, QuarrelGrudge);
            string what = world.Politics.Next(3) switch
            {
                0 => "ont mené leurs bêtes sur nos pâturages",
                1 => "ont abattu des arbres sur nos terres",
                _ => "ont déplacé les bornes de nos champs",
            };
            ColonyBrain.Say(wronged, world.Clock, $"Querelle de frontière : des gens de {other.Name} {what}. La colère monte.");
            ColonyBrain.Say(other, world.Clock, $"Querelle de frontière avec {wronged.Name} : ils nous accusent d'empiéter sur leurs terres.");
        }
    }

    /// <summary>Une colonie pacifique (ou diplomate) qui a de quoi offrir envoie un présent à une voisine qui la boude (sans la haïr).</summary>
    private static void SendGifts(WorldState world, Colony colony, List<Colony> living)
    {
        if (Temperament(colony) >= 0f && !Knowledge.Has(colony, Discovery.Diplomacy))
            return;
        int coins = colony.Stock.Get(ResourceType.Coins);
        if (coins < MinCoinsForGift)
            return;
        long now = world.Clock.Ticks;
        foreach (Colony other in living)
        {
            if (other == colony || AtWar(world, colony, other) || !world.WorldMap.Connected(colony, other)
                || other.OpinionOf(colony) >= 0f || other.OpinionOf(colony) < HostileOpinion + 10f
                || now - colony.LastGiftTicks.GetValueOrDefault(other, long.MinValue / 2) < GiftIntervalDays * TimeConstants.TicksPerDay)
                continue;
            if (world.Politics.NextSingle() >= GiftChancePerDay)
                continue;
            int gift = Math.Min(60, coins / 8);
            if (!colony.Stock.TryTransferTo(other.Stock, ResourceType.Coins, gift))
                continue;
            colony.LastGiftTicks[other] = now;
            Shift(other, colony, GiftOpinion);
            ColonyBrain.Say(colony, world.Clock, $"Nous envoyons un présent de {gift} pièces à {other.Name}, pour apaiser nos rapports.");
            ColonyBrain.Say(other, world.Clock, $"{colony.Name} nous offre un présent de {gift} pièces : un geste apprécié.");
            return;
        }
    }

    // ---------- Alliances ----------

    private static void ProposeAlliance(WorldState world, Colony colony, List<Colony> living)
    {
        if (colony.PresentMembers.Count < 6 || PactAllies(world, colony).Count() >= MaxAlliances || colony.Prayers.IsQuiet(DecisionKind.Alliance, world.Clock))
            return;
        // Un chef sociable et curieux s'allie plus volontiers : le seuil d'opinion baisse jusqu'à dix points (voir Leadership).
        float threshold = AllianceOpinion - 10f * Leadership.Stance(colony).Commerce;
        Colony? partner = living
            .Where(o => o != colony && o.PresentMembers.Count >= 6 && PactBetween(world, colony, o) is null && !Realms.SameRealm(world, colony, o)
                && colony.OpinionOf(o) >= threshold && o.OpinionOf(colony) >= AllianceOpinion - 10f
                && (Knowledge.Has(colony, Discovery.Diplomacy) || Knowledge.Has(o, Discovery.Diplomacy))
                && PactAllies(world, o).Count() < MaxAlliances && world.WorldMap.Connected(colony, o))
            .OrderByDescending(o => colony.OpinionOf(o)).FirstOrDefault();
        if (partner is null)
            return;
        colony.Prayers.Ask(DecisionKind.Alliance, partner.Name, $"Sceller une alliance avec {partner.Name} ?",
            $"Nous estimons les {partner.Species.Plural.ToLowerInvariant()} de {partner.Name} et ils nous le rendent bien. Alliés, nos caravanes "
            + "se contenteraient de moindres gains, nos savoirs circuleraient deux fois plus vite et chacun viendrait défendre l'autre.",
            () => SealAlliance(world, colony, partner), world.Clock, AllianceRefusalCooldownDays);
    }

    public static void SealAlliance(WorldState world, Colony a, Colony b)
    {
        if (PactBetween(world, a, b) is not null || Realms.SameRealm(world, a, b) || a.PresentMembers.Count == 0 || b.PresentMembers.Count == 0)
            return;
        world.Pacts.Add(new Pact(a, b, PactKind.Alliance, world.Clock.Ticks));
        ColonyBrain.Say(a, world.Clock, $"Nous scellons une alliance avec {b.Name} !");
        ColonyBrain.Say(b, world.Clock, $"{a.Name} nous propose son alliance : nous l'acceptons avec joie.");
    }

    // ---------- Guerre et paix ----------

    /// <summary>
    /// Une colonie hostile, sans autre guerre en cours et qui se croit au moins aussi forte (les prudentes veulent nettement plus),
    /// prie pour avoir le droit d'attaquer.
    /// </summary>
    private static void ConsiderWar(WorldState world, Colony colony, List<Colony> living)
    {
        if (colony.PresentMembers.Count < MinPopulationForWar || AtWarWithAnyone(world, colony)
            || !(colony.Sensors?.SurvivalAssured ?? false) || Warfare.HealthyAdults(colony) < MinHealthyAdultsForWar
            || colony.Prayers.IsQuiet(DecisionKind.War, world.Clock))
            return;
        float caution = 1f - 0.3f * AverageAxis(colony, Axis.Audace);
        float power = Warfare.DefenseEstimate(world, colony, attacker: null);
        // Un chef belliqueux se fâche plus vite : le seuil d'hostilité monte jusqu'à dix points (voir Leadership). La décision, elle, reste au joueur (D1).
        float hostile = HostileOpinion + 10f * Leadership.Stance(colony).Bellicisme;
        Colony? enemy = living
            .Where(o => o != colony && colony.OpinionOf(o) <= hostile && PactBetween(world, colony, o) is null && !Realms.SameRealm(world, colony, o)
                && world.WorldMap.Connected(colony, o) && world.WorldMap.TravelDays(colony, o) <= MaxWarTravelDays
                && power >= caution * Warfare.DefenseEstimate(world, o, colony))
            .OrderBy(o => colony.OpinionOf(o)).FirstOrDefault();
        if (enemy is null)
            return;
        var reasons = OpinionFactors(world, colony, enemy).Where(f => f.Value < 0).OrderBy(f => f.Value).Take(3).Select(f => f.Reason.ToLowerInvariant());
        colony.Prayers.Ask(DecisionKind.War, enemy.Name, $"Déclarer la guerre à {enemy.Name} ?",
            $"Nous ne supportons plus {enemy.Name} ({string.Join(", ", reasons)}). Nos guerriers iraient piller leurs réserves ; "
            + "certains ne reviendraient pas, et la guerre ne cesserait qu'à la paix.",
            () => DeclareWar(world, colony, enemy), world.Clock, WarRefusalCooldownDays);
    }

    public static void DeclareWar(WorldState world, Colony aggressor, Colony target)
    {
        if (PactBetween(world, aggressor, target) is not null || Realms.SameRealm(world, aggressor, target) || aggressor.PresentMembers.Count == 0 || target.PresentMembers.Count == 0)
            return;
        world.Pacts.Add(new Pact(aggressor, target, PactKind.War, world.Clock.Ticks));
        aggressor.BattlesWon = aggressor.BattlesLost = target.BattlesWon = target.BattlesLost = 0;
        ColonyBrain.Say(aggressor, world.Clock, $"⚔ Nous déclarons la guerre à {target.Name} !");
        ColonyBrain.Say(target, world.Clock, $"⚔ {aggressor.Name} nous déclare la guerre ! Il faudra nous défendre.");
        foreach (Colony ally in Allies(world, target).ToList())
        {
            Shift(ally, aggressor, -20f);
            ColonyBrain.Say(ally, world.Clock, $"{aggressor.Name} attaque nos alliés de {target.Name} : nous enverrons des renforts s'il le faut.");
        }
    }

    /// <summary>Une colonie lasse ou battue prie pour la paix.</summary>
    private static void ConsiderPeace(WorldState world, Colony colony)
    {
        foreach (Pact war in world.Pacts.Where(p => p.Kind == PactKind.War && p.Involves(colony)).ToList())
        {
            if (world.Clock.Ticks - war.SinceTicks < MinWarDaysBeforePeace * TimeConstants.TicksPerDay
                || colony.Prayers.IsQuiet(DecisionKind.Peace, world.Clock))
                continue;
            bool weary = colony.WarWeariness >= 25f + 25f * Temperament(colony);
            bool beaten = colony.BattlesLost >= colony.BattlesWon + 2;
            if (!weary && !beaten)
                continue;
            Colony enemy = war.Other(colony);
            colony.Prayers.Ask(DecisionKind.Peace, enemy.Name, $"Faire la paix avec {enemy.Name} ?",
                beaten
                    ? $"Nous avons perdu {colony.BattlesLost} batailles contre {enemy.Name}. Il est temps de demander la paix avant d'enterrer d'autres des nôtres."
                    : $"La guerre contre {enemy.Name} nous épuise. Nous voudrions proposer la paix et une trêve de {TruceDays} jours.",
                () => OfferPeace(world, colony, enemy), world.Clock, PeaceRefusalCooldownDays);
        }
    }

    /// <summary>
    /// La colonie propose la paix : l'ennemi l'accepte s'il est las lui aussi, s'il ne gagne pas, ou si la guerre traîne depuis longtemps.
    /// La paix ouvre une trêve pendant laquelle personne ne reprend les armes.
    /// </summary>
    internal static void OfferPeace(WorldState world, Colony colony, Colony enemy)
    {
        if (PactBetween(world, colony, enemy) is not { Kind: PactKind.War } war)
            return;
        bool accepted = enemy.WarWeariness >= 10f || enemy.BattlesWon <= enemy.BattlesLost
            || world.Clock.Ticks - war.SinceTicks >= WarDaysBeforeForcedPeace * TimeConstants.TicksPerDay;
        if (!accepted)
        {
            ColonyBrain.Say(colony, world.Clock, $"{enemy.Name} rejette notre offre de paix : la guerre continue.");
            ColonyBrain.Say(enemy, world.Clock, $"{colony.Name} nous demande la paix, mais nous avons l'avantage : nous refusons.");
            return;
        }
        MakePeace(world, war);
    }

    internal static void MakePeace(WorldState world, Pact war)
    {
        world.Pacts.Remove(war);
        bool diplomats = Knowledge.Has(war.A, Discovery.Diplomacy) || Knowledge.Has(war.B, Discovery.Diplomacy);
        long days = diplomats ? TruceDays * 3 / 2 : TruceDays;
        world.Pacts.Add(new Pact(war.A, war.B, PactKind.Truce, world.Clock.Ticks, world.Clock.Ticks + days * TimeConstants.TicksPerDay));
        foreach (Colony side in new[] { war.A, war.B })
        {
            Colony other = war.Other(side);
            side.Opinions[other] = Math.Max(side.OpinionOf(other), -30f);
            side.BattlesWon = side.BattlesLost = 0;
            ColonyBrain.Say(side, world.Clock, $"☮ La paix est signée avec {other.Name} : une trêve de {days} jours commence.");
        }
    }

    // ---------- Échos des autres systèmes ----------

    /// <summary>Une caravane a conclu un échange : les deux colonies s'en apprécient un peu plus.</summary>
    internal static void OnTrade(Colony from, Colony to)
    {
        Shift(from, to, TradeOpinion);
        Shift(to, from, TradeOpinion);
    }

    /// <summary>Une colonie vient d'être attaquée : elle en garde rancune, et ses alliés aussi.</summary>
    internal static void OnAttacked(WorldState world, Colony victim, Colony attacker)
    {
        Shift(victim, attacker, -AttackOpinion);
        AddGrudge(victim, attacker, AttackGrudge);
        foreach (Colony ally in Allies(world, victim))
            if (ally != attacker)
                Shift(ally, attacker, -AttackOpinion / 2f);
    }

    /// <summary>Le renfort qu'envoient les alliés d'une colonie attaquée (ceux qui sont à moins de six jours de marche).</summary>
    public static float AlliedHelp(WorldState world, Colony defender, Colony? attacker)
    {
        float help = 0f;
        foreach (Colony ally in Allies(world, defender))
            if (ally != attacker && world.WorldMap.Connected(ally, defender) && world.WorldMap.TravelDays(ally, defender) <= 6f)
                help += 0.15f * Warfare.HealthyAdults(ally);
        return help;
    }
}
