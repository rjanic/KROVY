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

    private static TimberAnnotationSettingsRequest Request(
        int denominator,
        TimberAnnotationSettingsApplyScope scope) =>
        new(
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Rectangle,
            denominator,
            scope);
}
