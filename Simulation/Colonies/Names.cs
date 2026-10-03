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

    /// <summary>Un prénom au hasard, de préférence qu'aucun autre membre de la colonie ne porte déjà.</summary>
    public static string Pick(Sex sex, Random random, IEnumerable<string> taken)
    {
        string[] list = sex == Sex.Female ? Female : Male;
        var used = new HashSet<string>(taken);
        string name = list[random.Next(list.Length)];
        if (!used.Contains(name))
            return name;
        string? free = list.FirstOrDefault(n => !used.Contains(n));
        if (free is not null)
            return free;

        // Tous les prénoms sont pris : on les numérote, comme les rois (« Odon II »).
        for (int n = 2; ; n++)
        {
            string numbered = $"{name} {Roman(n)}";
            if (!used.Contains(numbered))
                return numbered;
        }
    }

    private static string Roman(int n)
    {
        var result = new System.Text.StringBuilder();
        foreach ((int value, string symbol) in new[] { (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
            while (n >= value)
            {
                result.Append(symbol);
                n -= value;
            }
        return result.ToString();
    }
}
