using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les offrandes collectives : les habitants décident seuls d'un autel ou d'une statue, réunissent des matériaux réellement livrés à leur
/// établissement, travaillent la pierre au sanctuaire, puis dressent un monument. Rien ici n'accorde un pouvoir : le monument porte un souhait
/// (voir <see cref="DivineWishes"/>) que le joueur est libre d'ignorer, de refuser ou d'accepter.
/// Les offrandes cèdent toujours devant une crise : elles ne prennent ni les derniers vivres, ni le bois de chauffe, ni les bras nécessaires.
/// </summary>
public static class Offerings
{
    /// <summary>Habitants présents qu'il faut avant de songer à une offrande.</summary>
        public const int MinResidents = 14;

    /// <summary>Jours entre deux projets d'offrande d'une même colonie.</summary>
    public const int CooldownDays = 30;

    /// <summary>Jours qu'un projet attend son sanctuaire, ou une crise, avant d'être abandonné (les apports sont alors rendus au stock).</summary>
    public const int PatienceDays = 90;

    /// <summary>Travail de pierre fourni par une action de sculpteur, en secondes à vitesse ×1.</summary>
    public const float ChunkSeconds = 20f;

    /// <summary>Part des pièces disponibles que le coût des matériaux précieux peut représenter à la création du projet (valeur d'équilibrage initiale).</summary>
    public const float CoinShare = 0.25f;

    public static OfferingProject? Active(Colony colony, int settlementId) =>
        colony.Offerings.FirstOrDefault(p => p.SettlementId == settlementId && p.IsActive);

    private static OfferingProject? ActiveHere(Colony colony) => Active(colony, colony.LocalSettlement.Id);

    private static bool InCrisis(Colony colony) =>
        ExtendedIndustry.Crisis(colony) || colony.Sensors is { SurvivalAssured: false };

    /// <summary>Un projet attend son sanctuaire : la colonie bâtit un lieu de préparation (voir <see cref="Civic.Candidates"/>).</summary>
    public static bool WantsShrine(Colony colony) =>
        ActiveHere(colony) is { State: OfferingProjectState.Proposed } && !colony.Buildings.Any(b => b.Type == BuildingType.Shrine);

    /// <summary>Le besoin d'un matériau pour l'économie locale : ce qui manque encore au projet en cours, publié aux échanges (0 hors collecte).</summary>
    public static int Need(Colony colony, ResourceType good) =>
        ActiveHere(colony) is { State: OfferingProjectState.Gathering or OfferingProjectState.Proposed } project ? project.Missing(good) : 0;

    /// <summary>Le projet est en chantier et rien ne presse plus que lui : on peut travailler la pierre.</summary>
    internal static bool CanSculpt(Colony colony) => ActiveHere(colony) is { State: OfferingProjectState.Building } && !InCrisis(colony);

    /// <summary>Le travail de sculpture à faire maintenant : le sanctuaire où travailler, si le projet est en chantier et que le confort ne prend pas trop de bras.</summary>
    internal static Building? PickSculptJob(Colony colony)
    {
        if (!CanSculpt(colony)) return null;
        if (colony.Buildings.FirstOrDefault(b => b.Type == BuildingType.Shrine && b.IsComplete) is not { } shrine) return null;
        int sculptors = colony.PresentMembers.Count(m => m.Activity?.Kind == ActivityKind.Sculpt);
        return sculptors < Math.Max(1, colony.Workers.Count() / 10) ? shrine : null;
    }

    /// <summary>Une action de sculpture s'achève : elle ajoute son travail au projet de l'établissement, une seule fois par action.</summary>
    internal static void AddWork(Colony colony)
    {
        if (ActiveHere(colony) is { State: OfferingProjectState.Building } project)
            project.WorkDone = Math.Min(project.WorkSeconds, project.WorkDone + ChunkSeconds);
    }

    /// <summary>Chaque heure, dans le contexte d'un établissement : lancer, approvisionner, suspendre, reprendre, achever ou abandonner son projet.</summary>
    internal static void Hourly(WorldState world, Colony colony)
    {
        Settlement place = colony.LocalSettlement;
        if (ActiveHere(colony) is not { } project)
        {
            if (place == colony.PrimarySettlement) TryEnvisage(world, colony, place);
            return;
        }
        long now = world.Clock.Ticks;
        switch (project.State)
        {
            case OfferingProjectState.Proposed:
                if (colony.Buildings.Any(b => b.Type == BuildingType.Shrine && b.IsComplete))
                    SetState(project, OfferingProjectState.Gathering, now);
                else if (Waited(project, now) > PatienceDays)
                    Cancel(world, colony, project, "aucun sanctuaire n'a pu être bâti");
                break;
            case OfferingProjectState.Gathering:
                if (InCrisis(colony)) Suspend(project, now);
                else
                {
                    Supply(colony, place, project);
                    if (project.HasAllMaterials) SetState(project, OfferingProjectState.Building, now);
                }
                break;
            case OfferingProjectState.Building:
                if (InCrisis(colony)) Suspend(project, now);
                else if (project.WorkDone >= project.WorkSeconds) Complete(world, colony, place, project);
                break;
            case OfferingProjectState.Suspended:
                if (!InCrisis(colony)) { project.BlockedReason = null; SetState(project, project.ResumeState, now); }
                else if (Waited(project, now) > PatienceDays) Cancel(world, colony, project, "la crise a duré trop longtemps");
                break;
        }
    }

    private static double Waited(OfferingProject project, long now) => (now - project.StateTicks) / (double)TimeConstants.TicksPerDay;

    private static void SetState(OfferingProject project, OfferingProjectState state, long now)
    {
        project.State = state;
        project.StateTicks = now;
    }

    private static void Suspend(OfferingProject project, long now)
    {
        project.ResumeState = project.State;
        project.BlockedReason = "les vivres ou le chauffage sont trop justes pour une offrande";
        SetState(project, OfferingProjectState.Suspended, now);
    }

    /// <summary>
    /// Transfère au projet les unités réellement présentes dans le stock local, au-delà des réserves de pierre et de bois de l'établissement.
    /// Les unités livrées quittent le stock disponible : elles ne peuvent plus être vendues ni consommées ailleurs.
    /// </summary>
    private static void Supply(Colony colony, Settlement place, OfferingProject project)
    {
        foreach (ResourceType type in project.Required.Keys.OrderBy(t => (int)t))
        {
            int missing = project.Missing(type);
            if (missing == 0) continue;
            int spare = type switch
            {
                ResourceType.Stone => place.Stock.Available(type) - ColonyBrain.StoneReserveTarget,
                ResourceType.Wood => place.Stock.Available(type) - (int)ColonyBrain.HeatingTarget(colony, colony.Clock.Season) - 10,
                _ => place.Stock.Available(type),
            };
            int take = Math.Min(missing, spare);
            if (take > 0 && place.Stock.TryTake(type, take, ResourceFlow.Transfer))
                project.Delivered[type] = project.Delivered.GetValueOrDefault(type) + take;
        }
    }

    private static void Complete(WorldState world, Colony colony, Settlement place, OfferingProject project)
    {
        if (project.State != OfferingProjectState.Building || !project.HasAllMaterials) return;
        Building? shrine = colony.Buildings.FirstOrDefault(b => b.Type == BuildingType.Shrine && b.IsComplete);
        var monument = new Monument
        {
            Id = colony.NextMonumentId(), SettlementId = place.Id, ProjectId = project.Id, Model = project.Model,
            CompletedTicks = world.Clock.Ticks, X = shrine?.X ?? colony.CampX, Y = shrine?.Y ?? colony.CampY,
        };
        foreach ((ResourceType type, int units) in project.Required.OrderBy(r => (int)r.Key))
        {
            monument.Materials[type] = units;
            ResourceAccounting.Record(place.Stock, type, ResourceFlow.Usage, units);
        }
        project.Delivered.Clear();
        project.MonumentId = monument.Id;
        project.BlockedReason = null;
        SetState(project, OfferingProjectState.Completed, world.Clock.Ticks);
        colony.Monuments.Add(monument);
        colony.LastOfferingTicks = world.Clock.Ticks;
        ColonyBrain.Say(colony, world.Clock, $"Notre {project.Template.Name} est {(project.Template.Feminine ? "achevée" : "achevé")} : les habitants s'y recueillent, sans rien en attendre d'office.");
        DivineWishes.TryCreate(colony, monument);
    }

    /// <summary>Abandon défini : les apports non incorporés retournent physiquement au stock de l'établissement.</summary>
    private static void Cancel(WorldState world, Colony colony, OfferingProject project, string reason)
    {
        Settlement place = colony.Settlements.FirstOrDefault(s => s.Id == project.SettlementId) ?? colony.PrimarySettlement;
        foreach ((ResourceType type, int units) in project.Delivered.OrderBy(d => (int)d.Key))
            place.Stock.Add(type, units, ResourceFlow.Transfer);
        project.Delivered.Clear();
        project.BlockedReason = reason;
        SetState(project, OfferingProjectState.Cancelled, world.Clock.Ticks);
        colony.LastOfferingTicks = world.Clock.Ticks;
        ColonyBrain.Say(colony, world.Clock, $"Nous renonçons à notre {project.Template.Name} : {reason}. Les matériaux reviennent aux réserves.");
    }

    /// <summary>La colonie envisage une offrande : réserves sûres, savoir de la maçonnerie, assez d'habitants, un modèle réalisable et des fournisseurs plausibles.</summary>
    private static void TryEnvisage(WorldState world, Colony colony, Settlement place)
    {
        if (colony.PresentMembers.Count < MinResidents || InCrisis(colony) || !Knowledge.Allows(colony, BuildingType.Shrine)
            || world.Clock.Ticks - colony.LastOfferingTicks < CooldownDays * TimeConstants.TicksPerDay) return;
        foreach (OfferingTemplate template in OfferingTemplate.All)
        {
            // Chaque modèle n'est dressé qu'une fois : un nouveau rituel exige un autre modèle, jamais la même dépense répétée.
            if (colony.Offerings.Any(p => p.Model == template.Model && p.State != OfferingProjectState.Cancelled) || !Feasible(world, colony, template)) continue;
            var project = new OfferingProject
            {
                Id = colony.NextOfferingId(), SettlementId = place.Id, Model = template.Model, State = OfferingProjectState.Proposed,
                WorkSeconds = template.WorkSeconds, CreatedTicks = world.Clock.Ticks, StateTicks = world.Clock.Ticks,
            };
            foreach ((ResourceType type, int units) in template.Materials) project.Required[type] = units;
            colony.Offerings.Add(project);
            ColonyBrain.Say(colony, world.Clock, template.Prestigious
                ? $"Les habitants veulent honorer les dieux d'{(template.Feminine ? "une" : "un")} {template.Name} : il faudra réunir de l'or et des pierres rares."
                : $"Les habitants veulent dresser {(template.Feminine ? "une" : "un")} {template.Name} avec la pierre et le bois de la région.");
            return;
        }
    }

    /// <summary>Un modèle est réalisable si ses matériaux précieux sont en stock ou offerts par un fournisseur connu, pour un coût raisonnable en pièces.</summary>
    private static bool Feasible(WorldState world, Colony colony, OfferingTemplate template)
    {
        double cost = 0;
        foreach ((ResourceType type, int units) in template.Materials)
        {
            if (type is ResourceType.Stone or ResourceType.Wood) continue;
            int missing = Math.Max(0, units - colony.Stock.Available(type));
            if (missing == 0) continue;
            if (!world.SupplierMemories.Any(m => m.Observer == colony && m.Offers.Any(o => o.Good == type && o.Available > 0))) return false;
            cost += missing * Economy.Cost(colony, type);
        }
        return cost <= CoinShare * colony.Stock.Available(ResourceType.Coins);
    }

    /// <summary>Chaque jour : souhaits invalidés ou à créer.</summary>
    internal static void Daily(Colony colony) => DivineWishes.Daily(colony);
}
