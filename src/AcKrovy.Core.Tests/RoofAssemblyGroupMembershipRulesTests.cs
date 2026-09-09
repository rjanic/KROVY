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
}
