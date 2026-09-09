using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Extracts the authoritative XY roof-footprint polygon used by generated-member
/// override domain validation. Hip uses topology boundary nodes; gable/monopitch
/// use their plane/eave boundaries. Never invents an AABB of stations.
/// </summary>
public static class RoofRafterDomainPolygon
{
    public static IReadOnlyList<RoofPoint2D> FromGeometry(IRoofGeometry geometry) =>
        geometry switch
        {
            HipRoofGeometry hip => FromHip(hip),
            MonopitchRoofGeometry monopitch => FromBoundary(monopitch.BoundaryPoints),
            SimpleGableRoofGeometry gable => FromGable(gable),
            _ => Array.Empty<RoofPoint2D>(),
        };

    public static IReadOnlyList<RoofPoint2D> FromHip(HipRoofGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        var count = geometry.Topology.BoundaryVertexCount;
        if (count < 3 || geometry.Topology.Nodes.Count < count)
        {
            return Array.Empty<RoofPoint2D>();
        }

        var points = new RoofPoint2D[count];
        for (var index = 0; index < count; index++)
        {
            var node = geometry.Topology.Nodes[index];
            points[index] = new RoofPoint2D(node.X, node.Y);
        }

        return points;
    }

    private static IReadOnlyList<RoofPoint2D> FromGable(SimpleGableRoofGeometry gable)
    {
        if (gable.Faces.Count < 2)
        {
            return Array.Empty<RoofPoint2D>();
        }

        // Face0 eave start→end, Face1 eave end→start reconstitutes the footprint.
        var face0 = gable.Faces[0].Eave;
        var face1 = gable.Faces[1].Eave;
        return
        [
            new RoofPoint2D(face0.Start.X, face0.Start.Y),
            new RoofPoint2D(face0.End.X, face0.End.Y),
            new RoofPoint2D(face1.End.X, face1.End.Y),
            new RoofPoint2D(face1.Start.X, face1.Start.Y),
        ];
    }

    private static IReadOnlyList<RoofPoint2D> FromBoundary(IReadOnlyList<RoofPoint3D> boundary)
    {
        if (boundary is null || boundary.Count < 3)
        {
            return Array.Empty<RoofPoint2D>();
        }

        var points = new RoofPoint2D[boundary.Count];
        for (var index = 0; index < boundary.Count; index++)
        {
            points[index] = new RoofPoint2D(boundary[index].X, boundary[index].Y);
        }

        return points;
    }
}
