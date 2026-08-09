using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Standalone Plain ItemOnly / DimensionsLeader CREATE finalization only.
/// Mirrors Combined's post-host 60° / landing‖T semantics without sharing the
/// BlockContent host path. Must not be used by R3 Combined or framed ItemOnly.
/// </summary>
public static class TimberStandaloneNativeLeaderCreateFinalizationRules
{
    public const double AngleToleranceRadians =
        TimberItemLeaderLayoutCalculator.AngleToleranceRadians;

    public const double AngleToleranceDeg = AngleToleranceRadians * 180d / Math.PI;

    /// <summary>
    /// World knee at exactly 60° from transform axis T (readable Start→End fold),
    /// preserving segment length and standalone side sign.
    /// Right: A = +T·cos60 + +N·sin60; Left: A = −T·cos60 + +N·sin60.
    /// </summary>
    public static TimberPlanarPoint BuildCorrectedKnee(
        TimberPlanarPoint attachment,
        double segmentLengthModelMm,
        double transformRadians,
        TimberLeaderHorizontalSide side)
    {
        if (segmentLengthModelMm <= AngleToleranceRadians ||
            double.IsNaN(segmentLengthModelMm) ||
            double.IsInfinity(segmentLengthModelMm))
        {
            throw new ArgumentOutOfRangeException(nameof(segmentLengthModelMm));
        }

        if (double.IsNaN(transformRadians) || double.IsInfinity(transformRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(transformRadians));
        }

        var angle = TimberItemLeaderLayoutCalculator.FirstSegmentAngleRadians;
        var tX = Math.Cos(transformRadians);
        var tY = Math.Sin(transformRadians);
        var nX = -tY;
        var nY = tX;
        var alongT = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        return new TimberPlanarPoint(
            attachment.X +
            (alongT * segmentLengthModelMm * Math.Cos(angle) * tX) +
            (segmentLengthModelMm * Math.Sin(angle) * nX),
            attachment.Y +
            (alongT * segmentLengthModelMm * Math.Cos(angle) * tY) +
            (segmentLengthModelMm * Math.Sin(angle) * nY));
    }

    /// <summary>
    /// Landing end from final knee along ±T (Right=+T, Left=−T). Never inherits
    /// the 60° first-segment tilt — interior elbow is then exactly 120°.
    /// </summary>
    public static TimberPlanarPoint BuildCorrectedLandingEnd(
        TimberPlanarPoint correctedKnee,
        double transformRadians,
        TimberLeaderHorizontalSide side,
        double landingLengthModelMm)
    {
        if (landingLengthModelMm <= AngleToleranceRadians ||
            double.IsNaN(landingLengthModelMm) ||
            double.IsInfinity(landingLengthModelMm))
        {
            throw new ArgumentOutOfRangeException(nameof(landingLengthModelMm));
        }

        if (double.IsNaN(transformRadians) || double.IsInfinity(transformRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(transformRadians));
        }

        var alongT = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        var tX = Math.Cos(transformRadians) * alongT;
        var tY = Math.Sin(transformRadians) * alongT;
        return new TimberPlanarPoint(
            correctedKnee.X + (landingLengthModelMm * tX),
            correctedKnee.Y + (landingLengthModelMm * tY));
    }

    /// <summary>
    /// CREATE/rebuild decision from live attachment/knee: correct first segment
    /// to 60° vs transform T when host recomputed it, and always supply landing
    /// along ±T from the final knee.
    /// </summary>
    public static bool TryResolveCreateFinalization(
        TimberPlanarPoint attachment,
        TimberPlanarPoint actualKnee,
        double transformRadians,
        TimberLeaderHorizontalSide side,
        double landingLengthModelMm,
        out TimberPlanarPoint correctedKnee,
        out TimberPlanarPoint landingEnd,
        out bool kneeChanged)
    {
        correctedKnee = actualKnee;
        landingEnd = actualKnee;
        kneeChanged = false;

        var dx = actualKnee.X - attachment.X;
        var dy = actualKnee.Y - attachment.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= AngleToleranceRadians ||
            double.IsNaN(length) ||
            double.IsInfinity(length))
        {
            return false;
        }

        if (landingLengthModelMm <= AngleToleranceRadians ||
            double.IsNaN(landingLengthModelMm) ||
            double.IsInfinity(landingLengthModelMm))
        {
            return false;
        }

        var tX = Math.Cos(transformRadians);
        var tY = Math.Sin(transformRadians);
        var actualAngleDeg =
            TimberItemLeaderLayoutCalculator.MeasureAcuteAngleRadians(
                dx,
                dy,
                tX,
                tY) * 180d / Math.PI;
        var needsKneeCorrection =
            Math.Abs(actualAngleDeg - 60d) > AngleToleranceDeg;

        correctedKnee = needsKneeCorrection
            ? BuildCorrectedKnee(attachment, length, transformRadians, side)
            : actualKnee;
        kneeChanged = needsKneeCorrection;
        landingEnd = BuildCorrectedLandingEnd(
            correctedKnee,
            transformRadians,
            side,
            landingLengthModelMm);
        return true;
    }

    /// <summary>
    /// Vector contract used by tests and host mapping audits:
    /// unsigned angle(T,A)=60°, B∥T, angle(Incoming,Outgoing)=120°.
    /// </summary>
    public static bool MeetsSixtyOneTwentyContract(
        double attachmentX,
        double attachmentY,
        double kneeX,
        double kneeY,
        double landingEndX,
        double landingEndY,
        double transformRadians)
    {
        var aX = kneeX - attachmentX;
        var aY = kneeY - attachmentY;
        var aLen = Math.Sqrt((aX * aX) + (aY * aY));
        if (aLen <= AngleToleranceRadians)
        {
            return false;
        }

        var tX = Math.Cos(transformRadians);
        var tY = Math.Sin(transformRadians);
        var angleTA = Math.Acos(ClampUnit(
            ((aX * tX) + (aY * tY)) / aLen)) * 180d / Math.PI;
        if (Math.Abs(angleTA - 60d) > AngleToleranceDeg)
        {
            return false;
        }

        var bX = landingEndX - kneeX;
        var bY = landingEndY - kneeY;
        var bLen = Math.Sqrt((bX * bX) + (bY * bY));
        if (bLen <= AngleToleranceRadians)
        {
            return false;
        }

        var cross = Math.Abs(((bX / bLen) * tY) - ((bY / bLen) * tX));
        if (cross > AngleToleranceRadians)
        {
            return false;
        }

        // Incoming = Source−Knee = −A; Outgoing = LandingEnd−Knee = B.
        var incomingX = attachmentX - kneeX;
        var incomingY = attachmentY - kneeY;
        var inLen = Math.Sqrt((incomingX * incomingX) + (incomingY * incomingY));
        if (inLen <= AngleToleranceRadians)
        {
            return false;
        }

        var elbow = Math.Acos(ClampUnit(
            ((incomingX * bX) + (incomingY * bY)) / (inLen * bLen))) * 180d / Math.PI;
        return Math.Abs(elbow - 120d) <= AngleToleranceDeg;
    }

    private static double ClampUnit(double value) =>
        Math.Min(1d, Math.Max(-1d, value));
}
