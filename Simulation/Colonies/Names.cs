namespace GodColony.Simulation.Colonies;

/// <summary>Prénoms provisoires ; chaque espèce et chaque culture aura plus tard ses propres noms.</summary>
public static class Names
{
    private static readonly string[] Female =
    [
        "Aelis", "Bertille", "Clémence", "Douce", "Esclarmonde", "Flore", "Gisèle", "Héloïse", "Isaure", "Jehanne",
        "Liesse", "Mahaut", "Nicolette", "Oriane", "Pernelle", "Richilde", "Sibylle", "Tiphaine", "Ysabeau", "Aude",
    ];

    private static readonly string[] Male =
    [
        "Aymeric", "Baudouin", "Clovis", "Enguerrand", "Foulques", "Gautier", "Hugues", "Josselin", "Lancelin", "Mathieu",
        "Odon", "Perceval", "Raoul", "Savary", "Thibaut", "Urbain", "Yvon", "Bertrand", "Gauvain", "Renaud",
    ];

    public static string Pick(Sex sex, Random random)
    {
        string[] list = sex == Sex.Female ? Female : Male;
        return list[random.Next(list.Length)];
    }
}
