using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberSlopeAnnotationPlacementCalculatorTests
{
    [Fact]
    public void LongElementWithoutCollisionKeepsPreferredOneThirdPosition()
    {
        var placement = TimberSlopeAnnotationPlacementCalculator.Calculate(
            6000d,
            new TimberSlopeAnnotationLongitudinalInterval(2800d, 3600d));

        Assert.Equal(2000d, placement.AnchorDistanceMm);
        Assert.True(placement.UsesPreferredPosition);
    }

    [Fact]
    public void ShorterElementMovesAnnotationTowardStartWhenOneThirdCollides()
    {
        var placement = TimberSlopeAnnotationPlacementCalculator.Calculate(
            3000d,
            new TimberSlopeAnnotationLongitudinalInterval(1100d, 1900d));

        Assert.Equal(780d, placement.AnchorDistanceMm);
        Assert.False(placement.UsesPreferredPosition);
    }

    [Fact]
    public void PlacementUsesEndSideWhenStartSideHasInsufficientSpace()
    {
        var placement = TimberSlopeAnnotationPlacementCalculator.Calculate(
            4000d,
            new TimberSlopeAnnotationLongitudinalInterval(200d, 1500d));

        Assert.Equal(1820d, placement.AnchorDistanceMm);
        Assert.False(placement.UsesPreferredPosition);
    }

    [Fact]
    public void DirectionDoesNotParticipateInAnchorCalculation()
    {
        var normal = TimberSlopeAnnotationPlacementCalculator.Calculate(
            3000d,
            new TimberSlopeAnnotationLongitudinalInterval(1100d, 1900d));
        var reversed = TimberSlopeAnnotationPlacementCalculator.Calculate(
            3000d,
            new TimberSlopeAnnotationLongitudinalInterval(1100d, 1900d));

        Assert.Equal(normal.AnchorDistanceMm, reversed.AnchorDistanceMm);
        Assert.NotEqual(
            TimberSlopeArrowCalculator.Calculate(0d, 0d, 3000d, 0d, normal.AnchorDistanceMm, 0d, false),
            TimberSlopeArrowCalculator.Calculate(0d, 0d, 3000d, 0d, reversed.AnchorDistanceMm, 0d, true));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(40d)]
    [InlineData(300d)]
    public void VeryShortElementProducesFiniteStableAnchor(double length)
    {
        var first = TimberSlopeAnnotationPlacementCalculator.Calculate(
            length,
            new TimberSlopeAnnotationLongitudinalInterval(-500d, 500d));
        var second = TimberSlopeAnnotationPlacementCalculator.Calculate(
            length,
            new TimberSlopeAnnotationLongitudinalInterval(-500d, 500d));

        Assert.True(double.IsFinite(first.AnchorDistanceMm));
        Assert.InRange(first.AnchorDistanceMm, 0d, length);
        Assert.Equal(first, second);
    }
}
