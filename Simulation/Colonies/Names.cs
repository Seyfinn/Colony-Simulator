namespace GodColony.Simulation.Colonies;

/// <summary>Prénoms provisoires ; chaque espèce et chaque culture aura plus tard ses propres noms.</summary>
public static class Names
{
    private static readonly string[] Female =
    [
        "Aelis", "Bertille", "Clémence", "Douce", "Esclarmonde", "Flore", "Gisèle", "Héloïse", "Isaure", "Jehanne",
        "Liesse", "Mahaut", "Nicolette", "Oriane", "Pernelle", "Richilde", "Sibylle", "Tiphaine", "Ysabeau", "Aude",
        "Adèle", "Adélaïde", "Agathe", "Agnès", "Aliénor", "Alix", "Alizon", "Amicie", "Anastasie", "Apolline",
        "Arnaude", "Aubrée", "Avoie", "Azalaïs", "Béatrix", "Bénigne", "Berthe", "Blanche", "Brigitte", "Cassandre",
        "Cécile", "Célestine", "Charlotte", "Clarisse", "Claudine", "Colette", "Constance", "Cunégonde", "Delphine", "Denise",
        "Diane", "Dorothée", "Édith", "Éléonore", "Élisabeth", "Élodie", "Emmeline", "Ermengarde", "Estelle", "Étiennette",
        "Eudoxie", "Eulalie", "Faustine", "Félicie", "Fleurie", "Florence", "Françoise", "Geneviève", "Gertrude", "Gillette",
        "Guenièvre", "Guillemette", "Hélène", "Hélissende", "Hermine", "Hildegarde", "Honorine", "Hortense", "Huguette", "Inès",
        "Irène", "Isabeau", "Isabelle", "Jacquette", "Joséphine", "Judith", "Julienne", "Justine", "Laurence", "Léonie",
        "Lucie", "Lucrèce", "Madeleine", "Marguerite", "Marie", "Marthe", "Mathilde", "Mélisande", "Mélusine", "Mirabelle",
        "Monique", "Ombeline", "Ozanne", "Pétronille", "Philippa", "Philomène", "Pierrette", "Radegonde", "Raymonde", "Reine",
        "Rolande", "Rose", "Roxane", "Sabine", "Sidonie", "Solange", "Sophie", "Suzanne", "Théodora", "Thérèse",
        "Valentine", "Véronique", "Victoire", "Viviane", "Yolande", "Yseult", "Yvette", "Zoé", "Ancelie", "Aveline",
        "Basilie", "Bertrade", "Clothilde", "Dulcie", "Ermesinde", "Gersende", "Hawise", "Aldonce", "Amélie", "Anne",
        "Armelle", "Aurore", "Capucine", "Éliane", "Gabrielle", "Lisbeth", "Ludivine", "Margaux", "Noémie", "Perrine",
        "Romane", "Séraphine", "Servane", "Tiphanie",
    ];

    private static readonly string[] Male =
    [
        "Aymeric", "Baudouin", "Clovis", "Enguerrand", "Foulques", "Gautier", "Hugues", "Josselin", "Lancelin", "Mathieu",
        "Odon", "Perceval", "Raoul", "Savary", "Thibaut", "Urbain", "Yvon", "Bertrand", "Gauvain", "Renaud",
        "Adalbert", "Adhémar", "Adrien", "Alain", "Albéric", "Alexandre", "Alphonse", "Amaury", "Ambroise", "André",
        "Anselme", "Arnaud", "Arthur", "Aubin", "Augustin", "Aurélien", "Barthélemy", "Basile", "Benoît", "Bernard",
        "Blaise", "Bruno", "Cédric", "Célestin", "Charles", "Christophe", "Clément", "Constant", "Corentin", "Damien",
        "Denis", "Didier", "Dominique", "Édouard", "Élie", "Émeric", "Étienne", "Eudes", "Eustache", "Évrard",
        "Fabien", "Ferdinand", "Florent", "François", "Frédéric", "Gaétan", "Gaspard", "Gaston", "Geoffroy", "Georges",
        "Gérard", "Germain", "Gilbert", "Gilles", "Godefroy", "Guérin", "Gui", "Guillaume", "Gustave", "Henri",
        "Hilaire", "Honoré", "Hubert", "Isidore", "Jacques", "Jean", "Jérôme", "Joachim", "Jourdain", "Julien",
        "Landry", "Laurent", "Léger", "Léon", "Léonard", "Louis", "Lucas", "Ludovic", "Marc", "Marcel",
        "Martin", "Mathias", "Maurice", "Maxime", "Michel", "Nicolas", "Octave", "Olivier", "Pascal", "Patrice",
        "Paul", "Philibert", "Philippe", "Pierre", "Prosper", "Quentin", "Rainier", "Raphaël", "Raymond", "Rémi",
        "Robert", "Roch", "Rodolphe", "Roger", "Roland", "Samson", "Sébastien", "Serge", "Siméon", "Sylvain",
        "Tancrède", "Théodore", "Théophile", "Thierry", "Thomas", "Tristan", "Valentin", "Valéry", "Venance", "Victor",
        "Vincent", "Vital", "Wandrille", "Xavier", "Yves", "Zacharie", "Gontran", "Anatole", "Armand", "Auguste",
        "Clair", "Cyprien", "Éloi", "Fulbert", "Gabriel", "Ghislain", "Guy", "Hector", "Ignace", "Lambert",
        "Lothaire", "Malo", "Mayeul", "Nazaire", "Ogier", "Pons", "Romain", "Théodoric", "Ulrich",
    ];

    private static readonly string[] Surnames =
    [
        "Lenoir", "Fabre", "Mercier", "Boulanger", "Charron", "Delorme", "Fontaine", "Gaillard", "Hamel", "Joubert",
        "Lefèvre", "Marchand", "Navarre", "Perrin", "Rousseau", "Tessier", "Vidal", "Moulin", "Garnier", "Sabatier",
        "Du Bois", "De la Roche", "Pelletier", "Collin", "Masson", "Berger", "Chevalier", "Lambert", "Renard", "Blanchard",
    ];

    /// <summary>Un nom de famille au hasard, de préférence qu'aucune autre famille de la colonie ne porte déjà.</summary>
    public static string PickSurname(Random random, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        string surname = Surnames[random.Next(Surnames.Length)];
        if (!used.Contains(surname))
            return surname;
        return Surnames.FirstOrDefault(n => !used.Contains(n)) ?? surname;
    }

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
