using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinPlanDistanceBoundaryTests
{
    [Theory]
    [InlineData(10000d, 6000d, 45d)]
    [InlineData(4000d, 3000d, 45d)]
    [InlineData(10000d, 6000d, 30d)]
    public void PlanDistanceFromEave_Zero_IsValidAlongSourceEave(
        double widthMm,
        double depthMm,
        double pitchDegrees)
    {
        var solved = Solve(widthMm, depthMm, pitchDegrees);
        var result = CreateWallPlateOnly(solved, planDistanceMm: 0d);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.NotNull(result.Plan);
        Assert.Equal(4, result.Plan!.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate));
        Assert.All(
            result.Plan.Items.Where(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate),
            item =>
            {
                Assert.NotNull(item.PhysicalPlacement);
                Assert.NotNull(item.ElevationProfile);
            });
    }

    [Theory]
    [InlineData(10000d, 6000d, 45d, 1200d)]
    [InlineData(4000d, 3000d, 45d, 1100d)]
    [InlineData(10000d, 6000d, 30d, 2000d)]
    public void PlanDistanceFromEave_BelowExclusiveMax_Succeeds(
        double widthMm,
        double depthMm,
        double pitchDegrees,
        double planDistanceMm)
    {
        var solved = Solve(widthMm, depthMm, pitchDegrees);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                solved.Geometry,
                out var maxExclusiveMm));
        Assert.True(planDistanceMm < maxExclusiveMm);

        var result = CreateWallPlateOnly(solved, planDistanceMm);
        Assert.True(result.IsValid, $"{result.Error} max={maxExclusiveMm}");
        Assert.NotNull(result.Plan);
    }

    [Theory]
    [InlineData(10000d, 6000d, 45d)]
    [InlineData(4000d, 3000d, 45d)]
    public void PlanDistanceFromEave_AtOrAboveExclusiveMax_FailsOutsideRoof(
        double widthMm,
        double depthMm,
        double pitchDegrees)
    {
        var solved = Solve(widthMm, depthMm, pitchDegrees);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                solved.Geometry,
                out var maxExclusiveMm));

        foreach (var distance in new[] { maxExclusiveMm, maxExclusiveMm + 1d, maxExclusiveMm + 200d })
        {
            var result = CreateWallPlateOnly(solved, distance);
            Assert.False(result.IsValid);
            Assert.Null(result.Plan);
            Assert.Equal(RoofAutomaticPurlinPlanError.ElevationOutsideRoof, result.Error);
        }
    }

    [Fact]
    public void PlanDistanceFromEave_ExclusiveMax_MatchesRiseOverTanPitch()
    {
        var solved = Solve(10000d, 6000d, 45d);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                solved.Geometry,
                out var maxExclusiveMm));
        Assert.Equal(
            solved.Geometry.RiseMm / Math.Tan(45d * Math.PI / 180d),
            maxExclusiveMm,
            9);
    }

    private static RoofAutomaticPurlinPlanResult CreateWallPlateOnly(
        SolvedFixture solved,
        double planDistanceMm)
    {
        var layout = new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                planDistanceMm,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    25d),
                WidthMm: 140d,
                HeightMm: 140d),
        };
        return RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.WallPlateBottom,
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
    }

    private static SolvedFixture Solve(double widthMm, double depthMm, double pitchDegrees)
    {
        var points = new RoofPoint2D[]
        {
            new(0, 0),
            new(widthMm, 0),
            new(widthMm, depthMm),
            new(0, depthMm),
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
        return new SolvedFixture(
            Assert.IsType<HipRoofGeometry>(geometry.Geometry),
            provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
