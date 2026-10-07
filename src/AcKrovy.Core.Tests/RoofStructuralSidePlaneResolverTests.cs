using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralSidePlaneResolverTests
{
    [Theory]
    [InlineData(RoofRafterBoundaryRole.Hip, 0d, 1000d, 80d)]
    [InlineData(RoofRafterBoundaryRole.Hip, 1000d, 1000d, 120d)]
    [InlineData(RoofRafterBoundaryRole.Hip, 1732.050807568877d, 1000d, 140d)]
    [InlineData(RoofRafterBoundaryRole.Valley, 1000d, 1000d, 100d)]
    [InlineData(RoofRafterBoundaryRole.Valley, 1732.050807568877d, 1000d, 180d)]
    [InlineData(RoofRafterBoundaryRole.Valley, -1000d, -1000d, 160d)]
    public void PlumbSidePlane_HasExactPlanHalfWidth_ForGeneralApproachAngle(
        RoofRafterBoundaryRole role, double approachX, double approachY,
        double structuralWidth)
    {
        var axis = new RoofSegment3D(
            new RoofPoint3D(0d, 0d, 3000d),
            new RoofPoint3D(2000d, 0d, 4000d));
        var target = new RoofStructuralRafterTrimSource(7, role, axis,
            structuralWidth);
        var centerlineEndpoint = new RoofPoint2D(500d, 0d);
        var interior = new RoofPoint2D(
            centerlineEndpoint.X + approachX,
            centerlineEndpoint.Y + approachY);

        Assert.True(RoofStructuralSidePlaneResolver.TryCreatePlane(
            target, interior, out var plane, out var reason), reason);
        Assert.Equal(0d, plane!.PlaneNormal.Z);
        Assert.Equal(1d, Math.Sqrt(
            plane.PlaneNormal.X * plane.PlaneNormal.X +
            plane.PlaneNormal.Y * plane.PlaneNormal.Y), 8);
        var measuredOffset =
            (plane.PlanePoint.X - axis.Start.X) * plane.PlaneNormal.X +
            (plane.PlanePoint.Y - axis.Start.Y) * plane.PlaneNormal.Y;
        Assert.Equal(structuralWidth / 2d, measuredOffset, 8);
        Assert.Equal(0d, centerlineEndpoint.Y);

        // The line/plane intersection uses the full approach angle, never a
        // fixed subtraction of W/2 along the ordinary axis.
        var signedAtInterior =
            (interior.X - plane.PlanePoint.X) * plane.PlaneNormal.X +
            (interior.Y - plane.PlanePoint.Y) * plane.PlaneNormal.Y;
        var signedAtCenter =
            (centerlineEndpoint.X - plane.PlanePoint.X) * plane.PlaneNormal.X +
            (centerlineEndpoint.Y - plane.PlanePoint.Y) * plane.PlaneNormal.Y;
        var fraction = -signedAtCenter / (signedAtInterior - signedAtCenter);
        var cut = new RoofPoint2D(
            centerlineEndpoint.X + fraction * approachX,
            centerlineEndpoint.Y + fraction * approachY);
        Assert.Equal(0d,
            (cut.X - plane.PlanePoint.X) * plane.PlaneNormal.X +
            (cut.Y - plane.PlanePoint.Y) * plane.PlaneNormal.Y, 8);
        if (Math.Abs(approachX) > 1d)
        {
            var distanceAlongOrdinary = fraction *
                Math.Sqrt(approachX * approachX + approachY * approachY);
            Assert.True(distanceAlongOrdinary > structuralWidth / 2d);
        }
    }

    [Theory]
    [InlineData(0d, "StructuralPlanDirectionDegenerate")]
    [InlineData(-1d, "StructuralWidthInvalid")]
    public void InvalidStructuralInput_FailsWithReason(
        double width, string expectedReason)
    {
        var axis = width == 0d
            ? new RoofSegment3D(new RoofPoint3D(0, 0, 0),
                new RoofPoint3D(0, 0, 100))
            : new RoofSegment3D(new RoofPoint3D(0, 0, 0),
                new RoofPoint3D(1000, 0, 100));
        var target = new RoofStructuralRafterTrimSource(
            0, RoofRafterBoundaryRole.Hip, axis,
            width == 0d ? 80d : width);
        Assert.False(RoofStructuralSidePlaneResolver.TryCreatePlane(
            target, new RoofPoint2D(0, 100), out var cut, out var reason));
        Assert.Null(cut);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void A_InteriorContact_ResolvesUniqueStructuralCut()
    {
        var (topology, sources, faceIndex) = ResolveTrapezoidHipFixture();
        // Face0 jack: endpoint lies in relative interior of one Hip.
        var jack = FindInteriorHipJack(topology, faceIndex: 0);
        Assert.True(RoofStructuralSidePlaneResolver.TryResolve(
            topology, jack.SourceFaceIndex, RoofRafterBoundaryRole.Hip,
            jack.Endpoint, jack.Interior, sources, out var cut, out var reason), reason);
        Assert.NotNull(cut);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void B_SingleEndpointContact_PreservesExistingBehavior()
    {
        var (topology, sources, _) = ResolveTrapezoidHipFixture();
        var jack = FindInteriorHipJack(topology, faceIndex: 0);
        var single = sources.Where(source =>
        {
            var edge = topology.Edges[source.TopologyEdgeIndex];
            return edge.FaceIndices.Contains(jack.SourceFaceIndex) &&
                   PointOnClosedSegment(topology.Segment(edge), jack.Endpoint);
        }).Take(1).ToArray();
        Assert.Single(single);
        Assert.True(RoofStructuralSidePlaneResolver.TryResolve(
            topology, jack.SourceFaceIndex, RoofRafterBoundaryRole.Hip,
            jack.Endpoint, jack.Interior, single, out var cut, out var reason), reason);
        Assert.NotNull(cut);
        Assert.Equal(single[0].TopologyEdgeIndex, cut!.TopologyEdgeIndex);
    }

    [Fact]
    public void C_SharedNodeTwoHipEndpoints_LeavesStructuralCutUnset()
    {
        var (topology, sources, _) = ResolveTrapezoidHipFixture();
        var apex = FindSharedNodeApex(topology);
        Assert.True(RoofStructuralSidePlaneResolver.TryResolve(
            topology, apex.SourceFaceIndex, RoofRafterBoundaryRole.Hip,
            apex.Endpoint, apex.Interior, sources, out var cut, out var reason), reason);
        Assert.Null(cut);
        Assert.Equal(RoofStructuralSidePlaneResolver.SharedNodeNoSideCutReason, reason);
    }

    [Fact]
    public void F_TrueAmbiguousNonSharedNode_FailsClosed()
    {
        // Duplicate interior contacts on the same Hip edge (not a shared-node apex).
        var (topology, sources, _) = ResolveTrapezoidHipFixture();
        var jack = FindInteriorHipJack(topology, faceIndex: 0);
        var hip = sources.First(source =>
            PointOnClosedSegment(topology.Segment(topology.Edges[source.TopologyEdgeIndex]), jack.Endpoint) &&
            IsRelativeInterior(topology.Segment(topology.Edges[source.TopologyEdgeIndex]), jack.Endpoint));
        var duplicated = new[] { hip, hip };
        Assert.False(RoofStructuralSidePlaneResolver.TryResolve(
            topology, jack.SourceFaceIndex, RoofRafterBoundaryRole.Hip,
            jack.Endpoint, jack.Interior, duplicated, out var cut, out var reason));
        Assert.Null(cut);
        Assert.Equal("StructuralTargetAmbiguous", reason);
    }

    [Fact]
    public void G_InteriorCandidateWinsOverEndpointCandidate()
    {
        var (topology, sources, _) = ResolveTrapezoidHipFixture();
        var jack = FindInteriorHipJack(topology, faceIndex: 0);
        Assert.True(RoofStructuralSidePlaneResolver.TryResolve(
            topology, jack.SourceFaceIndex, RoofRafterBoundaryRole.Hip,
            jack.Endpoint, jack.Interior, sources, out var cut, out var reason), reason);
        Assert.NotNull(cut);
        Assert.NotEqual(RoofStructuralSidePlaneResolver.SharedNodeNoSideCutReason, reason);
    }

    private static (RoofTopology Topology, RoofStructuralRafterTrimSource[] Sources, int FaceIndex)
        ResolveTrapezoidHipFixture()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new RoofPoint2D(0, 0), new RoofPoint2D(10000, 0),
                new RoofPoint2D(9500, 6000), new RoofPoint2D(1200, 6000),
            ], true));
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(35d), RoofKind.Hip));
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var sources = geometry.Topology.Edges
            .Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip)
            .Select(item => new RoofStructuralRafterTrimSource(
                item.index, RoofRafterBoundaryRole.Hip,
                geometry.Topology.Segment(item.edge), 100d))
            .ToArray();
        return (geometry.Topology, sources, 1);
    }

    private static (int SourceFaceIndex, RoofPoint2D Endpoint, RoofPoint2D Interior)
        FindSharedNodeApex(RoofTopology topology)
    {
        var layout = RoofFaceRafterLayoutService.Create(topology, 500d).Layout!;
        var apex = layout.Segments.First(segment =>
            segment.EndBoundaryRole == RoofRafterBoundaryRole.Hip &&
            CountHipHits(topology, segment.SourceFaceIndex, segment.PlanEnd) >= 2);
        return (apex.SourceFaceIndex, apex.PlanEnd, apex.PlanStart);
    }

    private static (int SourceFaceIndex, RoofPoint2D Endpoint, RoofPoint2D Interior)
        FindInteriorHipJack(RoofTopology topology, int faceIndex)
    {
        var layout = RoofFaceRafterLayoutService.Create(topology, 500d).Layout!;
        var jack = layout.Segments.First(segment =>
            segment.SourceFaceIndex == faceIndex &&
            segment.EndBoundaryRole == RoofRafterBoundaryRole.Hip &&
            CountHipHits(topology, segment.SourceFaceIndex, segment.PlanEnd) == 1 &&
            topology.Edges
                .Where(edge => edge.Kind == RoofTopologyEdgeKind.Hip &&
                               edge.FaceIndices.Contains(segment.SourceFaceIndex))
                .Select(edge => topology.Segment(edge))
                .Any(axis => IsRelativeInterior(axis, segment.PlanEnd)));
        return (jack.SourceFaceIndex, jack.PlanEnd, jack.PlanStart);
    }

    private static int CountHipHits(
        RoofTopology topology, int sourceFaceIndex, RoofPoint2D endpoint) =>
        topology.Edges.Count(edge =>
            edge.Kind == RoofTopologyEdgeKind.Hip &&
            edge.FaceIndices.Contains(sourceFaceIndex) &&
            PointOnClosedSegment(topology.Segment(edge), endpoint));

    private static bool PointOnClosedSegment(RoofSegment3D edge, RoofPoint2D point)
    {
        var dx = edge.End.X - edge.Start.X;
        var dy = edge.End.Y - edge.Start.Y;
        var squared = dx * dx + dy * dy;
        if (squared <= 0) return false;
        var fraction = ((point.X - edge.Start.X) * dx + (point.Y - edge.Start.Y) * dy) / squared;
        var missX = point.X - (edge.Start.X + fraction * dx);
        var missY = point.Y - (edge.Start.Y + fraction * dy);
        return fraction >= -1e-6 && fraction <= 1d + 1e-6 &&
               Math.Sqrt(missX * missX + missY * missY) <= 1e-6;
    }

    private static bool IsRelativeInterior(RoofSegment3D edge, RoofPoint2D point)
    {
        var dx = edge.End.X - edge.Start.X;
        var dy = edge.End.Y - edge.Start.Y;
        var squared = dx * dx + dy * dy;
        if (squared <= 0) return false;
        var fraction = ((point.X - edge.Start.X) * dx + (point.Y - edge.Start.Y) * dy) / squared;
        return PointOnClosedSegment(edge, point) &&
               fraction > 1e-6 && fraction < 1d - 1e-6;
    }
}
