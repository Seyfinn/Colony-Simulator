using System;
using System.Collections.Generic;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Ce que les courbes de la vue chiffrée suivent, au choix du joueur.</summary>
public enum ColonyMetric { Population, FoodDays, Mood, Coins }

/// <summary>
/// Un relevé quotidien de chaque colonie (habitants, réserves, humeur, pièces) pour tracer les courbes de la vue chiffrée.
/// C'est l'affichage qui le tient en lisant le monde : la simulation ne garde pas d'historique, et il repart de zéro au chargement.
/// Au-delà de <see cref="MaxSamples"/> relevés, on n'en garde plus qu'un sur deux : la courbe couvre toujours toute la partie
/// pour un coût fixe, et le dernier relevé reste celui du jour.
/// </summary>
public sealed class ColonyHistory
{
    public readonly record struct Sample(long Day, int Population, float FoodDays, float Mood, int Coins)
    {
        public float Value(ColonyMetric metric) => metric switch
        {
            ColonyMetric.Population => Population,
            ColonyMetric.FoodDays => FoodDays,
            ColonyMetric.Mood => Mood,
            _ => Coins,
        };
    }

    private const int MaxSamples = 480;

    private readonly Dictionary<Colony, List<Sample>> _colonies = [];
    private readonly List<Sample> _world = [];
    private long _lastDay = long.MinValue;

    /// <summary>Jours entre deux relevés conservés : il double à chaque fois que les courbes atteignent leur taille maximale.</summary>
    private int _stride = 1;

    /// <summary>Change à chaque relevé : les courbes ne se redessinent qu'à ce moment-là.</summary>
    public int Version { get; private set; }

    public ResourceHistory Resources { get; } = new();

    /// <summary>Le total du monde : population de toutes les colonies, moyenne des réserves et de l'humeur, pièces cumulées.</summary>
    public IReadOnlyList<Sample> World => _world;

    public IReadOnlyList<Sample> Of(Colony colony) => _colonies.TryGetValue(colony, out var samples) ? samples : [];

    /// <summary>À appeler après chaque tick : on ne relève qu'une fois par jour.</summary>
    public void Observe(WorldState world)
    {
        Resources.Observe(world);
        long day = world.Clock.TotalDays;
        if (day == _lastDay)
            return;
        _lastDay = day;

        int population = 0, coins = 0;
        float food = 0, mood = 0;
        foreach (Colony colony in world.Colonies)
        {
            Sample sample = Measure(colony, day);
            if (!_colonies.TryGetValue(colony, out var samples))
                _colonies[colony] = samples = [];
            Push(samples, sample);
            population += sample.Population;
            coins += sample.Coins;
            food += sample.FoodDays * sample.Population;
            mood += sample.Mood * sample.Population;
        }
        Push(_world, new Sample(day, population, population == 0 ? 0 : food / population, population == 0 ? 0 : mood / population, coins));
        if (_world.Count > MaxSamples)
            Thin();
        Version++;
    }

    public static Sample Measure(Colony colony, long day) =>
        new(day, colony.Members.Count, FoodDays(colony), colony.AverageMood, colony.Stock.Get(ResourceType.Coins));

    /// <summary>Jours de repas en réserve, comme dans le bandeau de la colonie.</summary>
    public static float FoodDays(Colony colony) =>
        colony.Stock.FoodUnits / (Math.Max(1, colony.Members.Count) * ColonyBrain.MealsPerColonistPerDay);

    /// <summary>La valeur relevée il y a un an, ou le premier relevé si la colonie est plus jeune (avec son jour).</summary>
    public static Sample? YearAgo(IReadOnlyList<Sample> samples)
    {
        if (samples.Count < 2)
            return null;
        long target = samples[^1].Day - TimeConstants.DaysPerYear;
        int low = 0, high = samples.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (samples[middle].Day <= target) low = middle;
            else high = middle - 1;
        }
        return samples[low];
    }

    /// <summary>Le relevé du jour remplace le précédent s'il ne tombait pas sur un jour à conserver.</summary>
    private void Push(List<Sample> samples, Sample sample)
    {
        if (samples.Count > 1 && samples[^1].Day % _stride != 0)
            samples[^1] = sample;
        else
            samples.Add(sample);
    }

    /// <summary>Ne garde qu'un relevé sur deux, en gardant toujours le premier (l'origine de la courbe) et le dernier (aujourd'hui).</summary>
    private void Thin()
    {
        _stride *= 2;
        foreach (List<Sample> samples in _colonies.Values)
            Keep(samples);
        Keep(_world);
    }

    private void Keep(List<Sample> samples)
    {
        if (samples.Count < 3)
            return;
        Sample last = samples[^1];
        int kept = 1;
        for (int i = 1; i < samples.Count - 1; i++)
            if (samples[i].Day % _stride == 0)
                samples[kept++] = samples[i];
        samples[kept++] = last;
        samples.RemoveRange(kept, samples.Count - kept);
    }
}
