namespace AcKrovy.Core.Services.Roofs;

/// <summary>BodyIdentity is an opaque command-local CAD binding, never a semantic key.</summary>
public sealed record RoofOrdinaryPhysicalBinding(string BodyIdentity, string SemanticKey, bool Collateral = false);
public sealed record RoofOrdinaryPhysicalReconciliationPlan(
    IReadOnlyList<string> RemoveBodyIdentities, IReadOnlyList<string> RebuildKeys);

public static class RoofOrdinaryPhysicalReconciliationRules
{
    public static bool TryPlan(IReadOnlyCollection<string> semanticKeys,
        IReadOnlyCollection<string> changedKeys, IReadOnlyList<RoofOrdinaryPhysicalBinding> bodies,
        bool allowCardinalityChanges, out RoofOrdinaryPhysicalReconciliationPlan? plan)
    {
        plan = null;
        var expected = new HashSet<string>(semanticKeys, StringComparer.Ordinal);
        if (expected.Count != semanticKeys.Count ||
            bodies.Select(body => body.BodyIdentity).Distinct(StringComparer.Ordinal).Count() != bodies.Count)
            return false;
        var grouped = bodies.GroupBy(body => body.SemanticKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (!allowCardinalityChanges &&
            (grouped.Count != expected.Count || grouped.Any(pair =>
                !expected.Contains(pair.Key) || pair.Value.Length != 1 || pair.Value[0].Collateral))) return false;
        var remove = new HashSet<string>(StringComparer.Ordinal);
        var rebuild = new HashSet<string>(changedKeys.Where(expected.Contains), StringComparer.Ordinal);
        foreach (var pair in grouped)
        {
            var canonical = pair.Value.Where(body => !body.Collateral).ToArray();
            if (!expected.Contains(pair.Key))
            {
                foreach (var body in pair.Value) remove.Add(body.BodyIdentity);
                continue;
            }
            foreach (var body in pair.Value.Where(body => body.Collateral)) remove.Add(body.BodyIdentity);
            // Multiple pre-existing bodies are damaged state, not a reason to choose
            // a canonical solid by iteration order or native geometry.
            if (canonical.Length != 1) rebuild.Add(pair.Key);
        }
        foreach (var key in expected)
            if (!grouped.ContainsKey(key)) rebuild.Add(key);
        foreach (var key in rebuild)
            if (grouped.TryGetValue(key, out var existing))
                foreach (var body in existing) remove.Add(body.BodyIdentity);
        plan = new RoofOrdinaryPhysicalReconciliationPlan(
            remove.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            rebuild.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        return true;
    }

    public static bool IsCanonical(IReadOnlyCollection<string> semanticKeys,
        IReadOnlyCollection<string> physicalKeys) =>
        semanticKeys.Count == semanticKeys.Distinct(StringComparer.Ordinal).Count() &&
        physicalKeys.Count == physicalKeys.Distinct(StringComparer.Ordinal).Count() &&
        new HashSet<string>(semanticKeys, StringComparer.Ordinal).SetEquals(physicalKeys);
}
