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
        Assert.True(plan.ReconcileSlopeAngleText);
        Assert.True(plan.ShouldSlopeAngleTextExist);
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
