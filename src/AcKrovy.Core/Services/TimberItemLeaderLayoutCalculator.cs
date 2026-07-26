using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberItemLeaderLayoutCalculator
{
    public const double TextHeightMm = TimberMainAnnotationTextRules.TextHeightMm;
    public const double FramePaddingMm = 70d;
    public const double MinimumCircleDiameterMm = 450d;
    public const double MinimumSlotWidthMm = 500d;
    public const double MinimumEnvelopeHeightMm = 360d;
    public const double EnvelopeSizeStepMm = 20d;
    public const double EstimatedCharacterWidthFactor = 0.62d;
    public const double TextClearanceMm = 240d;
    public const double MinimumLeaderRunMm = 180d;
    public const double FirstSegmentAngleRadians = Math.PI / 3d;
    public const double FirstSegmentLengthMm = 360d;
    public const double FramedLeaderAdditionalOffsetMm = 350d;
    public const double FramedFirstSegmentAngleRadians = Math.PI / 3d;
    public const double FramedItemLandingDistanceMm = 0d;
    public const double CombinedFramedLandingDistanceMm = 350d;
    public const double AngleToleranceRadians = 1e-8d;

    public static TimberItemLeaderLayout Calculate(
        TimberLeaderPlacement placement,
        string itemText,
        ItemNumberLeaderStyle style,
        TimberLeaderHorizontalSide? preferredSide = null)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }

        var normalizedText = itemText?.Trim() ?? string.Empty;
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(style);
        var estimatedTextWidth = Math.Max(
            TextHeightMm,
            normalizedText.Length * TextHeightMm * EstimatedCharacterWidthFactor);
        var paddedWidth = QuantizeUp(estimatedTextWidth + 2d * FramePaddingMm);
        var envelopeWidth = normalizedStyle switch
        {
            ItemNumberLeaderStyle.Circle => Math.Max(MinimumCircleDiameterMm, paddedWidth),
            ItemNumberLeaderStyle.Slot => Math.Max(MinimumSlotWidthMm, paddedWidth),
            _ => estimatedTextWidth,
        };
        var envelopeHeight = normalizedStyle == ItemNumberLeaderStyle.Plain
            ? TextHeightMm
            : Math.Max(MinimumEnvelopeHeightMm, TextHeightMm + 2d * FramePaddingMm);
        if (normalizedStyle == ItemNumberLeaderStyle.Circle)
        {
            envelopeHeight = envelopeWidth;
        }

        if (normalizedStyle is not ItemNumberLeaderStyle.Plain)
        {
            var axisX = Math.Cos(placement.RotationRadians);
            var axisY = Math.Sin(placement.RotationRadians);
            var existingOffset =
                envelopeWidth / 2d +
                TextClearanceMm +
                MinimumLeaderRunMm;
            var existingContentX = placement.TextX + axisX * existingOffset;
            var existingContentY = placement.TextY + axisY * existingOffset;
            var existingSide = existingContentX < placement.AnchorX
                ? TimberLeaderHorizontalSide.Left
                : TimberLeaderHorizontalSide.Right;
            return new TimberItemLeaderLayout(
                placement.AnchorX,
                placement.AnchorY,
                existingContentX,
                existingContentY,
                existingContentX,
                existingContentY,
                existingSide,
                envelopeWidth,
                envelopeHeight);
        }

        var side = preferredSide ?? (Math.Cos(placement.RotationRadians) < 0d
            ? TimberLeaderHorizontalSide.Left
            : TimberLeaderHorizontalSide.Right);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            FirstSegmentLengthMm,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up);
        var horizontalDirection = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        var alongAxisOffset =
            envelopeWidth / 2d +
            TextClearanceMm +
            MinimumLeaderRunMm;
        var contentX = placement.AnchorX + horizontalDirection * alongAxisOffset;
        var contentY = knee.Y + TextHeightMm / 2d;

        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            contentX,
            contentY,
            side,
            envelopeWidth,
            envelopeHeight);
    }

    public static TimberItemLeaderLayout CalculateBlock(
        TimberLeaderPlacement placement,
        string itemText,
        ItemNumberLeaderStyle style,
        TimberLeaderHorizontalSide? preferredSide = null)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }

        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, itemText);

        var side = preferredSide ?? (Math.Cos(placement.RotationRadians) < 0d
            ? TimberLeaderHorizontalSide.Left
            : TimberLeaderHorizontalSide.Right);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            FirstSegmentLengthMm + FramedLeaderAdditionalOffsetMm,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up,
            FramedFirstSegmentAngleRadians);
        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            knee.X,
            knee.Y,
            side,
            definition.WidthMm,
            definition.HeightMm);
    }

    public static (double X, double Y) CalculateKnee(
        double anchorX,
        double anchorY,
        TimberLeaderHorizontalSide side,
        double segmentLengthMm,
        TimberLeaderPlaneBasis? planeBasis = null,
        TimberLeaderVerticalSide verticalSide = TimberLeaderVerticalSide.Up,
        double firstSegmentAngleRadians = FirstSegmentAngleRadians)
    {
        if (segmentLengthMm <= 0d ||
            double.IsNaN(segmentLengthMm) ||
            double.IsInfinity(segmentLengthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(segmentLengthMm));
        }

        var basis = NormalizeBasis(planeBasis ?? TimberLeaderPlaneBasis.WorldXY);
        var horizontalDirection = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        var verticalDirection = verticalSide == TimberLeaderVerticalSide.Down ? -1d : 1d;
        var directionX =
            horizontalDirection * Math.Cos(firstSegmentAngleRadians) * basis.HorizontalX +
            verticalDirection * Math.Sin(firstSegmentAngleRadians) * basis.VerticalX;
        var directionY =
            horizontalDirection * Math.Cos(firstSegmentAngleRadians) * basis.HorizontalY +
            verticalDirection * Math.Sin(firstSegmentAngleRadians) * basis.VerticalY;
        return (
            anchorX + segmentLengthMm * directionX,
            anchorY + segmentLengthMm * directionY);
    }

    public static (double X, double Y) CalculateSegmentMidpoint(
        double startX,
        double startY,
        double endX,
        double endY) =>
        (
            startX + (endX - startX) / 2d,
            startY + (endY - startY) / 2d);

    public static double MeasureAcuteAngleRadians(
        double segmentX,
        double segmentY,
        double localHorizontalX,
        double localHorizontalY)
    {
        var segmentLength = Math.Sqrt(segmentX * segmentX + segmentY * segmentY);
        var horizontalLength = Math.Sqrt(
            localHorizontalX * localHorizontalX +
            localHorizontalY * localHorizontalY);
        if (segmentLength <= 0d ||
            horizontalLength <= 0d ||
            double.IsNaN(segmentLength) ||
            double.IsNaN(horizontalLength) ||
            double.IsInfinity(segmentLength) ||
            double.IsInfinity(horizontalLength))
        {
            throw new ArgumentOutOfRangeException(nameof(segmentX));
        }

        var normalizedDot =
            (segmentX * localHorizontalX + segmentY * localHorizontalY) /
            (segmentLength * horizontalLength);
        return Math.Acos(Math.Min(1d, Math.Max(-1d, Math.Abs(normalizedDot))));
    }

    private static TimberLeaderPlaneBasis NormalizeBasis(TimberLeaderPlaneBasis basis)
    {
        var horizontalLength = Math.Sqrt(
            basis.HorizontalX * basis.HorizontalX +
            basis.HorizontalY * basis.HorizontalY);
        var verticalLength = Math.Sqrt(
            basis.VerticalX * basis.VerticalX +
            basis.VerticalY * basis.VerticalY);
        if (horizontalLength <= 0d || verticalLength <= 0d)
        {
            throw new ArgumentException("Annotation plane basis must contain non-zero axes.", nameof(basis));
        }

        var horizontalX = basis.HorizontalX / horizontalLength;
        var horizontalY = basis.HorizontalY / horizontalLength;
        var verticalX = basis.VerticalX / verticalLength;
        var verticalY = basis.VerticalY / verticalLength;
        if (Math.Abs(horizontalX * verticalX + horizontalY * verticalY) > AngleToleranceRadians)
        {
            throw new ArgumentException("Annotation plane axes must be perpendicular.", nameof(basis));
        }

        return new TimberLeaderPlaneBasis(horizontalX, horizontalY, verticalX, verticalY);
    }

    private static double QuantizeUp(double value) =>
        Math.Ceiling(value / EnvelopeSizeStepMm) * EnvelopeSizeStepMm;
}
