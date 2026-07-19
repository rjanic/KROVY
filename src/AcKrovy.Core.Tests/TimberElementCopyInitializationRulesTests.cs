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
    public void CopiedElement_PreservesSourceCuttingAllowanceWhenDefaultDiffers()
    {
        var original = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K1",
            CuttingAllowanceMm = 300,
        };
        var copiedMetadata = original;
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 400),
            },
        };

        Assert.Equal(300, original.CuttingAllowanceMm);
        Assert.Equal(300, copiedMetadata.CuttingAllowanceMm);
        Assert.Equal(400, profile.GetCuttingAllowanceMm(TimberElementType.Rafter));
    }

    [Fact]
    public void CopiedElement_PreservesZeroCuttingAllowanceWhenDefaultDiffers()
    {
        var original = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K1",
            CuttingAllowanceMm = 0,
        };
        var copiedMetadata = original;
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 100),
            },
        };

        Assert.Equal(0, copiedMetadata.CuttingAllowanceMm);
        Assert.Equal(100, profile.GetCuttingAllowanceMm(TimberElementType.Rafter));
    }

    [Fact]
    public void CopiedElement_PreservesCuttingAllowanceWhenItEqualsDefault()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 100),
            },
        };
        var original = TimberElementDefaults.For(TimberElementType.Rafter, profile) with
        {
            ElementId = "K1",
            CuttingAllowanceMm = 100,
        };
        var copiedMetadata = original;

        Assert.Equal(profile.GetCuttingAllowanceMm(TimberElementType.Rafter), copiedMetadata.CuttingAllowanceMm);
    }

    [Fact]
    public void NewAssign_UsesCurrentDefaultByElementType()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 300),
            },
        };

        var assigned = TimberElementDefaults.For(TimberElementType.Rafter, profile);

        Assert.Equal(300, assigned.CuttingAllowanceMm);
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
        var copy = original;

        var assignments = TimberElementItemNumbering.AssignElementIds(new[]
        {
            TimberCalculator.Measure(original, 5000),
            TimberCalculator.Measure(copy, 5000),
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
        var copy = original;

        var assignments = TimberElementItemNumbering.AssignElementIds(new[]
        {
            TimberCalculator.Measure(original, 5000),
            TimberCalculator.Measure(copy, 5300),
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
