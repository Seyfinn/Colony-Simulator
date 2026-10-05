using GodColony.Simulation.Time;

namespace GodColony.Simulation.Colonies;

/// <summary>Relevés des flux réellement observés ; garde les 240 derniers jours sans perdre les pics d'échanges.</summary>
public sealed class ResourceHistory
{
    public const int MaxSamples = 240;
    public readonly record struct Sample(long Day, float Production, float Usage, float Purchases, float Sales)
    {
        public float Value(ResourceFlow flow) => flow switch
        {
            ResourceFlow.Production => Production, ResourceFlow.Usage => Usage,
            ResourceFlow.Purchase => Purchases, ResourceFlow.Sale => Sales, _ => 0,
        };
    }

    private sealed class Observation
    {
        public long Ticks;
        public readonly Dictionary<ResourceType, long[]> Totals = [];
        public readonly Dictionary<ResourceType, List<Sample>> Samples = [];
    }

    private readonly Dictionary<Settlement, Observation> _colonies = [];
    private long _lastDay = -1;
    public int Version { get; private set; }

    public IReadOnlyList<Sample> Of(Colony colony, ResourceType good) =>
        _colonies.TryGetValue(colony.CurrentSettlement, out var observation) && observation.Samples.TryGetValue(good, out var samples) ? samples : [];

    public void Observe(WorldState world)
    {
        long day = world.Clock.TotalDays;
        if (day == _lastDay && _colonies.Count == world.Settlements.Count()) return;
        bool newDay = day != _lastDay;
        _lastDay = day;
        foreach (Settlement place in world.Settlements)
        {
            Colony colony = place.Owner;
            using var scope = colony.UseSettlement(place);
            bool first = !_colonies.TryGetValue(colony.CurrentSettlement, out var observation);
            if (first) _colonies[place] = observation = new Observation { Ticks = world.Clock.Ticks };
            if (!first && !newDay) continue;
            double days = (world.Clock.Ticks - observation!.Ticks) / (double)TimeConstants.TicksPerDay;
            foreach (ResourceType good in Enum.GetValues<ResourceType>())
            {
                long[] totals = Enumerable.Range(0, 4).Select(i => ResourceAccounting.Total(colony.Stock, good, (ResourceFlow)i)).ToArray();
                if (!observation.Samples.TryGetValue(good, out var samples)) observation.Samples[good] = samples = [];
                if (!first && days > 0)
                {
                    long[] before = observation.Totals[good];
                    float Rate(int i) => (float)((totals[i] - before[i]) / days);
                    samples.Add(new Sample(day, Rate(0), Rate(1), Rate(2), Rate(3)));
                    if (samples.Count > MaxSamples) samples.RemoveAt(0);
                }
                observation.Totals[good] = totals;
            }
            observation.Ticks = world.Clock.Ticks;
        }
        Version++;
    }
}
