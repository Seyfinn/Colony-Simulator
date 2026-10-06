using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les royaumes. Une colonie fille naît dans le royaume de sa mère (créé s'il n'existe pas) sans rien lui devoir ; la conquête en ajoute d'autres. Les membres sont alliés,
/// se laissent passer, et élisent un roi parmi leurs chefs. L'influence de la capitale baisse avec la distance et la taille du royaume : la loyauté de chaque membre tend
/// vers une cible qui en dépend, la capitale paie un entretien en vivres et en bois (des consommations comptées, jamais un transfert), et une colonie peu attachée finit par
/// faire sécession. C'est pourquoi les empires restent rares : un très grand royaume ne tient que si la capitale est riche <b>et</b> centrale.
/// Tout le hasard vient de <see cref="WorldState.Politics"/>.
/// </summary>
public static class Realms
{
    /// <summary>Loyauté d'une fille de schisme à sa naissance, d'un peuple conquis juste après la conquête.</summary>
    public const float DaughterLoyalty = 0.9f, ConqueredLoyalty = 0.3f;

    /// <summary>Sous cette loyauté, un jour de plus compte vers la sécession ; au bout de dix jours la colonie s'en va.</summary>
    public const float SecessionLoyalty = 0.25f;
    public const int SecessionDays = 10;

    /// <summary>Une sécession entraîne les membres voisins dont la loyauté est sous ce seuil.</summary>
    public const float FollowLoyalty = 0.35f;

    /// <summary>Un royaume élit son roi tous les quatre ans.</summary>
    public const long KingTermTicks = 4L * TimeConstants.TicksPerYear;

    /// <summary>Entretien quotidien par case de distance à la capitale : 0,2 vivre et 0,1 bois par membre.</summary>
    public const float FoodPerDistance = 0.2f, WoodPerDistance = 0.1f;

    /// <summary>Après une conquête, le peuple conquis garde cinq ans de rancœur.</summary>
    public const long ConquestMemoryTicks = 5L * TimeConstants.TicksPerYear;

    // ---------- Consultation ----------

    public static Realm? Of(WorldState world, Colony colony) =>
        colony.RealmId == 0 ? null : world.Realms.FirstOrDefault(r => r.Id == colony.RealmId);

    public static bool SameRealm(WorldState world, Colony a, Colony b) => a != b && a.RealmId != 0 && a.RealmId == b.RealmId;

    public static IEnumerable<Colony> Members(WorldState world, Realm realm) =>
        realm.MemberColonyIds.Select(id => world.Colonies.FirstOrDefault(c => c.Id == id)).OfType<Colony>();

    public static Colony? Capital(WorldState world, Realm realm) => world.Colonies.FirstOrDefault(c => c.Id == realm.CapitalColonyId);

    public static Colonist? King(WorldState world, Realm realm) =>
        Capital(world, realm) is { } capital ? Members(world, realm).SelectMany(c => c.Members).FirstOrDefault(m => m.Id == realm.KingColonistId) : null;

    /// <summary>La distance en cases du monde entre deux colonies.</summary>
    public static int Distance(WorldState world, Colony a, Colony b) => world.WorldMap.Grid.Distance(world.WorldMap.TileOf(a), world.WorldMap.TileOf(b));

    /// <summary>La loyauté d'une colonie en mots, pour la fiche du royaume.</summary>
    public static string LoyaltyWord(Colony colony) => colony.Loyalty switch
    {
        >= 0.7f => "fidèle",
        >= 0.45f => "hésitante",
        >= SecessionLoyalty + 0.1f => "distante",
        _ => "au bord de la sécession",
    };

    // ---------- Formation ----------

    internal static Realm Found(WorldState world, Colony capital)
    {
        int id = world.Realms.Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;
        int color = 0;
        while (world.Realms.Any(r => r.ColorIndex == color))
            color++;
        var realm = new Realm
        {
            Id = id, Name = $"Royaume de {capital.Name}", ColorIndex = color, CapitalColonyId = capital.Id, MemberColonyIds = [capital.Id],
            FoundedTicks = world.Clock.Ticks, NextKingElectionTicks = world.Clock.Ticks + KingTermTicks,
        };
        world.Realms.Add(realm);
        capital.RealmId = id;
        capital.Loyalty = 1f;
        capital.LowLoyaltyDays = 0;
        return realm;
    }

    /// <summary>Une colonie fille naît dans le royaume de sa mère (créé si besoin, avec la mère pour capitale), fidèle mais sans rien lui devoir.</summary>
    internal static void OnDaughter(WorldState world, Colony mother, Colony daughter)
    {
        Realm realm = Of(world, mother) ?? Found(world, mother);
        if (mother.RealmId == 0)
            mother.RealmId = realm.Id;
        Join(world, realm, daughter, DaughterLoyalty);
        ElectKing(world, realm);
        ColonyBrain.Say(daughter, world.Clock, $"{daughter.Name} naît dans le {realm.Name}, sans rien devoir à {mother.Name}.");
    }

    /// <summary>Une colonie entre dans un royaume : ses habitants et ceux des autres membres se savent alliés.</summary>
    internal static void Join(WorldState world, Realm realm, Colony colony, float loyalty)
    {
        if (!realm.MemberColonyIds.Contains(colony.Id))
            realm.MemberColonyIds.Add(colony.Id);
        colony.RealmId = realm.Id;
        colony.Loyalty = loyalty;
        colony.LowLoyaltyDays = 0;
    }

    // ---------- Le roi ----------

    /// <summary>
    /// Le roi est élu par les chefs des colonies membres (une voix chacun, parmi eux) : renom du candidat, plus la moitié de la taille de sa colonie, plus un cinquième de l'opinion
    /// du votant sur sa colonie, plus du bruit. À égalité, le chef de la capitale l'emporte. La capitale devient la colonie du roi.
    /// </summary>
    public static Colonist? ElectKing(WorldState world, Realm realm)
    {
        var chiefs = Members(world, realm).Select(c => (Colony: c, Chief: Leadership.ChiefOf(c))).Where(p => p.Chief is not null).OrderBy(p => p.Colony.Id).ToList();
        realm.NextKingElectionTicks = world.Clock.Ticks + KingTermTicks;
        if (chiefs.Count == 0)
            return null;
        Colony? oldCapital = Capital(world, realm);
        var votes = chiefs.ToDictionary(p => p.Colony.Id, _ => 0);
        foreach ((Colony voter, Colonist? _) in chiefs)
        {
            int choice = chiefs[0].Colony.Id;
            float best = float.MinValue;
            foreach ((Colony candidate, Colonist? chief) in chiefs)
            {
                float score = chief!.Renown + 0.5f * candidate.Members.Count + voter.OpinionOf(candidate) / 5f + (world.Politics.NextSingle() * 6f - 3f);
                if (score > best) { best = score; choice = candidate.Id; }
            }
            votes[choice]++;
        }
        int top = votes.Values.Max();
        List<int> leaders = votes.Where(v => v.Value == top).Select(v => v.Key).ToList();
        int winnerId = leaders.Contains(realm.CapitalColonyId) ? realm.CapitalColonyId : leaders.Min();
        Colony home = chiefs.First(p => p.Colony.Id == winnerId).Colony;
        Colonist king = Leadership.ChiefOf(home)!;
        realm.KingColonistId = king.Id;
        realm.CapitalColonyId = home.Id;
        home.Loyalty = 1f;
        home.LowLoyaltyDays = 0;
        foreach (Colony member in Members(world, realm))
            ColonyBrain.Say(member, world.Clock, $"{king.Name}, chef de {home.Name}, est élu{(king.Sex == Sex.Female ? "e" : "")} {(king.Sex == Sex.Female ? "reine" : "roi")} du {realm.Name} "
                + $"avec {top} voix sur {chiefs.Count}." + (oldCapital is not null && oldCapital != home ? $" La capitale passe à {home.Name}." : ""));
        return king;
    }

    // ---------- Chaque jour ----------

    /// <summary>
    /// Appelé chaque jour après les chefs et avant la diplomatie : l'entretien est payé (ou non), la loyauté de chaque membre évolue, une colonie trop longtemps peu attachée
    /// fait sécession, le roi est réélu à la mort du précédent ou tous les quatre ans, un royaume réduit à sa capitale se dissout.
    /// </summary>
    public static void Daily(WorldState world)
    {
        foreach (Realm realm in world.Realms.OrderBy(r => r.Id).ToList())
        {
            List<Colony> members = Members(world, realm).Where(c => c.Members.Count > 0).ToList();
            realm.MemberColonyIds.RemoveAll(id => members.All(c => c.Id != id));
            if (members.Count <= 1 || Capital(world, realm) is null)
            {
                Dissolve(world, realm);
                continue;
            }
            if (King(world, realm) is null || world.Clock.Ticks >= realm.NextKingElectionTicks)
                ElectKing(world, realm);
            Colony capital = Capital(world, realm) ?? members[0];
            bool paidYesterday = realm.UpkeepPaid;
            realm.UpkeepPaid = PayUpkeep(world, realm, capital, members);

            foreach (Colony member in members.Where(m => m != capital).OrderBy(m => m.Id).ToList())
            {
                float target = LoyaltyTarget(world, realm, capital, member, members.Count, paidYesterday);
                member.Loyalty += (target - member.Loyalty) * 0.05f;
                member.LowLoyaltyDays = member.Loyalty < SecessionLoyalty ? member.LowLoyaltyDays + 1 : 0;
                if (member.LowLoyaltyDays >= SecessionDays)
                    Secede(world, realm, member);
            }
        }
    }

    /// <summary>
    /// Où tend la loyauté d'un membre : 0,95, moins 3 % par case de distance à la capitale, moins 4 % par autre membre, moins 25 % si la colonie a été conquise il y a moins de cinq ans,
    /// plus l'opinion qu'elle a de la capitale (sur 400), plus 5 % pour un même peuple, plus ou moins 10 % selon que l'entretien a été payé la veille.
    /// </summary>
    public static float LoyaltyTarget(WorldState world, Realm realm, Colony capital, Colony member, int memberCount, bool upkeepPaid) =>
        0.95f - 0.03f * Distance(world, member, capital) - 0.04f * (memberCount - 1)
        - (member.ConqueredTicks is { } conquered && world.Clock.Ticks - conquered < ConquestMemoryTicks ? 0.25f : 0f)
        + member.OpinionOf(capital) / 400f + (member.Species == capital.Species ? 0.05f : 0f) + (upkeepPaid ? 0.10f : -0.10f);

    /// <summary>
    /// L'entretien : pour chaque membre, 0,2 vivre et 0,1 bois par case de distance (messagers, fêtes, garnisons), consommés à la capitale comme les repas : jamais transférés
    /// au membre. Payé seulement si la capitale garde ses réserves de survie ; sinon « entretien non payé » et la loyauté en souffre.
    /// </summary>
    public static bool PayUpkeep(WorldState world, Realm realm, Colony capital, IReadOnlyList<Colony> members)
    {
        (int food, int wood) = UpkeepCost(world, capital, members);
        Stockpile stock = capital.PrimarySettlement.Stock;
        int reserve = (int)Math.Ceiling(Math.Max(1, capital.PresentMembers.Count) * ColonyBrain.MealsPerColonistPerDay * 3f);
        if (capital.Sensors is { SurvivalAssured: false } || stock.FoodUnits < food + reserve
            || stock.Available(ResourceType.Wood) < wood + (int)ColonyBrain.HeatingTarget(capital, world.Clock.Season))
        {
            if (!realm.UpkeepPaid || world.Clock.DayOfSeason == 1)
                ColonyBrain.Say(capital, world.Clock, $"Entretien non payé : {capital.Name} ne peut pas se permettre de faire vivre tout le {realm.Name}.");
            return false;
        }
        TakeFood(stock, food);
        if (wood > 0)
            stock.TryTake(ResourceType.Wood, wood);
        return true;
    }

    /// <summary>Le coût quotidien de l'entretien : somme sur les membres de leur distance, vivres et bois (arrondis au-dessus sur la somme).</summary>
    public static (int Food, int Wood) UpkeepCost(WorldState world, Colony capital, IEnumerable<Colony> members)
    {
        int distance = members.Where(m => m != capital).Sum(m => Math.Max(1, Distance(world, m, capital)));
        return ((int)Math.Ceiling(FoodPerDistance * distance), (int)Math.Ceiling(WoodPerDistance * distance));
    }

    private static void TakeFood(Stockpile stock, int units)
    {
        foreach (ResourceType food in new[] { ResourceType.Bread, ResourceType.Food, ResourceType.Fish, ResourceType.SaltedMeat, ResourceType.Meat, ResourceType.Grain })
        {
            int take = Math.Min(units, stock.Available(food));
            if (take > 0 && stock.TryTake(food, take))
                units -= take;
            if (units <= 0)
                return;
        }
    }

    // ---------- Sécession et dissolution ----------

    /// <summary>
    /// Une colonie peu attachée proclame son indépendance. Conquise, elle peut prier le joueur de lui déclarer la guerre à son ancienne capitale ; sinon l'opinion mutuelle baisse
    /// de 20 points, sans guerre automatique. Les membres voisins eux aussi peu loyaux la suivent dans un nouveau royaume dont elle est la capitale.
    /// </summary>
    public static void Secede(WorldState world, Realm realm, Colony colony)
    {
        Colony? former = Capital(world, realm);
        realm.MemberColonyIds.Remove(colony.Id);
        colony.RealmId = 0;
        colony.LowLoyaltyDays = 0;
        colony.Loyalty = 1f;
        foreach (Colony other in Members(world, realm))
            ColonyBrain.Say(other, world.Clock, $"{colony.Name} proclame son indépendance du {realm.Name}.");
        ColonyBrain.Say(colony, world.Clock, $"{colony.Name} proclame son indépendance du {realm.Name} !");

        List<Colony> followers = former is null ? [] : Members(world, realm)
            .Where(m => m != former && m.Loyalty < FollowLoyalty && Distance(world, m, colony) <= 3).OrderBy(m => m.Id).ToList();
        if (followers.Count > 0)
        {
            Realm created = Found(world, colony);
            foreach (Colony follower in followers)
            {
                realm.MemberColonyIds.Remove(follower.Id);
                Join(world, created, follower, 0.6f);
                ColonyBrain.Say(follower, world.Clock, $"{follower.Name} suit {colony.Name} dans le {created.Name}.");
            }
            ElectKing(world, created);
        }

        if (former is not null)
        {
            if (colony.ConqueredTicks is not null)
            {
                Colony enemy = former;
                if (!Diplomacy.AtWar(world, colony, enemy) && !Diplomacy.AtWarWithAnyone(world, colony) && !colony.Prayers.IsQuiet(DecisionKind.War, world.Clock))
                    colony.Prayers.Ask(DecisionKind.War, enemy.Name, $"Déclarer la guerre à {enemy.Name} ?",
                        $"Nous avons été conquis par {enemy.Name} et nous venons de reprendre notre liberté. Certains voudraient la venger par les armes ; "
                        + "la guerre ne cesserait qu'à la paix.",
                        () => Diplomacy.DeclareWar(world, colony, enemy), world.Clock, 20);
            }
            else
            {
                colony.Opinions[former] = Math.Max(Diplomacy.MinOpinion, colony.OpinionOf(former) - 20f);
                former.Opinions[colony] = Math.Max(Diplomacy.MinOpinion, former.OpinionOf(colony) - 20f);
            }
        }
        if (realm.MemberColonyIds.Count <= 1)
            Dissolve(world, realm);
    }

    /// <summary>Le royaume n'a plus que sa capitale : il se dissout, chacun redevient indépendant.</summary>
    public static void Dissolve(WorldState world, Realm realm)
    {
        foreach (Colony member in world.Colonies.Where(c => c.RealmId == realm.Id))
        {
            member.RealmId = 0;
            member.LowLoyaltyDays = 0;
            member.Loyalty = 1f;
        }
        world.Realms.Remove(realm);
    }

    // ---------- Territoire ----------

    /// <summary>Le royaume dont une case du monde fait partie : celui du propriétaire de la région (null si elle n'a pas de maître ou s'il est indépendant).</summary>
    internal static Realm? OfTile(WorldState world, int tile) =>
        world.Regions.TryGetValue(tile, out RegionState? region) && region.OwnerColonyId is int owner
            && world.Colonies.FirstOrDefault(c => c.Id == owner) is { } colony ? Of(world, colony) : null;
}
