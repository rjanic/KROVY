using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementEditRulesTests
{
    [Fact]
    public void EmptyPatch_ProducesNoWritesOrAnnotationRefresh()
    {
        var result = Execute([Source("K1")], EmptyPatch());

        Assert.Equal(0, result.MetadataWrites);
        Assert.Equal(0, result.AnnotationRefreshes);
        Assert.Equal(0, result.ChangedElements);
    }

    [Fact]
    public void CheckedFieldWithSameValue_ProducesNoEffectiveChange()
    {
        var source = Source("K1");
        var result = Execute(
            [source],
            EmptyPatch() with { WidthMm = source.WidthMm });

        Assert.Equal(0, result.MetadataWrites);
        Assert.Equal(0, result.AnnotationRefreshes);
        Assert.Equal(0, result.ChangedElements);
    }

    [Fact]
    public void OneRealChange_ProducesExactlyOneWriteAndOneChangedElement()
    {
        var result = Execute(
            [Source("K1")],
            EmptyPatch() with { WidthMm = 120 });

        Assert.Equal(1, result.MetadataWrites);
        Assert.Equal(1, result.AnnotationRefreshes);
        Assert.Equal(1, result.ChangedElements);
    }

    [Fact]
    public void BatchEdit_WritesOnlyElementsWhoseResultActuallyChanges()
    {
        var result = Execute(
            [
                Source("K1") with { WidthMm = 80 },
                Source("K2") with { WidthMm = 120 },
                Source("K3") with { WidthMm = 80 },
            ],
            EmptyPatch() with { WidthMm = 120 });

        Assert.Equal(2, result.MetadataWrites);
        Assert.Equal(1, result.AnnotationRefreshes);
        Assert.Equal(2, result.ChangedElements);
    }

    [Fact]
    public void SameSelectedValue_DoesNotTriggerIncidentalMetadataRepair()
    {
        var source = Source("K1") with
        {
            ElementType = TimberElementType.Rafter,
            CustomElementTypeId = "legacy-custom",
            CustomElementTypeName = "Legacy",
            CustomElementTypePrefix = "L",
        };

        var changed = TimberElementEditRules.TryCreateEffectiveChange(
            source,
            EmptyPatch() with { WidthMm = source.WidthMm },
            useDefaultCuttingAllowanceByType: false,
            TimberElementDefaultProfile.CreateDefault(),
            out var updated);

        Assert.False(changed);
        Assert.Same(source, updated);
    }

    [Fact]
    public void RequestedTypeDefault_IsEffectiveOnlyWhenAllowanceDiffers()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        var source = Source("K1") with
        {
            CuttingAllowanceMm = profile.GetCuttingAllowanceMm(TimberElementType.Rafter),
        };

        Assert.False(TimberElementEditRules.TryCreateEffectiveChange(
            source,
            EmptyPatch(),
            useDefaultCuttingAllowanceByType: true,
            profile,
            out _));

        Assert.True(TimberElementEditRules.TryCreateEffectiveChange(
            source with { CuttingAllowanceMm = source.CuttingAllowanceMm + 10 },
            EmptyPatch(),
            useDefaultCuttingAllowanceByType: true,
            profile,
            out _));
    }

    private static ExecutionResult Execute(
        IReadOnlyList<TimberElementData> sources,
        TimberElementPatch patch)
    {
        var metadataWrites = 0;
        var changedElements = 0;
        var profile = TimberElementDefaultProfile.CreateDefault();

        foreach (var source in sources)
        {
            if (!TimberElementEditRules.TryCreateEffectiveChange(
                    source,
                    patch,
                    useDefaultCuttingAllowanceByType: false,
                    profile,
                    out _))
            {
                continue;
            }

            metadataWrites++;
            changedElements++;
        }

        return new ExecutionResult(
            metadataWrites,
            changedElements > 0 ? 1 : 0,
            changedElements);
    }

    private static TimberElementPatch EmptyPatch() => new(
        ElementType: null,
        WidthMm: null,
        HeightMm: null,
        SlopeDegrees: null,
        RoofPlaneId: null,
        CuttingAllowanceMm: null,
        LengthCalculationMode: null,
        ManualLengthMm: null,
        Material: null,
        Note: null);

    private static TimberElementData Source(string elementId) => new()
    {
        SchemaVersion = TimberElementDataSchema.CurrentVersion,
        ElementId = elementId,
        ElementType = TimberElementType.Rafter,
        WidthMm = 80,
        HeightMm = 160,
        SlopeDegrees = 35,
        RoofPlaneId = "R1",
        CuttingAllowanceMm = 100,
        LengthCalculationMode = LengthCalculationMode.AutoByElementType,
        Material = "Smrek C24",
    };

    private sealed record ExecutionResult(
        int MetadataWrites,
        int AnnotationRefreshes,
        int ChangedElements);
}
