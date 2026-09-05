using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Hip preset over the shared polygon topology engine. Legacy rectangle direction
/// is an optional wrapper constraint; arbitrary polygons derive their own branches.
/// </summary>
public static class HipRoofGeometrySolver
{
    public static RoofGeometryResult Solve(RoofDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (definition.Kind != RoofKind.Hip) return Invalid(SimpleGableRoofGeometryError.InvalidRoofKind);
        var parameters = definition.Parameters;
        if (parameters.Face1SlopeDegrees is { } otherSlope &&
            (!RoofTopologySolver.Finite(otherSlope) || !RoofTopologySolver.Finite(parameters.SlopeDegrees ?? double.NaN) ||
             Math.Abs(otherSlope - parameters.SlopeDegrees!.Value) > SimpleGableRoofGeometryTolerance.AngularTolerance))
        {
            return Invalid(SimpleGableRoofGeometryError.InvalidSlope);
        }
        if (parameters.EaveHeightDifferenceMm is { } difference &&
            (!RoofTopologySolver.Finite(difference) || Math.Abs(difference) > SimpleGableRoofGeometryTolerance.CoordinateToleranceMm))
        {
            return Invalid(SimpleGableRoofGeometryError.InvalidEaveHeightDifference);
        }
        var result = RoofTopologySolver.Solve(definition.Footprint, parameters.SlopeDegrees ?? double.NaN);
        if (!result.IsValid)
        {
            return Invalid(result.Error switch
            {
                RoofTopologyError.InvalidSlope => SimpleGableRoofGeometryError.InvalidSlope,
                RoofTopologyError.DegenerateDimensions => SimpleGableRoofGeometryError.DegenerateDimensions,
                RoofTopologyError.NonFiniteGeometry => SimpleGableRoofGeometryError.NonFiniteGeometry,
                RoofTopologyError.ConcaveWavefrontNotImplemented => SimpleGableRoofGeometryError.ConcaveWavefrontNotImplemented,
                RoofTopologyError.InvalidFootprint => SimpleGableRoofGeometryError.InvalidFootprint,
                _ => SimpleGableRoofGeometryError.NumericallyUnresolvedTopology,
            });
        }
        var topology = result.Topology!;
        var firstEave = topology.Segment(topology.Edges[0]);
        var direction = CanonicalDirection(firstEave.Start, firstEave.End);
        var firstFace = 0;
        if (definition.Footprint.Vertices.Count == 4)
        {
            // Reuse stable rectangle recognition only for compatibility presentation.
            // It never constructs the polygon topology or drives the convex backend.
            var longest = definition.Footprint.Edges.OrderByDescending(edge => edge.LengthMm).First();
            RoofDirection2D.TryCreate(longest.End.X - longest.Start.X, longest.End.Y - longest.Start.Y, out var inferred);
            var recognized = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
                definition.Footprint, parameters with { RidgeDirection = inferred }, RoofKind.SimpleGable));
            if (recognized.IsValid)
            {
                var gable = recognized.Geometry!;
                var tolerance = SimpleGableRoofGeometryTolerance.LengthTolerance(gable.RidgeLengthMm, 2d * gable.RunMm);
                if (parameters.RidgeDirection is { } requested)
                {
                    var legacy = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
                        definition.Footprint, parameters with { RidgeDirection = requested }, RoofKind.SimpleGable));
                    if (!legacy.IsValid || legacy.Geometry!.RidgeLengthMm < 2d * legacy.Geometry.RunMm - tolerance)
                    {
                        return Invalid(SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved);
                    }
                }
                if (topology.Nodes.Count == 5)
                {
                    var first = definition.Footprint.Edges[0];
                    RoofDirection2D.TryCreate(first.End.X - first.Start.X, first.End.Y - first.Start.Y, out inferred);
                    gable = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
                        definition.Footprint, parameters with { RidgeDirection = inferred }, RoofKind.SimpleGable)).Geometry!;
                }
                direction = gable.RidgeDirection;
                firstFace = Enumerable.Range(0, 4).First(index =>
                    topology.Nodes[index].DistanceTo(gable.Faces[0].Eave.Start) <= tolerance);
            }
        }
        return new RoofGeometryResult(true, new HipRoofGeometry(topology, direction, firstFace), SimpleGableRoofGeometryError.None);
    }

    private static RoofDirection2D CanonicalDirection(RoofPoint3D first, RoofPoint3D second)
    {
        var x = second.X - first.X;
        var y = second.Y - first.Y;
        RoofDirection2D.TryCreate(x, y, out var direction);
        if (direction.X < -SimpleGableRoofGeometryTolerance.AngularTolerance ||
            Math.Abs(direction.X) <= SimpleGableRoofGeometryTolerance.AngularTolerance && direction.Y < 0d)
        {
            RoofDirection2D.TryCreate(-x, -y, out direction);
        }
        return direction;
    }

    private static RoofGeometryResult Invalid(SimpleGableRoofGeometryError error) => new(false, null, error);
}
