using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberEndpointRecalcNumberingTests
{
    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);

    [Fact]
    public void TrimCommand_AndExtendCommand_AreEndpointRecalcCommands()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("EXTEND"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTrimCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsExtendCommand("EXTEND"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsExtendCommand("_EXTEND"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsExtendCommand("TRIM"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsTrimCommand("EXTEND"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("MOVE"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("ROTATE"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("GRIP_STRETCH"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("ERASE"));
    }

    [Fact]
    public void TrimmedPlanLength_RecalculatesSlopeAwareCuttingLengthAndVolume()
    {
        var data = Rafter("K7");
        var before = TimberCalculator.Measure(data, 2800d);
        var after = TimberCalculator.Measure(data, 2500d);
        Assert.Equal("K7", after.Data.ElementId);
        Assert.Equal(2500d, after.PlanLengthMm);
        Assert.Equal(TimberCalculator.CalculateSlopeCorrectedLengthMm(2500d, 35d), after.ActualLengthMm, 6);
        Assert.True(after.CuttingLengthMm < before.CuttingLengthMm);
        Assert.True(after.VolumeM3 < before.VolumeM3);
        Assert.True(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(
            TimberElementSignature.FromMeasurement(before),
            TimberElementSignature.FromMeasurement(after)));
    }

    [Fact]
    public void ExtendedPlanLength_RecalculatesSlopeAwareCuttingLengthAndVolume()
    {
        var data = Rafter("K4");
        var before = TimberCalculator.Measure(data, 2500d);
        var after = TimberCalculator.Measure(data, 2800d);
        Assert.Equal(2800d, after.PlanLengthMm);
        Assert.True(after.CuttingLengthMm > before.CuttingLengthMm);
        Assert.True(after.VolumeM3 > before.VolumeM3);
        Assert.Equal(
            TimberCalculator.CalculateSlopeCorrectedLengthMm(2800d, 35d),
            after.ActualLengthMm,
            6);
    }

    [Fact]
    public void OneChangedMember_DoesNotMarkUnchangedNeighborsForRecalc()
    {
        var canonical = Horizontal(2800d);
        var trimmed = new RoofGeneratedMemberGeometry(canonical.Start, new(2500d, 0d, 0d));
        var members = new[] { canonical, trimmed, canonical, canonical };
        var changed = members.Count(item =>
            RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(canonical, item));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void MultipleChangedMembers_AreAllSelected()
    {
        var canonical = Horizontal(2800d);
        var first = new RoofGeneratedMemberGeometry(canonical.Start, new(2500d, 0d, 0d));
        var second = new RoofGeneratedMemberGeometry(canonical.Start, new(2400d, 0d, 0d));
        var third = new RoofGeneratedMemberGeometry(canonical.Start, new(2300d, 0d, 0d));
        var observed = new[] { first, canonical, second, canonical, third };
        Assert.Equal(3, observed.Count(item =>
            RoofGeneratedMemberRecalcScopeRules.RequiresRecalculation(canonical, item)));
    }

    [Fact]
    public void TrimmedMember_JoinsExistingEquivalentSignatureGroup()
    {
        var existing = Measurement("K3", 2500d);
        var neighbor = Measurement("K3", 2500d);
        var trimmed = Measurement("K7", 2500d);
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(existing, false),
            new TimberElementItemNumberingCandidate(neighbor, false),
            new TimberElementItemNumberingCandidate(trimmed, true),
        ]);
        Assert.Equal("K3", result[0].ElementId);
        Assert.Equal("K3", result[1].ElementId);
        Assert.Equal("K3", result[2].ElementId);
        Assert.Equal(result[0].Signature, result[2].Signature);
        Assert.Equal("K7", trimmed.Data.ElementId);
        Assert.NotEqual(trimmed.Data.ElementId, result[2].ElementId);
    }

    [Fact]
    public void TrimmedMember_LeavesOldGroup_AndKeepsRemainingMemberNumber()
    {
        var remaining = Measurement("K3", 2800d);
        var trimmed = Measurement("K3", 2500d);
        var existingShort = Measurement("K5", 2500d);
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(remaining, false),
            new TimberElementItemNumberingCandidate(existingShort, false),
            new TimberElementItemNumberingCandidate(trimmed, true),
        ]);
        Assert.Equal("K3", result[0].ElementId);
        Assert.Equal("K5", result[1].ElementId);
        Assert.Equal("K5", result[2].ElementId);
        Assert.Equal(result[1].Signature, result[2].Signature);
        Assert.NotEqual(result[0].Signature, result[2].Signature);
    }

    [Fact]
    public void TrimmedMember_WithoutExistingTarget_GetsDeterministicFreeNumber()
    {
        var remaining = Measurement("K3", 2800d);
        var other = Measurement("K4", 2600d);
        var trimmed = Measurement("K3", 2500d);
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(remaining, false),
            new TimberElementItemNumberingCandidate(other, false),
            new TimberElementItemNumberingCandidate(trimmed, true),
        ]);
        Assert.Equal("K3", result[0].ElementId);
        Assert.Equal("K4", result[1].ElementId);
        Assert.Equal("K1", result[2].ElementId);
    }

    [Fact]
    public void TwoEditedMembers_ConvergeToSameSignatureAndNumber()
    {
        var existing = Measurement("K3", 2500d);
        var first = Measurement("K5", 2500d);
        var second = Measurement("K6", 2500d);
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(existing, false),
            new TimberElementItemNumberingCandidate(first, true),
            new TimberElementItemNumberingCandidate(second, true),
        ]);
        Assert.Equal(new[] { "K3", "K3", "K3" }, result.Select(item => item.ElementId));
        Assert.Equal(result[1].Signature, result[2].Signature);
    }

    [Fact]
    public void ExtendBackToOriginalSignature_RejoinsOriginalNumber()
    {
        var original = Measurement("K3", 2800d);
        var shortened = Measurement("K7", 2500d);
        var afterTrim = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(original, false),
            new TimberElementItemNumberingCandidate(shortened, true),
        ]);
        Assert.Equal("K3", afterTrim[0].ElementId);
        Assert.NotEqual("K3", afterTrim[1].ElementId);

        var restored = Measurement(afterTrim[1].ElementId, 2800d);
        var afterExtend = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(Measurement("K3", 2800d), false),
            new TimberElementItemNumberingCandidate(restored, true),
        ]);
        Assert.Equal("K3", afterExtend[0].ElementId);
        Assert.Equal("K3", afterExtend[1].ElementId);
        Assert.Equal(afterExtend[0].Signature, afterExtend[1].Signature);
    }

    [Fact]
    public void UnchangedSignature_DoesNotRequireNumberingSynchronization()
    {
        var data = Rafter("K3");
        var first = RoofGeneratedMemberRecalcScopeRules.SignatureFrom(data, 2800d);
        var second = RoofGeneratedMemberRecalcScopeRules.SignatureFrom(data, 2800d);
        Assert.False(RoofGeneratedMemberRecalcScopeRules.RequiresNumberingSynchronization(first, second));
        Assert.Equal(0, RoofGeneratedMemberRecalcScopeRules.CountAffectedSignatureGroups(
        [
            new RoofGeneratedMemberSignatureTransition(first, second),
        ]));
    }

    [Fact]
    public void SignatureMerge_CountsTwoAffectedGroups()
    {
        var longSig = RoofGeneratedMemberRecalcScopeRules.SignatureFrom(Rafter("K7"), 2800d);
        var shortSig = RoofGeneratedMemberRecalcScopeRules.SignatureFrom(Rafter("K3"), 2500d);
        Assert.Equal(2, RoofGeneratedMemberRecalcScopeRules.CountAffectedSignatureGroups(
        [
            new RoofGeneratedMemberSignatureTransition(longSig, shortSig),
        ]));
    }

    [Fact]
    public void ElementId_IsTheDisplayedItemNumber_NotPhysicalIdentity()
    {
        var remaining = Measurement("K3", 2800d);
        var trimmed = Measurement("K7", 2500d);
        var existingShort = Measurement("K3", 2500d);
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(existingShort, false),
            new TimberElementItemNumberingCandidate(remaining, false),
            new TimberElementItemNumberingCandidate(trimmed, true),
        ]);
        Assert.Equal("K3", result[0].ElementId);
        Assert.Equal("K7", trimmed.Data.ElementId);
        Assert.Equal("K3", result[2].ElementId);
        Assert.Equal(
            new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 7),
            new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 7));
    }

    [Fact]
    public void ReportGrouping_MergesEquivalentSignaturesToSharedNumber()
    {
        var existing = WithAssigned(Measurement("K3", 2500d), "K3");
        var trimmed = WithAssigned(Measurement("K7", 2500d), "K3");
        var other = WithAssigned(Measurement("K4", 2800d), "K4");
        var report = TimberReportBuilder.Build([existing, trimmed, other]);
        var shortGroup = Assert.Single(report.Lines, line => line.CuttingLengthMm == existing.CuttingLengthMm);
        Assert.Equal("K3", shortGroup.ElementId);
        Assert.Equal(2, shortGroup.Count);
        Assert.Equal(existing.VolumeM3 + trimmed.VolumeM3, shortGroup.TotalVolumeM3, 12);
        Assert.Contains(report.Lines, line => line.ElementId == "K4" && line.Count == 1);
    }

    [Fact]
    public void AssignElementIds_AfterEndpointSync_IsIdempotent()
    {
        var existing = Measurement("K3", 2500d);
        var trimmed = Measurement("K7", 2500d);
        var first = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(existing, false),
            new TimberElementItemNumberingCandidate(trimmed, true),
        ]);
        var second = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(WithAssigned(existing, first[0].ElementId), true),
            new TimberElementItemNumberingCandidate(WithAssigned(trimmed, first[1].ElementId), true),
        ]);
        Assert.Equal(first.Select(item => item.ElementId), second.Select(item => item.ElementId));
        Assert.Equal(first.Select(item => item.Signature), second.Select(item => item.Signature));
    }

    [Fact]
    public void EndpointOverride_NormalizesTowardZero_WhenExtendedBack()
    {
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face1, 7);
        var canonical = Horizontal(2800d);
        var trimmed = new RoofGeneratedMemberGeometry(canonical.Start, new(2500d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, trimmed, ZUp, out var startDelta, out var endDelta, out var acceptedTrim, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, key, "K7", startDelta, endDelta);
        Assert.NotNull(composed);
        Assert.True(composed.HasGeometryOverride);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var baseline));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(acceptedTrim, baseline));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, canonical, ZUp, out var backStart, out var backEnd, out _, out _));
        var restored = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            composed, key, "K7", backStart, backEnd);
        Assert.True(restored is null || !restored.HasGeometryOverride);
    }

    [Fact]
    public void LabelFormatter_UsesSynchronizedElementId()
    {
        var data = Rafter("K3") with { AnnotationMode = TimberAnnotationMode.ItemNumberLeader };
        var measurement = TimberCalculator.Measure(data, 2500d);
        Assert.Equal("K3", TimberMainAnnotationFormatter.Format(data, measurement));
        Assert.NotEqual("K7", TimberMainAnnotationFormatter.Format(data, measurement));
    }

    [Fact]
    public void DifferentMaterialOrSection_DoNotShareNumberEvenAtSameLength()
    {
        var sameLength = Measurement("K3", 2500d);
        var otherMaterial = Measurement("K8", 2500d) with
        {
            Data = Measurement("K8", 2500d).Data with { Material = "Dub C24" },
        };
        var otherSection = Measurement("K9", 2500d) with
        {
            Data = Measurement("K9", 2500d).Data with { WidthMm = 100d },
        };
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(sameLength, false),
            new TimberElementItemNumberingCandidate(otherMaterial, true),
            new TimberElementItemNumberingCandidate(otherSection, true),
        ]);
        Assert.Equal("K3", result[0].ElementId);
        Assert.NotEqual("K3", result[1].ElementId);
        Assert.NotEqual("K3", result[2].ElementId);
        Assert.NotEqual(result[1].ElementId, result[2].ElementId);
        Assert.NotEqual(result[0].Signature, result[1].Signature);
        Assert.NotEqual(result[0].Signature, result[2].Signature);
    }

    [Fact]
    public void K1K2K10_SortAndGapSemantics_RemainStable()
    {
        var result = TimberElementItemNumbering.AssignElementIds(
        [
            new TimberElementItemNumberingCandidate(Measurement("K1", 1000d), false),
            new TimberElementItemNumberingCandidate(Measurement("K2", 2000d), false),
            new TimberElementItemNumberingCandidate(Measurement("K10", 3000d), false),
            new TimberElementItemNumberingCandidate(Measurement("K4", 4000d), true),
        ]);
        Assert.Equal(new[] { "K1", "K2", "K10", "K4" }, result.Select(item => item.ElementId));
        var compacted = TimberElementItemNumbering.RenumberElementIdsByCuttingLength(
            result.Select(item => WithAssigned(item.Measurement, item.ElementId)));
        Assert.Equal(new[] { "K1", "K2", "K3", "K4" }, compacted.Select(item => item.ElementId));
    }

    private static RoofGeneratedMemberGeometry Horizontal(double length) =>
        new(new RoofPoint3D(0d, 0d, 0d), new RoofPoint3D(length, 0d, 0d));

    private static TimberElementData Rafter(string elementId) =>
        TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = elementId,
            SlopeDegrees = 35d,
        };

    private static TimberElementMeasurement Measurement(string elementId, double planLengthMm) =>
        TimberCalculator.Measure(Rafter(elementId), planLengthMm);

    private static TimberElementMeasurement WithAssigned(
        TimberElementMeasurement measurement,
        string elementId) =>
        measurement with { Data = measurement.Data with { ElementId = elementId } };
}
