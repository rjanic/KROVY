using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementLabelPlacementCalculatorTests
{
    [Fact]
    public void Calculate_HorizontalShortAndLongLinesUseSamePerpendicularOffset()
    {
        var shortLine = TimberElementLabelPlacementCalculator.Calculate(0, 0, 3000, 0, 1500, 0, 180);
        var longLine = TimberElementLabelPlacementCalculator.Calculate(0, 0, 15000, 0, 7500, 0, 180);

        Assert.Equal(180, DistanceFromLine(0, 0, 3000, 0, shortLine.X, shortLine.Y), precision: 6);
        Assert.Equal(180, DistanceFromLine(0, 0, 15000, 0, longLine.X, longLine.Y), precision: 6);
    }

    [Fact]
    public void Calculate_VerticalLineUsesFixedPerpendicularOffset()
    {
        var placement = TimberElementLabelPlacementCalculator.Calculate(0, 0, 0, 5000, 0, 2500, 180);

        Assert.Equal(180, DistanceFromLine(0, 0, 0, 5000, placement.X, placement.Y), precision: 6);
    }

    [Fact]
    public void Calculate_DiagonalLineUsesFixedPerpendicularOffset()
    {
        var placement = TimberElementLabelPlacementCalculator.Calculate(0, 0, 3000, 3000, 1500, 1500, 180);

        Assert.Equal(180, DistanceFromLine(0, 0, 3000, 3000, placement.X, placement.Y), precision: 6);
    }

    [Fact]
    public void Calculate_ExtendingLineMovesToNewMidpointButKeepsOffset()
    {
        var original = TimberElementLabelPlacementCalculator.Calculate(0, 0, 3000, 0, 1500, 0, 180);
        var extended = TimberElementLabelPlacementCalculator.Calculate(0, 0, 15000, 0, 7500, 0, 180);

        Assert.Equal(1500, original.X, precision: 6);
        Assert.Equal(7500, extended.X, precision: 6);
        Assert.Equal(180, original.Y, precision: 6);
        Assert.Equal(180, extended.Y, precision: 6);
    }

    [Fact]
    public void Calculate_RotatedElementMovesLabelAndRotatesItAlongElementAxis()
    {
        var horizontal = TimberElementLabelPlacementCalculator.Calculate(0, 0, 4000, 0, 2000, 0, 180);
        var vertical = TimberElementLabelPlacementCalculator.Calculate(0, 0, 0, 4000, 0, 2000, 180);

        Assert.Equal(0, horizontal.RotationRadians, precision: 6);
        Assert.Equal(Math.PI / 2d, vertical.RotationRadians, precision: 6);
        Assert.Equal(2000, horizontal.X, precision: 6);
        Assert.Equal(2000, vertical.Y, precision: 6);
    }

    private static double DistanceFromLine(
        double startX,
        double startY,
        double endX,
        double endY,
        double pointX,
        double pointY)
    {
        var dx = endX - startX;
        var dy = endY - startY;
        var numerator = Math.Abs(dy * pointX - dx * pointY + endX * startY - endY * startX);
        var denominator = Math.Sqrt(dx * dx + dy * dy);
        return numerator / denominator;
    }
}
