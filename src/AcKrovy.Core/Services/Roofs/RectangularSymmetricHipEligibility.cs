using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Stage-1 eligibility for authoritative rectangular/square physical 3D hip roofs.</summary>
public enum RectangularSymmetricHipEligibilityKind
{
    Ineligible = 0,
    ElongatedRectangle = 1,
    SquareOrCollapsedNearSquare = 2,
}

public enum RectangularSymmetricHipEligibilityError
{
    None = 0,
    NullGeometry,
    FootprintNotRectangular,
    TopologyFaceCountMismatch,
    TopologyHipCountMismatch,
    NonPlanarOrNonFinite,
    UnexpectedRidgeForSquare,
    MissingRidgeForElongated,
    NumericallyUnresolved,
}

public sealed record RectangularSymmetricHipEligibilityResult(
    bool IsEligible,
    RectangularSymmetricHipEligibilityKind Kind,
    RectangularSymmetricHipEligibilityError Error,
    RectangularRoofFootprintDescription? Rectangle);

/// <summary>
/// Classifies whether a solved Hip roof may materialize physical 3D faces.
/// Unsupported topologies are rejected explicitly — never approximated.
/// </summary>
public static class RectangularSymmetricHipEligibility
{
    public static RectangularSymmetricHipEligibilityResult Evaluate(
        RoofFootprint footprint,
        HipRoofGeometry geometry)
    {
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }

        if (geometry is null)
        {
            return new RectangularSymmetricHipEligibilityResult(
                false,
                RectangularSymmetricHipEligibilityKind.Ineligible,
                RectangularSymmetricHipEligibilityError.NullGeometry,
                null);
        }

        if (!RectangularRoofFootprintRules.TryDescribe(footprint, out var rectangle) ||
            rectangle is null)
        {
            return Fail(RectangularSymmetricHipEligibilityError.FootprintNotRectangular);
        }

        var topology = geometry.Topology;
        if (topology.Faces.Count != 4 || geometry.Faces.Count != 4)
        {
            return Fail(
                RectangularSymmetricHipEligibilityError.TopologyFaceCountMismatch,
                rectangle);
        }

        if (geometry.Hips.Count != 4)
        {
            return Fail(
                RectangularSymmetricHipEligibilityError.TopologyHipCountMismatch,
                rectangle);
        }

        if (!AreFacesPlanarAndFinite(geometry))
        {
            return Fail(
                RectangularSymmetricHipEligibilityError.NonPlanarOrNonFinite,
                rectangle);
        }

        if (geometry.IsPyramidal)
        {
            if (geometry.Ridge is not null || geometry.RidgeLengthMm > 0d ||
                geometry.Ridges.Count != 0 ||
                !geometry.Apex.HasValue)
            {
                return Fail(
                    RectangularSymmetricHipEligibilityError.UnexpectedRidgeForSquare,
                    rectangle);
            }

            if (geometry.Faces.Any(face => face.BoundaryPoints.Count != 3))
            {
                return Fail(
                    RectangularSymmetricHipEligibilityError.NumericallyUnresolved,
                    rectangle);
            }

            return new RectangularSymmetricHipEligibilityResult(
                true,
                RectangularSymmetricHipEligibilityKind.SquareOrCollapsedNearSquare,
                RectangularSymmetricHipEligibilityError.None,
                rectangle);
        }

        if (geometry.Ridge is null ||
            geometry.Ridges.Count != 1 ||
            geometry.RidgeLengthMm <= 0d)
        {
            return Fail(
                RectangularSymmetricHipEligibilityError.MissingRidgeForElongated,
                rectangle);
        }

        var triangular = geometry.Faces.Count(face => face.BoundaryPoints.Count == 3);
        var trapezoidal = geometry.Faces.Count(face => face.BoundaryPoints.Count == 4);
        if (triangular != 2 || trapezoidal != 2)
        {
            return Fail(
                RectangularSymmetricHipEligibilityError.NumericallyUnresolved,
                rectangle);
        }

        return new RectangularSymmetricHipEligibilityResult(
            true,
            RectangularSymmetricHipEligibilityKind.ElongatedRectangle,
            RectangularSymmetricHipEligibilityError.None,
            rectangle);
    }

    private static bool AreFacesPlanarAndFinite(HipRoofGeometry geometry)
    {
        foreach (var face in geometry.Faces)
        {
            var points = face.BoundaryPoints;
            if (points.Count < 3 || points.Any(point => !IsFinite(point)))
            {
                return false;
            }

            if (!IsPlanar(points))
            {
                return false;
            }
        }

        return geometry.Topology.Nodes.All(IsFinite);
    }

    private static bool IsPlanar(IReadOnlyList<RoofPoint3D> points)
    {
        var origin = points[0];
        var u = Subtract(points[1], origin);
        RoofPoint3D? normal = null;
        for (var index = 2; index < points.Count; index++)
        {
            var v = Subtract(points[index], origin);
            var candidate = Cross(u, v);
            var magnitude = Length(candidate);
            if (magnitude <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
            {
                continue;
            }

            candidate = Scale(candidate, 1d / magnitude);
            if (normal is null)
            {
                normal = candidate;
                continue;
            }

            var alignment = Math.Abs(Dot(normal.Value, candidate));
            if (Math.Abs(alignment - 1d) > SimpleGableRoofGeometryTolerance.AngularTolerance * 1e6)
            {
                // Allow a slightly looser planarity check for floating topology vertices.
                if (alignment < 1d - 1e-8)
                {
                    return false;
                }
            }
        }

        if (normal is null)
        {
            return false;
        }

        for (var index = 2; index < points.Count; index++)
        {
            var offset = Subtract(points[index], origin);
            if (Math.Abs(Dot(normal.Value, offset)) >
                Math.Max(1e-6, Length(offset) * 1e-9))
            {
                return false;
            }
        }

        return true;
    }

    private static RectangularSymmetricHipEligibilityResult Fail(
        RectangularSymmetricHipEligibilityError error,
        RectangularRoofFootprintDescription? rectangle = null) =>
        new(false, RectangularSymmetricHipEligibilityKind.Ineligible, error, rectangle);

    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static double Dot(RoofPoint3D a, RoofPoint3D b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static double Length(RoofPoint3D v) =>
        Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private static RoofPoint3D Scale(RoofPoint3D v, double s) =>
        new(v.X * s, v.Y * s, v.Z * s);

    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
