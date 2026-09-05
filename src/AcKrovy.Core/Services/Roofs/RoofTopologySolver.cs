using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Shared all-eave, uniform-pitch topology entry point for one simple straight-edged
/// outer polygon. No ridge-direction input. Convex input retains the clipping
/// backend; reflex input uses the kinetic wavefront backend.
/// </summary>
public static class RoofTopologySolver
{
    public static RoofTopologyResult Solve(RoofFootprintInput? input, double pitchDegrees)
    {
        // Validate in a translated frame: the existing footprint validator's area
        // sum uses absolute coordinates. Do not change that stable production path.
        if (input?.Vertices is not { Count: > 0 } source ||
            source.Any(point => !Finite(point.X) || !Finite(point.Y)))
        {
            var invalid = RoofFootprintValidator.Validate(input);
            return Invalid(RoofTopologyError.InvalidFootprint, invalid.Error);
        }
        var origin = new RoofPoint2D(source.Min(point => point.X), source.Min(point => point.Y));
        var local = source.Select(point => new RoofPoint2D(point.X - origin.X, point.Y - origin.Y)).ToArray();
        if (local.Any(point => !Finite(point.X) || !Finite(point.Y)))
        {
            return Invalid(RoofTopologyError.NonFiniteGeometry);
        }
        var validated = RoofFootprintValidator.Validate(input with { Vertices = local });
        if (!validated.IsValid)
        {
            return Invalid(RoofTopologyError.InvalidFootprint, validated.Error);
        }
        var footprint = validated.Footprint!;
        if (!Finite(footprint.AreaMm2) || !Finite(footprint.Centroid.X) || !Finite(footprint.Centroid.Y) ||
            footprint.Edges.Any(edge => !Finite(edge.LengthMm)))
        {
            return Invalid(RoofTopologyError.NonFiniteGeometry);
        }
        if (footprint.Edges.Any(edge => edge.LengthMm <= SimpleGableRoofGeometryTolerance.MinimumDimensionMm))
        {
            return Invalid(RoofTopologyError.DegenerateDimensions);
        }
        if (!Finite(pitchDegrees) || pitchDegrees <= 0d || pitchDegrees >= 90d)
        {
            return Invalid(RoofTopologyError.InvalidSlope);
        }
        var points = footprint.Vertices;
        var concave = false;
        for (var i = 0; i < points.Count; i++)
        {
            var previous = footprint.Edges[(i + points.Count - 1) % points.Count];
            var next = footprint.Edges[i];
            var turn = ((previous.End.X - previous.Start.X) / previous.LengthMm *
                        (next.End.Y - next.Start.Y) / next.LengthMm) -
                       ((previous.End.Y - previous.Start.Y) / previous.LengthMm *
                        (next.End.X - next.Start.X) / next.LengthMm);
            if (turn < -RoofFootprintValidator.CollinearityTolerance)
            {
                concave = true;
            }
            if (Math.Abs(turn) <= RoofFootprintValidator.CollinearityTolerance)
            {
                return Invalid(RoofTopologyError.InvalidFootprint, RoofValidationError.RedundantCollinearVertex);
            }
        }
        return concave ? ConcaveRoofTopologySolver.Solve(footprint, origin, pitchDegrees) :
            ConvexRoofTopologySolver.Solve(footprint, origin, pitchDegrees);
    }

    public static RoofTopologyResult Solve(RoofFootprint footprint, double pitchDegrees)
    {
        if (footprint is null)
        {
            throw new ArgumentNullException(nameof(footprint));
        }
        return Solve(new RoofFootprintInput(footprint.Vertices, true), pitchDegrees);
    }

    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static RoofTopologyResult Invalid(RoofTopologyError error,
        RoofValidationError footprintError = RoofValidationError.None) => new(false, null, error, footprintError);
}
