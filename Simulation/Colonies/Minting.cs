namespace GodColony.Simulation.Colonies;

/// <summary>
/// La frappe monétaire : une recette de l'atelier de frappe (1 or → 40 pièces) soumise au quota annuel de <see cref="MonetaryLedger"/>.
/// Le quota est engagé quand le lot commence, les pièces n'existent qu'à l'achèvement, et un lot abandonné rend son or et son engagement.
/// La frappe ne dépend d'aucune vente préalable : elle répond seulement à un manque de pièces du village et à de l'or qu'on n'a pas réservé ailleurs.
/// </summary>
public static class Minting
{
    /// <summary>Un village se contente de ce nombre de pièces par habitant avant de vouloir en frapper davantage.</summary>
    public const int ComfortCoinsPerResident = 40;

    /// <summary>Pièces que la colonie voudrait encore frapper maintenant (0 en crise, sans quota, sans or libre ou si elle a assez de pièces).</summary>
    public static int Wanted(Colony colony)
    {
        if (colony.Ledger is not { } ledger || ExtendedIndustry.Crisis(colony)) return 0;
        long room = ComfortCoinsPerResident * colony.PresentMembers.Count - colony.Stock.Available(ResourceType.Coins);
        long quota = ledger.RemainingFor(colony);
        long gold = (long)FreeGold(colony) * MonetaryLedger.CoinsPerGold;
        return (int)Math.Max(0, Math.Min(room, Math.Min(quota, gold)));
    }

    /// <summary>L'or qu'aucun bijou en projet ne réclame : seul celui-ci peut partir à la frappe.</summary>
    public static int FreeGold(Colony colony) =>
        Math.Max(0, colony.Stock.Available(ResourceType.Gold) - Math.Max(0, ExtendedIndustry.Target(colony, ResourceType.Jewelry) - colony.Stock.Get(ResourceType.Jewelry)));

    /// <summary>L'or à affiner pour la frappe : borné, car les gisements d'or sont finis et le quota annuel plafonne le besoin.</summary>
    public static int GoldWish(Colony colony)
    {
        if (colony.Ledger is not { } ledger || ExtendedIndustry.Crisis(colony)) return 0;
        long room = ComfortCoinsPerResident * colony.PresentMembers.Count - colony.Stock.Available(ResourceType.Coins);
        return (int)Math.Max(0, Math.Min(Math.Min(room, ledger.RemainingFor(colony)) / MonetaryLedger.CoinsPerGold, 8));
    }

    /// <summary>Au début du lot : engage le quota (une colonie sans registre ou sans quota ne peut pas frapper).</summary>
    internal static bool TryCommit(Colony colony, Activity activity, Recipe recipe)
    {
        if (colony.Ledger is not { } ledger || !ledger.TryCommit(colony, recipe.OutputAmount)) return false;
        activity.MintYear = ledger.Year;
        activity.MintCoins = recipe.OutputAmount;
        return true;
    }

    /// <summary>À l'achèvement : crée exactement les pièces engagées, une seule fois, dans le stock local de l'atelier.</summary>
    internal static void Complete(Colony colony, Activity activity)
    {
        if (activity.MintCoins <= 0 || colony.Ledger is not { } ledger) return;
        colony.Stock.Add(ResourceType.Coins, activity.MintCoins);
        ledger.Complete(activity.MintYear, activity.MintCoins);
        if (activity.Building is { } workshop)
            colony.LocalSettlement.ScaleLedger.AddProduction(workshop, ResourceType.Coins, activity.MintCoins);
        activity.MintCoins = 0;
    }

    /// <summary>Lot interrompu : l'engagement est rendu, aucune pièce n'a été créée (l'or revient au stock par la restitution ordinaire).</summary>
    internal static void Cancel(Colony colony, Activity activity)
    {
        if (activity.MintCoins <= 0) return;
        colony.Ledger?.Release(colony, activity.MintYear, activity.MintCoins);
        activity.MintCoins = 0;
    }
}
