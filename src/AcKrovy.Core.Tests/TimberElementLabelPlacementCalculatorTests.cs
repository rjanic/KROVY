using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementLabelPlacementCalculatorTests
{
    [Fact]
    public void Calculate_HorizontalShortAndLongLinesUseSamePerpendicularOffset()
    {
        var shortLine = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 3000, 0, 1500, 0, 180);
        var longLine = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 15000, 0, 7500, 0, 180);

        Assert.Equal(
            180,
            DistanceFromLine(0, 0, 3000, 0, shortLine.X, shortLine.Y),
            precision: 6);
        Assert.Equal(
            180,
            DistanceFromLine(0, 0, 15000, 0, longLine.X, longLine.Y),
            precision: 6);
    }

    [Fact]
    public void Calculate_VerticalLineUsesFixedPerpendicularOffset()
    {
        var placement = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 0, 5000, 0, 2500, 180);

        Assert.Equal(
            180,
            DistanceFromLine(
                0, 0, 0, 5000, placement.X, placement.Y),
            precision: 6);
    }

    [Fact]
    public void Calculate_DiagonalLineUsesFixedPerpendicularOffset()
    {
        var placement = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 3000, 3000, 1500, 1500, 180);

        Assert.Equal(
            180,
            DistanceFromLine(
                0, 0, 3000, 3000, placement.X, placement.Y),
            precision: 6);
    }

    [Fact]
    public void Calculate_ExtendingLineMovesToNewMidpointButKeepsOffset()
    {
        var original = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 3000, 0, 1500, 0, 180);
        var extended = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 15000, 0, 7500, 0, 180);

        Assert.Equal(1500, original.X, precision: 6);
        Assert.Equal(7500, extended.X, precision: 6);
        Assert.Equal(180, original.Y, precision: 6);
        Assert.Equal(180, extended.Y, precision: 6);
    }

    [Fact]
    public void Calculate_RotatedElementMovesLabelAndRotatesItAlongElementAxis()
    {
        var horizontal = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 4000, 0, 2000, 0, 180);
        var vertical = TimberElementLabelPlacementCalculator.Calculate(
            0, 0, 0, 4000, 0, 2000, 180);

        Assert.Equal(0, horizontal.RotationRadians, precision: 6);
        Assert.Equal(Math.PI / 2d, vertical.RotationRadians, precision: 6);
        Assert.Equal(2000, horizontal.X, precision: 6);
        Assert.Equal(2000, vertical.Y, precision: 6);
    }

    [Theory]
    [InlineData(0.5d, 52.083333333333336d)]
    [InlineData(1d, 104.16666666666667d)]
    [InlineData(2d, 208.33333333333334d)]
    public void Calculate_ThreeLineFullLabelCenterOffsetScalesExactlyOnce(
        double presentationScaleFactor,
        double expectedCenterOffsetMm)
    {
        var textHeightMm =
            TimberDimensionTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor);
        var centerOffsetMm =
            TimberDimensionTypographyRules
                .CalculateFullLabelCenterOffsetMm(textHeightMm);
        var placement = TimberElementLabelPlacementCalculator.Calculate(
            0d,
            0d,
            4000d,
            0d,
            2000d,
            0d,
            centerOffsetMm);

        Assert.Equal(expectedCenterOffsetMm, placement.Y, precision: 8);
        Assert.Equal(
            expectedCenterOffsetMm,
            DistanceFromLine(
                0d,
                0d,
                4000d,
                0d,
                placement.X,
                placement.Y),
            precision: 8);
    }

    [Fact]
    public void Calculate_CanonicalFullLabelPlacesSourceMidwayBetweenSecondAndThirdLines()
    {
        const string contents = "K2\\P80x160\\P2900 mm";
        const double textHeightMm = 125d;
        var lineAdvanceMm =
            TimberDimensionTypographyRules.CalculateLineAdvanceMm(
                textHeightMm);
        var centerOffsetMm =
            TimberDimensionTypographyRules
                .CalculateFullLabelCenterOffsetMm(textHeightMm);

        var placement = TimberElementLabelPlacementCalculator.Calculate(
            0d,
            0d,
            4000d,
            0d,
            2000d,
            0d,
            centerOffsetMm);

        var lines = contents.Split("\\P");
        var sourceLineRelativeToTextCenterMm = -placement.Y;
        const double secondLineCenterMm = 0d;
        var thirdLineCenterMm = -lineAdvanceMm;
        var secondToThirdGapMidpointMm =
            (secondLineCenterMm + thirdLineCenterMm) / 2d;

        Assert.Equal(3, lines.Length);
        Assert.Equal("80x160", lines[1]);
        Assert.Equal("2900 mm", lines[2]);
        Assert.Equal(
            textHeightMm * TimberDimensionTypographyRules.MTextLineAdvanceFactor,
            lineAdvanceMm,
            precision: 8);
        Assert.Equal(
            secondToThirdGapMidpointMm,
            sourceLineRelativeToTextCenterMm,
            precision: 8);
        Assert.InRange(
            sourceLineRelativeToTextCenterMm,
            thirdLineCenterMm,
            secondLineCenterMm);
        Assert.NotEqual(
            thirdLineCenterMm,
            sourceLineRelativeToTextCenterMm,
            precision: 8);
    }

    [Theory]
    [InlineData(4000d, 0d)]
    [InlineData(0d, 4000d)]
    [InlineData(4000d, 3000d)]
    public void Calculate_ThreeLineFullLabelUsesSameNormalOffsetForEveryOrientation(
        double endX,
        double endY)
    {
        var centerOffsetMm =
            TimberDimensionTypographyRules
                .CalculateFullLabelCenterOffsetMm(125d);
        var placement = TimberElementLabelPlacementCalculator.Calculate(
            0d,
            0d,
            endX,
            endY,
            endX / 2d,
            endY / 2d,
            centerOffsetMm);

        Assert.Equal(
            centerOffsetMm,
            DistanceFromLine(
                0d,
                0d,
                endX,
                endY,
                placement.X,
                placement.Y),
            precision: 8);
    }

    [Fact]
    public void Calculate_RepeatedRefreshKeepsSideAndRelativePlacement()
    {
        var centerOffsetMm =
            TimberDimensionTypographyRules
                .CalculateFullLabelCenterOffsetMm(125d);
        var first = TimberElementLabelPlacementCalculator.Calculate(
            500d,
            200d,
            4500d,
            3200d,
            2500d,
            1700d,
            centerOffsetMm);
        var refreshed = TimberElementLabelPlacementCalculator.Calculate(
            500d,
            200d,
            4500d,
            3200d,
            2500d,
            1700d,
            centerOffsetMm);

        Assert.Equal(first, refreshed);
        Assert.Equal(
            centerOffsetMm,
            DistanceFromLine(
                500d,
                200d,
                4500d,
                3200d,
                refreshed.X,
                refreshed.Y),
            precision: 8);
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
        var numerator = Math.Abs(
            dy * pointX -
            dx * pointY +
            endX * startY -
            endY * startX);
        var denominator = Math.Sqrt(dx * dx + dy * dy);
        return numerator / denominator;
    }
}
