using System.Diagnostics;
using System.Text.Json;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Tests;
using GodColony.Simulation.Time;

// Mesures séparées des neuf graines de croissance ; seul le résumé est écrit dans la console.
int parties = args.Length > 0 ? int.Parse(args[0]) : 15;
int annees = args.Length > 1 ? int.Parse(args[1]) : 20;
var resultats = new List<Resultat>();
var duree = Stopwatch.StartNew();
for (int numero = 0; numero < parties; numero++)
{
    var monde = new WorldState(101 + numero, startingColonists: 8, colonyCount: 2);
    long monnaieInitiale = Monnaie(monde);
    var famines = monde.Colonies.ToDictionary(c => c, _ => new StarvationWatch());
    int ecartsMonetaires = 0, stocksNegatifs = 0, voyagesSansEchange = 0;
    double poidsExcedentaire = 0;
    var missions = new Dictionary<TerritorialPurpose, HashSet<int>>();
    int etablissementsMax = 0;
    for (long tick = 0; tick < annees * (long)TimeConstants.TicksPerYear; tick++)
    {
        int voyagesAvant = monde.CompletedCaravans, heureAvant = monde.Clock.Hour;
        monde.Step();
        if (monde.CompletedCaravans > voyagesAvant)
            voyagesSansEchange += monde.Colonies.Sum(c => c.Trades.Count(r => r.WeSent && r.Ticks == monde.Clock.Ticks && !r.Lines.Any()));
        if (monde.Clock.Hour != heureAvant)
            foreach (Caravan voyage in monde.Caravans.Where(c => c.Purpose == TerritorialPurpose.Commerce))
                poidsExcedentaire = Math.Max(poidsExcedentaire, voyage.LoadWeight - Trade.CapacityOf(voyage.From, voyage.To));
        if (tick % TimeConstants.TicksPerDay != 0) continue;
        if (Monnaie(monde) + monde.CoinsLostToEvents != monnaieInitiale + monde.Money.Minted) ecartsMonetaires++;
        foreach (Caravan voyage in monde.Caravans)
        {
            if (!missions.TryGetValue(voyage.Purpose, out HashSet<int>? vus)) missions[voyage.Purpose] = vus = [];
            vus.Add(voyage.Id);
        }
        etablissementsMax = Math.Max(etablissementsMax, monde.Settlements.Count(s => s.Status == SettlementStatus.Active));
        foreach (Colony colonie in monde.Colonies)
        {
            if (!famines.TryGetValue(colonie, out StarvationWatch? famine)) famines[colonie] = famine = new();
            famine.Observe(colonie);
            stocksNegatifs += Enum.GetValues<ResourceType>().Count(r => colonie.Stock.Get(r) < 0);
        }
    }
    int population = monde.Colonies.SelectMany(c => c.Members.Concat(c.Transients))
        .Concat(monde.Caravans.SelectMany(c => c.Traders)).Distinct().Count();
    resultats.Add(new(101 + numero, population, monde.CompletedCaravans, voyagesSansEchange,
        famines.Values.Count(f => f.Victim is not null), monde.Colonies.Sum(c => c.Graves.Count(g => g.Cause == "faim")),
        ecartsMonetaires, stocksNegatifs, poidsExcedentaire, monde.Colonies.Sum(c => c.LifetimeTradeGainHours))
    {
        Colonies = monde.Colonies.Count, EtablissementsMax = etablissementsMax, EtablissementsFermes = monde.Settlements.Count(s => s.Status == SettlementStatus.Closed),
        Fondations = missions.GetValueOrDefault(TerritorialPurpose.Foundation)?.Count ?? 0, Prospections = missions.GetValueOrDefault(TerritorialPurpose.Prospection)?.Count ?? 0,
        Ravitaillements = missions.GetValueOrDefault(TerritorialPurpose.Supply)?.Count ?? 0, Relocalisations = missions.GetValueOrDefault(TerritorialPurpose.Relocation)?.Count ?? 0,
        Evacuations = missions.GetValueOrDefault(TerritorialPurpose.Evacuation)?.Count ?? 0, TravauxDeRoute = missions.GetValueOrDefault(TerritorialPurpose.RoadWork)?.Count ?? 0,
        Routes = monde.WorldMap.Roads.Built.Sum(r => r.Level), PiecesFrappees = monde.Money.Minted, Monuments = monde.Colonies.Sum(c => c.Monuments.Count),
        Souhaits = monde.Colonies.Sum(c => c.Wishes.Count), MasseMonetaire = (int)Monnaie(monde),
        Hameaux = monde.Settlements.Count(s => s.Kind == SettlementKind.Hamlet), Villages = monde.Settlements.Count(s => s.Kind == SettlementKind.Village && s.Owner.PrimarySettlement != s),
    });
}
var resume = new
{
    Parties = parties, Annees = annees, Graines = resultats.Select(r => r.Graine).ToArray(),
    PopulationMoyenne = resultats.Average(r => r.Population), PopulationMin = resultats.Min(r => r.Population), PopulationMax = resultats.Max(r => r.Population),
    VoyagesTermines = resultats.Sum(r => r.Voyages), VoyagesSansEchange = resultats.Sum(r => r.VoyagesSansEchange),
    ColoniesAvecFamineSoutenue = resultats.Sum(r => r.Famines), DecesDeFaim = resultats.Sum(r => r.DecesDeFaim),
    ObservationsAvecEcartMonetaire = resultats.Sum(r => r.EcartsMonetaires), StocksNegatifs = resultats.Sum(r => r.StocksNegatifs),
    PoidsExcedentaireMax = resultats.Max(r => r.PoidsExcedentaire), TravailEpargne = resultats.Sum(r => r.TravailEpargne),
    EtablissementsMaxMoyen = resultats.Average(r => (double)r.EtablissementsMax), EtablissementsFermes = resultats.Sum(r => r.EtablissementsFermes),
    Fondations = resultats.Sum(r => r.Fondations), Prospections = resultats.Sum(r => r.Prospections), Ravitaillements = resultats.Sum(r => r.Ravitaillements),
    Relocalisations = resultats.Sum(r => r.Relocalisations), Evacuations = resultats.Sum(r => r.Evacuations), TravauxDeRoute = resultats.Sum(r => r.TravauxDeRoute),
    NiveauxDeRoute = resultats.Sum(r => r.Routes), PiecesFrappees = resultats.Sum(r => r.PiecesFrappees), Monuments = resultats.Sum(r => r.Monuments), Souhaits = resultats.Sum(r => r.Souhaits),
    MasseMonetaireMoyenne = resultats.Average(r => (double)r.MasseMonetaire), Hameaux = resultats.Sum(r => r.Hameaux), VillagesSecondaires = resultats.Sum(r => r.Villages),
    Secondes = duree.Elapsed.TotalSeconds
};
string json = JsonSerializer.Serialize(resume, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (args.Length > 2)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    File.WriteAllText(args[2], json);
}
return resume.ObservationsAvecEcartMonetaire == 0 && resume.StocksNegatifs == 0 && resume.PoidsExcedentaireMax <= 0.0001 ? 0 : 1;

static long Monnaie(WorldState monde) => MonetaryLedger.Mass(monde);
sealed record Resultat(int Graine, int Population, int Voyages, int VoyagesSansEchange, int Famines, int DecesDeFaim,
    int EcartsMonetaires, int StocksNegatifs, double PoidsExcedentaire, double TravailEpargne)
{
    public int Colonies { get; init; }
    public int EtablissementsMax { get; init; }
    public int EtablissementsFermes { get; init; }
    public int Fondations { get; init; }
    public int Prospections { get; init; }
    public int Ravitaillements { get; init; }
    public int Relocalisations { get; init; }
    public int Evacuations { get; init; }
    public int TravauxDeRoute { get; init; }
    public int Routes { get; init; }
    public long PiecesFrappees { get; init; }
    public int Monuments { get; init; }
    public int Souhaits { get; init; }
    public int MasseMonetaire { get; init; }
    public int Hameaux { get; init; }
    public int Villages { get; init; }
}
