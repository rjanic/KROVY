using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofIndependentOrdinaryDesignationTests
{
    [Theory]
    [InlineData(1000, 1000, true)]
    [InlineData(900, 1200, false)]
    public void TwoSplitPieces_UseNormalManufacturingGroups(double first, double second, bool same)
    {
        var result = Assign((Measure("K4", first, 45), true), (Measure("K4", second, 45), true),
            (Measure("K4", 3000, 45), false));
        Assert.Equal(same, result[0].ElementId == result[1].ElementId);
        Assert.NotEqual("K4", result[0].ElementId);
        Assert.NotEqual("K4", result[1].ElementId);
        Assert.Equal("K4", result[2].ElementId);
        AssertUniqueSignatures(result);
    }

    [Fact]
    public void ShortenedHostMember_SplitsFromUnchangedK4_ByCurrentCuttingLength()
    {
        var original = Measure("K4", 3000, 45);
        var edited = Measure("K4", 1451, Math.Acos(1451 / 1954.7) * 180 / Math.PI);
        var result = Assign((edited, true), (original, false));

        Assert.Equal(4350, original.CuttingLengthMm);
        Assert.Equal(2100, edited.CuttingLengthMm);
        Assert.Equal(1954.7, edited.ActualLengthMm, 6);
        Assert.NotEqual("K4", result[0].ElementId);
        Assert.Equal("K4", result[1].ElementId);
        Assert.Equal(80, result[0].Measurement.Data.WidthMm);
        Assert.Equal(160, result[0].Measurement.Data.HeightMm);
        AssertUniqueSignatures(result);
    }

    [Fact]
    public void ChangedMemberFirst_MatchingExistingSignature_ReusesExistingGroup()
    {
        var result = Assign((Measure("K4", 1400, 45), true),
            (Measure("K4", 3000, 45), false), (Measure("K7", 1400, 45), false));

        Assert.Equal(new[] { "K7", "K4", "K7" }, result.Select(item => item.ElementId));
        AssertUniqueSignatures(result);
    }

    [Fact]
    public void SignaturePreservingLengthEdit_KeepsDesignation()
    {
        var before = Measure("K7", 1400, 45);
        var after = Measure("K7", 1401, 45);
        Assert.NotEqual(before.ActualLengthMm, after.ActualLengthMm);
        Assert.Equal(TimberElementSignature.FromMeasurement(before), TimberElementSignature.FromMeasurement(after));
        var result = Assign((after, TimberElementSignature.FromMeasurement(before) !=
            TimberElementSignature.FromMeasurement(after)));
        Assert.Equal("K7", Assert.Single(result).ElementId);
    }

    [Fact]
    public void RigidMove_UnchangedMeasurement_KeepsDesignation()
    {
        // Plan endpoints/placement are deliberately absent from the manufacturing signature.
        var original = Measure("K4", 3000, 45);
        var translated = Measure("K4", 3000, 45);
        var result = Assign((translated, TimberElementSignature.FromMeasurement(original) !=
            TimberElementSignature.FromMeasurement(translated)));
        Assert.Equal("K4", Assert.Single(result).ElementId);
    }

    [Fact]
    public void SoleChangedMember_DoesNotInheritItsFormerDesignation()
    {
        var result = Assign((Measure("K4", 1400, 45), true));
        Assert.NotEqual("K4", Assert.Single(result).ElementId);
    }

    [Fact]
    public void MultipleAcceptedEdits_SharingNewSignature_GetOneNewDesignation()
    {
        var result = Assign((Measure("K4", 1400, 45), true), (Measure("K5", 1400, 45), true),
            (Measure("K4", 3000, 45), false), (Measure("K5", 4000, 45), false));
        Assert.Equal(result[0].ElementId, result[1].ElementId);
        Assert.DoesNotContain(result[0].ElementId, new[] { "K4", "K5" });
        Assert.Equal("K4", result[2].ElementId);
        Assert.Equal("K5", result[3].ElementId);
        AssertUniqueSignatures(result);
    }

    [Fact]
    public void CurrentAutoReplacement_AndIndependent_AreGroupedByTheirOwnMeasurements()
    {
        var result = Assign((Measure("K4", 1400, 45), true),
            (Measure("K4", 3000, 45), false));
        Assert.Equal("K4", result[1].ElementId);
        Assert.NotEqual(result[1].ElementId, result[0].ElementId);
    }

    [Fact]
    public void NewDesignation_UsesExistingSeriesAllocation_WithoutCompactingGroups()
    {
        var result = Assign((Measure("K4", 1400, 45), true),
            (Measure("K1", 1000, 45), false), (Measure("K2", 2000, 45), false),
            (Measure("K3", 2500, 45), false), (Measure("K4", 3000, 45), false),
            (Measure("K9", 4500, 45), false));
        Assert.Equal(new[] { "K5", "K1", "K2", "K3", "K4", "K9" }, result.Select(item => item.ElementId));
    }

    private static IReadOnlyList<TimberElementItemAssignment> Assign(
        params (TimberElementMeasurement Measurement, bool Changed)[] members) =>
        TimberElementItemNumbering.AssignElementIdsAfterGeometryEdit(members.Select(member =>
            new TimberElementItemNumberingCandidate(member.Measurement, member.Changed)));

    private static TimberElementMeasurement Measure(string id, double planLength, double slope) =>
        TimberCalculator.Measure(new TimberElementData
        {
            ElementId = id, ElementType = TimberElementType.Rafter, WidthMm = 80, HeightMm = 160,
            SlopeDegrees = slope, CuttingAllowanceMm = 100, Material = "Smrek C24",
        }, planLength, 50);

    private static void AssertUniqueSignatures(IEnumerable<TimberElementItemAssignment> assignments)
    {
        foreach (var group in assignments.GroupBy(item => item.ElementId))
            Assert.Single(group.Select(item => item.Signature).Distinct());
    }
}
