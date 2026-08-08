using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// NEW G5 Combined create placement contract measured in final world space.
/// Layout is authored in canonical local T=+X / N=+Y, then rotated by the
/// readable axis. Desired world side is always
/// <see cref="TimberFramedCombinedG5RefreshPlacementRules.DefaultCreateSide"/>
/// relative to source Start→End (not merely RequestedSide / readable-local).
/// When readability folds the axis by π, the layout Side enum is mirrored so
/// the final annotation stays on Start→End Right.
/// </summary>
public static class TimberFramedCombinedG5CreatePlacementRules
{
    public const double FirstSegmentAngleToleranceDeg = 0.01d;

    public const double SideToleranceMm = 1e-6d;

    /// <summary>
    /// Desired world side for every new Combined create / recreate.
    /// </summary>
    public static TimberLeaderHorizontalSide DesiredWorldSide =>
        TimberFramedCombinedG5RefreshPlacementRules.DefaultCreateSide;

    /// <summary>
    /// Layout <see cref="TimberLeaderHorizontalSide"/> fed to
    /// <see cref="TimberFramedBlockContentLayoutCalculator"/> so that after
    /// TransformBy(readable) the annotation lies on
    /// <see cref="DesiredWorldSide"/> of source Start→End.
    /// </summary>
    public static TimberLeaderHorizontalSide ResolveCreateLayoutSide(
        double rawElementAxisRadians) =>
        TimberFramedBlockContentReadableOrientationRules
            .Decide(rawElementAxisRadians)
            .ReadableFlip
            ? TimberFramedBlockContentDoglegRules.Opposite(DesiredWorldSide)
            : DesiredWorldSide;

    /// <summary>
    /// Rotate a pre-transform layout point (canonical horizontal) into world
    /// around the attachment by the readable axis — same contract as host
    /// <c>TransformBy</c>.
    /// </summary>
    public static TimberPlanarPoint ToWorldAfterReadableTransform(
        TimberFramedBlockContentLayout layout,
        TimberPlanarPoint localPoint)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        var ax = layout.AttachmentLocal.X;
        var ay = layout.AttachmentLocal.Y;
        var dx = localPoint.X - ax;
        var dy = localPoint.Y - ay;
        var cos = Math.Cos(layout.ReadableAngleRadians);
        var sin = Math.Sin(layout.ReadableAngleRadians);
        return new TimberPlanarPoint(
            ax + (dx * cos) - (dy * sin),
            ay + (dx * sin) + (dy * cos));
    }

    public static TimberPlanarPoint WorldKnee(TimberFramedBlockContentLayout layout) =>
        ToWorldAfterReadableTransform(layout, layout.KneeLocal);

    public static TimberPlanarPoint WorldBlockPosition(
        TimberFramedBlockContentLayout layout) =>
        ToWorldAfterReadableTransform(layout, layout.LandingEndLocal);

    /// <summary>
    /// KROVY source basis: T = Start→End, N = 90° CCW.
    /// Positive <paramref name="signedSide"/> = Right.
    /// </summary>
    public static bool TryMeasureSignedSide(
        double attachmentX,
        double attachmentY,
        double centerX,
        double centerY,
        double startX,
        double startY,
        double endX,
        double endY,
        out double signedSide,
        out TimberLeaderHorizontalSide worldSide)
    {
        signedSide = 0d;
        worldSide = DesiredWorldSide;
        if (!TrySourceBasis(
                startX,
                startY,
                endX,
                endY,
                out _,
                out _,
                out var nX,
                out var nY))
        {
            return false;
        }

        signedSide =
            ((centerX - attachmentX) * nX) +
            ((centerY - attachmentY) * nY);
        worldSide = signedSide >= -SideToleranceMm
            ? TimberLeaderHorizontalSide.Right
            : TimberLeaderHorizontalSide.Left;
        return true;
    }

    /// <summary>
    /// Acute angle (degrees) between attachment→knee and source Start→End.
    /// </summary>
    public static bool TryMeasureFirstSegmentAngleDeg(
        double attachmentX,
        double attachmentY,
        double kneeX,
        double kneeY,
        double startX,
        double startY,
        double endX,
        double endY,
        out double angleDeg)
    {
        angleDeg = double.NaN;
        var sx = kneeX - attachmentX;
        var sy = kneeY - attachmentY;
        if (!TrySourceBasis(
                startX,
                startY,
                endX,
                endY,
                out var tX,
                out var tY,
                out _,
                out _))
        {
            return false;
        }

        var length = Math.Sqrt((sx * sx) + (sy * sy));
        if (length <= SideToleranceMm)
        {
            return false;
        }

        angleDeg = TimberItemLeaderLayoutCalculator.MeasureAcuteAngleRadians(
            sx,
            sy,
            tX,
            tY) * 180d / Math.PI;
        return true;
    }

    public static bool FirstSegmentAngleIsSixtyDegrees(double angleDeg) =>
        Math.Abs(
            angleDeg -
            (TimberItemLeaderLayoutCalculator.FramedFirstSegmentAngleRadians *
             180d / Math.PI)) <=
        FirstSegmentAngleToleranceDeg;

    public static TimberFramedBlockContentLayout CalculateCreate(
        double attachmentX,
        double attachmentY,
        double rawElementAxisRadians,
        TimberFramedBlockContentKind contentKind,
        double frameWidthMm,
        double frameHeightMm,
        int annotationScaleDenominator,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        double firstSegmentLengthModelMm,
        double landingLengthModelMm,
        double dimensionColumnEnvelopeWidthMm,
        TimberFramedBlockContentDimensionColumnSide dimensionColumnSide) =>
        TimberFramedCombinedG5RefreshPlacementRules.CalculateCanonical(
            attachmentX,
            attachmentY,
            rawElementAxisRadians,
            ResolveCreateLayoutSide(rawElementAxisRadians),
            contentKind,
            frameWidthMm,
            frameHeightMm,
            annotationScaleDenominator,
            itemPaperHeightMm,
            dimensionPaperHeightMm,
            firstSegmentLengthModelMm,
            landingLengthModelMm,
            dimensionColumnEnvelopeWidthMm,
            dimensionColumnSide);

    private static bool TrySourceBasis(
        double startX,
        double startY,
        double endX,
        double endY,
        out double tX,
        out double tY,
        out double nX,
        out double nY)
    {
        tX = endX - startX;
        tY = endY - startY;
        nX = 0d;
        nY = 0d;
        var length = Math.Sqrt((tX * tX) + (tY * tY));
        if (length <= SideToleranceMm)
        {
            return false;
        }

        tX /= length;
        tY /= length;
        nX = -tY;
        nY = tX;
        return true;
    }
}
