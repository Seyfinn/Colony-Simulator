namespace GodColony.Simulation.Colonies;

public enum DivineWishKind { CropBlessing = 0, ChampionBlessing = 1 }

/// <summary>
/// L'état d'un souhait. Il reste en attente tant que le joueur n'a pas répondu ; un accord (<see cref="DivineWish.AcceptedTicks"/>) applique le pouvoir à sa cible valide et
/// l'exauce (<see cref="Fulfilled"/>) dans la même transition, jamais avant. <see cref="AlreadyBlessed"/> : la cible portait déjà un effet du même type au moment de l'accord ;
/// l'accord est valide mais n'ajoute rien, et l'explication reste visible.
/// </summary>
public enum DivineWishStatus { AwaitingResponse = 0, Refused = 1, TargetInvalid = 2, Fulfilled = 3, AlreadyBlessed = 4 }

public enum WishTargetKind { Field = 0, Colonist = 1 }

/// <summary>
/// Ce que les habitants demandent au joueur-dieu après une offrande : une bénédiction de récolte (un champ) ou une faveur durable pour un guerrier.
/// La cible est désignée par identifiant stable. Une prière peut exister sans offrande, et une offrande ne rend jamais la réponse obligatoire.
/// </summary>
public sealed class DivineWish
{
    public int Id { get; internal set; }
    public int SettlementId { get; internal set; }
    public DivineWishKind Kind { get; internal set; }
    public WishTargetKind TargetKind { get; internal set; }
    public int TargetId { get; internal set; }

    /// <summary>L'offrande qui l'accompagne (0 pour un souhait sans offrande).</summary>
    public int OfferingProjectId { get; internal set; }
    public int MonumentId { get; internal set; }
    public DivineWishStatus Status { get; internal set; }
    public long CreatedTicks { get; internal set; }
    public long? AnsweredTicks { get; internal set; }

    /// <summary>Moment où le joueur a accordé le souhait, sans qu'un pouvoir l'ait encore appliqué.</summary>
    public long? AcceptedTicks { get; internal set; }

    /// <summary>L'effort réel investi, en clair, pour que le joueur juge de la demande.</summary>
    public string Description { get; internal set; } = "";

    /// <summary>La colonie qui a formulé le souhait : son identité, avec celle du souhait, reste figée même après un transfert politique.</summary>
    public int SourceColonyId { get; internal set; }

    /// <summary>Ce qu'est devenu l'accord, en clair : l'effet appliqué, ou le motif pour lequel rien ne l'a été (cible disparue, déjà bénie).</summary>
    public string Outcome { get; internal set; } = "";
}

/// <summary>Création, réponse et validité des souhaits ; les effets des pouvoirs relèvent d'un chantier distinct.</summary>
public static class DivineWishes
{
    /// <summary>Jours sans reposer la même demande après un refus.</summary>
    public const int RefusalCooldownDays = 20;

    /// <summary>Crée le souhait d'un monument si une cible valide existe ; renvoie null sinon (on réessaie chaque jour).</summary>
    internal static DivineWish? TryCreate(Colony colony, Monument monument)
    {
        if (monument.WishId != 0) return null;
        Settlement? place = colony.Settlements.FirstOrDefault(s => s.Id == monument.SettlementId);
        if (place is null) return null;
        OfferingTemplate template = OfferingTemplate.Of(monument.Model);
        WishTargetKind targetKind;
        int target;
        if (template.Wish == DivineWishKind.CropBlessing)
        {
            targetKind = WishTargetKind.Field;
            // Une cible qui porte déjà une bénédiction du même type n'est pas proposée : on filtre à la création, puis on revalide à la réponse.
            Field? field = place.Fields.Where(f => f.Plots.Any(p => p.Stage is CropStage.Growing or CropStage.Ripe) && !DivinePowers.IsBlessed(colony, DivineEffectKind.HarvestYield, place.Id, f.Id))
                .OrderBy(f => f.Id).FirstOrDefault();
            if (field is null) return null;
            target = field.Id;
        }
        else
        {
            targetKind = WishTargetKind.Colonist;
            Colonist? champion = place.Population.Where(c => c.Stage == LifeStage.Adult && !DivinePowers.IsBlessed(colony, DivineEffectKind.ChampionStrength, 0, c.Id))
                .OrderByDescending(c => c.Personality[Axis.Audace] + c.Personality[Axis.Temperament]).ThenBy(c => c.Id).FirstOrDefault();
            if (champion is null) return null;
            target = champion.Id;
        }
        string materials = string.Join(", ", monument.Materials.OrderBy(m => (int)m.Key).Select(m => $"{ResourceCatalog.Name(m.Key)} {m.Value}"));
        var wish = new DivineWish
        {
            Id = colony.NextWishId(), SourceColonyId = colony.Id, SettlementId = place.Id, Kind = template.Wish, TargetKind = targetKind, TargetId = target,
            OfferingProjectId = monument.ProjectId, MonumentId = monument.Id, Status = DivineWishStatus.AwaitingResponse,
            CreatedTicks = colony.Clock.Ticks,
            Description = $"{Capital(template.Name)} ({materials}).",
        };
        colony.Wishes.Add(wish);
        monument.WishId = wish.Id;
        string question = template.Wish == DivineWishKind.CropBlessing ? "Bénir nos récoltes ?" : "Accorder une faveur durable à notre champion ?";
        string reason = $"{wish.Description} " + (template.Wish == DivineWishKind.CropBlessing
            ? $"Les habitants espèrent ta bénédiction : la prochaine moisson des parcelles de ce champ déjà en croissance rendrait un quart de plus (une seule fois par parcelle, {DivinePowers.HarvestSeasons} saisons au plus)."
            : "Les habitants espèrent ta faveur : ce guerrier aurait un quart de force de plus au combat pendant un an, tant qu'il y prend part.");
        colony.Prayers.Ask(DecisionKind.Wish, $"souhait:{wish.Id}", question, reason, () => { }, colony.Clock, RefusalCooldownDays, wish.Id);
        return wish;
    }

    private static string Capital(string text) => char.ToUpper(text[0]) + text[1..];

    /// <summary>La réponse du joueur : un refus est enregistré ; un accord est retenu mais n'exauce rien tant qu'aucun pouvoir ne s'applique.</summary>
    internal static void OnAnswered(Colony colony, Prayer prayer, bool approve, long ticks)
    {
        DivineWish? wish = colony.Wishes.FirstOrDefault(w => w.Id == prayer.WishId);
        if (wish is null || wish.Status != DivineWishStatus.AwaitingResponse) return;
        wish.AnsweredTicks = ticks;
        if (approve) wish.AcceptedTicks = ticks;
        else wish.Status = DivineWishStatus.Refused;
    }

    /// <summary>
    /// Marque un souhait exaucé, seulement si l'effet appliqué lui est lié : un effet enregistré chez cette colonie, issu de ce souhait, après un accord réel, sur une cible encore valide.
    /// Un simple booléen ne suffit jamais à fabriquer un exaucement sans effet.
    /// </summary>
    public static bool TryFulfill(Colony colony, DivineWish wish, DivineEffect effect)
    {
        if (wish.AcceptedTicks is null || wish.Status != DivineWishStatus.AwaitingResponse || effect.WishId != wish.Id || effect.SourceColonyId != wish.SourceColonyId
            || !colony.DivineEffects.Contains(effect) || effect.Status != DivineEffectStatus.Active || !IsTargetValid(colony, wish))
            return false;
        wish.Status = DivineWishStatus.Fulfilled;
        return true;
    }

    /// <summary>La récolte est-elle encore en cours, le guerrier encore en vie ?</summary>
    public static bool IsTargetValid(Colony colony, DivineWish wish)
    {
        Settlement? place = colony.Settlements.FirstOrDefault(s => s.Id == wish.SettlementId);
        if (place is null) return false;
        return wish.TargetKind == WishTargetKind.Field
            ? place.Fields.Any(f => f.Id == wish.TargetId && f.Plots.Any(p => p.Stage is CropStage.Growing or CropStage.Ripe))
            : colony.Members.Any(c => c.Id == wish.TargetId);
    }

    /// <summary>Chaque jour : les souhaits dont la cible a disparu sont invalidés (l'offrande reste) et les monuments sans souhait en cherchent un.</summary>
    internal static void Daily(Colony colony)
    {
        DivinePowers.Daily(colony); // les effets vivants expirent ou s'invalident avant tout
        foreach (DivineWish wish in colony.Wishes.Where(w => w.Status == DivineWishStatus.AwaitingResponse))
        {
            if (IsTargetValid(colony, wish)) continue;
            wish.Status = DivineWishStatus.TargetInvalid;
            colony.Prayers.Withdraw(wish.Id);
        }
        foreach (Monument monument in colony.Monuments.Where(m => m.WishId == 0).ToArray())
            TryCreate(colony, monument);
    }
}
