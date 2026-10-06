using Stopwatch = System.Diagnostics.Stopwatch;
using System.Text.Json;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using GodColony.Simulation.Tests;

// Mesures hors GrowthTests. Le joueur de mesure accepte les prières ; les règles du jeu restent inchangées.
int parties = args.Length > 0 ? int.Parse(args[0]) : 15;
int annees = args.Length > 1 ? int.Parse(args[1]) : 20;
int colonies = args.Length > 2 ? int.Parse(args[2]) : 6;
var duree = Stopwatch.StartNew();
var resultats = new Resultat[parties];
Parallel.For(0, parties, new ParallelOptions { MaxDegreeOfParallelism = 3 }, numero =>
{
    var monde = new WorldState(301 + numero, startingColonists: 12, colonyCount: colonies);
    var royaumes = new HashSet<(int Id, long Fondation)>();
    var conquetes = new HashSet<(int Colonie, long Date)>();
    var secessions = new HashSet<(int Colonie, long Date)>();
    var voyages = new Dictionary<int, int>();
    int petits = 0, grands = 0, tailleMax = 0, charrettesMax = 0, demandeCharrettes = 0, monnaieIncorrecte = 0;
    float trajetMax = 0;
    var famines = new Dictionary<Colony, StarvationWatch>();
    float gibierMin = 100, pressionMax = 0;
    double abondance = 0;
    int observations = 0, gibierFaible = 0, heuresChasse = 0;
    for (long tick = 0; tick < (long)annees * TimeConstants.TicksPerYear; tick++)
    {
        int heure = monde.Clock.Hour;
        monde.Step();
        if (heure == monde.Clock.Hour) continue;
        foreach (Caravan voyage in monde.Caravans.Where(c => c.Purpose == TerritorialPurpose.Commerce))
            voyages[voyage.Id] = Math.Max(voyages.GetValueOrDefault(voyage.Id), voyage.Interceptions);
        heuresChasse += monde.Colonies.Sum(c => c.PresentMembers.Count(m => m.Activity?.Kind is ActivityKind.Hunt or ActivityKind.GreatHunt));
        foreach (Colony colonie in monde.Colonies.ToArray())
        foreach (Prayer priere in colonie.Prayers.Pending.ToArray()) monde.AnswerPrayer(priere, true);
        if (monde.Clock.Hour != 12) continue;
        foreach (Realm royaume in monde.Realms)
        {
            royaumes.Add((royaume.Id, royaume.FoundedTicks));
            int taille = royaume.MemberColonyIds.Count;
            tailleMax = Math.Max(tailleMax, taille);
            if (taille <= 3) petits++; else grands++;
        }
        foreach (Colony colonie in monde.Colonies)
        {
            if (!famines.TryGetValue(colonie, out var watch)) famines[colonie] = watch = new();
            watch.Observe(colonie);
            if (colonie.Sensors is { } sensors)
            {
                gibierMin = Math.Min(gibierMin, sensors.GameAbundance); abondance += sensors.GameAbundance; observations++;
                if (sensors.GameAbundance < 30) gibierFaible++;
                pressionMax = Math.Max(pressionMax, sensors.PredatorPressure);
            }
            if (colonie.ConqueredTicks is { } conquest) conquetes.Add((colonie.Id, conquest));
            foreach (Thought pensee in colonie.Thoughts.Where(t => t.Text.Contains("sécession", StringComparison.OrdinalIgnoreCase)))
                secessions.Add((colonie.Id, pensee.Ticks));
            foreach (Settlement lieu in colonie.Settlements.Where(s => s.Status == SettlementStatus.Active))
            {
                charrettesMax = Math.Max(charrettesMax, lieu.Stock.Get(ResourceType.Carts));
            }
            if (Carts.Wanted(colonie) > 0) demandeCharrettes++;
            trajetMax = Math.Max(trajetMax, Farming.AverageDepotRoundTripSeconds(colonie));
        }
        if (monde.Money.Imbalance(monde) != 0) monnaieIncorrecte++;
    }
    resultats[numero] = new(301 + numero, monde.Colonies.Count, monde.Colonies.Sum(c => c.Members.Count), royaumes.Count,
        tailleMax, petits, grands, conquetes.Count, secessions.Count, voyages.Count, voyages.Values.Count(v => v > 0),
        charrettesMax, demandeCharrettes, trajetMax, monnaieIncorrecte, monde.Colonies.Sum(c => c.Graves.Count(g => g.Cause == "faim")),
        gibierMin, abondance / Math.Max(1, observations), pressionMax, gibierFaible, heuresChasse, famines.Values.Count(f => f.Victim is not null));
    lock (resultats)
        File.WriteAllText("validation-nature-progression.json", JsonSerializer.Serialize(new { PartiesTerminees = resultats.Count(r => r is not null), Parties = parties, DureeSecondes = duree.Elapsed.TotalSeconds }));
});
var resume = new
{
    Parties = parties, Annees = annees, ColoniesInitiales = colonies, ReponsesAuxPrieres = "Toutes acceptées par le joueur de mesure",
    Graines = resultats.Select(r => r.Graine), PopulationMoyenne = resultats.Average(r => r.Population),
    RoyaumesFormes = resultats.Sum(r => r.Royaumes), PartiesAvecRoyaume = resultats.Count(r => r.Royaumes > 0),
    TailleMaxRoyaume = resultats.Max(r => r.TailleMax), ObservationsRoyaumesDe1A3 = resultats.Sum(r => r.Petits),
    ObservationsRoyaumesDe4EtPlus = resultats.Sum(r => r.Grands), Conquetes = resultats.Sum(r => r.Conquetes),
    PenseesDeSecession = resultats.Sum(r => r.Secessions), VoyagesObserves = resultats.Sum(r => r.Voyages),
    VoyagesInterceptes = resultats.Sum(r => r.Interceptes), TauxInterception = resultats.Sum(r => r.Interceptes) / (double)Math.Max(1, resultats.Sum(r => r.Voyages)),
    CharrettesMaxParStock = resultats.Max(r => r.Charrettes), PartiesAvecCharrette = resultats.Count(r => r.Charrettes > 0),
    JoursEtablissementsDemandantCharrette = resultats.Sum(r => r.DemandesCharrette), TrajetDepotMaxSecondes = resultats.Max(r => r.TrajetMax),
    ObservationsEcartMonetaire = resultats.Sum(r => r.MonnaieIncorrecte), DecesDeFaim = resultats.Sum(r => r.Famine),
    ColoniesAvecJeuneSoutenu = resultats.Sum(r => r.Jeunes), GibierMin = resultats.Min(r => r.GibierMin),
    GibierMoyen = resultats.Average(r => r.GibierMoyen), PressionPredateursMax = resultats.Max(r => r.PressionMax),
    ObservationsGibierSous30 = resultats.Sum(r => r.GibierFaible), HeuresColonDeChasse = resultats.Sum(r => r.HeuresChasse),
    DureeSecondes = duree.Elapsed.TotalSeconds,
};
Console.WriteLine(JsonSerializer.Serialize(resume, new JsonSerializerOptions { WriteIndented = true }));
record Resultat(int Graine, int Colonies, int Population, int Royaumes, int TailleMax, int Petits, int Grands,
    int Conquetes, int Secessions, int Voyages, int Interceptes, int Charrettes, int DemandesCharrette, float TrajetMax, int MonnaieIncorrecte, int Famine,
    float GibierMin, double GibierMoyen, float PressionMax, int GibierFaible, int HeuresChasse, int Jeunes);
