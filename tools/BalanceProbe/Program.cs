using System.Text.Json;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Tests;
using GodColony.Simulation.Time;

// Comparaison reproductible, distincte des neuf graines de GrowthTests : BalanceProbe [sortie] [parties=15] [années=12].
string sortie = args.Length > 0 ? args[0] : "docs/Mesures-equilibrage.json";
int parties = args.Length > 1 ? int.Parse(args[1]) : 15;
int annees = args.Length > 2 ? int.Parse(args[2]) : 12;
var mesures = new Mesure[parties];
Parallel.For(0, parties, new ParallelOptions { MaxDegreeOfParallelism = 4 }, numero =>
{
    var monde = new WorldState(1001 + numero, startingColonists: 8, colonyCount: 4);
    var fondateurs = monde.Colonies.ToArray();
    var famines = fondateurs.ToDictionary(c => c, _ => new StarvationWatch());
    var naissances = new HashSet<Colonist>();
    int ecartsMonnaie = 0, stocksNegatifs = 0;
    double? doublement = null;
    for (long tick = 1; tick <= annees * (long)TimeConstants.TicksPerYear; tick++)
    {
        monde.Step();
        if (doublement is null && fondateurs[0].Members.Count >= 16) doublement = tick / (double)TimeConstants.TicksPerYear;
        if (tick % TimeConstants.TicksPerDay != 0) continue;
        if (monde.Money.Imbalance(monde) != 0) ecartsMonnaie++;
        foreach (Colony colonie in monde.Colonies)
        {
            if (!famines.TryGetValue(colonie, out var veille)) famines[colonie] = veille = new();
            veille.Observe(colonie);
            foreach (Colonist enfant in colonie.Members.Where(c => c.Mother is not null)) naissances.Add(enfant);
        }
        stocksNegatifs += monde.Settlements.Sum(s => Enum.GetValues<ResourceType>().Count(r => s.Stock.Get(r) < 0));
    }
    mesures[numero] = new(1001 + numero, doublement, monde.CompletedCaravans, ecartsMonnaie, stocksNegatifs,
        Species.All.Select(espece =>
        {
            Colony[] colonies = monde.Colonies.Where(c => c.Species == espece).ToArray();
            Settlement[] lieux = monde.Settlements.Where(s => s.Owner.Species == espece).ToArray();
            return new Peuple(espece.Name, colonies.Sum(c => c.Members.Count), naissances.Count(c => c.Species == espece),
                colonies.Sum(c => c.Deaths.Count(d => d.Cause == "faim")), colonies.Count(c => famines[c].Victim is not null),
                lieux.Sum(s => s.Buildings.Count(b => b.IsComplete)), lieux.Sum(s => s.Stock.Get(ResourceType.Tools)),
                colonies.Length == 0 ? 0 : colonies.Average(c => c.AverageMood),
                lieux.Sum(s => s.Labor.TotalProduced(ResourceType.Bread)), lieux.Sum(s => s.Labor.TotalProduced(ResourceType.Tools)),
                colonies.Sum(c => c.LifetimeTradeGainHours));
        }).ToArray());
});
var resume = new
{
    Parties = parties, Annees = annees, Graines = mesures.Select(m => m.Graine),
    DoublementHumainMedian = mesures.Select(m => m.DoublementHumain ?? 99).Order().ElementAt(parties / 2),
    Voyages = mesures.Sum(m => m.Voyages), EcartsMonnaie = mesures.Sum(m => m.EcartsMonnaie), StocksNegatifs = mesures.Sum(m => m.StocksNegatifs),
    Peuples = Species.All.Select(e =>
    {
        Peuple[] peuples = mesures.SelectMany(m => m.Peuples).Where(p => p.Nom == e.Name).ToArray();
        return new { e.Name, PopulationMoyenne = peuples.Average(p => p.Population), PopulationMin = peuples.Min(p => p.Population),
            NaissancesMoyennes = peuples.Average(p => p.Naissances), MortsFaim = peuples.Sum(p => p.MortsFaim), Famines = peuples.Sum(p => p.Famines),
            BatimentsMoyens = peuples.Average(p => p.Batiments), OutilsMoyens = peuples.Average(p => p.Outils), HumeurMoyenne = peuples.Average(p => p.Humeur),
            PainProduit = peuples.Sum(p => p.PainProduit), OutilsProduits = peuples.Sum(p => p.OutilsProduits), GainCommerce = peuples.Sum(p => p.GainCommerce) };
    }),
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sortie))!);
File.WriteAllText(sortie, JsonSerializer.Serialize(new { Resume = resume, Parties = mesures }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(resume, new JsonSerializerOptions { WriteIndented = true }));

record Mesure(int Graine, double? DoublementHumain, int Voyages, int EcartsMonnaie, int StocksNegatifs, Peuple[] Peuples);
record Peuple(string Nom, int Population, int Naissances, int MortsFaim, int Famines, int Batiments, int Outils, double Humeur, int PainProduit, int OutilsProduits, double GainCommerce);
