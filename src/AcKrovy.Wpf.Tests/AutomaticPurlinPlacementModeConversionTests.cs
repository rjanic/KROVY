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
/// PlanDistance ↔ BottomEdge conversion must use BottomLocalZ − ReferenceLocalZ,
/// never RoofPlane / seating-contact Z, and must not keep PlanDistance validation
/// on a converted BottomEdge field.
/// </summary>
public sealed class AutomaticPurlinPlacementModeConversionTests
{
    private const double ToleranceMm = 6d;

    [Fact]
    public void WallPlateBottom_PlanDistanceToBottomEdge_YieldsZero_PreservesPhysical()
    {
        var vm = CreateHostFixture(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            referenceLocalZMm: 0d);
        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforePhys = CaptureWallPlate(before!);
        Assert.Equal(0d, beforePhys.BottomRelativeMm, 3);
        Assert.Equal(70d, beforePhys.CenterRelativeMm, 3);
        Assert.Equal(140d, beforePhys.TopRelativeMm, 3);
        Assert.Equal(342.583, beforePhys.RoofPlaneRelativeMm, 3);

        vm.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;

        Assert.Equal("0", Normalize(vm.WallPlateRow.PlacementValueText));
        Assert.False(vm.IsSchematicStale);
        Assert.False(vm.WallPlateRow.PlacementValueHasError);
        Assert.DoesNotContain(
            "distance from eave",
            vm.WallPlateRow.PlacementValueErrorText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "distance from eave",
            vm.ValidationMessage,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(vm.TryGetPreviewPlan(out var after));
        Assert.True(vm.TryCreateDraft(out var draft, out _));
        var afterPhys = CaptureWallPlate(after!);
        AssertPhysicalUnchanged(beforePhys, afterPhys);
        Assert.Equal(0d, afterPhys.BottomRelativeMm, 3);
        Assert.Equal(70d, afterPhys.CenterRelativeMm, 3);
        Assert.Equal(140d, afterPhys.TopRelativeMm, 3);
        Assert.Equal(342.583, afterPhys.RoofPlaneRelativeMm, 3);
        Assert.Equal(0d, draft!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(beforePhys.BottomLocalZMm, draft.WallPlateLowerEdgeHeightMm, 3);
    }

    [Fact]
    public void WallPlateBottom_ReopenAfterPitchAdapt_ShowsRelativeZero_PreservesStations()
    {
        // Simulates LiveRegen Write after 45→55 under WallPlateBottom BottomEdge:
        // product Place=0, absolute bottom stashed in WallPlateLowerEdgeHeightMm.
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var layout = new RoofAutomaticPurlinLayout(
            false,
            [
                new(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    1476.681d,
                    SeatingDepth: seating,
                    WidthMm: 160d,
                    HeightMm: 220d),
            ])
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 596.285d,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                0d,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
        };
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            0d);
        var solved = Solve(55d);
        var vm = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout,
            true,
            datum,
            true,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en-US"),
            AutomaticPurlinDialogMode.ProductionEdit,
            0);
        Assert.True(vm.TryApplySelectedRafterDimensions(100d, 125d));

        Assert.Equal("0", Normalize(vm.WallPlateRow.PlacementValueText));
        Assert.True(vm.TryGetPreviewPlan(out var plan));
        Assert.False(vm.IsSchematicStale);

        var wp = plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.Equal(0d, wp.ElevationProfile!.BottomRelativeElevationMm, 3);
        Assert.Equal(596.285d, wp.ElevationProfile.BottomLocalZMm, 3);

        Assert.True(
            RoofAutomaticPurlinPitchAdaptationRules.TryResolvePlanDistanceFromEaveMm(
                wp.PhysicalPlacement!.RafterUpperSurfaceLocalZMm,
                55d,
                out var wpStation));
        Assert.Equal(700d, wpStation, 3);

        var intermediate = plan.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate);
        Assert.Equal(1476.681d, intermediate.ElevationProfile!.BottomRelativeElevationMm, 3);
        Assert.True(
            RoofAutomaticPurlinPitchAdaptationRules.TryResolvePlanDistanceFromEaveMm(
                intermediate.PhysicalPlacement!.RafterUpperSurfaceLocalZMm,
                55d,
                out var intStation));
        Assert.Equal(1800d, intStation, 3);

        Assert.True(vm.TryCreateDraft(out var draft, out _));
        Assert.Equal(0d, draft!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(596.285d, draft.WallPlateLowerEdgeHeightMm, 3);
        Assert.Equal(1476.681d, draft.IntermediateItems[0].PlacementValueMm, 3);
    }

    [Theory]
    [InlineData(RoofRelativeElevationReferenceKind.SourceEavePlane, 0d)]
    [InlineData(RoofRelativeElevationReferenceKind.ExplicitLocalPlane, 0d)]
    [InlineData(RoofRelativeElevationReferenceKind.ExplicitLocalPlane, 1000d)]
    [InlineData(RoofRelativeElevationReferenceKind.WallPlateBottom, 0d)]
    public void PlanDistance_BottomEdge_RoundTrip_PreservesPhysical(
        RoofRelativeElevationReferenceKind kind,
        double explicitLocalZMm)
    {
        var vm = CreateHostFixture(kind, explicitLocalZMm);
        if (kind == RoofRelativeElevationReferenceKind.WallPlateBottom)
        {
            vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        }

        Assert.True(vm.TryGetPreviewPlan(out var baseline));
        var before = CaptureAll(baseline!);

        vm.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        if (vm.Rows.Count > 0)
        {
            vm.Rows[0].PlacementMode =
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        }

        Assert.True(vm.TryGetPreviewPlan(out var mid));
        Assert.False(vm.IsSchematicStale);
        AssertPhysicalUnchanged(before, CaptureAll(mid!));

        if (kind == RoofRelativeElevationReferenceKind.WallPlateBottom)
        {
            Assert.Equal("0", Normalize(vm.WallPlateRow.PlacementValueText));
        }

        vm.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        if (vm.Rows.Count > 0)
        {
            vm.Rows[0].PlacementMode =
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        }

        Assert.True(vm.TryGetPreviewPlan(out var after));
        Assert.False(vm.IsSchematicStale);
        AssertPhysicalUnchanged(before, CaptureAll(after!));
        Assert.Equal("700", Normalize(vm.WallPlateRow.PlacementValueText));
    }

    [Fact]
    public void WallPlateBottom_RepeatedRoundTrip_NoDrift()
    {
        var vm = CreateHostFixture(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d);
        Assert.True(vm.TryGetPreviewPlan(out var baseline));
        var phys = CaptureAll(baseline!);

        for (var i = 0; i < 4; i++)
        {
            vm.WallPlateRow.PlacementMode =
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
            Assert.Equal("0", Normalize(vm.WallPlateRow.PlacementValueText));
            Assert.True(vm.TryGetPreviewPlan(out var a));
            AssertPhysicalUnchanged(phys, CaptureAll(a!));

            vm.WallPlateRow.PlacementMode =
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
            Assert.Equal("700", Normalize(vm.WallPlateRow.PlacementValueText));
            Assert.True(vm.TryGetPreviewPlan(out var b));
            AssertPhysicalUnchanged(phys, CaptureAll(b!));
        }

        Assert.False(vm.IsSchematicStale);
    }

    [Fact]
    public void Intermediate_WallPlateBottom_PlanDistanceToBottomEdge_UsesWpBottomReference()
    {
        var vm = CreateHostFixture(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            includeIntermediate: true);
        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforePhys = CaptureAll(before!);
        var row = Assert.Single(vm.Rows);

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;

        Assert.True(vm.TryGetPreviewPlan(out var after));
        Assert.False(vm.IsSchematicStale);
        AssertPhysicalUnchanged(beforePhys, CaptureAll(after!));

        var wpBottom = before!.Items
            .First(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ElevationProfile!.BottomLocalZMm;
        var intBottom = before.Items
            .First(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .ElevationProfile!.BottomLocalZMm;
        Assert.Equal(
            intBottom - wpBottom,
            double.Parse(Normalize(row.PlacementValueText), CultureInfo.InvariantCulture),
            3);
    }

    private static AutomaticPurlinDialogViewModel CreateHostFixture(
        RoofRelativeElevationReferenceKind kind,
        double referenceLocalZMm,
        bool includeIntermediate = false)
    {
        var solved = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var intermediates = includeIntermediate
            ? new[]
            {
                new RoofAutomaticPurlinLayoutItem(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    1800d,
                    SeatingDepth: seating,
                    WidthMm: 160d,
                    HeightMm: 220d),
            }
            : Array.Empty<RoofAutomaticPurlinLayoutItem>();

        var layout = new RoofAutomaticPurlinLayout(false, intermediates)
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                700d,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
        };

        // Seed as SourceEave first so PlanDistance preview exists, then switch kind
        // without changing placement values (PlanDistance is datum-independent).
        var seedDatum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        var vm = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout,
            true,
            seedDatum,
            true,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en-US"),
            AutomaticPurlinDialogMode.ProductionEdit,
            0)
        {
            WallPlateEnabled = true,
            RidgeEnabled = false,
        };

        // Force rafter 100×125 to match HOST fixture technical RoofPlane.
        Assert.True(vm.TryApplySelectedRafterDimensions(100d, 125d));

        if (kind == RoofRelativeElevationReferenceKind.ExplicitLocalPlane)
        {
            vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
            vm.ReferenceLocalZText = referenceLocalZMm.ToString("0.###", CultureInfo.GetCultureInfo("en-US"));
        }
        else if (kind == RoofRelativeElevationReferenceKind.WallPlateBottom)
        {
            vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        }

        vm.RelativeReferenceText = "0";
        Assert.True(vm.TryGetPreviewPlan(out _));
        return vm;
    }

    private static Phys CaptureWallPlate(RoofAutomaticPurlinPlan plan)
    {
        var item = plan.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                item,
                45d,
                125d,
                out var roofPlaneRel));
        return new Phys(
            item.GeneratorRole,
            item.ElevationProfile!.BottomLocalZMm,
            item.ElevationProfile.CenterLocalZMm,
            item.ElevationProfile.TopLocalZMm,
            item.ElevationProfile.BottomRelativeElevationMm,
            item.ElevationProfile.CenterRelativeElevationMm,
            item.ElevationProfile.TopRelativeElevationMm,
            roofPlaneRel,
            item.Segment3D.Start.X,
            item.Segment3D.Start.Y,
            item.Segment3D.Start.Z);
    }

    private static List<Phys> CaptureAll(RoofAutomaticPurlinPlan plan) =>
        plan.Items
            .Where(i => i.ElevationProfile is not null)
            .OrderBy(i => i.GeneratorRole)
            .ThenBy(i => i.LayoutItemId, StringComparer.Ordinal)
            .ThenBy(i => i.Segment3D.Start.X)
            .Select(i =>
            {
                RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                    i,
                    45d,
                    125d,
                    out var roofPlaneRel);
                return new Phys(
                    i.GeneratorRole,
                    i.ElevationProfile!.BottomLocalZMm,
                    i.ElevationProfile.CenterLocalZMm,
                    i.ElevationProfile.TopLocalZMm,
                    i.ElevationProfile.BottomRelativeElevationMm,
                    i.ElevationProfile.CenterRelativeElevationMm,
                    i.ElevationProfile.TopRelativeElevationMm,
                    roofPlaneRel,
                    i.Segment3D.Start.X,
                    i.Segment3D.Start.Y,
                    i.Segment3D.Start.Z);
            })
            .ToList();

    private static void AssertPhysicalUnchanged(Phys before, Phys after)
    {
        Assert.Equal(before.BottomLocalZMm, after.BottomLocalZMm, ToleranceMm);
        Assert.Equal(before.CenterLocalZMm, after.CenterLocalZMm, ToleranceMm);
        Assert.Equal(before.TopLocalZMm, after.TopLocalZMm, ToleranceMm);
        Assert.Equal(before.StartX, after.StartX, ToleranceMm);
        Assert.Equal(before.StartY, after.StartY, ToleranceMm);
        Assert.Equal(before.StartZ, after.StartZ, ToleranceMm);
        Assert.Equal(before.BottomRelativeMm, after.BottomRelativeMm, 3);
        Assert.Equal(before.CenterRelativeMm, after.CenterRelativeMm, 3);
        Assert.Equal(before.TopRelativeMm, after.TopRelativeMm, 3);
        Assert.Equal(before.RoofPlaneRelativeMm, after.RoofPlaneRelativeMm, 3);
    }

    private static void AssertPhysicalUnchanged(IReadOnlyList<Phys> before, IReadOnlyList<Phys> after)
    {
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            AssertPhysicalUnchanged(before[i], after[i]);
        }
    }

    private static string Normalize(string text) =>
        text.Replace(',', '.').Trim();

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
        var geometry = Assert.IsType<HipRoofGeometry>(
            HipRoofGeometrySolver.Solve(new RoofDefinition(
                normalized.Validation.Footprint!,
                new RoofParameters(pitchDegrees),
                RoofKind.Hip)).Geometry);
        return new SolvedFixture(geometry, provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);

    private sealed record Phys(
        RoofAutomaticPurlinGeneratorRole Role,
        double BottomLocalZMm,
        double CenterLocalZMm,
        double TopLocalZMm,
        double BottomRelativeMm,
        double CenterRelativeMm,
        double TopRelativeMm,
        double RoofPlaneRelativeMm,
        double StartX,
        double StartY,
        double StartZ);
}
