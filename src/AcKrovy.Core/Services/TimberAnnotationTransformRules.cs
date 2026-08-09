using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral math for future manual annotation presentation transforms
/// (Edit kóty). Independent of CREATE readability folds, R3 vertical
/// correction, and host geometry APIs.
/// <para>
/// World-angle normalization interval is (−π, π] via Atan2(sin, cos).
/// Positive angles are counterclockwise about WCS +Z.
/// </para>
/// </summary>
public static class TimberAnnotationTransformRules
{
    public const double AngleToleranceRadians = 1e-9d;

    /// <summary>
    /// Normalize a WCS world angle into (−π, π].
    /// Deterministic for 0°, ±90°, 180°, 270°/ −90°, 360° and near-boundary
    /// values. This is intentionally separate from annotation readability folds.
    /// </summary>
    public static double NormalizeWorldAngleRadians(double angleRadians)
    {
        ValidateFinite(angleRadians, nameof(angleRadians));
        var wrapped = Math.Atan2(Math.Sin(angleRadians), Math.Cos(angleRadians));
        // Atan2 may return −π for half-turn inputs when sin residual is slightly
        // negative. Snap that endpoint to +π so the public interval is (−π, π].
        if (wrapped <= -Math.PI + AngleToleranceRadians)
        {
            return Math.PI;
        }

        return wrapped;
    }

    /// <summary>
    /// Shortest signed delta into (−π, π] from <paramref name="fromRadians"/>
    /// to <paramref name="toRadians"/>.
    /// </summary>
    public static double NormalizeAngleDeltaRadians(
        double fromRadians,
        double toRadians)
    {
        var from = NormalizeWorldAngleRadians(fromRadians);
        var to = NormalizeWorldAngleRadians(toRadians);
        return NormalizeWorldAngleRadians(to - from);
    }

    public static bool AreWorldAnglesEqual(
        double leftRadians,
        double rightRadians,
        double toleranceRadians = AngleToleranceRadians)
    {
        if (toleranceRadians < 0d ||
            double.IsNaN(toleranceRadians) ||
            double.IsInfinity(toleranceRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceRadians));
        }

        return Math.Abs(
                NormalizeAngleDeltaRadians(leftRadians, rightRadians)) <=
            toleranceRadians;
    }

    /// <summary>
    /// Resolve the target content world angle and rotation delta for one
    /// manual transform. Does not mutate timber geometry or metadata.
    /// </summary>
    public static TimberAnnotationTransformDecision Resolve(
        TimberAnnotationTransformRequest request,
        double currentContentWorldAngleRadians,
        double sourceAxisWorldAngleRadians = 0d)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var current = NormalizeWorldAngleRadians(currentContentWorldAngleRadians);
        var sourceAxis = NormalizeWorldAngleRadians(sourceAxisWorldAngleRadians);

        return request.Kind switch
        {
            TimberAnnotationTransformKind.RotateRelative =>
                ResolveRotateRelative(request, current),
            TimberAnnotationTransformKind.SetWorldOrientation =>
                ResolveSetWorldOrientation(request, current),
            TimberAnnotationTransformKind.MirrorAcrossSourceAxis =>
                ResolveMirror(current, sourceAxis),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Kind,
                null),
        };
    }

    /// <summary>
    /// Mirror content world angle <paramref name="contentWorldAngleRadians"/>
    /// across source-axis angle <paramref name="sourceAxisWorldAngleRadians"/>:
    /// <c>θ' = normalize(2α − θ)</c>.
    /// </summary>
    public static double MirrorContentWorldAngleRadians(
        double contentWorldAngleRadians,
        double sourceAxisWorldAngleRadians)
    {
        var theta = NormalizeWorldAngleRadians(contentWorldAngleRadians);
        var alpha = NormalizeWorldAngleRadians(sourceAxisWorldAngleRadians);
        return NormalizeWorldAngleRadians((2d * alpha) - theta);
    }

    /// <summary>
    /// Reflect a planar point across the infinite axis through
    /// <paramref name="axisOrigin"/> at <paramref name="axisAngleRadians"/>.
    /// A point on the axis is unchanged within floating-point tolerance.
    /// </summary>
    public static TimberPlanarPoint ReflectPointAcrossAxis(
        TimberPlanarPoint point,
        TimberPlanarPoint axisOrigin,
        double axisAngleRadians)
    {
        var axis = TimberPlanarVector.FromAngleRadians(
            NormalizeWorldAngleRadians(axisAngleRadians));
        var relativeX = point.X - axisOrigin.X;
        var relativeY = point.Y - axisOrigin.Y;
        var projection = (relativeX * axis.X) + (relativeY * axis.Y);
        var projectedX = axisOrigin.X + (projection * axis.X);
        var projectedY = axisOrigin.Y + (projection * axis.Y);
        return new TimberPlanarPoint(
            (2d * projectedX) - point.X,
            (2d * projectedY) - point.Y);
    }

    private static TimberAnnotationTransformDecision ResolveRotateRelative(
        TimberAnnotationTransformRequest request,
        double currentNormalized)
    {
        var delta = request.AngleRadians ??
            throw new InvalidOperationException(
                "RotateRelative requires AngleRadians.");
        ValidateFinite(delta, nameof(request.AngleRadians));
        var target = NormalizeWorldAngleRadians(currentNormalized + delta);
        var appliedDelta = NormalizeWorldAngleRadians(delta);
        return new TimberAnnotationTransformDecision(
            request,
            currentNormalized,
            target,
            appliedDelta);
    }

    private static TimberAnnotationTransformDecision ResolveSetWorldOrientation(
        TimberAnnotationTransformRequest request,
        double currentNormalized)
    {
        var absolute = request.AngleRadians ??
            throw new InvalidOperationException(
                "SetWorldOrientation requires AngleRadians.");
        var target = NormalizeWorldAngleRadians(absolute);
        var appliedDelta = NormalizeAngleDeltaRadians(currentNormalized, target);
        return new TimberAnnotationTransformDecision(
            request,
            currentNormalized,
            target,
            appliedDelta);
    }

    private static TimberAnnotationTransformDecision ResolveMirror(
        double currentNormalized,
        double sourceAxisNormalized)
    {
        var target = MirrorContentWorldAngleRadians(
            currentNormalized,
            sourceAxisNormalized);
        var appliedDelta = NormalizeAngleDeltaRadians(currentNormalized, target);
        return new TimberAnnotationTransformDecision(
            TimberAnnotationTransformRequest.MirrorAcrossSourceAxis(),
            currentNormalized,
            target,
            appliedDelta);
    }

    private static void ValidateFinite(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, null);
        }
    }
}

/// <summary>
/// Resolved CAD-neutral transform: normalized current/target world angles and
/// the signed rotation delta a host executor should apply about the attachment.
/// </summary>
public sealed record TimberAnnotationTransformDecision(
    TimberAnnotationTransformRequest Request,
    double CurrentContentWorldAngleRadians,
    double TargetContentWorldAngleRadians,
    double RotationDeltaRadians);
