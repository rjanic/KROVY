using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationRefreshPlannerTests
{
    [Theory]
    [InlineData(30, true, false)]
    [InlineData(0, false, true)]
    public void Create_AlwaysReconcilesCompleteSlopeAnnotation(
        double slopeDegrees,
        bool arrowShouldExist,
        bool markerShouldExist)
    {
        var plan = TimberAnnotationRefreshPlanner.Create(new TimberElementData
        {
            SlopeDegrees = slopeDegrees,
        });

        Assert.True(plan.EnsureLabel);
        Assert.True(plan.ReconcileSlopeArrow);
        Assert.Equal(arrowShouldExist, plan.ShouldSlopeArrowExist);
        Assert.Equal(markerShouldExist, plan.ShouldHorizontalSlopeMarkerExist);
        Assert.False(plan.ShouldPostPerpendicularMarkerExist);
        Assert.True(plan.ReconcileSlopeAngleText);
        Assert.True(plan.ShouldSlopeAngleTextExist);
    }

    [Fact]
    public void ExistingPostRefreshReplacesZeroSlopeMarkerWithPerpendicularMarker()
    {
        var data = TimberElementDefaults.For(TimberElementType.Post) with
        {
            ElementId = "S1",
            SlopeDegrees = 0d,
        };

        var first = TimberAnnotationRefreshPlanner.Create(data);
        var liveRefresh = TimberAnnotationRefreshPlanner.Create(data);

        Assert.Equal(first, liveRefresh);
        Assert.True(first.EnsureLabel);
        Assert.True(first.ReconcileSlopeArrow);
        Assert.False(first.ShouldSlopeArrowExist);
        Assert.False(first.ShouldHorizontalSlopeMarkerExist);
        Assert.True(first.ShouldPostPerpendicularMarkerExist);
        Assert.True(first.ReconcileSlopeAngleText);
        Assert.True(first.ShouldSlopeAngleTextExist);
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("COPYCLIP/PASTECLIP")]
    [InlineData("AK_LABELS")]
    [InlineData("LIVE_REFRESH")]
    public void RectangularFootprintPost_SuppressesAllLegacyAnnotations(string refreshPath)
    {
        var data = TimberElementDefaults.For(TimberElementType.Post) with
        {
            FootprintWidthEdgeIndex = 0,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500,
        };

        var plan = TimberAnnotationRefreshPlanner.Create(data, isRectangularFootprintPost: true);

        Assert.False(string.IsNullOrWhiteSpace(refreshPath));
        Assert.False(plan.EnsureLabel);
        Assert.False(plan.ReconcileSlopeArrow);
        Assert.False(plan.ShouldSlopeArrowExist);
        Assert.False(plan.ShouldHorizontalSlopeMarkerExist);
        Assert.False(plan.ShouldPostPerpendicularMarkerExist);
        Assert.False(plan.ReconcileSlopeAngleText);
        Assert.False(plan.ShouldSlopeAngleTextExist);
    }

    [Theory]
    [InlineData("ROTATE", true)]
    [InlineData("_ROTATE", true)]
    [InlineData(".ROTATE", true)]
    [InlineData("MOVE", false)]
    [InlineData("AK_EDIT", false)]
    public void RotateCommand_RequiresFullTimberAnnotationRefresh(string commandName, bool expected)
    {
        Assert.Equal(
            expected,
            LiveGeometryCommandRules.RequiresFullTimberAnnotationRefresh(commandName));
    }
}
