using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ConcaveRoofTopologySolverTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var fixture in RoofTopologySolverTests.ConcaveFixtures()) yield return fixture;
        yield return ["asymmetric U", new RoofPoint2D[] { new(0, 0), new(12000, 0), new(12000, 9500), new(8500, 9500), new(8500, 4000), new(2500, 4000), new(2500, 8000), new(0, 8000) }];
        yield return ["asymmetric T", new RoofPoint2D[] { new(0, 0), new(12000, 0), new(12000, 2500), new(8500, 2500), new(8500, 11000), new(3500, 11000), new(3500, 2500), new(0, 2500) }];
        yield return ["reflex hexagon", new RoofPoint2D[] { new(0, 0), new(9000, 0), new(7000, 4000), new(10000, 8000), new(3000, 10000), new(-1000, 5000) }];
        yield return ["HOST 291A concave", Host291AConcave()];
        yield return ["stepped", new RoofPoint2D[] { new(0, 0), new(11000, 0), new(11000, 2500), new(8000, 2500), new(8000, 5000), new(5000, 5000), new(5000, 8500), new(0, 8500) }];
        yield return ["shallow reflex", new RoofPoint2D[] { new(0, 0), new(4000, 0.01), new(8000, 0), new(8500, 6000), new(-1000, 7000) }];
        yield return ["narrow notch", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 9000), new(5001, 9000), new(5001, 4500), new(4999, 4500), new(4999, 9000), new(0, 9000) }];
        yield return ["split dumbbell", new RoofPoint2D[] { new(0, 0), new(4000, 0), new(4000, 1500), new(8000, 1500), new(8000, 0), new(14000, 0), new(14000, 8000), new(8000, 8000), new(8000, 3500), new(4000, 3500), new(4000, 6000), new(0, 6000) }];
        yield return ["concave quadrilateral", new RoofPoint2D[] { new(0, 0), new(8000, 0), new(2500, 2000), new(0, 6000) }];
        yield return ["five reflex vertices", Enumerable.Range(0,10).Select(i => new RoofPoint2D(
            (i % 2 == 0 ? 7000 : 3000) * Math.Cos(i * Math.PI / 5),
            (i % 2 == 0 ? 7000 : 3000) * Math.Sin(i * Math.PI / 5))).ToArray()];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ConcavePolygon_HasCompletePlanarPartitionAndValidSkeleton(string name, RoofPoint2D[] polygon)
    {
        var topology = Solve(polygon, name);
        AssertGeometry(topology);
        Assert.Contains(topology.Edges, e => e.Kind == RoofTopologyEdgeKind.Valley);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EquivalentWindingStartsAndTransforms_PreserveOwnershipAndGeometry(string name, RoofPoint2D[] polygon)
    {
        var baseline = Solve(polygon, name);
        foreach (var angle in new[] { 0d, 37d, 90d, 180d, 273d })
        {
            var transformed = Transform(polygon, angle, 125000, -225000);
            var expected = Solve(transformed, name + " rotated " + angle);
            AssertGeometry(expected);
            Assert.Equal(baseline.Nodes.Count, expected.Nodes.Count);
            var mapped = baseline.Nodes.Select(p =>
            {
                var xy = Transform(new[] { new RoofPoint2D(p.X, p.Y) }, angle, 125000, -225000)[0];
                return Enumerable.Range(0, expected.Nodes.Count).Single(i =>
                    expected.Nodes[i].DistanceTo(new(xy.X, xy.Y, p.Z)) < 1e-6);
            }).ToArray();
            foreach (var edge in baseline.Edges)
            {
                var corresponding = Assert.Single(expected.Edges, e =>
                    new[] { e.StartNodeIndex, e.EndNodeIndex }.Order().SequenceEqual(
                        new[] { mapped[edge.StartNodeIndex], mapped[edge.EndNodeIndex] }.Order()));
                Assert.Equal(edge.Kind, corresponding.Kind);
            }
            foreach (var face in baseline.Faces)
            {
                var corresponding = Assert.Single(expected.Faces, f => f.BoundaryNodeIndices[0] == mapped[face.SourceEdgeIndex]);
                Assert.Equal(face.BoundaryNodeIndices.Select(i => mapped[i]), corresponding.BoundaryNodeIndices);
            }
            for (var start = 0; start < transformed.Length; start++)
                foreach (var reverse in new[] { false, true })
                {
                    var points = reverse ? transformed.Reverse().ToArray() : transformed;
                    var shifted = Enumerable.Range(0, points.Length).Select(i => points[(start + i) % points.Length]).ToArray();
                    Assert.Equal(expected.Signature, Solve(shifted, name).Signature);
                }
        }
    }

    [Fact]
    public void EqualWidthL_HasAnalyticalSimultaneousRidgesAndReflexValley()
    {
        var points = (RoofPoint2D[])RoofTopologySolverTests.ConcaveFixtures().First()[1];
        var topology = Solve(points, "L oracle");
        var expected = new[] { new RoofPoint3D(1500,1500,1500*Math.Tan(Math.PI/6)),
            new RoofPoint3D(6500,1500,1500*Math.Tan(Math.PI/6)), new RoofPoint3D(1500,6500,1500*Math.Tan(Math.PI/6)) };
        Assert.Equal(expected.Length, topology.Nodes.Count - points.Length);
        foreach (var point in expected) Assert.Single(topology.Nodes, p => p.DistanceTo(point) < 1e-7);
        var valley = Assert.Single(topology.Edges, e => e.Kind == RoofTopologyEdgeKind.Valley);
        Assert.Equal(new RoofPoint3D(3000, 3000, 0), topology.Nodes[valley.StartNodeIndex]);
        Assert.InRange(topology.Nodes[valley.EndNodeIndex].DistanceTo(expected[0]), 0, 1e-7);
        Assert.Equal(2, topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Ridge));
        AssertGeometry(topology);
    }

    [Theory]
    [InlineData("U")]
    [InlineData("T")]
    public void SymmetricUAndT_MatchAnalyticalOffsetsAndMultipleContactDegrees(string name)
    {
        var polygon = (RoofPoint2D[])RoofTopologySolverTests.ConcaveFixtures().Single(f => (string)f[0] == name)[1];
        var expected = name == "U" ? new[] { (1500d, 1500d), (8500d, 1500d), (1500d, 7500d), (8500d, 7500d) } :
            new[] { (1500d, 1500d), (5000d, 1500d), (8500d, 1500d), (5000d, 7500d) };
        var topology = Solve(polygon, name);
        Assert.Equal(expected.Length, topology.Nodes.Count - polygon.Length);
        foreach (var (x, y) in expected)
            Assert.Single(topology.Nodes, p => p.DistanceTo(new(x, y, 1500 * Math.Tan(Math.PI / 6))) < 1e-7);
        Assert.Equal(2, topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Valley));
        Assert.Equal(3, topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Ridge));
        if (name == "T")
        {
            var center = Enumerable.Range(0, topology.Nodes.Count).Single(i =>
                topology.Nodes[i].DistanceTo(new(5000, 1500, 1500 * Math.Tan(Math.PI / 6))) < 1e-7);
            Assert.Equal(5, topology.Edges.Count(e => e.StartNodeIndex == center || e.EndNodeIndex == center));
        }
        AssertGeometry(topology);
    }

    [Fact]
    public void SplitProducesIndependentSurvivingLoops_AndCoplanarOwnershipSeams()
    {
        var polygon = (RoofPoint2D[])Fixtures().Single(f => (string)f[0] == "split dumbbell")[1];
        var topology = Solve(polygon, "split");
        var tangent = Math.Tan(Math.PI / 6);
        foreach (var (x, y, time) in new[] { (3000d,2500d,1000d), (9000d,2500d,1000d),
            (2000d,2000d,2000d), (2000d,4000d,2000d), (11000d,3000d,3000d), (11000d,5000d,3000d) })
            Assert.Single(topology.Nodes, p => p.DistanceTo(new(x, y, time * tangent)) < 1e-7);
        Assert.Contains(topology.Edges, e => e.Kind == RoofTopologyEdgeKind.CoplanarSeam);
        var footprint = RoofFootprintValidator.Validate(new(polygon, true)).Footprint!;
        var hip = Assert.IsType<HipRoofGeometry>(HipRoofGeometrySolver.Solve(new(footprint, new(30), RoofKind.Hip)).Geometry);
        Assert.All(hip.Faces, f => Assert.Equal(30, f.SlopeDegrees, 8));
        Assert.Equal(topology.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Ridge), hip.Ridges.Count);
        AssertGeometry(topology);
    }

    [Fact]
    public void Host291AExplicitClosingPoint_HasCorrectProvenanceRolesAndFortyFourOrdinaryRafters()
    {
        var unique = Host291AConcave();
        var raw = unique.Concat([unique[0]]).ToArray();
        var input = new RoofFootprintInput(raw, IsClosed: false);
        var validation = RoofFootprintValidator.Validate(input);

        Assert.Equal(7, raw.Length);
        Assert.True(RoofFootprintValidator.HasRepeatedClosingVertex(raw));
        Assert.True(validation.IsValid);
        Assert.Equal(6, validation.Footprint!.Vertices.Count);

        var result = RoofTopologySolver.Solve(input, 30d);
        Assert.True(result.IsValid, result.Error.ToString());
        var topology = Assert.IsType<RoofTopology>(result.Topology);
        Assert.Equal(2, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge));
        Assert.Equal(6, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip));
        Assert.Single(topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Valley);

        var corrected = Assert.Single(topology.Edges, edge =>
            edge.StartNodeIndex >= topology.BoundaryVertexCount &&
            edge.EndNodeIndex >= topology.BoundaryVertexCount &&
            edge.FaceIndices.SequenceEqual(new[] { 1, 4 }));
        Assert.Equal(RoofTopologyEdgeKind.Hip, corrected.Kind);
        Assert.Equal(4, corrected.OriginatingBoundaryVertexIndex);
        Assert.InRange(topology.Segment(corrected).LengthMm, 203.455d, 203.456d);

        Assert.Equal(topology.Edges.Count, topology.Edges.Select(edge =>
            (Math.Min(edge.StartNodeIndex, edge.EndNodeIndex),
             Math.Max(edge.StartNodeIndex, edge.EndNodeIndex))).Distinct().Count());
        Assert.DoesNotContain(topology.Edges, edge => topology.Segment(edge).LengthMm <= 1e-6);
        Assert.All(topology.Edges.Where(edge => edge.Kind != RoofTopologyEdgeKind.Eave), edge =>
            Assert.Contains(edge.Kind, new[]
            {
                RoofTopologyEdgeKind.Hip,
                RoofTopologyEdgeKind.Ridge,
                RoofTopologyEdgeKind.Valley,
                RoofTopologyEdgeKind.CoplanarSeam,
            }));

        var layout = RoofFaceRafterLayoutService.Create(topology, 900d);
        Assert.True(layout.IsValid, layout.Error.ToString());
        Assert.Equal(44, layout.Layout!.Segments.Count);
    }

    [Fact]
    public void ReflexHexagon_ContainsObliqueInternalHipAndObliqueRidge()
    {
        var polygon = (RoofPoint2D[])Fixtures().Single(fixture =>
            (string)fixture[0] == "reflex hexagon")[1];
        var topology = Solve(polygon, "oblique provenance");
        var internalHip = Assert.Single(topology.Edges, edge =>
            edge.Kind == RoofTopologyEdgeKind.Hip &&
            edge.StartNodeIndex >= topology.BoundaryVertexCount &&
            edge.EndNodeIndex >= topology.BoundaryVertexCount);

        AssertOblique(topology.Segment(internalHip));
        Assert.Contains(topology.Edges, edge =>
            edge.Kind == RoofTopologyEdgeKind.Ridge &&
            IsOblique(topology.Segment(edge)));
    }

    [Fact]
    public void SplitDumbbell_CompatibleConvexSuccessorIsHipWithoutDuplicatingSplitLineage()
    {
        var polygon = (RoofPoint2D[])Fixtures().Single(fixture =>
            (string)fixture[0] == "split dumbbell")[1];
        var topology = Solve(polygon, "split lineage");

        Assert.Equal(4, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Ridge));
        Assert.Equal(9, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Hip));
        Assert.Equal(4, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.Valley));
        Assert.Equal(2, topology.Edges.Count(edge => edge.Kind == RoofTopologyEdgeKind.CoplanarSeam));
        var continuedHip = Assert.Single(topology.Edges, edge =>
            edge.StartNodeIndex == 17 && edge.EndNodeIndex == 18);
        Assert.Equal(RoofTopologyEdgeKind.Hip, continuedHip.Kind);
        Assert.Equal(new[] { 4, 7 }, continuedHip.FaceIndices);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(37d)]
    [InlineData(180d)]
    public void NearSimultaneousEvents_CoalesceWithinSpatialContract(double angle)
    {
        var polygon = (RoofPoint2D[])RoofTopologySolverTests.ConcaveFixtures().Single(f => (string)f[0] == "U")[1];
        var baseline = Solve(Transform(polygon, angle, 0, 0), "symmetric");
        polygon[3] = polygon[3] with { X = polygon[3].X + 4e-7 };
        polygon[4] = polygon[4] with { X = polygon[4].X + 4e-7 };
        var moved = Transform(polygon, angle, 0, 0);
        var actual = Solve(moved, "near symmetric");
        Assert.Equal(baseline.Nodes.Count, actual.Nodes.Count);
        Assert.Equal(baseline.Edges.Count, actual.Edges.Count);
        Assert.Equal(2, actual.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Valley));
        Assert.Equal(actual.Signature, Solve(moved.Reverse().ToArray(), "near symmetric CW").Signature);
        for (var i = 0; i < actual.Nodes.Count; i++)
            for (var j = i + 1; j < actual.Nodes.Count; j++)
                Assert.True(actual.Nodes[i].DistanceTo(actual.Nodes[j]) > 1e-6);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(37d)]
    [InlineData(180d)]
    public void UnresolvableShortEventBranches_FailExplicitlyWithoutPartialOutput(double angle)
    {
        var polygon = (RoofPoint2D[])RoofTopologySolverTests.ConcaveFixtures().Single(f => (string)f[0] == "U")[1];
        polygon[3] = polygon[3] with { X = polygon[3].X + 1.2e-6 };
        polygon[4] = polygon[4] with { X = polygon[4].X + 1.2e-6 };
        var moved = Transform(polygon, angle, 0, 0);
        for (var start = 0; start < moved.Length; start++)
            foreach (var reverse in new[] { false, true })
            {
                var points = reverse ? moved.Reverse().ToArray() : moved;
                var shifted = Enumerable.Range(0, points.Length).Select(i => points[(start + i) % points.Length]).ToArray();
                Assert.True(RoofFootprintValidator.Validate(new(shifted, true)).IsValid);
                var result = RoofTopologySolver.Solve(new RoofFootprintInput(shifted, true), 30);
                Assert.False(result.IsValid);
                Assert.Null(result.Topology);
                Assert.Equal(RoofTopologyError.NumericallyUnresolvedTopology, result.Error);
            }
    }

    [Theory]
    [InlineData(5d)]
    [InlineData(80d)]
    public void UniformPitchOnlyScalesHeights(double pitch)
    {
        var polygon = (RoofPoint2D[])Fixtures().Single(f => (string)f[0] == "asymmetric U")[1];
        var baseline = Solve(polygon, "pitch baseline");
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(polygon, true), pitch);
        Assert.True(result.IsValid);
        var actual = result.Topology!;
        Assert.Equal(baseline.Edges.Select(e => (e.StartNodeIndex, e.EndNodeIndex, e.Kind)),
            actual.Edges.Select(e => (e.StartNodeIndex, e.EndNodeIndex, e.Kind)));
        for (var i = 0; i < actual.Nodes.Count; i++)
        {
            Assert.Equal(baseline.Nodes[i].X, actual.Nodes[i].X);
            Assert.Equal(baseline.Nodes[i].Y, actual.Nodes[i].Y);
            Assert.Equal(baseline.Nodes[i].Z / Math.Tan(Math.PI / 6) * Math.Tan(pitch * Math.PI / 180), actual.Nodes[i].Z, 7);
        }
        AssertGeometry(actual);
    }

    [Fact]
    public void SignatureIsCultureInvariant_AndLargeTranslationRetainsGraph()
    {
        var polygon = (RoofPoint2D[])Fixtures().Single(f => (string)f[0] == "asymmetric U")[1];
        var baseline = Solve(polygon, "culture");
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("sk-SK");
            Assert.Equal(baseline.Signature, Solve(polygon, "culture repeat").Signature);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        var translated = Solve(Transform(polygon, 0, 1e10, -1e10), "large translation");
        Assert.Equal(baseline.Edges.Select(e => (e.StartNodeIndex, e.EndNodeIndex, e.Kind)),
            translated.Edges.Select(e => (e.StartNodeIndex, e.EndNodeIndex, e.Kind)));
        for (var i = 0; i < baseline.Nodes.Count; i++)
            Assert.InRange(translated.Nodes[i].DistanceTo(new(baseline.Nodes[i].X + 1e10, baseline.Nodes[i].Y - 1e10, baseline.Nodes[i].Z)), 0, 2e-6);
    }

    private static RoofTopology Solve(RoofPoint2D[] points, string label)
    {
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(points, true), 30);
        Assert.True(result.IsValid, label + ": " + result.Error + "/" + result.FootprintError);
        return Assert.IsType<RoofTopology>(result.Topology);
    }

    private static RoofPoint2D[] Transform(RoofPoint2D[] points, double degrees, double x, double y)
    {
        var angle = degrees * Math.PI / 180;
        return points.Select(p => new RoofPoint2D(x + p.X * Math.Cos(angle) - p.Y * Math.Sin(angle),
            y + p.X * Math.Sin(angle) + p.Y * Math.Cos(angle))).ToArray();
    }

    private static RoofPoint2D[] Host291AConcave() =>
    [
        new(47947.813661, 12295.184331),
        new(47947.813661, 20038.646240),
        new(57988.858409, 20038.646240),
        new(57988.858409, 15403.104447),
        new(52849.740922, 15403.104447),
        new(52849.740922, 12295.184331),
    ];

    private static void AssertOblique(RoofSegment3D segment) => Assert.True(IsOblique(segment));

    private static bool IsOblique(RoofSegment3D segment) =>
        Math.Abs(segment.End.X - segment.Start.X) > 1e-6 &&
        Math.Abs(segment.End.Y - segment.Start.Y) > 1e-6;

    private static void AssertGeometry(RoofTopology t)
    {
        var n = t.BoundaryVertexCount;
        Assert.Equal(n, t.Faces.Count);
        Assert.Equal(n, t.Edges.Count(e => e.Kind == RoofTopologyEdgeKind.Eave));
        Assert.Equal(1, t.Nodes.Count - t.Edges.Count + t.Faces.Count);
        Assert.All(t.Nodes, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)));
        var incidence = new Dictionary<(int, int), List<int>>();
        var coverage = 0d;
        foreach (var face in t.Faces)
        {
            var cycle = face.BoundaryNodeIndices;
            Assert.Equal(cycle.Count, cycle.Distinct().Count());
            Assert.Equal(face.SourceEdgeIndex, cycle[0]);
            Assert.Equal((face.SourceEdgeIndex + 1) % n, cycle[1]);
            var a = t.Nodes[cycle[0]];
            var b = t.Nodes[cycle[1]];
            foreach (var i in cycle)
                Assert.InRange(Math.Abs(Cross(a, b, t.Nodes[i]) / a.DistanceTo(b) * Math.Tan(t.PitchDegrees * Math.PI / 180) - t.Nodes[i].Z), 0, 1e-6);
            var faceArea = Enumerable.Range(1, cycle.Count - 2).Sum(i => Cross(a, t.Nodes[cycle[i]], t.Nodes[cycle[i + 1]])) / 2;
            Assert.True(faceArea > 0);
            coverage += faceArea;
            for (var i = 0; i < cycle.Count; i++)
            {
                var key = Key(cycle[i], cycle[(i + 1) % cycle.Count]);
                if (!incidence.TryGetValue(key, out var list)) incidence[key] = list = new();
                list.Add(face.SourceEdgeIndex);
            }
        }
        var area = Enumerable.Range(1, n - 2).Sum(i => Cross(t.Nodes[0], t.Nodes[i], t.Nodes[i + 1])) / 2;
        Assert.InRange(Math.Abs(coverage - area), 0, Math.Max(1e-5, area * 1e-12));
        var adjacency = Enumerable.Range(0, t.Nodes.Count).Select(_ => new List<int>()).ToArray();
        foreach (var e in t.Edges)
        {
            Assert.Equal(incidence[Key(e.StartNodeIndex, e.EndNodeIndex)].Order(), e.FaceIndices);
            Assert.Equal(e.Kind == RoofTopologyEdgeKind.Eave ? 1 : 2, e.FaceIndices.Count);
            Assert.True(t.Segment(e).LengthMm > 1e-6);
            if (e.Kind != RoofTopologyEdgeKind.Eave)
            {
                adjacency[e.StartNodeIndex].Add(e.EndNodeIndex);
                adjacency[e.EndNodeIndex].Add(e.StartNodeIndex);
            }
            var a = t.Nodes[e.StartNodeIndex];
            var b = t.Nodes[e.EndNodeIndex];
            for (var step = 0; step <= 4; step++)
                Assert.True(Inside(new(a.X + (b.X - a.X) * step / 4, a.Y + (b.Y - a.Y) * step / 4, 0), t.Nodes.Take(n).ToArray()));
        }
        Assert.All(adjacency.Take(n), l => Assert.Single(l));
        Assert.All(adjacency.Skip(n), l => Assert.True(l.Count >= 3));
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(0);
        while (pending.Count > 0) { var i = pending.Pop(); if (seen.Add(i)) foreach (var j in adjacency[i]) pending.Push(j); }
        Assert.Equal(t.Nodes.Count, seen.Count);
        Assert.Equal(t.Edges.Count, incidence.Count);
        for (var i = 0; i < t.Edges.Count; i++)
            for (var j = i + 1; j < t.Edges.Count; j++)
            {
                var first = t.Edges[i]; var second = t.Edges[j];
                if (new[] { first.StartNodeIndex, first.EndNodeIndex }.Intersect(new[] { second.StartNodeIndex, second.EndNodeIndex }).Any()) continue;
                var a = t.Nodes[first.StartNodeIndex]; var b = t.Nodes[first.EndNodeIndex];
                var c = t.Nodes[second.StartNodeIndex]; var d = t.Nodes[second.EndNodeIndex];
                Assert.False(Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0);
                Assert.False(On(a, c, d) || On(b, c, d) || On(c, a, b) || On(d, a, b));
            }
    }

    private static (int, int) Key(int a, int b) => (Math.Min(a, b), Math.Max(a, b));
    private static double Cross(RoofPoint3D a, RoofPoint3D b, RoofPoint3D c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static bool On(RoofPoint3D p, RoofPoint3D a, RoofPoint3D b) =>
        Math.Abs(Cross(a, b, p)) / a.DistanceTo(b) < 1e-7 && p.X >= Math.Min(a.X, b.X) - 1e-7 && p.X <= Math.Max(a.X, b.X) + 1e-7 &&
        p.Y >= Math.Min(a.Y, b.Y) - 1e-7 && p.Y <= Math.Max(a.Y, b.Y) + 1e-7;
    private static bool Inside(RoofPoint3D p, RoofPoint3D[] polygon)
    {
        var winding = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
            if (On(p, a, b)) return true;
            if (a.Y <= p.Y && b.Y > p.Y && Cross(a, b, p) > 0) winding++;
            if (a.Y > p.Y && b.Y <= p.Y && Cross(a, b, p) < 0) winding--;
        }
        return winding != 0;
    }
}
