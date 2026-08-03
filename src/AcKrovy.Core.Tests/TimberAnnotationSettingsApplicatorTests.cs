using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationSettingsApplicatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(25)]
    public void Apply_UnchangedPreservesOverride(int? originalOverride)
    {
        var source = Source() with
        {
            AnnotationScaleDenominatorOverride = originalOverride,
        };

        var result = Apply(source, TimberAnnotationScaleOverridePatch.Unchanged);

        Assert.Equal(originalOverride, result.AnnotationScaleDenominatorOverride);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(100)]
    public void Apply_SetStoresValidOverride(int denominator)
    {
        var result = Apply(
            Source(),
            TimberAnnotationScaleOverridePatch.Set(denominator));

        Assert.Equal(denominator, result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void Apply_SetReplacesDifferentOverride()
    {
        var source = Source() with { AnnotationScaleDenominatorOverride = 100 };

        var result = Apply(
            source,
            TimberAnnotationScaleOverridePatch.Set(25));

        Assert.Equal(25, result.AnnotationScaleDenominatorOverride);
        Assert.NotEqual(source, result);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(251)]
    public void Set_InvalidOverrideIsRejected(int denominator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationScaleOverridePatch.Set(denominator));

    [Fact]
    public void Apply_ClearRemovesOverride()
    {
        var result = Apply(
            Source() with { AnnotationScaleDenominatorOverride = 25 },
            TimberAnnotationScaleOverridePatch.Clear);

        Assert.Null(result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void Apply_ChangesModeAndFramedItemStyleWithoutChangingOtherData()
    {
        var source = Source();
        var patch = new TimberAnnotationSettingsPatch(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Rectangle,
            TimberAnnotationScaleOverridePatch.Unchanged);

        var result = TimberAnnotationSettingsApplicator.Apply(source, patch);

        Assert.Equal(TimberAnnotationMode.DimensionsWithItemNumber, result.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Rectangle, result.ItemNumberLeaderStyle);
        Assert.Equal(source with
        {
            AnnotationMode = TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Rectangle,
        }, result);
    }

    [Fact]
    public void Apply_SameSettingsAndOverrideIsNoOpByValue()
    {
        var source = Source() with { AnnotationScaleDenominatorOverride = 25 };

        var result = Apply(
            source,
            TimberAnnotationScaleOverridePatch.Set(25));

        Assert.Equal(source, result);
    }

    [Fact]
    public void Apply_SameOverrideButDifferentModeIsChange()
    {
        var source = Source() with { AnnotationScaleDenominatorOverride = 25 };
        var patch = new TimberAnnotationSettingsPatch(
            TimberAnnotationMode.DimensionsLeader,
            source.ItemNumberLeaderStyle,
            TimberAnnotationScaleOverridePatch.Set(25));

        var result = TimberAnnotationSettingsApplicator.Apply(source, patch);

        Assert.Equal(25, result.AnnotationScaleDenominatorOverride);
        Assert.Equal(TimberAnnotationMode.DimensionsLeader, result.AnnotationMode);
        Assert.NotEqual(source, result);
    }

    [Fact]
    public void Apply_ChangedSchemaFourDataPreparesSchemaSixAndPreservesOtherMetadata()
    {
        var source = Source() with
        {
            SchemaVersion = 4,
            ElementId = "K19",
            CuttingAllowanceMm = 275,
            Material = "KVH",
            AnnotationScaleDenominatorOverride = null,
        };

        var changed = Apply(
            source,
            TimberAnnotationScaleOverridePatch.Set(25));
        var prepared = TimberElementDataVersioning.PrepareForWrite(changed);

        Assert.Equal(7, prepared.SchemaVersion);
        Assert.Equal(25, prepared.AnnotationScaleDenominatorOverride);
        Assert.Equal(source.ElementId, prepared.ElementId);
        Assert.Equal(source.CuttingAllowanceMm, prepared.CuttingAllowanceMm);
        Assert.Equal(source.Material, prepared.Material);
        Assert.Equal(source.WidthMm, prepared.WidthMm);
        Assert.Equal(source.HeightMm, prepared.HeightMm);
    }

    [Fact]
    public void AppliedElementResolvesOverrideWhileUnselectedElementKeepsDrawingContext()
    {
        var drawing = TimberAnnotationScaleResolver.ResolveDrawingContext(
            hasDrawingValue: true,
            drawingDenominator: 50);
        var unselected = Source();
        var selected = Apply(
            unselected,
            TimberAnnotationScaleOverridePatch.Set(25));

        var selectedContext = TimberAnnotationScaleResolver.ResolveElementContext(
            drawing,
            selected.AnnotationScaleDenominatorOverride);
        var unselectedContext = TimberAnnotationScaleResolver.ResolveElementContext(
            drawing,
            unselected.AnnotationScaleDenominatorOverride);

        Assert.Equal(25, selectedContext.Denominator);
        Assert.Equal(TimberAnnotationScaleSource.ElementOverride, selectedContext.Source);
        Assert.Equal(50, unselectedContext.Denominator);
        Assert.Equal(TimberAnnotationScaleSource.Drawing, unselectedContext.Source);
    }

    [Fact]
    public void Apply_ClearOverNullIsNoOpByValue()
    {
        var source = Source();

        var result = Apply(source, TimberAnnotationScaleOverridePatch.Clear);

        Assert.Equal(source, result);
    }

    [Fact]
    public void Apply_UnchangedTextSettingsPreservesLegacyNull()
    {
        var source = Source() with { AnnotationTextSettings = null };

        var result = TimberAnnotationSettingsApplicator.Apply(
            source,
            new TimberAnnotationSettingsPatch(
                source.AnnotationMode,
                source.ItemNumberLeaderStyle,
                TimberAnnotationScaleOverridePatch.Unchanged,
                TimberAnnotationTextSettingsPatch.Unchanged));

        Assert.Null(result.AnnotationTextSettings);
        Assert.Equal(source, result);
    }

    [Fact]
    public void Apply_SetStoresNormalizedTextSettingsAndPreservesOtherMetadata()
    {
        var source = Source() with
        {
            AnnotationScaleDenominatorOverride = 75,
        };
        var expected = TimberAnnotationTextSettings.Shared(
            "ISOCP",
            3.1d,
            3d,
            2d);

        var result = TimberAnnotationSettingsApplicator.Apply(
            source,
            new TimberAnnotationSettingsPatch(
                source.AnnotationMode,
                source.ItemNumberLeaderStyle,
                TimberAnnotationScaleOverridePatch.Unchanged,
                TimberAnnotationTextSettingsPatch.Set(
                    expected with { TextStyleName = " ISOCP " })));

        Assert.Equal(expected, result.AnnotationTextSettings);
        Assert.Equal(source.AnnotationScaleDenominatorOverride,
            result.AnnotationScaleDenominatorOverride);
        Assert.Equal(source.ElementId, result.ElementId);
        Assert.Equal(source.WidthMm, result.WidthMm);
        Assert.Equal(source.HeightMm, result.HeightMm);
        Assert.Equal(source.Material, result.Material);
        Assert.Equal(source.Note, result.Note);
        Assert.Equal(
            source with { AnnotationTextSettings = expected },
            result);
    }

    [Fact]
    public void Apply_SameExplicitTextSettingsIsNoOpByValue()
    {
        var settings = TimberAnnotationTextSettings.Shared(
            "ISOCP",
            3.1d,
            3d,
            2d);
        var source = Source() with { AnnotationTextSettings = settings };

        var result = TimberAnnotationSettingsApplicator.Apply(
            source,
            new TimberAnnotationSettingsPatch(
                source.AnnotationMode,
                source.ItemNumberLeaderStyle,
                TimberAnnotationScaleOverridePatch.Unchanged,
                TimberAnnotationTextSettingsPatch.Set(settings)));

        Assert.Equal(source, result);
    }

    private static TimberElementData Apply(
        TimberElementData source,
        TimberAnnotationScaleOverridePatch scalePatch) =>
        TimberAnnotationSettingsApplicator.Apply(
            source,
            new TimberAnnotationSettingsPatch(
                source.AnnotationMode,
                source.ItemNumberLeaderStyle,
                scalePatch));

    private static TimberElementData Source() => new()
    {
        SchemaVersion = TimberElementDataSchema.CurrentVersion,
        ElementId = "K7",
        ElementType = TimberElementType.Rafter,
        AnnotationMode = TimberAnnotationMode.FullLabel,
        ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
        WidthMm = 80,
        HeightMm = 160,
        SlopeDegrees = 35,
        RoofPlaneId = "R1",
        CuttingAllowanceMm = 100,
        Material = "Smrek C24",
        Note = "Povodna poznamka",
    };
}
