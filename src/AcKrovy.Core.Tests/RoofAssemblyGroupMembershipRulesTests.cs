using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAssemblyGroupMembershipRulesTests
{
    [Fact]
    public void Canonical_RequiresExactUniqueMembership()
    {
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(
            ["A", "B", "C"],
            ["A", "B", "C"]));
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(
            ["A", "B", "B", "C"],
            ["A", "B", "C"]));
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(
            ["A", "B"],
            ["A", "B", "C"]));
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(
            ["A", "B", "C", "X"],
            ["A", "B", "C"]));
    }

    [Fact]
    public void CountDuplicates_ReportsSurplusCopies()
    {
        Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountDuplicates(["A", "B", "C"]));
        Assert.Equal(2, RoofAssemblyGroupMembershipRules.CountDuplicates(["A", "A", "B", "B", "C"]));
    }

    [Fact]
    public void Plan_RemovesDuplicateObjectIdsAndAppendsMissing()
    {
        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(
            ["A", "A", "B", "X"],
            ["A", "B", "C"]);

        Assert.Equal(1, plan.RemoveOnce.Count(id => id == "A"));
        Assert.Contains("X", plan.RemoveOnce);
        Assert.DoesNotContain("B", plan.RemoveOnce);
        Assert.Equal(["C"], plan.AppendOnce);
    }

    [Fact]
    public void Plan_LeavesAlreadyCanonicalUnchanged()
    {
        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(
            ["A", "B", "C"],
            ["A", "B", "C"]);
        Assert.Empty(plan.RemoveOnce);
        Assert.Empty(plan.AppendOnce);
    }

    [Fact]
    public void Plan_RepeatedRepairStableAgainstDuplicateSeed()
    {
        var expected = new HashSet<string>(["S", "D1", "T1", "A1"], StringComparer.Ordinal);
        var actual = new List<string> { "S", "D1", "T1", "T1", "A1", "A1" };
        for (var i = 0; i < 5; i++)
        {
            var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
            foreach (var remove in plan.RemoveOnce)
            {
                actual.Remove(remove);
            }

            actual.AddRange(plan.AppendOnce);
            Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
            Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountDuplicates(actual));
            Assert.Equal(expected.Count, actual.Count);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Plan_SequentialRepairsDoNotGrowCount(int surplusCopies)
    {
        var expected = new HashSet<string>(["Owner", "Disp", "Gen", "Ann"], StringComparer.Ordinal);
        var actual = expected.ToList();
        for (var i = 0; i < surplusCopies; i++)
        {
            actual.Add("Gen");
        }

        var before = actual.Count;
        Assert.True(before > expected.Count);
        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
        foreach (var remove in plan.RemoveOnce)
        {
            actual.Remove(remove);
        }

        actual.AddRange(plan.AppendOnce);
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountDuplicates(actual));
    }

    [Fact]
    public void ToggleCycles_KeepCleanOwnerExactlyOnceAndCountStable()
    {
        var expected = new HashSet<string>(
            ["Owner", "Display", "Generated", "Annotation"],
            StringComparer.Ordinal);
        var actual = expected.ToList();

        for (var cycle = 0; cycle < 6; cycle++)
        {
            ApplyCanonicalization(actual, expected);
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(1, actual.Count(id => id == "Owner"));
            Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        }
    }

    [Fact]
    public void ToggleRepair_RemovesDuplicateOwnerBeforeStrictCanonicalPasses()
    {
        var expected = new HashSet<string>(
            ["Owner", "Display", "Generated", "Annotation"],
            StringComparer.Ordinal);
        var actual = new List<string>
        {
            "Owner", "Owner", "Display", "Generated", "Annotation",
        };

        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        Assert.Equal(1, RoofAssemblyGroupMembershipRules.CountDuplicates(actual));

        ApplyCanonicalization(actual, expected);

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(1, actual.Count(id => id == "Owner"));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
    }

    [Fact]
    public void ToggleRepair_StillRemovesDuplicateGeneratedChild()
    {
        var expected = new HashSet<string>(
            ["Owner", "Display", "Generated", "Annotation"],
            StringComparer.Ordinal);
        var actual = new List<string>
        {
            "Owner", "Display", "Generated", "Generated", "Annotation",
        };

        ApplyCanonicalization(actual, expected);

        Assert.Equal(1, actual.Count(id => id == "Generated"));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
    }

    [Fact]
    public void DisplayOnlyPartialGroup_ExpandsExactlyToExpectedWithoutDroppingOwner()
    {
        var expected = new HashSet<string>(
            ["Owner", "D0", "D1", "G0", "S0", "A0"],
            StringComparer.Ordinal);
        var actual = new List<string> { "Owner", "D0", "D1" };

        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
        Assert.Empty(plan.RemoveOnce);
        Assert.Equal(["A0", "G0", "S0"], plan.AppendOnce.OrderBy(id => id, StringComparer.Ordinal));

        actual.AddRange(plan.AppendOnce);
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        Assert.Equal(1, actual.Count(id => id == "Owner"));
    }

    private static void ApplyCanonicalization<T>(
        List<T> actual,
        IReadOnlyCollection<T> expected)
        where T : notnull
    {
        var plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
        foreach (var remove in plan.RemoveOnce)
        {
            actual.Remove(remove);
        }

        actual.AddRange(plan.AppendOnce);
    }
}
