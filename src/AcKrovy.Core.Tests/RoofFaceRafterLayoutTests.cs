using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofFaceRafterLayoutTests
{
    public static IEnumerable<object[]> ConcaveFixtures()
    {
        yield return ["L", new RoofPoint2D[]
        {
            new(0, 0), new(8000, 0), new(8000, 3000),
            new(3000, 3000), new(3000, 8000), new(0, 8000),
        }];
        yield return ["U", new RoofPoint2D[]
        {
            new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000),
            new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000),
        }];
        yield return ["T", new RoofPoint2D[]
        {
            new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000),
            new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000),
        }];
    }

    [Fact]
    public void Rectangle10000By6000_ProducesCanonicalExpectedLayout()
    {
        var topology = Solve(Rectangle(), 30d);

        var first = Create(topology, 500d);
        var second = Create(topology, 500d);

        Assert.Equal(64, first.Segments.Count);
        Assert.Equal(first.RequestedSpacingMm, second.RequestedSpacingMm);
        Assert.Equal(first.Segments, second.Segments);
        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(16, first.Segments.Count(r =>
            HasRoles(r, RoofRafterBoundaryRole.Eave, RoofRafterBoundaryRole.Ridge)));
        Assert.Equal(48, first.Segments.Count(r =>
            HasRoles(r, RoofRafterBoundaryRole.Eave, RoofRafterBoundaryRole.Hip)));
        Assert.DoesNotContain(first.Segments, r =>
            HasRoles(r, RoofRafterBoundaryRole.Ridge, RoofRafterBoundaryRole.Valley));
        AssertCanonicalGuarantees(topology, first);
        AssertOpposingRidgeHitsAligned(topology, first);
    }

    [Fact]
    public void AsymmetricUnequalEaves_ShareRidgeCenteredPhaseWithoutShift()
    {
        var topology = Solve(AsymmetricTrapezoid(), 30d);
        var layout = Create(topology, 500d);
        var ridge = topology.Edges.Single(edge =>
            edge.Kind == RoofTopologyEdgeKind.Ridge &&
            edge.FaceIndices.Count == 2);
        var firstFace = ridge.FaceIndices[0];
        var secondFace = ridge.FaceIndices[1];
        var firstEave = EaveLength(topology, firstFace);
        var secondEave = EaveLength(topology, secondFace);

        Assert.NotEqual(firstEave, secondEave, 8);
        AssertOpposingRidgeHitsAligned(topology, layout);

        var firstHits = RidgeHits(layout, firstFace);
        var secondHits = RidgeHits(layout, secondFace);
        Assert.Equal(firstHits.Count, secondHits.Count);
        Assert.NotEmpty(firstHits);
        Assert.All(firstHits.Zip(secondHits), pair =>
        {
            Assert.Equal(pair.First.X, pair.Second.X, 8);
            Assert.Equal(pair.First.Y, pair.Second.Y, 8);
        });
        Assert.All(firstHits.Zip(firstHits.Skip(1)), pair =>
            Assert.Equal(500d, Distance(pair.First, pair.Second), 8));

        // Unequal eave centering would phase-shift; ridge lattice must not match
        // the longer eave's independent centered remainder on the shared overlap.
        var longerFace = firstEave >= secondEave ? firstFace : secondFace;
        var longerEaveStations = RoofFaceRafterLayoutService.CreateStationDistances(
            Math.Max(firstEave, secondEave),
            500d);
        var eaveLocalWorldXs = longerEaveStations
            .Select(station => WorldStationOnEaveAxis(topology, longerFace, station))
            .ToArray();
        Assert.DoesNotContain(
            firstHits.Select(hit => Math.Round(hit.X, 6)),
            value => eaveLocalWorldXs.Any(eaveX =>
                Math.Abs(eaveX - value) <=
                RoofFaceRafterLayoutService.CoordinateToleranceMm));
    }

    [Fact]
    public void TShape_SplitCollinearRidgeComponent_AlignsAtHostDefaultSpacing()
    {
        var topology = Solve(TShape(), 30d);
        var horizontalRidges = topology.Edges
            .Select((edge, index) => (Edge: edge, Index: index))
            .Where(item =>
                item.Edge.Kind == RoofTopologyEdgeKind.Ridge &&
                item.Edge.FaceIndices.Count == 2 &&
                item.Edge.FaceIndices.Contains(0))
            .OrderBy(item => item.Index)
            .ToArray();

        Assert.Equal(2, horizontalRidges.Length);
        Assert.Contains(horizontalRidges, item =>
            item.Edge.FaceIndices.Contains(6));
        Assert.Contains(horizontalRidges, item =>
            item.Edge.FaceIndices.Contains(2));

        // HOST default automatic spacing reproduces the previous per-edge failure:
        // E16-only centering → 2350, E18-only → 5850, equivalence fail → eave fallback.
        var layout = Create(topology, 900d);
        AssertOpposingRidgeHitsAligned(topology, layout);

        var face0 = RidgeHits(layout, 0);
        var face6 = RidgeHits(layout, 6);
        var face2 = RidgeHits(layout, 2);
        Assert.NotEmpty(face0);
        Assert.NotEmpty(face6);
        Assert.NotEmpty(face2);
        Assert.Equal(0d, MaxCompatibleRidgeHitMismatch(topology, layout), 8);

        // Component span [1500,8500] with S=900 centers at 2300, not the old
        // per-sub-edge remainders 2350 / eave-local 500+k*900 lattice.
        Assert.Equal(2300d, face0[0].X, 8);
        Assert.Equal(2300d, face6[0].X, 8);
        Assert.DoesNotContain(face0, hit => Math.Abs(hit.X - 2350d) <= 1e-6);
        Assert.DoesNotContain(face6, hit => Math.Abs(hit.X - 2350d) <= 1e-6);
        Assert.All(face6, hit =>
            Assert.Contains(face0, other =>
                Distance(hit, other) <=
                RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.All(face2, hit =>
            Assert.Contains(face0, other =>
                Distance(hit, other) <=
                RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.True(face0.Count > face6.Count);
        Assert.True(face0.Count > face2.Count);
    }

    [Fact]
    public void TShape_FaceTouchingTwoCollinearRidgeEdges_KeepsComponentPhase()
    {
        var topology = Solve(TShape(), 30d);
        var layout = Create(topology, 900d);
        var face0Ridges = topology.Edges.Count(edge =>
            edge.Kind == RoofTopologyEdgeKind.Ridge &&
            edge.FaceIndices.Contains(0));

        Assert.Equal(2, face0Ridges);
        Assert.NotEmpty(RidgeHits(layout, 0));
        Assert.Equal(0d, MaxCompatibleRidgeHitMismatch(topology, layout), 8);
        Assert.All(RidgeHits(layout, 0).Zip(RidgeHits(layout, 0).Skip(1)), pair =>
            Assert.Equal(900d, Distance(pair.First, pair.Second), 8));
    }

    [Fact]
    public void PerpendicularConnectedRidges_DoNotShareOnePhaseComponent()
    {
        var topology = Solve(TShape(), 30d);
        var layout = Create(topology, 500d);
        var horizontal = RidgeHits(layout, 0);
        var vertical = RidgeHits(layout, 3);

        Assert.NotEmpty(horizontal);
        Assert.NotEmpty(vertical);
        Assert.True(horizontal.All(hit =>
            Math.Abs(hit.Y - 1500d) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.True(vertical.All(hit =>
            Math.Abs(hit.X - 5000d) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.False(
            Math.Abs(horizontal[0].X - vertical[0].X) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm &&
            Math.Abs(horizontal[0].Y - vertical[0].Y) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm);
    }

    [Fact]
    public void DisconnectedParallelRidges_RemainIndependentComponents()
    {
        var topology = Solve(UShape(), 30d);
        var layout = Create(topology, 500d);
        var left = RidgeHits(layout, 5);
        var right = RidgeHits(layout, 1);

        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
        Assert.True(left.All(hit =>
            Math.Abs(hit.X - 1500d) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.True(right.All(hit =>
            Math.Abs(hit.X - 8500d) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
        AssertOpposingRidgeHitsAligned(topology, layout);
        // Parallel but not endpoint-connected: distinct components (different X).
        Assert.True(Math.Abs(left[0].X - right[0].X) > 1d);
    }

    [Fact]
    public void ModuloCongruentButPhysicallyShifted_IsRejectedByHitAssertion()
    {
        // Documents the HOST failure mode: 2350 vs 2300 differ by 50 mm. They can
        // look "aligned" under a coarse modulo check, but physical hits must match.
        const double perEdgeCentered = 2350d;
        const double componentCentered = 2300d;
        Assert.Equal(50d, Math.Abs(perEdgeCentered - componentCentered), 8);
        Assert.True(Math.Abs(perEdgeCentered - componentCentered) >
            RoofFaceRafterLayoutService.CoordinateToleranceMm);

        var topology = Solve(TShape(), 30d);
        var layout = Create(topology, 900d);
        Assert.Equal(0d, MaxCompatibleRidgeHitMismatch(topology, layout), 8);
        Assert.All(RidgeHits(layout, 0), hit =>
            Assert.True(Math.Abs(hit.X - perEdgeCentered) >
                RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.Equal(componentCentered, RidgeHits(layout, 0)[0].X, 8);
    }

    [Fact]
    public void MultipleRidgeEdges_KeepIndependentPhases()
    {
        var topology = Solve(LShape(), 30d);
        var layout = Create(topology, 500d);
        var ridges = topology.Edges
            .Where(edge =>
                edge.Kind == RoofTopologyEdgeKind.Ridge &&
                edge.FaceIndices.Count == 2)
            .OrderBy(edge => Math.Min(edge.FaceIndices[0], edge.FaceIndices[1]))
            .ToArray();

        Assert.Equal(2, ridges.Length);
        AssertOpposingRidgeHitsAligned(topology, layout);

        var horizontal = RidgeHits(layout, ridges[0].FaceIndices[0]);
        var vertical = RidgeHits(layout, ridges[1].FaceIndices[0]);
        Assert.True(horizontal.All(hit =>
            Math.Abs(hit.Y - horizontal[0].Y) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.True(vertical.All(hit =>
            Math.Abs(hit.X - vertical[0].X) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm));
        Assert.False(
            Math.Abs(horizontal[0].X - vertical[0].X) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm &&
            Math.Abs(horizontal[0].Y - vertical[0].Y) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm);
    }

    [Fact]
    public void IncompatibleRafterDirections_AreNotPairable()
    {
        Assert.True(RoofFaceRafterLayoutService.AreCompatibleOpposingRafterFamilies(
            new RoofPoint2D(0d, 1d),
            new RoofPoint2D(0d, -1d)));
        Assert.False(RoofFaceRafterLayoutService.AreCompatibleOpposingRafterFamilies(
            new RoofPoint2D(0d, 1d),
            new RoofPoint2D(1d, 0d)));
    }

    [Fact]
    public void DuplicateFilter_KeepsOpposingSegmentsThatShareOnlyRidgeEndpoint()
    {
        var layout = Create(Solve(Rectangle(), 30d), 500d);
        var ridgeEnding = layout.Segments
            .Where(segment =>
                segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge ||
                segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge)
            .ToArray();
        var sharedEndpointPairs = ridgeEnding
            .SelectMany((left, index) => ridgeEnding
                .Skip(index + 1)
                .Where(right =>
                    left.SourceFaceIndex != right.SourceFaceIndex &&
                    ShareEndpoint(left, right))
                .Select(right => (left, right)))
            .ToArray();

        Assert.NotEmpty(sharedEndpointPairs);
        Assert.All(sharedEndpointPairs, pair =>
        {
            Assert.Contains(pair.left, layout.Segments);
            Assert.Contains(pair.right, layout.Segments);
        });
    }

    [Fact]
    public void Rectangle_HasNoRidgeToValleyIntervals()
    {
        var layout = Create(Solve(Rectangle(), 30d), 500d);

        Assert.DoesNotContain(layout.Segments, segment =>
            HasRoles(
                segment,
                RoofRafterBoundaryRole.Ridge,
                RoofRafterBoundaryRole.Valley));
    }

    [Fact]
    public void LShape_ExtendsOwningFaceStationFamiliesIntoRidgeValleyRegion()
    {
        var topology = Solve(LShape(), 30d);
        var layout = Create(topology, 500d);
        var ridgeValley = layout.Segments
            .Where(segment => HasRoles(
                segment,
                RoofRafterBoundaryRole.Ridge,
                RoofRafterBoundaryRole.Valley))
            .ToArray();

        Assert.Equal(70, layout.Segments.Count);
        Assert.Equal(6, ridgeValley.Length);
        Assert.Contains(ridgeValley, segment =>
            segment.SourceFaceIndex == 2 &&
            segment.StationDistanceMm == 5250d &&
            segment.PlanStart == new RoofPoint2D(2750d, 2750d) &&
            segment.StartBoundaryRole == RoofRafterBoundaryRole.Valley &&
            segment.PlanEnd == new RoofPoint2D(2750d, 1500d) &&
            segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        Assert.Contains(ridgeValley, segment =>
            segment.SourceFaceIndex == 3 &&
            segment.StationDistanceMm == -250d &&
            segment.PlanStart == new RoofPoint2D(2750d, 2750d) &&
            segment.StartBoundaryRole == RoofRafterBoundaryRole.Valley &&
            segment.PlanEnd == new RoofPoint2D(1500d, 2750d) &&
            segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);

        Assert.All(ridgeValley, segment =>
        {
            AssertSegmentInsideOwningFace(topology, segment);
            AssertUsesOwningFaceDirection(topology, segment);
            var familyHits = RidgeHits(layout, segment.SourceFaceIndex);
            var hit = RidgeHit(segment);
            Assert.Contains(familyHits, familyHit =>
                Distance(familyHit, hit) <=
                RoofFaceRafterLayoutService.CoordinateToleranceMm);
        });
        AssertOpposingRidgeHitsAligned(topology, layout);
    }

    [Theory]
    [InlineData("U", 112, 12)]
    [InlineData("T", 88, 12)]
    public void UAndT_UseGenericExtendedStationCoverage(
        string name,
        int expectedTotal,
        int expectedRidgeValley)
    {
        var polygon = name == "U" ? UShape() : TShape();
        var topology = Solve(polygon, 30d);
        var layout = Create(topology, 500d);
        var ridgeValley = layout.Segments.Where(segment => HasRoles(
            segment,
            RoofRafterBoundaryRole.Ridge,
            RoofRafterBoundaryRole.Valley)).ToArray();

        Assert.Equal(expectedTotal, layout.Segments.Count);
        Assert.Equal(expectedRidgeValley, ridgeValley.Length);
        Assert.All(ridgeValley, segment =>
        {
            AssertSegmentInsideOwningFace(topology, segment);
            AssertUsesOwningFaceDirection(topology, segment);
        });
        AssertCanonicalGuarantees(topology, layout);
        AssertOpposingRidgeHitsAligned(topology, layout);
    }

    [Fact]
    public void Rectangle_UsesExactSpacingAndSymmetricRemainderPerEave()
    {
        var topology = Solve(Rectangle(), 30d);
        var layout = Create(topology, 500d);

        foreach (var group in layout.Segments.GroupBy(r => r.SourceEaveEdgeIndex))
        {
            var eave = topology.Segment(topology.Edges[group.Key]);
            var stations = group.Select(r => r.StationDistanceMm).Order().ToArray();
            Assert.All(stations.Zip(stations.Skip(1)), pair =>
                Assert.Equal(500d, pair.Second - pair.First, 8));
            Assert.Equal(stations[0], eave.LengthMm - stations[^1], 8);
        }
    }

    [Fact]
    public void Rectangle_RaftersArePerpendicularToOwningEave()
    {
        var topology = Solve(Rectangle(), 30d);
        var layout = Create(topology, 500d);

        foreach (var rafter in layout.Segments)
        {
            var eave = topology.Segment(topology.Edges[rafter.SourceEaveEdgeIndex]);
            var eaveX = eave.End.X - eave.Start.X;
            var eaveY = eave.End.Y - eave.Start.Y;
            var runX = rafter.PlanEnd.X - rafter.PlanStart.X;
            var runY = rafter.PlanEnd.Y - rafter.PlanStart.Y;
            Assert.InRange(Math.Abs(eaveX * runX + eaveY * runY), 0d, 1e-6);
        }
    }

    [Theory]
    [MemberData(nameof(ConcaveFixtures))]
    public void ConcaveFixtures_AreDeterministicInsideAndDuplicateFree(
        string name,
        RoofPoint2D[] polygon)
    {
        var topology = Solve(polygon, 30d);

        var first = Create(topology, 500d);
        var second = Create(topology, 500d);

        Assert.NotEmpty(first.Segments);
        Assert.Equal(first.RequestedSpacingMm, second.RequestedSpacingMm);
        Assert.Equal(first.Segments, second.Segments);
        Assert.Contains(topology.Edges, edge =>
            edge.Kind == RoofTopologyEdgeKind.Valley);
        AssertCanonicalGuarantees(topology, first);
        Assert.All(first.Segments, rafter =>
            Assert.True(InsideOrOn(
                Midpoint(rafter.PlanStart, rafter.PlanEnd),
                polygon),
                name));
    }

    [Theory]
    [MemberData(nameof(ConcaveFixtures))]
    public void ConcaveFixtures_DoNotCrossPhysicalInternalBoundaries(
        string name,
        RoofPoint2D[] polygon)
    {
        var topology = Solve(polygon, 30d);
        var layout = Create(topology, 500d);
        var physical = topology.Edges.Where(edge => edge.Kind is
            RoofTopologyEdgeKind.Ridge or RoofTopologyEdgeKind.Hip or
            RoofTopologyEdgeKind.Valley).ToArray();

        foreach (var rafter in layout.Segments)
        {
            foreach (var edge in physical)
            {
                var boundary = topology.Segment(edge);
                var a = new RoofPoint2D(boundary.Start.X, boundary.Start.Y);
                var b = new RoofPoint2D(boundary.End.X, boundary.End.Y);
                Assert.False(
                    ProperlyIntersects(rafter.PlanStart, rafter.PlanEnd, a, b),
                    $"{name}: rafter crosses {edge.Kind}");
            }
        }
    }

    [Fact]
    public void DifferentSpacingChangesStationsWithoutHiddenDefault()
    {
        var topology = Solve(Rectangle(), 30d);

        var at600 = Create(topology, 600d);
        var at900 = Create(topology, 900d);

        Assert.NotEqual(at600.Signature, at900.Signature);
        Assert.NotEqual(
            at600.Segments.Select(r => (r.SourceEaveEdgeIndex, r.StationDistanceMm)),
            at900.Segments.Select(r => (r.SourceEaveEdgeIndex, r.StationDistanceMm)));
        Assert.Equal(600d, at600.RequestedSpacingMm);
        Assert.Equal(900d, at900.RequestedSpacingMm);
    }

    [Fact]
    public void ShortEaveProducesOneCenteredCandidate()
    {
        var stations = RoofFaceRafterLayoutService.CreateStationDistances(400d, 500d);

        Assert.Equal(new[] { 200d }, stations);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSpacingIsRejected(double spacing)
    {
        var result = RoofFaceRafterLayoutService.Create(
            Solve(Rectangle(), 30d),
            spacing);

        Assert.False(result.IsValid);
        Assert.Null(result.Layout);
        Assert.Equal(RoofFaceRafterLayoutError.InvalidSpacing, result.Error);
    }

    [Fact]
    public void LayoutPlanGeometryDoesNotDependOnSlope()
    {
        var at20 = Create(Solve(Rectangle(), 20d), 500d);
        var at55 = Create(Solve(Rectangle(), 55d), 500d);

        Assert.Equal(
            at20.Segments.Select(PlanIdentity),
            at55.Segments.Select(PlanIdentity));
    }

    private static void AssertOpposingRidgeHitsAligned(
        RoofTopology topology,
        RoofFaceRafterLayout layout)
    {
        Assert.Equal(0d, MaxCompatibleRidgeHitMismatch(topology, layout), 8);
    }

    private static double MaxCompatibleRidgeHitMismatch(
        RoofTopology topology,
        RoofFaceRafterLayout layout)
    {
        var maxMismatch = 0d;
        foreach (var ridge in topology.Edges.Where(edge =>
                     edge.Kind == RoofTopologyEdgeKind.Ridge &&
                     edge.FaceIndices.Count == 2))
        {
            var first = topology.Faces.Single(face =>
                face.SourceEdgeIndex == ridge.FaceIndices[0]);
            var second = topology.Faces.Single(face =>
                face.SourceEdgeIndex == ridge.FaceIndices[1]);
            var firstDir = RafterDirection(topology, first);
            var secondDir = RafterDirection(topology, second);
            if (!RoofFaceRafterLayoutService.AreCompatibleOpposingRafterFamilies(
                    firstDir,
                    secondDir))
            {
                continue;
            }

            var firstHits = RidgeHits(layout, ridge.FaceIndices[0]);
            var secondHits = RidgeHits(layout, ridge.FaceIndices[1]);
            var shorter = firstHits.Count <= secondHits.Count ? firstHits : secondHits;
            var longer = firstHits.Count <= secondHits.Count ? secondHits : firstHits;
            foreach (var shortHit in shorter)
            {
                var best = longer
                    .Select(longHit => Distance(longHit, shortHit))
                    .DefaultIfEmpty(double.PositiveInfinity)
                    .Min();
                maxMismatch = Math.Max(maxMismatch, best);
            }
        }

        return maxMismatch;
    }

    private static IReadOnlyList<RoofPoint2D> RidgeHits(
        RoofFaceRafterLayout layout,
        int faceIndex) => layout.Segments
        .Where(segment =>
            segment.SourceFaceIndex == faceIndex &&
            (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge ||
             segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge))
        .Select(RidgeHit)
        .OrderBy(point => point.X)
        .ThenBy(point => point.Y)
        .ToArray();

    private static RoofPoint2D RidgeHit(RoofFaceRafterSegment segment) =>
        segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge
            ? segment.PlanEnd
            : segment.PlanStart;

    private static RoofPoint2D RafterDirection(
        RoofTopology topology,
        RoofTopologyFace face)
    {
        var start = topology.Nodes[face.BoundaryNodeIndices[0]];
        var end = topology.Nodes[face.BoundaryNodeIndices[1]];
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return new RoofPoint2D(-dy / length, dx / length);
    }

    private static double EaveLength(RoofTopology topology, int faceIndex)
    {
        var face = topology.Faces.Single(item => item.SourceEdgeIndex == faceIndex);
        var start = topology.Nodes[face.BoundaryNodeIndices[0]];
        var end = topology.Nodes[face.BoundaryNodeIndices[1]];
        return Math.Sqrt(
            (end.X - start.X) * (end.X - start.X) +
            (end.Y - start.Y) * (end.Y - start.Y));
    }

    private static double WorldStationOnEaveAxis(
        RoofTopology topology,
        int faceIndex,
        double stationDistanceMm)
    {
        var face = topology.Faces.Single(item => item.SourceEdgeIndex == faceIndex);
        var start = topology.Nodes[face.BoundaryNodeIndices[0]];
        var end = topology.Nodes[face.BoundaryNodeIndices[1]];
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var fraction = stationDistanceMm / length;
        return start.X + dx * fraction;
    }

    private static bool ShareEndpoint(
        RoofFaceRafterSegment left,
        RoofFaceRafterSegment right) =>
        Distance(left.PlanStart, right.PlanStart) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm ||
        Distance(left.PlanStart, right.PlanEnd) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm ||
        Distance(left.PlanEnd, right.PlanStart) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm ||
        Distance(left.PlanEnd, right.PlanEnd) <=
            RoofFaceRafterLayoutService.CoordinateToleranceMm;

    private static double Distance(RoofPoint2D first, RoofPoint2D second) =>
        first.DistanceTo(second);

    private static void AssertCanonicalGuarantees(
        RoofTopology topology,
        RoofFaceRafterLayout layout)
    {
        Assert.Equal(
            layout.Segments
                .OrderBy(r => r.SourceEaveEdgeIndex)
                .ThenBy(r => r.StationDistanceMm)
                .ThenBy(r => r.StationIntervalIndex)
                .ThenBy(r => r.StartBoundaryRole)
                .ThenBy(r => r.EndBoundaryRole),
            layout.Segments);
        Assert.All(layout.Segments, rafter =>
        {
            Assert.True(rafter.PlanLengthMm >
                RoofFaceRafterLayoutService.CoordinateToleranceMm);
            Assert.Equal(
                rafter.PlanStart.DistanceTo(rafter.PlanEnd),
                rafter.PlanLengthMm,
                8);
            var face = topology.Faces.Single(item =>
                item.SourceEdgeIndex == rafter.SourceFaceIndex);
            Assert.True(InsideOrOn(
                Midpoint(rafter.PlanStart, rafter.PlanEnd),
                face.BoundaryNodeIndices
                    .Select(index => topology.Nodes[index])
                    .Select(point => new RoofPoint2D(point.X, point.Y))
                    .ToArray()));
        });
        Assert.Equal(
            layout.Segments.Count,
            layout.Segments.Select(SegmentKey).Distinct().Count());
    }

    private static void AssertSegmentInsideOwningFace(
        RoofTopology topology,
        RoofFaceRafterSegment segment)
    {
        var face = topology.Faces.Single(item =>
            item.SourceEdgeIndex == segment.SourceFaceIndex);
        var polygon = face.BoundaryNodeIndices
            .Select(index => topology.Nodes[index])
            .Select(point => new RoofPoint2D(point.X, point.Y))
            .ToArray();
        for (var step = 0; step <= 10; step++)
        {
            var fraction = step / 10d;
            var point = new RoofPoint2D(
                segment.PlanStart.X +
                (segment.PlanEnd.X - segment.PlanStart.X) * fraction,
                segment.PlanStart.Y +
                (segment.PlanEnd.Y - segment.PlanStart.Y) * fraction);
            Assert.True(InsideOrOn(point, polygon));
        }
    }

    private static void AssertUsesOwningFaceDirection(
        RoofTopology topology,
        RoofFaceRafterSegment segment)
    {
        var eave = topology.Segment(topology.Edges[segment.SourceEaveEdgeIndex]);
        var eaveX = eave.End.X - eave.Start.X;
        var eaveY = eave.End.Y - eave.Start.Y;
        var rafterX = segment.PlanEnd.X - segment.PlanStart.X;
        var rafterY = segment.PlanEnd.Y - segment.PlanStart.Y;

        Assert.InRange(Math.Abs(eaveX * rafterX + eaveY * rafterY), 0d, 1e-6);
        Assert.True((-eaveY * rafterX + eaveX * rafterY) > 0d);
    }

    private static object PlanIdentity(RoofFaceRafterSegment rafter) => new
    {
        rafter.SourceFaceIndex,
        rafter.SourceEaveEdgeIndex,
        rafter.StationIndex,
        rafter.StationIntervalIndex,
        rafter.StationDistanceMm,
        rafter.PlanStart,
        rafter.PlanEnd,
        rafter.StartBoundaryRole,
        rafter.EndBoundaryRole,
        rafter.PlanLengthMm,
    };

    private static bool HasRoles(
        RoofFaceRafterSegment segment,
        RoofRafterBoundaryRole first,
        RoofRafterBoundaryRole second) =>
        (segment.StartBoundaryRole == first &&
         segment.EndBoundaryRole == second) ||
        (segment.StartBoundaryRole == second &&
         segment.EndBoundaryRole == first);

    private static string SegmentKey(RoofFaceRafterSegment segment)
    {
        var first = $"{segment.PlanStart.X:F6},{segment.PlanStart.Y:F6}";
        var second = $"{segment.PlanEnd.X:F6},{segment.PlanEnd.Y:F6}";
        return string.CompareOrdinal(first, second) <= 0
            ? first + ";" + second
            : second + ";" + first;
    }

    private static RoofPoint2D Midpoint(RoofPoint2D first, RoofPoint2D second) =>
        new((first.X + second.X) / 2d, (first.Y + second.Y) / 2d);

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

    private static bool InsideOrOn(RoofPoint2D point, IReadOnlyList<RoofPoint2D> polygon)
    {
        var winding = 0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            if (OnSegment(point, a, b))
            {
                return true;
            }
            if (a.Y <= point.Y && b.Y > point.Y && Cross(a, b, point) > 0d)
            {
                winding++;
            }
            if (a.Y > point.Y && b.Y <= point.Y && Cross(a, b, point) < 0d)
            {
                winding--;
            }
        }
        return winding != 0;
    }

    private static bool OnSegment(RoofPoint2D point, RoofPoint2D a, RoofPoint2D b)
    {
        var length = a.DistanceTo(b);
        return length > 0d &&
               Math.Abs(Cross(a, b, point)) / length <=
                   RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.X >= Math.Min(a.X, b.X) - RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.X <= Math.Max(a.X, b.X) + RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.Y >= Math.Min(a.Y, b.Y) - RoofFaceRafterLayoutService.CoordinateToleranceMm &&
               point.Y <= Math.Max(a.Y, b.Y) + RoofFaceRafterLayoutService.CoordinateToleranceMm;
    }

    private static double Cross(RoofPoint2D a, RoofPoint2D b, RoofPoint2D c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static RoofFaceRafterLayout Create(RoofTopology topology, double spacing)
    {
        var result = RoofFaceRafterLayoutService.Create(topology, spacing);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFaceRafterLayout>(result.Layout);
    }

    private static RoofTopology Solve(IReadOnlyList<RoofPoint2D> polygon, double slope)
    {
        var result = RoofTopologySolver.Solve(new RoofFootprintInput(polygon, true), slope);
        Assert.True(result.IsValid, result.Error + "/" + result.FootprintError);
        return Assert.IsType<RoofTopology>(result.Topology);
    }

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static RoofPoint2D[] AsymmetricTrapezoid() =>
    [
        new(0, 0), new(10000, 0), new(9500, 6000), new(1200, 6000),
    ];

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

    private static RoofPoint2D[] TShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000),
        new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000),
    ];
}
