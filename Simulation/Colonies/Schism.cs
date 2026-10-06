using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Le schisme : dans un village qui a une raison durable de se séparer (voir <see cref="GrowthPolicy"/>), un colon ambitieux et peu attaché à sa terre rêve d'en fonder
/// une à lui. Ses amis, son conjoint et leurs enfants le suivraient, avec leur part des réserves. La colonie en prie le joueur :
/// accordé, le groupe part s'installer sur une région libre voisine et fonde une colonie sœur, qui garde les savoirs et l'affection
/// de sa mère ; refusé, la foi vacille et l'on n'en reparle pas avant longtemps.
/// </summary>
public static class Schism
{
    public const int MinCrampedPopulation = 20;
    public const int MinFollowers = 5;
    public const float ChancePerDay = 0.04f;
    public const int RefusalCooldownDays = 30;

    /// <summary>Distance maximale (en cases du monde) entre la colonie mère et sa fille.</summary>
    public const int MaxDistance = 8;

    /// <summary>Le meneur possible : un adulte ambitieux et peu enraciné (null s'il n'y en a pas).</summary>
    public static Colonist? Leader(Colony colony) => colony.PresentMembers
        .Where(m => m.Stage == LifeStage.Adult && m.Transit == TransitState.None
            && m.Personality[Axis.Ambition] >= 0.35f && m.Personality[Axis.Attachement] <= 0.3f)
        .OrderByDescending(m => m.Personality[Axis.Ambition] - m.Personality[Axis.Attachement]).ThenBy(m => m.Id)
        .FirstOrDefault();

    /// <summary>
    /// Ceux qui suivraient le meneur : ses amis et les adultes les moins enracinés, avec leurs conjoints et leurs enfants,
    /// sans dépasser le tiers de la colonie.
    /// </summary>
    public static List<Colonist> Followers(Colony colony, Colonist leader)
    {
        int cap = colony.PresentMembers.Count / 3;
        var group = new List<Colonist> { leader };
        void Join(Colonist colonist)
        {
            if (group.Count >= cap || group.Contains(colonist) || colonist.Transit != TransitState.None || !colony.PresentMembers.Contains(colonist))
                return;
            group.Add(colonist);
            if (colonist.Partner is { } partner)
                Join(partner);
            foreach (Colonist child in colonist.Children.Where(c => c.Stage is LifeStage.Child or LifeStage.Teen))
                Join(child);
        }
        if (leader.Partner is { } leaderPartner)
            Join(leaderPartner);
        foreach (Colonist child in leader.Children.Where(c => c.Stage is LifeStage.Child or LifeStage.Teen))
            Join(child);
        foreach (Colonist friend in leader.FriendsIn(colony).OrderBy(m => m.Id).ToList())
            Join(friend);
        foreach (Colonist restless in colony.PresentMembers.Where(m => m.Stage == LifeStage.Adult && m.Personality[Axis.Attachement] < 0f)
                     .OrderBy(m => m.Personality[Axis.Attachement]).ThenBy(m => m.Id).ToList())
            Join(restless);
        return group;
    }

    /// <summary>Habitants qu'il faut à un établissement pour faire sécession avec ses voisins.</summary>
    public const int MinSecessionPopulation = 10;

    /// <summary>Le meneur d'un établissement secondaire : un adulte présent, ambitieux et peu attaché.</summary>
    private static Colonist? LeaderOf(Settlement place) => place.Population
        .Where(m => m.Stage == LifeStage.Adult && m.Personality[Axis.Ambition] >= 0.35f && m.Personality[Axis.Attachement] <= 0.3f)
        .OrderByDescending(m => m.Personality[Axis.Ambition] - m.Personality[Axis.Attachement]).ThenBy(m => m.Id).FirstOrDefault();

    /// <summary>Un établissement secondaire peut-il se détacher (tous ses habitants chez eux, aucune mission en cours qui le concerne) ?</summary>
    public static bool CanSecede(WorldState world, Colony mother, Settlement place) =>
        place != mother.PrimarySettlement && place.Status == SettlementStatus.Active && world.Colonies.Count < WorldState.MaxPlayerColonies
        && place.Residents.Count() >= MinSecessionPopulation && place.Residents.All(c => c.TravelId == 0 && c.LocationSettlementId == place.Id)
        && !world.Caravans.Any(t => t.From == mother && (t.FromSettlementId == place.Id || t.ToSettlementId == place.Id))
        && !world.WarParties.Any(w => w.From == mother && w.Warriors.Any(x => x.HomeSettlementId == place.Id));

    /// <summary>
    /// Un établissement secondaire malheureux, mené par un ambitieux, voudrait se détacher de sa colonie : la colonie en prie le joueur.
    /// Accordé, l'établissement entier — stocks, ouvrages, champs, habitants et leurs projets — devient la colonie sœur.
    /// </summary>
    private static void SecessionDaily(WorldState world, Colony colony)
    {
        foreach (Settlement place in colony.Settlements.Where(s => CanSecede(world, colony, s)).OrderBy(s => s.Id))
        {
            if (place.Residents.Average(c => c.Needs.Mood) >= 0.45f || LeaderOf(place) is not { } leader
                || colony.Prayers.IsQuiet(DecisionKind.Schism, world.Clock) || Diplomacy.AtWarWithAnyone(world, colony)
                || world.Politics.NextSingle() >= ChancePerDay) continue;
            bool female = leader.Sex == Sex.Female;
            colony.Prayers.Ask(DecisionKind.Schism, $"village:{place.Id}:{leader.Id}",
                $"Laisser {place.Name} se détacher de {colony.Name} sous la conduite de {leader.Name} ?",
                $"Les {place.Residents.Count()} habitants de {place.Name} s'y sentent à l'étroit sous notre autorité ; {leader.Name}, {(female ? "ambitieuse" : "ambitieux")}, les mène. "
                + "Ils garderaient leurs stocks, leurs ouvrages et leurs champs, et resteraient nos proches parents.",
                () => Secede(world, colony, place.Id, leader.Id), world.Clock, RefusalCooldownDays);
            return;
        }
    }

    /// <summary>La sécession accordée : l'établissement devient une colonie sœur, avec ses habitants, ses biens et ses affaires en cours.</summary>
    internal static Colony? Secede(WorldState world, Colony mother, int settlementId, int leaderId)
    {
        Settlement? place = mother.Settlements.FirstOrDefault(s => s.Id == settlementId);
        if (place is null || !CanSecede(world, mother, place) || place.Population.FirstOrDefault(c => c.Id == leaderId) is not { } leader) return null;
        List<Colonist> people = place.Residents.ToList();
        var daughter = new Colony(UniqueName(world, leader, world.WorldMap.Grid[place.RegionTileIndex]), place.CampX, place.CampY, place.GatherSpots.ToList())
            { Species = mother.Species, Clock = world.Clock, Parent = mother };
        foreach ((Discovery discovery, long ticks) in mother.Known) daughter.Known[discovery] = ticks;
        // Ce que les habitants savent du terrain les suit : leurs renseignements sur les gîtes et les régions sondées.
        foreach (DepositKnowledge report in mother.DepositReports)
            daughter.DepositReports.Add(new DepositKnowledge { SiteId = report.SiteId, Region = report.Region, Material = report.Material, State = report.State,
                EstimateMin = report.EstimateMin, EstimateMax = report.EstimateMax, Confidence = report.Confidence, SurveyedDepth = report.SurveyedDepth,
                ObservedTicks = report.ObservedTicks, Source = report.Source, Alerted = report.Alerted });
        daughter.VisitedRegions.AddRange(mother.VisitedRegions);
        foreach ((int region, int reach) in mother.RegionReach) daughter.RegionReach[region] = reach;
        float share = people.Count / (float)Math.Max(1, mother.Members.Count);
        daughter.Settlements.Clear();
        mother.Settlements.Remove(place);
        daughter.Settlements.Add(place);
        place.AdoptBy(daughter);
        foreach (Colonist person in people)
        {
            mother.Members.Remove(person);
            person.Colony = daughter;
            daughter.Members.Add(person);
        }
        // Les couples séparés par la sécession se défont, comme pour un départ ordinaire.
        foreach (Colonist person in people)
            if (person.Partner is { } partner && partner.Colony != daughter)
            {
                partner.Partner = null; person.Partner = null;
                partner.Needs.Grief = Math.Max(partner.Needs.Grief, 0.5f);
                person.Needs.Grief = Math.Max(person.Needs.Grief, 0.5f);
            }
        world.AdoptColony(daughter, place);
        // Projets d'offrande, monuments et souhaits de l'établissement, et prières qui le concernaient.
        var projects = mother.Offerings.Where(p => p.SettlementId == place.Id).ToList();
        var monuments = mother.Monuments.Where(m => m.SettlementId == place.Id).ToList();
        var wishes = mother.Wishes.Where(w => w.SettlementId == place.Id).ToList();
        mother.Offerings.RemoveAll(projects.Contains); mother.Monuments.RemoveAll(monuments.Contains); mother.Wishes.RemoveAll(wishes.Contains);
        daughter.AdoptOfferings(projects, monuments, wishes);
        var effects = mother.DivineEffects.Where(e => e.Kind == DivineEffectKind.HarvestYield ? e.SettlementId == place.Id : people.Any(p => p.Id == e.TargetId)).ToList();
        mother.DivineEffects.RemoveAll(effects.Contains);
        daughter.AdoptEffects(effects); // l'identitÃ© source (colonie, souhait) reste figÃ©e
        mother.Prayers.WithdrawFor(place.Id);
        world.Money.Split(mother, daughter, share);
        mother.Opinions[daughter] = 40f; daughter.Opinions[mother] = 40f;
        ColonyBrain.Say(mother, world.Clock, $"{place.Name} se détache : {leader.Name} et {people.Count - 1} habitants forment {daughter.Name}. Nous leur souhaitons bonne fortune.");
        ColonyBrain.Say(daughter, world.Clock, $"Menés par {leader.Name}, les {people.Count} habitants de {place.Name} fondent {daughter.Name}.");
        Realms.OnDaughter(world, mother, daughter);
        return daughter;
    }

    /// <summary>
    /// Chaque matin : un village qui a une raison durable de se séparer (plus de place pour loger, mécontentement soutenu, installation nettement moins chère ailleurs)
    /// prie le joueur de laisser partir un groupe. Un village prospère, même grand, n'en demande aucun ; sans raison recevable, aucun tirage n'est fait.
    /// </summary>
    public static void Daily(WorldState world, Colony colony)
    {
        SecessionDaily(world, colony);
        if (world.Colonies.Count >= WorldState.MaxPlayerColonies || colony.PresentMembers.Count < MinCrampedPopulation
            || Diplomacy.AtWarWithAnyone(world, colony) || colony.Prayers.IsQuiet(DecisionKind.Schism, world.Clock)
            || Leader(colony) is not { } leader)
            return;
        List<Colonist> group = Followers(colony, leader);
        if (group.Count < MinFollowers || FindRegion(world, colony) < 0)
            return;
        GrowthDecision decision = GrowthPolicy.EvaluateDeparture(world, colony, group.Count);
        if (decision.Reason == GrowthReason.None || world.Politics.NextSingle() >= ChancePerDay)
            return;
        SettlementGrowthState state = colony.PrimarySettlement.GrowthState;
        int days = decision.Reason switch
        {
            GrowthReason.PersistentDiscontent => state.DiscontentDays,
            GrowthReason.LocalCapacityBlocked => state.BlockedDays,
            _ => state.HousingDays,
        };
        bool female = leader.Sex == Sex.Female;
        colony.Prayers.Ask(DecisionKind.Schism, leader.Id.ToString(),
            $"Laisser {leader.Name} fonder une nouvelle colonie avec {group.Count - 1} fidèles ?",
            GrowthPolicy.Explain(decision, days)
            + $"{leader.Name}, {(female ? "ambitieuse" : "ambitieux")}, rêve d'une terre à {(female ? "elle" : "lui")} et "
            + $"{group.Count - 1} habitants le suivraient, avec leur part de nos réserves. Ils resteraient nos proches parents.",
            () => Split(world, colony, leader.Id), world.Clock, RefusalCooldownDays);
    }

    /// <summary>La région libre la plus agréable pour le peuple de la colonie, à moins de <see cref="MaxDistance"/> cases (-1 sinon).</summary>
    public static int FindRegion(WorldState world, Colony colony)
    {
        int home = world.WorldMap.TileOf(colony);
        foreach (int tile in world.WorldMap.SuggestTiles(colony.Species, 40))
            if (world.WorldMap.Grid.Distance(home, tile) <= MaxDistance)
                return tile;
        return -1;
    }

    /// <summary>Le schisme accordé : le meneur et ses fidèles fondent une colonie sœur. Renvoie la nouvelle colonie (null si c'est devenu impossible).</summary>
    internal static Colony? Split(WorldState world, Colony mother, int leaderId)
    {
        if (mother.PresentMembers.FirstOrDefault(m => m.Id == leaderId) is not { } leader || world.Colonies.Count >= WorldState.MaxPlayerColonies)
            return null;
        // L'accord arrive parfois longtemps après la prière : le groupe, la place restante et le trajet se revérifient, sans rien retirer si le départ est devenu impossible.
        List<Colonist> group = Followers(mother, leader);
        string? obstacle = group.Count < MinFollowers ? "le groupe n'est plus assez nombreux"
            : mother.PresentMembers.Count - group.Count < ScaleRules.MinRemaining ? "le village serait trop dépeuplé"
            : FindRegion(world, mother) < 0 ? "aucune région libre n'est plus à portée"
            : Diplomacy.AtWarWithAnyone(world, mother) ? "la guerre retient chacun"
            : null;
        if (obstacle is not null)
        {
            ColonyBrain.Say(mother, world.Clock, $"Le départ de {leader.Name} est reporté : {obstacle}.");
            return null;
        }
        return FoundFrom(world, mother, leader, group, refugees: false);
    }

    /// <summary>
    /// Un groupe d'habitants fonde une colonie sur une région libre voisine. Pour un schisme, la colonie fille naît dans le royaume de sa mère (voir <see cref="Realms.OnDaughter"/>) ;
    /// des réfugiés d'une conquête fondent au contraire une colonie indépendante, sans mère.
    /// </summary>
    internal static Colony? FoundFrom(WorldState world, Colony mother, Colonist leader, List<Colonist> group, bool refugees)
    {
        int tile = FindRegion(world, mother);
        if (group.Count < (refugees ? 1 : MinFollowers) || tile < 0)
            return null;

        LocalMap map = world.GenerateColonyMap(tile);
        (int campX, int campY) = ColonyFounder.FindCampSite(map);
        Colony daughter = ColonyFounder.CreateCamp(map, UniqueName(world, leader, world.WorldMap.Grid[tile]), world.Clock, mother.Species, campX, campY);
        daughter.Parent = refugees ? null : mother;
        foreach ((Discovery discovery, long ticks) in mother.Known)
            daughter.Known[discovery] = ticks;

        // Chacun emporte sa part des réserves.
        float share = group.Count / (float)mother.PresentMembers.Count;
        foreach (ResourceType good in new[] { ResourceType.Food, ResourceType.Grain, ResourceType.Bread, ResourceType.Flour, ResourceType.SaltedMeat,
                     ResourceType.Wood, ResourceType.Stone, ResourceType.Tools, ResourceType.Clothes, ResourceType.Coins })
        {
            int units = (int)(mother.Stock.Get(good) * share);
            if (units > 0 && mother.Stock.TryTake(good, units, ResourceFlow.Transfer))
                daughter.Stock.Add(good, units, ResourceFlow.Transfer);
        }

        foreach (Colonist colonist in group)
        {
            ColonistAI.DetachFromColony(colonist);
            colonist.Colony = daughter;
            (int x, int y) = daughter.GatherSpots[world.Random.Next(daughter.GatherSpots.Count)];
            colonist.X = colonist.PrevX = x + 0.5f;
            colonist.Y = colonist.PrevY = y + 0.5f;
            colonist.Activity = null;
            colonist.Path = [];
            colonist.PathIndex = 0;
            colonist.UnhappyHours = 0;
            daughter.PresentMembers.Add(colonist);
        }
        // Les couples séparés par le départ se défont.
        foreach (Colonist colonist in group)
            if (colonist.Partner is { } partner && partner.Colony != daughter)
            {
                partner.Partner = null;
                colonist.Partner = null;
                partner.Needs.Grief = Math.Max(partner.Needs.Grief, 0.5f);
                colonist.Needs.Grief = Math.Max(colonist.Needs.Grief, 0.5f);
            }
        // La faveur d'un champion suit la personne, jamais le lieu.
        var carried = mother.DivineEffects.Where(e => e.Kind == DivineEffectKind.ChampionStrength && group.Any(g => g.Id == e.TargetId)).ToList();
        mother.DivineEffects.RemoveAll(carried.Contains);
        daughter.AdoptEffects(carried);
        daughter.AssignSectors();

        world.AddColony(daughter, tile);
        world.Money.Split(mother, daughter, share);
        mother.Opinions[daughter] = refugees ? -40f : 40f;
        daughter.Opinions[mother] = refugees ? -40f : 40f;
        ColonyBrain.Say(mother, world.Clock, refugees
            ? $"{leader.Name} et {group.Count - 1} habitants fuient la conquête et fondent {daughter.Name}."
            : $"{leader.Name} et {group.Count - 1} fidèles nous quittent pour fonder {daughter.Name}. Nous leur souhaitons bonne fortune.");
        ColonyBrain.Say(daughter, world.Clock, refugees
            ? $"Chassés par la conquête de {mother.Name}, {group.Count} réfugiés menés par {leader.Name} fondent {daughter.Name}."
            : $"Menés par {leader.Name}, {group.Count} anciens habitants de {mother.Name} fondent {daughter.Name}.");
        if (!refugees)
            Realms.OnDaughter(world, mother, daughter);
        return daughter;
    }

    private static string UniqueName(WorldState world, Colonist leader, WorldTile tile)
    {
        string prefix = tile.Relief == Relief.Mountains ? "Mont" : tile.Relief == Relief.Hills ? "Roche"
            : tile.River > 0 ? "Pont" : tile.Coastal ? "Port" : "Val";
        string name = $"{prefix}-{leader.Name}";
        for (int n = 2; world.Colonies.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{prefix}-{leader.Name} {n}";
        return name;
    }
}
