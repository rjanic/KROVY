namespace AcKrovy.Core.Services.Roofs;

/// <summary>Exact native provenance; identifiers are opaque and never geometry.</summary>
public static class RoofNativeCloneOwnershipRules
{
    public static bool IsComplete<T>(T owner, IReadOnlyCollection<T> requiredChildren,
        IReadOnlyDictionary<T, T> mapping, IReadOnlyCollection<T> preExistingIds) where T : notnull
    {
        var sources = requiredChildren.Append(owner).Distinct().ToArray();
        if (requiredChildren.Count == 0 ||
            sources.Any(id => !preExistingIds.Contains(id) || !mapping.ContainsKey(id))) return false;
        var destinations = sources.Select(id => mapping[id]).ToArray();
        return destinations.Distinct().Count() == sources.Length &&
            destinations.All(id => !preExistingIds.Contains(id));
    }

    public static bool TryGetDisposableClones<T>(T owner, IReadOnlyCollection<T> required,
        IReadOnlyCollection<T> disposable, IReadOnlyDictionary<T, T> mapping,
        IReadOnlyCollection<T> preExistingIds, out IReadOnlyList<T> clones) where T : notnull
    {
        clones = Array.Empty<T>();
        if (!IsComplete(owner, required, mapping, preExistingIds)) return false;
        var sources = required.Concat(disposable).Append(owner).Distinct().ToArray();
        if (sources.Any(id => !preExistingIds.Contains(id))) return false;
        var mapped = sources.Where(mapping.ContainsKey).Select(id => mapping[id]).ToArray();
        if (mapped.Distinct().Count() != mapped.Length || mapped.Any(preExistingIds.Contains)) return false;
        clones = disposable.Distinct().Where(mapping.ContainsKey).Select(id => mapping[id]).ToArray();
        return true;
    }
}
