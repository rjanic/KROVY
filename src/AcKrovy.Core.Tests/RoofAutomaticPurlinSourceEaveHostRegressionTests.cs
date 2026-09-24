using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Authoritative AutoCAD section fixture for SourceEave + WallPlateBottom simultaneously.
/// Expected values are derived independently from the CAD geometry, not from Core helpers.
/// </summary>
public sealed class RoofAutomaticPurlinSourceEaveHostRegressionTests
{
    // Independent CAD derivation:
    // pitch 45°, H=125, WP 140×140, plan 700, seating 25%, SourceEave upper face at eave = 0.
    // X_outer=630; Z_upper(630)=630; Top=630-(125-31.25)/cos45=497.417479…
    private const double ExpectedSourceEaveBottomMm = 357.4174785275226d;
    private const double ExpectedSourceEaveCenterMm = 427.4174785275226d;
    private const double ExpectedSourceEaveTopMm = 497.4174785275226d;
    private const double ExpectedSourceEaveRoofPlaneMm = 700d;
    private const double ExpectedWallPlateBottomRoofPlaneMm = 342.5825214724774d;

    [Fact]
    public void HostCase_SourceEave_And_WallPlateBottom_MatchAuthoritativeCadSection()
    {
        var solved = Solve(45d);
        var layout = CreateWallPlateLayout(700d, 140d, 140d, 25d);
        var planningSourceEave = CreatePlanning(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane, 0d, 0d),
            125d,
            140d,
            140d);
        var planningWallPlateBottom = CreatePlanning(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.WallPlateBottom, 0d, 0d),
            125d,
            140d,
            140d);

        var sourceEave = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry, solved.Provenance, layout, planningSourceEave);
        var wallPlateBottom = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry, solved.Provenance, layout, planningWallPlateBottom);
        Assert.True(sourceEave.IsValid, sourceEave.Error.ToString());
        Assert.True(wallPlateBottom.IsValid, wallPlateBottom.Error.ToString());

        var seWall = sourceEave.Plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        var wpWall = wallPlateBottom.Plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);

        // Identical physical assembly under both datums.
        Assert.Equal(seWall.Segment3D.Start.X, wpWall.Segment3D.Start.X, 6);
        Assert.Equal(seWall.Segment3D.Start.Y, wpWall.Segment3D.Start.Y, 6);
        Assert.Equal(seWall.Segment3D.Start.Z, wpWall.Segment3D.Start.Z, 6);
        Assert.Equal(seWall.ElevationProfile!.BottomLocalZMm, wpWall.ElevationProfile!.BottomLocalZMm, 6);
        Assert.Equal(seWall.ElevationProfile.TopLocalZMm, wpWall.ElevationProfile.TopLocalZMm, 6);

        Assert.Equal(ExpectedSourceEaveBottomMm, seWall.ElevationProfile.BottomLocalZMm, 6);
        Assert.Equal(ExpectedSourceEaveCenterMm, seWall.ElevationProfile.CenterLocalZMm, 6);
        Assert.Equal(ExpectedSourceEaveTopMm, seWall.ElevationProfile.TopLocalZMm, 6);
        Assert.Equal(ExpectedSourceEaveBottomMm, seWall.ElevationProfile.BottomRelativeElevationMm, 6);
        Assert.Equal(ExpectedSourceEaveCenterMm, seWall.ElevationProfile.CenterRelativeElevationMm, 6);
        Assert.Equal(ExpectedSourceEaveTopMm, seWall.ElevationProfile.TopRelativeElevationMm, 6);

        Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
            seWall, 45d, 125d, out var seRoofPlane));
        Assert.Equal(ExpectedSourceEaveRoofPlaneMm, seRoofPlane, 6);
        Assert.Equal("+0.357", RoofRelativeElevationDatumRules.FormatMetres(
            seWall.ElevationProfile.BottomRelativeElevationMm));
        Assert.Equal("+0.427", RoofRelativeElevationDatumRules.FormatMetres(
            seWall.ElevationProfile.CenterRelativeElevationMm));
        Assert.Equal("+0.497", RoofRelativeElevationDatumRules.FormatMetres(
            seWall.ElevationProfile.TopRelativeElevationMm));
        Assert.Equal("+0.700", RoofRelativeElevationDatumRules.FormatMetres(seRoofPlane));

        Assert.Equal(0d, wpWall.ElevationProfile.BottomRelativeElevationMm, 9);
        Assert.Equal(70d, wpWall.ElevationProfile.CenterRelativeElevationMm, 9);
        Assert.Equal(140d, wpWall.ElevationProfile.TopRelativeElevationMm, 9);
        Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
            wpWall, 45d, 125d, out var wpRoofPlane));
        Assert.Equal(ExpectedWallPlateBottomRoofPlaneMm, wpRoofPlane, 6);
        Assert.Equal("±0.000", RoofRelativeElevationDatumRules.FormatMetres(
            wpWall.ElevationProfile.BottomRelativeElevationMm));
        Assert.Equal("+0.070", RoofRelativeElevationDatumRules.FormatMetres(
            wpWall.ElevationProfile.CenterRelativeElevationMm));
        Assert.Equal("+0.140", RoofRelativeElevationDatumRules.FormatMetres(
            wpWall.ElevationProfile.TopRelativeElevationMm));
        Assert.Equal("+0.343", RoofRelativeElevationDatumRules.FormatMetres(wpRoofPlane));

        // Cross-datum offset equals physical wall-plate bottom above SourceEave.
        Assert.Equal(
            ExpectedSourceEaveBottomMm,
            seWall.ElevationProfile.BottomRelativeElevationMm -
            wpWall.ElevationProfile.BottomRelativeElevationMm,
            6);
        Assert.Equal(
            ExpectedSourceEaveBottomMm,
            seRoofPlane - wpRoofPlane,
            6);
    }

    [Fact]
    public void HostCase_SourceEave_Intermediate_MatchesClosedFormAtMemberAxis()
    {
        // Same physical roof as the wall-plate fixture; Intermediate at plan 2000 mm.
        const double planDistanceMm = 2000d;
        const double widthMm = 120d;
        const double heightMm = 160d;
        const double seatingPercent = 25d;
        const double rafterHeightMm = 125d;
        var pitchRad = 45d * Math.PI / 180d;
        var d = rafterHeightMm * seatingPercent / 100d;
        var expectedRoofPlane = planDistanceMm * Math.Tan(pitchRad);
        var expectedTop =
            (planDistanceMm - widthMm / 2d) * Math.Tan(pitchRad) -
            (rafterHeightMm - d) / Math.Cos(pitchRad);
        var expectedCenter = expectedTop - heightMm / 2d;
        var expectedBottom = expectedTop - heightMm;

        var solved = Solve(45d);
        var layout = CreateWallPlateLayout(700d, 140d, 140d, 25d) with
        {
            IntermediateItems =
            [
                new RoofAutomaticPurlinLayoutItem(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    planDistanceMm,
                    SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                        RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                        seatingPercent),
                    WidthMm: widthMm,
                    HeightMm: heightMm),
            ],
        };
        var planning = CreatePlanning(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane, 0d, 0d),
            rafterHeightMm,
            140d,
            140d) with
        {
            PurlinWidthMm = widthMm,
            PurlinHeightMm = heightMm,
        };

        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry, solved.Provenance, layout, planning);
        Assert.True(result.IsValid, result.Error.ToString());
        var intermediates = result.Plan!.Items
            .Where(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .ToArray();
        Assert.Equal(4, intermediates.Length);
        Assert.All(intermediates, intermediate =>
        {
            Assert.Equal(expectedBottom, intermediate.ElevationProfile!.BottomLocalZMm, 6);
            Assert.Equal(expectedCenter, intermediate.ElevationProfile.CenterLocalZMm, 6);
            Assert.Equal(expectedTop, intermediate.ElevationProfile.TopLocalZMm, 6);
            Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                intermediate, 45d, rafterHeightMm, out var roofPlane));
            Assert.Equal(expectedRoofPlane, roofPlane, 6);
            Assert.Equal(
                expectedRoofPlane,
                intermediate.PhysicalPlacement!.RafterUpperSurfaceLocalZMm,
                6);
        });
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(100d)]
    public void HostCase_SourceEave_SeatingSweep_MatchesClosedForm(double seatingPercent)
    {
        var solved = Solve(45d);
        var layout = CreateWallPlateLayout(700d, 140d, 140d, seatingPercent);
        var planning = CreatePlanning(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane, 0d, 0d),
            125d,
            140d,
            140d);
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry, solved.Provenance, layout, planning);
        Assert.True(result.IsValid, result.Error.ToString());
        var wall = result.Plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);

        var pitchRad = 45d * Math.PI / 180d;
        var d = 125d * seatingPercent / 100d;
        var expectedTop = (700d - 70d) * Math.Tan(pitchRad) - (125d - d) / Math.Cos(pitchRad);
        var expectedBottom = expectedTop - 140d;
        var expectedRoofPlane = 700d * Math.Tan(pitchRad);

        Assert.Equal(expectedTop, wall.ElevationProfile!.TopLocalZMm, 6);
        Assert.Equal(expectedBottom, wall.ElevationProfile.BottomLocalZMm, 6);
        Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
            wall, 45d, 125d, out var roofPlane));
        Assert.Equal(expectedRoofPlane, roofPlane, 6);
    }

    [Theory]
    [InlineData(0d, 0d, 70d, 140d, 342.5825214724774d)]
    [InlineData(200d, 200d, 270d, 340d, 542.5825214724774d)]
    public void HostCase_SourceEave_BottomEdgeHeight_PlacesPhysicalBottomAtRequestedRelative(
        double bottomAboveReferenceMm,
        double expectedBottomMm,
        double expectedCenterMm,
        double expectedTopMm,
        double expectedRoofPlaneMm)
    {
        // Independent inverse of seating fixture (H=125, WP 140×140, 25%, 45°):
        // RoofPlane = Top + (125-31.25)/cos45 + 70.
        var solved = Solve(45d);
        var layout = new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                bottomAboveReferenceMm,
                SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    25d),
                WidthMm: 140d,
                HeightMm: 140d),
        };
        var planning = CreatePlanning(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane, 0d, 0d),
            125d,
            140d,
            140d);

        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry, solved.Provenance, layout, planning);
        Assert.True(result.IsValid, result.Error.ToString());
        var wall = result.Plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);

        Assert.Equal(expectedBottomMm, wall.ElevationProfile!.BottomLocalZMm, 6);
        Assert.Equal(expectedCenterMm, wall.ElevationProfile.CenterLocalZMm, 6);
        Assert.Equal(expectedTopMm, wall.ElevationProfile.TopLocalZMm, 6);
        Assert.Equal(expectedBottomMm, wall.ElevationProfile.BottomRelativeElevationMm, 6);
        Assert.Equal(expectedCenterMm, wall.ElevationProfile.CenterRelativeElevationMm, 6);
        Assert.Equal(expectedTopMm, wall.ElevationProfile.TopRelativeElevationMm, 6);
        Assert.Equal(expectedBottomMm, wall.PhysicalPlacement!.PurlinBottomLocalZMm, 6);
        Assert.Equal(expectedTopMm, wall.PhysicalPlacement.PurlinTopLocalZMm, 6);
        Assert.Equal(expectedRoofPlaneMm, wall.PhysicalPlacement.RafterUpperSurfaceLocalZMm, 6);
        Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
            wall, 45d, 125d, out var roofPlane));
        Assert.Equal(expectedRoofPlaneMm, roofPlane, 6);
        Assert.Equal(
            RoofRelativeElevationDatumRules.FormatMetres(expectedBottomMm),
            RoofRelativeElevationDatumRules.FormatMetres(
                wall.ElevationProfile.BottomRelativeElevationMm));
        Assert.Equal(
            RoofRelativeElevationDatumRules.FormatMetres(expectedCenterMm),
            RoofRelativeElevationDatumRules.FormatMetres(
                wall.ElevationProfile.CenterRelativeElevationMm));
        Assert.Equal(
            RoofRelativeElevationDatumRules.FormatMetres(expectedTopMm),
            RoofRelativeElevationDatumRules.FormatMetres(
                wall.ElevationProfile.TopRelativeElevationMm));
        Assert.Equal(
            RoofRelativeElevationDatumRules.FormatMetres(expectedRoofPlaneMm),
            RoofRelativeElevationDatumRules.FormatMetres(roofPlane));
    }

    private static RoofAutomaticPurlinLayout CreateWallPlateLayout(
        double planDistanceMm,
        double widthMm,
        double heightMm,
        double seatingPercent) =>
        new(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                planDistanceMm,
                SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: widthMm,
                HeightMm: heightMm),
        };

    private static RoofAutomaticPurlinPlanningInput CreatePlanning(
        RoofRelativeElevationDatum datum,
        double rafterHeightMm,
        double wallPlateWidthMm,
        double wallPlateHeightMm) =>
        new(datum, wallPlateHeightMm, rafterHeightMm)
        {
            PurlinWidthMm = wallPlateWidthMm,
            WallPlatesEnabled = true,
            WallPlateWidthMm = wallPlateWidthMm,
            WallPlateHeightMm = wallPlateHeightMm,
        };

    private static Solved Solve(double pitchDegrees)
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
        var geometry = Assert.IsType<HipRoofGeometry>(
            HipRoofGeometrySolver.Solve(new RoofDefinition(
                normalized.Validation.Footprint!,
                new RoofParameters(pitchDegrees),
                RoofKind.Hip)).Geometry);
        return new Solved(geometry, provenance);
    }

    private sealed record Solved(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
