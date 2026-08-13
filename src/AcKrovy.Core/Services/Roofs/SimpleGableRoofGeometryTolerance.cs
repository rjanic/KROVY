namespace AcKrovy.Core.Services.Roofs;

/// <summary>Central numerical policy for the S2 rectangular gable solver.</summary>
public static class SimpleGableRoofGeometryTolerance
{
    public const double CoordinateToleranceMm = 0.000001d;
    public const double RelativeLengthTolerance = 0.0000000001d;
    public const double AngularTolerance = 0.0000000001d;
    public const double MinimumDimensionMm = 0.01d;
    public const double MinimumSlopeDegrees = 0d;
    public const double MaximumSlopeDegrees = 90d;

    internal static double LengthTolerance(double first, double second) =>
        Math.Max(
            CoordinateToleranceMm,
            Math.Max(Math.Abs(first), Math.Abs(second)) * RelativeLengthTolerance);
}
