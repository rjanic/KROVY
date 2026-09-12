using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralEdgeIdentityResolverTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return ["rectangle", new RoofPoint2D[] { P(0, 0), P(10000, 0), P(10000, 6000), P(0, 6000) }];
        yield return ["square", new RoofPoint2D[] { P(0, 0), P(6000, 0), P(6000, 6000), P(0, 6000) }];
        yield return ["convex-irregular", new RoofPoint2D[] { P(0, 0), P(7000, 0), P(10000, 4000), P(6000, 9000), P(-2000, 5000) }];
        yield return ["L", new RoofPoint2D[] { P(0, 0), P(8000, 0), P(8000, 3000), P(3000, 3000), P(3000, 8000), P(0, 8000) }];
        yield return ["U", new RoofPoint2D[] { P(0, 0), P(10000, 0), P(10000, 9000), P(7000, 9000), P(7000, 3000), P(3000, 3000), P(3000, 9000), P(0, 9000) }];
        yield return ["T", new RoofPoint2D[] { P(0, 0), P(10000, 0), P(10000, 3000), P(6500, 3000), P(6500, 9000), P(3500, 9000), P(3500, 3000), P(0, 3000) }];
        yield return ["stepped", new RoofPoint2D[] { P(0, 0), P(11000, 0), P(11000, 2500), P(8000, 2500), P(8000, 5000), P(5000, 5000), P(5000, 8500), P(0, 8500) }];
        yield return ["reflex-hexagon", new RoofPoint2D[] { P(0, 0), P(9000, 0), P(7000, 4000), P(10000, 8000), P(3000, 10000), P(-1000, 5000) }];
        yield return ["host-291A", Host291AConcave()];
        yield return ["split-dumbbell", new RoofPoint2D[] { P(0, 0), P(4000, 0), P(4000, 1500), P(8000, 1500), P(8000, 0), P(14000, 0), P(14000, 8000), P(8000, 8000), P(8000, 3500), P(4000, 3500), P(4000, 6000), P(0, 6000) }];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SupportedFixture_ResolvesEveryAndOnlyStructuralEdge(
        string name,
        RoofPoint2D[] points)
    {
        var solved = Resolve(points);
        Assert.True(solved.Resolution.IsValid, name + ": " + solved.Resolution.Error);
        Assert.Equal(
            solved.Geometry.Topology.Edges.Count(IsStructural),
            solved.Resolution.Edges.Count);
        Assert.Equal(
            solved.Resolution.Edges.Count,
            solved.Resolution.Edges.Select(edge => edge.StructuralIdentity).Distinct().Count());
        Assert.All(solved.Resolution.Edges, edge =>
        {
            Assert.True(edge.BoundaryEdgeIdA > 0);
            Assert.True(edge.BoundaryEdgeIdA < edge.BoundaryEdgeIdB);
            Assert.True(edge.Length3dMm > 0d);
            Assert.True(IsStructural(solved.Geometry.Topology.Edges[edge.TopologyEdgeIndex]));
        });
    }

    [Theory]
    [InlineData("rectangle", 1, 4, 0)]
    [InlineData("square", 0, 4, 0)]
    [InlineData("convex-irregular", 2, 5, 0)]
    [InlineData("L", 2, 5, 1)]
    [InlineData("U", 3, 6, 2)]
    [InlineData("T", 3, 6, 2)]
    [InlineData("stepped", 4, 6, 2)]
    [InlineData("reflex-hexagon", 2, 6, 1)]
    [InlineData("host-291A", 2, 6, 1)]
    [InlineData("split-dumbbell", 4, 9, 4)]
    public void SupportedFixtures_HaveExpectedRoleCounts(
        string name,
        int ridge,
        int hip,
        int valley)
    {
        var result = Resolve((RoofPoint2D[])Fixtures().Single(item =>
            (string)item[0] == name)[1]).Resolution;

        AssertRoleCounts(result, ridge, hip, valley);
    }

    [Fact]
    public void EavesAndCoplanarSeams_AreExplicitlyIgnored()
    {
        var solved = Resolve((RoofPoint2D[])Fixtures().Single(item =>
            (string)item[0] == "split-dumbbell")[1]);

        Assert.Contains(solved.Geometry.Topology.Edges, edge =>
            edge.Kind == RoofTopologyEdgeKind.CoplanarSeam);
        Assert.DoesNotContain(solved.Resolution.Edges, edge =>
            solved.Geometry.Topology.Edges[edge.TopologyEdgeIndex].Kind is
                RoofTopologyEdgeKind.Eave or RoofTopologyEdgeKind.CoplanarSeam);
    }

    [Fact]
    public void HipAndValleyLengths_AreTrueSloped3dLengths()
    {
        var solved = Resolve((RoofPoint2D[])Fixtures().Single(item =>
            (string)item[0] == "L")[1]);

        Assert.Contains(solved.Resolution.Edges, edge =>
            edge.StructuralRole == RoofStructuralRole.Valley);
        foreach (var edge in solved.Resolution.Edges)
        {
            var deltaX = edge.Segment3D.End.X - edge.Segment3D.Start.X;
            var deltaY = edge.Segment3D.End.Y - edge.Segment3D.Start.Y;
            var planLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (edge.StructuralRole is RoofStructuralRole.Hip or RoofStructuralRole.Valley)
            {
                Assert.True(edge.Length3dMm > planLength);
            }
            else
            {
                Assert.Equal(planLength, edge.Length3dMm, 7);
            }
        }
    }

    [Fact]
    public void PhysicalBoundaryIds_KeepStructuralKeySetAcrossCanonicalPermutations()
    {
        var points = (RoofPoint2D[])Fixtures().Single(item =>
            (string)item[0] == "L")[1];
        var count = points.Length;
        var baselineOrder = Enumerable.Range(0, count).ToArray();
        var variants = new[]
        {
            ResolveVariant(points, baselineOrder, 0d),
            ResolveVariant(points, Enumerable.Range(0, count).Select(i => (i + 2) % count).ToArray(), 0d),
            ResolveVariant(points, new[] { 0 }.Concat(Enumerable.Range(1, count - 1).Reverse()).ToArray(), 0d),
            ResolveVariant(points, baselineOrder, 90d),
            ResolveVariant(points, Enumerable.Range(0, count).Select(i => (i + 3) % count).ToArray(), 90d),
        };

        var expected = Keys(variants[0].Resolution);
        Assert.All(variants, variant => Assert.Equal(expected, Keys(variant.Resolution)));
        var baselineIndexes = variants[0].Resolution.Edges.ToDictionary(
            edge => edge.StructuralIdentity,
            edge => edge.TopologyEdgeIndex);
        Assert.Contains(variants[3].Resolution.Edges, edge =>
            baselineIndexes[edge.StructuralIdentity] != edge.TopologyEdgeIndex);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SupportedFixture_KeepsIdentitySetAndCountAcrossCanonicalPermutations(
        string name,
        RoofPoint2D[] points)
    {
        var count = points.Length;
        var baselineOrder = Enumerable.Range(0, count).ToArray();
        var variants = new[]
        {
            ResolveVariant(points, baselineOrder, 0d),
            ResolveVariant(
                points,
                Enumerable.Range(0, count).Select(index => (index + 2) % count).ToArray(),
                0d),
            ResolveVariant(
                points,
                new[] { 0 }.Concat(Enumerable.Range(1, count - 1).Reverse()).ToArray(),
                0d),
            ResolveVariant(points, baselineOrder, 90d),
            ResolveVariant(points, baselineOrder, 37d),
        };

        var expectedKeys = Keys(variants[0].Resolution);
        Assert.All(variants, variant =>
        {
            Assert.True(variant.Resolution.IsValid, name + ": " + variant.Resolution.Error);
            Assert.Equal(expectedKeys.Length, variant.Resolution.Edges.Count);
            Assert.Equal(expectedKeys, Keys(variant.Resolution));
        });
    }

    [Fact]
    public void IncompleteBoundaryProvenance_FailsAtomically()
    {
        var solved = Resolve((RoofPoint2D[])Fixtures().First()[1]);
        var incomplete = solved.Provenance with
        {
            EdgeProvenance = solved.Provenance.EdgeProvenance.Skip(1).ToArray(),
        };

        var result = RoofStructuralEdgeIdentityResolver.Resolve(
            solved.Geometry,
            incomplete);

        Assert.False(result.IsValid);
        Assert.Empty(result.Edges);
        Assert.Equal(
            RoofStructuralEdgeResolutionError.BoundaryProvenanceCountMismatch,
            result.Error);
    }

    [Fact]
    public void AmbiguousStructuralKeys_FailAtomicallyWithoutMerging()
    {
        var solved = Resolve((RoofPoint2D[])Fixtures().Single(item =>
            (string)item[0] == "square")[1]);
        var ambiguous = solved.Provenance with
        {
            EdgeProvenance = solved.Provenance.EdgeProvenance
                .Select((edge, index) => edge with { BoundaryEdgeId = index % 2 + 1 })
                .ToArray(),
        };

        var result = RoofStructuralEdgeIdentityResolver.Resolve(
            solved.Geometry,
            ambiguous);

        Assert.False(result.IsValid);
        Assert.Empty(result.Edges);
        Assert.Equal(
            RoofStructuralEdgeResolutionError.DuplicateStructuralIdentity,
            result.Error);
        Assert.Equal("Hip|1|2", result.DuplicateIdentity!.ToString());
    }

    [Fact]
    public void Host291AFormerInternalRidgePair_IsNowUniqueResolvedHipIdentity()
    {
        var solved = Resolve(Host291AConcave());
        var topology = solved.Geometry.Topology;
        var correctedTopologyEdge = Assert.Single(topology.Edges.Select((edge, index) => (edge, index)), item =>
            item.edge.StartNodeIndex >= topology.BoundaryVertexCount &&
            item.edge.EndNodeIndex >= topology.BoundaryVertexCount &&
            item.edge.FaceIndices.SequenceEqual(new[] { 1, 4 }));
        var corrected = Assert.Single(solved.Resolution.Edges, edge =>
            edge.TopologyEdgeIndex == correctedTopologyEdge.index);

        Assert.Equal(RoofTopologyEdgeKind.Hip, correctedTopologyEdge.edge.Kind);
        Assert.Equal(RoofStructuralRole.Hip, corrected.StructuralRole);
        Assert.DoesNotContain(solved.Resolution.Edges, edge =>
            edge.StructuralRole == RoofStructuralRole.Ridge &&
            edge.BoundaryEdgeIdA == corrected.BoundaryEdgeIdA &&
            edge.BoundaryEdgeIdB == corrected.BoundaryEdgeIdB);
        Assert.Equal(solved.Resolution.Edges.Count,
            solved.Resolution.Edges.Select(edge => edge.StructuralIdentity).Distinct().Count());
    }

    private static SolvedFixture Resolve(
        RoofPoint2D[] points,
        IReadOnlyList<int>? boundaryIds = null)
    {
        var input = new RoofFootprintInput(points, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var ids = boundaryIds ?? Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray();
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            ids).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometryResult = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30),
            RoofKind.Hip));
        Assert.True(geometryResult.IsValid, geometryResult.Error.ToString());
        var geometry = Assert.IsType<HipRoofGeometry>(geometryResult.Geometry);
        return new SolvedFixture(
            geometry,
            provenance,
            RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance));
    }

    private static SolvedFixture ResolveVariant(
        IReadOnlyList<RoofPoint2D> physicalPoints,
        IReadOnlyList<int> rawOrder,
        double rotationDegrees)
    {
        var angle = rotationDegrees * Math.PI / 180d;
        var transformed = rawOrder.Select(index =>
        {
            var point = physicalPoints[index];
            return new RoofPoint2D(
                point.X * Math.Cos(angle) - point.Y * Math.Sin(angle),
                point.X * Math.Sin(angle) + point.Y * Math.Cos(angle));
        }).ToArray();
        var ids = Enumerable.Range(0, rawOrder.Count)
            .Select(index => PhysicalEdgeId(
                rawOrder[index],
                rawOrder[(index + 1) % rawOrder.Count],
                physicalPoints.Count))
            .ToArray();
        return Resolve(transformed, ids);
    }

    private static int PhysicalEdgeId(int first, int second, int count)
    {
        if ((first + 1) % count == second)
        {
            return first + 1;
        }

        if ((second + 1) % count == first)
        {
            return second + 1;
        }

        throw new InvalidOperationException("Raw order is not a polygon boundary cycle.");
    }

    private static bool IsStructural(RoofTopologyEdge edge) => edge.Kind is
        RoofTopologyEdgeKind.Ridge or RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley;

    private static string[] Keys(RoofStructuralEdgeResolutionResult result)
    {
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Edges.Select(edge => edge.StructuralIdentity.ToString()).Order().ToArray();
    }

    private static void AssertRoleCounts(
        RoofStructuralEdgeResolutionResult result,
        int ridge,
        int hip,
        int valley)
    {
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(ridge, result.Edges.Count(edge => edge.StructuralRole == RoofStructuralRole.Ridge));
        Assert.Equal(hip, result.Edges.Count(edge => edge.StructuralRole == RoofStructuralRole.Hip));
        Assert.Equal(valley, result.Edges.Count(edge => edge.StructuralRole == RoofStructuralRole.Valley));
    }

    private static RoofPoint2D P(double x, double y) => new(x, y);

    private static RoofPoint2D[] Host291AConcave() =>
    [
        P(47947.813661, 12295.184331),
        P(47947.813661, 20038.646240),
        P(57988.858409, 20038.646240),
        P(57988.858409, 15403.104447),
        P(52849.740922, 15403.104447),
        P(52849.740922, 12295.184331),
    ];

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance,
        RoofStructuralEdgeResolutionResult Resolution);
}
