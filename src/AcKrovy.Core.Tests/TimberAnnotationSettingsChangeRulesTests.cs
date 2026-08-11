using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationSettingsChangeRulesTests
{
    [Fact]
    public void AnnotationModeOnlyChange_IsDetected()
    {
        Assert.True(TimberAnnotationSettingsChangeRules.HasAnnotationModeChanged(
            TimberAnnotationMode.DimensionsLeader,
            TimberAnnotationMode.ItemNumberLeader));
        Assert.True(TimberAnnotationSettingsChangeRules.HasPresentationChanged(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain));
    }

    [Fact]
    public void ItemNumberLeaderStyleOnlyChange_IsDetected()
    {
        Assert.True(TimberAnnotationSettingsChangeRules.HasItemNumberLeaderStyleChanged(
            ItemNumberLeaderStyle.Circle,
            ItemNumberLeaderStyle.Rectangle));
        Assert.True(TimberAnnotationSettingsChangeRules.HasPresentationChanged(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Rectangle));
    }

    [Fact]
    public void PresetChangeAlteringModeAndStyle_IsDetected()
    {
        var from = SettingsAnnotationPresetRules.Get(
            SettingsAnnotationPreset.DimensionsOnly);
        var to = SettingsAnnotationPresetRules.Get(
            SettingsAnnotationPreset.ItemCircle);

        Assert.True(TimberAnnotationSettingsChangeRules.HasPresentationChanged(
            from.AnnotationMode,
            from.ItemNumberLeaderStyle,
            to.AnnotationMode,
            to.ItemNumberLeaderStyle));
    }

    [Fact]
    public void NoActualAnnotationChange_PreservesNoChangeBehavior()
    {
        Assert.False(TimberAnnotationSettingsChangeRules.HasPresentationChanged(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain));
        Assert.False(TimberAnnotationSettingsChangeRules.HasScaleChanged(50, 50));
        Assert.False(TimberAnnotationSettingsChangeRules.ShouldRefreshAllEligible(
            drawingScaleChanged: false,
            presentationSettingsChanged: false));
    }

    [Fact]
    public void PresetOnlyChange_DoesNotRequireScaleChange_AndForcesEligibleRefresh()
    {
        Assert.False(TimberAnnotationSettingsChangeRules.HasScaleChanged(50, 50));
        Assert.True(TimberAnnotationSettingsChangeRules.ShouldRefreshAllEligible(
            drawingScaleChanged: false,
            presentationSettingsChanged: true));
    }

    [Fact]
    public void ScaleOnlyChange_ContinuesToForceEligibleRefresh()
    {
        Assert.True(TimberAnnotationSettingsChangeRules.HasScaleChanged(50, 100));
        Assert.True(TimberAnnotationSettingsChangeRules.ShouldRefreshAllEligible(
            drawingScaleChanged: true,
            presentationSettingsChanged: false));
    }

    [Fact]
    public void PresetPlusScaleChange_ContinuesToWork()
    {
        Assert.True(TimberAnnotationSettingsChangeRules.HasPresentationChanged(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle));
        Assert.True(TimberAnnotationSettingsChangeRules.HasScaleChanged(50, 75));
        Assert.True(TimberAnnotationSettingsChangeRules.ShouldRefreshAllEligible(
            drawingScaleChanged: true,
            presentationSettingsChanged: true));
    }

    [Theory]
    [InlineData(TimberAnnotationSettingsApplyScope.SelectedElements)]
    [InlineData(TimberAnnotationSettingsApplyScope.AllElements)]
    public void PresetOnlyChange_LeavesScaleOverrideUnchanged(
        TimberAnnotationSettingsApplyScope scope)
    {
        var patch = TimberAnnotationSettingsChangeRules.ResolveScaleOverride(
            scope,
            applyScaleChange: false,
            scaleDenominator: 50);

        Assert.Equal(TimberAnnotationScaleOverrideChange.Unchanged, patch.Change);
    }

    [Fact]
    public void SelectedElements_ScaleChange_SetsExplicitOverride()
    {
        var patch = TimberAnnotationSettingsChangeRules.ResolveScaleOverride(
            TimberAnnotationSettingsApplyScope.SelectedElements,
            applyScaleChange: true,
            scaleDenominator: 25);

        Assert.Equal(TimberAnnotationScaleOverrideChange.Set, patch.Change);
        Assert.Equal(25, patch.Denominator);
    }

    [Fact]
    public void AllElements_ScaleChange_ClearsOverride()
    {
        var patch = TimberAnnotationSettingsChangeRules.ResolveScaleOverride(
            TimberAnnotationSettingsApplyScope.AllElements,
            applyScaleChange: true,
            scaleDenominator: 100);

        Assert.Equal(TimberAnnotationScaleOverrideChange.Clear, patch.Change);
    }
}

public sealed class TimberAnnotationSettingsPresetOnlyDispatchTests
{
    [Fact]
    public void PresetOnly_SelectedElements_DispatchesModeStylePatchWithoutScale()
    {
        var text = TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            50,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            text,
            TimberAnnotationTextSettingsPatch.Unchanged,
            applyScaleChange: false,
            presentationSettingsChanged: true);

        Assert.Equal(
            TimberAnnotationSettingsApplyScope.SelectedElements,
            request.ApplyScope);
        Assert.False(request.ApplyScaleChange);
        Assert.True(request.PresentationSettingsChanged);

        var patch = request.CreateElementPatch();
        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, patch.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Circle, patch.ItemNumberLeaderStyle);
        Assert.Equal(
            TimberAnnotationScaleOverrideChange.Unchanged,
            patch.AnnotationScaleOverride.Change);

        var source = Element(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            overrideDenominator: 50,
            text);
        var result = TimberAnnotationSettingsApplicator.Apply(source, patch);
        Assert.NotEqual(source, result);
        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, result.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Circle, result.ItemNumberLeaderStyle);
        Assert.Equal(50, result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void PresetOnly_AllElements_DispatchesModeStylePatchWithoutScale()
    {
        var text = TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            50,
            TimberAnnotationSettingsApplyScope.AllElements,
            text,
            TimberAnnotationTextSettingsPatch.Unchanged,
            applyScaleChange: false,
            presentationSettingsChanged: true);

        Assert.Equal(
            TimberAnnotationSettingsApplyScope.AllElements,
            request.ApplyScope);
        Assert.False(request.ApplyScaleChange);
        Assert.True(request.PresentationSettingsChanged);

        var patch = request.CreateElementPatch();
        Assert.Equal(
            TimberAnnotationScaleOverrideChange.Unchanged,
            patch.AnnotationScaleOverride.Change);

        var source = Element(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            overrideDenominator: null,
            text);
        var result = TimberAnnotationSettingsApplicator.Apply(source, patch);
        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, result.AnnotationMode);
        Assert.Equal(ItemNumberLeaderStyle.Circle, result.ItemNumberLeaderStyle);
        Assert.Null(result.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void ScaleOnly_SelectedElements_StillSetsOverride()
    {
        var text = TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            100,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            text,
            TimberAnnotationTextSettingsPatch.Unchanged,
            applyScaleChange: true,
            presentationSettingsChanged: false);

        var patch = request.CreateElementPatch();
        Assert.Equal(TimberAnnotationScaleOverrideChange.Set, patch.AnnotationScaleOverride.Change);
        Assert.Equal(100, patch.AnnotationScaleOverride.Denominator);
        Assert.False(request.PresentationSettingsChanged);
    }

    [Fact]
    public void NewElementsOnly_RemainsDefaultOnly_AndDoesNotPatchExistingScale()
    {
        var text = TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            50,
            TimberAnnotationSettingsApplyScope.NewElementsOnly,
            text,
            TimberAnnotationTextSettingsPatch.Unchanged,
            applyScaleChange: false,
            presentationSettingsChanged: true);

        Assert.Equal(
            TimberAnnotationSettingsApplyScope.NewElementsOnly,
            request.ApplyScope);
        var patch = request.CreateElementPatch();
        Assert.Equal(
            TimberAnnotationScaleOverrideChange.Unchanged,
            patch.AnnotationScaleOverride.Change);
        Assert.Equal(
            TimberAnnotationTextSettingsChange.Unchanged,
            patch.AnnotationTextSettings.Change);
    }

    [Fact]
    public void UnchangedScale_IsNotOverwrittenByPresetOnlyApply()
    {
        var text = TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
        var source = Element(
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            overrideDenominator: 75,
            text);
        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            50,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            text,
            TimberAnnotationTextSettingsPatch.Unchanged,
            applyScaleChange: false,
            presentationSettingsChanged: true);

        var result = TimberAnnotationSettingsApplicator.Apply(
            source,
            request.CreateElementPatch());

        Assert.Equal(75, result.AnnotationScaleDenominatorOverride);
        Assert.Equal(TimberAnnotationMode.ItemNumberLeader, result.AnnotationMode);
    }

    private static TimberElementData Element(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        int? overrideDenominator,
        TimberAnnotationTextSettings text) =>
        new()
        {
            SchemaVersion = TimberElementDataSchema.CurrentVersion,
            ElementId = "K1",
            AnnotationMode = mode,
            ItemNumberLeaderStyle = style,
            AnnotationScaleDenominatorOverride = overrideDenominator,
            AnnotationTextSettings = text,
        };
}
