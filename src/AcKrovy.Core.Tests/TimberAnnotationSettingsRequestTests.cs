using AcKrovy.Core.Models;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationSettingsRequestTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(250)]
    public void ConstructorAcceptsInclusiveScaleBoundaries(int denominator)
    {
        var request = Request(denominator, TimberAnnotationSettingsApplyScope.SelectedElements);

        Assert.Equal(denominator, request.ScaleDenominator);
        Assert.Equal(denominator, request.CreateElementPatch().AnnotationScaleOverride.Denominator);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(251)]
    public void ConstructorRejectsOutOfRangeScaleWithoutClamping(int denominator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Request(denominator, TimberAnnotationSettingsApplyScope.SelectedElements));

    [Fact]
    public void NewElementsOnlyLeavesExistingOverrideUnchanged() =>
        Assert.Equal(
            TimberAnnotationScaleOverrideChange.Unchanged,
            Request(25, TimberAnnotationSettingsApplyScope.NewElementsOnly)
                .CreateElementPatch().AnnotationScaleOverride.Change);

    [Fact]
    public void SelectedElementsSetsExplicitOverride() =>
        Assert.Equal(
            TimberAnnotationScaleOverrideChange.Set,
            Request(25, TimberAnnotationSettingsApplyScope.SelectedElements)
                .CreateElementPatch().AnnotationScaleOverride.Change);

    [Fact]
    public void AllElementsClearsOverride() =>
        Assert.Equal(
            TimberAnnotationScaleOverrideChange.Clear,
            Request(75, TimberAnnotationSettingsApplyScope.AllElements)
                .CreateElementPatch().AnnotationScaleOverride.Change);

    [Fact]
    public void RequestWithoutTextSettingsLeavesTextPatchUnchangedForStagedCompatibility()
    {
        var patch = Request(
            50,
            TimberAnnotationSettingsApplyScope.SelectedElements)
            .CreateElementPatch();

        Assert.Equal(
            TimberAnnotationTextSettingsChange.Unchanged,
            patch.AnnotationTextSettings.Change);
    }

    [Fact]
    public void NewElementsOnlyWithTextSettingsLeavesExistingTextUnchanged()
    {
        var request = RequestWithTextSettings(
            TimberAnnotationSettingsApplyScope.NewElementsOnly);

        Assert.Equal(TextSettings(), request.AnnotationTextSettings);
        Assert.Equal(
            TimberAnnotationTextSettingsChange.Unchanged,
            request.CreateElementPatch().AnnotationTextSettings.Change);
    }

    [Theory]
    [InlineData(TimberAnnotationSettingsApplyScope.SelectedElements)]
    [InlineData(TimberAnnotationSettingsApplyScope.AllElements)]
    public void ExistingElementScopesSetNormalizedTextSettings(
        TimberAnnotationSettingsApplyScope scope)
    {
        var request = RequestWithTextSettings(scope);
        var patch = request.CreateElementPatch().AnnotationTextSettings;

        Assert.Equal(TimberAnnotationTextSettingsChange.Set, patch.Change);
        Assert.Equal(TextSettings(), patch.Apply(null));
    }

    [Fact]
    public void ConstructorRejectsInvalidTextSettingsWithoutClamping()
    {
        var invalid = TextSettings() with
        {
            ItemCodePaperHeightMm = 3.51d,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle.Plain,
            50,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            annotationTextSettings: invalid));
    }

    [Fact]
    public void PublicConstructors_PreserveFourFiveAndSixArgumentContracts()
    {
        var type = typeof(TimberAnnotationSettingsRequest);
        var fourArgumentConstructor = type.GetConstructor(
        [
            typeof(TimberAnnotationMode),
            typeof(ItemNumberLeaderStyle),
            typeof(int),
            typeof(TimberAnnotationSettingsApplyScope),
        ]);
        var fiveArgumentConstructor = type.GetConstructor(
        [
            typeof(TimberAnnotationMode),
            typeof(ItemNumberLeaderStyle),
            typeof(int),
            typeof(TimberAnnotationSettingsApplyScope),
            typeof(TimberAnnotationTextSettings),
        ]);
        var sixArgumentConstructor = type.GetConstructor(
        [
            typeof(TimberAnnotationMode),
            typeof(ItemNumberLeaderStyle),
            typeof(int),
            typeof(TimberAnnotationSettingsApplyScope),
            typeof(TimberAnnotationTextSettings),
            typeof(TimberAnnotationTextSettingsPatch),
        ]);

        Assert.NotNull(fourArgumentConstructor);
        Assert.NotNull(fiveArgumentConstructor);
        Assert.NotNull(sixArgumentConstructor);
        Assert.All(
            fiveArgumentConstructor!.GetParameters(),
            parameter => Assert.False(parameter.IsOptional));
        Assert.All(
            sixArgumentConstructor!.GetParameters(),
            parameter => Assert.False(parameter.IsOptional));

        Assert.NotNull(typeof(TimberAnnotationSettingsPatch).GetConstructor(
        [
            typeof(TimberAnnotationMode),
            typeof(ItemNumberLeaderStyle),
            typeof(TimberAnnotationScaleOverridePatch),
        ]));
    }

    [Fact]
    public void PatchOverload_StoresSettingsForProfileAndUsesExplicitPatchForElements()
    {
        var settings = TextSettings();
        var rolePatch = TimberAnnotationTextSettingsPatch.ForRole(
            TimberAnnotationTextRole.Dimension,
            "ROMANS",
            3d);

        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle.Plain,
            50,
            TimberAnnotationSettingsApplyScope.SelectedElements,
            settings,
            rolePatch);

        Assert.Equal(settings, request.AnnotationTextSettings);
        Assert.Equal(rolePatch, request.AnnotationTextPatch);

        var patch = request.CreateElementPatch().AnnotationTextSettings;
        Assert.Equal(TimberAnnotationTextSettingsChange.Set, patch.Change);
        Assert.Equal(TimberAnnotationTextSettingsChange.Unchanged, patch.ItemCode.Change);
        Assert.Equal(TimberAnnotationTextSettingsChange.Set, patch.Dimension.Change);
        Assert.Equal("ROMANS", patch.Dimension.TextStyleName);
        Assert.Equal(3d, patch.Dimension.PaperHeightMm);
        Assert.Equal(TimberAnnotationTextSettingsChange.Unchanged, patch.Slope.Change);
    }

    [Fact]
    public void PatchOverload_NewElementsOnlyLeavesTextUnchangedEvenWhenPatchIsSet()
    {
        var rolePatch = TimberAnnotationTextSettingsPatch.ForRole(
            TimberAnnotationTextRole.ItemCode,
            "ARIAL",
            2.7d);

        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle.Plain,
            50,
            TimberAnnotationSettingsApplyScope.NewElementsOnly,
            TextSettings(),
            rolePatch);

        Assert.Equal(
            TimberAnnotationTextSettingsChange.Unchanged,
            request.CreateElementPatch().AnnotationTextSettings.Change);
    }

    [Fact]
    public void PatchOverload_UnchangedPatchFallsBackToFullSettingsForBackwardCompat()
    {
        var settings = TextSettings();
        var request = new TimberAnnotationSettingsRequest(
            TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle.Plain,
            50,
            TimberAnnotationSettingsApplyScope.AllElements,
            settings,
            TimberAnnotationTextSettingsPatch.Unchanged);

        var patch = request.CreateElementPatch().AnnotationTextSettings;
        Assert.Equal(TimberAnnotationTextSettingsChange.Set, patch.Change);
        Assert.Equal(settings, patch.Apply(null));
    }

    [Fact]
    public void ExistingConstructors_DefaultAnnotationTextPatchToUnchanged()
    {
        var withoutText = Request(50, TimberAnnotationSettingsApplyScope.SelectedElements);
        var withText = RequestWithTextSettings(
            TimberAnnotationSettingsApplyScope.SelectedElements);

        Assert.Equal(
            TimberAnnotationTextSettingsChange.Unchanged,
            withoutText.AnnotationTextPatch.Change);
        Assert.Equal(
            TimberAnnotationTextSettingsChange.Unchanged,
            withText.AnnotationTextPatch.Change);
    }

    [Fact]
    public void PatchOverload_RejectsNullPatch()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TimberAnnotationSettingsRequest(
                TimberAnnotationMode.FullLabel,
                ItemNumberLeaderStyle.Plain,
                50,
                TimberAnnotationSettingsApplyScope.SelectedElements,
                TextSettings(),
                annotationTextPatch: null!));
    }

    [Fact]
    public void ExplicitTextSettingsConstructorRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TimberAnnotationSettingsRequest(
                TimberAnnotationMode.FullLabel,
                ItemNumberLeaderStyle.Plain,
                50,
                TimberAnnotationSettingsApplyScope.SelectedElements,
                annotationTextSettings: null!));
    }

    [Fact]
    public void TextSettingsPatchSetRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TimberAnnotationTextSettingsPatch.Set(null!));
    }

    private static TimberAnnotationSettingsRequest Request(
        int denominator,
        TimberAnnotationSettingsApplyScope scope) =>
        new(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Rectangle,
            denominator,
            scope);

    private static TimberAnnotationSettingsRequest RequestWithTextSettings(
        TimberAnnotationSettingsApplyScope scope) =>
        new(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Rectangle,
            50,
            scope,
            annotationTextSettings: TextSettings());

    private static TimberAnnotationTextSettings TextSettings() =>
        TimberAnnotationTextSettings.Shared("ISOCP", 3.1d, 3d, 2d);
}
