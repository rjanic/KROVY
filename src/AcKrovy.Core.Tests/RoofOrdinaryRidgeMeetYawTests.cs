using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryRidgeMeetYawTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(2d)]
    [InlineData(23.029917d)]
    [InlineData(29.526529d)]
    [InlineData(30d)]
    [InlineData(60d)]
    public void RidgeMeet_FinalPhysicalCutUsesTopologyPlaneWithoutChangingSection(double yawDegrees)
    {
        var footprint = RoofFootprintValidator.Validate(new(
            [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(
            footprint.Footprint!, new(45), RoofKind.Hip)).Geometry);
        var layout = RoofFaceRafterLayoutService.Create(roof.Topology, 900).Layout!;
        var canonical = layout.Segments.First(item => item.SourceFaceIndex == 0 &&
            item.StartBoundaryRole == RoofRafterBoundaryRole.Eave &&
            item.EndBoundaryRole == RoofRafterBoundaryRole.Ridge &&
            item.PlanStart.X > 4000 && item.PlanStart.X < 6000);
        var end = canonical.PlanEnd;
        var start = yawDegrees == 0 ? canonical.PlanStart : new RoofPoint2D(
            end.X + 1200 * Math.Tan(yawDegrees * Math.PI / 180), end.Y - 1200);
        var accepted = new RoofSegment3D(new(start.X, start.Y, 0), new(end.X, end.Y, 0));
        var segment = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(
            roof.Topology, canonical, accepted);
        Assert.Equal(RoofRafterBoundaryRole.Ridge, segment.EndBoundaryRole);
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0, canonical.StationIndex);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuildSemanticMember(
            roof.Topology, segment, key, accepted, 3000, 80, 160,
            new(LowerEndCutMode.Vertical, RidgeJoinMode.Meet), null,
            out var member, out var reason), reason);
        Assert.NotNull(member);

        var face = roof.Topology.Faces.Single(item => item.SourceEdgeIndex == member.SourceFaceIndex);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(roof.Topology, face, out var normal));
        var n = new RoofPoint3D(normal.X, normal.Y, normal.Z);
        var origin = roof.Topology.Nodes[face.BoundaryNodeIndices[0]];
        var liftedStart = Lift(start, origin, n);
        var liftedEnd = Lift(end, origin, n);
        var expectedL = Unit(Sub(liftedEnd, liftedStart));
        var expectedW = Unit(Cross(n, expectedL));
        var prism = member.RidgeMeetCut?.SourcePrismVertices ?? member.SolidVertices;
        var actualL = Unit(Sub(prism[2], prism[0]));
        var actualW = Unit(Sub(prism[1], prism[0]));
        Assert.InRange(Dot(actualL, expectedL), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(Dot(actualW, expectedW), 1 - 1e-9, 1 + 1e-9);
        Assert.InRange(Math.Abs(Dot(actualL, actualW)), 0, 1e-9);
        Assert.InRange(Distance(prism[0], prism[1]), 80 - 1e-8, 80 + 1e-8);
        Assert.InRange(Dot(Sub(prism[0], prism[4]), n), 160 - 1e-8, 160 + 1e-8);
        var top = member.RidgeMeetCut?.TopFaceVertices ?? prism.Take(4).ToArray();
        Assert.All(top, vertex => Assert.InRange(Math.Abs(Dot(Sub(vertex,
            new RoofPoint3D(origin.X, origin.Y, origin.Z + 3000)), n)), 0, 1e-7));

        var ridgeEdge = Assert.Single(roof.Topology.Edges, edge =>
            edge.Kind == RoofTopologyEdgeKind.Ridge && edge.FaceIndices.Contains(face.SourceEdgeIndex));
        var ridgeNode = roof.Topology.Nodes[ridgeEdge.StartNodeIndex];
        var otherNode = roof.Topology.Nodes[ridgeEdge.EndNodeIndex];
        var ridgeNormal = Unit(new RoofPoint3D(
            -(otherNode.Y - ridgeNode.Y), otherNode.X - ridgeNode.X, 0));
        var ridgeVertices = member.RidgeMeetCut?.CutFaceVertices ??
            new[] { prism[2], prism[3], prism[6], prism[7] };
        Assert.True(ridgeVertices.Count >= 3);
        Assert.All(ridgeVertices, vertex => Assert.InRange(Math.Abs(Dot(Sub(vertex,
            new RoofPoint3D(ridgeNode.X, ridgeNode.Y, 0)), ridgeNormal)), 0, 1e-7));
        Assert.Equal(80, member.WidthMm);
        Assert.Equal(160, member.HeightMm);
    }

    private static RoofPoint3D Lift(RoofPoint2D point, RoofPoint3D origin, RoofPoint3D n) =>
        new(point.X, point.Y, origin.Z + 3000 -
            (n.X * (point.X - origin.X) + n.Y * (point.Y - origin.Y)) / n.Z);
    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Distance(RoofPoint3D a, RoofPoint3D b) => Math.Sqrt(Dot(Sub(a, b), Sub(a, b)));
    private static RoofPoint3D Unit(RoofPoint3D p) => new(p.X / Math.Sqrt(Dot(p, p)),
        p.Y / Math.Sqrt(Dot(p, p)), p.Z / Math.Sqrt(Dot(p, p)));
}
