using System.Collections;
using System.Reflection;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Pathfinding;

namespace GodColony.Simulation.Tests;

/// <summary>Compare les données et les références, indépendamment de l'ordre interne des ensembles et dictionnaires.</summary>
internal sealed class WorldComparison
{
    private readonly Dictionary<object, object> _references = new(ReferenceEqualityComparer.Instance);
    internal string? Difference(object? expected, object? actual, string path = "Monde")
    {
        if (expected is null || actual is null) return expected == actual ? null : path + " : valeur absente";
        Type type = expected.GetType();
        if (type != actual.GetType()) return path + " : type différent";
        if (type.IsPrimitive || type.IsEnum || expected is string || expected is Species)
            return expected.Equals(actual) ? null : $"{path} : {expected} ≠ {actual}";
        if (!type.IsValueType)
        {
            if (_references.TryGetValue(expected, out object? mapped))
                return ReferenceEquals(mapped, actual) ? null : path + " : référence partagée différente";
            _references.Add(expected, actual);
        }
        if (expected is IDictionary left && actual is IDictionary right)
        {
            if (left.Count != right.Count) return path + " : taille différente";
            foreach (DictionaryEntry entry in left)
            {
                object key = _references.GetValueOrDefault(entry.Key) ?? entry.Key;
                // Une colonie pas encore parcourue (l'opinion d'une colonie sur les suivantes) se retrouve par son nom, qui est unique.
                if (key is Colony colony && !right.Contains(key))
                    key = right.Keys.OfType<Colony>().FirstOrDefault(c => c.Name == colony.Name) ?? key;
                if (!right.Contains(key)) return path + $" : clé absente ({key})";
                string? difference = Difference(entry.Value, right[key], path + $"[{key}]");
                if (difference is not null) return difference;
            }
            return null;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            object?[] leftSet = ((IEnumerable)expected).Cast<object?>().ToArray();
            object?[] rightSet = ((IEnumerable)actual).Cast<object?>().ToArray();
            return leftSet.Length == rightSet.Length && leftSet.All(rightSet.Contains) ? null : path + " : ensemble différent";
        }
        if (expected is IList leftList && actual is IList rightList)
        {
            if (leftList.Count != rightList.Count) return path + " : taille différente";
            for (int i = 0; i < leftList.Count; i++)
                if (Difference(leftList[i], rightList[i], path + $"[{i}]") is { } difference) return difference;
            return null;
        }
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.Name))
        {
            if (typeof(Delegate).IsAssignableFrom(field.FieldType) || field.FieldType == typeof(Pathfinder)
                || (type == typeof(LocalMap) && field.Name == "_scratch")
                || field.GetCustomAttribute<NonSerializedAttribute>() is not null) continue;
            if (Difference(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name) is { } difference) return difference;
        }
        return null;
    }
}
