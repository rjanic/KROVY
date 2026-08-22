using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Creates deterministic equalized rafter stations from solved simple-gable geometry.</summary>
public static class SimpleGableRafterLayoutSolver
{
    public const double CoordinateToleranceMm = 1e-7d;

    public static SimpleGableRafterLayoutResult Solve(
        SimpleGableRoofGeometry geometry,
        RafterLayoutParameters parameters)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        var maximumSpacing = parameters.MaximumSpacingMm;
        if (!IsFinite(maximumSpacing) || maximumSpacing <= 0d)
        {
            return Invalid(SimpleGableRafterLayoutError.InvalidMaximumSpacing);
        }

        var rafterPlanWidth = parameters.RafterPlanWidthMm;
        if (!IsFinite(rafterPlanWidth) || rafterPlanWidth <= 0d)
        {
            return Invalid(SimpleGableRafterLayoutError.InvalidRafterPlanWidth);
        }

        if (!TryGetAlignedSegments(
                geometry,
                out var ridge,
                out var firstEave,
                out var secondEave) ||
            !geometry.Faces.All(face => IsFinite(face.SlopeDegrees)) ||
            !IsFinite(geometry.RidgeLengthMm) ||
            geometry.RidgeLengthMm <= CoordinateToleranceMm)
        {
            return Invalid(SimpleGableRafterLayoutError.InvalidRoofGeometry);
        }

        if (rafterPlanWidth >= geometry.RidgeLengthMm)
        {
            return Invalid(SimpleGableRafterLayoutError.InvalidRafterPlanWidth);
        }

        var usableCenterSpan = geometry.RidgeLengthMm - rafterPlanWidth;
        var rawIntervalCount = Math.Ceiling(usableCenterSpan / maximumSpacing);
        if (!IsFinite(rawIntervalCount) || rawIntervalCount >= int.MaxValue)
        {
            return Invalid(SimpleGableRafterLayoutError.TooManyStations);
        }

        var intervalCount = Math.Max(1, (int)rawIntervalCount);
        var stationCount = intervalCount + 1;
        var actualSpacing = usableCenterSpan / intervalCount;
        var rafters = new List<SimpleGableRafter>(checked(stationCount * 2));
        for (var stationIndex = 0; stationIndex < stationCount; stationIndex++)
        {
            var normalizedStation = (double)stationIndex / intervalCount;
            var centerDistance = rafterPlanWidth / 2d + usableCenterSpan * normalizedStation;
            var fraction = centerDistance / geometry.RidgeLengthMm;
            var ridgePoint = Interpolate(ridge, fraction);
            var firstEavePoint = Interpolate(firstEave, fraction);
            var secondEavePoint = Interpolate(secondEave, fraction);
            rafters.Add(CreateRafter(
                RafterRoofFace.Face0,
                stationIndex,
                stationCount,
                fraction,
                firstEavePoint,
                ridgePoint,
                geometry.Faces[0].SlopeDegrees));
            rafters.Add(CreateRafter(
                RafterRoofFace.Face1,
                stationIndex,
                stationCount,
                fraction,
                secondEavePoint,
                ridgePoint,
                geometry.Faces[1].SlopeDegrees));
        }

        var signature = string.Join(
            ";",
            "RAFTER_LAYOUT_V1",
            geometry.Signature,
            maximumSpacing.ToString("R", CultureInfo.InvariantCulture),
            rafterPlanWidth.ToString("R", CultureInfo.InvariantCulture),
            intervalCount.ToString(CultureInfo.InvariantCulture));
        return new SimpleGableRafterLayoutResult(
            true,
            new SimpleGableRafterLayout(
                maximumSpacing,
                rafterPlanWidth,
                geometry.RidgeLengthMm,
                usableCenterSpan,
                intervalCount,
                stationCount,
                actualSpacing,
                rafters,
                signature),
            SimpleGableRafterLayoutError.None);
    }

    private static SimpleGableRafter CreateRafter(
        RafterRoofFace face,
        int stationIndex,
        int stationCount,
        double fraction,
        RoofPoint3D eave,
        RoofPoint3D ridge,
        double slopeDegrees) =>
        new(
            face,
            stationIndex,
            stationCount,
            fraction,
            new RoofPoint2D(eave.X, eave.Y),
            new RoofPoint2D(ridge.X, ridge.Y),
            slopeDegrees);

    private static bool TryGetAlignedSegments(
        SimpleGableRoofGeometry geometry,
        out RoofSegment3D ridge,
        out RoofSegment3D firstEave,
        out RoofSegment3D secondEave)
    {
        ridge = geometry.Ridge;
        firstEave = default!;
        secondEave = default!;
        if (geometry.Faces.Count != 2 ||
            geometry.Faces[0].Index != 0 ||
            geometry.Faces[1].Index != 1 ||
            !IsFinite(ridge))
        {
            return false;
        }

        var ridgeX = ridge.End.X - ridge.Start.X;
        var ridgeY = ridge.End.Y - ridge.Start.Y;
        firstEave = Align(geometry.Faces[0].Eave, ridgeX, ridgeY);
        secondEave = Align(geometry.Faces[1].Eave, ridgeX, ridgeY);
        return IsFinite(firstEave) && IsFinite(secondEave);
    }

    private static RoofSegment3D Align(RoofSegment3D segment, double ridgeX, double ridgeY)
    {
        var segmentX = segment.End.X - segment.Start.X;
        var segmentY = segment.End.Y - segment.Start.Y;
        return segmentX * ridgeX + segmentY * ridgeY >= 0d
            ? segment
            : new RoofSegment3D(segment.End, segment.Start);
    }

    private static RoofPoint3D Interpolate(RoofSegment3D segment, double fraction)
    {
        return new RoofPoint3D(
            segment.Start.X + (segment.End.X - segment.Start.X) * fraction,
            segment.Start.Y + (segment.End.Y - segment.Start.Y) * fraction,
            segment.Start.Z + (segment.End.Z - segment.Start.Z) * fraction);
    }

    private static bool IsFinite(RoofSegment3D segment) =>
        IsFinite(segment.Start) && IsFinite(segment.End);

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static SimpleGableRafterLayoutResult Invalid(
        SimpleGableRafterLayoutError error) =>
        new(false, null, error);
}
