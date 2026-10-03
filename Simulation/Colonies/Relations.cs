namespace GodColony.Simulation.Colonies;

/// <summary>
/// Les liens entre colons : chaque conversation rapproche ou éloigne deux personnes, selon leurs personnalités.
/// L'affinité va de -100 à +100 ; au-delà de ±40, on parle d'amitié ou de rivalité.
/// </summary>
public static class Relations
{
    public const float FriendThreshold = 40f;
    public const float FriendKeepThreshold = 25f;
    public const float RivalThreshold = -40f;
    public const float RivalKeepThreshold = -25f;

    /// <summary>Chaque jour, les liens s'effacent un peu en l'absence de contact.</summary>
    private const float DailyFading = 0.985f;

    public static float Affinity(Colonist a, Colonist b) => a.Affinities.GetValueOrDefault(b.Id);

    public enum Change { None, BecameFriends, BecameRivals }

    public sealed record Outcome(float Delta, bool Dispute, Change Change);

    /// <summary>
    /// Deux colons conversent. Des personnalités proches s'entendent, des personnalités opposées se frottent ;
    /// deux caractères belliqueux peuvent se disputer.
    /// </summary>
    public static Outcome Converse(Colonist a, Colonist b, Random random)
    {
        float compatibility = Personality.Compatibility(a.Personality, b.Personality);
        float sociability = (a.Personality[Axis.Sociabilite] + b.Personality[Axis.Sociabilite]) / 2f;
        float temper = (a.Personality[Axis.Temperament] + b.Personality[Axis.Temperament]) / 2f;

        // Une bonne entente rapproche, une mauvaise éloigne ; les sociables en font plus de cas.
        float delta = 30f * (compatibility - 0.35f) * Math.Clamp(1f + 0.5f * sociability, 0.5f, 1.5f);
        delta += 4f * random.NextSingle() - 2f;

        bool dispute = temper > 0.3f && compatibility < 0.5f && random.NextSingle() < 0.3f;
        if (dispute)
            delta = -8f;

        return new Outcome(delta, dispute, Apply(a, b, delta));
    }

    /// <summary>Modifie l'affinité de deux colons et dit si leur lien vient de changer de nature.</summary>
    public static Change Apply(Colonist a, Colonist b, float delta)
    {
        float affinity = Math.Clamp(Affinity(a, b) + delta, -100f, 100f);
        a.Affinities[b.Id] = affinity;
        b.Affinities[a.Id] = affinity;

        Change change = Change.None;
        if (affinity >= FriendThreshold && a.Friends.Add(b.Id))
        {
            b.Friends.Add(a.Id);
            a.Rivals.Remove(b.Id);
            b.Rivals.Remove(a.Id);
            change = Change.BecameFriends;
        }
        else if (affinity <= RivalThreshold && a.Rivals.Add(b.Id))
        {
            b.Rivals.Add(a.Id);
            a.Friends.Remove(b.Id);
            b.Friends.Remove(a.Id);
            change = Change.BecameRivals;
        }
        else if (affinity < FriendKeepThreshold)
        {
            a.Friends.Remove(b.Id);
            b.Friends.Remove(a.Id);
        }
        if (affinity > RivalKeepThreshold)
        {
            a.Rivals.Remove(b.Id);
            b.Rivals.Remove(a.Id);
        }
        return change;
    }

    /// <summary>
    /// Vivre sous le même toit rapproche ceux qui s'entendent et exaspère ceux qui s'opposent :
    /// chaque jour, les colocataires gagnent ou perdent en affinité selon leur compatibilité.
    /// </summary>
    public static List<(Colonist A, Colonist B, Change Change)> Cohabit(Colony colony)
    {
        var changes = new List<(Colonist, Colonist, Change)>();
        foreach (Building hut in colony.Buildings)
        for (int i = 0; i < hut.Residents.Count; i++)
        for (int j = i + 1; j < hut.Residents.Count; j++)
        {
            Colonist a = hut.Residents[i], b = hut.Residents[j];
            float delta = CohabitationRate * (Personality.Compatibility(a.Personality, b.Personality) - 0.45f);
            Change change = Apply(a, b, delta);
            if (change != Change.None)
                changes.Add((a, b, change));
        }
        return changes;
    }

    private const float CohabitationRate = 10f;

    /// <summary>Chaque jour, les liens s'effacent doucement ; ceux qui ont quitté la colonie sont oubliés.</summary>
    public static void FadeDaily(Colony colony)
    {
        var present = colony.Members.Select(m => m.Id).ToHashSet();
        foreach (Colonist colonist in colony.Members)
        foreach (int id in colonist.Affinities.Keys.ToList())
        {
            if (!present.Contains(id))
            {
                colonist.Affinities.Remove(id);
                colonist.Friends.Remove(id);
                colonist.Rivals.Remove(id);
                continue;
            }
            float faded = colonist.Affinities[id] * DailyFading;
            if (MathF.Abs(faded) < 1f)
                colonist.Affinities.Remove(id);
            else
                colonist.Affinities[id] = faded;
            if (faded < FriendKeepThreshold)
                colonist.Friends.Remove(id);
            if (faded > RivalKeepThreshold)
                colonist.Rivals.Remove(id);
        }
    }
}
