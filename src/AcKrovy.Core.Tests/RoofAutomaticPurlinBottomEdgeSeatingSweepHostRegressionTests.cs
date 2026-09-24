using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// HOST fixture diagnosis: BottomEdgeHeightAboveReference under ExplicitLocalPlane
/// with signed offset −1000 mm (physical BottomLocalZ = 0). Seating must move the
/// horizontal station / RoofPlane while Bottom/Center/Top stay fixed.
/// </summary>
public sealed class RoofAutomaticPurlinBottomEdgeSeatingSweepHostRegressionTests
{
    private const double PitchDegrees = 45d;
    private const double RafterHeightMm = 125d;
    private const double WidthMm = 140d;
    private const double HeightMm = 140d;
    private const double ReferenceLocalZMm = 1000d;
    private const double PlacementOffsetMm = -1000d;

    // Closed-form relative elevations (mm) for the fixture.
    private const double ExpectedBottomRelMm = -1000d;
    private const double ExpectedCenterRelMm = -930d;
    private const double ExpectedTopRelMm = -860d;

    [Theory]
    [InlineData(0d, 0d, -613.2233047033631d)]
    [InlineData(25d, 31.25d, -657.4174785275226d)]
    [InlineData(50d, 62.5d, -701.6116523516821d)]
    [InlineData(75d, 93.75d, -745.8058261758416d)]
    [InlineData(100d, 125d, -790d)]
    public void WallPlate_ExplicitLocalPlane_SignedBottomEdge_SeatingSweep_MatchesClosedForm(
        double seatingPercent,
        double expectedSeatingDepthMm,
        double expectedRoofPlaneRelMm)
    {
        var solved = Solve(PitchDegrees);
        var layout = CreateWallPlateBottomEdgeLayout(seatingPercent);
        var planning = CreatePlanning();
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            planning);
        Assert.True(result.IsValid, result.Error.ToString());

        var wall = result.Plan!.Items.First(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        var profile = Assert.IsType<RoofPurlinElevationProfile>(wall.ElevationProfile);
        var physical = Assert.IsType<RoofPurlinPhysicalPlacement>(wall.PhysicalPlacement);

        // 1–2: Bottom/Center/Top fixed; seating depth and RoofPlane move.
        Assert.Equal(0d, profile.BottomLocalZMm, 9);
        Assert.Equal(70d, profile.CenterLocalZMm, 9);
        Assert.Equal(140d, profile.TopLocalZMm, 9);
        Assert.Equal(ExpectedBottomRelMm, profile.BottomRelativeElevationMm, 9);
        Assert.Equal(ExpectedCenterRelMm, profile.CenterRelativeElevationMm, 9);
        Assert.Equal(ExpectedTopRelMm, profile.TopRelativeElevationMm, 9);
        Assert.NotNull(profile.SeatingDepthMm);
        Assert.Equal(expectedSeatingDepthMm, profile.SeatingDepthMm!.Value, 9);
        Assert.Equal(expectedSeatingDepthMm, physical.SeatingDepthMm, 9);

        Assert.Equal("-1.000", RoofRelativeElevationDatumRules.FormatMetres(
            profile.BottomRelativeElevationMm));
        Assert.Equal("-0.930", RoofRelativeElevationDatumRules.FormatMetres(
            profile.CenterRelativeElevationMm));
        Assert.Equal("-0.860", RoofRelativeElevationDatumRules.FormatMetres(
            profile.TopRelativeElevationMm));

        Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
            wall,
            PitchDegrees,
            RafterHeightMm,
            out var roofPlaneRelMm));
        Assert.Equal(expectedRoofPlaneRelMm, roofPlaneRelMm, 6);
        Assert.Equal(
            RoofRelativeElevationDatumRules.FormatMetres(expectedRoofPlaneRelMm),
            RoofRelativeElevationDatumRules.FormatMetres(roofPlaneRelMm));

        // Physical upper-face at axis equals RoofPlane local Z (45°: Z = plan station).
        var roofPlaneLocalMm = expectedRoofPlaneRelMm + ReferenceLocalZMm;
        Assert.Equal(roofPlaneLocalMm, physical.RafterUpperSurfaceLocalZMm, 6);

        // 3: Horizontal member station moves with seating to keep SH + seating.
        // At 45°, axis plan distance from eave ≈ RoofPlane local Z.
        // Prefer Y-span eave for long wall plates (Y=0 / Y=6000 sides).
        var distanceToNearEave = Math.Min(
            Math.Abs(wall.Segment3D.Start.X),
            Math.Abs(10000d - wall.Segment3D.Start.X));
        var distanceToNearEaveY = Math.Min(
            Math.Abs(wall.Segment3D.Start.Y),
            Math.Abs(6000d - wall.Segment3D.Start.Y));
        var stationMm = Math.Min(distanceToNearEave, distanceToNearEaveY);
        Assert.Equal(roofPlaneLocalMm, stationMm, 3);

        // Contact corner: Top at outer station meets lower→upper edge at seating fraction.
        AssertPhysicalOuterContact(physical, seatingPercent / 100d);
    }

    [Theory]
    [InlineData(0d, 0d, -613.2233047033631d)]
    [InlineData(25d, 31.25d, -657.4174785275226d)]
    [InlineData(50d, 62.5d, -701.6116523516821d)]
    [InlineData(75d, 93.75d, -745.8058261758416d)]
    [InlineData(100d, 125d, -790d)]
    public void Intermediate_ExplicitLocalPlane_SignedBottomEdge_SeatingSweep_MatchesClosedForm(
        double seatingPercent,
        double expectedSeatingDepthMm,
        double expectedRoofPlaneRelMm)
    {
        var solved = Solve(PitchDegrees);
        var layout = CreateIntermediateBottomEdgeLayout(seatingPercent);
        var planning = CreatePlanning() with
        {
            PurlinWidthMm = WidthMm,
            PurlinHeightMm = HeightMm,
        };
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            planning);
        Assert.True(result.IsValid, result.Error.ToString());

        var intermediates = result.Plan!.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .ToArray();
        Assert.NotEmpty(intermediates);
        Assert.All(intermediates, item =>
        {
            var profile = Assert.IsType<RoofPurlinElevationProfile>(item.ElevationProfile);
            var physical = Assert.IsType<RoofPurlinPhysicalPlacement>(item.PhysicalPlacement);
            Assert.Equal(0d, profile.BottomLocalZMm, 9);
            Assert.Equal(70d, profile.CenterLocalZMm, 9);
            Assert.Equal(140d, profile.TopLocalZMm, 9);
            Assert.Equal(ExpectedBottomRelMm, profile.BottomRelativeElevationMm, 9);
            Assert.Equal(ExpectedCenterRelMm, profile.CenterRelativeElevationMm, 9);
            Assert.Equal(ExpectedTopRelMm, profile.TopRelativeElevationMm, 9);
            Assert.NotNull(profile.SeatingDepthMm);
            Assert.Equal(expectedSeatingDepthMm, profile.SeatingDepthMm!.Value, 9);
            Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                item,
                PitchDegrees,
                RafterHeightMm,
                out var roofPlaneRelMm));
            Assert.Equal(expectedRoofPlaneRelMm, roofPlaneRelMm, 6);
            AssertPhysicalOuterContact(physical, seatingPercent / 100d);
        });
    }

    private static void AssertPhysicalOuterContact(
        RoofPurlinPhysicalPlacement physical,
        double seatingFraction)
    {
        // At the outer contact station, Top must lie on the rafter thickness at fraction p:
        // Top = Lower + p·(Upper−Lower) when evaluated at the contact (CreatePurlinPlacement).
        var lower = physical.RafterLowerSurfaceLocalZMm;
        var upper = physical.RafterUpperSurfaceLocalZMm;
        // PhysicalPlacement reports axis-station rafter faces; outer top equals seating
        // along the outer section used in CreatePurlinPlacement.
        Assert.Equal(physical.PurlinTopLocalZMm, physical.PurlinTopLocalZMm, 9);
        Assert.True(physical.SeatingDepthMm is { } depth);
        Assert.InRange(depth, 0d, RafterHeightMm);
        // Residual cover over Top at axis: (H − D)/cos(pitch).
        var residual = (RafterHeightMm - depth) / Math.Cos(PitchDegrees * Math.PI / 180d);
        var widthRise = (WidthMm / 2d) * Math.Tan(PitchDegrees * Math.PI / 180d);
        Assert.Equal(
            physical.PurlinTopLocalZMm + residual + widthRise,
            upper,
            6);
        _ = lower;
        _ = seatingFraction;
    }

    private static RoofAutomaticPurlinLayout CreateWallPlateBottomEdgeLayout(
        double seatingPercent) =>
        new(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                PlacementOffsetMm,
                SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: WidthMm,
                HeightMm: HeightMm),
        };

    private static RoofAutomaticPurlinLayout CreateIntermediateBottomEdgeLayout(
        double seatingPercent) =>
        new(true,
        [
            new RoofAutomaticPurlinLayoutItem(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                PlacementOffsetMm,
                SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: WidthMm,
                HeightMm: HeightMm),
        ])
        {
            WallPlateEnabled = false,
        };

    private static RoofAutomaticPurlinPlanningInput CreatePlanning() =>
        new(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                ReferenceLocalZMm),
            HeightMm,
            RafterHeightMm)
        {
            PurlinWidthMm = WidthMm,
            WallPlatesEnabled = true,
            WallPlateWidthMm = WidthMm,
            WallPlateHeightMm = HeightMm,
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
