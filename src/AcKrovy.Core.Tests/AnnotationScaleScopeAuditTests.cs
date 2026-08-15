using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AnnotationScaleScopeAuditTests
{
    private static readonly int[] Denominators = [25, 50, 75, 100, 137];

    [Theory]
    [MemberData(nameof(ScaleCases))]
    public void SaveNewStoresExactScaleForLaterNewElement(int denominator)
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.AnnotationScaleDenominator = denominator;
        var existing = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationScaleDenominatorOverride = 25,
        };

        var created = TimberElementDefaults.For(TimberElementType.Rafter, profile);

        Assert.Equal(denominator, created.AnnotationScaleDenominatorOverride);
        Assert.Equal(25, existing.AnnotationScaleDenominatorOverride);
        Assert.Equal(
            denominator != TimberAnnotationScaleRules.DefaultDenominator,
            TimberAnnotationSettingsChangeRules.ShouldApplyScaleChange(
                TimberAnnotationSettingsApplyScope.NewElementsOnly,
                TimberAnnotationScaleRules.DefaultDenominator,
                denominator));
    }

    [Theory]
    [MemberData(nameof(ScaleCases))]
    public void ApplySelectionAlwaysSetsExactExplicitOverride(int denominator)
    {
        var source = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationScaleDenominatorOverride = 25,
        };
        var request = Request(
            denominator,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            applyScaleChange:
                TimberAnnotationSettingsChangeRules.ShouldApplyScaleChange(
                    TimberAnnotationSettingsApplyScope.SelectedElements,
                    acceptedDrawingDenominator: 50,
                    selectedDenominator: denominator));

        var selected = TimberAnnotationSettingsApplicator.Apply(
            source,
            request.CreateElementPatch());
        var effective = TimberAnnotationScaleResolver.ResolveElementContext(
            new TimberAnnotationScaleContext(50, TimberAnnotationScaleSource.Drawing),
            selected.AnnotationScaleDenominatorOverride);

        Assert.True(request.ApplyScaleChange);
        Assert.Equal(denominator, selected.AnnotationScaleDenominatorOverride);
        Assert.Equal(denominator, effective.Denominator);
        Assert.Equal(TimberAnnotationScaleSource.ElementOverride, effective.Source);
        Assert.Equal(25, source.AnnotationScaleDenominatorOverride);
    }

    [Fact]
    public void ApplySelectionExplicit50IsNotConflatedWithNoOverride()
    {
        Assert.False(TimberAnnotationSettingsChangeRules.HasScaleChanged(50, 50));
        Assert.True(TimberAnnotationSettingsChangeRules.ShouldApplyScaleChange(
            TimberAnnotationSettingsApplyScope.SelectedElements,
            acceptedDrawingDenominator: 50,
            selectedDenominator: 50));

        var patch = Request(
            50,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            applyScaleChange: true).CreateElementPatch().AnnotationScaleOverride;

        Assert.Equal(TimberAnnotationScaleOverrideChange.Set, patch.Change);
        Assert.Equal(50, patch.Denominator);
    }

    [Theory]
    [MemberData(nameof(ScaleCases))]
    public void ApplyAllWritesUniformDrawingScaleAndClearsEveryOverride(int denominator)
    {
        var request = Request(
            denominator,
            TimberAnnotationSettingsApplyScope.AllElements,
            applyScaleChange:
                TimberAnnotationSettingsChangeRules.ShouldApplyScaleChange(
                    TimberAnnotationSettingsApplyScope.AllElements,
                    acceptedDrawingDenominator: denominator,
                    selectedDenominator: denominator));
        var withOverride = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationScaleDenominatorOverride = 25,
        };
        var withoutOverride = withOverride with
        {
            AnnotationScaleDenominatorOverride = null,
        };

        var first = TimberAnnotationSettingsApplicator.Apply(
            withOverride,
            request.CreateElementPatch());
        var second = TimberAnnotationSettingsApplicator.Apply(
            withoutOverride,
            request.CreateElementPatch());
        var drawing = new TimberAnnotationScaleContext(
            denominator,
            TimberAnnotationScaleSource.Drawing);

        Assert.True(request.ApplyScaleChange);
        Assert.Null(first.AnnotationScaleDenominatorOverride);
        Assert.Null(second.AnnotationScaleDenominatorOverride);
        Assert.Equal(
            denominator,
            TimberAnnotationScaleResolver.ResolveElementContext(
                drawing,
                first.AnnotationScaleDenominatorOverride).Denominator);
        Assert.Equal(
            denominator,
            TimberAnnotationScaleResolver.ResolveElementContext(
                drawing,
                second.AnnotationScaleDenominatorOverride).Denominator);
    }

    [Theory]
    [InlineData(TimberAnnotationMode.NoAnnotations)]
    [InlineData(TimberAnnotationMode.ItemNumberLeader)]
    [InlineData(TimberAnnotationMode.DimensionsLeader)]
    [InlineData(TimberAnnotationMode.FullLabel)]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber)]
    public void ScalePatchIsPresentationModeAgnostic(TimberAnnotationMode mode)
    {
        var source = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            AnnotationMode = mode,
            AnnotationScaleDenominatorOverride = 25,
        };
        var request = new TimberAnnotationSettingsRequest(
            mode,
            source.ItemNumberLeaderStyle,
            75,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            source.AnnotationTextSettings!,
            TimberAnnotationTextSettingsPatch.Unchanged,
            applyScaleChange: true,
            presentationSettingsChanged: false);

        var updated = TimberAnnotationSettingsApplicator.Apply(
            source,
            request.CreateElementPatch());
        var plan = TimberAnnotationRefreshPlanner.Create(updated);

        Assert.Equal(75, updated.AnnotationScaleDenominatorOverride);
        Assert.Equal(
            mode == TimberAnnotationMode.NoAnnotations,
            !plan.EnsureLabel &&
            !plan.ReconcileSlopeArrow &&
            !plan.ReconcileSlopeAngleText);
    }

    [Fact]
    public void ProductionSelectionUsesScopedIdsAndCanonicalRefresh()
    {
        var commands = Read("Commands", "AcKrovyCommands.cs");
        var window = Read("UI", "LayerSettingsWindow.xaml.cs");
        var apply = Segment(
            commands,
            "private static SettingsDrawingApplyResult ApplySettingsToExistingElements(",
            "private sealed record SettingsDrawingApplyResult(");

        Assert.Contains("ShouldApplyScaleChange(", window);
        Assert.Contains("targetIds.Distinct().ToList()", apply);
        Assert.Contains("AutoCadEntityHelpers.IsSupportedTimberGeometry(entity)", apply);
        Assert.Contains("metadataStore.TryRead(entity, out var data)", apply);
        Assert.Contains("UpdateLabelsForChangedEntities(", apply);
        Assert.Contains("presentationBatchContext", apply);
        Assert.DoesNotContain("RoofGeneratedTimber", apply + window);
        Assert.DoesNotContain("ElementLabelService.UpdateAll", apply);
    }

    [Fact]
    public void StableSchemasAndReactiveArchitectureRemainUntouched()
    {
        var window = Read("UI", "LayerSettingsWindow.xaml.cs");
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, TimberDrawingSettings.DrawingSettingsSchemaVersion);
        Assert.DoesNotContain("ObjectModified", window);
        Assert.DoesNotContain("CommandEnded", window);
    }

    public static IEnumerable<object[]> ScaleCases() =>
        Denominators.Select(denominator => new object[] { denominator });

    private static TimberAnnotationSettingsRequest Request(
        int denominator,
        TimberAnnotationSettingsApplyScope scope,
        bool applyScaleChange) =>
        new(
            TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle.Rectangle,
            denominator,
            scope,
            TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings(),
            TimberAnnotationTextSettingsPatch.Unchanged,
            applyScaleChange,
            presentationSettingsChanged: false);

    private static string Read(string area, string fileName) =>
        RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", area, fileName);

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
