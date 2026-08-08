using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberSlopeAnnotationLongitudinalIntervalRoleTests
{
    [Theory]
    [InlineData(TimberMainAnnotationComponentRole.Primary, true)]
    [InlineData(TimberMainAnnotationComponentRole.FramedItem, true)]
    [InlineData(TimberMainAnnotationComponentRole.CircleText, true)]
    [InlineData(TimberMainAnnotationComponentRole.CircleLeaderLine, false)]
    [InlineData(TimberMainAnnotationComponentRole.CircleFrame, false)]
    public void IsLongitudinalIntervalLabelRole_ExcludesG4LeaderAndFrame(
        TimberMainAnnotationComponentRole role,
        bool expected) =>
        Assert.Equal(
            expected,
            TimberSlopeAnnotationRules.IsLongitudinalIntervalLabelRole(role));
}
