using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Le schisme : dans un village devenu grand (ou à l'étroit et morose), un colon ambitieux et peu attaché à sa terre rêve d'en fonder
/// une à lui. Ses amis, son conjoint et leurs enfants le suivraient, avec leur part des réserves. La colonie en prie le joueur :
/// accordé, le groupe part s'installer sur une région libre voisine et fonde une colonie sœur, qui garde les savoirs et l'affection
/// de sa mère ; refusé, la foi vacille et l'on n'en reparle pas avant longtemps.
/// </summary>
public static class Schism
{
    public const int MinPopulation = 30;
    public const int MinCrampedPopulation = 20;
    public const int MinFollowers = 5;
    public const float ChancePerDay = 0.04f;
    public const int RefusalCooldownDays = 30;

    /// <summary>Distance maximale (en cases du monde) entre la colonie mère et sa fille.</summary>
    public const int MaxDistance = 8;

    /// <summary>Le meneur possible : un adulte ambitieux et peu enraciné (null s'il n'y en a pas).</summary>
    public static Colonist? Leader(Colony colony) => colony.Members
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
        int cap = colony.Members.Count / 3;
        var group = new List<Colonist> { leader };
        void Join(Colonist colonist)
        {
            if (group.Count >= cap || group.Contains(colonist) || colonist.Transit != TransitState.None || !colony.Members.Contains(colonist))
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
        foreach (Colonist restless in colony.Members.Where(m => m.Stage == LifeStage.Adult && m.Personality[Axis.Attachement] < 0f)
                     .OrderBy(m => m.Personality[Axis.Attachement]).ThenBy(m => m.Id).ToList())
            Join(restless);
        return group;
    }

    /// <summary>Chaque matin : un grand village où couve un schisme prie le joueur de laisser partir les dissidents.</summary>
    public static void Daily(WorldState world, Colony colony)
    {
        int people = colony.Members.Count;
        bool cramped = people >= MinCrampedPopulation && (colony.AverageMood < 0.5f || colony.Homeless >= 4);
        if ((people < MinPopulation && !cramped) || world.Colonies.Count >= WorldState.MaxPlayerColonies
            || Diplomacy.AtWarWithAnyone(world, colony) || colony.Prayers.IsQuiet(DecisionKind.Schism, world.Clock))
            return;
        if (world.Politics.NextSingle() >= ChancePerDay || Leader(colony) is not { } leader)
            return;
        List<Colonist> group = Followers(colony, leader);
        if (group.Count < MinFollowers || FindRegion(world, colony) < 0)
            return;
        bool female = leader.Sex == Sex.Female;
        colony.Prayers.Ask(DecisionKind.Schism, leader.Id.ToString(),
            $"Laisser {leader.Name} fonder une nouvelle colonie avec {group.Count - 1} fidèles ?",
            (cramped ? "Le village est à l'étroit et l'humeur s'en ressent. " : "Le village a beaucoup grandi. ")
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
        if (mother.Members.FirstOrDefault(m => m.Id == leaderId) is not { } leader || world.Colonies.Count >= WorldState.MaxPlayerColonies)
            return null;
        List<Colonist> group = Followers(mother, leader);
        int tile = FindRegion(world, mother);
        if (group.Count < MinFollowers || tile < 0)
            return null;

        LocalMap map = world.GenerateColonyMap(tile);
        (int campX, int campY) = ColonyFounder.FindCampSite(map);
        Colony daughter = ColonyFounder.CreateCamp(map, UniqueName(world, leader, world.WorldMap.Grid[tile]), world.Clock, mother.Species, campX, campY);
        daughter.Parent = mother;
        foreach ((Discovery discovery, long ticks) in mother.Known)
            daughter.Known[discovery] = ticks;

        // Chacun emporte sa part des réserves.
        float share = group.Count / (float)mother.Members.Count;
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
            daughter.Members.Add(colonist);
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
        daughter.AssignSectors();

        world.AddColony(daughter, tile);
        mother.Opinions[daughter] = 40f;
        daughter.Opinions[mother] = 40f;
        ColonyBrain.Say(mother, world.Clock, $"{leader.Name} et {group.Count - 1} fidèles nous quittent pour fonder {daughter.Name}. Nous leur souhaitons bonne fortune.");
        ColonyBrain.Say(daughter, world.Clock, $"Menés par {leader.Name}, {group.Count} anciens habitants de {mother.Name} fondent {daughter.Name}.");
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
