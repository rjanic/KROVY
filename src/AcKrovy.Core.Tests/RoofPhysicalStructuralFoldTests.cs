using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofPhysicalStructuralFoldTests
{
    [Theory]
    [InlineData("rectangle")]
    [InlineData("canonical L")]
    [InlineData("canonical U")]
    [InlineData("canonical T")]
    [InlineData("asymmetric U")]
    [InlineData("asymmetric T")]
    [InlineData("actual HOST 291A L")]
    public void EveryEligibleStructuralTimber_MatchesPhysicalFoldClass(string name)
    {
        var pipeline = Create(PointsFor(name));
        foreach (var edge in pipeline.Resolution.Edges)
        {
            var topologyEdge = pipeline.Geometry.Topology.Edges[edge.TopologyEdgeIndex];
            var classified = RoofPhysicalStructuralFold.TryClassify(
                pipeline.Geometry.Topology, topologyEdge, out var fold);
            if (edge.StructuralRole == RoofStructuralRole.Ridge)
            {
                Assert.False(edge.IsAutomaticStructuralTimberEligible, name);
                Assert.True(classified, name);
                Assert.Equal(RoofPhysicalStructuralFoldClass.HorizontalRidge, fold);
                continue;
            }

            if (!edge.IsAutomaticStructuralTimberEligible)
            {
                Assert.DoesNotContain(pipeline.Plan.Items, item =>
                    item.LogicalKey == edge.StructuralIdentity);
                continue;
            }

            Assert.True(classified, name);
            if (edge.StructuralRole == RoofStructuralRole.Hip)
            {
                Assert.Equal(RoofPhysicalStructuralFoldClass.ConvexHip, fold);
            }
            else
            {
                Assert.Equal(RoofPhysicalStructuralFoldClass.ConcaveValley, fold);
            }

            Assert.Contains(pipeline.Plan.Items, item =>
                item.LogicalKey == edge.StructuralIdentity);
        }

        Assert.Equal(
            pipeline.Plan.Items.Count,
            pipeline.Plan.Items.Select(item => item.LogicalKey).Distinct().Count());
        Assert.DoesNotContain(pipeline.Plan.Items, item =>
            item.LogicalKey.Role == RoofStructuralRole.Ridge);
    }

    [Fact]
    public void ActualHost291AL_HasSixHipsOneValleyAndTwoHorizontalRidges()
    {
        var pipeline = Create(ActualHost291AL());
        Assert.Equal(6, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Single(pipeline.Plan.Items, item =>
            item.ElementType == TimberElementType.ValleyRafter);
        Assert.Contains(pipeline.Plan.Items, item =>
            item.LogicalKey.ToString() == "Hip|1|4");
        Assert.Equal(2, HorizontalRidgeCount(pipeline));
        AssertPurlinRidgeCount(pipeline, 2);
    }

    [Fact]
    public void HostEquivalentAsymmetricU_MaterializesBothInternalConvexFolds()
    {
        var pipeline = Create(AsymmetricU());
        Assert.Equal(8, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(2, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.ValleyRafter));

        var internalHip = Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.StructuralIdentity.ToString() == "Hip|5|8");
        Assert.True(internalHip.IsAutomaticStructuralTimberEligible);
        Assert.Null(internalHip.PhysicalPathAnchorVertexIndex);
        Assert.Contains(pipeline.Plan.Items, item =>
            item.LogicalKey.ToString() == "Hip|5|8" &&
            item.ElementType == TimberElementType.HipRafter);

        var continuation = Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.StructuralIdentity.ToString() == "Hip|2|5");
        Assert.True(continuation.IsAutomaticStructuralTimberEligible);
        Assert.Equal(
            RoofTopologyEdgeKind.Ridge,
            pipeline.Geometry.Topology.Edges[continuation.TopologyEdgeIndex].Kind);

        // Both outer long-side hips remain.
        Assert.Contains(pipeline.Plan.Items, item => item.LogicalKey.ToString() == "Hip|1|2");
        Assert.Contains(pipeline.Plan.Items, item => item.LogicalKey.ToString() == "Hip|1|8");
        Assert.Equal(3, HorizontalRidgeCount(pipeline));
        AssertPurlinRidgeCount(pipeline, 3);
    }

    [Fact]
    public void HostEquivalentAsymmetricT_MaterializesMarkedInternalConvexFolds()
    {
        var pipeline = Create(AsymmetricT());
        Assert.Equal(8, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(2, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.ValleyRafter));

        var hip16 = Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.StructuralIdentity.ToString() == "Hip|1|6");
        Assert.True(hip16.IsAutomaticStructuralTimberEligible);
        Assert.Null(hip16.PhysicalPathAnchorVertexIndex);
        Assert.Contains(pipeline.Plan.Items, item =>
            item.LogicalKey.ToString() == "Hip|1|6");

        var hip14 = Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.StructuralIdentity.ToString() == "Hip|1|4");
        Assert.True(hip14.IsAutomaticStructuralTimberEligible);
        Assert.Equal(
            RoofTopologyEdgeKind.Ridge,
            pipeline.Geometry.Topology.Edges[hip14.TopologyEdgeIndex].Kind);
        Assert.Contains(pipeline.Plan.Items, item =>
            item.LogicalKey.ToString() == "Hip|1|4");

        Assert.Equal(3, HorizontalRidgeCount(pipeline));
        AssertPurlinRidgeCount(pipeline, 3);
    }

    [Theory]
    [InlineData("asymmetric U")]
    [InlineData("asymmetric T")]
    [InlineData("actual HOST 291A L")]
    public void WindingAndCyclicStart_PreservePhysicalFoldTimberKeys(string name)
    {
        var points = PointsFor(name);
        var physicalIds = Enumerable.Range(0, points.Length).ToDictionary(
            index => PhysicalEdgeKey(points[index], points[(index + 1) % points.Length]),
            index => index + 1);
        var expected = DesiredKeys(Create(points, physicalIds));

        for (var shift = 0; shift < points.Length; shift++)
        foreach (var reverse in new[] { false, true })
        {
            var ordered = reverse ? points.Reverse().ToArray() : points.ToArray();
            var variant = Enumerable.Range(0, ordered.Length)
                .Select(index => ordered[(index + shift) % ordered.Length])
                .ToArray();
            Assert.Equal(expected, DesiredKeys(Create(variant, physicalIds)));
        }
    }

    [Theory]
    [InlineData("asymmetric U", 8, 2, 3)]
    [InlineData("asymmetric T", 8, 2, 3)]
    [InlineData("actual HOST 291A L", 6, 1, 2)]
    public void MirrorX_PreservesPhysicalFoldTimberCounts(
        string name,
        int expectedHip,
        int expectedValley,
        int expectedHorizontalRidge)
    {
        var points = PointsFor(name);
        var mirrored = points.Select(point => new RoofPoint2D(-point.X, point.Y)).ToArray();
        var pipeline = Create(mirrored);
        Assert.Equal(expectedHip, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(expectedValley, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.ValleyRafter));
        Assert.Equal(expectedHorizontalRidge, HorizontalRidgeCount(pipeline));
        AssertPurlinRidgeCount(pipeline, expectedHorizontalRidge);
        Assert.All(pipeline.Plan.Items, item =>
            Assert.True(pipeline.Resolution.Edges.Single(edge =>
                edge.StructuralIdentity == item.LogicalKey).IsAutomaticStructuralTimberEligible));
    }

    [Fact]
    public void TrueHorizontalRidge_NeverBecomesStructuralTimber()
    {
        var pipeline = Create(Rectangle());
        Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.StructuralRole == RoofStructuralRole.Ridge);
        Assert.All(pipeline.Resolution.Edges.Where(edge =>
                edge.StructuralRole == RoofStructuralRole.Ridge), edge =>
        {
            Assert.True(RoofPhysicalStructuralFold.IsHorizontalRidge(
                pipeline.Geometry.Topology,
                pipeline.Geometry.Topology.Edges[edge.TopologyEdgeIndex]));
            Assert.False(edge.IsAutomaticStructuralTimberEligible);
        });
        Assert.DoesNotContain(pipeline.Plan.Items, item =>
            item.LogicalKey.Role == RoofStructuralRole.Ridge);
        Assert.Equal(4, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        AssertPurlinRidgeCount(pipeline, 1);
    }

    private static int HorizontalRidgeCount(Pipeline pipeline) =>
        pipeline.Resolution.Edges.Count(edge =>
            edge.StructuralRole == RoofStructuralRole.Ridge &&
            RoofPhysicalStructuralFold.IsHorizontalRidge(
                pipeline.Geometry.Topology,
                pipeline.Geometry.Topology.Edges[edge.TopologyEdgeIndex]));

    private static void AssertPurlinRidgeCount(Pipeline pipeline, int expected)
    {
        var purlin = RoofAutomaticPurlinPlanner.Create(
            pipeline.Geometry,
            pipeline.Provenance,
            new RoofAutomaticPurlinLayout(true, Array.Empty<RoofAutomaticPurlinLayoutItem>()),
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.SourceEavePlane,
                    0d,
                    0d),
                2d,
                160d));
        Assert.True(purlin.IsValid, purlin.Error.ToString());
        Assert.Equal(expected, purlin.Plan!.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge));
    }

    private static string[] DesiredKeys(Pipeline pipeline) =>
        pipeline.Plan.Items.Select(item => item.LogicalKey.ToString())
            .Order(StringComparer.Ordinal).ToArray();

    private static Pipeline Create(
        RoofPoint2D[] points,
        IReadOnlyDictionary<string, int>? idsByPhysicalEdge = null)
    {
        var input = new RoofFootprintInput(points, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var identity = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation).Identity!;
        if (idsByPhysicalEdge is not null)
        {
            identity = identity with
            {
                BoundaryEdgeIds = Enumerable.Range(0, points.Length)
                    .Select(index => idsByPhysicalEdge[PhysicalEdgeKey(
                        points[index],
                        points[(index + 1) % points.Length])])
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
        return new Pipeline(geometry, resolution, plan, provenance);
    }

    private static RoofPoint2D[] PointsFor(string name) => name switch
    {
        "rectangle" => Rectangle(),
        "canonical L" => CanonicalL(),
        "canonical U" => CanonicalU(),
        "canonical T" => CanonicalT(),
        "asymmetric U" => AsymmetricU(),
        "asymmetric T" => AsymmetricT(),
        _ => ActualHost291AL(),
    };

    private static string PhysicalEdgeKey(RoofPoint2D start, RoofPoint2D end)
    {
        static string PointKey(RoofPoint2D point) => $"{point.X:R},{point.Y:R}";
        var first = PointKey(start);
        var second = PointKey(end);
        return string.CompareOrdinal(first, second) <= 0
            ? first + ";" + second
            : second + ";" + first;
    }

    private static RoofPoint2D[] Points(params (double X, double Y)[] points) =>
        points.Select(point => new RoofPoint2D(point.X, point.Y)).ToArray();

    private static RoofPoint2D[] Rectangle() =>
        Points((0, 0), (10000, 0), (10000, 6000), (0, 6000));

    private static RoofPoint2D[] CanonicalL() =>
        Points((0, 0), (8000, 0), (8000, 3000), (3000, 3000), (3000, 8000), (0, 8000));

    private static RoofPoint2D[] CanonicalU() =>
        Points((0, 0), (10000, 0), (10000, 9000), (7000, 9000), (7000, 3000), (3000, 3000), (3000, 9000), (0, 9000));

    private static RoofPoint2D[] CanonicalT() =>
        Points((0, 0), (10000, 0), (10000, 3000), (6500, 3000), (6500, 9000), (3500, 9000), (3500, 3000), (0, 3000));

    private static RoofPoint2D[] AsymmetricU() =>
        Points((0, 0), (12000, 0), (12000, 9500), (8500, 9500), (8500, 4000), (2500, 4000), (2500, 8000), (0, 8000));

    private static RoofPoint2D[] AsymmetricT() =>
        Points((0, 0), (12000, 0), (12000, 2500), (8500, 2500), (8500, 11000), (3500, 11000), (3500, 2500), (0, 2500));

    private static RoofPoint2D[] ActualHost291AL() =>
        Points(
            (47947.81366099819, 12295.18433052305),
            (47947.81366099819, 21478.922129281324),
            (57988.8584085473, 21478.922129281324),
            (57988.8584085473, 15403.104446947087),
            (52849.74092174883, 15403.104446947087),
            (52849.740921748824, 12295.18433052305));

    private sealed record Pipeline(
        HipRoofGeometry Geometry,
        RoofStructuralEdgeResolutionResult Resolution,
        RoofAutomaticStructuralRafterPlanResult Plan,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
