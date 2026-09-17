using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofSpuriousHipRafterRegressionTests
{
    [Fact]
    public void ActualHost291AL_InclinedTopologyRidgeResolvesToPhysicalHipContinuation()
    {
        var pipeline = CreatePipeline(ActualHost291AL());
        var topology = pipeline.Geometry.Topology;
        var ridges = topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Ridge)
            .ToArray();
        var horizontalRidges = ridges.Where(item => Math.Abs(
            topology.Nodes[item.edge.StartNodeIndex].Z -
            topology.Nodes[item.edge.EndNodeIndex].Z) <=
            RoofAutomaticPurlinPlanner.CoordinateToleranceMm).ToArray();
        var inclinedRidge = Assert.Single(ridges.Except(horizontalRidges));
        var continuation = Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.TopologyEdgeIndex == inclinedRidge.index);
        var valley = Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.StructuralIdentity.ToString() == "Valley|4|5");

        Assert.Equal(3, ridges.Length);
        Assert.Equal(2, horizontalRidges.Length);
        Assert.Equal(5, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip));
        Assert.Single(topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Valley);
        Assert.Equal(new[] { 2, 5 }, inclinedRidge.edge.FaceIndices);
        Assert.Equal(2, inclinedRidge.edge.OriginatingBoundaryVertexIndex);
        Assert.Equal(RoofStructuralRole.Hip, continuation.StructuralRole);
        Assert.Equal("Hip|1|4", continuation.StructuralIdentity.ToString());
        Assert.Equal(2, continuation.OriginatingBoundaryVertexIndex);
        Assert.Null(continuation.PhysicalBoundaryAnchorVertexIndex);
        Assert.Equal(5, continuation.PhysicalPathAnchorVertexIndex);
        Assert.True(continuation.IsAutomaticStructuralTimberEligible);
        AssertPoint(
            new RoofPoint3D(50398.77729137352, 17854.06807732241, 1415.064511771175),
            continuation.Segment3D.Start);
        AssertPoint(
            new RoofPoint3D(50985.72250216531, 18441.013288114205, 1753.9374872213793),
            continuation.Segment3D.End);
        Assert.Contains(
            new[] { valley.Segment3D.Start, valley.Segment3D.End },
            point => point.DistanceTo(continuation.Segment3D.Start) <= 1e-6);
        Assert.Equal(6, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Single(pipeline.Plan.Items, item =>
            item.ElementType == TimberElementType.ValleyRafter);
        Assert.Contains(pipeline.Plan.Items, item =>
            item.LogicalKey == continuation.StructuralIdentity &&
            item.ElementType == TimberElementType.HipRafter);
        Assert.DoesNotContain(topology.Edges, edge =>
            edge.Kind == RoofTopologyEdgeKind.Hip &&
            edge.StartNodeIndex >= topology.BoundaryVertexCount &&
            edge.EndNodeIndex >= topology.BoundaryVertexCount);

        var purlin = RoofAutomaticPurlinPlanner.Create(
            pipeline.Geometry,
            pipeline.Provenance,
            new RoofAutomaticPurlinLayout(
                true,
                Array.Empty<RoofAutomaticPurlinLayoutItem>()),
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.SourceEavePlane,
                    0d,
                    0d),
                2d,
                160d));
        Assert.True(purlin.IsValid, purlin.Error.ToString());
        Assert.Equal(2, purlin.Plan!.Items.Count);
        Assert.All(purlin.Plan.Items, item => Assert.Equal(
            RoofAutomaticPurlinGeneratorRole.Ridge,
            item.GeneratorRole));
    }

    public static IEnumerable<object[]> StructuralFixtures()
    {
        yield return ["rectangle", Rectangle(), 1, 4, 4, 0, 4, 0];
        yield return ["canonical L", CanonicalL(), 2, 5, 5, 1, 5, 1];
        yield return ["canonical U", CanonicalU(), 3, 6, 6, 2, 6, 2];
        yield return ["canonical T", CanonicalT(), 3, 6, 6, 2, 6, 2];
        yield return ["HOST 291A L", Host291AL(), 3, 5, 5, 1, 6, 1];
        yield return ["actual HOST 291A L", ActualHost291AL(), 3, 5, 5, 1, 6, 1];
        yield return ["asymmetric U", AsymmetricU(), 4, 7, 6, 2, 8, 2];
        yield return ["mirrored asymmetric U", MirrorX(AsymmetricU()), 4, 7, 6, 2, 8, 2];
        yield return ["skewed L continuation", SkewedLContinuation(), 2, 6, 5, 1, 6, 1];
        yield return ["skewed U continuation", SkewedUContinuation(), 4, 7, 6, 2, 8, 2];
        yield return ["mirrored skewed U continuation", MirrorX(SkewedUContinuation()), 4, 7, 6, 2, 8, 2];
        yield return ["reflex hexagon", ReflexHexagon(), 2, 6, 5, 1, 7, 1];
        yield return ["split dumbbell", SplitDumbbell(), 4, 9, 8, 4, 9, 4];
        yield return ["concave quadrilateral", ConcaveQuadrilateral(), 0, 4, 3, 1, 4, 1];
    }

    [Fact]
    public void Host291AL_InternalConvexFold_IsPhysicalHipTimber()
    {
        var pipeline = CreatePipeline(Host291AL());
        var topology = pipeline.Geometry.Topology;
        var candidate = Assert.Single(topology.Edges.Select((edge, index) => (edge, index)), item =>
            item.edge.StartNodeIndex >= topology.BoundaryVertexCount &&
            item.edge.EndNodeIndex >= topology.BoundaryVertexCount &&
            item.edge.FaceIndices.SequenceEqual(new[] { 1, 4 }));
        var resolved = Assert.Single(pipeline.Resolution.Edges, edge =>
            edge.TopologyEdgeIndex == candidate.index);

        Assert.Equal(RoofTopologyEdgeKind.Ridge, candidate.edge.Kind);
        Assert.Equal(2, candidate.edge.OriginatingBoundaryVertexIndex);
        AssertPoint(
            new RoofPoint3D(50398.777291499995, 17587.6826095, 1415.0645118431564),
            topology.Nodes[candidate.edge.StartNodeIndex]);
        AssertPoint(
            new RoofPoint3D(50531.9700255, 17720.8753435, 1338.1656510141547),
            topology.Nodes[candidate.edge.EndNodeIndex]);
        Assert.Equal(RoofStructuralRole.Hip, resolved.StructuralRole);
        Assert.Equal("Hip|2|5", resolved.StructuralIdentity.ToString());
        Assert.Equal(2, resolved.OriginatingBoundaryVertexIndex);
        Assert.Null(resolved.PhysicalBoundaryAnchorVertexIndex);
        Assert.Equal(5, resolved.PhysicalPathAnchorVertexIndex);
        Assert.True(RoofPhysicalStructuralFold.TryClassify(
            topology, candidate.edge, out var fold));
        Assert.Equal(RoofPhysicalStructuralFoldClass.ConvexHip, fold);
        Assert.True(resolved.IsAutomaticStructuralTimberEligible);
        Assert.Contains(pipeline.Plan.Items, item =>
            item.LogicalKey == resolved.StructuralIdentity &&
            item.ElementType == TimberElementType.HipRafter);

        Assert.Equal(6, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Single(pipeline.Plan.Items, item =>
            item.ElementType == TimberElementType.ValleyRafter);
        var ordinary = RoofFaceRafterLayoutService.Create(topology, 900d);
        Assert.True(ordinary.IsValid, ordinary.Error.ToString());
        Assert.Equal(44, ordinary.Layout!.Segments.Count);
        AssertHorizontalRidgeCount(pipeline, 2);
    }

    [Fact]
    public void AsymmetricU_InternalConvexFold_IsPhysicalHipTimberOnEitherConcaveSide()
    {
        var variants = new[] { AsymmetricU(), MirrorX(AsymmetricU()) };
        foreach (var points in variants)
        {
            var pipeline = CreatePipeline(points);
            var topology = pipeline.Geometry.Topology;
            var candidate = Assert.Single(topology.Edges.Select((edge, index) => (edge, index)), item =>
                item.edge.Kind == RoofTopologyEdgeKind.Hip &&
                item.edge.StartNodeIndex >= topology.BoundaryVertexCount &&
                item.edge.EndNodeIndex >= topology.BoundaryVertexCount);
            var resolved = Assert.Single(pipeline.Resolution.Edges, edge =>
                edge.TopologyEdgeIndex == candidate.index);

            Assert.NotNull(candidate.edge.OriginatingBoundaryVertexIndex);
            Assert.True(IsConvex(topology, candidate.edge.OriginatingBoundaryVertexIndex!.Value));
            Assert.Equal(RoofStructuralRole.Hip, resolved.StructuralRole);
            Assert.Null(resolved.PhysicalBoundaryAnchorVertexIndex);
            Assert.True(RoofPhysicalStructuralFold.TryClassify(
                topology, candidate.edge, out var fold));
            Assert.Equal(RoofPhysicalStructuralFoldClass.ConvexHip, fold);
            Assert.True(resolved.IsAutomaticStructuralTimberEligible);
            Assert.Contains(pipeline.Plan.Items, item =>
                item.LogicalKey == resolved.StructuralIdentity &&
                item.ElementType == TimberElementType.HipRafter);
            Assert.Equal(8, pipeline.Plan.Items.Count(item =>
                item.ElementType == TimberElementType.HipRafter));
            Assert.Equal(2, pipeline.Plan.Items.Count(item =>
                item.ElementType == TimberElementType.ValleyRafter));
            AssertAllReflexValleysArePhysicalAndMaterialized(pipeline);
            AssertHorizontalRidgeCount(pipeline, expectedMinimum: 1);
        }

        var original = CreatePipeline(AsymmetricU());
        var originalTopology = original.Geometry.Topology;
        var originalCandidate = Assert.Single(original.Resolution.Edges, edge =>
            edge.StructuralRole == RoofStructuralRole.Hip &&
            !edge.HasPhysicalBoundaryAnchor &&
            !edge.HasPhysicalPathAnchor &&
            originalTopology.Edges[edge.TopologyEdgeIndex].Kind == RoofTopologyEdgeKind.Hip);
        var originalEdge = originalTopology.Edges[originalCandidate.TopologyEdgeIndex];
        Assert.Equal(new[] { 1, 4 }, originalEdge.FaceIndices);
        Assert.Equal(2, originalCandidate.OriginatingBoundaryVertexIndex);
        Assert.Equal("Hip|2|5", originalCandidate.StructuralIdentity.ToString());
        Assert.True(originalCandidate.IsAutomaticStructuralTimberEligible);
        AssertPoint(
            new RoofPoint3D(10000, 2000, 1154.7005383792514),
            originalCandidate.Segment3D.Start);
        AssertPoint(
            new RoofPoint3D(10250, 2250, 1010.362971081845),
            originalCandidate.Segment3D.End);
    }

    [Theory]
    [MemberData(nameof(StructuralFixtures))]
    public void DesiredStructuralTimber_UsesCompatibleConnectedPhysicalPaths(
        string name,
        RoofPoint2D[] points,
        int expectedRidgeTopology,
        int expectedHipTopology,
        int expectedHipPaths,
        int expectedValleyTopology,
        int expectedHipTimber,
        int expectedValleyTimber)
    {
        var pipeline = CreatePipeline(points);
        var topology = pipeline.Geometry.Topology;
        Assert.Equal(expectedRidgeTopology, topology.Edges.Count(edge =>
            edge.Kind == RoofTopologyEdgeKind.Ridge));
        Assert.Equal(expectedHipTopology, topology.Edges.Count(edge =>
            edge.Kind == RoofTopologyEdgeKind.Hip));
        Assert.Equal(expectedValleyTopology, topology.Edges.Count(edge =>
            edge.Kind == RoofTopologyEdgeKind.Valley));
        Assert.Equal(expectedHipPaths, pipeline.Resolution.Edges
            .Where(edge => edge.StructuralRole == RoofStructuralRole.Hip)
            .Select(edge => edge.PhysicalPathAnchorVertexIndex)
            .Where(anchor => anchor.HasValue)
            .Distinct()
            .Count());
        Assert.Equal(expectedHipTimber, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(expectedValleyTimber, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.ValleyRafter));
        Assert.Equal(
            pipeline.Plan.Items.Count,
            pipeline.Plan.Items.Select(item => item.LogicalKey).Distinct().Count());

        foreach (var edge in pipeline.Resolution.Edges.Where(edge =>
                     edge.StructuralRole is RoofStructuralRole.Hip or RoofStructuralRole.Valley))
        {
            var topologyEdge = topology.Edges[edge.TopologyEdgeIndex];
            var desired = pipeline.Plan.Items.Where(item =>
                item.LogicalKey == edge.StructuralIdentity).ToArray();
            Assert.True(
                RoofPhysicalStructuralFold.TryClassify(topology, topologyEdge, out var fold),
                name);
            if (!edge.IsAutomaticStructuralTimberEligible)
            {
                Assert.Empty(desired);
                Assert.True(
                    fold is RoofPhysicalStructuralFoldClass.Invalid or
                        RoofPhysicalStructuralFoldClass.HorizontalRidge,
                    name);
                continue;
            }

            var item = Assert.Single(desired);
            if (edge.StructuralRole == RoofStructuralRole.Hip)
            {
                Assert.Equal(RoofPhysicalStructuralFoldClass.ConvexHip, fold);
                Assert.Equal(TimberElementType.HipRafter, item.ElementType);
            }
            else
            {
                Assert.Equal(RoofPhysicalStructuralFoldClass.ConcaveValley, fold);
                Assert.Equal(TimberElementType.ValleyRafter, item.ElementType);
            }
        }

        Assert.DoesNotContain(
            pipeline.Plan.Items,
            item => item.LogicalKey.Role == RoofStructuralRole.Ridge);

        foreach (var path in pipeline.Resolution.Edges
                     .Where(edge => edge.IsAutomaticStructuralTimberEligible &&
                                    edge.PhysicalPathAnchorVertexIndex.HasValue)
                     .GroupBy(edge => (edge.StructuralRole, edge.PhysicalPathAnchorVertexIndex)))
        {
            Assert.Single(path, edge => edge.HasPhysicalBoundaryAnchor);
            AssertConnectedPath(topology, path, path.Key.PhysicalPathAnchorVertexIndex!.Value, name);
        }
    }

    [Theory]
    [InlineData("skewed L")]
    [InlineData("skewed U")]
    [InlineData("mirrored skewed U")]
    [InlineData("reflex hexagon")]
    [InlineData("split dumbbell")]
    [InlineData("concave quadrilateral")]
    public void DownstreamSameLineageHipContinuation_IsEligibleAndMaterialized(string name)
    {
        var points = name switch
        {
            "skewed L" => SkewedLContinuation(),
            "skewed U" => SkewedUContinuation(),
            "mirrored skewed U" => MirrorX(SkewedUContinuation()),
            "reflex hexagon" => ReflexHexagon(),
            "split dumbbell" => SplitDumbbell(),
            _ => ConcaveQuadrilateral(),
        };
        var pipeline = CreatePipeline(points);
        var topology = pipeline.Geometry.Topology;
        var hipPaths = pipeline.Resolution.Edges
            .Where(edge => edge.StructuralRole == RoofStructuralRole.Hip &&
                           edge.PhysicalPathAnchorVertexIndex.HasValue)
            .GroupBy(edge => edge.PhysicalPathAnchorVertexIndex);
        var path = Assert.Single(hipPaths, group =>
            group.Count() > 1 &&
            group.All(edge => edge.OriginatingBoundaryVertexIndex == group.Key));

        Assert.Single(path, edge => edge.HasPhysicalBoundaryAnchor);
        Assert.Contains(path, edge => !edge.HasPhysicalBoundaryAnchor);
        Assert.All(path, edge =>
        {
            Assert.True(edge.IsAutomaticStructuralTimberEligible);
            Assert.Contains(pipeline.Plan.Items, item => item.LogicalKey == edge.StructuralIdentity);
        });
        if (name == "skewed L")
        {
            Assert.Equal(
                new[] { "Hip|1|3", "Hip|1|6" },
                path.Select(edge => edge.StructuralIdentity.ToString()).Order().ToArray());
        }
        else if (name == "skewed U")
        {
            Assert.Equal(
                new[] { "Hip|1|8", "Hip|6|8" },
                path.Select(edge => edge.StructuralIdentity.ToString()).Order().ToArray());
        }
        AssertConnectedPath(topology, path, path.Key!.Value, name);
    }

    [Theory]
    [InlineData("canonical L")]
    [InlineData("canonical U")]
    public void CanonicalLAndU_PreserveEveryGenuineOuterHipAndEveryReflexValley(string name)
    {
        var points = name == "canonical L" ? CanonicalL() : CanonicalU();
        var pipeline = CreatePipeline(points);
        var topology = pipeline.Geometry.Topology;
        var convex = Enumerable.Range(0, topology.BoundaryVertexCount)
            .Where(index => IsConvex(topology, index)).ToArray();
        var reflex = Enumerable.Range(0, topology.BoundaryVertexCount)
            .Where(index => IsReflex(topology, index)).ToArray();

        Assert.Equal(convex.Length, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.HipRafter));
        Assert.Equal(reflex.Length, pipeline.Plan.Items.Count(item =>
            item.ElementType == TimberElementType.ValleyRafter));
        Assert.All(convex, anchor => Assert.Single(
            pipeline.Resolution.Edges,
            edge => edge.StructuralRole == RoofStructuralRole.Hip &&
                    edge.PhysicalBoundaryAnchorVertexIndex == anchor));
        AssertAllReflexValleysArePhysicalAndMaterialized(pipeline);

        var ordinary = RoofFaceRafterLayoutService.Create(topology, 500d);
        Assert.True(ordinary.IsValid, name + ": " + ordinary.Error);
        Assert.DoesNotContain(ordinary.Layout!.Segments, segment =>
            segment.PlanLengthMm <= RoofFaceRafterLayoutService.CoordinateToleranceMm);
        Assert.Equal(
            ordinary.Layout.Segments.Count,
            ordinary.Layout.Segments.Select(GeometryKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PhysicalEligibilityAndDesiredKeys_AreStableAcrossInputOrdering()
    {
        foreach (var points in new[]
                 {
                     Host291AL(), ActualHost291AL(), AsymmetricU(), SkewedLContinuation(),
                     SkewedUContinuation(), MirrorX(SkewedUContinuation()),
                     SplitDumbbell(),
                 })
        {
            var physicalIds = Enumerable.Range(0, points.Length).ToDictionary(
                index => PhysicalEdgeKey(points[index], points[(index + 1) % points.Length]),
                index => index + 1);
            var expected = StructuralKeys(CreatePipeline(points, physicalIds));

            for (var shift = 0; shift < points.Length; shift++)
            foreach (var reverse in new[] { false, true })
            {
                var ordered = reverse ? points.Reverse().ToArray() : points.ToArray();
                var variant = Enumerable.Range(0, ordered.Length)
                    .Select(index => ordered[(index + shift) % ordered.Length])
                    .ToArray();
                var pipeline = CreatePipeline(variant, physicalIds);

                Assert.Equal(expected, StructuralKeys(pipeline));
                Assert.All(pipeline.Plan.Items, item =>
                {
                    var edge = pipeline.Resolution.Edges.Single(resolved =>
                        resolved.StructuralIdentity == item.LogicalKey);
                    Assert.True(edge.IsAutomaticStructuralTimberEligible);
                    Assert.True(RoofPhysicalStructuralFold.IsTimberEligibleFold(
                        pipeline.Geometry.Topology,
                        pipeline.Geometry.Topology.Edges[edge.TopologyEdgeIndex],
                        edge.StructuralRole));
                });
            }
        }
    }

    private static void AssertHorizontalRidgeCount(Pipeline pipeline, int? expectedExact = null, int expectedMinimum = 0)
    {
        var horizontal = pipeline.Resolution.Edges.Count(edge =>
            edge.StructuralRole == RoofStructuralRole.Ridge &&
            RoofPhysicalStructuralFold.IsHorizontalRidge(
                pipeline.Geometry.Topology,
                pipeline.Geometry.Topology.Edges[edge.TopologyEdgeIndex]));
        if (expectedExact.HasValue)
        {
            Assert.Equal(expectedExact.Value, horizontal);
        }
        else
        {
            Assert.True(horizontal >= expectedMinimum);
        }

        Assert.DoesNotContain(pipeline.Plan.Items, item =>
            item.LogicalKey.Role == RoofStructuralRole.Ridge);
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
        Assert.Equal(horizontal, purlin.Plan!.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge));
    }

    private static void AssertConnectedPath(
        RoofTopology topology,
        IEnumerable<ResolvedRoofStructuralEdge> resolvedPath,
        int anchor,
        string name)
    {
        var path = resolvedPath.Select(edge => topology.Edges[edge.TopologyEdgeIndex]).ToArray();
        var reachedNodes = new HashSet<int> { anchor };
        var remaining = path.ToList();
        while (remaining.RemoveAll(edge =>
                   reachedNodes.Contains(edge.StartNodeIndex) ||
                   reachedNodes.Contains(edge.EndNodeIndex)) is var removed && removed > 0)
        {
            foreach (var edge in path.Where(edge =>
                         reachedNodes.Contains(edge.StartNodeIndex) ||
                         reachedNodes.Contains(edge.EndNodeIndex)))
            {
                reachedNodes.Add(edge.StartNodeIndex);
                reachedNodes.Add(edge.EndNodeIndex);
            }
        }

        Assert.Empty(remaining);
        Assert.DoesNotContain(reachedNodes, node => node != anchor && node < topology.BoundaryVertexCount);
        var degrees = path.SelectMany(edge => new[] { edge.StartNodeIndex, edge.EndNodeIndex })
            .GroupBy(node => node)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(1, degrees[anchor]);
        Assert.Equal(2, degrees.Values.Count(degree => degree == 1));
        Assert.All(degrees.Values, degree => Assert.InRange(degree, 1, 2));
        Assert.Equal(degrees.Count - 1, path.Length);
    }

    private static void AssertAllReflexValleysArePhysicalAndMaterialized(Pipeline pipeline)
    {
        var topology = pipeline.Geometry.Topology;
        foreach (var anchor in Enumerable.Range(0, topology.BoundaryVertexCount)
                     .Where(index => IsReflex(topology, index)))
        {
            var valley = Assert.Single(pipeline.Resolution.Edges, edge =>
                edge.StructuralRole == RoofStructuralRole.Valley &&
                edge.PhysicalBoundaryAnchorVertexIndex == anchor);
            var desired = Assert.Single(pipeline.Plan.Items, item =>
                item.LogicalKey == valley.StructuralIdentity);
            Assert.Equal(TimberElementType.ValleyRafter, desired.ElementType);
        }
    }

    private static Pipeline CreatePipeline(
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

    private static string[] StructuralKeys(Pipeline pipeline) =>
        pipeline.Plan.Items.Select(item => item.LogicalKey.ToString())
            .Order(StringComparer.Ordinal).ToArray();

    private static bool IsConvex(RoofTopology topology, int vertex) =>
        CornerTurn(topology, vertex) > 0d;

    private static bool IsReflex(RoofTopology topology, int vertex) =>
        CornerTurn(topology, vertex) < 0d;

    private static double CornerTurn(RoofTopology topology, int vertex)
    {
        var previous = topology.Nodes[
            (vertex + topology.BoundaryVertexCount - 1) % topology.BoundaryVertexCount];
        var current = topology.Nodes[vertex];
        var next = topology.Nodes[(vertex + 1) % topology.BoundaryVertexCount];
        return (current.X - previous.X) * (next.Y - current.Y) -
               (current.Y - previous.Y) * (next.X - current.X);
    }

    private static string GeometryKey(RoofFaceRafterSegment segment)
    {
        var first = PointKey(segment.PlanStart);
        var second = PointKey(segment.PlanEnd);
        return string.CompareOrdinal(first, second) <= 0
            ? first + ";" + second
            : second + ";" + first;
    }

    private static string PointKey(RoofPoint2D point) => $"{point.X:R},{point.Y:R}";

    private static string PhysicalEdgeKey(RoofPoint2D start, RoofPoint2D end)
    {
        var first = PointKey(start);
        var second = PointKey(end);
        return string.CompareOrdinal(first, second) <= 0
            ? first + ";" + second
            : second + ";" + first;
    }

    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) =>
        Assert.InRange(actual.DistanceTo(expected), 0d, 1e-6);

    private static RoofPoint2D[] MirrorX(IEnumerable<RoofPoint2D> points) =>
        points.Select(point => new RoofPoint2D(-point.X, point.Y)).ToArray();

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

    private static RoofPoint2D[] Host291AL() =>
        Points((47947.813661, 12295.184331), (47947.813661, 20038.646240), (57988.858409, 20038.646240), (57988.858409, 15403.104447), (52849.740922, 15403.104447), (52849.740922, 12295.184331));

    private static RoofPoint2D[] ActualHost291AL() =>
        Points(
            (47947.81366099819, 12295.18433052305),
            (47947.81366099819, 21478.922129281324),
            (57988.8584085473, 21478.922129281324),
            (57988.8584085473, 15403.104446947087),
            (52849.74092174883, 15403.104446947087),
            (52849.740921748824, 12295.18433052305));

    private static RoofPoint2D[] SkewedLContinuation() =>
        Points(
            (535.9508311543385, -614.8670056438385),
            (8310.79917634409, -540.4368551636287),
            (8607.299408366578, 3580.2053038404347),
            (3024.887563299801, 3431.0999022475908),
            (3190.644176020587, 8301.789694745927),
            (358.22164540096253, 8869.577687871446));

    private static RoofPoint2D[] SkewedUContinuation() =>
        Points(
            (47.96957771664938, 699.5318859813418),
            (10598.020908934073, 294.6911721465603),
            (11028.608622648106, 9681.530072997106),
            (8043.833415370357, 9417.967178261824),
            (6959.520126906931, 3725.9610330806863),
            (3097.280868700371, 3167.0296671181122),
            (2973.673720785265, 8206.962798818462),
            (-620.7920469906144, 9030.245679165351));

    private static RoofPoint2D[] ReflexHexagon() =>
        Points((0, 0), (9000, 0), (7000, 4000), (10000, 8000), (3000, 10000), (-1000, 5000));

    private static RoofPoint2D[] SplitDumbbell() =>
        Points((0, 0), (4000, 0), (4000, 1500), (8000, 1500), (8000, 0), (14000, 0),
            (14000, 8000), (8000, 8000), (8000, 3500), (4000, 3500), (4000, 6000), (0, 6000));

    private static RoofPoint2D[] ConcaveQuadrilateral() =>
        Points((0, 0), (8000, 0), (2500, 2000), (0, 6000));

    private sealed record Pipeline(
        HipRoofGeometry Geometry,
        RoofStructuralEdgeResolutionResult Resolution,
        RoofAutomaticStructuralRafterPlanResult Plan,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
