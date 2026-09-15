using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofMissingValleyRafterRegressionTests
{
    public static IEnumerable<object[]> ConcaveFixtures()
    {
        yield return ["L", LShape(), 5, 1];
        yield return ["U", UShape(), 6, 2];
    }

    [Theory]
    [MemberData(nameof(ConcaveFixtures))]
    public void EveryReflexCorner_ReachesExactlyOneValleyDesiredMember(
        string name,
        RoofPoint2D[] points,
        int expectedHipCount,
        int expectedValleyCount)
    {
        _ = name;
        var pipeline = CreatePipeline(points);
        var reflexVertices = ReflexVertexIndices(points);

        Assert.Equal(expectedValleyCount, reflexVertices.Count);
        Assert.Equal(expectedHipCount, pipeline.Resolution.Edges.Count(edge =>
            edge.StructuralRole == RoofStructuralRole.Hip));
        Assert.Equal(expectedValleyCount, pipeline.Resolution.Edges.Count(edge =>
            edge.StructuralRole == RoofStructuralRole.Valley));
        Assert.Equal(expectedHipCount, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(expectedValleyCount, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.ValleyRafter));

        foreach (var reflexVertex in reflexVertices)
        {
            var topologyValley = Assert.Single(pipeline.Topology.Edges, edge =>
                edge.Kind == RoofTopologyEdgeKind.Valley &&
                (edge.StartNodeIndex == reflexVertex || edge.EndNodeIndex == reflexVertex));
            var topologyEdgeIndex = Enumerable.Range(0, pipeline.Topology.Edges.Count)
                .Single(index => ReferenceEquals(pipeline.Topology.Edges[index], topologyValley));
            var resolved = Assert.Single(pipeline.Resolution.Edges, edge =>
                edge.TopologyEdgeIndex == topologyEdgeIndex);
            Assert.Equal(RoofStructuralRole.Valley, resolved.StructuralRole);

            var expectedBoundaryIds = AdjacentBoundaryIds(
                reflexVertex,
                points.Length,
                pipeline.BoundaryIdentity);
            Assert.Equal(
                new RoofStructuralLogicalKey(
                    RoofStructuralRole.Valley,
                    expectedBoundaryIds.Lower,
                    expectedBoundaryIds.Upper),
                resolved.StructuralIdentity);

            var desired = Assert.Single(pipeline.Plan.Items, item =>
                item.LogicalKey == resolved.StructuralIdentity);
            Assert.Equal(TimberElementType.ValleyRafter, desired.ElementType);
            Assert.Equal(resolved.Segment3D, desired.Segment3D);
        }
    }

    [Theory]
    [MemberData(nameof(ConcaveFixtures))]
    public void OrdinaryRafters_TerminateAtAndNeverCrossEveryValley(
        string name,
        RoofPoint2D[] points,
        int expectedHipCount,
        int expectedValleyCount)
    {
        _ = name;
        _ = expectedHipCount;
        var pipeline = CreatePipeline(points);
        var layoutResult = RoofFaceRafterLayoutService.Create(pipeline.Topology, 500d);

        Assert.True(layoutResult.IsValid, layoutResult.Error.ToString());
        var layout = Assert.IsType<RoofFaceRafterLayout>(layoutResult.Layout);
        var valleys = pipeline.Topology.Edges
            .Where(edge => edge.Kind == RoofTopologyEdgeKind.Valley)
            .ToArray();
        Assert.Equal(expectedValleyCount, valleys.Length);

        foreach (var valley in valleys)
        {
            var boundary = pipeline.Topology.Segment(valley);
            var boundaryStart = new RoofPoint2D(boundary.Start.X, boundary.Start.Y);
            var boundaryEnd = new RoofPoint2D(boundary.End.X, boundary.End.Y);
            Assert.Contains(layout.Segments, rafter =>
                rafter.StartBoundaryRole == RoofRafterBoundaryRole.Valley &&
                OnSegment(rafter.PlanStart, boundaryStart, boundaryEnd) ||
                rafter.EndBoundaryRole == RoofRafterBoundaryRole.Valley &&
                OnSegment(rafter.PlanEnd, boundaryStart, boundaryEnd));
            Assert.DoesNotContain(layout.Segments, rafter => ProperlyIntersects(
                rafter.PlanStart,
                rafter.PlanEnd,
                boundaryStart,
                boundaryEnd));
        }

        Assert.Equal(
            layout.Segments.Count,
            layout.Segments.Select(GeometryKey).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(layout.Segments, segment =>
            segment.PlanLengthMm <= RoofFaceRafterLayoutService.CoordinateToleranceMm);
    }

    [Fact]
    public void LAndUReflexValleys_AreStableAcrossInputPermutations()
    {
        foreach (var points in new[] { LShape(), UShape() })
        {
            var idsByPhysicalEdge = Enumerable.Range(0, points.Length)
                .ToDictionary(index => PhysicalEdgeKey(points[index], points[(index + 1) % points.Length]), index => index + 1);
            var expected = StructuralKeys(CreatePipeline(points, idsByPhysicalEdge));

            for (var shift = 0; shift < points.Length; shift++)
            {
                foreach (var reverse in new[] { false, true })
                {
                    var ordered = reverse ? points.Reverse().ToArray() : points.ToArray();
                    var variant = Enumerable.Range(0, ordered.Length)
                        .Select(index => ordered[(index + shift) % ordered.Length])
                        .ToArray();
                    Assert.Equal(
                        expected,
                        StructuralKeys(CreatePipeline(variant, idsByPhysicalEdge)));
                }
            }
        }
    }

    [Fact]
    public void OrthogonalLAndUVariants_MaterializeOneValleyPerReflexCorner()
    {
        foreach (var width in new[] { 7000d, 10000d, 14000d })
        foreach (var height in new[] { 6500d, 9000d, 13000d })
        {
            var l = new[]
            {
                new RoofPoint2D(0, 0), new RoofPoint2D(width, 0),
                new RoofPoint2D(width, height * 0.35), new RoofPoint2D(width * 0.4, height * 0.35),
                new RoofPoint2D(width * 0.4, height), new RoofPoint2D(0, height),
            };
            var u = new[]
            {
                new RoofPoint2D(0, 0), new RoofPoint2D(width, 0),
                new RoofPoint2D(width, height), new RoofPoint2D(width * 0.72, height),
                new RoofPoint2D(width * 0.72, height * 0.3), new RoofPoint2D(width * 0.28, height * 0.3),
                new RoofPoint2D(width * 0.28, height), new RoofPoint2D(0, height),
            };

            AssertOneValleyPerReflexCorner(CreatePipeline(l));
            AssertOneValleyPerReflexCorner(CreatePipeline(u));
        }
    }

    private static void AssertOneValleyPerReflexCorner(Pipeline pipeline)
    {
        var reflexVertices = ReflexVertexIndices(pipeline.Points);
        Assert.All(reflexVertices, reflexVertex => Assert.Single(
            pipeline.Topology.Edges,
            edge => edge.Kind == RoofTopologyEdgeKind.Valley &&
                    (edge.StartNodeIndex == reflexVertex || edge.EndNodeIndex == reflexVertex)));
        Assert.Equal(reflexVertices.Count, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.ValleyRafter));
    }

    private static Pipeline CreatePipeline(
        RoofPoint2D[] points,
        IReadOnlyDictionary<string, int>? idsByPhysicalEdge = null)
    {
        var input = new RoofFootprintInput(points, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var sequential = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation);
        Assert.True(sequential.IsValid, sequential.Error.ToString());
        var identity = sequential.Identity!;
        if (idsByPhysicalEdge is not null)
        {
            identity = identity with
            {
                BoundaryEdgeIds = normalized.EdgeProvenance
                    .OrderBy(edge => edge.RawPhysicalSegmentIndex)
                    .Select(edge =>
                    {
                        var start = points[edge.RawPhysicalSegmentIndex];
                        var end = points[(edge.RawPhysicalSegmentIndex + 1) % points.Length];
                        return idsByPhysicalEdge[PhysicalEdgeKey(start, end)];
                    })
                    .ToArray(),
            };
        }

        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid, provenance.IdentityError.ToString());
        var solved = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
        Assert.True(resolution.IsValid, resolution.Error.ToString());
        var plan = RoofAutomaticStructuralRafterPlanner.Create(resolution);
        Assert.True(plan.IsValid, plan.Error.ToString());
        return new Pipeline(points, identity, geometry.Topology, resolution, plan);
    }

    private static IReadOnlyList<RoofStructuralLogicalKey> StructuralKeys(Pipeline pipeline) =>
        pipeline.Plan.Items.Select(item => item.LogicalKey).OrderBy(key => key.Role)
            .ThenBy(key => key.BoundaryEdgeIdA).ThenBy(key => key.BoundaryEdgeIdB).ToArray();

    private static (int Lower, int Upper) AdjacentBoundaryIds(
        int vertex,
        int count,
        RoofBoundaryIdentity identity)
    {
        var first = identity.BoundaryEdgeIds[(vertex + count - 1) % count];
        var second = identity.BoundaryEdgeIds[vertex];
        return (Math.Min(first, second), Math.Max(first, second));
    }

    private static IReadOnlyList<int> ReflexVertexIndices(IReadOnlyList<RoofPoint2D> points)
    {
        var result = new List<int>();
        for (var index = 0; index < points.Count; index++)
        {
            var previous = points[(index + points.Count - 1) % points.Count];
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            if (Cross(previous, current, next) < 0d)
            {
                result.Add(index);
            }
        }
        return result;
    }

    private static bool ProperlyIntersects(
        RoofPoint2D a,
        RoofPoint2D b,
        RoofPoint2D c,
        RoofPoint2D d)
    {
        const double tolerance = RoofFaceRafterLayoutService.CoordinateToleranceMm;
        return Cross(a, b, c) * Cross(a, b, d) < -tolerance &&
               Cross(c, d, a) * Cross(c, d, b) < -tolerance;
    }

    private static bool OnSegment(RoofPoint2D point, RoofPoint2D start, RoofPoint2D end)
    {
        var length = start.DistanceTo(end);
        return length > 0d &&
               Math.Abs(Cross(start, end, point)) / length <=
                   RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.X >= Math.Min(start.X, end.X) -
                   RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.X <= Math.Max(start.X, end.X) +
                   RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.Y >= Math.Min(start.Y, end.Y) -
                   RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.Y <= Math.Max(start.Y, end.Y) +
                   RoofFaceRafterLayoutService.CoordinateToleranceMm;
    }

    private static double Cross(RoofPoint2D a, RoofPoint2D b, RoofPoint2D c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static string GeometryKey(RoofFaceRafterSegment segment)
    {
        var first = PointKey(segment.PlanStart);
        var second = PointKey(segment.PlanEnd);
        return string.CompareOrdinal(first, second) <= 0
            ? first + ";" + second
            : second + ";" + first;
    }

    private static string PhysicalEdgeKey(RoofPoint2D start, RoofPoint2D end)
    {
        var first = PointKey(start);
        var second = PointKey(end);
        return string.CompareOrdinal(first, second) <= 0
            ? first + ";" + second
            : second + ";" + first;
    }

    private static string PointKey(RoofPoint2D point) =>
        $"{point.X:R},{point.Y:R}";

    private static RoofPoint2D[] LShape() =>
    [
        new(0, 0), new(8000, 0), new(8000, 3000),
        new(3000, 3000), new(3000, 8000), new(0, 8000),
    ];

    private static RoofPoint2D[] UShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000),
        new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000),
    ];

    private sealed record Pipeline(
        RoofPoint2D[] Points,
        RoofBoundaryIdentity BoundaryIdentity,
        RoofTopology Topology,
        RoofStructuralEdgeResolutionResult Resolution,
        RoofAutomaticStructuralRafterPlanResult Plan);
}
