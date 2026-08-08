namespace AcKrovy.Core.Services;

/// <summary>
/// Shared readable-angle normalization used by FullLabel and G5 BlockContent
/// layouts. Canonical readable half-plane is [−π/2, π/2] i.e. [−90°, +90°].
/// Angles outside that range are folded by exactly ±π (180°) until they land
/// inside the half-plane. Both vertical boundaries stay: +90° reads from the
/// right; −90° (270°) reads from the left — host WHITE DOBRÉ contract. Do not
/// fold −90°→+90°.
/// </summary>
public static class TimberAnnotationReadabilityRules
{
    /// <summary>
    /// Alias for <see cref="NormalizeReadableRotationRadians"/> — same [−90°, +90°]
    /// contract used by R3 Combined BlockContent content orientation.
    /// </summary>
    public static double NormalizeReadableAngle(double angleRadians) =>
        NormalizeReadableRotationRadians(angleRadians);

    public static double NormalizeReadableRotationRadians(double rotationRadians)
    {
        if (double.IsNaN(rotationRadians) || double.IsInfinity(rotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        }

        // Physical orientation is modulo 360° — wrap before folding so absolute
        // inputs such as 315° / 360° (not only Atan2 (−π, π]) reach [−π/2, π/2].
        var rotation = WrapPhysicalAngleRadians(rotationRadians);
        const double halfPi = Math.PI / 2d;
        // FP: Atan2(sin(3π/2), cos(3π/2)) is slightly < −π/2 (−90.000…°). Treat
        // near-verticals as exact so 270° stays −90° (WHITE DOBRÉ), not +90°.
        const double verticalEps = 1e-12d;
        if (Math.Abs(rotation - halfPi) <= verticalEps)
        {
            return halfPi;
        }

        if (Math.Abs(rotation + halfPi) <= verticalEps)
        {
            return -halfPi;
        }

        while (rotation > halfPi)
        {
            rotation -= Math.PI;
        }

        while (rotation < -halfPi)
        {
            rotation += Math.PI;
        }

        return rotation;
    }

    public static double NormalizeReadableAngleDegrees(double angleDegrees)
    {
        if (double.IsNaN(angleDegrees) || double.IsInfinity(angleDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        }

        return NormalizeReadableRotationRadians(angleDegrees * Math.PI / 180d) *
               180d / Math.PI;
    }

    /// <summary>
    /// Wrap to (−π, π] via Atan2 — identical physical direction for Start→End
    /// and End→Start differs by π before readability fold.
    /// </summary>
    public static double WrapPhysicalAngleRadians(double radians)
    {
        if (double.IsNaN(radians) || double.IsInfinity(radians))
        {
            throw new ArgumentOutOfRangeException(nameof(radians));
        }

        return Math.Atan2(Math.Sin(radians), Math.Cos(radians));
    }

    public static bool IsReadabilityFlipped(double elementAxisRadians)
    {
        var physical = WrapPhysicalAngleRadians(elementAxisRadians);
        var readable = NormalizeReadableRotationRadians(elementAxisRadians);
        return Math.Abs(NormalizeAngleDelta(readable - physical)) > 1e-9;
    }

    public static double NormalizeAngleDelta(double radians)
    {
        if (double.IsNaN(radians) || double.IsInfinity(radians))
        {
            throw new ArgumentOutOfRangeException(nameof(radians));
        }

        var value = radians;
        while (value > Math.PI)
        {
            value -= 2d * Math.PI;
        }

        while (value <= -Math.PI)
        {
            value += 2d * Math.PI;
        }

        return value;
    }
}
