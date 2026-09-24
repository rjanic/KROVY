using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinRidgeHostRegressionTests
{
    [Fact]
    public void HostCase_45deg_WallPlateBottom_RidgeElevationsAndRoofPlane()
    {
        var points = new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) };
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
        var geometry = Assert.IsType<HipRoofGeometry>(
            HipRoofGeometrySolver.Solve(new RoofDefinition(
                normalized.Validation.Footprint!,
                new RoofParameters(45d),
                RoofKind.Hip)).Geometry);

        var layout = new RoofAutomaticPurlinLayout(true, Array.Empty<RoofAutomaticPurlinLayoutItem>())
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
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = new(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                25d),
        };
        var planning = new RoofAutomaticPurlinPlanningInput(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.WallPlateBottom, 0d, 0d),
            220d,
            125d)
        {
            PurlinWidthMm = 160d,
            WallPlatesEnabled = true,
            WallPlateWidthMm = 140d,
            WallPlateHeightMm = 140d,
        };

        var result = RoofAutomaticPurlinPlanner.Create(geometry, provenance, layout, planning);
        Assert.True(result.IsValid, result.Error.ToString());
        var plan = Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);

        var wall = plan.Items.First(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.Equal(0d, wall.ElevationProfile!.BottomRelativeElevationMm, 9);
        Assert.Equal(140d, wall.ElevationProfile.TopRelativeElevationMm, 9);
        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                wall, 45d, 125d, out var wallPlane));
        Assert.Equal(342.5825214724774d, wallPlane, 9);
        Assert.Equal("+0.343", RoofRelativeElevationDatumRules.FormatMetres(wallPlane));

        var ridge = Assert.Single(
            plan.Items,
            i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        var profile = ridge.ElevationProfile!;
        Assert.Equal(2210d, profile.BottomRelativeElevationMm, 6);
        Assert.Equal(2320d, profile.CenterRelativeElevationMm, 6);
        Assert.Equal(2430d, profile.TopRelativeElevationMm, 6);
        Assert.Equal(110d, profile.CenterRelativeElevationMm - profile.BottomRelativeElevationMm, 9);
        Assert.Equal(110d, profile.TopRelativeElevationMm - profile.CenterRelativeElevationMm, 9);

        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                ridge, 45d, 125d, out var ridgePlane));
        Assert.Equal(2642.5825214724774d, ridgePlane, 6);
        Assert.Equal("+2.210", RoofRelativeElevationDatumRules.FormatMetres(profile.BottomRelativeElevationMm));
        Assert.Equal("+2.320", RoofRelativeElevationDatumRules.FormatMetres(profile.CenterRelativeElevationMm));
        Assert.Equal("+2.430", RoofRelativeElevationDatumRules.FormatMetres(profile.TopRelativeElevationMm));
        Assert.Equal("+2.643", RoofRelativeElevationDatumRules.FormatMetres(ridgePlane));
        Assert.NotEqual("+2.550", RoofRelativeElevationDatumRules.FormatMetres(ridgePlane));

        // Same assembly under SourceEave: absolute elevations = WPB relatives + WP bottom.
        var sourceEavePlanning = planning with
        {
            RelativeElevationDatum = new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane, 0d, 0d),
        };
        var sourceEaveResult = RoofAutomaticPurlinPlanner.Create(
            geometry, provenance, layout, sourceEavePlanning);
        Assert.True(sourceEaveResult.IsValid, sourceEaveResult.Error.ToString());
        var seWall = sourceEaveResult.Plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        var seRidge = Assert.Single(
            sourceEaveResult.Plan.Items,
            i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Equal(357.4174785275226d, seWall.ElevationProfile!.BottomLocalZMm, 6);
        Assert.Equal(700d, RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
            seWall, 45d, 125d, out var seWallPlane) ? seWallPlane : double.NaN, 6);
        Assert.Equal(2567.4174785275226d, seRidge.ElevationProfile!.BottomLocalZMm, 6);
        Assert.Equal(2677.4174785275226d, seRidge.ElevationProfile.CenterLocalZMm, 6);
        Assert.Equal(2787.4174785275226d, seRidge.ElevationProfile.TopLocalZMm, 6);
        Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
            seRidge, 45d, 125d, out var seRidgePlane));
        Assert.Equal(3000d, seRidgePlane, 6);
        Assert.Equal(
            seRidge.ElevationProfile.BottomLocalZMm - seWall.ElevationProfile.BottomLocalZMm,
            profile.BottomRelativeElevationMm,
            6);
    }
}

