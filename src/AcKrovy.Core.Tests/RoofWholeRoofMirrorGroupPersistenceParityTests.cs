using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Models the HOST SAVE/REOPEN mirrored-group failure arithmetic:
/// display-only 22 + two Append passes of the 288 timber/annotation children → 598.
/// </summary>
public sealed class RoofWholeRoofMirrorGroupPersistenceParityTests
{
    [Fact]
    public void HostComplexX_DoubleChildAppend_ProducesExactReopen598Signature()
    {
        // 1 owner + 21 display = 22; children 56+16+216 = 288; 22 + 2*288 = 598.
        var ownerAndDisplay = new List<string> { "Owner" };
        ownerAndDisplay.AddRange(Enumerable.Range(0, 21).Select(i => $"D{i}"));
        Assert.Equal(22, ownerAndDisplay.Count);

        var children = new List<string>();
        children.AddRange(Enumerable.Range(0, 56).Select(i => $"G{i}"));
        children.AddRange(Enumerable.Range(0, 16).Select(i => $"S{i}"));
        children.AddRange(Enumerable.Range(0, 216).Select(i => $"A{i}"));
        Assert.Equal(288, children.Count);

        var expected = new HashSet<string>(ownerAndDisplay.Concat(children), StringComparer.Ordinal);
        Assert.Equal(310, expected.Count);

        // First EnsureGroup expansion (correct).
        var actual = ownerAndDisplay.ToList();
        var first = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
        Assert.Empty(first.RemoveOnce);
        Assert.Equal(288, first.AppendOnce.Count);
        actual.AddRange(first.AppendOnce);
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        Assert.Equal(310, actual.Count);

        // Second EnsureGroup expansion when GetAllEntityIds under-reports and still looks
        // like the display-only set (stale actual = 22). Plan appends the same 288 again.
        var staleActual = ownerAndDisplay.ToList();
        var second = RoofAssemblyGroupMembershipRules.PlanCanonicalization(staleActual, expected);
        Assert.Equal(288, second.AppendOnce.Count);
        actual.AddRange(second.AppendOnce);

        Assert.Equal(598, actual.Count);
        Assert.Equal(288, RoofAssemblyGroupMembershipRules.CountDuplicates(actual));
        Assert.Equal(1, actual.Count(id => id == "Owner"));
        Assert.Equal(21, actual.Count(id => id.StartsWith('D')));
        Assert.Equal(112, actual.Count(id => id.StartsWith('G')));
        Assert.Equal(32, actual.Count(id => id.StartsWith('S')));
        Assert.Equal(432, actual.Count(id => id.StartsWith('A')));
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
    }

    [Fact]
    public void CollapseDuplicateChildSlots_RestoresExactCanonical310()
    {
        var expected = FullExpected();
        var actual = expected.ToList();
        actual.AddRange(expected.Where(id => id.StartsWith('G') || id.StartsWith('S') || id.StartsWith('A')));
        Assert.Equal(598, actual.Count);

        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
        Assert.Equal(288, plan.RemoveOnce.Count);
        Assert.Empty(plan.AppendOnce);
        foreach (var remove in plan.RemoveOnce)
        {
            actual.Remove(remove);
        }

        Assert.Equal(310, actual.Count);
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountDuplicates(actual));
    }

    [Fact]
    public void FreshPresenceGuard_PreventsSecondAppendOfSameChildIds()
    {
        var expected = FullExpected();
        var actual = expected.Where(id => id == "Owner" || id.StartsWith('D')).ToList();
        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
        var present = new HashSet<string>(actual, StringComparer.Ordinal);
        var appended = new List<string>();
        foreach (var addId in plan.AppendOnce)
        {
            if (!present.Add(addId))
            {
                continue;
            }

            appended.Add(addId);
        }

        actual.AddRange(appended);
        Assert.Equal(310, actual.Count);

        // Stale plan would try the same AppendOnce again; presence guard skips them.
        var skipped = 0;
        foreach (var addId in plan.AppendOnce)
        {
            if (!present.Add(addId))
            {
                skipped++;
                continue;
            }

            actual.Add(addId);
        }

        Assert.Equal(288, skipped);
        Assert.Equal(310, actual.Count);
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
    }

    private static HashSet<string> FullExpected()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal) { "Owner" };
        for (var i = 0; i < 21; i++)
        {
            expected.Add($"D{i}");
        }

        for (var i = 0; i < 56; i++)
        {
            expected.Add($"G{i}");
        }

        for (var i = 0; i < 16; i++)
        {
            expected.Add($"S{i}");
        }

        for (var i = 0; i < 216; i++)
        {
            expected.Add($"A{i}");
        }

        return expected;
    }
}
