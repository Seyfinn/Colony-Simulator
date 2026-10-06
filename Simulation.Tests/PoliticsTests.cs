using System.Reflection;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Lot C : le chef élu par les adultes, les royaumes (filiation, roi, loyauté, entretien, sécession) et la conquête.</summary>
public sealed class PoliticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GodColony-politics-tests-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(_directory, "world.gcsave");

    public void Dispose()
    {
        foreach (string suffix in new[] { "", ".bak", ".tmp" })
            if (File.Exists(SavePath + suffix)) File.Delete(SavePath + suffix);
        if (Directory.Exists(_directory)) Directory.Delete(_directory);
    }

    private static WorldState World(int colonies = 3, int colonists = 12, int seed = 12345) =>
        new(seed, startingColonists: colonists, migration: false, lifecycle: false, colonyCount: colonies, trade: false);

    private static void SetAxis(Colonist colonist, Axis axis, float value)
    {
        var values = (float[])typeof(Personality).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(colonist.Personality)!;
        values[(int)axis] = value;
    }

    private static Colonist Chief(WorldState world, Colony colony)
    {
        Leadership.Elect(world, colony);
        return Leadership.ChiefOf(colony)!;
    }

    private static Realm MakeRealm(WorldState world, Colony capital, params Colony[] others)
    {
        Realm realm = Realms.Found(world, capital);
        foreach (Colony other in others)
            Realms.Join(world, realm, other, 0.9f);
        return realm;
    }

    // ---------- Chef ----------

    [Fact]
    public void Seuls_les_adultes_votent_et_le_resultat_est_deterministe()
    {
        WorldState a = World(1);
        Colony colony = a.Colonies[0];
        Colonist child = colony.Members.First();
        child.GetType().GetProperty(nameof(Colonist.BirthTicks))!.SetValue(child, a.Clock.Ticks - TimeConstants.TicksPerYear / 2);
        Assert.Equal(LifeStage.Child, child.Stage);
        int electors = colony.Members.Count(m => m.Stage is LifeStage.Adult or LifeStage.Elder);

        Colonist chief = Chief(a, colony);
        Assert.NotEqual(child, chief);
        Assert.Contains(colony.Thoughts, t => t.Text.Contains($"voix sur {electors}") && t.Text.Contains(chief.Name));
        Assert.Equal(a.Clock.Ticks + Leadership.TermTicks, colony.NextElectionTicks);
        Assert.Equal(1, colony.ElectionCount);

        // Même graine, mêmes votes.
        WorldState c = World(1), d = World(1);
        Assert.Equal(Chief(c, c.Colonies[0]).Id, Chief(d, d.Colonies[0]).Id);
        Assert.Equal(c.Politics.Next(), d.Politics.Next());
    }

    [Fact]
    public void Le_renom_pese_dans_le_vote_et_se_gagne_par_les_exploits_et_se_perd_avec_le_temps()
    {
        WorldState world = World(1, 14);
        Colony colony = world.Colonies[0];
        Colonist famous = colony.Members.OrderBy(m => m.Id).Last();
        famous.Renown = 500f;
        Assert.Equal(famous.Id, Chief(world, colony).Id);

        // Les adultes gagnent un peu de renom chaque jour et en perdent 1 %.
        Colonist plain = colony.Members.First(m => m != famous);
        plain.Renown = 0f;
        Leadership.Daily(world, colony);
        Assert.True(plain.Renown > 0f && plain.Renown < 1f);
        float before = famous.Renown;
        Leadership.Daily(world, colony);
        Assert.True(famous.Renown < before + 1f && famous.Renown > before * 0.98f);
    }

    [Fact]
    public void Le_chef_est_reelu_a_sa_mort_a_la_fin_du_mandat_et_apres_une_crise_au_plus_une_fois_par_an()
    {
        WorldState world = World(1, 14);
        Colony colony = world.Colonies[0];
        Leadership.Daily(world, colony); // premier chef
        Colonist first = Leadership.ChiefOf(colony)!;
        Assert.Equal(1, colony.ElectionCount);
        Leadership.Daily(world, colony);
        Assert.Equal(1, colony.ElectionCount); // rien ne change en temps normal

        Lifecycle.Die(world, first, "maladie");
        Leadership.Daily(world, colony);
        Colonist second = Leadership.ChiefOf(colony)!;
        Assert.NotEqual(first, second);
        Assert.Equal(2, colony.ElectionCount);

        // Mandat échu : nouvelle élection, le chef peut être réélu.
        colony.NextElectionTicks = world.Clock.Ticks;
        Leadership.Daily(world, colony);
        Assert.Equal(3, colony.ElectionCount);
        Assert.NotNull(Leadership.ChiefOf(colony));

        // Une famine déclenche une élection de crise, mais pas deux dans l'année.
        colony.Sensors = ColonyBrain.Sense(colony, world.Clock) with { FoodDays = 0.1f };
        Leadership.Daily(world, colony);
        Assert.Equal(4, colony.ElectionCount);
        Leadership.Daily(world, colony);
        Assert.Equal(4, colony.ElectionCount);
        Assert.True(colony.LastCrisisElectionTicks is not null);
    }

    [Fact]
    public void La_personnalite_du_chef_inflechit_les_decisions_dans_les_bornes()
    {
        WorldState world = World(1, 12);
        Colony colony = world.Colonies[0];
        Colonist chief = Chief(world, colony);
        foreach (Axis axis in Personality.All) SetAxis(chief, axis, 0f);
        Assert.Equal(ChiefStance.Neutral, Leadership.Stance(colony));

        SetAxis(chief, Axis.Attachement, 1f); SetAxis(chief, Axis.Audace, -1f);
        SetAxis(chief, Axis.Temperament, 1f);
        SetAxis(chief, Axis.Sociabilite, 1f); SetAxis(chief, Axis.Curiosite, 1f);
        SetAxis(chief, Axis.Ardeur, 1f); SetAxis(chief, Axis.Ambition, 1f);
        ChiefStance cautious = Leadership.Stance(colony);
        Assert.Equal(1f, cautious.Prudence);
        Assert.Equal(0f, cautious.Bellicisme);
        Assert.Equal(1f, cautious.Commerce);
        Assert.Equal(1f, cautious.Batisseur);
        Assert.All(new[] { cautious.Prudence, cautious.Bellicisme, cautious.Commerce, cautious.Batisseur }, v => Assert.InRange(v, -1f, 1f));
        Assert.InRange(Leadership.StanceEffect, 0f, 0.2f);

        // Un chef prudent veut davantage de vivres : à stock égal, la pression sur la nourriture est plus forte que sous un chef audacieux.
        colony.Stock.TryTake(ResourceType.Grain, colony.Stock.Get(ResourceType.Grain));
        colony.Stock.Add(ResourceType.Grain, 40);
        float prudent = ColonyBrain.Sense(colony, world.Clock).FoodPressure;
        SetAxis(chief, Axis.Attachement, -1f); SetAxis(chief, Axis.Audace, 1f);
        float bold = ColonyBrain.Sense(colony, world.Clock).FoodPressure;
        Assert.True(prudent > bold, $"{prudent} > {bold}");
        Assert.Equal(-1f, Leadership.Stance(colony).Prudence);
    }

    [Fact]
    public void Les_decisions_graves_restent_des_prieres_au_joueur()
    {
        WorldState world = World(2, 12);
        Colony a = world.Colonies[0], b = world.Colonies[1];
        Chief(world, a);
        // Un chef belliqueux abaisse le seuil, mais ne déclare jamais la guerre sans prière : aucun pacte n'apparaît tout seul.
        foreach (Axis axis in new[] { Axis.Audace, Axis.Temperament }) SetAxis(Leadership.ChiefOf(a)!, axis, 1f);
        a.Opinions[b] = -80f;
        for (int day = 0; day < 20; day++) Diplomacy.Daily(world);
        Assert.False(Diplomacy.AtWar(world, a, b));
        Assert.All(a.Prayers.Pending, p => Assert.Equal(PrayerStatus.Pending, p.Status));
    }

    // ---------- Royaumes ----------

    [Fact]
    public void Une_fille_de_schisme_entre_dans_le_royaume_de_sa_mere_sans_rien_lui_devoir()
    {
        WorldState world = World(1, 24);
        Colony mother = world.Colonies[0];
        List<Colonist> people = mother.Members.OrderBy(m => m.Id).ToList();
        Colonist leader = people[0];
        SetAxis(leader, Axis.Ambition, 0.9f); SetAxis(leader, Axis.Attachement, -0.5f);
        foreach (Colonist follower in people.Skip(1).Take(8)) { SetAxis(follower, Axis.Attachement, -0.6f); leader.Friends.Add(follower.Id); }
        Colony? daughter = Schism.Split(world, mother, leader.Id);
        Assert.NotNull(daughter);

        Realm realm = Assert.Single(world.Realms);
        Assert.Equal(mother.Id, realm.CapitalColonyId);
        Assert.Equal([mother.Id, daughter!.Id], realm.MemberColonyIds);
        Assert.Equal(realm.Id, mother.RealmId);
        Assert.Equal(realm.Id, daughter.RealmId);
        Assert.Equal(Realms.DaughterLoyalty, daughter.Loyalty);
        Assert.StartsWith("Royaume de ", realm.Name);
        Assert.Equal(realm, world.RealmOfTile(world.WorldMap.TileOf(daughter)));
        Assert.Contains(daughter.Thoughts, t => t.Text.Contains("sans rien devoir"));

        // Aucun dû : ni tribut, ni stock partagé, ni mariages imposés.
        int coins = mother.Stock.Get(ResourceType.Coins) + daughter.Stock.Get(ResourceType.Coins);
        Realms.Daily(world);
        Assert.Equal(coins, mother.Stock.Get(ResourceType.Coins) + daughter.Stock.Get(ResourceType.Coins));
        Assert.NotSame(mother.Stock, daughter.Stock);
    }

    [Fact]
    public void Les_membres_d_un_royaume_se_traitent_en_allies_sans_pacte()
    {
        WorldState world = World(3);
        Colony a = world.Colonies[0], b = world.Colonies[1], c = world.Colonies[2];
        MakeRealm(world, a, b);
        Assert.True(Diplomacy.AreAllied(world, a, b));
        Assert.Contains(b, Diplomacy.Allies(world, a));
        Assert.True(Diplomacy.PassageAllowed(world, a, b));
        Assert.False(Diplomacy.AreAllied(world, a, c));
        Assert.Null(Diplomacy.PactBetween(world, a, b));

        // Pas de guerre entre membres, même sur ordre.
        Diplomacy.DeclareWar(world, a, b);
        Assert.False(Diplomacy.AtWar(world, a, b));
        Diplomacy.SealAlliance(world, a, b);
        Assert.Empty(world.Pacts);
        // Les renforts de la défense comptent les membres.
        Assert.True(Diplomacy.AlliedHelp(world, a, attacker: c) >= 0f);
    }

    [Fact]
    public void Le_roi_est_elu_par_les_chefs_des_colonies_membres()
    {
        WorldState world = World(3);
        Colony a = world.Colonies[0], b = world.Colonies[1], c = world.Colonies[2];
        foreach (Colony colony in world.Colonies) Chief(world, colony);
        Colonist star = Leadership.ChiefOf(c)!;
        star.Renown = 900f;
        Realm realm = MakeRealm(world, a, b, c);
        Colonist? king = Realms.ElectKing(world, realm);
        Assert.NotNull(king);
        Assert.Equal(star.Id, king!.Id);
        Assert.Equal(star.Id, realm.KingColonistId);
        Assert.Equal(c.Id, realm.CapitalColonyId); // la capitale suit le roi
        Assert.Equal(king, Realms.King(world, realm));
        Assert.Equal(world.Clock.Ticks + Realms.KingTermTicks, realm.NextKingElectionTicks);
        Assert.Contains(a.Thoughts, t => t.Text.Contains(star.Name) && t.Text.Contains("voix sur 3"));

        // Mort du roi : le royaume en élit un autre dès le jour suivant.
        Lifecycle.Die(world, star, "maladie");
        Leadership.Elect(world, c);
        Realms.Daily(world);
        Assert.NotEqual(star.Id, realm.KingColonistId);
        Assert.NotNull(Realms.King(world, realm));
    }

    [Fact]
    public void La_loyaute_baisse_avec_la_distance_et_la_taille_du_royaume()
    {
        WorldState world = World(4, 10);
        Colony capital = world.Colonies[0];
        Realm realm = MakeRealm(world, capital, world.Colonies.Skip(1).ToArray());
        List<Colony> byDistance = world.Colonies.Skip(1).OrderBy(c => Realms.Distance(world, c, capital)).ToList();
        Colony near = byDistance[0], far = byDistance[^1];
        near.Opinions[capital] = 0f; far.Opinions[capital] = 0f;
        float nearTarget = Realms.LoyaltyTarget(world, realm, capital, near, 4, true);
        float farTarget = Realms.LoyaltyTarget(world, realm, capital, far, 4, true);
        if (Realms.Distance(world, far, capital) > Realms.Distance(world, near, capital))
            Assert.True(farTarget < nearTarget, $"{farTarget} < {nearTarget}");
        Assert.True(Realms.LoyaltyTarget(world, realm, capital, near, 8, true) < Realms.LoyaltyTarget(world, realm, capital, near, 2, true));
        // L'entretien impayé, une conquête récente et une opinion exécrable pèsent aussi.
        Assert.True(Realms.LoyaltyTarget(world, realm, capital, near, 4, false) < nearTarget);
        near.GetType().GetProperty(nameof(Colony.ConqueredTicks))!.SetValue(near, world.Clock.Ticks);
        Assert.True(Realms.LoyaltyTarget(world, realm, capital, near, 4, true) < nearTarget - 0.2f);
        near.Opinions[capital] = -100f;
        Assert.True(Realms.LoyaltyTarget(world, realm, capital, near, 4, true) < nearTarget - 0.4f);
    }

    [Fact]
    public void L_entretien_est_une_consommation_de_la_capitale_jamais_un_transfert_vers_les_membres()
    {
        WorldState world = World(3, 12);
        Colony capital = world.Colonies[0];
        Realm realm = MakeRealm(world, capital, world.Colonies[1], world.Colonies[2]);
        capital.Sensors = ColonyBrain.Sense(capital, world.Clock) with { FoodDays = 30f, FoodPressure = 0f, HeatingPressure = 0f };
        capital.Stock.Add(ResourceType.Bread, 500);
        capital.Stock.Add(ResourceType.Wood, 500);
        (int food, int wood) = Realms.UpkeepCost(world, capital, Realms.Members(world, realm));
        Assert.True(food > 0 && wood > 0);
        int memberFood = world.Colonies[1].Stock.FoodUnits + world.Colonies[2].Stock.FoodUnits;
        int grain = capital.Stock.Get(ResourceType.Bread), woodBefore = capital.Stock.Get(ResourceType.Wood);
        int usageBefore = (int)ResourceAccounting.Total(capital.PrimarySettlement.Stock, ResourceType.Bread, ResourceFlow.Usage);
        Assert.True(Realms.PayUpkeep(world, realm, capital, Realms.Members(world, realm).ToList()));
        Assert.Equal(grain - food, capital.Stock.Get(ResourceType.Bread));
        Assert.Equal(woodBefore - wood, capital.Stock.Get(ResourceType.Wood));
        Assert.Equal(memberFood, world.Colonies[1].Stock.FoodUnits + world.Colonies[2].Stock.FoodUnits);
        Assert.True(ResourceAccounting.Total(capital.PrimarySettlement.Stock, ResourceType.Bread, ResourceFlow.Usage) >= usageBefore + food);

        // Faute de réserves de survie, l'entretien n'est pas payé : la loyauté en souffre.
        capital.Stock.TryTake(ResourceType.Bread, capital.Stock.Get(ResourceType.Bread));
        capital.Stock.TryTake(ResourceType.Wood, capital.Stock.Get(ResourceType.Wood));
        Assert.False(Realms.PayUpkeep(world, realm, capital, Realms.Members(world, realm).ToList()));
        Assert.Contains(capital.Thoughts, t => t.Text.Contains("Entretien non payé"));
    }

    [Fact]
    public void Une_colonie_peu_attachee_fait_secession_apres_dix_jours_et_le_royaume_se_dissout()
    {
        WorldState world = World(2, 12);
        Colony capital = world.Colonies[0], member = world.Colonies[1];
        Realm realm = MakeRealm(world, capital, member);
        Chief(world, capital); Chief(world, member);
        Realms.ElectKing(world, realm);
        // Une colonie conquise, qui déteste la capitale, dont l'entretien n'est pas payé : sa loyauté s'effondre.
        member.GetType().GetProperty(nameof(Colony.ConqueredTicks))!.SetValue(member, world.Clock.Ticks);
        member.Opinions[capital] = -100f;
        member.Loyalty = 0.05f;
        capital.Sensors = ColonyBrain.Sense(capital, world.Clock) with { FoodDays = 0.2f };
        for (int day = 0; day < Realms.SecessionDays - 1; day++)
        {
            Realms.Daily(world);
            Assert.Equal(realm.Id, member.RealmId);
        }
        Assert.True(member.LowLoyaltyDays >= Realms.SecessionDays - 2);
        for (int day = 0; day < 4 && member.RealmId != 0; day++) Realms.Daily(world);
        Assert.Equal(0, member.RealmId);
        Assert.Contains(member.Thoughts, t => t.Text.Contains("indépendance"));
        // Une colonie conquise peut en prier le joueur de reprendre les armes ; la décision lui revient.
        Assert.Contains(member.Prayers.Pending, p => p.Kind == DecisionKind.War);
        Assert.False(Diplomacy.AtWar(world, member, capital));
        // Réduit à sa seule capitale, le royaume est dissous.
        Assert.Empty(world.Realms);
        Assert.Equal(0, capital.RealmId);
    }

    [Fact]
    public void Un_royaume_reduit_a_sa_capitale_se_dissout_et_une_colonie_qui_part_entraine_ses_voisines_peu_loyales()
    {
        WorldState world = World(4, 10);
        Colony a = world.Colonies[0], b = world.Colonies[1], c = world.Colonies[2], d = world.Colonies[3];
        Realm lone = MakeRealm(world, a);
        Realms.Daily(world);
        Assert.DoesNotContain(lone, world.Realms);
        Assert.Equal(0, a.RealmId);

        Realm realm = MakeRealm(world, a, b, c, d);
        foreach (Colony colony in new[] { b, c }) colony.Loyalty = 0.2f;
        b.Opinions[a] = 0f;
        Realms.Secede(world, realm, b);
        Assert.Equal(0, b.RealmId == realm.Id ? 1 : 0);
        Assert.DoesNotContain(b.Id, realm.MemberColonyIds);
        // Les membres voisins dont la loyauté est sous 0,35 la suivent dans un nouveau royaume (si elle en est assez proche).
        foreach (Colony follower in new[] { c, d }.Where(x => x.RealmId != realm.Id && x.RealmId != 0))
            Assert.Equal(b.RealmId, follower.RealmId);
        Assert.True(world.Realms.Count <= 2);
    }

    // ---------- Conquête ----------

    private static (WorldState World, Colony Attacker, Colony Defender, Colony Refuge, WarParty Party) ConquestWorld(int defenders = 20)
    {
        WorldState world = World(3, defenders);
        Colony attacker = world.Colonies[0], defender = world.Colonies[1], refuge = world.Colonies[2];
        attacker.Sensors = ColonyBrain.Sense(attacker, world.Clock) with { FoodDays = 30f, FoodPressure = 0f, HeatingPressure = 0f };
        attacker.BattlesWon = 2;
        world.Pacts.Add(new Pact(attacker, defender, PactKind.War, world.Clock.Ticks));
        // Presque tous les défenseurs adultes sont blessés.
        foreach (Colonist colonist in defender.Members.Where(m => m.Stage == LifeStage.Adult).OrderBy(m => m.Id).Skip(2))
            colonist.Ailment = Ailment.Injured;
        var warriors = attacker.Members.OrderBy(m => m.Id).Take(5).ToList();
        var party = new WarParty(attacker, defender, warriors, 0, world.Clock.Ticks, world.Clock.Ticks + 100);
        return (world, attacker, defender, refuge, party);
    }

    [Fact]
    public void Sans_toutes_les_conditions_la_bataille_ne_cause_que_le_pillage()
    {
        (WorldState world, Colony attacker, Colony defender, _, WarParty party) = ConquestWorld();
        Assert.True(Conquest.CanConquer(world, attacker, defender, 10f, 1f));
        Assert.False(Conquest.CanConquer(world, attacker, defender, 1.9f, 1f), "victoire pas assez nette");
        attacker.BattlesWon = 1;
        Assert.False(Conquest.CanConquer(world, attacker, defender, 10f, 1f), "deux batailles gagnées sont requises");
        attacker.BattlesWon = 2;
        attacker.Sensors = attacker.Sensors! with { FoodDays = 3f };
        Assert.False(Conquest.CanConquer(world, attacker, defender, 10f, 1f), "dix jours de vivres sont requis");
        attacker.Sensors = attacker.Sensors with { FoodDays = 30f };
        foreach (Colonist colonist in defender.Members) colonist.Ailment = Ailment.None;
        Assert.False(Conquest.CanConquer(world, attacker, defender, 10f, 1f), "il reste trop de défenseurs valides");

        int people = defender.Members.Count;
        Assert.False(Conquest.TryConquer(world, attacker, defender, party, 1.5f, 1f));
        Assert.Equal(people, defender.Members.Count);
        Assert.Equal(0, defender.RealmId);
        Assert.Equal(0, attacker.RealmId);
    }

    [Fact]
    public void Une_capitale_de_royaume_de_plus_de_trois_membres_ne_tombe_pas_par_conquete()
    {
        (WorldState world, Colony attacker, Colony defender, Colony refuge, _) = ConquestWorld();
        Realm realm = MakeRealm(world, defender, refuge);
        realm.MemberColonyIds.AddRange([901, 902, 903]);
        Assert.False(Conquest.CanConquer(world, attacker, defender, 10f, 1f));
        realm.MemberColonyIds.RemoveRange(2, 3);
        Assert.True(Conquest.CanConquer(world, attacker, defender, 10f, 1f));
    }

    [Fact]
    public void Une_conquete_tue_fait_fuir_et_assujettit_sans_rien_creer_ni_perdre_hors_les_morts()
    {
        (WorldState world, Colony attacker, Colony defender, Colony refuge, WarParty party) = ConquestWorld(24);
        string name = defender.Name;
        Species species = defender.Species;
        int people = defender.Members.Count;
        int worldPeople = world.Colonies.Sum(c => c.Members.Count + c.Transients.Count);
        int goods = world.Colonies.Sum(c => c.Stock.Amounts.Where(p => p.Key != ResourceType.Coins).Sum(p => p.Value));
        int coins = world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins));
        int renownBefore = (int)party.Warriors.Sum(w => w.Renown);
        int graves = defender.Deaths.Count;

        Assert.True(Conquest.TryConquer(world, attacker, defender, party, 10f, 1f));

        // La colonie garde son nom et son peuple, mais entre dans le royaume du vainqueur.
        Assert.Equal(name, defender.Name);
        Assert.Equal(species, defender.Species);
        Realm realm = Assert.Single(world.Realms);
        Assert.Equal(attacker.Id, realm.CapitalColonyId);
        Assert.Equal(realm.Id, defender.RealmId);
        Assert.Equal(realm.Id, attacker.RealmId);
        Assert.Equal(Realms.ConqueredLoyalty, defender.Loyalty);
        Assert.NotNull(defender.ConqueredTicks);
        Assert.Empty(world.Pacts); // la guerre entre les deux est finie
        Assert.Equal(20, attacker.Prestige);
        Assert.Equal(renownBefore + 5 * party.Warriors.Count, (int)party.Warriors.Sum(w => w.Renown));
        Assert.True(defender.ElectionCount >= 1); // le chef du conquis est réélu aussitôt
        Assert.Contains(defender.Thoughts, t => t.Text.Contains("conquise"));

        // Morts, fuyards, restants : dans les fourchettes, sans personne de créé.
        int dead = defender.Deaths.Count - graves;
        Assert.InRange(dead, (int)MathF.Round(people * Conquest.DeadMin) - 1, (int)MathF.Round(people * (Conquest.DeadMin + Conquest.DeadSpan)) + 1);
        int fled = people - dead - defender.Members.Count;
        Assert.InRange(fled, (int)MathF.Round(people * Conquest.FleeMin) - 1, (int)MathF.Round(people * (Conquest.FleeMin + Conquest.FleeSpan)) + 1);
        Assert.InRange(defender.Members.Count, people * 0.45, people * 0.72);
        Assert.Equal(worldPeople - dead, world.Colonies.Sum(c => c.Members.Count + c.Transients.Count));

        // Les biens des fuyards partent avec eux (transfert), ceux des morts restent au stock : rien de perdu, rien de créé.
        Assert.Equal(goods, world.Colonies.Sum(c => c.Stock.Amounts.Where(p => p.Key != ResourceType.Coins).Sum(p => p.Value)));
        Assert.Equal(coins, world.Colonies.Sum(c => c.Stock.Get(ResourceType.Coins)));
    }

    [Fact]
    public void Un_groupe_de_refugies_peut_fonder_une_colonie_independante()
    {
        (WorldState world, Colony attacker, Colony defender, _, WarParty party) = ConquestWorld(28);
        int colonies = world.Colonies.Count;
        Assert.True(Conquest.TryConquer(world, attacker, defender, party, 10f, 1f));
        // Soit les fuyards ont fondé une colonie sans mère ni royaume, soit ils ont rejoint une voisine indépendante : jamais chez le vainqueur.
        foreach (Colony colony in world.Colonies.Skip(colonies))
        {
            Assert.Null(colony.Parent);
            Assert.Equal(0, colony.RealmId);
            Assert.NotEqual(attacker, colony);
        }
        Assert.All(world.Colonies.Where(c => c != defender && c != attacker && c.Transients.Any(t => t.Transit == TransitState.Arriving)),
            c => Assert.Equal(0, c.RealmId));
    }

    // ---------- Territoire et sauvegarde ----------

    [Fact]
    public void Le_territoire_d_une_colonie_est_dans_son_royaume_et_une_colonie_independante_n_en_a_pas()
    {
        WorldState world = World(3);
        Colony a = world.Colonies[0], b = world.Colonies[1], c = world.Colonies[2];
        Realm realm = MakeRealm(world, a, b);
        Assert.Equal(realm, world.RealmOfTile(world.WorldMap.TileOf(a)));
        Assert.Equal(realm, world.RealmOfTile(world.WorldMap.TileOf(b)));
        Assert.Null(world.RealmOfTile(world.WorldMap.TileOf(c)));
        Assert.Null(world.RealmOfTile(-5));
        Assert.Equal("fidèle", Realms.LoyaltyWord(b));
        b.Loyalty = 0.5f;
        Assert.Equal("hésitante", Realms.LoyaltyWord(b));
        b.Loyalty = 0.1f;
        Assert.Equal("au bord de la sécession", Realms.LoyaltyWord(b));
        Assert.Contains("chef", Leadership.Describe(a, world.Clock));
        Chief(world, a);
        Assert.Contains(Leadership.ChiefOf(a)!.FullName, Leadership.Describe(a, world.Clock));
    }

    [Fact]
    public void Un_monde_avec_royaumes_chefs_et_loyautes_se_sauvegarde_et_se_recharge_a_l_identique()
    {
        WorldState world = World(3, 12);
        foreach (Colony colony in world.Colonies) Chief(world, colony);
        Realm realm = MakeRealm(world, world.Colonies[0], world.Colonies[1]);
        Realms.ElectKing(world, realm);
        world.Colonies[1].Loyalty = 0.6f;
        world.Colonies[1].GetType().GetProperty(nameof(Colony.ConqueredTicks))!.SetValue(world.Colonies[1], world.Clock.Ticks);
        for (int i = 0; i < 2 * TimeConstants.TicksPerDay; i++) world.Step();
        WorldSave.Save(SavePath, world);
        WorldState loaded = WorldSave.Load(SavePath).World;
        Assert.Null(new WorldComparison().Difference(world, loaded));
        Assert.Single(loaded.Realms);
        Assert.Equal(realm.KingColonistId, loaded.Realms[0].KingColonistId);
        Assert.Equal(0.6f, loaded.Colonies[1].Loyalty, 1);
        Assert.NotNull(loaded.Colonies[1].ConqueredTicks);
        for (int i = 0; i < 3 * TimeConstants.TicksPerDay; i++) { world.Step(); loaded.Step(); }
        Assert.Null(new WorldComparison().Difference(world, loaded));
    }
}
