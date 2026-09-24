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
/// Independent ViewModel regressions for datum rebase and plan-distance conversion.
/// Asserts physical XYZ invariance via plan Segment3D / ElevationProfile — not shared
/// presentation-helper equality alone.
/// </summary>
public sealed class AutomaticPurlinDatumSwitchPlacementConversionTests
{
    private const double ToleranceMm = 6d;

    [Fact]
    public void ExplicitLocalZ_1000_To_0_RebasesBottomEdge_PreservesPhysicalAndMovesGuide()
    {
        var vm = CreateHostLikeVm();
        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforeGuide = vm.SectionPresentation.ReferencePlane!.LocalZMm;
        var beforePhys = CapturePhysical(before!);

        vm.ReferenceLocalZText = "0";

        Assert.True(vm.TryGetPreviewPlan(out var after));
        Assert.True(vm.TryCreateDraft(out _, out var datum));
        Assert.Equal(0d, datum!.ReferenceLocalZMm, 9);
        Assert.False(vm.IsSchematicStale);
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(after!));

        Assert.Equal("0", Normalize(vm.WallPlateRow.PlacementValueText));
        Assert.Equal("500", Normalize(vm.Rows[0].PlacementValueText));
        Assert.Equal("1500", Normalize(vm.Rows[1].PlacementValueText));

        var afterGuide = vm.SectionPresentation.ReferencePlane!.LocalZMm;
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                vm.SectionPresentation,
                out var tipZ));
        Assert.Equal(tipZ, afterGuide, ToleranceMm);
        Assert.NotEqual(beforeGuide, afterGuide, ToleranceMm);
    }

    [Fact]
    public void Explicit_To_WallPlateBottom_RebasesBottomEdge_PreservesPhysical()
    {
        var vm = CreateHostLikeVm();
        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforePhys = CapturePhysical(before!);
        var beforeWp = beforePhys.First(p => p.Role == RoofAutomaticPurlinGeneratorRole.WallPlate);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;

        Assert.True(vm.TryGetPreviewPlan(out var after));
        Assert.True(vm.TryCreateDraft(out _, out var datum));
        Assert.Equal(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            datum!.ReferenceKind);
        Assert.Equal(beforeWp.BottomLocalZMm, datum.ReferenceLocalZMm, ToleranceMm);
        Assert.Equal("0", Normalize(vm.WallPlateRow.PlacementValueText));
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(after!));

        var guide = vm.SectionPresentation.ReferencePlane!.LocalZMm;
        Assert.True(
            AutomaticPurlinSectionPresentation.TryResolveSchematicWallPlateBottomLocalZMm(
                vm.SectionPresentation.Members,
                out var schematicWpBottom));
        Assert.Equal(schematicWpBottom, guide, ToleranceMm);
    }

    [Fact]
    public void WallPlateBottom_To_Explicit_RestoresCachedLocalZ_PreservesPhysical()
    {
        var vm = CreateHostLikeVm();
        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforePhys = CapturePhysical(before!);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        Assert.True(vm.TryGetPreviewPlan(out _));

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;

        Assert.True(vm.TryGetPreviewPlan(out var after));
        Assert.True(vm.TryCreateDraft(out _, out var datum));
        Assert.Equal(1000d, datum!.ReferenceLocalZMm, 9);
        Assert.Equal("1000", Normalize(vm.ReferenceLocalZText));
        Assert.Equal("-1000", Normalize(vm.WallPlateRow.PlacementValueText));
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(after!));
    }

    [Fact]
    public void SourceEavePlane_Transitions_PreservePhysical_ForBottomEdge()
    {
        var vm = CreateHostLikeVm();
        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforePhys = CapturePhysical(before!);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
        Assert.True(vm.TryGetPreviewPlan(out var atEave));
        Assert.Equal("0", Normalize(vm.ReferenceLocalZText));
        Assert.Equal("0", Normalize(vm.WallPlateRow.PlacementValueText));
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(atEave!));

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        Assert.True(vm.TryGetPreviewPlan(out var back));
        Assert.True(vm.TryCreateDraft(out _, out var restored));
        Assert.Equal(1000d, restored!.ReferenceLocalZMm, 9);
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(back!));
    }

    [Fact]
    public void RepeatedForwardBackward_BottomEdgeDatumSwitch_PreservesPhysical()
    {
        var vm = CreateHostLikeVm();
        Assert.True(vm.TryGetPreviewPlan(out var baseline));
        var phys = CapturePhysical(baseline!);

        for (var i = 0; i < 3; i++)
        {
            vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
            Assert.True(vm.TryGetPreviewPlan(out var a));
            AssertPhysicalUnchanged(phys, CapturePhysical(a!));

            vm.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
            Assert.True(vm.TryGetPreviewPlan(out var b));
            AssertPhysicalUnchanged(phys, CapturePhysical(b!));

            vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
            Assert.True(vm.TryGetPreviewPlan(out var c));
            AssertPhysicalUnchanged(phys, CapturePhysical(c!));
        }
    }

    [Fact]
    public void NegativeBottomEdgeOffsets_SurviveDatumSwitch()
    {
        var solved = Solve(45d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var layout = CreateBottomEdgeLayout(-200d, -100d, seatingPercent: 0d);
        var vm = CreateVm(solved, layout, datum);
        vm.RidgeEnabled = false;
        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforePhys = CapturePhysical(before!);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
        Assert.True(vm.TryGetPreviewPlan(out var after));
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(after!));
        Assert.Equal("800", Normalize(vm.WallPlateRow.PlacementValueText));
        Assert.Equal("900", Normalize(vm.Rows[0].PlacementValueText));
    }

    [Fact]
    public void WallPlateDisabled_UnderWallPlateBottom_ClearsReferenceGuide()
    {
        var vm = CreateHostLikeVm();
        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        Assert.NotNull(vm.SectionPresentation.ReferencePlane);

        vm.WallPlateEnabled = false;

        Assert.Null(vm.SectionPresentation.ReferencePlane);
    }

    [Fact]
    public void PlanDistance_EaveRidge_7000_1000_6000_RoundTrip_PreservesPhysical()
    {
        var solved = Solve(45d, shortSpanMm: 14000d);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                solved.Geometry,
                out var maxExclusiveMm));
        Assert.Equal(7000d, maxExclusiveMm, 3);

        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var layout = new RoofAutomaticPurlinLayout(
            false,
            [
                new RoofAutomaticPurlinLayoutItem(
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
                    1000d,
                    SeatingDepth: seating,
                    WidthMm: 160d,
                    HeightMm: 220d),
            ])
        {
            WallPlateEnabled = false,
        };
        var vm = CreateVm(
            solved,
            layout,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));
        var row = Assert.Single(vm.Rows);
        if (row.SelectedRidgeReference is null && row.RidgeReferences.Count == 1)
        {
            row.SelectedRidgeReference = row.RidgeReferences[0];
        }

        Assert.True(vm.TryGetPreviewPlan(out var before));
        var beforePhys = CapturePhysical(before!);
        Assert.Equal("1000", Normalize(row.PlacementValueText));

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        Assert.Equal("6000", Normalize(row.PlacementValueText));
        Assert.True(vm.TryGetPreviewPlan(out var mid));
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(mid!));

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
        Assert.Equal("1000", Normalize(row.PlacementValueText));
        Assert.True(vm.TryGetPreviewPlan(out var after));
        AssertPhysicalUnchanged(beforePhys, CapturePhysical(after!));
    }

    [Fact]
    public void LeftAndRightMembers_StayMirrored_AfterDatumSwitch()
    {
        var vm = CreateHostLikeVm();
        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        Assert.True(vm.TryGetPreviewPlan(out var plan));

        var left = plan!.Items
            .Where(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .OrderBy(i => i.Segment3D.Start.X)
            .ToList();
        Assert.True(left.Count >= 2);
        Assert.Equal(
            left[0].ElevationProfile!.BottomLocalZMm,
            left[^1].ElevationProfile!.BottomLocalZMm,
            ToleranceMm);
    }

    [Fact]
    public void InvalidDraft_RetainsSchematic_AndMarksStale()
    {
        var vm = CreateHostLikeVm();
        Assert.True(vm.TryGetPreviewPlan(out _));
        Assert.False(vm.IsSchematicStale);
        var guideBefore = vm.SectionPresentation.ReferencePlane!.LocalZMm;

        vm.WallPlateRow.PlacementValueText = "not-a-number";

        Assert.True(vm.TryGetPreviewPlan(out _));
        Assert.True(vm.CanPreview);
        Assert.False(vm.TryCreateDraft(out _, out _));
        Assert.True(vm.IsSchematicStale);
        Assert.Contains("stale", vm.SchematicStaleMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(guideBefore, vm.SectionPresentation.ReferencePlane!.LocalZMm, ToleranceMm);
    }

    private static AutomaticPurlinDialogViewModel CreateHostLikeVm()
    {
        var solved = Solve(45d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var layout = CreateBottomEdgeLayout(-1000d, -500d, 500d, seatingPercent: 0d);
        return CreateVm(solved, layout, datum);
    }

    private static AutomaticPurlinDialogViewModel CreateVm(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout,
        RoofRelativeElevationDatum datum) =>
        new(
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

    private static RoofAutomaticPurlinLayout CreateBottomEdgeLayout(
        double wallOffsetMm,
        double int1Mm,
        double? int2Mm = null,
        double seatingPercent = 0d)
    {
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            seatingPercent);
        var intermediates = new List<RoofAutomaticPurlinLayoutItem>
        {
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                int1Mm,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d),
        };
        if (int2Mm is { } second)
        {
            intermediates.Add(new(
                "cccccccccccccccccccccccccccccccc",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                second,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d));
        }

        return new RoofAutomaticPurlinLayout(true, intermediates.AsReadOnly())
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                wallOffsetMm,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = seating,
        };
    }

    private static void AssertPhysicalUnchanged(
        IReadOnlyList<Phys> before,
        IReadOnlyList<Phys> after)
    {
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Role, after[i].Role);
            Assert.Equal(before[i].BottomLocalZMm, after[i].BottomLocalZMm, ToleranceMm);
            Assert.Equal(before[i].CenterLocalZMm, after[i].CenterLocalZMm, ToleranceMm);
            Assert.Equal(before[i].TopLocalZMm, after[i].TopLocalZMm, ToleranceMm);
            Assert.Equal(before[i].StartX, after[i].StartX, ToleranceMm);
            Assert.Equal(before[i].StartY, after[i].StartY, ToleranceMm);
            Assert.Equal(before[i].StartZ, after[i].StartZ, ToleranceMm);
        }
    }

    private static List<Phys> CapturePhysical(RoofAutomaticPurlinPlan plan) =>
        plan.Items
            .Where(i => i.ElevationProfile is not null)
            .OrderBy(i => i.GeneratorRole)
            .ThenBy(i => i.LayoutItemId, StringComparer.Ordinal)
            .ThenBy(i => i.Segment3D.Start.X)
            .Select(i => new Phys(
                i.GeneratorRole,
                i.ElevationProfile!.BottomLocalZMm,
                i.ElevationProfile.CenterLocalZMm,
                i.ElevationProfile.TopLocalZMm,
                i.Segment3D.Start.X,
                i.Segment3D.Start.Y,
                i.Segment3D.Start.Z))
            .ToList();

    private static string Normalize(string text) =>
        text.Replace(',', '.').Trim();

    private static SolvedFixture Solve(double pitchDegrees, double shortSpanMm = 7000d)
    {
        var longSpanMm = Math.Max(shortSpanMm + 2000d, 14000d);
        var points = new RoofPoint2D[]
        {
            new(0, 0), new(longSpanMm, 0), new(longSpanMm, shortSpanMm), new(0, shortSpanMm),
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
        double StartX,
        double StartY,
        double StartZ);
}
