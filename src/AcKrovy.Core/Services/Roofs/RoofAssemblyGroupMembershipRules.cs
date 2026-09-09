namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Pure multiset rules for canonical roof-assembly GROUP membership. Host groups can
/// retain erased member handles and un-erase / append can leave duplicate membership
/// entries; set equivalence alone is not sufficient for canonical validation.
/// </summary>
public static class RoofAssemblyGroupMembershipRules
{
    public sealed record MembershipPlan<T>(
        IReadOnlyList<T> RemoveOnce,
        IReadOnlyList<T> AppendOnce)
        where T : notnull;

    public static bool IsCanonicalMembership<T>(
        IReadOnlyList<T> actualMembers,
        IReadOnlyCollection<T> expectedUniqueMembers)
        where T : notnull
    {
        if (actualMembers is null || expectedUniqueMembers is null)
        {
            return false;
        }

        var expected = expectedUniqueMembers as HashSet<T> ??
                       new HashSet<T>(expectedUniqueMembers);
        if (expected.Count != expectedUniqueMembers.Count)
        {
            return false;
        }

        if (actualMembers.Count != expected.Count ||
            actualMembers.Distinct().Count() != expected.Count)
        {
            return false;
        }

        return expected.SetEquals(actualMembers);
    }

    public static int CountDuplicates<T>(IReadOnlyList<T> actualMembers)
        where T : notnull
    {
        if (actualMembers is null || actualMembers.Count == 0)
        {
            return 0;
        }

        return actualMembers.Count - actualMembers.Distinct().Count();
    }

    public static int CountMissing<T>(
        IReadOnlyList<T> actualMembers,
        IReadOnlyCollection<T> expectedUniqueMembers)
        where T : notnull
    {
        if (expectedUniqueMembers is null || expectedUniqueMembers.Count == 0)
        {
            return 0;
        }

        var actual = new HashSet<T>(actualMembers ?? Array.Empty<T>());
        var missing = 0;
        foreach (var id in expectedUniqueMembers)
        {
            if (!actual.Contains(id))
            {
                missing++;
            }
        }

        return missing;
    }

    public static int CountForeign<T>(
        IReadOnlyList<T> actualMembers,
        IReadOnlyCollection<T> expectedUniqueMembers)
        where T : notnull
    {
        if (actualMembers is null || actualMembers.Count == 0)
        {
            return 0;
        }

        var expected = expectedUniqueMembers as HashSet<T> ??
                       new HashSet<T>(expectedUniqueMembers ?? Array.Empty<T>());
        var foreign = 0;
        var seen = new HashSet<T>();
        foreach (var id in actualMembers)
        {
            if (!expected.Contains(id) && seen.Add(id))
            {
                foreign++;
            }
        }

        return foreign;
    }

    /// <summary>
    /// Builds remove/append operations so each expected member appears exactly once
    /// and foreign extras are removed. Removes are ordered to clear all surplus
    /// copies before any append.
    /// </summary>
    public static MembershipPlan<T> PlanCanonicalization<T>(
        IReadOnlyList<T> actualMembers,
        IReadOnlyCollection<T> expectedUniqueMembers)
        where T : notnull
    {
        var expected = expectedUniqueMembers as HashSet<T> ??
                       new HashSet<T>(expectedUniqueMembers ?? Array.Empty<T>());
        if (expected.Count != (expectedUniqueMembers?.Count ?? 0))
        {
            throw new ArgumentException(
                "Expected membership must be unique.",
                nameof(expectedUniqueMembers));
        }

        var counts = new Dictionary<T, int>();
        foreach (var id in actualMembers ?? Array.Empty<T>())
        {
            counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1;
        }

        var removeOnce = new List<T>();
        foreach (var pair in counts)
        {
            if (!expected.Contains(pair.Key))
            {
                for (var i = 0; i < pair.Value; i++)
                {
                    removeOnce.Add(pair.Key);
                }

                continue;
            }

            for (var i = 1; i < pair.Value; i++)
            {
                removeOnce.Add(pair.Key);
            }
        }

        var appendOnce = new List<T>();
        foreach (var id in expected)
        {
            if (!counts.TryGetValue(id, out var count) || count == 0)
            {
                appendOnce.Add(id);
            }
        }

        return new MembershipPlan<T>(removeOnce, appendOnce);
    }
}
