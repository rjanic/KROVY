using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Shared deterministic rafter layout entry point for automatic generation and
/// supported-source replacement. Simple/asymmetric gables and monopitch use
/// bounded planes; Hip reuses the R1 face-layout + materialization adapter.
/// </summary>
public static class RoofRafterLayoutSolver
{
    public const double CoordinateToleranceMm = 1e-7d;

    public static RoofRafterLayoutResult Solve(
        IRoofGeometry geometry,
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
            return Invalid(RoofRafterLayoutError.InvalidMaximumSpacing);
        }

        var rafterPlanWidth = parameters.RafterPlanWidthMm;
        if (!IsFinite(rafterPlanWidth) || rafterPlanWidth <= 0d)
        {
            return Invalid(RoofRafterLayoutError.InvalidRafterPlanWidth);
        }

        // Hip ordinary rafters share this generation/replacement entry point so
        // live resize and edit reuse the R1 face layout without a host-side solver.
        if (geometry is HipRoofGeometry hip)
        {
            return SolveHip(hip, maximumSpacing, rafterPlanWidth);
        }

        if (!TryCreateNormalizedPlanes(geometry, out var planes) || planes.Count == 0)
        {
            return Invalid(RoofRafterLayoutError.InvalidRoofGeometry);
        }

        var stationSpan = PlanLength(planes[0].RunStartBoundary);
        if (!IsFinite(stationSpan) || stationSpan <= CoordinateToleranceMm)
        {
            return Invalid(RoofRafterLayoutError.InvalidRoofGeometry);
        }
        if (rafterPlanWidth >= stationSpan)
        {
            return Invalid(RoofRafterLayoutError.InvalidRafterPlanWidth);
        }
        if (!TryCreateDirection(planes[0].RunStartBoundary, out var stationDirection) ||
            planes.Any(plane => !IsValidPlane(plane, stationSpan, stationDirection)))
        {
            return Invalid(RoofRafterLayoutError.InvalidRoofGeometry);
        }

        var usableCenterSpan = stationSpan - rafterPlanWidth;
        var rawIntervalCount = Math.Ceiling(usableCenterSpan / maximumSpacing);
        if (!IsFinite(rawIntervalCount) || rawIntervalCount >= int.MaxValue)
        {
            return Invalid(RoofRafterLayoutError.TooManyStations);
        }

        var intervalCount = Math.Max(1, (int)rawIntervalCount);
        var stationCount = intervalCount + 1;
        if (stationCount > int.MaxValue / planes.Count)
        {
            return Invalid(RoofRafterLayoutError.TooManyStations);
        }

        var actualSpacing = usableCenterSpan / intervalCount;
        var rafters = new List<RoofRafterGeometry>(stationCount * planes.Count);
        for (var stationIndex = 0; stationIndex < stationCount; stationIndex++)
        {
            var stationFraction = (double)stationIndex / intervalCount;
            var stationPosition = rafterPlanWidth / 2d + usableCenterSpan * stationFraction;
            var boundaryFraction = stationPosition / stationSpan;
            foreach (var plane in planes)
            {
                if (!TryCreateRafter(
                        plane,
                        stationIndex,
                        stationCount,
                        boundaryFraction,
                        stationPosition,
                        out var rafter))
                {
                    return Invalid(RoofRafterLayoutError.InvalidRoofGeometry);
                }
                rafters.Add(rafter!);
            }
        }

        var signature = string.Join(
            ";",
            "RAFTER_LAYOUT_V1",
            geometry.Signature,
            maximumSpacing.ToString("R", CultureInfo.InvariantCulture),
            rafterPlanWidth.ToString("R", CultureInfo.InvariantCulture),
            intervalCount.ToString(CultureInfo.InvariantCulture));
        return new RoofRafterLayoutResult(
            true,
            new RoofRafterLayout(
                maximumSpacing,
                rafterPlanWidth,
                stationSpan,
                usableCenterSpan,
                intervalCount,
                stationCount,
                actualSpacing,
                stationDirection,
                planes,
                rafters,
                signature,
                RoofRafterDomainPolygon.FromGeometry(geometry)),
            RoofRafterLayoutError.None);
    }

    private static bool TryCreateNormalizedPlanes(
        IRoofGeometry geometry,
        out IReadOnlyList<RoofRafterPlane> planes)
    {
        var raw = geometry switch
        {
            SimpleGableRoofGeometry gable when
                gable.Faces.Count == 2 &&
                gable.Faces[0].Index == 0 &&
                gable.Faces[1].Index == 1 =>
                gable.Faces.Select(face => new RoofRafterPlane(
                    face.Index == 0 ? RafterRoofFace.Face0 : RafterRoofFace.Face1,
                    face.Eave,
                    gable.Ridge,
                    face.SlopeDegrees)).ToArray(),
            MonopitchRoofGeometry monopitch =>
                [new RoofRafterPlane(
                    RafterRoofFace.Face0,
                    monopitch.LowEave,
                    monopitch.HighEave,
                    monopitch.SlopeDegrees)],
            _ => Array.Empty<RoofRafterPlane>(),
        };
        if (raw.Length == 0 || raw.Select(plane => plane.Face).Distinct().Count() != raw.Length)
        {
            planes = Array.Empty<RoofRafterPlane>();
            return false;
        }

        planes = raw.Select(Normalize).ToArray();
        return true;
    }

    private static RoofRafterPlane Normalize(RoofRafterPlane plane)
    {
        var start = Canonicalize(plane.RunStartBoundary);
        var end = Canonicalize(plane.RunEndBoundary);
        if (PlanDot(start, end) < 0d)
        {
            end = Reverse(end);
        }
        return plane with { RunStartBoundary = start, RunEndBoundary = end };
    }

    private static bool IsValidPlane(
        RoofRafterPlane plane,
        double stationSpan,
        RoofDirection2D stationDirection)
    {
        if (!IsFinite(plane.RunStartBoundary) ||
            !IsFinite(plane.RunEndBoundary) ||
            !IsFinite(plane.SlopeDegrees) ||
            plane.SlopeDegrees <= SimpleGableRoofGeometryTolerance.MinimumSlopeDegrees ||
            plane.SlopeDegrees >= SimpleGableRoofGeometryTolerance.MaximumSlopeDegrees ||
            Math.Abs(PlanLength(plane.RunStartBoundary) - stationSpan) >
                SimpleGableRoofGeometryTolerance.LengthTolerance(
                    PlanLength(plane.RunStartBoundary),
                    stationSpan) ||
            Math.Abs(PlanLength(plane.RunEndBoundary) - stationSpan) >
                SimpleGableRoofGeometryTolerance.LengthTolerance(
                    PlanLength(plane.RunEndBoundary),
                    stationSpan) ||
            !TryCreateDirection(plane.RunStartBoundary, out var startDirection) ||
            !TryCreateDirection(plane.RunEndBoundary, out var endDirection) ||
            Dot(startDirection, stationDirection) < 1d -
                SimpleGableRoofGeometryTolerance.AngularTolerance ||
            Dot(endDirection, stationDirection) < 1d -
                SimpleGableRoofGeometryTolerance.AngularTolerance)
        {
            return false;
        }

        var startRun = PlanVector(plane.RunStartBoundary.Start, plane.RunEndBoundary.Start);
        var endRun = PlanVector(plane.RunStartBoundary.End, plane.RunEndBoundary.End);
        var startRunLength = Length(startRun);
        var endRunLength = Length(endRun);
        return startRunLength > CoordinateToleranceMm &&
               endRunLength > CoordinateToleranceMm &&
               Math.Abs(startRunLength - endRunLength) <=
                   SimpleGableRoofGeometryTolerance.LengthTolerance(startRunLength, endRunLength) &&
               Math.Abs(Dot(startRun, new Vector2(stationDirection.X, stationDirection.Y)) /
                   startRunLength) <= SimpleGableRoofGeometryTolerance.AngularTolerance &&
               Math.Abs(Cross(startRun, endRun) / (startRunLength * endRunLength)) <=
                   SimpleGableRoofGeometryTolerance.AngularTolerance &&
               Dot(startRun, endRun) > 0d;
    }

    private static bool TryCreateRafter(
        RoofRafterPlane plane,
        int stationIndex,
        int stationCount,
        double fraction,
        double stationPosition,
        out RoofRafterGeometry? rafter)
    {
        rafter = null;
        var start3D = Interpolate(plane.RunStartBoundary, fraction);
        var end3D = Interpolate(plane.RunEndBoundary, fraction);
        var planStart = new RoofPoint2D(start3D.X, start3D.Y);
        var planEnd = new RoofPoint2D(end3D.X, end3D.Y);
        var planLength = planStart.DistanceTo(planEnd);
        if (!IsFinite(planLength) ||
            planLength <= CoordinateToleranceMm ||
            !RoofDirection2D.TryCreate(
                planEnd.X - planStart.X,
                planEnd.Y - planStart.Y,
                out var runDirection))
        {
            return false;
        }

        var radians = plane.SlopeDegrees * Math.PI / 180d;
        var trueLength = planLength / Math.Cos(radians);
        if (!IsFinite(trueLength) || trueLength <= CoordinateToleranceMm)
        {
            return false;
        }

        rafter = new RoofRafterGeometry(
            plane.Face,
            stationIndex,
            stationCount,
            fraction,
            stationPosition,
            planStart,
            planEnd,
            runDirection,
            planLength,
            trueLength,
            plane.SlopeDegrees);
        return true;
    }

    private static RoofSegment3D Canonicalize(RoofSegment3D segment) =>
        Compare(segment.Start, segment.End) <= 0 ? segment : Reverse(segment);

    private static int Compare(RoofPoint3D first, RoofPoint3D second)
    {
        var x = first.X.CompareTo(second.X);
        return x != 0 ? x : first.Y.CompareTo(second.Y);
    }

    private static RoofSegment3D Reverse(RoofSegment3D segment) =>
        new(segment.End, segment.Start);

    private static double PlanDot(RoofSegment3D first, RoofSegment3D second) =>
        Dot(
            PlanVector(first.Start, first.End),
            PlanVector(second.Start, second.End));

    private static bool TryCreateDirection(
        RoofSegment3D segment,
        out RoofDirection2D direction) =>
        RoofDirection2D.TryCreate(
            segment.End.X - segment.Start.X,
            segment.End.Y - segment.Start.Y,
            out direction);

    private static RoofPoint3D Interpolate(RoofSegment3D segment, double fraction) =>
        new(
            segment.Start.X + (segment.End.X - segment.Start.X) * fraction,
            segment.Start.Y + (segment.End.Y - segment.Start.Y) * fraction,
            segment.Start.Z + (segment.End.Z - segment.Start.Z) * fraction);

    private static double PlanLength(RoofSegment3D segment) =>
        Math.Sqrt(
            Math.Pow(segment.End.X - segment.Start.X, 2d) +
            Math.Pow(segment.End.Y - segment.Start.Y, 2d));

    private static Vector2 PlanVector(RoofPoint3D start, RoofPoint3D end) =>
        new(end.X - start.X, end.Y - start.Y);

    private static double Length(Vector2 vector) =>
        Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private static double Dot(RoofDirection2D first, RoofDirection2D second) =>
        first.X * second.X + first.Y * second.Y;

    private static double Dot(Vector2 first, Vector2 second) =>
        first.X * second.X + first.Y * second.Y;

    private static double Cross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;

    private static bool IsFinite(RoofSegment3D segment) =>
        IsFinite(segment.Start) && IsFinite(segment.End);

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofRafterLayoutResult SolveHip(
        HipRoofGeometry geometry,
        double maximumSpacingMm,
        double rafterPlanWidthMm)
    {
        var faceResult = RoofFaceRafterLayoutService.Create(
            geometry.Topology,
            maximumSpacingMm);
        if (!faceResult.IsValid || faceResult.Layout is null)
        {
            return faceResult.Error switch
            {
                RoofFaceRafterLayoutError.InvalidSpacing =>
                    Invalid(RoofRafterLayoutError.InvalidMaximumSpacing),
                RoofFaceRafterLayoutError.TooManyStations =>
                    Invalid(RoofRafterLayoutError.TooManyStations),
                _ => Invalid(RoofRafterLayoutError.InvalidRoofGeometry),
            };
        }

        if (!RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
                geometry,
                faceResult.Layout,
                rafterPlanWidthMm,
                out var layout) ||
            !RoofRafterMaterializationRules.IsConsistent(geometry, layout))
        {
            return Invalid(RoofRafterLayoutError.InvalidRoofGeometry);
        }

        return new RoofRafterLayoutResult(true, layout, RoofRafterLayoutError.None);
    }

    private static RoofRafterLayoutResult Invalid(RoofRafterLayoutError error) =>
        new(false, null, error);

    private readonly record struct Vector2(double X, double Y);
}
