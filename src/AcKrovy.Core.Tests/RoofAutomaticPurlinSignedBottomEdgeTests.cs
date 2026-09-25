using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// HOST: ExplicitLocalPlane at LocalZ=1000 / relative 0 must accept signed BottomEdge
/// offsets that remain physically inside the roof (negative = below the datum).
/// </summary>
public sealed class RoofAutomaticPurlinSignedBottomEdgeTests
{
    private const string IntermediateId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string IntermediateIdB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Theory]
    [InlineData(45d)]
    [InlineData(50d)]
    public void WallPlate_ExplicitLocalPlane_SignedOffsets_MatchRelativeAndPhysicalBottom(
        double pitchDegrees)
    {
        var solved = Solve(pitchDegrees);
        var datum = ElevatedDatum();
        foreach (var offsetMm in new[] { -200d, 0d, 20d })
        {
            var result = PlanWallPlate(solved, datum, offsetMm);
            Assert.True(result.IsValid, $"{pitchDegrees}° offset {offsetMm}: {result.Error}");
            var walls = result.Plan!.Items
                .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
                .ToArray();
            Assert.NotEmpty(walls);
            Assert.All(walls, item =>
            {
                var profile = Assert.IsType<RoofPurlinElevationProfile>(item.ElevationProfile);
                Assert.Equal(datum.ReferenceLocalZMm + offsetMm, profile.BottomLocalZMm, 6);
                Assert.Equal(offsetMm, profile.BottomRelativeElevationMm, 6);
                Assert.Equal(offsetMm + 70d, profile.CenterRelativeElevationMm, 6);
                Assert.Equal(offsetMm + 140d, profile.TopRelativeElevationMm, 6);
            });
        }
    }

    [Theory]
    [InlineData(45d)]
    [InlineData(50d)]
    public void Intermediate_ExplicitLocalPlane_SignedOffsets_MatchRelativeAndPhysicalBottom(
        double pitchDegrees)
    {
        var solved = Solve(pitchDegrees);
        var datum = ElevatedDatum();
        foreach (var offsetMm in new[] { -200d, 0d, 20d })
        {
            var result = PlanIntermediate(solved, datum, offsetMm);
            Assert.True(result.IsValid, $"{pitchDegrees}° offset {offsetMm}: {result.Error}");
            var intermediates = result.Plan!.Items
                .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
                .ToArray();
            Assert.NotEmpty(intermediates);
            Assert.All(intermediates, item =>
            {
                var profile = Assert.IsType<RoofPurlinElevationProfile>(item.ElevationProfile);
                Assert.Equal(datum.ReferenceLocalZMm + offsetMm, profile.BottomLocalZMm, 6);
                Assert.Equal(offsetMm, profile.BottomRelativeElevationMm, 6);
                Assert.Equal(offsetMm + 70d, profile.CenterRelativeElevationMm, 6);
                Assert.Equal(offsetMm + 140d, profile.TopRelativeElevationMm, 6);
            });
        }
    }

    [Fact]
    public void SignedOffset_SamePhysicalPlacement_InvariantAcrossDatumChange()
    {
        var solved = Solve(50d);
        // Physical bottom at LocalZ 800 mm.
        var elevated = PlanWallPlate(
            solved,
            ElevatedDatum(),
            placementOffsetMm: -200d);
        var sourceEave = PlanWallPlate(
            solved,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d),
            placementOffsetMm: 800d);

        Assert.True(elevated.IsValid, elevated.Error.ToString());
        Assert.True(sourceEave.IsValid, sourceEave.Error.ToString());

        var elevatedBottom = elevated.Plan!.Items
            .First(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ElevationProfile!.BottomLocalZMm;
        var eaveBottom = sourceEave.Plan!.Items
            .First(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ElevationProfile!.BottomLocalZMm;
        Assert.Equal(800d, elevatedBottom, 6);
        Assert.Equal(800d, eaveBottom, 6);
        Assert.Equal(elevatedBottom, eaveBottom, 6);
    }

    [Fact]
    public void SignedOffset_BelowPhysicalRoof_IsRejectedWithGeometricError()
    {
        var solved = Solve(50d);
        // Far below eave LocalZ — offset -2000 from elevated 1000 ⇒ physical bottom -1000.
        // WallPlate may fail first on min plan-distance (width/2) once the axis station
        // resolves near the eave; Intermediate still hits pure geometric rejection.
        var wallPlate = PlanWallPlate(solved, ElevatedDatum(), placementOffsetMm: -2000d);
        Assert.False(wallPlate.IsValid);
        Assert.True(
            wallPlate.Error is
                RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum or
                RoofAutomaticPurlinPlanError.ElevationOutsideRoof or
                RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
            wallPlate.Error.ToString());

        var intermediate = PlanIntermediate(solved, ElevatedDatum(), placementOffsetMm: -2000d);
        Assert.False(intermediate.IsValid);
        Assert.True(
            intermediate.Error is
                RoofAutomaticPurlinPlanError.ElevationOutsideRoof or
                RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
            intermediate.Error.ToString());
    }

    [Fact]
    public void SignedOffset_AboveRise_IsRejectedWithGeometricError()
    {
        var solved = Solve(50d);
        var result = PlanWallPlate(
            solved,
            ElevatedDatum(),
            placementOffsetMm: solved.Geometry.RiseMm + 500d);
        Assert.False(result.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.ElevationOutsideRoof, result.Error);
    }

    [Fact]
    public void Persistence_RoundTripsNegativeWallPlateAndIntermediateBottomEdge()
    {
        var layout = new RoofAutomaticPurlinLayout(
            false,
            [
                new RoofAutomaticPurlinLayoutItem(
                    IntermediateId,
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    -200d,
                    null,
                    new RoofAutomaticPurlinSeatingDepth(
                        RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                        25d),
                    140d,
                    140d),
            ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                -200d,
                null,
                new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    25d),
                140d,
                140d),
        };

        var written = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        Assert.True(written.IsValid, written.Error.ToString());
        Assert.Equal(-200d, written.Layout!.WallPlatePlacement!.PlacementValueMm);
        Assert.Equal(-200d, written.Layout.IntermediateItems[0].PlacementValueMm);

        var storedWall = RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
            written.Layout.WallPlatePlacement);
        var storedIntermediate = new RoofPurlinLayoutStoredItem(
            IntermediateId,
            1,
            RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
            -200d,
            RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
            0,
            0,
            RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
            25d,
            140d,
            140d);

        var reread = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            0,
            [storedIntermediate],
            1,
            written.Layout.WallPlateLowerEdgeHeightMm,
            storedWall,
            null,
            null);
        Assert.True(reread.IsValid, reread.Error.ToString());
        Assert.Equal(-200d, reread.Layout!.WallPlatePlacement!.PlacementValueMm);
        Assert.Equal(-200d, reread.Layout.IntermediateItems[0].PlacementValueMm);
    }

    [Fact]
    public void TwoIndependentIntermediates_AcceptDistinctSignedOffsets()
    {
        var solved = Solve(50d);
        var idB = IntermediateIdB;
        var layout = new RoofAutomaticPurlinLayout(
            false,
            [
                BottomEdgeItem(IntermediateId, -200d),
                BottomEdgeItem(idB, 20d),
            ]);
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            HostPlanningInput(ElevatedDatum()));
        Assert.True(result.IsValid, result.Error.ToString());
        var byId = result.Plan!.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .GroupBy(item => item.LayoutItemId!)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Assert.All(byId[IntermediateId], item =>
            Assert.Equal(-200d, item.ElevationProfile!.BottomRelativeElevationMm, 6));
        Assert.All(byId[idB], item =>
            Assert.Equal(20d, item.ElevationProfile!.BottomRelativeElevationMm, 6));
    }

    private static RoofAutomaticPurlinPlanResult PlanWallPlate(
        SolvedFixture solved,
        RoofRelativeElevationDatum datum,
        double placementOffsetMm)
    {
        var layout = new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = BottomEdgeItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                placementOffsetMm),
        };
        return RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            HostPlanningInput(datum));
    }

    private static RoofAutomaticPurlinPlanResult PlanIntermediate(
        SolvedFixture solved,
        RoofRelativeElevationDatum datum,
        double placementOffsetMm) =>
        RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            new RoofAutomaticPurlinLayout(false, [BottomEdgeItem(IntermediateId, placementOffsetMm)]),
            HostPlanningInput(datum));

    private static RoofAutomaticPurlinLayoutItem BottomEdgeItem(string id, double offsetMm) =>
        new(
            id,
            true,
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            offsetMm,
            null,
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                25d),
            140d,
            140d);

    private static RoofAutomaticPurlinPlanningInput HostPlanningInput(
        RoofRelativeElevationDatum datum) =>
        new(datum, 140d, 100d)
        {
            WallPlatesEnabled = true,
            WallPlateWidthMm = 140d,
            WallPlateHeightMm = 140d,
        };

    private static RoofRelativeElevationDatum ElevatedDatum() =>
        new(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);

    private static SolvedFixture Solve(double pitchDegrees)
    {
        var points = new RoofPoint2D[]
        {
            new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000),
        };
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometry = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(pitchDegrees),
            RoofKind.Hip));
        Assert.True(geometry.IsValid, geometry.Error.ToString());
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
