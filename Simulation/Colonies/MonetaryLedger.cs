namespace GodColony.Simulation.Colonies;

/// <summary>
/// Bilan monétaire du monde : dotations politiques, pièces frappées et quota annuel de frappe partagé entre les colonies.
/// Le quota est une règle de capacité, pas une banque : il borne l'émission même si l'or devient abondant. Les pièces réellement
/// présentes restent dans les stocks, les cargaisons et les poches ; le registre ne les possède pas.
/// Au début de chaque année de jeu, le plafond mondial est de 5 % de la monnaie encore présente, réparti entre les colonies vivantes
/// selon leur population (plus fort reste, puis identifiant). Un reste de conversion inférieur à une pièce passe à l'année suivante ;
/// un quota inutilisé expire. Un lot qui traverse la fin d'année garde son engagement dans le budget de son année d'origine.
/// </summary>
public sealed class MonetaryLedger
{
    /// <summary>Part de la monnaie présente qu'on peut frapper chaque année.</summary>
    public const int CapPercent = 5;

    /// <summary>Pièces tirées d'une unité d'or affiné.</summary>
    public const int CoinsPerGold = 2;

    /// <summary>L'année du budget en cours (0 : aucun budget établi).</summary>
    public int Year { get; internal set; }

    /// <summary>Le plafond mondial de frappe de cette année, en pièces.</summary>
    public long YearCap { get; internal set; }

    /// <summary>Le reste de conversion du plafond, en centièmes de pièce (toujours inférieur à une pièce).</summary>
    public long CapRemainderHundredths { get; internal set; }

    /// <summary>Pièces frappées cette année, à l'intérieur du plafond.</summary>
    public long MintedThisYear { get; internal set; }

    /// <summary>Pièces frappées depuis le début de la partie, y compris par un lot né d'une année précédente.</summary>
    public long Minted { get; internal set; }

    /// <summary>Pièces données aux colonies fondées depuis le début de la partie (une seule fois par colonie politique).</summary>
    public long Dotations { get; internal set; }

    private Dictionary<int, long>? _allowances, _used;

    /// <summary>La part de quota de chaque colonie cette année (identifiant de colonie).</summary>
    public IReadOnlyDictionary<int, long> Allowances => _allowances ??= [];

    /// <summary>Ce que chaque colonie a déjà frappé ou engagé cette année.</summary>
    public IReadOnlyDictionary<int, long> Used => _used ??= [];

    public long AllowanceOf(Colony colony) => Allowances.GetValueOrDefault(colony.Id);
    public long UsedBy(Colony colony) => Used.GetValueOrDefault(colony.Id);
    public long RemainingFor(Colony colony) => Math.Max(0, AllowanceOf(colony) - UsedBy(colony));

    /// <summary>Somme exacte des pièces du monde : stocks de tous les établissements et pièces embarquées.</summary>
    public static long Mass(WorldState world) =>
        world.Settlements.Sum(s => (long)s.Stock.Get(ResourceType.Coins)) + world.Caravans.Sum(c => (long)c.Coins);

    /// <summary>
    /// Écart entre la monnaie présente et ce que le registre explique (dotations + frappe − pertes). Nul tant que personne n'ajoute
    /// de pièces hors des règles ; les tests de scénarios qui fixent des stocks à la main le fausseraient.
    /// </summary>
    public long Imbalance(WorldState world) => Mass(world) - (Dotations + Minted - world.CoinsLostToEvents);

    /// <summary>Établit le budget de l'année : plafond, reste de conversion et parts. Les engagements des années passées ne sont pas redistribués.</summary>
    internal void StartYear(WorldState world)
    {
        Year = world.Clock.Year;
        long hundredths = Mass(world) * CapPercent + CapRemainderHundredths;
        YearCap = hundredths / 100;
        CapRemainderHundredths = hundredths % 100;
        MintedThisYear = 0;
        _allowances = [];
        _used = [];
        var living = world.Colonies.Where(c => c.Members.Count > 0).OrderBy(c => c.Id).ToList();
        long population = living.Sum(c => (long)c.Members.Count);
        if (population == 0 || YearCap == 0) return;
        long assigned = 0;
        foreach (Colony colony in living)
        {
            long share = YearCap * colony.Members.Count / population;
            _allowances[colony.Id] = share;
            assigned += share;
        }
        // Les pièces restantes vont aux plus forts restes, à égalité à l'identifiant le plus bas : l'attribution est déterministe.
        foreach (Colony colony in living
                     .OrderByDescending(c => YearCap * c.Members.Count % population).ThenBy(c => c.Id)
                     .Take((int)(YearCap - assigned)))
            _allowances[colony.Id]++;
    }

    /// <summary>Engage du quota pour un lot : refusé si la colonie n'en a plus cette année.</summary>
    internal bool TryCommit(Colony colony, int coins)
    {
        if (coins <= 0 || RemainingFor(colony) < coins) return false;
        _used ??= [];
        _used[colony.Id] = UsedBy(colony) + coins;
        return true;
    }

    /// <summary>Un lot abandonné rend son engagement, mais seulement si son année est encore celle du budget en cours.</summary>
    internal void Release(Colony colony, int year, int coins)
    {
        if (year != Year || coins <= 0) return;
        _used ??= [];
        _used[colony.Id] = Math.Max(0, UsedBy(colony) - coins);
    }

    /// <summary>Un lot achevé : les pièces créées comptent dans le total de la partie et, si l'année est la même, dans celle de l'année.</summary>
    internal void Complete(int year, int coins)
    {
        Minted += coins;
        if (year == Year) MintedThisYear += coins;
    }

    /// <summary>Un schisme partage le quota restant selon la part des fidèles ; la colonie sœur ne reçoit pas de quota en plus.</summary>
    internal void Split(Colony mother, Colony daughter, float share)
    {
        long moved = (long)(RemainingFor(mother) * Math.Clamp(share, 0f, 1f));
        if (moved <= 0) return;
        _allowances ??= [];
        _allowances[mother.Id] = AllowanceOf(mother) - moved;
        _allowances[daughter.Id] = AllowanceOf(daughter) + moved;
    }
}
