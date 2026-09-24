using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// Presentation-only visual seating: plan-distance / ridge rectangle tops must meet the
/// SVG visual rafter edge at seating fraction (0% = lower, 100% = upper). Technical
/// ElevationProfile values are asserted unchanged vs the planner output.
/// </summary>
public sealed class AutomaticPurlinVisualSeatingContactTests
{
    [Theory]
    [InlineData(30d, 0d, 140d, 125d)]
    [InlineData(30d, 25d, 140d, 125d)]
    [InlineData(30d, 50d, 140d, 125d)]
    [InlineData(30d, 75d, 140d, 125d)]
    [InlineData(30d, 100d, 140d, 125d)]
    [InlineData(45d, 0d, 140d, 125d)]
    [InlineData(45d, 25d, 140d, 125d)]
    [InlineData(45d, 50d, 140d, 125d)]
    [InlineData(45d, 75d, 140d, 125d)]
    [InlineData(45d, 100d, 140d, 125d)]
    [InlineData(45d, 0d, 180d, 125d)]
    [InlineData(45d, 100d, 120d, 160d)]
    public void PlanDistanceWallPlate_MemberTopMeetsVisualSeatingContact(
        double pitchDegrees,
        double seatingPercent,
        double timberHeightMm,
        double rafterHeightMm)
    {
        var solved = Solve(pitchDegrees);
        var layout = HostWallPlateLayout(seatingPercent, timberHeightMm, timberHeightMm);
        var plan = CreatePlan(solved, layout, rafterHeightMm);
        var presentation = CreatePresentation(solved, plan, layout, rafterHeightMm);

        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.WallPlate,
            seatingPercent / 100d);
        AssertTechnicalProfileUnchanged(plan, presentation, layout);
    }

    [Theory]
    [InlineData(30d, 0d)]
    [InlineData(30d, 50d)]
    [InlineData(30d, 100d)]
    [InlineData(45d, 0d)]
    [InlineData(45d, 25d)]
    [InlineData(45d, 100d)]
    public void PlanDistanceIntermediateAndRidge_MemberTopMeetsVisualSeatingContact(
        double pitchDegrees,
        double seatingPercent)
    {
        var solved = Solve(pitchDegrees);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                1800d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: 160d,
                HeightMm: 220d),
        ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                500d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = new(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                seatingPercent),
        };
        var plan = CreatePlan(solved, layout, 125d);
        var presentation = CreatePresentation(solved, plan, layout, 125d);

        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.WallPlate,
            seatingPercent / 100d);
        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.Intermediate,
            seatingPercent / 100d);
        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.Ridge,
            seatingPercent / 100d);
        AssertTechnicalProfileUnchanged(plan, presentation, layout);
    }

    [Fact]
    public void HostCase_45deg_WallPlateBottom_Seating0_TechnicalAndVisual()
    {
        var solved = Solve(45d);
        var layout = HostWallPlateLayout(0d, 140d, 140d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            0d);
        var plan = CreatePlan(solved, layout, 125d, datum);
        var wall = plan.Items.First(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.Equal(0d, wall.ElevationProfile!.BottomRelativeElevationMm, 3);
        Assert.Equal(70d, wall.ElevationProfile.CenterRelativeElevationMm, 3);
        Assert.Equal(140d, wall.ElevationProfile.TopRelativeElevationMm, 3);
        Assert.InRange(
            wall.PhysicalPlacement!.RafterUpperSurfaceRelativeElevationMm,
            380d,
            395d);

        var presentation = CreatePresentation(solved, plan, layout, 125d, datum);
        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.WallPlate,
            seatingFraction: 0d);
        AssertTechnicalProfileUnchanged(plan, presentation, layout);
    }

    [Theory]
    [InlineData(30d, 0d)]
    [InlineData(30d, 25d)]
    [InlineData(30d, 50d)]
    [InlineData(30d, 75d)]
    [InlineData(30d, 100d)]
    [InlineData(45d, 0d)]
    [InlineData(45d, 25d)]
    [InlineData(45d, 50d)]
    [InlineData(45d, 75d)]
    [InlineData(45d, 100d)]
    public void BottomEdgeWallPlate_ExplicitLocalPlane_SeatingSweep_BottomReferenceAndContact(
        double pitchDegrees,
        double seatingPercent)
    {
        var solved = Solve(pitchDegrees);
        var layout = new RoofAutomaticPurlinLayout(false, [])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                -1000d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: 140d,
                HeightMm: 140d),
        };
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var plan = CreatePlan(solved, layout, 125d, datum);
        var wall = plan.Items.First(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.Equal(-1000d, wall.ElevationProfile!.BottomRelativeElevationMm, 3);
        Assert.Equal(-930d, wall.ElevationProfile.CenterRelativeElevationMm, 3);
        Assert.Equal(-860d, wall.ElevationProfile.TopRelativeElevationMm, 3);
        Assert.Equal(125d * seatingPercent / 100d, wall.ElevationProfile.SeatingDepthMm!.Value, 6);

        var presentation = CreatePresentation(solved, plan, layout, 125d, datum);
        Assert.NotNull(presentation.ReferencePlane);
        // Signed SH = −1000 mm: bottoms stay 1000 mm below the ExplicitLocalPlane guide.
        Assert.All(
            presentation.Members.Where(m => m.Role == RoofAutomaticPurlinGeneratorRole.WallPlate),
            plate =>
            {
                var bottomZ = plate.CenterZMm - plate.HeightMm / 2d;
                Assert.Equal(presentation.ReferencePlane!.LocalZMm - 1000d, bottomZ, 6);
                Assert.Equal(140d, plate.HeightMm, 6);
            });

        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.WallPlate,
            seatingPercent / 100d);
        AssertTechnicalProfileUnchanged(plan, presentation, layout);
    }

    [Theory]
    [InlineData(30d, 0d)]
    [InlineData(30d, 50d)]
    [InlineData(30d, 100d)]
    [InlineData(45d, 0d)]
    [InlineData(45d, 25d)]
    [InlineData(45d, 50d)]
    [InlineData(45d, 75d)]
    [InlineData(45d, 100d)]
    public void BottomEdgeIntermediate_ExplicitLocalPlane_SeatingSweep_BottomReferenceAndContact(
        double pitchDegrees,
        double seatingPercent)
    {
        var solved = Solve(pitchDegrees);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                -1000d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: 140d,
                HeightMm: 140d),
        ])
        {
            WallPlateEnabled = false,
        };
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var plan = CreatePlan(solved, layout, 125d, datum);
        var presentation = CreatePresentation(solved, plan, layout, 125d, datum);

        Assert.All(
            plan.Items.Where(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate),
            item =>
            {
                Assert.Equal(-1000d, item.ElevationProfile!.BottomRelativeElevationMm, 3);
                Assert.Equal(-930d, item.ElevationProfile.CenterRelativeElevationMm, 3);
                Assert.Equal(-860d, item.ElevationProfile.TopRelativeElevationMm, 3);
            });
        Assert.NotNull(presentation.ReferencePlane);
        Assert.All(
            presentation.Members.Where(m => m.Role == RoofAutomaticPurlinGeneratorRole.Intermediate),
            plate =>
            {
                var bottomZ = plate.CenterZMm - plate.HeightMm / 2d;
                Assert.Equal(presentation.ReferencePlane!.LocalZMm - 1000d, bottomZ, 6);
            });
        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.Intermediate,
            seatingPercent / 100d);
        AssertTechnicalProfileUnchanged(plan, presentation, layout);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(200d, 25d)]
    [InlineData(-100d, 50d)]
    public void BottomEdgeWallPlate_SignedOffsets_KeepBottomToReferenceAndContact(
        double placementOffsetMm,
        double seatingPercent)
    {
        var solved = Solve(45d);
        var layout = new RoofAutomaticPurlinLayout(false, [])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                placementOffsetMm,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: 140d,
                HeightMm: 140d),
        };
        // Elevated ExplicitLocalPlane keeps signed bottoms inside the roof envelope.
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            500d);
        var plan = CreatePlan(solved, layout, 125d, datum);
        var presentation = CreatePresentation(solved, plan, layout, 125d, datum);
        Assert.NotNull(presentation.ReferencePlane);
        Assert.All(
            presentation.Members.Where(m => m.Role == RoofAutomaticPurlinGeneratorRole.WallPlate),
            plate =>
            {
                var bottomZ = plate.CenterZMm - plate.HeightMm / 2d;
                Assert.Equal(presentation.ReferencePlane!.LocalZMm + placementOffsetMm, bottomZ, 3);
            });
        AssertVisualContactForRole(
            presentation,
            RoofAutomaticPurlinGeneratorRole.WallPlate,
            seatingPercent / 100d);
        AssertTechnicalProfileUnchanged(plan, presentation, layout);
    }

    [Fact]
    public void BottomEdgeWallPlate_KeepsBottomOnReference_NotVisualSeatingAlign()
    {
        var solved = Solve(45d);
        var layout = new RoofAutomaticPurlinLayout(false, [])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                0d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    0d),
                WidthMm: 140d,
                HeightMm: 140d),
        };
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            0d);
        var plan = CreatePlan(solved, layout, 125d, datum);
        var presentation = CreatePresentation(solved, plan, layout, 125d, datum);

        Assert.NotNull(presentation.ReferencePlane);
        Assert.All(
            presentation.Members.Where(m => m.Role == RoofAutomaticPurlinGeneratorRole.WallPlate),
            plate =>
            {
                var bottomZ = plate.CenterZMm - plate.HeightMm / 2d;
                Assert.Equal(presentation.ReferencePlane!.LocalZMm, bottomZ, 6);
                Assert.Equal(140d, plate.HeightMm, 6);
            });
    }

    private static void AssertVisualContactForRole(
        AutomaticPurlinSectionPresentation presentation,
        RoofAutomaticPurlinGeneratorRole role,
        double seatingFraction)
    {
        var members = presentation.Members.Where(member => member.Role == role).ToArray();
        Assert.NotEmpty(members);
        foreach (var member in members)
        {
            if (member.Side == AutomaticPurlinSectionSide.Center ||
                role == RoofAutomaticPurlinGeneratorRole.Ridge)
            {
                var leftX = member.CenterXMm - member.WidthMm / 2d;
                var rightX = member.CenterXMm + member.WidthMm / 2d;
                Assert.True(
                    AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                        presentation.Rafters,
                        AutomaticPurlinSectionSide.Left,
                        leftX,
                        seatingFraction,
                        out var leftContact,
                        out _));
                Assert.True(
                    AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                        presentation.Rafters,
                        AutomaticPurlinSectionSide.Right,
                        rightX,
                        seatingFraction,
                        out var rightContact,
                        out _));
                Assert.Equal((leftContact + rightContact) / 2d, member.MemberTopZMm, 3);
                continue;
            }

            var contactX = member.CenterXMm +
                AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                    member.Side,
                    member.WidthMm);
            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                    presentation.Rafters,
                    member.Side,
                    contactX,
                    seatingFraction,
                    out var visualContactZ,
                    out _));
            Assert.Equal(visualContactZ, member.MemberTopZMm, 3);
        }
    }

    private static void AssertTechnicalProfileUnchanged(
        RoofAutomaticPurlinPlan plan,
        AutomaticPurlinSectionPresentation presentation,
        RoofAutomaticPurlinLayout layout)
    {
        foreach (var member in presentation.Members)
        {
            var item = plan.Items.First(candidate =>
                candidate.GeneratorRole == member.Role &&
                (member.LayoutItemId is null ||
                 string.Equals(candidate.LayoutItemId, member.LayoutItemId, StringComparison.Ordinal)));
            var profile = Assert.IsType<RoofPurlinElevationProfile>(item.ElevationProfile);
            Assert.Equal(profile.TopLocalZMm - profile.BottomLocalZMm, member.HeightMm, 3);
            var expectedSeating = item.PhysicalPlacement?.SeatingDepthMm ?? profile.SeatingDepthMm;
            Assert.NotNull(member.SeatingDepthMm);
            Assert.NotNull(expectedSeating);
            Assert.Equal(expectedSeating!.Value, member.SeatingDepthMm!.Value, 6);
            // Labels stay on the planner profile — never rewritten by visual seating.
            Assert.Contains("BE ", member.BottomText, StringComparison.Ordinal);
            Assert.Contains("TE ", member.TopText, StringComparison.Ordinal);
        }

        // PlacementMode metadata is the gate for visual vs physical rectangle authority.
        Assert.All(
            presentation.Members.Where(m => m.Role == RoofAutomaticPurlinGeneratorRole.WallPlate),
            m => Assert.Equal(
                layout.WallPlatePlacement!.PlacementMode,
                m.PlacementMode));
    }

    private static RoofAutomaticPurlinLayout HostWallPlateLayout(
        double seatingPercent,
        double widthMm,
        double heightMm) =>
        new(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                500d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: widthMm,
                HeightMm: heightMm),
        };

    private static RoofAutomaticPurlinPlan CreatePlan(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout,
        double rafterHeightMm,
        RoofRelativeElevationDatum? datum = null)
    {
        datum ??= new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            0d);
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                datum,
                220d,
                rafterHeightMm)
            {
                PurlinWidthMm = 160d,
                WallPlatesEnabled = layout.WallPlateEnabled,
                WallPlateWidthMm = layout.WallPlatePlacement?.WidthMm ?? 140d,
                WallPlateHeightMm = layout.WallPlatePlacement?.HeightMm ?? 140d,
            });
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation(
        SolvedFixture solved,
        RoofAutomaticPurlinPlan plan,
        RoofAutomaticPurlinLayout layout,
        double rafterHeightMm,
        RoofRelativeElevationDatum? datum = null)
    {
        var culture = CultureInfo.GetCultureInfo("en");
        return AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            rafterHeightMm,
            80d,
            datum,
            layout);
    }

    private static SolvedFixture Solve(double pitchDegrees)
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
        var geometry = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(pitchDegrees),
            RoofKind.Hip));
        Assert.True(geometry.IsValid);
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
