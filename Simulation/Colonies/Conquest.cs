using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// La conquête : rare et coûteuse, elle suit une bataille gagnée sans appel par l'attaquant. Une partie des habitants meurt, une partie fuit, le reste change d'allégeance :
/// la colonie garde son nom, son peuple et ses habitudes, mais entre dans le royaume du vainqueur (créé au besoin), peu loyale pour longtemps. Le hasard vient de
/// <see cref="WorldState.Politics"/>, les habitants sont parcourus dans l'ordre de leur identifiant.
/// </summary>
public static class Conquest
{
    /// <summary>Victoire nette : la force de l'attaque vaut au moins deux fois celle de la défense.</summary>
    public const float DecisiveRatio = 2f;

    /// <summary>Il ne reste plus que 40 % des adultes valides au vaincu.</summary>
    public const float MaxRemainingShare = 0.4f;

    /// <summary>L'attaquant a gagné deux batailles dans cette guerre.</summary>
    public const int MinBattlesWon = 2;

    /// <summary>Jours de vivres que l'attaquant doit avoir pour oser administrer un conquis.</summary>
    public const float MinFoodDays = 10f;

    /// <summary>Un royaume dont la capitale compte plus de trois membres ne tombe pas par une conquête : trop rare, les batailles ordinaires suffisent.</summary>
    public const int MaxCapitalRealmSize = 3;

    /// <summary>Part des habitants qui meurent (10 à 20 %) ou fuient (20 à 30 %).</summary>
    public const float DeadMin = 0.10f, DeadSpan = 0.10f, FleeMin = 0.20f, FleeSpan = 0.10f;

    /// <summary>Au moins ce nombre d'adultes fuyant ensemble peuvent fonder une nouvelle colonie.</summary>
    public const int MinFoundingRefugees = 6;

    /// <summary>
    /// Le vainqueur a-t-il les moyens d'administrer une colonie de plus ? Il lui faut dix jours de vivres, et l'entretien du royaume (vivres par jour, distance comprise)
    /// ne doit pas dépasser le quart de ce que sa population mange.
    /// </summary>
    public static bool CanSupport(WorldState world, Colony attacker, Colony defender)
    {
        if (attacker.Sensors is not { } sensors || sensors.FoodDays < MinFoodDays)
            return false;
        Realm? realm = Realms.Of(world, attacker);
        IEnumerable<Colony> members = realm is null ? [attacker] : Realms.Members(world, realm);
        (int food, _) = Realms.UpkeepCost(world, attacker, members.Append(defender).Distinct());
        return food <= 0.25f * attacker.PresentMembers.Count * ColonyBrain.MealsPerColonistPerDay;
    }

    /// <summary>Les conditions de la conquête, toutes ensemble ; sinon le pillage habituel reste la seule issue.</summary>
    public static bool CanConquer(WorldState world, Colony attacker, Colony defender, float attack, float defense)
    {
        if (attack < DecisiveRatio * defense || attacker.BattlesWon < MinBattlesWon || Realms.SameRealm(world, attacker, defender))
            return false;
        int adults = defender.PresentMembers.Count(m => m.Stage == LifeStage.Adult);
        if (adults == 0 || Warfare.HealthyAdults(defender) > MaxRemainingShare * adults)
            return false;
        if (Realms.Of(world, defender) is { } held && held.CapitalColonyId == defender.Id && held.MemberColonyIds.Count > MaxCapitalRealmSize)
            return false;
        return CanSupport(world, attacker, defender);
    }

    /// <summary>
    /// La bataille vient d'être gagnée : si les conditions sont réunies, la colonie est conquise. Renvoie vrai dans ce cas.
    /// </summary>
    internal static bool TryConquer(WorldState world, Colony attacker, Colony defender, WarParty party, float attack, float defense)
    {
        if (!CanConquer(world, attacker, defender, attack, defense))
            return false;
        Random luck = world.Politics;
        int people = defender.Members.Count;

        // Les morts : 10 à 20 % des habitants, les adultes d'abord, tirés dans l'ordre des identifiants.
        int dead = (int)MathF.Round(people * (DeadMin + DeadSpan * luck.NextSingle()));
        List<Colonist> pool = defender.Members.OrderBy(m => m.Id).ToList();
        List<Colonist> victims = pool.Where(m => m.Stage is LifeStage.Adult or LifeStage.Teen).OrderBy(_ => luck.Next()).Take(dead).OrderBy(m => m.Id).ToList();
        foreach (Colonist victim in victims)
            Lifecycle.Die(world, victim, "tombé lors de la conquête");

        // Les fuyards : 20 à 30 % des habitants, vers une colonie indépendante proche, ou en groupe pour fonder une nouvelle colonie.
        int fleeing = (int)MathF.Round(people * (FleeMin + FleeSpan * luck.NextSingle()));
        List<Colonist> fugitives = defender.PresentMembers.Where(m => m.Transit == TransitState.None).OrderBy(m => m.Id).ToList()
            .OrderBy(_ => luck.Next()).Take(fleeing).OrderBy(m => m.Id).ToList();
        int fled = Flee(world, attacker, defender, fugitives);

        // Les restants changent d'allégeance : ils n'aiment pas leurs vainqueurs.
        foreach (Colonist stays in defender.Members)
            stays.Needs.Grief = Math.Max(stays.Needs.Grief, 0.3f);
        defender.Opinions[attacker] = Math.Max(Diplomacy.MinOpinion, defender.OpinionOf(attacker) - 30f);

        Realm realm = Realms.Of(world, attacker) ?? Realms.Found(world, attacker);
        Realms.Join(world, realm, defender, Realms.ConqueredLoyalty);
        defender.ConqueredTicks = world.Clock.Ticks;
        if (world.Pacts.FirstOrDefault(p => p.Kind == PactKind.War && p.Between(attacker, defender)) is { } war)
            world.Pacts.Remove(war);
        attacker.BattlesWon = attacker.BattlesLost = defender.BattlesWon = defender.BattlesLost = 0;
        attacker.Prestige += 20;
        foreach (Colonist warrior in party.Warriors)
            warrior.Renown += 5f;
        Leadership.Elect(world, defender);
        // La capitale reste le vainqueur : un royaume né de la conquête a pour roi le chef du conquérant (les prochaines élections royales suivront le calendrier).
        if (realm.KingColonistId == 0 && Leadership.ChiefOf(attacker) is { } victor)
            realm.KingColonistId = victor.Id;
        ColonyBrain.Say(defender, world.Clock, $"⚔ {defender.Name} est conquise par {attacker.Name} : {victims.Count} des nôtres sont tombés, {fled} ont fui. "
            + $"Nous gardons notre nom et nos habitudes, mais nous entrons dans le {realm.Name}.");
        ColonyBrain.Say(attacker, world.Clock, $"⚔ {defender.Name} est conquise : elle entre dans le {realm.Name}. Il faudra payer son entretien, et sa loyauté reste à gagner.");
        return true;
    }

    /// <summary>
    /// Les fuyards partent : un groupe d'au moins six adultes fonde une nouvelle colonie indépendante si une région est libre à proximité, sinon chacun rejoint la colonie
    /// indépendante la plus proche (hors du royaume du vainqueur). Chacun emporte ce qu'il peut porter : une part des vivres, prélevée sur le stock du vaincu.
    /// Renvoie le nombre de fuyards qui ont pu partir.
    /// </summary>
    private static int Flee(WorldState world, Colony attacker, Colony defender, List<Colonist> fugitives)
    {
        if (fugitives.Count == 0)
            return 0;
        int adults = fugitives.Count(f => f.Stage == LifeStage.Adult);
        if (adults >= MinFoundingRefugees && world.Colonies.Count < WorldState.MaxPlayerColonies && Schism.FindRegion(world, defender) >= 0
            && fugitives.Where(f => f.Stage == LifeStage.Adult).OrderByDescending(f => f.Renown).ThenBy(f => f.Id).FirstOrDefault() is { } leader
            && Schism.FoundFrom(world, defender, leader, fugitives, refugees: true) is not null)
            return fugitives.Count;

        Colony? refuge = world.Colonies.Where(c => c != defender && c != attacker && c.Members.Count > 0 && !Realms.SameRealm(world, c, attacker)
                && world.WorldMap.Connected(defender, c) && !Diplomacy.AtWar(world, defender, c))
            .OrderBy(c => Realms.Distance(world, defender, c)).ThenBy(c => c.Id).FirstOrDefault();
        if (refuge is null)
            return 0;
        int moved = 0;
        foreach (Colonist fugitive in fugitives)
        {
            if (Migration.FindEdgePoint(world, refuge, refuge.CampX, refuge.CampY) is not { } entry)
                break;
            ColonistAI.DetachFromColony(fugitive);
            defender.Transients.Remove(fugitive);
            if (fugitive.Partner is { } partner)
            {
                partner.Partner = null;
                partner.Needs.Grief = Math.Max(partner.Needs.Grief, 0.5f);
                fugitive.Partner = null;
            }
            fugitive.Colony = refuge;
            fugitive.HomeSettlementId = 0;
            fugitive.LocationSettlementId = refuge.PrimarySettlementId;
            fugitive.TravelId = 0;
            fugitive.X = fugitive.PrevX = entry.X + 0.5f;
            fugitive.Y = fugitive.PrevY = entry.Y + 0.5f;
            fugitive.Transit = TransitState.Arriving;
            fugitive.ReturningTrader = false;
            fugitive.Needs.Food = Math.Min(fugitive.Needs.Food, 0.5f);
            refuge.Transients.Add(fugitive);
            moved++;
        }
        // Ce qu'ils emportent : une part portable des vivres du vaincu, transférée au refuge.
        int carry = moved * Diplomacy.LootPerWarrior / 2;
        foreach (ResourceType good in new[] { ResourceType.Grain, ResourceType.Bread, ResourceType.SaltedMeat, ResourceType.Food })
        {
            int units = Math.Min(carry, defender.Stock.Available(good) / 4);
            if (units > 0 && defender.Stock.TryTransferTo(refuge.Stock, good, units))
                carry -= units;
        }
        if (moved > 0)
            ColonyBrain.Say(refuge, world.Clock, $"{moved} réfugiés de {defender.Name} frappent à notre porte : chassés par la conquête, ils s'installent chez nous.");
        return moved;
    }
}
