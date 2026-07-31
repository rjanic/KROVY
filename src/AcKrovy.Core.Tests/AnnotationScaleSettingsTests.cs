using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AnnotationScaleSettingsTests
{
    [Theory]
    [InlineData(25, TimberAnnotationScalePreset.Scale25)]
    [InlineData(50, TimberAnnotationScalePreset.Scale50)]
    [InlineData(75, TimberAnnotationScalePreset.Scale75)]
    [InlineData(100, TimberAnnotationScalePreset.Scale100)]
    [InlineData(60, TimberAnnotationScalePreset.Custom)]
    [InlineData(125, TimberAnnotationScalePreset.Custom)]
    public void PresetMapping_UsesLockedPresets(
        int denominator,
        TimberAnnotationScalePreset expected) =>
        Assert.Equal(expected, TimberAnnotationScaleSettingsRules.GetPreset(denominator));

    [Theory]
    [InlineData(25, 62.5, 67.5, 40, 0.5)]
    [InlineData(50, 125, 135, 80, 1)]
    [InlineData(75, 187.5, 202.5, 120, 1.5)]
    [InlineData(100, 250, 270, 160, 2)]
    public void Preview_UsesProductionTypographyRules(
        int denominator,
        double dimensionHeight,
        double itemHeight,
        double slopeHeight,
        double blockScale)
    {
        var preview = TimberAnnotationScaleSettingsRules.CreatePreview(denominator);

        Assert.Equal(dimensionHeight, preview.DimensionTextHeightMm);
        Assert.Equal(itemHeight, preview.ItemNumberTextHeightMm);
        Assert.Equal(slopeHeight, preview.SlopeTextHeightMm);
        Assert.Equal(blockScale, preview.FramedBlockScale);
    }

    [Fact]
    public void DrawingOverrideChange_WritesOnlyDrawingAndRefreshes()
    {
        var plan = Plan(
            hasOverride: true,
            drawing: 50,
            oldDefault: 50,
            TimberDrawingAnnotationScaleChange.SetOverride,
            requestedDrawing: 100,
            newDefault: 50);

        Assert.True(plan.WriteDrawingOverride);
        Assert.False(plan.RemoveDrawingOverride);
        Assert.False(plan.SaveUserDefault);
        Assert.True(plan.RefreshDrawing);
        Assert.Equal(100, plan.NewEffectiveDenominator);
    }

    [Fact]
    public void SameDrawingOverride_IsNoOp()
    {
        var plan = Plan(
            true, 75, 50,
            TimberDrawingAnnotationScaleChange.SetOverride,
            75, 50);

        Assert.False(plan.WriteDrawingOverride);
        Assert.False(plan.RemoveDrawingOverride);
        Assert.False(plan.RefreshDrawing);
    }

    [Fact]
    public void DefaultOnlyChange_PinsInheritedDrawingBeforeSavingDefault()
    {
        var plan = Plan(
            false, 50, 50,
            TimberDrawingAnnotationScaleChange.None,
            50, 100);

        Assert.True(plan.WriteDrawingOverride);
        Assert.Equal(50, plan.DrawingDenominator);
        Assert.True(plan.SaveUserDefault);
        Assert.False(plan.RefreshDrawing);
        Assert.Equal(50, plan.NewEffectiveDenominator);
    }

    [Fact]
    public void DefaultChange_DoesNotRewriteExistingDrawingOverride()
    {
        var plan = Plan(
            true, 75, 50,
            TimberDrawingAnnotationScaleChange.None,
            75, 100);

        Assert.False(plan.WriteDrawingOverride);
        Assert.True(plan.SaveUserDefault);
        Assert.False(plan.RefreshDrawing);
        Assert.Equal(75, plan.NewEffectiveDenominator);
    }

    [Fact]
    public void ClearOverride_RemovesValueAndRefreshesOnlyWhenEffectiveChanges()
    {
        var changed = Plan(
            true, 100, 50,
            TimberDrawingAnnotationScaleChange.ClearOverride,
            100, 50);
        var unchanged = Plan(
            true, 50, 50,
            TimberDrawingAnnotationScaleChange.ClearOverride,
            50, 50);

        Assert.True(changed.RemoveDrawingOverride);
        Assert.True(changed.RefreshDrawing);
        Assert.True(unchanged.RemoveDrawingOverride);
        Assert.False(unchanged.RefreshDrawing);
    }

    private static TimberAnnotationScalePersistencePlan Plan(
        bool hasOverride,
        int drawing,
        int oldDefault,
        TimberDrawingAnnotationScaleChange change,
        int requestedDrawing,
        int newDefault) =>
        TimberAnnotationScaleSettingsRules.CreatePersistencePlan(
            hasOverride,
            drawing,
            oldDefault,
            change,
            requestedDrawing,
            newDefault);
}
