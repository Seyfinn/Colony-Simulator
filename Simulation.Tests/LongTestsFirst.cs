using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCollectionOrderer("GodColony.Simulation.Tests.LongTestsFirst", "Simulation.Tests")]

namespace GodColony.Simulation.Tests;

/// <summary>
/// Lance d'abord les classes les plus longues (des parties de plusieurs années) : démarrées en dernier, elles terminaient seules la suite pendant que
/// les autres cœurs restaient inoccupés. Les autres classes gardent l'ordre habituel de xUnit. Un nom absent de la liste n'a aucun effet.
/// </summary>
public sealed class LongTestsFirst : ITestCollectionOrderer
{
    // Liste mesurée (du plus long au plus court) : à revoir si une autre classe devient la dernière à finir.
    private static readonly string[] Longest =
    [
        "ResumeAtDay150Tests", "SocialTests", "SaveTests", "DeterminismTests", "GrowthTests", "LifecycleTests",
        "ResumeAtDay90Tests", "VillageSoakTests", "NeedsPriorityTests", "OfferingTests", "SoakTests",
    ];

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        new DefaultTestCollectionOrderer().OrderTestCollections(testCollections).OrderBy(Rank);

    private static int Rank(ITestCollection collection)
    {
        int rank = Array.FindIndex(Longest, name => collection.DisplayName.EndsWith("." + name, StringComparison.Ordinal));
        return rank < 0 ? Longest.Length : rank;
    }
}
