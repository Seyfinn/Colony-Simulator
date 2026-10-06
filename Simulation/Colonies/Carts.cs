namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les charrettes locales. Quand le dépôt est loin (plus de vingt cases) et que la charge s'annonce lourde, un porteur prend une charrette du stock : il porte jusqu'à quatre
/// fois plus (il enchaîne les abattages ou les moissons avant de rentrer, ou charge davantage de matériaux pour un chantier), marche aussi vite sur un chemin aménagé et à 60 %
/// hors route. Il la rend en déposant sa charge. Une charrette ne se crée pas : elle se fabrique à la forge (10 bois et 1 fer) et reste au stock de la colonie.
/// </summary>
public static class Carts
{
    /// <summary>Distance au dépôt, en cases, au-delà de laquelle une charrette vaut la peine.</summary>
    public const int MinDistance = 20;

    /// <summary>Capacité de port : quatre fois celle d'un porteur.</summary>
    public const int CapacityFactor = 4;

    /// <summary>Vitesse hors des chemins (sur un sentier ou un chemin de terre, la charrette ne ralentit pas).</summary>
    public const float OffRoadSpeed = 0.6f;

    /// <summary>Aller-retour moyen au dépôt (en secondes) à partir duquel la colonie songe à fabriquer une charrette.</summary>
    public const float RoundTripThresholdSeconds = 8f;

    public static int InService(Colony colony) => colony.PresentMembers.Count(c => c.UsingCart);

    /// <summary>Charrettes disponibles : celles du stock moins celles en service.</summary>
    public static int Free(Colony colony) => colony.Stock.Get(ResourceType.Carts) - InService(colony);

    /// <summary>
    /// Les charrettes que la colonie voudrait avoir : une seule tant que le village est petit, deux dès seize habitants, et seulement si l'aller-retour moyen d'un champ
    /// à son dépôt dépasse le seuil et qu'aucune n'est libre.
    /// </summary>
    public static int Wanted(Colony colony)
    {
        if (colony.PresentMembers.Count < 8 || Free(colony) > 0 || Farming.AverageDepotRoundTripSeconds(colony) <= RoundTripThresholdSeconds)
            return 0;
        int have = colony.Stock.Get(ResourceType.Carts);
        // Le nombre voulu suit le besoin simultané mesuré sur dix jours (le pic en service, plus une de plus si des porteurs ont attendu), borné par la main-d œuvre : une par huit habitants.
        ScaleDay[] window = colony.LocalSettlement.ScaleLedger.Last(10).ToArray();
        int observed = Math.Max(1, window.Select(d => d.CartPeak).DefaultIfEmpty(0).Max() + (window.Any(d => d.CartDenied > 0) ? 1 : 0));
        int limit = Math.Min(observed, 1 + colony.PresentMembers.Count / 8);
        return have < limit ? have + 1 : 0;
    }

    /// <summary>La distance, en cases, entre ce lieu et le dépôt le plus proche.</summary>
    public static float DistanceToDepot(Colony colony, int x, int y) =>
        SettlementServices.Nearest(colony, ServiceUse.Stock, x, y) is { } depot ? SettlementServices.DistanceTo(colony, depot, x, y) : 0f;

    /// <summary>Le colon prend-il une charrette ? Si le dépôt est à plus de vingt cases, qu'une charrette est libre et que la charge s'annonce lourde.</summary>
    public static bool ShouldTake(Colony colony, Colonist colonist)
    {
        if (colonist.UsingCart || DistanceToDepot(colony, colonist.TileX, colonist.TileY) <= MinDistance)
            return false;
        if (Free(colony) > 0)
            return true;
        colony.LocalSettlement.ScaleLedger.Today.CartDenied++; // un porteur aurait pris une charrette : le besoin simultané se mesure
        return false;
    }

    /// <summary>Le colon prend une charrette libre ; le pic de charrettes en service de la journée se retient.</summary>
    internal static void Take(Colony colony, Colonist colonist)
    {
        colonist.UsingCart = true;
        ScaleDay today = colony.LocalSettlement.ScaleLedger.Today;
        today.CartPeak = Math.Max(today.CartPeak, InService(colony));
    }

    /// <summary>Le colon rend sa charrette (il a déposé sa charge) : elle redevient disponible au stock.</summary>
    internal static void Return(Colonist colonist)
    {
        colonist.UsingCart = false;
        colonist.CartUnit = 0;
        colonist.LastHarvest = null;
    }
}
