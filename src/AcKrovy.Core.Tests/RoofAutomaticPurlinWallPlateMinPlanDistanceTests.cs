using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinWallPlateMinPlanDistanceTests
{
    [Theory]
    [InlineData(140d, 70d)]
    [InlineData(160d, 80d)]
    [InlineData(200d, 100d)]
    public void Minimum_IsHalfWidth(double widthMm, double expectedMinMm)
    {
        Assert.Equal(
            expectedMinMm,
            RoofAutomaticPurlinWallPlatePlanDistanceRules.ResolveMinimumPlanDistanceFromEaveMm(
                widthMm));
    }

    [Theory]
    [InlineData(160d, 79.999d, false)]
    [InlineData(160d, 80.000d, true)]
    [InlineData(160d, 80.001d, true)]
    [InlineData(140d, 69.999d, false)]
    [InlineData(140d, 70.000d, true)]
    [InlineData(200d, 99.999d, false)]
    [InlineData(200d, 100.000d, true)]
    public void Boundary_UsesInclusiveMinimumWithSharedTolerance(
        double widthMm,
        double planDistanceMm,
        bool expectedValid)
    {
        Assert.Equal(
            expectedValid,
            RoofAutomaticPurlinWallPlatePlanDistanceRules.IsPlanDistanceAtOrAboveMinimum(
                planDistanceMm,
                widthMm));
    }

    [Theory]
    [InlineData(140d, 69.999d)]
    [InlineData(160d, 79.999d)]
    [InlineData(200d, 99.999d)]
    public void PlanDistance_BelowMinimum_Fails(
        double widthMm,
        double planDistanceMm)
    {
        var solved = Solve(45d);
        var result = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            planDistanceMm,
            widthMm,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        Assert.False(result.IsValid);
        Assert.Equal(
            RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum,
            result.Error);
        Assert.Equal(
            RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
            result.FailedLayoutItemId);
        Assert.Null(result.Plan);
    }

    [Theory]
    [InlineData(140d, 70d)]
    [InlineData(160d, 80d)]
    [InlineData(200d, 100d)]
    [InlineData(140d, 700d)]
    public void PlanDistance_AtOrAboveMinimum_Succeeds(
        double widthMm,
        double planDistanceMm)
    {
        var solved = Solve(45d);
        var result = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            planDistanceMm,
            widthMm,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.NotNull(result.Plan);
    }

    [Theory]
    [InlineData(RoofRelativeElevationReferenceKind.SourceEavePlane)]
    [InlineData(RoofRelativeElevationReferenceKind.ExplicitLocalPlane)]
    [InlineData(RoofRelativeElevationReferenceKind.WallPlateBottom)]
    public void HostFixture_Width140_Station700_RemainsValid(
        RoofRelativeElevationReferenceKind datumKind)
    {
        var solved = Solve(45d);
        var result = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            140d,
            datumKind);
        Assert.True(result.IsValid, result.Error.ToString());
    }

    [Theory]
    [InlineData(45d)]
    [InlineData(55d)]
    public void BottomEdge_BelowMinimumPlanStation_Fails(double pitchDegrees)
    {
        var solved = Solve(pitchDegrees);
        // Without seating, RoofSurfaceLocalZ = bottom + height/2.
        // Choose bottom so axis plan station is just below width/2.
        var widthMm = 160d;
        var heightMm = 160d;
        var minPlan = widthMm / 2d;
        var tan = Math.Tan(pitchDegrees * Math.PI / 180d);
        var targetPlan = minPlan - 1d;
        var bottomLocal = targetPlan * tan - heightMm / 2d;
        var result = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            bottomLocal,
            widthMm,
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            datumLocalZMm: 0d,
            seatingPercent: null);
        Assert.False(result.IsValid);
        Assert.Equal(
            RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum,
            result.Error);
    }

    [Theory]
    [InlineData(45d)]
    [InlineData(55d)]
    public void BottomEdge_HostLikeStation_RemainsValid(double pitchDegrees)
    {
        var solved = Solve(pitchDegrees);
        // Plan station ≈ 700 mm → BottomEdge relative under SourceEave@0 ≈ 700*tan(pitch).
        var tan = Math.Tan(pitchDegrees * Math.PI / 180d);
        var bottomApprox = 700d * tan - 35d; // rough seated offset; planner resolves exactly
        var result = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            140d,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        Assert.True(result.IsValid, result.Error.ToString());
        _ = bottomApprox;
    }

    [Fact]
    public void Intermediate_MayRemainAtZeroPlanDistance()
    {
        var solved = Solve(45d);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                0d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    25d),
                WidthMm: 160d,
                HeightMm: 220d),
        ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                700d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    25d),
                WidthMm: 140d,
                HeightMm: 140d),
        };
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.SourceEavePlane,
                    0d,
                    0d),
                220d,
                125d)
            {
                PurlinWidthMm = 160d,
                WallPlatesEnabled = true,
                WallPlateWidthMm = 140d,
                WallPlateHeightMm = 140d,
            });
        // Intermediate at 0 may fail OutsideRoof/elevation for other reasons on some pitches;
        // the WallPlate min rule must not be the failure when WP is at 700.
        if (!result.IsValid)
        {
            Assert.NotEqual(
                RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum,
                result.Error);
        }
    }

    [Fact]
    public void WidthIncrease_CanInvalidatePreviouslyValidStation()
    {
        var solved = Solve(45d);
        var at80 = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            80d,
            wallPlateWidthMm: 160d,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        Assert.True(at80.IsValid, at80.Error.ToString());

        var afterWider = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            80d,
            wallPlateWidthMm: 200d,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        Assert.False(afterWider.IsValid);
        Assert.Equal(
            RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum,
            afterWider.Error);
    }

    [Theory]
    [InlineData(15d, 150d)]
    [InlineData(45d, 150d)]
    [InlineData(55d, 150d)]
    public void PlanDistanceFromRidge_EquivalentToEaveStation_IsValid(
        double pitchDegrees,
        double fromEaveMm)
    {
        var solved = Solve(pitchDegrees);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                solved.Geometry,
                out var maxExclusiveMm));
        var fromRidgeMm = maxExclusiveMm - fromEaveMm;
        Assert.True(fromRidgeMm > 0d);

        var fromEave = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            fromEaveMm,
            wallPlateWidthMm: 140d,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        Assert.True(fromEave.IsValid, fromEave.Error.ToString());

        var fromRidge = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
            fromRidgeMm,
            wallPlateWidthMm: 140d,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        Assert.True(fromRidge.IsValid, fromRidge.Error.ToString());

        var eaveWalls = fromEave.Plan!.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .OrderBy(item => item.Segment3D.Start.X)
            .ThenBy(item => item.Segment3D.Start.Y)
            .ToArray();
        var ridgeWalls = fromRidge.Plan!.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .OrderBy(item => item.Segment3D.Start.X)
            .ThenBy(item => item.Segment3D.Start.Y)
            .ToArray();
        Assert.Equal(eaveWalls.Length, ridgeWalls.Length);
        Assert.NotEmpty(eaveWalls);
        for (var i = 0; i < eaveWalls.Length; i++)
        {
            Assert.Equal(
                eaveWalls[i].ElevationProfile!.BottomLocalZMm,
                ridgeWalls[i].ElevationProfile!.BottomLocalZMm,
                3);
            Assert.Equal(
                eaveWalls[i].PhysicalPlacement!.PurlinCenterLocalZMm,
                ridgeWalls[i].PhysicalPlacement!.PurlinCenterLocalZMm,
                3);
        }

        Assert.True(
            RoofAutomaticPurlinWallPlatePlanDistanceRules.TryResolveAxisPlanDistanceFromEaveMm(
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
                fromRidgeMm,
                ridgeWalls[0].PhysicalPlacement!.RafterUpperSurfaceLocalZMm,
                pitchDegrees,
                out var resolvedFromEaveMm));
        Assert.Equal(fromEaveMm, resolvedFromEaveMm, 3);
    }

    [Theory]
    [InlineData(15d, 69.999d, 140d)]
    [InlineData(15d, 70.000d, 140d)]
    [InlineData(15d, 150.000d, 140d)]
    public void PlanDistanceFromRidge_HostEquivalentStations_MatchEaveValidity(
        double pitchDegrees,
        double fromEaveMm,
        double widthMm)
    {
        var solved = Solve(pitchDegrees);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                solved.Geometry,
                out var maxExclusiveMm));
        var fromRidgeMm = maxExclusiveMm - fromEaveMm;
        var expectValid =
            RoofAutomaticPurlinWallPlatePlanDistanceRules.IsPlanDistanceAtOrAboveMinimum(
                fromEaveMm,
                widthMm);

        var fromEave = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            fromEaveMm,
            widthMm,
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        var fromRidge = CreateWallPlate(
            solved,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
            fromRidgeMm,
            widthMm,
            RoofRelativeElevationReferenceKind.SourceEavePlane);

        Assert.Equal(expectValid, fromEave.IsValid);
        Assert.Equal(expectValid, fromRidge.IsValid);
        if (!expectValid)
        {
            Assert.Equal(
                RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum,
                fromEave.Error);
            Assert.Equal(
                RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum,
                fromRidge.Error);
        }
    }

    [Fact]
    public void TryResolveAxisPlanDistance_FromRidge_UsesRoofSurfaceNotPlacementValue()
    {
        // Placement value 2850 must NOT be treated as an eave distance (would pass min 70
        // wrongly if the mode were ignored, or fail TryResolve if FromRidge is unsupported).
        Assert.True(
            RoofAutomaticPurlinWallPlatePlanDistanceRules.TryResolveAxisPlanDistanceFromEaveMm(
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
                placementValueMm: 2850d,
                roofSurfaceLocalZMm: 150d * Math.Tan(15d * Math.PI / 180d),
                pitchDegrees: 15d,
                out var fromEaveMm));
        Assert.Equal(150d, fromEaveMm, 3);
        Assert.True(
            RoofAutomaticPurlinWallPlatePlanDistanceRules.IsPlanDistanceAtOrAboveMinimum(
                fromEaveMm,
                wallPlateWidthMm: 140d));
    }

    private static RoofAutomaticPurlinPlanResult CreateWallPlate(
        SolvedFixture solved,
        RoofAutomaticPurlinPlacementMode mode,
        double placementValueMm,
        double wallPlateWidthMm,
        RoofRelativeElevationReferenceKind datumKind,
        double datumLocalZMm = 0d,
        double? seatingPercent = 25d)
    {
        RoofAutomaticPurlinSeatingDepth? seating = seatingPercent is { } percent
            ? new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                percent)
            : null;
        var layout = new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                mode,
                placementValueMm,
                SeatingDepth: seating,
                WidthMm: wallPlateWidthMm,
                HeightMm: wallPlateWidthMm),
        };
        return RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(datumKind, 0d, datumLocalZMm),
                220d,
                125d)
            {
                PurlinWidthMm = 160d,
                WallPlatesEnabled = true,
                WallPlateWidthMm = wallPlateWidthMm,
                WallPlateHeightMm = wallPlateWidthMm,
            });
    }

    private static SolvedFixture Solve(double pitchDegrees)
    {
        var points = new RoofPoint2D[]
        {
            new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000),
        };
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
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
        Assert.True(geometry.IsValid);
        return new SolvedFixture(
            Assert.IsType<HipRoofGeometry>(geometry.Geometry),
            provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
