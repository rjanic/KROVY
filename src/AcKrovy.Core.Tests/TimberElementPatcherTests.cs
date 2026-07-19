using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementPatcherTests
{
    [Fact]
    public void Apply_EmptyPatchKeepsSourceUnchanged()
    {
        var source = Source();
        var patch = EmptyPatch();

        var result = TimberElementPatcher.Apply(source, patch);

        Assert.Equal(source, result);
    }

    [Fact]
    public void Apply_ChangesOnlyElementType()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            ElementType = TimberElementType.Purlin,
        });

        AssertPatched(result, value => Assert.Equal(TimberElementType.Purlin, value.ElementType));
    }

    [Fact]
    public void Apply_ChangesOnlyWidth()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            WidthMm = 120,
        });

        AssertPatched(result, value => Assert.Equal(120, value.WidthMm));
    }

    [Fact]
    public void Apply_ChangesOnlyHeight()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            HeightMm = 240,
        });

        AssertPatched(result, value => Assert.Equal(240, value.HeightMm));
    }

    [Fact]
    public void Apply_ChangesOnlySlope()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            SlopeDegrees = 42,
        });

        AssertPatched(result, value => Assert.Equal(42, value.SlopeDegrees));
    }

    [Fact]
    public void Apply_ChangesOnlySlopeDirection()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            IsSlopeDirectionReversed = true,
        });

        AssertPatched(result, value => Assert.True(value.IsSlopeDirectionReversed));
    }

    [Fact]
    public void Apply_ChangesOnlyCuttingAllowance()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            CuttingAllowanceMm = 150,
        });

        AssertPatched(result, value => Assert.Equal(150, value.CuttingAllowanceMm));
    }

    [Fact]
    public void Apply_NullCuttingAllowanceKeepsOriginalValueForMixedEdit()
    {
        var source = Source() with { CuttingAllowanceMm = 275 };

        var result = TimberElementPatcher.Apply(source, EmptyPatch());

        Assert.Equal(275, result.CuttingAllowanceMm);
    }

    [Fact]
    public void Apply_SameCuttingAllowanceCanBeAppliedToMultipleElements()
    {
        var first = Source() with { CuttingAllowanceMm = 100 };
        var second = Source() with { ElementId = "K8", CuttingAllowanceMm = 200 };
        var patch = EmptyPatch() with { CuttingAllowanceMm = 300 };

        var firstResult = TimberElementPatcher.Apply(first, patch);
        var secondResult = TimberElementPatcher.Apply(second, patch);

        Assert.Equal(300, firstResult.CuttingAllowanceMm);
        Assert.Equal(300, secondResult.CuttingAllowanceMm);
    }

    [Fact]
    public void Apply_TypeChangeKeepsIndividualCuttingAllowanceWhenNotExplicitlyChanged()
    {
        var source = Source() with { ElementType = TimberElementType.Rafter, CuttingAllowanceMm = 500 };
        var patch = EmptyPatch() with { ElementType = TimberElementType.WallPlate };

        var result = TimberElementPatcher.Apply(source, patch);

        Assert.Equal(TimberElementType.WallPlate, result.ElementType);
        Assert.Equal(500, result.CuttingAllowanceMm);
    }

    [Fact]
    public void Apply_DefaultApplicatorAfterTypeChangeUsesNewTypeDefault()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 300),
                new(TimberElementType.WallPlate, 100),
            },
        };
        var source = Source() with { ElementType = TimberElementType.Rafter, CuttingAllowanceMm = 500 };
        var patch = EmptyPatch() with { ElementType = TimberElementType.WallPlate };

        var patched = TimberElementPatcher.Apply(source, patch);
        var result = TimberElementDefaultApplicator.ApplyCuttingAllowance(patched, profile);

        Assert.Equal(TimberElementType.WallPlate, result.ElementType);
        Assert.Equal(100, result.CuttingAllowanceMm);
    }

    [Fact]
    public void Apply_ChangesOnlyLengthCalculationMode()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            LengthCalculationMode = LengthCalculationMode.ManualLength,
        });

        AssertPatched(result, value => Assert.Equal(LengthCalculationMode.ManualLength, value.LengthCalculationMode));
    }

    [Fact]
    public void Apply_ChangesOnlyManualLength()
    {
        var result = TimberElementPatcher.Apply(Source(), EmptyPatch() with
        {
            ManualLengthMm = 3200,
        });

        AssertPatched(result, value => Assert.Equal(3200, value.ManualLengthMm));
    }

    [Fact]
    public void Apply_ChangesMultipleFields()
    {
        var source = Source();
        var patch = EmptyPatch() with
        {
            ElementType = TimberElementType.Brace,
            WidthMm = 90,
            HeightMm = 140,
            SlopeDegrees = 38,
            RoofPlaneId = "R2",
            CuttingAllowanceMm = 80,
            LengthCalculationMode = LengthCalculationMode.SlopeCorrected,
            ManualLengthMm = 2800,
            Material = "KVH",
            Note = "Montaz",
        };

        var result = TimberElementPatcher.Apply(source, patch);

        Assert.Equal(TimberElementType.Brace, result.ElementType);
        Assert.Equal(90, result.WidthMm);
        Assert.Equal(140, result.HeightMm);
        Assert.Equal(38, result.SlopeDegrees);
        Assert.Equal("R2", result.RoofPlaneId);
        Assert.Equal(80, result.CuttingAllowanceMm);
        Assert.Equal(LengthCalculationMode.SlopeCorrected, result.LengthCalculationMode);
        Assert.Equal(2800, result.ManualLengthMm);
        Assert.Equal("KVH", result.Material);
        Assert.Equal("Montaz", result.Note);
        Assert.Equal(source.SchemaVersion, result.SchemaVersion);
        Assert.Equal(source.ElementId, result.ElementId);
    }

    [Fact]
    public void Apply_UnsetFieldsKeepOriginalValues()
    {
        var source = Source();
        var patch = EmptyPatch() with
        {
            WidthMm = 120,
        };

        var result = TimberElementPatcher.Apply(source, patch);

        Assert.Equal(source.ElementType, result.ElementType);
        Assert.Equal(120, result.WidthMm);
        Assert.Equal(source.HeightMm, result.HeightMm);
        Assert.Equal(source.SlopeDegrees, result.SlopeDegrees);
        Assert.Equal(source.IsSlopeDirectionReversed, result.IsSlopeDirectionReversed);
        Assert.Equal(source.RoofPlaneId, result.RoofPlaneId);
        Assert.Equal(source.CuttingAllowanceMm, result.CuttingAllowanceMm);
        Assert.Equal(source.LengthCalculationMode, result.LengthCalculationMode);
        Assert.Equal(source.ManualLengthMm, result.ManualLengthMm);
        Assert.Equal(source.Material, result.Material);
        Assert.Equal(source.Note, result.Note);
    }

    [Fact]
    public void Apply_NullManualLengthKeepsOriginalManualLength()
    {
        var source = Source();
        var patch = EmptyPatch() with
        {
            ManualLengthMm = null,
        };

        var result = TimberElementPatcher.Apply(source, patch);

        Assert.Equal(source.ManualLengthMm, result.ManualLengthMm);
    }

    private static void AssertPatched(TimberElementData result, Action<TimberElementData> assertChangedField)
    {
        var source = Source();
        assertChangedField(result);

        Assert.Equal(source.SchemaVersion, result.SchemaVersion);
        Assert.Equal(source.ElementId, result.ElementId);
        Assert.Equal(source.RoofPlaneId, result.RoofPlaneId);
        Assert.Equal(source.Material, result.Material);
        Assert.Equal(source.Note, result.Note);
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

    private static TimberElementData Source() => new()
    {
        SchemaVersion = 1,
        ElementId = "K7",
        ElementType = TimberElementType.Rafter,
        WidthMm = 80,
        HeightMm = 160,
        SlopeDegrees = 35,
        RoofPlaneId = "R1",
        CuttingAllowanceMm = 100,
        LengthCalculationMode = LengthCalculationMode.AutoByElementType,
        ManualLengthMm = 2500,
        Material = "Smrek C24",
        Note = "Povodna poznamka",
    };
}
