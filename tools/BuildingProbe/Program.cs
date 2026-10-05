using System.Text.Json;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Tests;
using GodColony.Simulation.Time;

// Mesures supplémentaires : les neuf graines de GrowthTests restent intactes.
int parties = args.Length > 0 ? int.Parse(args[0]) : 15;
var resultats = new List<(double Doublement, int Population, bool Famine, int Deces, int Mines, int Barrages, int Enclos, int Marches)>();
for (int numero = 0; numero < parties; numero++)
{
    var monde = new WorldState(101 + numero, startingColonists: 8);
    Colony colonie = monde.Colonies[0];
    var famine = new StarvationWatch();
    double doublement = 99;
    for (long tick = 1; tick <= 6L * TimeConstants.TicksPerYear; tick++)
    {
        monde.Step();
        if (doublement == 99 && colonie.Members.Count >= 16) doublement = tick / (double)TimeConstants.TicksPerYear;
        if (tick % TimeConstants.TicksPerDay == 0) famine.Observe(colonie);
    }
    resultats.Add((doublement, colonie.Members.Count, famine.Victim is not null,
        colonie.Graves.Count(g => g.Cause == "faim"), colonie.Buildings.Count(b => b.Type == BuildingType.MineDepot && b.IsComplete),
        colonie.Buildings.Count(b => b.IsDam && b.IsComplete),
        colonie.Buildings.Count(b => b.Type == BuildingType.Pen && b.IsExtension && b.IsComplete),
        colonie.Buildings.Count(b => b.Type == BuildingType.Market && b.IsExtension && b.IsComplete)));
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    Parties = parties, Annees = 6, Graines = Enumerable.Range(101, parties),
    DoublementMedian = resultats.Select(r => r.Doublement).Order().ElementAt(parties / 2),
    PartiesAyantDouble = resultats.Count(r => r.Doublement < 99),
    PopulationMoyenne = resultats.Average(r => r.Population), PopulationMin = resultats.Min(r => r.Population),
    FaminesSoutenues = resultats.Count(r => r.Famine), DecesDeFaim = resultats.Sum(r => r.Deces),
    MinesAchevees = resultats.Sum(r => r.Mines), BarragesAcheves = resultats.Sum(r => r.Barrages),
    ExtensionsEnclos = resultats.Sum(r => r.Enclos), ExtensionsMarches = resultats.Sum(r => r.Marches),
}, new JsonSerializerOptions { WriteIndented = true }));
