using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentLayoutCalculatorTests
{
    public static IEnumerable<object[]> ContentKinds()
    {
        yield return [TimberFramedBlockContentKind.Plain];
        yield return [TimberFramedBlockContentKind.Circle];
        yield return [TimberFramedBlockContentKind.Rectangle];
        yield return [TimberFramedBlockContentKind.Slot];
    }

    public static IEnumerable<object[]> MatrixCases()
    {
        var kinds = new[]
        {
            TimberFramedBlockContentKind.Plain,
            TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentKind.Rectangle,
            TimberFramedBlockContentKind.Slot,
        };
        var sides = new[]
        {
            TimberLeaderHorizontalSide.Left,
            TimberLeaderHorizontalSide.Right,
        };
        var anglesDeg = new[] { 0d, 35d, 90d, 135d, 180d, 215d, 270d };
        var denominators = new[] { 25, 50, 100 };

        foreach (var kind in kinds)
        {
            foreach (var side in sides)
            {
                foreach (var angle in anglesDeg)
                {
                    foreach (var denominator in denominators)
                    {
                        yield return [kind, side, angle, denominator];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void LayoutMatrix_HonorsSixtyDegreeSegmentClearGapAndSideSign(
        TimberFramedBlockContentKind kind,
        TimberLeaderHorizontalSide side,
        double angleDegrees,
        int denominator)
    {
        var request = CreateRequest(kind, side, angleDegrees * Math.PI / 180d, denominator);
        var layout = TimberFramedBlockContentLayoutCalculator.Calculate(request);

        Assert.Equal(request.AttachmentX, layout.AttachmentLocal.X);
        Assert.Equal(request.AttachmentY, layout.AttachmentLocal.Y);
        Assert.Equal(
            TimberFramedBlockContentLayoutCalculator.FirstSegmentAngleRadians,
            layout.FirstSegmentAngleRadians);
        Assert.Equal(request.FirstSegmentLengthModelMm, layout.FirstSegmentLengthModelMm);
        Assert.Equal(request.LandingLengthModelMm, layout.LandingLengthModelMm);

        var expectedSideSign =
            TimberFramedBlockContentLayoutCalculator.SideSign(side);
        Assert.Equal(expectedSideSign, layout.SideSign);

        var dx = layout.KneeLocal.X - layout.AttachmentLocal.X;
        var dy = layout.KneeLocal.Y - layout.AttachmentLocal.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        Assert.Equal(request.FirstSegmentLengthModelMm, length, 9);

        // Local T=+X: angle = atan2(across, along) with N=+Y.
        var angleDeg = Math.Atan2(dy, dx) * 180d / Math.PI;
        Assert.Equal(expectedSideSign * 60d, angleDeg, 9);

        Assert.Equal(layout.KneeLocal, layout.LandingStartLocal);
        Assert.Equal(
            request.LandingLengthModelMm,
            layout.LandingStartLocal.DistanceTo(layout.LandingEndLocal),
            9);
        Assert.Equal(0d, layout.LandingEndLocal.Y - layout.LandingStartLocal.Y, 9);
        Assert.True(layout.LandingEndLocal.X > layout.LandingStartLocal.X);

        Assert.NotNull(layout.WidthCenterLocal);
        Assert.NotNull(layout.HeightCenterLocal);
        var width = layout.WidthCenterLocal!.Value;
        var height = layout.HeightCenterLocal!.Value;
        Assert.Equal(width.X, height.X, 9);

        var landingY = layout.LandingEndLocal.Y;
        Assert.Equal(
            layout.RowCenterDistanceModelMm / 2d,
            width.Y - landingY,
            9);
        Assert.Equal(
            -layout.RowCenterDistanceModelMm / 2d,
            height.Y - landingY,
            9);
        Assert.Equal(
            layout.RowCenterDistanceModelMm,
            width.DistanceTo(height),
            9);
        Assert.Equal(
            layout.RowClearGapModelMm,
            TimberDimensionRowClearGapRules.CalculateActualGlyphClearGapModelMm(
                width.DistanceTo(height),
                layout.DimensionTextModelHeightMm),
            9);

        var expectedDimHeight =
            TimberDimensionRowClearGapRules.CalculateDimensionTextModelHeightMm(
                2.5d,
                denominator);
        Assert.Equal(expectedDimHeight, layout.DimensionTextModelHeightMm);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(2.7d, denominator),
            layout.ItemTextModelHeightMm);

        if (kind == TimberFramedBlockContentKind.Plain)
        {
            Assert.Equal(0d, layout.FrameWidthMm);
            Assert.Equal(0d, layout.FrameHeightMm);
            Assert.Null(layout.FrameCenterLocal);
        }
        else
        {
            Assert.True(layout.FrameWidthMm > 0d);
            Assert.True(layout.FrameHeightMm > 0d);
            Assert.NotNull(layout.FrameCenterLocal);
            Assert.Equal(layout.LandingEndLocal, layout.FrameCenterLocal!.Value);
        }

        Assert.Equal(
            TimberFramedBlockContentReadableOrientationRules
                .Decide(request.ElementAxisRadians)
                .PresentationAngle,
            layout.ReadableAngleRadians);
        Assert.Equal(
            TimberFramedBlockContentReadableOrientationRules
                .Decide(request.ElementAxisRadians)
                .ReadableFlip,
            layout.ReadabilityFlipped);
    }

    [Theory]
    [InlineData(TimberLeaderHorizontalSide.Left, -1d)]
    [InlineData(TimberLeaderHorizontalSide.Right, 1d)]
    public void LeftRight_AreExactNormalMirrors(TimberLeaderHorizontalSide side, double sideSign)
    {
        var left = TimberFramedBlockContentLayoutCalculator.Calculate(
            CreateRequest(
                TimberFramedBlockContentKind.Circle,
                TimberLeaderHorizontalSide.Left,
                0d,
                50));
        var right = TimberFramedBlockContentLayoutCalculator.Calculate(
            CreateRequest(
                TimberFramedBlockContentKind.Circle,
                TimberLeaderHorizontalSide.Right,
                0d,
                50));

        Assert.Equal(-1d, left.SideSign);
        Assert.Equal(1d, right.SideSign);
        Assert.Equal(sideSign, TimberFramedBlockContentLayoutCalculator.SideSign(side));

        Assert.Equal(left.AttachmentLocal, right.AttachmentLocal);
        Assert.Equal(left.KneeLocal.X, right.KneeLocal.X, 9);
        Assert.Equal(
            -(left.KneeLocal.Y - left.AttachmentLocal.Y),
            right.KneeLocal.Y - right.AttachmentLocal.Y,
            9);
        // Text/frame are not mirrored — only the knee uses ±N.
        Assert.Equal(
            left.WidthCenterLocal!.Value.X - left.LandingEndLocal.X,
            right.WidthCenterLocal!.Value.X - right.LandingEndLocal.X,
            9);
        Assert.Equal(
            left.WidthCenterLocal!.Value.Y - left.LandingEndLocal.Y,
            right.WidthCenterLocal!.Value.Y - right.LandingEndLocal.Y,
            9);
    }

    [Fact]
    public void FrameKind_DoesNotChangeTextScale()
    {
        var plain = TimberFramedBlockContentLayoutCalculator.Calculate(
            CreateRequest(TimberFramedBlockContentKind.Plain, TimberLeaderHorizontalSide.Left, 0d, 50));
        var circle = TimberFramedBlockContentLayoutCalculator.Calculate(
            CreateRequest(TimberFramedBlockContentKind.Circle, TimberLeaderHorizontalSide.Left, 0d, 50));

        Assert.Equal(plain.DimensionTextModelHeightMm, circle.DimensionTextModelHeightMm);
        Assert.Equal(plain.ItemTextModelHeightMm, circle.ItemTextModelHeightMm);
        Assert.Equal(plain.RowClearGapModelMm, circle.RowClearGapModelMm);
        Assert.Equal(plain.RowCenterDistanceModelMm, circle.RowCenterDistanceModelMm);
    }

    [Fact]
    public void ItemOnly_OmitsWidthHeightCenters()
    {
        var request = CreateRequest(
            TimberFramedBlockContentKind.Rectangle,
            TimberLeaderHorizontalSide.Right,
            0d,
            50) with
        {
            Presentation = TimberFramedBlockContentPresentation.ItemOnly,
        };
        var layout = TimberFramedBlockContentLayoutCalculator.Calculate(request);
        Assert.Null(layout.WidthCenterLocal);
        Assert.Null(layout.HeightCenterLocal);
        Assert.Equal(TimberFramedBlockContentPresentation.ItemOnly, layout.Presentation);
    }

    [Fact]
    public void Plain_RejectsNonZeroFrameSize()
    {
        var request = CreateRequest(
            TimberFramedBlockContentKind.Plain,
            TimberLeaderHorizontalSide.Left,
            0d,
            50) with
        {
            FrameWidthMm = 10d,
            FrameHeightMm = 10d,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberFramedBlockContentLayoutCalculator.Calculate(request));
    }

    [Theory]
    [MemberData(nameof(ContentKinds))]
    public void Framed_RejectsNonPositiveFrameSize(TimberFramedBlockContentKind kind)
    {
        if (kind == TimberFramedBlockContentKind.Plain)
        {
            return;
        }

        var request = CreateRequest(kind, TimberLeaderHorizontalSide.Left, 0d, 50) with
        {
            FrameWidthMm = 0d,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberFramedBlockContentLayoutCalculator.Calculate(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(251)]
    public void InvalidDenominator_Throws(int denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberFramedBlockContentLayoutCalculator.Calculate(
                CreateRequest(
                    TimberFramedBlockContentKind.Plain,
                    TimberLeaderHorizontalSide.Left,
                    0d,
                    50) with
                {
                    AnnotationScaleDenominator = denominator,
                }));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void InvalidSegmentLength_Throws(double length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberFramedBlockContentLayoutCalculator.Calculate(
                CreateRequest(
                    TimberFramedBlockContentKind.Plain,
                    TimberLeaderHorizontalSide.Left,
                    0d,
                    50) with
                {
                    FirstSegmentLengthModelMm = length,
                }));
    }

    [Theory]
    [InlineData(0.5d)]
    [InlineData(20d)]
    [InlineData(double.NaN)]
    public void InvalidItemPaperHeight_Throws(double paperHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberFramedBlockContentLayoutCalculator.Calculate(
                CreateRequest(
                    TimberFramedBlockContentKind.Plain,
                    TimberLeaderHorizontalSide.Left,
                    0d,
                    50) with
                {
                    ItemNumberPaperHeightMm = paperHeight,
                }));
    }

    private static TimberFramedBlockContentLayoutRequest CreateRequest(
        TimberFramedBlockContentKind kind,
        TimberLeaderHorizontalSide side,
        double elementAxisRadians,
        int denominator)
    {
        var framed = kind != TimberFramedBlockContentKind.Plain;
        return new TimberFramedBlockContentLayoutRequest(
            AttachmentX: 1000d,
            AttachmentY: 2000d,
            ElementAxisRadians: elementAxisRadians,
            Side: side,
            ContentKind: kind,
            FrameWidthMm: framed ? 400d : 0d,
            FrameHeightMm: framed ? 400d : 0d,
            AnnotationScaleDenominator: denominator,
            ItemNumberPaperHeightMm: 2.7d,
            DimensionPaperHeightMm: 2.5d,
            FirstSegmentLengthModelMm: 900d,
            LandingLengthModelMm:
                TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
                TimberAnnotationScaleRules.GetScaleFactor(denominator),
            DimensionColumnEnvelopeWidthMm: 200d,
            Presentation: TimberFramedBlockContentPresentation.Combined);
    }
}
