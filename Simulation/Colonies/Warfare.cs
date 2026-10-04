using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>
/// La guerre entre deux colonies : tous les quatre jours au plus, chaque camp qui s'en sent la force envoie une bande de guerriers
/// (un adulte valide sur trois, de 2 à 8, les plus téméraires d'abord, armés d'outils de fer s'il y en a). Elle marche jusqu'à
/// l'ennemi et livre bataille : les guerriers comptent pour un (1,6 armés), les défenseurs restés au village pour 0,6 (plus leurs
/// outils), les fortifications et l'art de la guerre pèsent, les alliés envoient des renforts. Vainqueurs, les guerriers pillent
/// pièces, outils et vivres, tuent ou blessent des défenseurs ; vaincus, ils laissent des morts sur le terrain.
/// </summary>
public static class Warfare
{
    private const float WarriorStrength = 1f;
    private const float WeaponStrength = 0.6f;
    private const float DefenderStrength = 0.6f;
    private const float DefenderToolStrength = 0.5f;

    public static int HealthyAdults(Colony colony) =>
        colony.Members.Count(m => m.Stage == LifeStage.Adult && m.Ailment == Ailment.None);

    /// <summary>Combien de guerriers la colonie peut envoyer : un adulte valide sur trois, de 2 à 8.</summary>
    public static int WarriorCount(Colony colony) =>
        Math.Clamp(HealthyAdults(colony) / 3, Diplomacy.MinWarriors, Diplomacy.MaxWarriors);

    /// <summary>La force qu'aurait sa prochaine bande de guerriers (sans le hasard de la bataille).</summary>
    public static float AttackEstimate(Colony colony)
    {
        int warriors = WarriorCount(colony);
        int armed = Math.Min(warriors, colony.Stock.Get(ResourceType.Tools));
        return Strength(colony, warriors, armed);
    }

    private static float Strength(Colony colony, int warriors, int armed) =>
        (warriors * WarriorStrength + armed * WeaponStrength) * (Knowledge.Has(colony, Discovery.Warfare) ? Knowledge.WarfareFactor : 1f);

    /// <summary>La force de ceux qui défendent la colonie, renforts des alliés compris (sans le hasard de la bataille).</summary>
    public static float DefenseEstimate(WorldState world, Colony colony, Colony? attacker)
    {
        int defenders = HealthyAdults(colony);
        float strength = defenders * DefenderStrength + DefenderToolStrength * Math.Min(colony.Stock.Get(ResourceType.Tools), defenders);
        if (Knowledge.Has(colony, Discovery.Fortification))
            strength *= Knowledge.FortificationFactor;
        return strength + Diplomacy.AlliedHelp(world, colony, attacker);
    }

    // ---------- Départ ----------

    /// <summary>Chaque matin, un camp en guerre envoie une bande s'il le peut et s'il pense pouvoir l'emporter (l'agresseur attaque toujours).</summary>
    internal static void LaunchParty(WorldState world, Colony colony)
    {
        long now = world.Clock.Ticks;
        if (world.WarParties.Any(p => p.From == colony) || now - colony.LastWarPartyTicks < Diplomacy.DaysBetweenWarParties * TimeConstants.TicksPerDay
            || HealthyAdults(colony) < Diplomacy.MinHealthyAdultsForWar || !(colony.Sensors?.SurvivalAssured ?? false))
            return;
        foreach (Pact war in world.Pacts.Where(p => p.Kind == PactKind.War && p.Involves(colony)))
        {
            Colony enemy = war.Other(colony);
            bool aggressor = war.A == colony;
            if (!world.WorldMap.Connected(colony, enemy)
                || (!aggressor && AttackEstimate(colony) < 0.9f * DefenseEstimate(world, enemy, colony)))
                continue;
            Depart(world, colony, enemy);
            return;
        }
    }

    private static List<Colonist> PickWarriors(Colony colony) =>
        colony.Members
            .Where(m => m.Stage == LifeStage.Adult && m.Ailment == Ailment.None && m.PregnantUntilTicks is null && m.Transit == TransitState.None)
            .OrderByDescending(m => m.Personality[Axis.Audace] + m.Personality[Axis.Temperament])
            .ThenBy(m => m.Id)
            .Take(WarriorCount(colony))
            .ToList();

    public static WarParty? Depart(WorldState world, Colony from, Colony to)
    {
        List<Colonist> warriors = PickWarriors(from);
        if (warriors.Count < Diplomacy.MinWarriors)
            return null;
        int weapons = Math.Min(warriors.Count, from.Stock.Get(ResourceType.Tools));
        from.Stock.TryTake(ResourceType.Tools, weapons, ResourceFlow.Transfer);
        long now = world.Clock.Ticks;
        long oneWay = Math.Max((long)TimeConstants.TicksPerHour, (long)(world.WorldMap.TravelDays(from, to) * TimeConstants.TicksPerDay));
        var party = new WarParty(from, to, warriors, weapons, now, now + oneWay);
        foreach (Colonist warrior in warriors)
        {
            ColonistAI.DetachFromColony(warrior);
            if (Migration.FindEdgePoint(world, from, warrior.TileX, warrior.TileY) is not null)
            {
                warrior.Transit = TransitState.Leaving;
                from.Transients.Add(warrior);
            }
        }
        from.LastWarPartyTicks = now;
        world.WarParties.Add(party);
        ColonyBrain.Say(from, world.Clock, $"⚔ {warriors.Count} guerriers partent attaquer {to.Name}" + (weapons > 0 ? $", armés de {weapons} outils de fer." : "."));
        return party;
    }

    // ---------- En route ----------

    /// <summary>Chaque heure : les bandes arrivent et livrent bataille, puis rentrent.</summary>
    public static void Hourly(WorldState world)
    {
        long now = world.Clock.Ticks;
        foreach (WarParty party in world.WarParties.ToList())
        {
            if (party.State == WarPartyState.Outbound && now >= party.ArriveTicks)
            {
                if (Diplomacy.AtWar(world, party.From, party.To) && party.To.Members.Count > 0)
                    Battle(world, party);
                else
                    ColonyBrain.Say(party.From, world.Clock, $"Nos guerriers arrivent devant {party.To.Name}, mais la guerre est finie : ils font demi-tour.");
                party.State = WarPartyState.Returning;
                party.ReturnTicks = now + (party.ArriveTicks - party.DepartTicks);
            }
            if (party.State == WarPartyState.Returning && now >= party.ReturnTicks)
                ComeHome(world, party);
        }
    }

    private static void Battle(WorldState world, WarParty party)
    {
        Colony attacker = party.From, defender = party.To;
        Random luck = world.Politics;
        GameClock clock = world.Clock;
        float attack = Strength(attacker, party.Warriors.Count, party.Weapons) * (0.8f + 0.4f * luck.NextSingle());
        float defense = DefenseEstimate(world, defender, attacker) * (0.8f + 0.4f * luck.NextSingle());
        Diplomacy.OnAttacked(world, defender, attacker);

        if (attack > defense)
        {
            party.Victory = true;
            attacker.BattlesWon++;
            defender.BattlesLost++;
            float margin = (attack - defense) / Math.Max(1f, defense);
            int killed = Math.Min(3, (int)(margin * 3f + luck.NextSingle()));
            List<string> loot = Pillage(world, party);
            var victims = defender.Members.Where(m => m.Stage is LifeStage.Adult or LifeStage.Teen && m.Ailment == Ailment.None)
                .OrderBy(_ => luck.Next()).ToList();
            int dead = 0, wounded = 0;
            foreach (Colonist victim in victims.Take(killed))
            {
                Lifecycle.Die(world, victim, "guerre");
                dead++;
            }
            foreach (Colonist victim in victims.Skip(killed).Take(1 + luck.Next(3)))
            {
                Health.Fall(defender, victim, Ailment.Injured, 72 + luck.Next(0, 48), clock, "");
                wounded++;
            }
            defender.RecordEvent(new(ColonyEventKind.Raid, 1, defender.CampY, clock.Ticks, ColonyEventOutcome.Pillaged));
            defender.WarWeariness += 4f + 12f * dead;
            attacker.WarWeariness += 4f;
            ColonyBrain.Say(defender, clock, $"⚔ Les guerriers de {attacker.Name} nous attaquent et l'emportent : "
                + (loot.Count > 0 ? $"ils emportent {string.Join(", ", loot)} ; " : "")
                + (dead > 0 ? $"{dead} des nôtres sont tués, " : "") + $"{wounded} blessés.");
            // Quelques vainqueurs rentrent blessés.
            foreach (Colonist warrior in party.Warriors.Where(_ => luck.NextSingle() < 0.25f).ToList())
                Health.Fall(attacker, warrior, Ailment.Injured, 48 + luck.Next(0, 48), clock, "");
            return;
        }

        party.Victory = false;
        attacker.BattlesLost++;
        defender.BattlesWon++;
        float gap = (defense - attack) / Math.Max(1f, defense);
        int fallen = Math.Clamp(1 + (int)(party.Warriors.Count * gap), 1, Math.Max(1, party.Warriors.Count - 1));
        int lostWeapons = 0;
        foreach (Colonist warrior in party.Warriors.OrderBy(_ => luck.Next()).Take(fallen).ToList())
        {
            party.Warriors.Remove(warrior);
            attacker.Transients.Remove(warrior);
            if (party.Weapons > party.Warriors.Count)
            {
                party.Weapons--;
                lostWeapons++;
            }
            Lifecycle.Die(world, warrior, "guerre");
        }
        foreach (Colonist warrior in party.Warriors)
            if (luck.NextSingle() < 0.5f)
                Health.Fall(attacker, warrior, Ailment.Injured, 72 + luck.Next(0, 48), clock, "");
        int hurt = 0;
        foreach (Colonist defenderHurt in defender.Members.Where(m => m.Stage == LifeStage.Adult && m.Ailment == Ailment.None)
                     .OrderBy(_ => luck.Next()).Take(luck.Next(2)).ToList())
        {
            Health.Fall(defender, defenderHurt, Ailment.Injured, 48 + luck.Next(0, 48), clock, "");
            hurt++;
        }
        defender.Stock.Add(ResourceType.Tools, lostWeapons, ResourceFlow.Transfer);
        defender.RecordEvent(new(ColonyEventKind.Raid, 1, defender.CampY, clock.Ticks, ColonyEventOutcome.Repelled));
        attacker.WarWeariness += 4f + 12f * fallen;
        defender.WarWeariness += 2f;
        ColonyBrain.Say(defender, clock, $"⚔ Nous repoussons les guerriers de {attacker.Name} : {fallen} d'entre eux restent sur le terrain"
            + (lostWeapons > 0 ? $", nous ramassons {lostWeapons} outils" : "") + (hurt > 0 ? $" ; {hurt} des nôtres sont blessés." : "."));
    }

    /// <summary>Les vainqueurs emportent un quart des pièces, puis des outils, du fer et des vivres tant qu'ils peuvent porter.</summary>
    private static List<string> Pillage(WorldState world, WarParty party)
    {
        Stockpile stock = party.To.Stock;
        var taken = new List<string>();
        int coins = stock.Get(ResourceType.Coins) / 4;
        if (coins > 0 && stock.TryTake(ResourceType.Coins, coins, ResourceFlow.Loss))
        {
            party.Loot[ResourceType.Coins] = coins;
            taken.Add($"{coins} pièces");
        }
        int room = party.Warriors.Count * Diplomacy.LootPerWarrior;
        foreach (ResourceType good in new[] { ResourceType.Tools, ResourceType.Iron, ResourceType.SaltedMeat, ResourceType.Bread, ResourceType.Grain,
                     ResourceType.Clothes, ResourceType.Beer, ResourceType.Salt, ResourceType.Spices })
        {
            int units = Math.Min(room, stock.Get(good) / 4);
            if (units <= 0 || !stock.TryTake(good, units, ResourceFlow.Loss))
                continue;
            party.Loot[good] = party.Loot.GetValueOrDefault(good) + units;
            taken.Add($"{units} {Trade.GoodName(good, units)}");
            room -= units;
            if (room <= 0)
                break;
        }
        return taken;
    }

    // ---------- Retour ----------

    private static void ComeHome(WorldState world, WarParty party)
    {
        world.WarParties.Remove(party);
        Colony colony = party.From;
        colony.Stock.Add(ResourceType.Tools, party.Weapons, ResourceFlow.Transfer);
        foreach ((ResourceType good, int units) in party.Loot)
            colony.Stock.Add(good, units, ResourceFlow.Transfer);

        foreach (Colonist warrior in party.Warriors)
        {
            colony.Transients.Remove(warrior);
            warrior.Activity = null;
            warrior.Needs.Food = Math.Min(warrior.Needs.Food, 0.6f);
            warrior.Needs.Rest = Math.Min(warrior.Needs.Rest, 0.5f);
            if (Migration.FindEdgePoint(world, colony, colony.CampX, colony.CampY) is { } entry)
            {
                warrior.X = warrior.PrevX = entry.X + 0.5f;
                warrior.Y = warrior.PrevY = entry.Y + 0.5f;
                warrior.Transit = TransitState.Arriving;
                warrior.ReturningTrader = true;
                colony.Transients.Add(warrior);
            }
            else
            {
                (int x, int y) = colony.GatherSpots[world.Random.Next(Math.Min(12, colony.GatherSpots.Count))];
                warrior.X = warrior.PrevX = x + 0.5f;
                warrior.Y = warrior.PrevY = y + 0.5f;
                warrior.Transit = TransitState.None;
                colony.Members.Add(warrior);
            }
        }
        colony.FillVacancies();
        colony.AssignSectors();

        string loot = string.Join(", ", party.Loot.Where(l => l.Value > 0).Select(l => $"{l.Value} {Trade.GoodName(l.Key, l.Value)}"));
        ColonyBrain.Say(colony, world.Clock, party.Victory switch
        {
            true => $"Nos guerriers rentrent victorieux de {party.To.Name}" + (loot.Length > 0 ? $", chargés de {loot}." : "."),
            false => $"Nos guerriers rentrent de {party.To.Name}, battus : {party.Warriors.Count} reviennent.",
            _ => $"Nos guerriers sont de retour de {party.To.Name}.",
        });
    }
}
