namespace AcKrovy.Core.Services;

/// <summary>
/// Shared readable-angle normalization used by FullLabel and G5 BlockContent
/// layouts. Keeps text upright by folding angles outside (−π/2, π/2].
/// </summary>
public static class TimberAnnotationReadabilityRules
{
    public static double NormalizeReadableRotationRadians(double rotationRadians)
    {
        if (double.IsNaN(rotationRadians) || double.IsInfinity(rotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        }

        var rotation = rotationRadians;
        if (rotation > Math.PI / 2d)
        {
            rotation -= Math.PI;
        }
        else if (rotation <= -Math.PI / 2d)
        {
            rotation += Math.PI;
        }

        return rotation;
    }

    public static bool IsReadabilityFlipped(double elementAxisRadians) =>
        Math.Abs(
            NormalizeAngleDelta(
                NormalizeReadableRotationRadians(elementAxisRadians) -
                elementAxisRadians)) >
        1e-9;

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
