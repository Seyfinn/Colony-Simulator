using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

public enum DivineEffectKind { HarvestYield = 0, ChampionStrength = 1 }

public enum DivineEffectStatus { Active = 0, Consumed = 1, Expired = 2, TargetInvalid = 3 }

public enum BlessedPlotState { Pending = 0, Consumed = 1, Invalid = 2 }

/// <summary>Une parcelle bénie : la case et le cycle agricole (semis, dernière moisson) présents lors de l'accord. Aucune semaille ultérieure n'en profite.</summary>
public sealed class BlessedPlot
{
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public long PlantedTicks { get; internal set; }
    public long LastHarvestTicks { get; internal set; }
    public BlessedPlotState State { get; internal set; }

    /// <summary>Unités de plus que la moisson a réellement données (0 tant qu'elle n'a pas eu lieu, ou si le rendement était trop petit pour que le coefficient change quoi que ce soit).</summary>
    public int Gained { get; internal set; }
}

/// <summary>
/// Un pouvoir appliqué : la bénédiction d'une récolte (un champ) ou d'un champion. Son identité source — la colonie et le souhait — est figée même si l'effet change ensuite de
/// propriétaire politique. Un effet n'existe que parce qu'un accord valide l'a créé, une seule fois, sur une cible présente.
/// </summary>
public sealed class DivineEffect
{
    public int Id { get; internal set; }
    public int SourceColonyId { get; internal set; }
    public int WishId { get; internal set; }
    public DivineEffectKind Kind { get; internal set; }
    public WishTargetKind TargetKind { get; internal set; }

    /// <summary>Le champ ou le colon visé ; pour un champ, l'établissement qui le porte.</summary>
    public int TargetId { get; internal set; }
    public int SettlementId { get; internal set; }
    public long StartTicks { get; internal set; }
    public long ExpiresTicks { get; internal set; }
    public DivineEffectStatus Status { get; internal set; }
    public List<BlessedPlot> Plots { get; } = [];
}

/// <summary>Ce que l'interface lit d'un effet : une vue immuable, jamais une autorité du monde.</summary>
public sealed record DivineEffectView(int Id, int WishId, DivineEffectKind Kind, DivineEffectStatus Status, WishTargetKind TargetKind, int TargetId, int SettlementId,
    long StartTicks, long ExpiresTicks, int PendingPlots, int ConsumedPlots, int InvalidPlots, int UnitsGained, string Explanation);

/// <summary>
/// Les pouvoirs accordés au joueur-dieu : bénédiction des récoltes (un quart de rendement de plus, une seule fois par parcelle déjà en croissance, deux saisons au plus) et du champion
/// (un quart de la force de base d'un guerrier, un an, tant qu'il prend part au combat). Les effets sont bornés, ne se cumulent pas et ne remplacent ni l'agriculture ni le
/// combat. Accepter applique le pouvoir une fois, à une cible valide ; refuser ou ignorer ne produit rien. Aucun hasard n'est tiré ici.
/// </summary>
public static class DivinePowers
{
    /// <summary>Coefficient entier de récolte (1250/1000), arrondi au plus proche, égalités vers le haut.</summary>
    public const int HarvestPermille = 1250;
    public const int HarvestSeasons = 2;

    /// <summary>Part de la force de base d'un combattant que gagne le champion, et durée de la faveur en années de simulation.</summary>
    public const float ChampionShare = 0.25f;
    public const int ChampionYears = 1;

    // --- Lecture ---

    /// <summary>Une cible porte-t-elle déjà un effet vivant de ce type ? (<paramref name="settlementId"/> : 0 pour un colon.)</summary>
    public static bool IsBlessed(Colony colony, DivineEffectKind kind, int settlementId, int targetId) =>
        colony.DivineEffects.Any(e => e.Kind == kind && e.Status == DivineEffectStatus.Active && e.TargetId == targetId && e.SettlementId == settlementId
            && colony.Clock.Ticks < e.ExpiresTicks);

    /// <summary>Les effets d'une colonie, du plus ancien au plus récent, sous forme de vues pour l'interface.</summary>
    public static IReadOnlyList<DivineEffectView> Views(Colony colony) => colony.DivineEffects.OrderBy(e => e.Id).Select(e =>
        new DivineEffectView(e.Id, e.WishId, e.Kind, e.Status, e.TargetKind, e.TargetId, e.SettlementId, e.StartTicks, e.ExpiresTicks,
            e.Plots.Count(p => p.State == BlessedPlotState.Pending), e.Plots.Count(p => p.State == BlessedPlotState.Consumed), e.Plots.Count(p => p.State == BlessedPlotState.Invalid),
            e.Plots.Sum(p => p.Gained), Explain(e))).ToList();

    private static string Explain(DivineEffect effect) => (effect.Kind, effect.Status) switch
    {
        (DivineEffectKind.HarvestYield, DivineEffectStatus.Active) => "La prochaine moisson de chaque parcelle bénie rend un quart de plus.",
        (DivineEffectKind.HarvestYield, DivineEffectStatus.Consumed) => "Les parcelles bénies ont été moissonnées : la bénédiction est consommée.",
        (DivineEffectKind.HarvestYield, DivineEffectStatus.Expired) => "La bénédiction a expiré avant d'avoir servi à toutes les parcelles.",
        (DivineEffectKind.HarvestYield, _) => "Les parcelles bénies ont disparu ou leur cycle s'est achevé sans moisson.",
        (_, DivineEffectStatus.Active) => "Le champion a un quart de force de plus tant qu'il combat.",
        (_, DivineEffectStatus.Expired) => "La faveur du champion est achevée.",
        _ => "Le champion n'est plus parmi nous.",
    };

    // --- L'application ---

    /// <summary>
    /// Un souhait accordé applique son pouvoir : une seule fois, à une cible valide. Idempotent (un second appel renvoie l'effet existant sans rien ajouter) ; une cible disparue
    /// invalide le souhait, une cible déjà bénie le termine en <see cref="DivineWishStatus.AlreadyBlessed"/>. L'offrande n'est ni rendue ni consommée une seconde fois, et aucune
    /// monnaie ni ressource n'est créée.
    /// </summary>
    internal static DivineEffect? ApplyAccepted(Colony colony, DivineWish wish)
    {
        DivineEffect? existing = colony.DivineEffects.FirstOrDefault(e => e.SourceColonyId == wish.SourceColonyId && e.WishId == wish.Id);
        if (existing is not null)
            return existing;
        if (wish.AcceptedTicks is null || wish.Status != DivineWishStatus.AwaitingResponse)
            return null;
        Settlement? place = colony.Settlements.FirstOrDefault(s => s.Id == wish.SettlementId);
        if (place is null || !DivineWishes.IsTargetValid(colony, wish))
        {
            wish.Status = DivineWishStatus.TargetInvalid;
            wish.Outcome = wish.TargetKind == WishTargetKind.Field ? "Ce champ n'a plus de culture en croissance : rien n'a été béni." : "Ce guerrier n'est plus des nôtres : rien n'a été accordé.";
            ColonyBrain.Say(colony, colony.Clock, "Notre souhait est entendu, mais " + (wish.TargetKind == WishTargetKind.Field ? "la récolte visée n'existe plus." : "le champion n'est plus là."));
            return null;
        }

        DivineEffectKind kind = wish.TargetKind == WishTargetKind.Field ? DivineEffectKind.HarvestYield : DivineEffectKind.ChampionStrength;
        int settlementId = kind == DivineEffectKind.HarvestYield ? place.Id : 0;
        if (IsBlessed(colony, kind, settlementId, wish.TargetId))
        {
            wish.Status = DivineWishStatus.AlreadyBlessed;
            wish.Outcome = kind == DivineEffectKind.HarvestYield ? "Ce champ est déjà béni : l'accord n'ajoute rien." : "Ce champion est déjà béni : l'accord n'ajoute rien.";
            ColonyBrain.Say(colony, colony.Clock, "Notre souhait est entendu : " + (kind == DivineEffectKind.HarvestYield ? "ce champ est déjà béni, rien de plus ne s'y ajoute." : "notre champion est déjà béni, rien de plus ne s'y ajoute."));
            return null;
        }

        long now = colony.Clock.Ticks;
        var effect = new DivineEffect
        {
            Id = colony.NextEffectId(), SourceColonyId = wish.SourceColonyId, WishId = wish.Id, Kind = kind, TargetKind = wish.TargetKind,
            TargetId = wish.TargetId, SettlementId = settlementId, StartTicks = now, Status = DivineEffectStatus.Active,
            ExpiresTicks = now + (kind == DivineEffectKind.HarvestYield ? HarvestSeasons * TimeConstants.DaysPerSeason * (long)TimeConstants.TicksPerDay : ChampionYears * (long)TimeConstants.TicksPerYear),
        };
        if (kind == DivineEffectKind.HarvestYield)
        {
            // Uniquement le cycle présent : les parcelles déjà en croissance ou mûres, avec leurs dates. Une semaille ultérieure n'en profite jamais.
            Field field = place.Fields.First(f => f.Id == wish.TargetId);
            foreach (FieldPlot plot in field.Plots.Where(p => p.Stage is CropStage.Growing or CropStage.Ripe))
                effect.Plots.Add(new BlessedPlot { X = plot.X, Y = plot.Y, PlantedTicks = plot.PlantedTicks, LastHarvestTicks = plot.LastHarvestTicks });
        }
        colony.DivineEffects.Add(effect);
        // L'effet et l'exaucement naissent dans la même transition ; la preuve d'application est l'effet lui-même.
        DivineWishes.TryFulfill(colony, wish, effect);
        wish.Outcome = kind == DivineEffectKind.HarvestYield
            ? $"{effect.Plots.Count} parcelles bénies : leur prochaine moisson rendra un quart de plus."
            : "Le champion a un quart de force de plus au combat pendant un an.";
        ColonyBrain.Say(colony, colony.Clock, kind == DivineEffectKind.HarvestYield
            ? $"Ta bénédiction se pose sur nos champs : {effect.Plots.Count} parcelles rendront un quart de plus à leur prochaine moisson."
            : "Ta faveur se pose sur notre champion : il aura un quart de force de plus au combat pendant un an.");
        return effect;
    }

    // --- Les récoltes ---

    /// <summary>
    /// Le rendement supplémentaire qu'une moisson reçoit : le coefficient s'applique au rendement réel (après le piétinement), arrondi au plus proche, égalités vers le haut, et le
    /// bonus se consomme une seule fois pour ce cycle exact de cette parcelle. Aucun bonus si l'effet a expiré, si la parcelle n'a pas été bénie ou si son cycle a changé. À appeler
    /// avant de réinitialiser les dates de la parcelle. Le gain peut être nul sur un très petit rendement : aucun minimum artificiel.
    /// </summary>
    public static int HarvestBonus(Colony colony, int x, int y, int actualYield, long plantedTicks, long lastHarvestTicks)
    {
        Settlement place = colony.LocalSettlement;
        Field? field = place.Fields.FirstOrDefault(f => f.Contains(x, y));
        if (field is null || actualYield <= 0)
            return 0;
        long now = colony.Clock.Ticks;
        foreach (DivineEffect effect in colony.DivineEffects)
        {
            if (effect.Kind != DivineEffectKind.HarvestYield || effect.Status != DivineEffectStatus.Active || effect.SettlementId != place.Id || effect.TargetId != field.Id || now >= effect.ExpiresTicks)
                continue;
            BlessedPlot? blessed = effect.Plots.FirstOrDefault(p => p.X == x && p.Y == y && p.State == BlessedPlotState.Pending
                && p.PlantedTicks == plantedTicks && p.LastHarvestTicks == lastHarvestTicks);
            if (blessed is null)
                continue;
            int boosted = (int)(((long)actualYield * HarvestPermille + 500) / 1000);
            blessed.State = BlessedPlotState.Consumed;
            blessed.Gained = boosted - actualYield;
            if (effect.Plots.All(p => p.State != BlessedPlotState.Pending))
                effect.Status = DivineEffectStatus.Consumed;
            return blessed.Gained;
        }
        return 0;
    }

    // --- Le champion ---

    /// <summary>
    /// La force que les champions bénis ajoutent à un groupe de combattants : un quart de la force de base d'un combattant (<paramref name="baseStrength"/>), pour chaque champion qui est
    /// réellement parmi eux, vivant, de cette colonie et dont la faveur est encore vivante. Ne bénit ni les armes, ni les alliés, ni une bande dont le champion est absent ; le bonus
    /// s'ajoute avant les facteurs collectifs (art de la guerre, fortification).
    /// </summary>
    public static float ChampionStrengthBonus(Colony colony, IEnumerable<Colonist> participants, float baseStrength)
    {
        if (colony.DivineEffects.Count == 0)
            return 0f;
        long now = colony.Clock.Ticks;
        float bonus = 0f;
        foreach (Colonist fighter in participants)
            if (fighter.Colony == colony && colony.DivineEffects.Any(e => e.Kind == DivineEffectKind.ChampionStrength && e.Status == DivineEffectStatus.Active
                    && e.TargetId == fighter.Id && now < e.ExpiresTicks))
                bonus += ChampionShare * baseStrength;
        return bonus;
    }

    // --- La vie des effets ---

    /// <summary>Chaque jour : les effets arrivés à échéance expirent, ceux dont la cible a disparu (champion mort, champ détruit, cycle achevé sans moisson) s'invalident. Aucun tirage.</summary>
    internal static void Daily(Colony colony)
    {
        if (colony.DivineEffects.Count == 0)
            return;
        long now = colony.Clock.Ticks;
        foreach (DivineEffect effect in colony.DivineEffects.Where(e => e.Status == DivineEffectStatus.Active))
        {
            if (now >= effect.ExpiresTicks)
            {
                effect.Status = DivineEffectStatus.Expired;
                continue;
            }
            if (effect.Kind == DivineEffectKind.ChampionStrength)
            {
                if (!colony.Members.Any(m => m.Id == effect.TargetId))
                    effect.Status = DivineEffectStatus.TargetInvalid;
                continue;
            }
            Field? field = colony.Settlements.FirstOrDefault(s => s.Id == effect.SettlementId)?.Fields.FirstOrDefault(f => f.Id == effect.TargetId);
            foreach (BlessedPlot blessed in effect.Plots.Where(p => p.State == BlessedPlotState.Pending))
            {
                FieldPlot? plot = field?.Plots.FirstOrDefault(p => p.X == blessed.X && p.Y == blessed.Y);
                if (plot is null || plot.Stage == CropStage.Fallow || plot.PlantedTicks != blessed.PlantedTicks || plot.LastHarvestTicks != blessed.LastHarvestTicks)
                    blessed.State = BlessedPlotState.Invalid;
            }
            if (effect.Plots.All(p => p.State != BlessedPlotState.Pending))
                effect.Status = effect.Plots.Any(p => p.State == BlessedPlotState.Consumed) ? DivineEffectStatus.Consumed : DivineEffectStatus.TargetInvalid;
        }
        // L'historique est borné : les effets clos les plus anciens s'effacent (le souhait garde sa trace).
        List<DivineEffect> closed = colony.DivineEffects.Where(e => e.Status != DivineEffectStatus.Active).OrderBy(e => e.Id).ToList();
        foreach (DivineEffect old in closed.Take(Math.Max(0, closed.Count - 32)))
            colony.DivineEffects.Remove(old);
    }

    /// <summary>Un colon meurt : sa faveur s'éteint tout de suite.</summary>
    internal static void OnColonistGone(Colony colony, Colonist colonist)
    {
        foreach (DivineEffect effect in colony.DivineEffects.Where(e => e.Kind == DivineEffectKind.ChampionStrength && e.Status == DivineEffectStatus.Active && e.TargetId == colonist.Id))
            effect.Status = DivineEffectStatus.TargetInvalid;
    }
}
