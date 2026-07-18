using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementCopyInitializationRulesTests
{
    [Fact]
    public void ShouldInitializeAsNewPhysicalElement_DetectsLocalCopyFromExistingSourceLabel()
    {
        var result = TimberElementCopyInitializationRules.ShouldInitializeAsNewPhysicalElement(
            currentSourceHandle: "NEW",
            elementId: "K1",
            labelCandidates: new[] { Label("L1", "K1", "OLD") },
            existingTimberSourceHandles: new[] { "OLD", "NEW" });

        Assert.True(result);
    }

    [Fact]
    public void ShouldInitializeAsNewPhysicalElement_DoesNotInitializeWhenCurrentSourceLabelExists()
    {
        var result = TimberElementCopyInitializationRules.ShouldInitializeAsNewPhysicalElement(
            currentSourceHandle: "NEW",
            elementId: "K1",
            labelCandidates: new[]
            {
                Label("L1", "K1", "OLD"),
                Label("L2", "K1", "NEW"),
            },
            existingTimberSourceHandles: new[] { "OLD", "NEW" });

        Assert.False(result);
    }

    [Fact]
    public void ShouldInitializeAsNewPhysicalElement_ProtectsImportedDataWhenOriginalHandleIsNotInDrawing()
    {
        var result = TimberElementCopyInitializationRules.ShouldInitializeAsNewPhysicalElement(
            currentSourceHandle: "NEW",
            elementId: "K1",
            labelCandidates: new[] { Label("L1", "K1", "OLD") },
            existingTimberSourceHandles: new[] { "NEW" });

        Assert.False(result);
    }

    [Fact]
    public void CopiedElement_UsesCurrentDefaultByElementTypeAndOriginalStaysUnchanged()
    {
        var original = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K1",
            CuttingAllowanceMm = 100,
        };
        var copiedMetadata = original;
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 400),
            },
        };

        var initializedCopy = TimberElementDefaultApplicator.ApplyCuttingAllowance(copiedMetadata, profile);

        Assert.Equal(100, original.CuttingAllowanceMm);
        Assert.Equal(400, initializedCopy.CuttingAllowanceMm);
    }

    [Fact]
    public void CopiedElement_NewAllowanceRecalculatesCuttingLength()
    {
        var copy = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            TimberElementDefaults.For(TimberElementType.Rafter) with
            {
                LengthCalculationMode = LengthCalculationMode.PlanLength,
                CuttingAllowanceMm = 100,
            },
            new TimberElementDefaultProfile
            {
                Styles = new List<TimberElementDefaultStyle>
                {
                    new(TimberElementType.Rafter, 400),
                },
            });

        var measurement = TimberCalculator.Measure(copy, planLengthMm: 5000);

        Assert.Equal(5400, measurement.CuttingLengthMm);
    }

    [Fact]
    public void CopiedElement_SameResultingSignatureCanKeepSameElementId()
    {
        var original = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K1",
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            CuttingAllowanceMm = 100,
        };
        var copy = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            original,
            new TimberElementDefaultProfile
            {
                Styles = new List<TimberElementDefaultStyle>
                {
                    new(TimberElementType.Rafter, 120),
                },
            });

        var assignments = TimberElementItemNumbering.AssignElementIds(new[]
        {
            TimberCalculator.Measure(original, 5000),
            TimberCalculator.Measure(copy, 4975),
        });

        Assert.Equal(5100, assignments[0].Signature.CuttingLengthMm);
        Assert.Equal(5100, assignments[1].Signature.CuttingLengthMm);
        Assert.Equal(new[] { "K1", "K1" }, assignments.Select(assignment => assignment.ElementId));
    }

    [Fact]
    public void CopiedElement_DifferentResultingSignatureGetsDifferentElementId()
    {
        var original = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K1",
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            CuttingAllowanceMm = 100,
        };
        var copy = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            original,
            new TimberElementDefaultProfile
            {
                Styles = new List<TimberElementDefaultStyle>
                {
                    new(TimberElementType.Rafter, 400),
                },
            });

        var assignments = TimberElementItemNumbering.AssignElementIds(new[]
        {
            TimberCalculator.Measure(original, 5000),
            TimberCalculator.Measure(copy, 5000),
        });

        Assert.Equal(5100, assignments[0].Signature.CuttingLengthMm);
        Assert.Equal(5400, assignments[1].Signature.CuttingLengthMm);
        Assert.Equal(new[] { "K1", "K2" }, assignments.Select(assignment => assignment.ElementId));
    }

    private static TimberElementLabelCandidate Label(string key, string elementId, string sourceHandle) =>
        new()
        {
            LabelKey = key,
            ElementId = elementId,
            SourceHandle = sourceHandle,
        };
}
