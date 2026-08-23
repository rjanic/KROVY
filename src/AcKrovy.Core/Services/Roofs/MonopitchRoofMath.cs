namespace AcKrovy.Core.Services.Roofs;

/// <summary>Unit-safe monopitch slope/height relationships using millimetres and degrees.</summary>
public static class MonopitchRoofMath
{
    public static bool TryCalculateHeightDifferenceMm(
        double spanMm,
        double slopeDegrees,
        out double heightDifferenceMm)
    {
        heightDifferenceMm = double.NaN;
        if (!IsValidSpan(spanMm) || !IsValidSlope(slopeDegrees))
        {
            return false;
        }

        var value = spanMm * Math.Tan(slopeDegrees * Math.PI / 180d);
        if (!IsFinite(value) || value <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
        {
            return false;
        }

        heightDifferenceMm = value;
        return true;
    }

    public static bool TryCalculateSlopeDegrees(
        double spanMm,
        double heightDifferenceMm,
        out double slopeDegrees)
    {
        slopeDegrees = double.NaN;
        if (!IsValidSpan(spanMm) || !IsFinite(heightDifferenceMm) ||
            heightDifferenceMm <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
        {
            return false;
        }

        var value = Math.Atan(heightDifferenceMm / spanMm) * 180d / Math.PI;
        if (!IsValidSlope(value))
        {
            return false;
        }

        slopeDegrees = value;
        return true;
    }

    internal static bool IsValidSlope(double value) =>
        IsFinite(value) &&
        value > SimpleGableRoofGeometryTolerance.MinimumSlopeDegrees &&
        value < SimpleGableRoofGeometryTolerance.MaximumSlopeDegrees;

    private static bool IsValidSpan(double value) =>
        IsFinite(value) && value > SimpleGableRoofGeometryTolerance.MinimumDimensionMm;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
