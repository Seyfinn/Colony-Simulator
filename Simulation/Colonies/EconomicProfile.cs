namespace GodColony.Simulation.Colonies;

/// <summary>
/// Ce qu'une colonie semble être, d'après ce qu'elle a réellement produit, vendu et acheté : des descriptions déduites, jamais une profession obligatoire
/// ni un bonus. Calculé à la demande, il n'est pas sauvegardé.
/// </summary>
public sealed record EconomicProfile(IReadOnlyList<string> Labels, IReadOnlyList<ResourceType> Dependencies, IReadOnlyList<ResourceType> Exports)
{
    /// <summary>Part du travail productif qu'une catégorie doit atteindre pour décrire la colonie.</summary>
    public const double LabelShare = 0.35;

    /// <summary>Part des approvisionnements d'un bien qui vient d'importations avant de parler de dépendance.</summary>
    public const double DependencyShare = 0.5;

    public static EconomicProfile Of(Colony colony)
    {
        // Le travail investi par catégorie : unités produites × heures par unité, d'après le registre persistant.
        double food = 0, mining = 0, craft = 0;
        foreach (ResourceType good in Enum.GetValues<ResourceType>())
        {
            if (colony.Labor.HoursPerUnit(good) is not { } hours) continue;
            double work = colony.Labor.TotalProduced(good) * hours;
            if (ResourceCatalog.Nutrition(good) > 0) food += work;
            else if (good is ResourceType.Stone or ResourceType.IronOre or ResourceType.CopperOre or ResourceType.GoldOre or ResourceType.MineralCoal or ResourceType.Clay
                     or ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond) mining += work;
            else if (good is not (ResourceType.Wood or ResourceType.Coins)) craft += work;
        }
        double total = food + mining + craft;
        var labels = new List<string>();
        if (total > 0)
        {
            if (food / total >= LabelShare) labels.Add("agricole");
            if (mining / total >= LabelShare) labels.Add("minière");
            if (craft / total >= LabelShare) labels.Add("artisanale");
        }
        var sold = new Dictionary<ResourceType, int>(); var bought = new Dictionary<ResourceType, int>();
        foreach (TradeLine line in colony.Trades.Where(t => t.WeSent).SelectMany(t => t.Lines))
        {
            var side = line.IsSale ? sold : bought;
            side[line.Good] = side.GetValueOrDefault(line.Good) + line.Units;
        }
        if (sold.Values.Sum() + bought.Values.Sum() >= 40 && colony.Trades.Count >= 5) labels.Add("marchande");
        var dependencies = bought.Where(p => p.Value >= DependencyShare * (p.Value + colony.Labor.TotalProduced(p.Key))).Select(p => p.Key).OrderBy(g => (int)g).ToList();
        var exports = sold.Where(p => p.Value >= colony.Labor.TotalProduced(p.Key) * 0.2).Select(p => p.Key).OrderBy(g => (int)g).ToList();
        return new EconomicProfile(labels, dependencies, exports);
    }
}
