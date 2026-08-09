namespace AcKrovy.Core.Services;

/// <summary>
/// Standalone Plain / DimensionsLeader / framed ItemOnly orientation only.
/// Must not be used by R3 Combined BlockContent production.
/// <para>
/// Contract (physical Start→End domain, full 0–360°):
/// 1) wrap physical axis;
/// 2) fold to canonical readable half-plane [−π/2, π/2];
/// 3) default semantic RIGHT is authored in canonical WorldXY before TransformBy;
/// 4) exact physical ±90° (and 270°≡−90°) receive exactly one π half-turn;
/// 5) exact ±180° fold to 0° with no half-turn (never compare already-readable
///    angles when deciding the half-turn);
/// 6) never stack a second readability fold or a second half-turn.
/// </para>
/// </summary>
public static class TimberStandaloneNativeLeaderOrientationRules
{
    public const double AngleToleranceRadians =
        TimberItemLeaderLayoutCalculator.AngleToleranceRadians;

    /// <summary>
    /// Whole-annotation <c>TransformBy</c> angle from physical Start→End axis.
    /// Input must be physical, never an already readability-normalized angle.
    /// </summary>
    public static double ResolveTransformRadians(double physicalSourceAxisRadians)
    {
        if (double.IsNaN(physicalSourceAxisRadians) ||
            double.IsInfinity(physicalSourceAxisRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(physicalSourceAxisRadians));
        }

        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            physicalSourceAxisRadians);

        // Explicit ±180° boundary: fold once to 0°, never half-turn.
        if (IsExactOneEighty(physical))
        {
            return 0d;
        }

        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                physical);

        // Explicit ±90° / 270° boundary: half-turn once on PHYSICAL, after fold.
        if (IsExactVertical(physical))
        {
            return TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                readable + Math.PI);
        }

        return TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(readable);
    }

    public static bool IsExactVertical(double physicalSourceAxisRadians)
    {
        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            physicalSourceAxisRadians);
        return Math.Abs(Math.Abs(physical) - (Math.PI / 2d)) <= AngleToleranceRadians;
    }

    public static bool IsExactOneEighty(double physicalSourceAxisRadians)
    {
        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            physicalSourceAxisRadians);
        return Math.Abs(Math.Abs(physical) - Math.PI) <= AngleToleranceRadians;
    }
}
