using System.Globalization;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

// Sonde de mesure longue durée (économie, empires, colonies) : elle ne vérifie rien, elle écrit des séries annuelles dans results/.
// Usage : LongTermProbe [parties=10] [annees=100] [colonies=6] [colonsInitiaux=12]. Le joueur de mesure accepte toutes les prières.
int parties = args.Length > 0 ? int.Parse(args[0]) : 10;
int annees = args.Length > 1 ? int.Parse(args[1]) : 100;
int coloniesInitiales = args.Length > 2 ? int.Parse(args[2]) : 6;
int colons = args.Length > 3 ? int.Parse(args[3]) : 12;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string dossier = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "results"));
Directory.CreateDirectory(dossier);

var colonnes = new List<string>[parties];
var flux = new List<string>[parties];
var mondes = new List<string>[parties];
var evenements = new List<string>[parties];
var debut = System.Diagnostics.Stopwatch.StartNew();

Parallel.For(0, parties, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(parties, 6) }, numero =>
{
    int graine = 401 + numero;
    var monde = new WorldState(graine, startingColonists: colons, colonyCount: coloniesInitiales);
    var lignesColonies = colonnes[numero] = [];
    var lignesFlux = flux[numero] = [];
    var lignesMonde = mondes[numero] = [];
    var lignesEvt = evenements[numero] = [];
    var vues = new Dictionary<int, string>();
    var royaumes = new Dictionary<int, (int Membres, int Roi)>();
    var conquetes = new HashSet<int>();
    var expeditions = new HashSet<int>();
    var batailles = new HashSet<int>();
    var pactes = new HashSet<(PactKind, int, int, long)>();
    var missions = new HashSet<int>();
    var famines = new Dictionary<Colony, FamineObservee>();
    ResourceType[] biens = Enum.GetValues<ResourceType>();
    ResourceType[] suivis = biens.Where(b => b != ResourceType.Coins).Append(ResourceType.Coins).Distinct().ToArray();

    void Evt(string type, string detail) => lignesEvt.Add($"{graine};{monde.Clock.Year};{type};{detail}");
    static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    for (long tick = 1; tick <= annees * (long)TimeConstants.TicksPerYear; tick++)
    {
        int heure = monde.Clock.Hour;
        monde.Step();
        if (heure == monde.Clock.Hour) continue;
        foreach (Colony c in monde.Colonies.ToArray())
            foreach (Prayer p in c.Prayers.Pending.ToArray()) monde.AnswerPrayer(p, true);
        if (monde.Clock.Hour != 12) continue;

        // Chaque jour à midi : événements politiques et militaires, famines.
        var presentes = monde.Colonies.Select(c => c.Id).ToHashSet();
        foreach (var (id, nom) in vues.Where(v => !presentes.Contains(v.Key)).ToArray()) { Evt("colonie_disparue", $"{id} {nom}"); vues.Remove(id); }
        foreach (Colony c in monde.Colonies)
        {
            if (vues.TryAdd(c.Id, c.Name)) Evt("colonie_nee", $"{c.Id} {c.Name} ({c.Species.Plural}) mere={c.Parent?.Id ?? 0}");
            if (c.ConqueredTicks is not null && conquetes.Add(c.Id)) Evt("conquete", $"{c.Id} {c.Name} royaume={c.RealmId}");
            if (!famines.TryGetValue(c, out var f)) famines[c] = f = new();
            f.Observe(c);
        }
        foreach (Realm r in monde.Realms)
        {
            var etat = (r.MemberColonyIds.Count, r.KingColonistId);
            if (!royaumes.TryGetValue(r.Id, out var avant)) Evt("royaume_fonde", $"{r.Id} {r.Name} membres={etat.Item1}");
            else if (avant.Membres != etat.Item1) Evt("royaume_membres", $"{r.Id} {r.Name} {avant.Membres}->{etat.Item1}");
            else if (avant.Roi != etat.Item2) Evt("royaume_roi", $"{r.Id} {r.Name} roi={etat.Item2}");
            royaumes[r.Id] = etat;
        }
        foreach (int id in royaumes.Keys.Where(k => monde.Realms.All(r => r.Id != k)).ToArray()) { Evt("royaume_dissous", id.ToString()); royaumes.Remove(id); }
        foreach (Pact p in monde.Pacts)
            if (pactes.Add((p.Kind, p.A.Id, p.B.Id, p.SinceTicks))) Evt("pacte", $"{p.Kind} {p.A.Id}-{p.B.Id}");
        foreach (WarParty w in monde.WarParties)
        {
            if (expeditions.Add(w.Id)) Evt("expedition_guerre", $"{w.From.Id}->{w.To.Id} guerriers={w.Warriors.Count} armes={w.Weapons}");
            if (w.Victory is not null && batailles.Add(w.Id)) Evt("bataille", $"{w.From.Id}->{w.To.Id} victoire={w.Victory} butin={w.Loot.Values.Sum()}");
        }
        foreach (Caravan v in monde.Caravans)
            if (v.Purpose != TerritorialPurpose.Commerce && missions.Add(v.Id)) Evt("mission", $"{v.Purpose} {v.From.Id}->{v.To.Id}");

        if (monde.Clock.DayOfYear == TimeConstants.DaysPerYear - 1) Releve(monde.Clock.Year);
    }

    void Releve(int an)
    {
        long population = 0;
        foreach (Colony c in monde.Colonies)
        {
            var lieux = c.Settlements.Where(s => s.Status == SettlementStatus.Active).ToList();
            population += c.Members.Count;
            lignesColonies.Add(string.Join(';', graine, an, c.Id, c.Name, c.Species.Plural, c.RealmId, c.Parent?.Id ?? 0, c.Members.Count, c.Children,
                lieux.Sum(s => s.Buildings.Count(b => b.IsComplete)), lieux.Count, F(c.AverageMood), c.Homeless, F(c.Loyalty), c.Prestige,
                c.Known.Count, c.Monuments.Count, c.Settlements.Sum(s => s.Chickens + s.Sheep + s.Cows + s.Horses + s.Oxen), c.Settlements.Sum(s => s.Fields.Count),
                c.TotalDeaths, c.BattlesWon, c.BattlesLost, F(c.WarWeariness), lieux.Sum(s => s.Stock.Get(ResourceType.Coins)), lieux.Sum(s => s.Stock.FoodUnits),
                c.Trades.Count, F((float)c.LifetimeTradeGainHours), famines.TryGetValue(c, out var f) && f.Victime ? 1 : 0));
        }
        var stocks = monde.Colonies.SelectMany(c => c.Settlements).Select(s => s.Stock).ToList();
        foreach (ResourceType bien in suivis)
        {
            long stock = stocks.Sum(s => s.Get(bien));
            long[] t = new[] { ResourceFlow.Production, ResourceFlow.Usage, ResourceFlow.Purchase, ResourceFlow.Sale, ResourceFlow.Loss }
                .Select(flow => stocks.Sum(s => ResourceAccounting.Total(s, bien, flow))).ToArray();
            if (stock + t.Sum() > 0) lignesFlux.Add($"{graine};{an};{bien};{t[0]};{t[1]};{t[2]};{t[3]};{t[4]};{stock}");
        }
        lignesMonde.Add(string.Join(';', graine, an, monde.Colonies.Count, population, monde.Realms.Count,
            monde.Realms.Count == 0 ? 0 : monde.Realms.Max(r => r.MemberColonyIds.Count),
            monde.Pacts.Count(p => p.Kind == PactKind.Alliance), monde.Pacts.Count(p => p.Kind == PactKind.War), monde.Pacts.Count(p => p.Kind == PactKind.Truce),
            monde.WarParties.Count, monde.Caravans.Count, monde.CompletedCaravans, monde.Money.Minted, monde.Money.Dotations, monde.CoinsLostToEvents,
            MonetaryLedger.Mass(monde), monde.Money.Imbalance(monde), monde.Caravans.Sum(c => c.Coins)));
    }
});

void Ecrire(string nom, string entete, List<string>[] blocs) =>
    File.WriteAllLines(Path.Combine(dossier, nom), new[] { entete }.Concat(blocs.SelectMany(b => b)));
Ecrire("colonies.csv", "graine;annee;colonie_id;colonie;espece;royaume;mere;colons;enfants;batiments;etablissements;humeur;sans_abri;loyaute;prestige;decouvertes;monuments;betail;champs;morts_cumules;victoires;defaites;lassitude;pieces;vivres;voyages_commerce;gain_commerce_heures;famine_detectee", colonnes);
Ecrire("flux.csv", "graine;annee;bien;production;usage;achat;vente;perte;stock", flux);
Ecrire("monde.csv", "graine;annee;colonies;population;royaumes;plus_grand_royaume;alliances;guerres;treves;expeditions_en_cours;caravanes_en_cours;caravanes_finies;frappe;dotations;pertes_pieces;masse_monnaie;ecart_monnaie;pieces_en_caravane", mondes);
Ecrire("evenements.csv", "graine;annee;type;detail", evenements);
Console.WriteLine($"{parties} parties de {annees} ans ({coloniesInitiales} colonies de {colons} colons) en {debut.Elapsed.TotalMinutes:0.0} min -> {dossier}");

// Même règle que StarvationWatch (Simulation.Tests), recopiée pour ne pas lier la sonde au projet de tests.
sealed class FamineObservee
{
    private Dictionary<Colonist, long> _precedent = [];
    public bool Victime { get; private set; }
    public void Observe(Colony colonie)
    {
        var maintenant = colonie.Members.Where(m => m.Needs.Food <= 0.02f).ToHashSet();
        Victime |= maintenant.Any(c => _precedent.TryGetValue(c, out long repas) && c.LastMealTicks == repas);
        _precedent = maintenant.ToDictionary(c => c, c => c.LastMealTicks);
    }
}
