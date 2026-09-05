using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofTopologySolverTests
{
    public static IEnumerable<object[]> ConvexFixtures()
    {
        yield return ["rectangle", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) }];
        yield return ["square", Regular(4)];
        yield return ["pentagon", Regular(5)];
        yield return ["irregular pentagon", new RoofPoint2D[] { new(0, 0), new(7000, 0), new(10000, 4000), new(6000, 9000), new(-2000, 5000) }];
        yield return ["hexagon", Regular(6)];
        yield return ["irregular hexagon", new RoofPoint2D[] { new(0, 0), new(8000, 0), new(10000, 3000), new(7000, 8000), new(3000, 10000), new(-2000, 5000) }];
        yield return ["triangle", new RoofPoint2D[] { new(0, 0), new(8000, 0), new(0, 6000) }];
    }

    public static IEnumerable<object[]> ConcaveFixtures()
    {
        yield return ["L", new RoofPoint2D[] { new(0, 0), new(8000, 0), new(8000, 3000), new(3000, 3000), new(3000, 8000), new(0, 8000) }];
        yield return ["U", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000), new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000) }];
        yield return ["T", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000), new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000) }];
    }

    [Theory]
    [MemberData(nameof(ConvexFixtures))]
    public void ConvexPolygon_PartitionsIntoConnectedPlanarFacesWithCorrectPitch(string name, RoofPoint2D[] polygon)
    {
        var topology = Solve(polygon);
        AssertGraphAndGeometry(topology, polygon, name);
        Assert.Equal(polygon.Length, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip));
        Assert.DoesNotContain(topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Valley);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void SymmetricSimultaneousEvents_HaveOneNodeAndNoTinyInternalEdges(int count)
    {
        foreach (var angle in new[] { 0d, 17d, 90d, 143d })
        {
            var polygon = Transform(Regular(count), angle, 1200, -3400);
            var topology = Solve(polygon);
            var apex = Assert.Single(topology.Nodes.Skip(count));
            AssertPoint(new(1200, -3400, 5000 * Math.Cos(Math.PI / count) * Math.Tan(Math.PI / 6)), apex);
            Assert.Equal(count, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip));
            Assert.DoesNotContain(topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Ridge);
            Assert.All(topology.Faces, face => Assert.Equal(3, face.BoundaryNodeIndices.Count));
            AssertGraphAndGeometry(topology, polygon, "symmetric");
        }
    }

    [Theory]
    [MemberData(nameof(ConvexFixtures))]
    public void WindingAndCyclicStarts_ProduceIdenticalIndexedGraphAndSignature(string name, RoofPoint2D[] polygon)
    {
        var transformed = Transform(polygon, 37, 325, -725);
        var baseline = Solve(transformed);
        for (var start = 0; start < polygon.Length; start++)
        {
            foreach (var reverse in new[] { false, true })
            {
                var ordered = reverse ? transformed.Reverse().ToArray() : transformed;
                var variant = Solve(Enumerable.Range(0, ordered.Length).Select(i => ordered[(i + start) % ordered.Length]).ToArray());
                Assert.Equal(baseline.Signature, variant.Signature);
                Assert.Equal(baseline.Nodes, variant.Nodes);
                Assert.Equal(Connectivity(baseline), Connectivity(variant));
            }
        }
        AssertGraphAndGeometry(baseline, transformed, name);
    }

    [Theory]
    [MemberData(nameof(ConvexFixtures))]
    public void RotationAndTranslation_PreservePhysicalGraphAndFaceAssociations(string name, RoofPoint2D[] polygon)
    {
        var baseline = Solve(polygon);
        foreach (var angle in new[] { 0d, 37d, 90d, 180d, 273d })
        {
            var moved = Transform(polygon, angle, 125000, -225000);
            var actual = Solve(moved);
            Assert.Equal(baseline.Nodes.Count, actual.Nodes.Count);
            Assert.Equal(baseline.Edges.Count, actual.Edges.Count);
            var mapped = baseline.Nodes.Select(point =>
            {
                var plan = Transform([new(point.X, point.Y)], angle, 125000, -225000)[0];
                var expected = new RoofPoint3D(plan.X, plan.Y, point.Z);
                return Enumerable.Range(0, actual.Nodes.Count).Single(index => actual.Nodes[index].DistanceTo(expected) < 1e-6);
            }).ToArray();
            foreach (var edge in baseline.Edges)
            {
                var matches = actual.Edges.Where(candidate =>
                    new[] { candidate.StartNodeIndex, candidate.EndNodeIndex }.Order().SequenceEqual(
                        new[] { mapped[edge.StartNodeIndex], mapped[edge.EndNodeIndex] }.Order()));
                Assert.Equal(edge.Kind, Assert.Single(matches).Kind);
            }
            foreach (var face in baseline.Faces)
            {
                var eaveNodes = face.BoundaryNodeIndices.Take(2).Select(index => mapped[index]).ToArray();
                var corresponding = Assert.Single(actual.Faces, candidate =>
                    candidate.BoundaryNodeIndices.Take(2).SequenceEqual(eaveNodes));
                Assert.Equal(face.BoundaryNodeIndices.Select(index => mapped[index]), corresponding.BoundaryNodeIndices);
            }
            AssertGraphAndGeometry(actual, moved, name);
        }
    }

    [Theory]
    [MemberData(nameof(ConcaveFixtures))]
    public void ConcaveSimplePolygons_AreValidInputsButExplicitlyRequireWavefrontBackend(string name, RoofPoint2D[] polygon)
    {
        foreach (var angle in new[] { 0d, 37d, 180d })
        {
            var moved = Transform(polygon, angle, 1250, -700);
            for (var start = 0; start < moved.Length; start++)
            {
                var ordered = Enumerable.Range(0, moved.Length).Select(i => moved[(start + i) % moved.Length]).Reverse().ToArray();
                var validated = RoofFootprintValidator.Validate(new(ordered, true));
                Assert.True(validated.IsValid, name);
                var result = RoofTopologySolver.Solve(validated.Footprint!, 30);
                Assert.False(result.IsValid);
                Assert.Null(result.Topology);
                Assert.Equal(RoofTopologyError.ConcaveWavefrontNotImplemented, result.Error);
                var hip = HipRoofGeometrySolver.Solve(new(validated.Footprint!, new(30), RoofKind.Hip));
                Assert.False(hip.IsValid);
                Assert.Null(hip.Geometry);
                Assert.Equal(SimpleGableRoofGeometryError.ConcaveWavefrontNotImplemented, hip.Error);
            }
        }
    }

    [Fact]
    public void IrregularPolygon_HasMultipleRidgesAndNoGlobalRidgeInput()
    {
        var polygon = (RoofPoint2D[])ConvexFixtures().Single(fixture => (string)fixture[0] == "irregular hexagon")[1];
        var validated = RoofFootprintValidator.Validate(new(polygon, true));
        var baseline = HipRoofGeometrySolver.Solve(new(validated.Footprint!, new(30), RoofKind.Hip));
        var geometry = Assert.IsType<HipRoofGeometry>(baseline.Geometry);
        Assert.True(geometry.Ridges.Count > 1);
        Assert.Null(geometry.Ridge);
        Assert.Null(geometry.Apex);
        Assert.Equal(polygon.Length, geometry.Faces.Count);
        foreach (var direction in new[] { Direction(1, 0), Direction(-1, 0), Direction(1, 1) })
        {
            var variant = HipRoofGeometrySolver.Solve(new(validated.Footprint!, new(30, direction), RoofKind.Hip));
            Assert.Equal(geometry.Signature, variant.Geometry!.Signature);
        }
        Assert.Equal(Solve(polygon).Signature, geometry.Topology.Signature);
    }

    [Fact]
    public void Triangle_ApexMatchesAnalyticalIncenter()
    {
        var topology = Solve([new(0, 0), new(8000, 0), new(0, 6000)]);
        AssertPoint(new(2000, 2000, 2000 * Math.Tan(Math.PI / 6)), Assert.Single(topology.Nodes.Skip(3)));
    }

    [Theory]
    [InlineData(5d)]
    [InlineData(30d)]
    [InlineData(80d)]
    public void PitchChangesOnlyHeights_NotPlanTopology(double pitch)
    {
        var polygon = (RoofPoint2D[])ConvexFixtures().Single(fixture => (string)fixture[0] == "irregular pentagon")[1];
        var baseline = Solve(polygon);
        var actual = Solve(polygon, pitch);
        Assert.Equal(Connectivity(baseline), Connectivity(actual));
        for (var i = 0; i < baseline.Nodes.Count; i++)
        {
            Assert.Equal(baseline.Nodes[i].X, actual.Nodes[i].X);
            Assert.Equal(baseline.Nodes[i].Y, actual.Nodes[i].Y);
            Assert.Equal(baseline.Nodes[i].Z / Math.Tan(Math.PI / 6) * Math.Tan(pitch * Math.PI / 180), actual.Nodes[i].Z, 7);
        }
        AssertGraphAndGeometry(actual, polygon, "pitch");
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(90d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidPitch_IsRejected(double pitch)
    {
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(Regular(5), true), pitch);
        Assert.Equal(RoofTopologyError.InvalidSlope, result.Error);
        Assert.Null(result.Topology);
    }

    public static IEnumerable<object[]> InvalidFootprints()
    {
        yield return [new RoofFootprintInput(Regular(5), false), RoofValidationError.OpenLoop];
        yield return [new RoofFootprintInput(Regular(5), true, HasCurvedSegments: true), RoofValidationError.UnsupportedCurvedSegment];
        yield return [new RoofFootprintInput(Regular(5), true, IsPlanar: false), RoofValidationError.NonPlanar];
        yield return [new RoofFootprintInput([new(0, 0), new(8000, 6000), new(0, 6000), new(8000, 0)], true), RoofValidationError.SelfIntersection];
        yield return [new RoofFootprintInput([new(0, 0), new(8000, 0), new(8000, 0), new(0, 6000)], true), RoofValidationError.DuplicateConsecutiveVertex];
        yield return [new RoofFootprintInput([new(0, 0), new(0.001, 0), new(8000, 6000), new(0, 6000)], true), RoofValidationError.ZeroLengthEdge];
        yield return [new RoofFootprintInput([new(0, 0), new(1000, 0), new(2000, 0)], true), RoofValidationError.DegenerateArea];
        yield return [new RoofFootprintInput([new(0, 0), new(4000, 0), new(8000, 0), new(8000, 6000), new(0, 6000)], true), RoofValidationError.RedundantCollinearVertex];
        yield return [new RoofFootprintInput([new(0, 0), new(4000, -1e-8), new(8000, 0), new(8000, 6000), new(0, 6000)], true), RoofValidationError.RedundantCollinearVertex];
        foreach (var coordinate in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            yield return [new RoofFootprintInput([new(coordinate, 0), new(8000, 0), new(0, 6000)], true), RoofValidationError.NonFiniteCoordinate];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidFootprints))]
    public void InvalidOuterPolygon_IsRejectedWithoutRepair(RoofFootprintInput input, RoofValidationError expected)
    {
        var result = RoofTopologySolver.Solve(input, 30);
        Assert.False(result.IsValid);
        Assert.Null(result.Topology);
        Assert.Equal(RoofTopologyError.InvalidFootprint, result.Error);
        Assert.Equal(expected, result.FootprintError);
    }

    [Fact]
    public void RepeatedClosingPoint_UsesExistingClosureContract()
    {
        var polygon = Regular(5);
        var result = RoofTopologySolver.Solve(new RoofFootprintInput([.. polygon, polygon[0]], false), 30);
        Assert.True(result.IsValid);
        Assert.Equal(Solve(polygon).Signature, result.Topology!.Signature);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(20)]
    public void NonuniformConvexPolygons_PreserveCoverageAndPlanarityAcrossAspectRatios(int count)
    {
        // Nonuniform points on an ellipse are independently guaranteed to be convex.
        var random = new Random(1234 + count);
        foreach (var ratio in new[] { 0.15d, 0.7d, 1d, 4d })
        {
            var polygon = Enumerable.Range(0, count).Select(i =>
            {
                var angle = 2 * Math.PI * (i + 0.3 * random.NextDouble()) / count;
                return new RoofPoint2D(5000 * Math.Cos(angle), 5000 * ratio * Math.Sin(angle));
            }).ToArray();
            var actual = Solve(polygon);
            AssertGraphAndGeometry(actual, polygon, "ellipse");
        }
    }

    [Theory]
    [InlineData(0.01d)]
    [InlineData(0.00001d)]
    public void NearCollinearButDistinctSourcePlanes_AreHandledAboveValidationTolerance(double deviation)
    {
        RoofPoint2D[] polygon = [new(0, 0), new(4000, -deviation), new(8000, 0), new(8000, 6000), new(0, 6000)];
        AssertGraphAndGeometry(Solve(polygon), polygon, "near collinear");
    }

    [Fact]
    public void LargeWorldTranslation_UsesLocalValidationAndPreservesHeights()
    {
        var polygon = (RoofPoint2D[])ConvexFixtures().Single(fixture => (string)fixture[0] == "irregular pentagon")[1];
        var baseline = Solve(polygon);
        var actual = Solve(Transform(polygon, 0, 1e10, -1e10));
        Assert.Equal(Connectivity(baseline), Connectivity(actual));
        for (var i = 0; i < baseline.Nodes.Count; i++)
        {
            Assert.InRange(Math.Abs(baseline.Nodes[i].X + 1e10 - actual.Nodes[i].X), 0, 2e-6);
            Assert.InRange(Math.Abs(baseline.Nodes[i].Y - 1e10 - actual.Nodes[i].Y), 0, 2e-6);
            Assert.Equal(baseline.Nodes[i].Z, actual.Nodes[i].Z, 7);
        }
    }

    [Fact]
    public void Signature_IsCultureInvariantButTracksPhysicalPlacementAndPitch()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var polygon = Regular(6);
            var baseline = Solve(polygon);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sk-SK");
            Assert.Equal(baseline.Signature, Solve(polygon).Signature);
            Assert.NotEqual(baseline.Signature, Solve(Transform(polygon, 0, 10, 20)).Signature);
            Assert.NotEqual(baseline.Signature, Solve(polygon, 35).Signature);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void OutputCollections_AreImmutableAndIndependentOfInputArray()
    {
        var input = Regular(5);
        var topology = Solve(input);
        var signature = topology.Signature;
        input[0] = new(double.NaN, double.NaN);
        Assert.Equal(signature, topology.Signature);
        Assert.Throws<NotSupportedException>(() => ((IList<RoofPoint3D>)topology.Nodes)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<RoofTopologyEdge>)topology.Edges).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)topology.Faces[0].BoundaryNodeIndices).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)topology.Edges[0].FaceIndices).Clear());
    }

    [Fact]
    public void FiniteButOverflowingInput_DoesNotYieldPartialTopology()
    {
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(
            [new(0, 0), new(1e200, 0), new(1e200, 1e200), new(0, 1e200)], true), 30);
        Assert.False(result.IsValid);
        Assert.Null(result.Topology);
        Assert.Equal(RoofTopologyError.NonFiniteGeometry, result.Error);
    }

    private static void AssertGraphAndGeometry(RoofTopology topology, RoofPoint2D[] polygon, string name)
    {
        var n = polygon.Length;
        Assert.Equal(n, topology.Faces.Count);
        Assert.Equal(n, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Eave));
        Assert.Equal(1, topology.Nodes.Count - topology.Edges.Count + topology.Faces.Count);
        Assert.All(topology.Nodes, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z), name));
        var incidence = new Dictionary<(int, int), List<int>>();
        var area = 0d;
        foreach (var face in topology.Faces)
        {
            var cycle = face.BoundaryNodeIndices;
            Assert.Equal(cycle.Count, cycle.Distinct().Count());
            Assert.True(cycle.Count >= 3);
            Assert.Equal(face.SourceEdgeIndex, cycle[0]);
            Assert.Equal((face.SourceEdgeIndex + 1) % n, cycle[1]);
            var a = topology.Nodes[cycle[0]];
            var b = topology.Nodes[cycle[1]];
            var eaveLength = a.DistanceTo(b);
            foreach (var index in cycle)
            {
                var p = topology.Nodes[index];
                var run = Cross(a, b, p) / eaveLength;
                Assert.InRange(Math.Abs(run * Math.Tan(topology.PitchDegrees * Math.PI / 180) - p.Z), 0d, 1e-6);
            }
            for (var i = 1; i < cycle.Count - 1; i++)
            {
                var triangleArea = Cross(a, topology.Nodes[cycle[i]], topology.Nodes[cycle[i + 1]]) / 2;
                Assert.True(triangleArea > 0, name);
                area += triangleArea;
            }
            for (var i = 0; i < cycle.Count; i++)
            {
                var first = cycle[i];
                var second = cycle[(i + 1) % cycle.Count];
                var key = (Math.Min(first, second), Math.Max(first, second));
                if (!incidence.TryGetValue(key, out var uses)) incidence[key] = uses = [];
                uses.Add(face.SourceEdgeIndex);
            }
        }
        var polygonArea = Math.Abs(Enumerable.Range(1, n - 2).Sum(i =>
            (polygon[i].X - polygon[0].X) * (polygon[i + 1].Y - polygon[0].Y) -
            (polygon[i].Y - polygon[0].Y) * (polygon[i + 1].X - polygon[0].X))) / 2;
        Assert.InRange(Math.Abs(area - polygonArea), 0, Math.Max(1e-5, polygonArea * 1e-12));
        Assert.Equal(incidence.Count, topology.Edges.Count);
        foreach (var edge in topology.Edges)
        {
            var key = (Math.Min(edge.StartNodeIndex, edge.EndNodeIndex), Math.Max(edge.StartNodeIndex, edge.EndNodeIndex));
            Assert.Equal(incidence[key].Order(), edge.FaceIndices);
            Assert.Equal(edge.Kind == RoofTopologyEdgeKind.Eave ? 1 : 2, edge.FaceIndices.Count);
            Assert.True(topology.Segment(edge).LengthMm > 1e-6);
            if (edge.Kind == RoofTopologyEdgeKind.Hip)
            {
                Assert.True(edge.StartNodeIndex < n && edge.EndNodeIndex >= n);
            }
            if (edge.Kind == RoofTopologyEdgeKind.Ridge)
            {
                Assert.True(edge.StartNodeIndex >= n && edge.EndNodeIndex >= n);
            }
        }
        // Interior segments may meet only at a shared graph node; index counts
        // alone would not detect crossed edges in a falsely assembled tree.
        for (var i = 0; i < topology.Edges.Count; i++)
        {
            var first = topology.Edges[i];
            var a = topology.Nodes[first.StartNodeIndex];
            var b = topology.Nodes[first.EndNodeIndex];
            for (var j = i + 1; j < topology.Edges.Count; j++)
            {
                var second = topology.Edges[j];
                if (new[] { first.StartNodeIndex, first.EndNodeIndex }.Intersect(
                    new[] { second.StartNodeIndex, second.EndNodeIndex }).Any()) continue;
                var c = topology.Nodes[second.StartNodeIndex];
                var d = topology.Nodes[second.EndNodeIndex];
                var firstSide = Cross(a, b, c) * Cross(a, b, d);
                var secondSide = Cross(c, d, a) * Cross(c, d, b);
                Assert.False(firstSide < -1e-10 && secondSide < -1e-10, "Crossed topology edges");
            }
        }
        var skeleton = topology.Edges.Where(edge => edge.Kind != RoofTopologyEdgeKind.Eave).ToArray();
        Assert.Equal(topology.Nodes.Count - 1, skeleton.Length);
        for (var i = 0; i < topology.Nodes.Count; i++)
        {
            var degree = skeleton.Count(edge => edge.StartNodeIndex == i || edge.EndNodeIndex == i);
            if (i < n) Assert.Equal(1, degree);
            else Assert.True(degree >= 3);
        }
        var seen = new HashSet<int> { 0 };
        for (var i = 0; i < topology.Nodes.Count; i++)
        {
            foreach (var edge in skeleton)
            {
                if (seen.Contains(edge.StartNodeIndex)) seen.Add(edge.EndNodeIndex);
                if (seen.Contains(edge.EndNodeIndex)) seen.Add(edge.StartNodeIndex);
            }
        }
        Assert.Equal(topology.Nodes.Count, seen.Count);
    }

    private static string Connectivity(RoofTopology topology) => string.Join(";", topology.Edges.Select(edge =>
        $"{edge.StartNodeIndex},{edge.EndNodeIndex},{edge.Kind}:{string.Join(',', edge.FaceIndices)}"));
    private static double Cross(RoofPoint3D a, RoofPoint3D b, RoofPoint3D c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) => Assert.InRange(expected.DistanceTo(actual), 0d, 1e-7d);
    private static RoofTopology Solve(RoofPoint2D[] polygon, double pitch = 30)
    {
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(polygon, true), pitch);
        Assert.True(result.IsValid, $"{result.Error}: {result.FootprintError}");
        return result.Topology!;
    }
    private static RoofPoint2D[] Regular(int count) => Enumerable.Range(0, count).Select(i =>
        new RoofPoint2D(5000 * Math.Cos(2 * Math.PI * i / count), 5000 * Math.Sin(2 * Math.PI * i / count))).ToArray();
    private static RoofPoint2D[] Transform(RoofPoint2D[] polygon, double degrees, double dx, double dy)
    {
        var radians = degrees * Math.PI / 180;
        return polygon.Select(point => new RoofPoint2D(dx + point.X * Math.Cos(radians) - point.Y * Math.Sin(radians),
            dy + point.X * Math.Sin(radians) + point.Y * Math.Cos(radians))).ToArray();
    }
    private static RoofDirection2D Direction(double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return direction;
    }
}
