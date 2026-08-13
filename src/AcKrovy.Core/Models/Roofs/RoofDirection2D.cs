namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Normalized direction in the drawing XY plane. X points along world +X and Y
/// along world +Y; positive angles therefore run counter-clockwise from +X.
/// </summary>
public readonly record struct RoofDirection2D
{
    private const double MinimumVectorLength = 0.000000001d;

    private RoofDirection2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }

    public double AngleRadians => Math.Atan2(Y, X);

    public static bool TryCreate(double x, double y, out RoofDirection2D direction)
    {
        direction = default;
        if (!IsFinite(x) || !IsFinite(y))
        {
            return false;
        }

        var length = Math.Sqrt(x * x + y * y);
        if (length < MinimumVectorLength)
        {
            return false;
        }

        direction = new RoofDirection2D(x / length, y / length);
        return true;
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
