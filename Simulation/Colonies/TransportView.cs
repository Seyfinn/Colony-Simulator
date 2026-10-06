namespace GodColony.Simulation.Colonies;

/// <summary>
/// Lecture pure de ce qui se transporte : le contenu et la destination d'un porteur, d'une charrette ou d'une caravane, en langage clair (« 6 bois → entrepôt »,
/// « 12 sel, 30 pièces → Valmont »). Le texte vient des données de la simulation ; l'affichage ne fait que le montrer au survol.
/// </summary>
public static class TransportView
{
    /// <summary>
    /// Ce qu'un colon porte et où il va : « 6 bois → entrepôt », « 4 pierre → chantier : four à pain (en charrette) ». Vide s'il ne porte rien.
    /// </summary>
    public static string TransportLabel(Colonist colonist)
    {
        if (colonist.Colony is null || colonist.Carrying is not { Amount: > 0 } load)
            return "";
        string cart = colonist.UsingCart ? " (en charrette)" : "";
        return $"{load.Amount} {Trade.GoodName(load.Type, load.Amount)} → {DestinationOf(colonist)}{cart}";
    }

    /// <summary>La destination d'un porteur : le chantier qu'il approvisionne, sinon le dépôt le plus proche (l'entrepôt achevé ou le camp).</summary>
    public static string DestinationOf(Colonist colonist)
    {
        if (colonist.CarryingTo is { } site)
            return $"chantier : {Building.NameOf(site.Type)}";
        Colony colony = colonist.Colony;
        if (SettlementServices.Nearest(colony, ServiceUse.Stock, colonist.TileX, colonist.TileY) is { } point && point.Id != SettlementServices.CampId)
            if (colony.BuildingById(point.Id - 1000) is { } depot)
                return Building.NameOf(depot.Type);
        return "camp";
    }

    /// <summary>
    /// Le chargement d'une caravane et sa destination : « 12 sel, 30 pièces → Valmont », « 8 grain → Valmont (retour) ». Une mission interne dit sa raison (« ravitaillement »).
    /// </summary>
    public static string TransportLabel(Caravan caravan)
    {
        string cargo = string.Join(", ", caravan.Cargo.Where(p => p.Value > 0 && p.Key != ResourceType.Coins).OrderBy(p => p.Key)
            .Select(p => $"{p.Value} {Trade.GoodName(p.Key, p.Value)}")
            .Concat(caravan.Coins > 0 ? [$"{caravan.Coins} pièces"] : []));
        if (cargo.Length == 0)
            cargo = "à vide";
        string gear = caravan.Gear switch
        {
            CaravanGear.WoodCart => " (charrette)",
            CaravanGear.IronCart => " (charrette renforcée)",
            CaravanGear.Draft => caravan.DraftCount > 0 ? $" (attelage de {Trade.GoodName(caravan.DraftSpecies, 2)})" : " (charrette renforcée)",
            _ => "",
        };
        if (caravan.Purpose != TerritorialPurpose.Commerce)
            return $"{cargo} → {TerritorialTravel.Label(caravan.Purpose)}{gear}";
        return caravan.State == CaravanState.Returning
            ? $"{cargo} → {caravan.From.Name} (retour){gear}"
            : $"{cargo} → {caravan.To.Name}{gear}";
    }
}
