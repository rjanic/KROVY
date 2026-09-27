using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Membership arithmetic only; native transaction ordering requires HOST evidence.</summary>
public sealed class RoofLockedSourceGroupFinalizationTests
{
    [Fact]
    public void ProvisionalSix_DoesNotValidateSevenAfterNativeRestore()
    {
        var expected = Fixture("293C");
        var provisional = expected.ToList();
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(provisional, expected));
        provisional.Add("293C");
        Assert.Equal(7, provisional.Count);
        Assert.Equal(6, provisional.Distinct().Count());
        Assert.Equal(2, provisional.Count(id => id == "293C"));
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(provisional, expected));
    }

    [Fact]
    public void FirstAndRepeatedRepair_NormalizesCommittedSlotsAndPreservesOtherOwner()
    {
        var original = Fixture("2912");
        var originalBefore = original.ToArray();
        var expected = Fixture("293C");
        var actual = expected.ToList();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            // Model the supplied failure arithmetic, not AutoCAD event execution.
            actual.Remove("293C");
            actual.Add("293C"); // Provisional repair restores the source slot.
            actual.Add("293C");
            Assert.Equal(7, actual.Count);
            RemoveSurplusSourceSlots(actual, "293C");
            Assert.Equal(6, actual.Count);
            Assert.Equal(6, actual.Distinct().Count());
            Assert.Single(actual, id => id == "293C");
            Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
            var secondPass = RoofAssemblyGroupMembershipRules.PlanCanonicalization(actual, expected);
            Assert.Empty(secondPass.RemoveOnce);
            Assert.Empty(secondPass.AppendOnce);
            Assert.Equal(originalBefore, original);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void IndexedRemoval_PreservesEveryUniqueMemberAndItsOrder(int extraSlots)
    {
        var expected = Fixture("293C");
        var actual = expected.Concat(Enumerable.Repeat("293C", extraSlots)).ToList();
        RemoveSurplusSourceSlots(actual, "293C");
        Assert.Equal(expected, actual);
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        Assert.Equal(6, actual.Count);
        Assert.Single(actual, id => id == "293C");
    }

    [Fact]
    public void CompleteTimberPurlinAnnotationAssembly_IsNotReducedToSix()
    {
        var expected = Fixture("293C").Concat(new[] { "Purlin", "Rafter", "Annotation" }).ToArray();
        var actual = expected.Concat(new[] { "293C" }).ToList();
        RemoveSurplusSourceSlots(actual, "293C");
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
        Assert.Equal(9, actual.Count);
        Assert.Contains("Purlin", actual);
        Assert.Contains("Annotation", actual);
        Assert.Single(actual, id => id == "293C");
    }

    private static string[] Fixture(string owner) =>
        new[] { owner }.Concat(Enumerable.Range(0, 5).Select(i => owner + ":Display:" + i)).ToArray();

    [Fact]
    public void SourceSlotPlan_NeverTargetsUniqueOrOtherDuplicateMembers()
    {
        var actual = new List<string> { "D0", "293C", "D1", "Foreign", "D1", "293C", "293C" };
        Assert.Equal(new[] { 5, 6 }, RoofAssemblyGroupMembershipRules.SurplusMemberIndices(actual, "293C"));
        RemoveSurplusSourceSlots(actual, "293C");
        Assert.Equal(new[] { "D0", "293C", "D1", "Foreign", "D1" }, actual);
    }

    private static void RemoveSurplusSourceSlots(List<string> actual, string owner)
    {
        foreach (var index in RoofAssemblyGroupMembershipRules.SurplusMemberIndices(actual, owner).Reverse())
            actual.RemoveAt(index);
    }
}
