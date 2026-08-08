using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberItemLeaderLayoutCalculator
{
    public const double TextHeightMm = TimberMainAnnotationTextRules.TextHeightMm;
    public const double FramePaddingMm =
        TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;
    public const double MinimumCircleDiameterMm =
        TimberItemLeaderBlockDefinitionRules.CircleDiameterMm;
    public const double MinimumSlotWidthMm =
        TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm;
    public const double MinimumEnvelopeHeightMm =
        TimberItemLeaderBlockDefinitionRules.FrameHeightMm;
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
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationScaleFactor));
        }

        var normalizedText = itemText?.Trim() ?? string.Empty;
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(style);
        if (normalizedStyle is not ItemNumberLeaderStyle.Plain)
        {
            var definition =
                TimberItemLeaderBlockDefinitionRules.Resolve(
                    normalizedStyle,
                    normalizedText);
            var scaledEnvelopeWidth =
                definition.WidthMm * presentationScaleFactor;
            var scaledEnvelopeHeight =
                definition.HeightMm * presentationScaleFactor;
            var axisX = Math.Cos(placement.RotationRadians);
            var axisY = Math.Sin(placement.RotationRadians);
            var existingOffset =
                scaledEnvelopeWidth / 2d +
                TextClearanceMm * presentationScaleFactor +
                MinimumLeaderRunMm * presentationScaleFactor;
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
                scaledEnvelopeWidth,
                scaledEnvelopeHeight);
        }

        var effectiveTextHeight = normalizedStyle == ItemNumberLeaderStyle.Plain
            ? TextHeightMm * presentationScaleFactor
            : TextHeightMm;
        var estimatedTextWidth = Math.Max(
            effectiveTextHeight,
            normalizedText.Length * effectiveTextHeight * EstimatedCharacterWidthFactor);
        var paddedWidth = QuantizeUp(estimatedTextWidth + 2d * FramePaddingMm);
        var envelopeWidth = normalizedStyle switch
        {
            ItemNumberLeaderStyle.Circle => Math.Max(MinimumCircleDiameterMm, paddedWidth),
            ItemNumberLeaderStyle.Slot => Math.Max(MinimumSlotWidthMm, paddedWidth),
            _ => estimatedTextWidth,
        };
        var envelopeHeight = normalizedStyle == ItemNumberLeaderStyle.Plain
            ? effectiveTextHeight
            : Math.Max(MinimumEnvelopeHeightMm, TextHeightMm + 2d * FramePaddingMm);
        if (normalizedStyle == ItemNumberLeaderStyle.Circle)
        {
            envelopeHeight = envelopeWidth;
        }

        var side = preferredSide ?? (Math.Cos(placement.RotationRadians) < 0d
            ? TimberLeaderHorizontalSide.Left
            : TimberLeaderHorizontalSide.Right);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            FirstSegmentLengthMm * presentationScaleFactor,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up);
        var horizontalDirection = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        var alongAxisOffset =
            envelopeWidth / 2d +
            TextClearanceMm * presentationScaleFactor +
            MinimumLeaderRunMm * presentationScaleFactor;
        var contentX = placement.AnchorX + horizontalDirection * alongAxisOffset;
        var contentY = knee.Y + effectiveTextHeight / 2d;

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

    public static TimberItemLeaderLayout CalculatePlainItemNumber(
        TimberLeaderPlacement placement,
        string itemText,
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationScaleFactor));
        }

        var effectiveTextHeight =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor);
        var normalizedText = itemText?.Trim() ?? string.Empty;
        var envelopeWidth = Math.Max(
            effectiveTextHeight,
            normalizedText.Length *
            effectiveTextHeight *
            EstimatedCharacterWidthFactor);
        var side = preferredSide ??
            (Math.Cos(placement.RotationRadians) < 0d
                ? TimberLeaderHorizontalSide.Left
                : TimberLeaderHorizontalSide.Right);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            FirstSegmentLengthMm * presentationScaleFactor,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up);

        var normalX = placement.TextX - placement.AnchorX;
        var normalY = placement.TextY - placement.AnchorY;
        var normalLength = Math.Sqrt(
            normalX * normalX +
            normalY * normalY);
        if (normalLength <= AngleToleranceRadians)
        {
            normalX = -Math.Sin(placement.RotationRadians);
            normalY = Math.Cos(placement.RotationRadians);
            normalLength = 1d;
        }
        normalX /= normalLength;
        normalY /= normalLength;

        // Native Plain item text remains horizontal. Use its projected
        // half-envelope along the source normal so every orientation keeps
        // the same edge clearance from the source axis.
        var projectedHalfEnvelope =
            Math.Abs(normalX) * envelopeWidth / 2d +
            Math.Abs(normalY) * effectiveTextHeight / 2d;
        var centerOffset =
            projectedHalfEnvelope +
            TimberItemNumberTypographyRules.CalculatePlainTextClearanceMm(
                presentationScaleFactor);
        var contentX = placement.AnchorX + normalX * centerOffset;
        var contentY = placement.AnchorY + normalY * centerOffset;

        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            contentX,
            contentY,
            side,
            envelopeWidth,
            effectiveTextHeight);
    }

    public static TimberItemLeaderLayout CalculateBlock(
        TimberLeaderPlacement placement,
        string itemText,
        ItemNumberLeaderStyle style,
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationScaleFactor));
        }

        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, itemText);

        var side = preferredSide ?? (Math.Cos(placement.RotationRadians) < 0d
            ? TimberLeaderHorizontalSide.Left
            : TimberLeaderHorizontalSide.Right);
        var planeBasis = TimberLeaderPlaneBasis.FromRotationRadians(
            placement.RotationRadians);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            (FirstSegmentLengthMm + FramedLeaderAdditionalOffsetMm) * presentationScaleFactor,
            planeBasis,
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
            definition.WidthMm * presentationScaleFactor,
            definition.HeightMm * presentationScaleFactor);
    }

    /// <summary>
    /// Combined landing/dogleg direction in the element-aligned plane.
    /// Uses an existing content delta when present; otherwise ±local H from
    /// <paramref name="rotationRadians"/> and <paramref name="side"/> —
    /// never world +X/−X.
    /// </summary>
    public static (double X, double Y) ResolveCombinedLandingDirection(
        double rotationRadians,
        TimberLeaderHorizontalSide side,
        double contentDeltaX = 0d,
        double contentDeltaY = 0d)
    {
        if (double.IsNaN(rotationRadians) || double.IsInfinity(rotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        }

        var contentLength = Math.Sqrt(
            contentDeltaX * contentDeltaX +
            contentDeltaY * contentDeltaY);
        if (contentLength > AngleToleranceRadians)
        {
            return (
                contentDeltaX / contentLength,
                contentDeltaY / contentLength);
        }

        var basis = NormalizeBasis(
            TimberLeaderPlaneBasis.FromRotationRadians(rotationRadians));
        var horizontalDirection = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        return (
            horizontalDirection * basis.HorizontalX,
            horizontalDirection * basis.HorizontalY);
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
