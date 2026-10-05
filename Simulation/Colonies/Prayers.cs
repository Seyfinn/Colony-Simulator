using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les grandes décisions que la colonie ne prend pas seule : elle les soumet au joueur-dieu dans une prière.
/// Un barrage, une alliance, une guerre, la paix, un schisme (voir <see cref="Diplomacy"/> et <see cref="Schism"/>).
/// </summary>
public enum DecisionKind { Dam, Alliance, War, Peace, Schism, Wish }

/// <summary>Une prière retirée par la colonie (par exemple un souhait dont la cible a disparu) n'a reçu aucune réponse du joueur.</summary>
public enum PrayerStatus { Pending, Approved, Refused, Withdrawn }

/// <summary>Une demande de la colonie au joueur : une question, la raison de la demande, et ce qui se passe si on accepte.</summary>
public sealed class Prayer
{
    internal Prayer(int id, Colony colony, DecisionKind kind, string subject, string question, string reason, long askedTicks, Action apply)
    {
        Id = id;
        Colony = colony;
        SettlementId = colony.LocalSettlement.Id;
        Kind = kind;
        Subject = subject;
        Question = question;
        Reason = reason;
        AskedTicks = askedTicks;
        Settlement place = colony.LocalSettlement;
        Apply = () => { using var scope = colony.UseSettlement(place); apply(); };
    }

    public int Id { get; }
    public Colony Colony { get; }
    public int SettlementId { get; internal set; }
    public DecisionKind Kind { get; }

    /// <summary>Ce dont il s'agit (un emplacement, par exemple) : on ne repose pas deux fois la même question en attente.</summary>
    public string Subject { get; }

    /// <summary>« Construire un barrage sur la rivière ? »</summary>
    public string Question { get; }

    /// <summary>Pourquoi la colonie le demande, en langage clair.</summary>
    public string Reason { get; }

    public long AskedTicks { get; }

    /// <summary>Pour un souhait divin : son identifiant stable (0 pour une décision administrative).</summary>
    public int WishId { get; internal set; }
    public long? AnsweredTicks { get; internal set; }
    public PrayerStatus Status { get; internal set; }

    /// <summary>Vrai si la décision a été accordée d'office, sans que le joueur ait été sollicité.</summary>
    public bool AutoApproved { get; internal set; }

    internal Action Apply { get; private set; }

    /// <summary>Reconstruit la décision depuis son sujet ; les fonctions ne sont pas enregistrées dans le fichier.</summary>
    internal void RestoreAction(WorldState world)
    {
        Apply = () => { };
        if (Kind == DecisionKind.Schism && Subject.Split(':') is ["village", var village, var chief] && int.TryParse(village, out int villageId) && int.TryParse(chief, out int chiefId))
            Apply = () => Schism.Secede(world, Colony, villageId, chiefId);
        else if (Kind == DecisionKind.Dam && Subject.Split(',') is [var sx, var sy]
            && int.TryParse(sx, out int x) && int.TryParse(sy, out int y))
            Apply = () => ColonyBrain.ApplyDamDecision(Colony, Colony.Map, Colony.Clock, x, y);
        else if (Kind == DecisionKind.Schism && int.TryParse(Subject, out int leader))
            Apply = () => Schism.Split(world, Colony, leader);
        else if (world.Colonies.FirstOrDefault(c => c.Name == Subject) is { } other)
            Apply = Kind switch
            {
                DecisionKind.Alliance => () => Diplomacy.SealAlliance(world, Colony, other),
                DecisionKind.War => () => Diplomacy.DeclareWar(world, Colony, other),
                DecisionKind.Peace => () => Diplomacy.OfferPeace(world, Colony, other),
                _ => Apply,
            };
        if (SettlementId == 0) SettlementId = Colony.PrimarySettlementId;
        Action decision = Apply;
        Apply = () => { using var scope = Colony.UseSettlement(world.SettlementById(SettlementId) ?? Colony.PrimarySettlement); decision(); };
    }

    internal int CooldownDays { get; init; } = PrayerBook.RefusalCooldownDays;
}

/// <summary>
/// Les prières d'une colonie. Une décision en attente ne bloque rien d'autre : la colonie continue de vivre
/// et attend. Refuser fait vaciller la foi (surtout des pieux) et la colonie n'insiste pas avant quelques jours ;
/// accorder la renforce un peu. Le joueur peut décider d'accorder d'office un type de décision.
/// </summary>
public sealed class PrayerBook(Colony colony)
{
    /// <summary>Jours pendant lesquels la colonie ne repose pas une question refusée.</summary>
    public const int RefusalCooldownDays = 5;

    /// <summary>Foi perdue par un colon moyen quand un refus lui parvient (plus pour un pieux).</summary>
    public const float RefusalFaithLoss = 0.12f;

    /// <summary>Foi gagnée quand une prière est exaucée.</summary>
    public const float ApprovalFaithGain = 0.03f;

    private int _nextId = 1;
    private readonly List<Prayer> _prayers = [];
    private readonly Dictionary<(DecisionKind Kind, string Subject), long> _blockedUntilTicks = [];
    private readonly Dictionary<DecisionKind, long> _quietUntilTicks = [];

    /// <summary>Toutes les prières, des plus anciennes aux plus récentes.</summary>
    public IReadOnlyList<Prayer> All => _prayers;

    public IEnumerable<Prayer> Pending => _prayers.Where(p => p.Status == PrayerStatus.Pending);

    /// <summary>Les types de décision que le joueur accorde d'office.</summary>
    public HashSet<DecisionKind> AutoApprove { get; } = [];

    /// <summary>Déclenché quand une prière attend une réponse (pour prévenir le joueur).</summary>
    public event Action<Prayer>? Asked;

    /// <summary>
    /// La colonie soumet une décision. Renvoie la prière, ou null si la même est déjà en attente
    /// ou a été refusée récemment. Si le joueur accorde ce type d'office, la décision est exécutée tout de suite.
    /// </summary>
    /// <param name="cooldownDays">Jours sans reposer la question si elle est refusée (5 par défaut).</param>
    public Prayer? Ask(DecisionKind kind, string subject, string question, string reason, Action apply, GameClock clock, int cooldownDays = RefusalCooldownDays, int wishId = 0)
    {
        if (_prayers.Any(p => p.Status == PrayerStatus.Pending && p.Kind == kind && p.Subject == subject))
            return null;
        if (_blockedUntilTicks.TryGetValue((kind, subject), out long until) && clock.Ticks < until)
            return null;

        var prayer = new Prayer(_nextId++, colony, kind, subject, question, reason, clock.Ticks, apply) { CooldownDays = cooldownDays, WishId = wishId };
        _prayers.Add(prayer);

        // Un souhait divin n'hérite jamais de l'accord d'office : aucune décision administrative n'est prise en son nom.
        if (kind != DecisionKind.Wish && AutoApprove.Contains(kind))
        {
            Resolve(prayer, approve: true, auto: true, clock);
            return prayer;
        }

        ColonyBrain.Say(colony, clock, $"Nous adressons une prière : {question}");
        Asked?.Invoke(prayer);
        return prayer;
    }

    /// <summary>
    /// Vrai si la colonie ne doit pas encore solliciter le joueur pour ce type de décision : une demande attend
    /// sa réponse, ou un refus est trop récent. Évite de chercher une solution dont on ne pourra pas parler.
    /// </summary>
    public bool IsQuiet(DecisionKind kind, GameClock clock) =>
        Pending.Any(p => p.Kind == kind) || (_quietUntilTicks.TryGetValue(kind, out long until) && clock.Ticks < until);

    /// <summary>Le joueur répond à une prière en attente.</summary>
    public void Answer(Prayer prayer, bool approve, GameClock clock)
    {
        if (prayer.Status != PrayerStatus.Pending || prayer.Colony != colony)
            return;
        Resolve(prayer, approve, auto: false, clock);
    }

    private void Resolve(Prayer prayer, bool approve, bool auto, GameClock clock)
    {
        prayer.Status = approve ? PrayerStatus.Approved : PrayerStatus.Refused;
        prayer.AutoApproved = auto;
        prayer.AnsweredTicks = clock.Ticks;
        if (prayer.Kind == DecisionKind.Wish)
            DivineWishes.OnAnswered(colony, prayer, approve, clock.Ticks);

        if (approve && prayer.Kind == DecisionKind.Wish)
        {
            // Le souhait attend un pouvoir qui n'existe pas encore : l'accord est retenu, rien n'est exaucé.
            ColonyBrain.Say(colony, clock, "Notre souhait est entendu : les habitants attendent que ta puissance s'exerce.");
            return;
        }
        if (approve)
        {
            prayer.Apply();
            if (!auto)
                ShiftFaith(ApprovalFaithGain);
            ColonyBrain.Say(colony, clock, auto
                ? $"Décision prise (accord habituel) : {prayer.Question}"
                : $"Notre prière est exaucée : {prayer.Question}");
            return;
        }

        long until = clock.Ticks + prayer.CooldownDays * TimeConstants.TicksPerDay;
        _blockedUntilTicks[(prayer.Kind, prayer.Subject)] = until;
        _quietUntilTicks[prayer.Kind] = until;
        ShiftFaith(-RefusalFaithLoss);
        ColonyBrain.Say(colony, clock, "Notre prière est restée sans réponse favorable : la foi vacille.");
    }

    /// <summary>Un établissement passé à une colonie sœur emporte ses affaires : les prières en attente qui le concernaient sont retirées sans réponse.</summary>
    internal void WithdrawFor(int settlementId)
    {
        foreach (Prayer prayer in _prayers.Where(p => p.Status == PrayerStatus.Pending && p.SettlementId == settlementId))
        {
            prayer.Status = PrayerStatus.Withdrawn;
            prayer.AnsweredTicks = colony.Clock.Ticks;
        }
    }

    /// <summary>La colonie retire la prière d'un souhait devenu sans objet : aucune réponse n'est enregistrée et la foi ne bouge pas.</summary>
    internal void Withdraw(int wishId)
    {
        foreach (Prayer prayer in _prayers.Where(p => p.Status == PrayerStatus.Pending && p.Kind == DecisionKind.Wish && p.WishId == wishId))
        {
            prayer.Status = PrayerStatus.Withdrawn;
            prayer.AnsweredTicks = colony.Clock.Ticks;
        }
    }

    /// <summary>La foi de chacun bouge, plus fort chez les pieux (un sceptique s'en moque un peu).</summary>
    private void ShiftFaith(float amount)
    {
        foreach (Colonist colonist in colony.PresentMembers)
            colonist.Needs.Faith += amount * (1f + 0.5f * colonist.Personality[Axis.Piete]);
    }
}
