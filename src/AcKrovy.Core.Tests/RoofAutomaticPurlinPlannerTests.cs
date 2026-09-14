using System.Reflection;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinPlannerTests
{
    private const string LayoutIdA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LayoutIdB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static IEnumerable<object[]> FixtureMatrix()
    {
        yield return ["Rectangle", Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)), 1, new[] { 4 }];
        yield return ["L", Points((0, 0), (8000, 0), (8000, 3000), (3000, 3000), (3000, 8000), (0, 8000)), 2, new[] { 6 }];
        yield return ["U", Points((0, 0), (10000, 0), (10000, 9000), (7000, 9000), (7000, 3000), (3000, 3000), (3000, 9000), (0, 9000)), 3, new[] { 8 }];
        yield return ["T", Points((0, 0), (10000, 0), (10000, 3000), (6500, 3000), (6500, 9000), (3500, 9000), (3500, 3000), (0, 3000)), 3, new[] { 8 }];
        yield return ["Asymmetric U", Points((0, 0), (12000, 0), (12000, 9500), (8500, 9500), (8500, 4000), (2500, 4000), (2500, 8000), (0, 8000)), 3, new[] { 8, 6, 4 }];
        yield return ["Asymmetric T", Points((0, 0), (12000, 0), (12000, 2500), (8500, 2500), (8500, 11000), (3500, 11000), (3500, 2500), (0, 2500)), 3, new[] { 8, 4 }];
        yield return ["Irregular convex", Points((0, 0), (7000, 0), (10000, 4000), (6000, 9000), (-2000, 5000)), 0, new[] { 5, 4, 3 }];
        yield return ["Reflex hexagon", Points((0, 0), (9000, 0), (7000, 4000), (10000, 8000), (3000, 10000), (-1000, 5000)), 0, new[] { 6, 5, 4, 3 }];
    }

    [Theory]
    [MemberData(nameof(FixtureMatrix))]
    public void RidgePolicy_EmitsOnlyHorizontalStructuralRidges(
        string name,
        RoofPoint2D[] points,
        int expectedHorizontalRidges,
        int[] _)
    {
        var solved = Solve(points);
        var disabled = Plan(solved, new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>()));
        var enabled = Plan(solved, new RoofAutomaticPurlinLayout(true, Array.Empty<RoofAutomaticPurlinLayoutItem>()));
        var structural = RoofStructuralEdgeIdentityResolver.Resolve(solved.Geometry, solved.Provenance);
        var eligible = structural.Edges.Where(edge =>
            edge.StructuralRole == RoofStructuralRole.Ridge &&
            Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) <=
            RoofAutomaticPurlinPlanner.CoordinateToleranceMm).ToArray();

        Assert.Empty(disabled.Items);
        Assert.Equal(expectedHorizontalRidges, eligible.Length);
        Assert.Equal(expectedHorizontalRidges, enabled.Items.Count);
        Assert.All(enabled.Items, item =>
        {
            Assert.Equal(name, name);
            Assert.Equal(RoofAutomaticPurlinGeneratorRole.Ridge, item.GeneratorRole);
            Assert.Equal(TimberElementType.Purlin, item.ElementType);
            Assert.Null(item.LayoutItemId);
            var key = Assert.IsType<RoofAutomaticPurlinRidgeKey>(item.GeneratedKey);
            var source = Assert.Single(eligible, edge => edge.StructuralIdentity == key.StructuralKey);
            Assert.Equal(source.Segment3D, item.Segment3D);
            Assert.Equal(source.Length3dMm, item.LengthMm);
        });
        Assert.DoesNotContain(structural.Edges.Where(edge =>
                edge.StructuralRole == RoofStructuralRole.Ridge &&
                Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) >
                RoofAutomaticPurlinPlanner.CoordinateToleranceMm),
            edge => enabled.Items.Any(item =>
                item.GeneratedKey is RoofAutomaticPurlinRidgeKey key &&
                key.StructuralKey == edge.StructuralIdentity));
    }

    [Theory]
    [MemberData(nameof(FixtureMatrix))]
    public void IntermediateSlices_PreserveEveryExpectedNoncriticalBand(
        string name,
        RoofPoint2D[] points,
        int _,
        int[] expectedCounts)
    {
        var solved = Solve(points);
        var heights = NoncriticalBandHeights(solved.Geometry);

        Assert.Equal(expectedCounts.Length, heights.Length);
        var actual = heights.Select(height =>
        {
            var plan = Plan(solved, IntermediateLayout(LayoutIdA, height));
            Assert.All(plan.Items, item =>
            {
                Assert.Equal(RoofAutomaticPurlinGeneratorRole.Intermediate, item.GeneratorRole);
                Assert.Equal(TimberElementType.Purlin, item.ElementType);
                Assert.Equal(LayoutIdA, item.LayoutItemId);
                Assert.True(item.LengthMm > RoofAutomaticPurlinPlanner.CoordinateToleranceMm);
                Assert.Equal(height, item.Segment3D.Start.Z, 9);
                Assert.Equal(height, item.Segment3D.End.Z, 9);
            });
            return plan.Items.Count;
        }).ToArray();

        Assert.Equal(expectedCounts, actual);
        Assert.True(actual[0] > 0, name);
    }

    [Fact]
    public void LayoutItemIdentity_IsCanonicalOpaqueGuidNAndCreatedUniquely()
    {
        var first = RoofAutomaticPurlinLayoutItemIdentity.Create();
        var second = RoofAutomaticPurlinLayoutItemIdentity.Create();

        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.Matches("^[0-9a-f]{32}$", second);
        Assert.NotEqual(first, second);
        Assert.True(RoofAutomaticPurlinLayoutItemIdentity.TryNormalize(
            LayoutIdA.ToUpperInvariant(),
            out var normalized));
        Assert.Equal(LayoutIdA, normalized);
    }

    [Fact]
    public void GeneratedPersistenceRoundtrip_ReconstructsEachOriginalPlanItemKey()
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);
        var elevation = NoncriticalBandHeights(solved.Geometry)[0];
        var plan = Plan(solved, new(true,
        [
            HeightItem(LayoutIdA, true, elevation),
        ]));

        Assert.Contains(plan.Items, item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Contains(plan.Items, item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate);
        foreach (var item in plan.Items)
        {
            var written = RoofAutomaticPurlinGeneratedDataRules.Create(
                "00af",
                item.GeneratedKey);
            Assert.True(written.IsValid, written.Error.ToString());

            RoofAutomaticPurlinGeneratedDataValidationResult read;
            if (written.Data!.GeneratedKey is RoofAutomaticPurlinRidgeKey ridge)
            {
                read = RoofAutomaticPurlinGeneratedDataRules.ValidateRidgeStored(
                    1,
                    written.Data.RoofOwnerReference,
                    "Ridge",
                    ridge.StructuralKey.BoundaryEdgeIdA,
                    ridge.StructuralKey.BoundaryEdgeIdB);
            }
            else
            {
                var intermediate = Assert.IsType<RoofAutomaticPurlinIntermediateKey>(
                    written.Data.GeneratedKey);
                Assert.True(RoofAutomaticPurlinBoundaryKeyRules.TryFormat(
                    intermediate.EndpointBoundaryKeyA,
                    out var endpointA,
                    out _));
                Assert.True(RoofAutomaticPurlinBoundaryKeyRules.TryFormat(
                    intermediate.EndpointBoundaryKeyB,
                    out var endpointB,
                    out _));
                read = RoofAutomaticPurlinGeneratedDataRules.ValidateIntermediateStored(
                    1,
                    written.Data.RoofOwnerReference,
                    "Intermediate",
                    intermediate.LayoutItemId,
                    intermediate.SourceFaceBoundaryEdgeId,
                    endpointA,
                    endpointB);
            }

            Assert.True(read.IsValid, read.Error.ToString());
            Assert.Equal(item.GeneratedKey, read.Data!.GeneratedKey);
        }
    }

    [Fact]
    public void ZeroOneTwoAndDisabledLayoutItems_ProduceOnlyEnabledDesiredState()
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);
        var height = NoncriticalBandHeights(solved.Geometry)[0];

        Assert.Empty(Plan(solved, new(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())).Items);
        Assert.Equal(4, Plan(solved, IntermediateLayout(LayoutIdA, height)).Items.Count);
        Assert.Equal(8, Plan(solved, new(false,
        [
            HeightItem(LayoutIdA, true, height),
            HeightItem(LayoutIdB, true, height * 1.5d),
        ])).Items.Count);
        Assert.Equal(4, Plan(solved, new(false,
        [
            HeightItem(LayoutIdA, true, height),
            HeightItem(LayoutIdB, false, double.NaN),
        ])).Items.Count);
    }

    [Fact]
    public void ElevationEdit_PreservesLayoutIdentityWhilePhysicalKeySetMayChange()
    {
        var points = (RoofPoint2D[])FixtureMatrix().Single(fixture =>
            (string)fixture[0] == "Asymmetric U")[1];
        var solved = Solve(points);
        var heights = NoncriticalBandHeights(solved.Geometry);
        var low = Plan(solved, IntermediateLayout(LayoutIdA, heights[0]));
        var high = Plan(solved, IntermediateLayout(LayoutIdA, heights[^1]));

        Assert.All(low.Items.Concat(high.Items), item => Assert.Equal(LayoutIdA, item.LayoutItemId));
        Assert.Equal(8, low.Items.Count);
        Assert.Equal(4, high.Items.Count);
        Assert.NotEqual(Keys(low), Keys(high));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public void InvalidLayoutItemId_FailsAtomically(string id)
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);
        var result = Create(solved, IntermediateLayout(id, 100d));

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(string.IsNullOrWhiteSpace(id)
                ? RoofAutomaticPurlinPlanError.EmptyLayoutItemId
                : RoofAutomaticPurlinPlanError.MalformedLayoutItemId,
            result.Error);
    }

    [Fact]
    public void DuplicateLayoutItemId_IsCaseInsensitiveGuidIdentityAndFailsAtomically()
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);
        var result = Create(solved, new(false,
        [
            HeightItem(LayoutIdA, true, 100d),
            HeightItem(LayoutIdA.ToUpperInvariant(), true, 200d),
        ]));

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(RoofAutomaticPurlinPlanError.DuplicateLayoutItemId, result.Error);
    }

    [Theory]
    [InlineData(double.NaN, RoofAutomaticPurlinPlanError.InvalidPlacementValue)]
    [InlineData(double.PositiveInfinity, RoofAutomaticPurlinPlanError.InvalidPlacementValue)]
    [InlineData(0d, RoofAutomaticPurlinPlanError.InvalidPlacementValue)]
    [InlineData(-1d, RoofAutomaticPurlinPlanError.InvalidPlacementValue)]
    public void InvalidOrNonpositivePlacement_FailsAtomically(
        double elevation,
        RoofAutomaticPurlinPlanError expected)
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);
        var result = Create(solved, IntermediateLayout(LayoutIdA, elevation));

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public void ElevationAtOrAboveRise_FailsAtomically()
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);

        foreach (var elevation in new[] { solved.Geometry.RiseMm, solved.Geometry.RiseMm + 1d })
        {
            var result = Create(solved, IntermediateLayout(LayoutIdA, elevation));
            Assert.False(result.IsValid);
            Assert.Null(result.Plan);
            Assert.Equal(RoofAutomaticPurlinPlanError.ElevationOutsideRoof, result.Error);
        }
    }

    [Fact]
    public void CriticalInternalNodeElevationAndToleranceNeighborhood_AreRejected()
    {
        var points = (RoofPoint2D[])FixtureMatrix().Single(fixture =>
            (string)fixture[0] == "Asymmetric U")[1];
        var solved = Solve(points);
        var critical = solved.Geometry.Topology.Nodes
            .Skip(solved.Geometry.Topology.BoundaryVertexCount)
            .Select(node => node.Z)
            .Where(z => z > 0d && z < solved.Geometry.RiseMm)
            .Min();

        foreach (var elevation in new[]
                 {
                     critical,
                     critical - RoofAutomaticPurlinPlanner.CoordinateToleranceMm,
                     critical + RoofAutomaticPurlinPlanner.CoordinateToleranceMm,
                 })
        {
            var result = Create(solved, IntermediateLayout(LayoutIdA, elevation));
            Assert.False(result.IsValid);
            Assert.Null(result.Plan);
            Assert.Equal(RoofAutomaticPurlinPlanError.CriticalEventElevation, result.Error);
        }
    }

    [Theory]
    [MemberData(nameof(FixtureMatrix))]
    public void GeneratedKeySet_IsInvariantUnderRawPermutationAndWcsRotation(
        string name,
        RoofPoint2D[] points,
        int expectedRidgeCount,
        int[] expectedIntermediateCounts)
    {
        var count = points.Length;
        var baselineOrder = Enumerable.Range(0, count).ToArray();
        var reverseOrder = new[] { 0 }.Concat(Enumerable.Range(1, count - 1).Reverse()).ToArray();
        var variants = new[]
        {
            SolveVariant(points, baselineOrder, 0d),
            SolveVariant(points, Enumerable.Range(0, count).Select(index => (index + 2) % count).ToArray(), 0d),
            SolveVariant(points, reverseOrder, 0d),
            SolveVariant(points, baselineOrder, 90d),
            SolveVariant(points, baselineOrder, 37d),
        };
        var elevation = NoncriticalBandHeights(variants[0].Geometry)[0];
        var expected = Keys(Plan(variants[0], new(true,
        [
            HeightItem(LayoutIdA, true, elevation),
        ])));

        Assert.All(variants, variant =>
        {
            var actual = Keys(Plan(variant, new(true,
            [
                HeightItem(LayoutIdA, true, elevation),
            ])));
            Assert.Equal(expected, actual);
        });
        Assert.NotEmpty(expected);
        Assert.Equal(expectedRidgeCount + expectedIntermediateCounts[0], expected.Length);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void IntermediateKeys_AreCoordinateFreeCanonicalAndCoverSemanticBoundaries()
    {
        var observedKinds = new HashSet<RoofTopologyEdgeKind>();
        foreach (var fixture in FixtureMatrix())
        {
            var solved = Solve((RoofPoint2D[])fixture[1]);
            foreach (var height in NoncriticalBandHeights(solved.Geometry))
            {
                var plan = Plan(solved, IntermediateLayout(LayoutIdA, height));
                foreach (var key in plan.Items.Select(item =>
                             Assert.IsType<RoofAutomaticPurlinIntermediateKey>(item.GeneratedKey)))
                {
                    Assert.DoesNotContain(",", key.ToString());
                    Assert.DoesNotContain(".", key.ToString());
                    Assert.True(key.SourceFaceBoundaryEdgeId > 0);
                    Assert.True(Compare(key.EndpointBoundaryKeyA, key.EndpointBoundaryKeyB) <= 0);
                    observedKinds.Add(key.EndpointBoundaryKeyA.Kind);
                    observedKinds.Add(key.EndpointBoundaryKeyB.Kind);
                }
            }
        }

        Assert.Contains(RoofTopologyEdgeKind.Hip, observedKinds);
        Assert.Contains(RoofTopologyEdgeKind.Valley, observedKinds);
        Assert.Contains(RoofTopologyEdgeKind.Ridge, observedKinds);
    }

    [Fact]
    public void CoplanarSeam_IsAStableClippingBoundaryAndIntervalsAreNotMergedAcrossIt()
    {
        var points = Points(
            (0, 0), (4000, 0), (4000, 1500), (8000, 1500),
            (8000, 0), (14000, 0), (14000, 8000), (8000, 8000),
            (8000, 3500), (4000, 3500), (4000, 6000), (0, 6000));
        var solved = Solve(points);
        Assert.Contains(solved.Geometry.Topology.Edges, edge =>
            edge.Kind == RoofTopologyEdgeKind.CoplanarSeam);
        var plans = NoncriticalBandHeights(solved.Geometry)
            .Select(height => Plan(solved, IntermediateLayout(LayoutIdA, height)))
            .ToArray();
        var seamTerminated = plans
            .SelectMany(plan => plan.Items)
            .Select(item => Assert.IsType<RoofAutomaticPurlinIntermediateKey>(item.GeneratedKey))
            .Where(key => key.EndpointBoundaryKeyA.Kind == RoofTopologyEdgeKind.CoplanarSeam ||
                          key.EndpointBoundaryKeyB.Kind == RoofTopologyEdgeKind.CoplanarSeam)
            .ToArray();

        Assert.Equal(points.Length, plans[0].Items.Count);
        Assert.NotEmpty(seamTerminated);
        Assert.True(seamTerminated.Length % 2 == 0);
    }

    [Fact]
    public void IncompleteBoundaryProvenance_FailsAtomically()
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);
        var incomplete = solved.Provenance with
        {
            EdgeProvenance = solved.Provenance.EdgeProvenance.Skip(1).ToArray(),
        };

        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            incomplete,
            IntermediateLayout(LayoutIdA, 100d),
            PlanningInput());

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(RoofAutomaticPurlinPlanError.BoundaryProvenanceCountMismatch, result.Error);
    }

    [Fact]
    public void NonfiniteTopologyCoordinate_FailsAtomically()
    {
        var malformed = MalformedGeometry(
        [
            new(double.NaN, 0d, 0d),
            new(10d, 0d, 0d),
            new(10d, 10d, 10d),
            new(0d, 10d, 10d),
        ],
        duplicateFace: false);
        var result = RoofAutomaticPurlinPlanner.Create(
            malformed.Geometry,
            malformed.Provenance,
            IntermediateLayout(LayoutIdA, 5d),
            PlanningInput());

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(RoofAutomaticPurlinPlanError.InvalidCoordinate, result.Error);
    }

    [Fact]
    public void ZeroLengthSlice_FailsAtomically()
    {
        var malformed = MalformedGeometry(
        [
            new(0d, 0d, 0d),
            new(10d, 0d, 0d),
            new(0d, 10d, 10d),
            new(10d, 10d, 10d),
        ],
        duplicateFace: false);
        var result = RoofAutomaticPurlinPlanner.Create(
            malformed.Geometry,
            malformed.Provenance,
            IntermediateLayout(LayoutIdA, 5d),
            PlanningInput());

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(RoofAutomaticPurlinPlanError.ZeroLengthSegment, result.Error);
    }

    [Fact]
    public void DuplicatePhysicalGeneratedKey_FailsAtomicallyWithoutOrdinalSuffix()
    {
        var malformed = MalformedGeometry(
        [
            new(0d, 0d, 0d),
            new(10d, 0d, 0d),
            new(10d, 10d, 10d),
            new(0d, 10d, 10d),
        ],
        duplicateFace: true);
        var result = RoofAutomaticPurlinPlanner.Create(
            malformed.Geometry,
            malformed.Provenance,
            IntermediateLayout(LayoutIdA, 5d),
            PlanningInput());

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(RoofAutomaticPurlinPlanError.DuplicateGeneratedKey, result.Error);
        Assert.NotNull(result.DuplicateGeneratedKey);
    }

    [Fact]
    public void NullInputsFailWithoutPartialPlan()
    {
        var solved = Solve((RoofPoint2D[])FixtureMatrix().First()[1]);

        Assert.Equal(RoofAutomaticPurlinPlanError.InvalidGeometry,
            RoofAutomaticPurlinPlanner.Create(null, solved.Provenance, new(false, []), PlanningInput()).Error);
        Assert.Equal(RoofAutomaticPurlinPlanError.InvalidBoundaryProvenance,
            RoofAutomaticPurlinPlanner.Create(solved.Geometry, null, new(false, []), PlanningInput()).Error);
        Assert.Equal(RoofAutomaticPurlinPlanError.InvalidLayout,
            RoofAutomaticPurlinPlanner.Create(solved.Geometry, solved.Provenance, null, PlanningInput()).Error);
    }

    [Theory]
    [InlineData(0d, 160d)]
    [InlineData(double.NaN, 160d)]
    [InlineData(220d, 0d)]
    [InlineData(220d, double.PositiveInfinity)]
    public void InvalidPhysicalSectionDimensionsFailBeforePlanning(
        double purlinHeightMm,
        double rafterHeightMm)
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false,
            [
                DistanceItem(RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave, 500d),
            ]),
            PlanningInput() with
            {
                PurlinHeightMm = purlinHeightMm,
                RafterHeightMm = rafterHeightMm,
            });

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(RoofAutomaticPurlinPlanError.InvalidPhysicalSection, result.Error);
    }

    [Theory]
    [InlineData(RoofAutomaticPurlinSeatingDepthMode.None, 25d)]
    [InlineData(RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight, 0d)]
    [InlineData(RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight, 100d)]
    [InlineData(RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm, 0d)]
    [InlineData(RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm, 160d)]
    public void DistanceMode_InvalidSeatingFailsWithoutPartialPlan(
        RoofAutomaticPurlinSeatingDepthMode mode,
        double value)
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var item = DistanceItem(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            500d) with
        {
            SeatingDepth = new RoofAutomaticPurlinSeatingDepth(mode, value),
        };

        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false, [item]),
            PlanningInput());

        Assert.False(result.IsValid);
        Assert.Null(result.Plan);
        Assert.Equal(RoofAutomaticPurlinPlanError.InvalidSeatingDepth, result.Error);
    }

    [Fact]
    public void BottomEdgeHeight_UsesEffectivePurlinHeightAndRelativeDatum()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var input = new RoofAutomaticPurlinPlanningInput(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.WallPlateBottom,
                3580d,
                0d),
            220d,
            160d);
        var layout = new RoofAutomaticPurlinLayout(false,
        [
            new RoofAutomaticPurlinLayoutItem(
                LayoutIdA,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                500d),
        ]);

        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            input);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.NotEmpty(result.Plan!.Items);
        Assert.All(result.Plan.Items, item =>
        {
            var elevation = Assert.IsType<RoofPurlinElevationProfile>(item.ElevationProfile);
            Assert.Equal(500d, elevation.BottomLocalZMm);
            Assert.Equal(610d, elevation.CenterLocalZMm);
            Assert.Equal(720d, elevation.TopLocalZMm);
            Assert.Equal(4080d, elevation.BottomRelativeElevationMm);
            Assert.Equal(4190d, elevation.CenterRelativeElevationMm);
            Assert.Equal(4300d, elevation.TopRelativeElevationMm);
        });
    }

    [Fact]
    public void PlanDistanceFromEave_UsesHorizontalStationAndPhysicalSeatingPlacement()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var item = DistanceItem(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            1000d);

        var resolution = RoofAutomaticPurlinPlanner.ResolvePlacement(
            solved.Geometry,
            solved.Provenance,
            item,
            PlanningInput() with { RafterHeightMm = 160d });

        Assert.True(resolution.IsValid);
        Assert.Equal(1000d * Math.Tan(Math.PI / 6d), resolution.RoofSurfaceLocalZMm!.Value, 8);
        Assert.Equal(40d, resolution.SeatingDepthMm);
        var planned = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false, [item]),
            PlanningInput() with { RafterHeightMm = 160d });
        Assert.True(planned.IsValid, planned.Error.ToString());
        Assert.All(planned.Plan!.Items, plannedItem =>
        {
            var physical = Assert.IsType<RoofPurlinPhysicalPlacement>(plannedItem.PhysicalPlacement);
            Assert.Equal(40d, physical.SeatingDepthMm, 9);
            Assert.Equal(resolution.RoofSurfaceLocalZMm.Value, physical.RafterCenterLocalZMm, 8);
            Assert.Equal(
                resolution.RoofSurfaceLocalZMm.Value - 80d * Math.Cos(Math.PI / 6d),
                physical.RafterLowerSurfaceLocalZMm,
                8);
            Assert.Equal(
                resolution.RoofSurfaceLocalZMm.Value +
                Math.Cos(Math.PI / 6d) * (40d - 80d),
                physical.PurlinTopLocalZMm,
                8);
        });
    }

    [Fact]
    public void RidgeDistance_AutoResolvesExactlyOneButRequiresKeyForMultiple()
    {
        var rectangle = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var item = DistanceItem(RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge, 500d);
        var single = RoofAutomaticPurlinPlanner.ResolvePlacement(
            rectangle.Geometry,
            rectangle.Provenance,
            item,
            PlanningInput());
        Assert.True(single.IsValid);
        Assert.NotNull(single.ResolvedReferenceRidgeKey);
        Assert.Equal(
            rectangle.Geometry.RiseMm - 500d * Math.Tan(Math.PI / 6d),
            single.RoofSurfaceLocalZMm!.Value,
            8);

        var multi = Solve(Points(
            (0, 0), (8000, 0), (8000, 3000),
            (3000, 3000), (3000, 8000), (0, 8000)));
        var ambiguous = RoofAutomaticPurlinPlanner.ResolvePlacement(
            multi.Geometry,
            multi.Provenance,
            item,
            PlanningInput());
        Assert.False(ambiguous.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.ReferenceRidgeRequired, ambiguous.Error);

        var structural = RoofStructuralEdgeIdentityResolver.Resolve(
            multi.Geometry,
            multi.Provenance);
        var reference = structural.Edges.First(edge => edge.StructuralRole == RoofStructuralRole.Ridge);
        var explicitResult = RoofAutomaticPurlinPlanner.ResolvePlacement(
            multi.Geometry,
            multi.Provenance,
            item with { ReferenceRidgeKey = reference.StructuralIdentity },
            PlanningInput());
        Assert.True(explicitResult.IsValid);
        Assert.Equal(reference.StructuralIdentity, explicitResult.ResolvedReferenceRidgeKey);
    }

    [Fact]
    public void RidgeDistancePreview_AppliesPhysicalSeatingAndRemainsHorizontal()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var item = DistanceItem(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
            500d);

        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false, [item]),
            PlanningInput() with { PurlinHeightMm = 220d });

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.NotEmpty(result.Plan!.Items);
        var expectedRafterCenter = solved.Geometry.RiseMm -
            500d * Math.Tan(Math.PI / 6d);
        var expectedPurlinTop = expectedRafterCenter +
            Math.Cos(Math.PI / 6d) * (40d - 80d);
        Assert.All(result.Plan.Items, planned =>
        {
            var physical = Assert.IsType<RoofPurlinPhysicalPlacement>(planned.PhysicalPlacement);
            Assert.Equal(expectedRafterCenter, physical.RafterCenterLocalZMm, 8);
            Assert.Equal(expectedPurlinTop, physical.PurlinTopLocalZMm, 8);
            Assert.Equal(expectedPurlinTop - 110d, planned.Segment3D.Start.Z, 8);
            Assert.Equal(planned.Segment3D.Start.Z, planned.Segment3D.End.Z, 10);
        });
    }

    [Fact]
    public void SeatingEdit_PreservesLayoutAndGeneratedIdentityWhileChangingOnlyElevation()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var original = DistanceItem(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            1000d);
        var edited = original with
        {
            SeatingDepth = new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                30d),
        };

        var before = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false, [original]),
            PlanningInput());
        var after = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false, [edited]),
            PlanningInput());

        Assert.True(before.IsValid, before.Error.ToString());
        Assert.True(after.IsValid, after.Error.ToString());
        Assert.Equal(original.LayoutItemId, edited.LayoutItemId);
        Assert.Equal(
            before.Plan!.Items.Select(item => item.GeneratedKey),
            after.Plan!.Items.Select(item => item.GeneratedKey));
        Assert.Equal(
            before.Plan.Items.Select(item => (item.Segment3D.Start.X, item.Segment3D.Start.Y,
                item.Segment3D.End.X, item.Segment3D.End.Y)),
            after.Plan.Items.Select(item => (item.Segment3D.Start.X, item.Segment3D.Start.Y,
                item.Segment3D.End.X, item.Segment3D.End.Y)));
        Assert.All(before.Plan.Items.Zip(after.Plan.Items), pair =>
            Assert.NotEqual(pair.First.Segment3D.Start.Z, pair.Second.Segment3D.Start.Z));
    }

    [Fact]
    public void RidgeDistance_MissingStableKeyFailsClosed()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var item = DistanceItem(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
            500d) with
        {
            ReferenceRidgeKey = new RoofStructuralLogicalKey(RoofStructuralRole.Ridge, 999, 1000),
        };

        var result = RoofAutomaticPurlinPlanner.ResolvePlacement(
            solved.Geometry,
            solved.Provenance,
            item,
            PlanningInput());

        Assert.False(result.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.ReferenceRidgeNotFound, result.Error);
    }

    [Fact]
    public void RidgeDistance_InclinedReferenceRidgeFailsClosed()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var inclined = InclineFirstRidge(solved.Geometry);

        var result = RoofAutomaticPurlinPlanner.ResolvePlacement(
            inclined,
            solved.Provenance,
            DistanceItem(RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge, 500d),
            PlanningInput());

        Assert.False(result.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.InclinedReferenceRidge, result.Error);
    }

    [Fact]
    public void PreviewHeightMode_UsesLowerEdgeSemanticsAndRectangleProducesFiveSegments()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            3580d,
            0d);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new RoofAutomaticPurlinLayoutItem(
                LayoutIdA,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                500d),
        ]);

        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(datum, 220d, 160d));

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(5, result.Plan!.Items.Count);
        var intermediate = result.Plan.Items.Where(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate).ToArray();
        Assert.Equal(4, intermediate.Length);
        Assert.All(intermediate, item =>
        {
            Assert.Equal(610d, item.Segment3D.Start.Z, 9);
            Assert.Equal(610d, item.Segment3D.End.Z, 9);
            var profile = Assert.IsType<RoofPurlinElevationProfile>(item.ElevationProfile);
            Assert.Equal(500d, profile.BottomLocalZMm, 9);
            Assert.Equal(610d, profile.CenterLocalZMm, 9);
            Assert.Equal(720d, profile.TopLocalZMm, 9);
            Assert.Equal(4080d, profile.BottomRelativeElevationMm, 9);
            Assert.Equal(4190d, profile.CenterRelativeElevationMm, 9);
            Assert.Equal(4300d, profile.TopRelativeElevationMm, 9);
            Assert.Null(profile.SeatingDepthMm);
        });
    }

    [Fact]
    public void PlanDistancePreview_AppliesNormalProjectedSeatingWithoutMovingPlanStation()
    {
        var solved = Solve(Points((0, 0), (10000, 0), (10000, 6000), (0, 6000)));
        var item = DistanceItem(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            1000d);
        var input = new RoofAutomaticPurlinPlanningInput(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d),
            220d,
            160d);

        var preview = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false, [item]),
            input);
        Assert.True(preview.IsValid, preview.Error.ToString());
        var roofCenter = 1000d * Math.Tan(Math.PI / 6d);
        var expectedTop = roofCenter + Math.Cos(Math.PI / 6d) * (40d - 80d);
        Assert.All(preview.Plan!.Items, planned =>
        {
            var profile = Assert.IsType<RoofPurlinElevationProfile>(planned.ElevationProfile);
            var physical = Assert.IsType<RoofPurlinPhysicalPlacement>(planned.PhysicalPlacement);
            Assert.Equal(expectedTop - 220d, profile.BottomLocalZMm, 8);
            Assert.Equal(expectedTop - 110d, profile.CenterLocalZMm, 8);
            Assert.Equal(expectedTop, profile.TopLocalZMm, 8);
            Assert.Equal(40d, profile.SeatingDepthMm);
            Assert.Equal(roofCenter, physical.RafterCenterLocalZMm, 8);
            Assert.Equal(physical.PurlinCenterLocalZMm, planned.Segment3D.Start.Z, 8);
            Assert.Equal(physical.PurlinCenterLocalZMm, planned.Segment3D.End.Z, 8);
            Assert.NotEqual(roofCenter, planned.Segment3D.Start.Z);
        });
        Assert.Single(preview.Plan.Items.Select(planned => planned.Segment3D.Start.Z)
            .DistinctBy(value => Math.Round(value, 9)));
    }

    private static RoofAutomaticPurlinLayoutItem DistanceItem(
        RoofAutomaticPurlinPlacementMode mode,
        double distanceMm) => new(
            LayoutIdA,
            true,
            mode,
            distanceMm,
            null,
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                25d));

    private static RoofAutomaticPurlinLayout IntermediateLayout(string id, double elevation) =>
        new(false, [HeightItem(id, true, elevation)]);

    private static RoofAutomaticPurlinLayoutItem HeightItem(
        string id,
        bool enabled,
        double centerLocalZMm) => new(
            id,
            enabled,
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            centerLocalZMm - 1d);

    private static RoofAutomaticPurlinPlanningInput PlanningInput() => new(
        new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d),
        2d,
        160d);

    private static RoofAutomaticPurlinPlanResult Create(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout) =>
        RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            PlanningInput());

    private static RoofAutomaticPurlinPlan Plan(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout)
    {
        var result = Create(solved, layout);
        Assert.True(result.IsValid, result.Error + ": " + result.FailedLayoutItemId);
        return Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);
    }

    private static SolvedFixture Solve(
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
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(geometryResult.IsValid, geometryResult.Error.ToString());
        return new SolvedFixture(
            Assert.IsType<HipRoofGeometry>(geometryResult.Geometry),
            provenance);
    }

    private static SolvedFixture SolveVariant(
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
        return Solve(transformed, ids);
    }

    private static int PhysicalEdgeId(int first, int second, int count)
    {
        if ((first + 1) % count == second) return first + 1;
        if ((second + 1) % count == first) return second + 1;
        throw new InvalidOperationException("Raw order is not a polygon boundary cycle.");
    }

    private static double[] NoncriticalBandHeights(HipRoofGeometry geometry)
    {
        var critical = geometry.Topology.Nodes
            .Skip(geometry.Topology.BoundaryVertexCount)
            .Select(node => node.Z)
            .Where(height => height > 0d && height < geometry.RiseMm)
            .OrderBy(height => height)
            .Aggregate(new List<double>(), (values, height) =>
            {
                if (values.Count == 0 ||
                    Math.Abs(values[^1] - height) > RoofAutomaticPurlinPlanner.CoordinateToleranceMm)
                {
                    values.Add(height);
                }
                return values;
            });
        var bounds = new[] { 0d }.Concat(critical).Append(geometry.RiseMm).ToArray();
        return Enumerable.Range(0, bounds.Length - 1)
            .Where(index => bounds[index + 1] - bounds[index] >
                            2d * RoofAutomaticPurlinPlanner.CoordinateToleranceMm)
            .Select(index => (bounds[index] + bounds[index + 1]) / 2d)
            .ToArray();
    }

    private static string[] Keys(RoofAutomaticPurlinPlan plan) =>
        plan.Items.Select(item => item.GeneratedKey.ToString()).Order().ToArray();

    private static int Compare(
        RoofAutomaticPurlinBoundaryKey first,
        RoofAutomaticPurlinBoundaryKey second)
    {
        var kind = first.Kind.CompareTo(second.Kind);
        if (kind != 0) return kind;
        var a = first.BoundaryEdgeIdA.CompareTo(second.BoundaryEdgeIdA);
        return a != 0 ? a : first.BoundaryEdgeIdB.CompareTo(second.BoundaryEdgeIdB);
    }

    private static (HipRoofGeometry Geometry, RoofBoundaryIdentityProvenanceResult Provenance)
        MalformedGeometry(IReadOnlyList<RoofPoint3D> nodes, bool duplicateFace)
    {
        var edgeType = typeof(RoofTopologyEdge);
        RoofTopologyEdge Edge(int start, int end) => (RoofTopologyEdge)Activator.CreateInstance(
            edgeType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [start, end, RoofTopologyEdgeKind.Eave, new[] { 0 }],
            culture: null)!;
        var faceType = typeof(RoofTopologyFace);
        RoofTopologyFace Face() => (RoofTopologyFace)Activator.CreateInstance(
            faceType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [0, new[] { 0, 1, 2, 3 }],
            culture: null)!;
        var edges = new[] { Edge(0, 1), Edge(1, 2), Edge(2, 3), Edge(3, 0) };
        var faces = duplicateFace ? new[] { Face(), Face() } : new[] { Face() };
        var topology = (RoofTopology)Activator.CreateInstance(
            typeof(RoofTopology),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [nodes, edges, faces, 4, 30d],
            culture: null)!;
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        var geometry = (HipRoofGeometry)Activator.CreateInstance(
            typeof(HipRoofGeometry),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [topology, direction, 0],
            culture: null)!;
        var provenance = new RoofBoundaryIdentityProvenanceResult(
            true,
            null,
            Enumerable.Range(0, 4).Select(index =>
                new RoofBoundaryEdgeProvenance(index, index, index + 1)).ToArray(),
            RoofValidationError.None,
            RoofBoundaryIdentityError.None);
        return (geometry, provenance);
    }

    private static HipRoofGeometry InclineFirstRidge(HipRoofGeometry source)
    {
        var ridge = source.Topology.Edges.First(edge => edge.Kind == RoofTopologyEdgeKind.Ridge);
        var nodes = source.Topology.Nodes.ToArray();
        var endpoint = nodes[ridge.EndNodeIndex];
        nodes[ridge.EndNodeIndex] = endpoint with { Z = endpoint.Z + 10d };

        var edges = source.Topology.Edges.Select(edge =>
            (RoofTopologyEdge)Activator.CreateInstance(
                typeof(RoofTopologyEdge),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [edge.StartNodeIndex, edge.EndNodeIndex, edge.Kind, edge.FaceIndices],
                culture: null)!).ToArray();
        var faces = source.Topology.Faces.Select(face =>
            (RoofTopologyFace)Activator.CreateInstance(
                typeof(RoofTopologyFace),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [face.SourceEdgeIndex, face.BoundaryNodeIndices],
                culture: null)!).ToArray();
        var topology = (RoofTopology)Activator.CreateInstance(
            typeof(RoofTopology),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                nodes,
                edges,
                faces,
                source.Topology.BoundaryVertexCount,
                source.Topology.PitchDegrees,
            ],
            culture: null)!;
        return (HipRoofGeometry)Activator.CreateInstance(
            typeof(HipRoofGeometry),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [topology, source.OrientationDirection, 0],
            culture: null)!;
    }

    private static RoofPoint2D[] Points(params (double X, double Y)[] points) =>
        points.Select(point => new RoofPoint2D(point.X, point.Y)).ToArray();

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
