using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CREATE-only first-segment finalization. After host insert / style / dogleg /
/// landing / BlockPosition / TransformBy rewrites, recompute attachment→knee so
/// the FINAL world acute angle to source Start→End is 60° ±0.01°.
/// Never used for grip STRETCH, refresh, content edit, or user-placement keep.
/// </summary>
public static class TimberFramedCombinedG5CreateFirstSegmentRules
{
    public const double AngleToleranceDeg =
        TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleToleranceDeg;

    /// <summary>
    /// Desired world knee from final attachment, preserving segment length and
    /// layout side sign, using the readable annotation tangent (same as host
    /// TransformBy of the canonical horizontal baseline).
    /// </summary>
    public static TimberPlanarPoint BuildCorrectedKnee(
        TimberPlanarPoint attachment,
        double segmentLengthModelMm,
        double readableAngleRadians,
        double sideSign)
    {
        if (segmentLengthModelMm <=
            TimberFramedCombinedG5CreatePlacementRules.SideToleranceMm)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentLengthModelMm));
        }

        if (double.IsNaN(sideSign) ||
            double.IsInfinity(sideSign) ||
            Math.Abs(sideSign) <=
                TimberFramedCombinedG5CreatePlacementRules.SideToleranceMm)
        {
            throw new ArgumentOutOfRangeException(nameof(sideSign));
        }

        var angle = TimberItemLeaderLayoutCalculator.FramedFirstSegmentAngleRadians;
        var tX = Math.Cos(readableAngleRadians);
        var tY = Math.Sin(readableAngleRadians);
        var nX = -tY;
        var nY = tX;
        var sign = sideSign >= 0d ? 1d : -1d;
        return new TimberPlanarPoint(
            attachment.X +
            (segmentLengthModelMm * Math.Cos(angle) * tX) +
            (sign * segmentLengthModelMm * Math.Sin(angle) * nX),
            attachment.Y +
            (segmentLengthModelMm * Math.Cos(angle) * tY) +
            (sign * segmentLengthModelMm * Math.Sin(angle) * nY));
    }

    /// <summary>
    /// CREATE landing after a corrected knee: second segment stays along the
    /// readable tangent (+T), matching
    /// <see cref="TimberFramedBlockContentLayoutCalculator"/>
    /// (<c>landingEnd = knee + T · landingLength</c>). Never inherits the 60°
    /// first-segment tilt. Preserves dogleg length.
    /// </summary>
    public static TimberPlanarPoint BuildCorrectedLandingEnd(
        TimberPlanarPoint correctedKnee,
        double readableAngleRadians,
        double landingLengthModelMm)
    {
        if (landingLengthModelMm <=
            TimberFramedCombinedG5CreatePlacementRules.SideToleranceMm)
        {
            throw new ArgumentOutOfRangeException(nameof(landingLengthModelMm));
        }

        if (double.IsNaN(readableAngleRadians) ||
            double.IsInfinity(readableAngleRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(readableAngleRadians));
        }

        var tX = Math.Cos(readableAngleRadians);
        var tY = Math.Sin(readableAngleRadians);
        return new TimberPlanarPoint(
            correctedKnee.X + (landingLengthModelMm * tX),
            correctedKnee.Y + (landingLengthModelMm * tY));
    }

    /// <summary>
    /// Acute angle (degrees) between knee→landing and the readable tangent.
    /// Straight create landing ≈ 0° (parallel to +T, not the 60° knee vector).
    /// </summary>
    public static bool TryMeasureLandingSegmentAngleToReadableDeg(
        TimberPlanarPoint knee,
        TimberPlanarPoint landingEnd,
        double readableAngleRadians,
        out double angleDeg)
    {
        angleDeg = double.NaN;
        var dx = landingEnd.X - knee.X;
        var dy = landingEnd.Y - knee.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= TimberFramedCombinedG5CreatePlacementRules.SideToleranceMm)
        {
            return false;
        }

        var tX = Math.Cos(readableAngleRadians);
        var tY = Math.Sin(readableAngleRadians);
        angleDeg = TimberItemLeaderLayoutCalculator.MeasureAcuteAngleRadians(
            dx,
            dy,
            tX,
            tY) * 180d / Math.PI;
        return true;
    }

    public static bool LandingSegmentIsStraightAlongReadable(double angleDeg) =>
        Math.Abs(angleDeg) <=
        TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleToleranceDeg;

    /// <summary>
    /// Measure acute angle of the first visible leader segment (v0→v1) to the
    /// source axis. Independent of DTO / request geometry.
    /// </summary>
    public static bool TryMeasureFirstVisibleSegmentAngleDeg(
        double vertex0X,
        double vertex0Y,
        double vertex1X,
        double vertex1Y,
        double startX,
        double startY,
        double endX,
        double endY,
        out double angleDeg) =>
        TimberFramedCombinedG5CreatePlacementRules.TryMeasureFirstSegmentAngleDeg(
            vertex0X,
            vertex0Y,
            vertex1X,
            vertex1Y,
            startX,
            startY,
            endX,
            endY,
            out angleDeg);

    public static bool NeedsCreateFinalization(
        double actualFirstSegmentAngleDeg) =>
        !TimberFramedCombinedG5CreatePlacementRules.FirstSegmentAngleIsSixtyDegrees(
            actualFirstSegmentAngleDeg);

    /// <summary>
    /// CREATE finalization decision from FINAL world vertices.
    /// </summary>
    public static bool TryResolveCreateFinalization(
        TimberPlanarPoint attachment,
        TimberPlanarPoint actualKnee,
        double readableAngleRadians,
        double sideSign,
        double startX,
        double startY,
        double endX,
        double endY,
        out TimberPlanarPoint correctedKnee,
        out double actualAngleDeg,
        out bool changed)
    {
        correctedKnee = actualKnee;
        actualAngleDeg = double.NaN;
        changed = false;

        var dx = actualKnee.X - attachment.X;
        var dy = actualKnee.Y - attachment.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= TimberFramedCombinedG5CreatePlacementRules.SideToleranceMm)
        {
            return false;
        }

        if (!TryMeasureFirstVisibleSegmentAngleDeg(
                attachment.X,
                attachment.Y,
                actualKnee.X,
                actualKnee.Y,
                startX,
                startY,
                endX,
                endY,
                out actualAngleDeg))
        {
            return false;
        }

        if (!NeedsCreateFinalization(actualAngleDeg))
        {
            correctedKnee = actualKnee;
            changed = false;
            return true;
        }

        correctedKnee = BuildCorrectedKnee(
            attachment,
            length,
            readableAngleRadians,
            sideSign);
        changed = true;
        return true;
    }

    /// <summary>
    /// Host-free path when source endpoints are unavailable: use readable T as
    /// the source axis (same contract as horizontal create after TransformBy).
    /// </summary>
    public static bool TryResolveCreateFinalizationFromReadableAxis(
        TimberPlanarPoint attachment,
        TimberPlanarPoint actualKnee,
        double readableAngleRadians,
        double sideSign,
        out TimberPlanarPoint correctedKnee,
        out double actualAngleDeg,
        out bool changed)
    {
        var axisLength = 1000d;
        var startX = attachment.X - (Math.Cos(readableAngleRadians) * axisLength);
        var startY = attachment.Y - (Math.Sin(readableAngleRadians) * axisLength);
        var endX = attachment.X + (Math.Cos(readableAngleRadians) * axisLength);
        var endY = attachment.Y + (Math.Sin(readableAngleRadians) * axisLength);
        return TryResolveCreateFinalization(
            attachment,
            actualKnee,
            readableAngleRadians,
            sideSign,
            startX,
            startY,
            endX,
            endY,
            out correctedKnee,
            out actualAngleDeg,
            out changed);
    }
}
