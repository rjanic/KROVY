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
/// Reproduces confirmed F1–F3 datum mode-switch defects and locks the per-kind LocalZ contract.
/// Expected relatives use closed-form conversion, not a second call to the planner under test.
/// </summary>
public sealed class AutomaticPurlinReferenceDatumSwitchTests
{
    [Fact]
    public void SourceEave_NeverInheritsLocalZ_FromWallPlateBottomOrExplicit()
    {
        var solved = Solve();
        var vm = CreateVm(
            solved,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.WallPlateBottom,
                0d,
                250d));
        vm.WallPlateEnabled = true;
        Assert.True(vm.TryCreateDraft(out _, out var wpDatum));
        Assert.True(vm.TryGetPreviewPlan(out var wpPlan));
        var wpWalls = wpPlan!.Items
            .Where(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ToArray();
        Assert.NotEmpty(wpWalls);
        Assert.All(
            wpWalls,
            wall => Assert.Equal(
                wall.ElevationProfile!.BottomLocalZMm,
                wpDatum!.ReferenceLocalZMm,
                6));
        Assert.True(double.IsFinite(wpDatum!.ReferenceLocalZMm));

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
        Assert.True(vm.TryCreateDraft(out _, out var seDatum));
        Assert.Equal(RoofRelativeElevationReferenceKind.SourceEavePlane, seDatum!.ReferenceKind);
        Assert.Equal(0d, seDatum.ReferenceLocalZMm, 9);
        Assert.Equal("0", NormalizeLocalZText(vm.ReferenceLocalZText));

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        vm.ReferenceLocalZText = "900";
        Assert.True(vm.TryCreateDraft(out _, out var exDatum));
        Assert.Equal(900d, exDatum!.ReferenceLocalZMm, 9);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
        Assert.True(vm.TryCreateDraft(out _, out var seAgain));
        Assert.Equal(0d, seAgain!.ReferenceLocalZMm, 9);
        Assert.Equal("0", NormalizeLocalZText(vm.ReferenceLocalZText));
    }

    [Fact]
    public void Explicit_DisplayedLocalZ_EqualsPlannerDatum_AfterKindSwitches()
    {
        var solved = Solve();
        var vm = CreateVm(
            solved,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));
        vm.WallPlateEnabled = true;

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        vm.ReferenceLocalZText = "500";
        Assert.True(vm.TryCreateDraft(out _, out var first));
        Assert.Equal(500d, first!.ReferenceLocalZMm, 9);
        AssertUiMatchesDatum(vm, first);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        Assert.True(vm.TryCreateDraft(out _, out var wp));
        Assert.True(vm.TryGetPreviewPlan(out var wpPlan));
        var wpWalls = wpPlan!.Items
            .Where(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ToArray();
        Assert.NotEmpty(wpWalls);
        Assert.All(
            wpWalls,
            wall => Assert.Equal(
                wall.ElevationProfile!.BottomLocalZMm,
                wp!.ReferenceLocalZMm,
                6));
        Assert.True(double.IsFinite(wp!.ReferenceLocalZMm));
        AssertUiMatchesDatum(vm, wp);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        Assert.True(vm.TryCreateDraft(out _, out var restored));
        Assert.Equal(500d, restored!.ReferenceLocalZMm, 9);
        AssertUiMatchesDatum(vm, restored);
        Assert.Equal("500", NormalizeLocalZText(vm.ReferenceLocalZText));
    }

    [Theory]
    [InlineData(RoofRelativeElevationReferenceKind.SourceEavePlane)]
    [InlineData(RoofRelativeElevationReferenceKind.ExplicitLocalPlane)]
    [InlineData(RoofRelativeElevationReferenceKind.WallPlateBottom)]
    public void AllSupportedKinds_ProduceValidDraft(RoofRelativeElevationReferenceKind kind)
    {
        var vm = CreateVm(
            Solve(),
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));
        vm.WallPlateEnabled = true;
        vm.ReferenceKind = kind;
        if (kind == RoofRelativeElevationReferenceKind.ExplicitLocalPlane)
        {
            vm.ReferenceLocalZText = "125.5";
        }

        Assert.True(vm.TryCreateDraft(out _, out var datum));
        Assert.Equal(kind, datum!.ReferenceKind);
        AssertUiMatchesDatum(vm, datum);
        if (kind == RoofRelativeElevationReferenceKind.SourceEavePlane)
        {
            Assert.Equal(0d, datum.ReferenceLocalZMm, 9);
        }
    }

    [Fact]
    public void RepeatedTransitionsWithoutApply_PreservePerKindLocalZSemantics()
    {
        var vm = CreateVm(
            Solve(),
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                -250d,
                400d));
        vm.WallPlateEnabled = true;
        vm.RelativeReferenceText = "-0.250";

        // Explicit -> SourceEave -> Explicit
        vm.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
        Assert.True(vm.TryCreateDraft(out _, out var se));
        Assert.Equal(0d, se!.ReferenceLocalZMm, 9);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        Assert.True(vm.TryCreateDraft(out _, out var ex));
        Assert.Equal(400d, ex!.ReferenceLocalZMm, 9);
        AssertUiMatchesDatum(vm, ex);

        // Explicit -> WallPlateBottom -> SourceEave
        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        Assert.True(vm.TryCreateDraft(out _, out var wp));
        Assert.True(vm.TryGetPreviewPlan(out var wpPlan));
        var wpWalls = wpPlan!.Items
            .Where(i => i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ToArray();
        Assert.NotEmpty(wpWalls);
        Assert.All(
            wpWalls,
            wall => Assert.Equal(
                wall.ElevationProfile!.BottomLocalZMm,
                wp!.ReferenceLocalZMm,
                6));
        Assert.True(double.IsFinite(wp!.ReferenceLocalZMm));

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
        Assert.True(vm.TryCreateDraft(out _, out var se2));
        Assert.Equal(0d, se2!.ReferenceLocalZMm, 9);

        // SourceEave -> WallPlateBottom -> Explicit restores 400
        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        Assert.True(vm.TryCreateDraft(out _, out _));
        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        Assert.True(vm.TryCreateDraft(out _, out var ex2));
        Assert.Equal(400d, ex2!.ReferenceLocalZMm, 9);
        AssertUiMatchesDatum(vm, ex2);
    }

    [Fact]
    public void Explicit_InvalidLocalZText_FailsClosed_DoesNotReuseStaleValue()
    {
        var vm = CreateVm(
            Solve(),
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                200d));
        vm.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        Assert.True(vm.TryCreateDraft(out _, out var ok));
        Assert.Equal(200d, ok!.ReferenceLocalZMm, 9);

        vm.ReferenceLocalZText = "not-a-number";
        Assert.False(vm.TryCreateDraft(out _, out _));
        Assert.True(vm.ReferenceLocalZHasError);
    }

    [Fact]
    public void PlanDriven_ChangingReferenceAlone_PreservesPhysicalXyz_AndShiftsRelativesEqually()
    {
        var solved = Solve();
        var vm = CreateVm(
            solved,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));
        vm.WallPlateEnabled = true;
        vm.RidgeEnabled = true;
        vm.WallPlateRow.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        vm.WallPlateRow.PlacementValueText = "850";

        Assert.True(vm.TryGetPreviewPlan(out var planSe));
        Assert.True(vm.TryCreateDraft(out _, out var seDatum));
        Assert.Equal(0d, seDatum!.ReferenceLocalZMm, 9);

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        vm.RelativeReferenceText = "±0.000";
        Assert.True(vm.TryGetPreviewPlan(out var planWp));
        Assert.True(vm.TryCreateDraft(out _, out var wpDatum));

        var seItems = planSe!.Items.OrderBy(i => i.GeneratedKey.ToString()).ToArray();
        var wpItems = planWp!.Items.OrderBy(i => i.GeneratedKey.ToString()).ToArray();
        Assert.Equal(seItems.Length, wpItems.Length);

        // Independent expected shift: rel_WPB - rel_SE = (0 - L_wp) - (0 - 0) = -L_wp
        var expectedRelShift = -wpDatum!.ReferenceLocalZMm;

        for (var i = 0; i < seItems.Length; i++)
        {
            var a = seItems[i];
            var b = wpItems[i];
            Assert.Equal(a.Segment3D.Start.X, b.Segment3D.Start.X, 6);
            Assert.Equal(a.Segment3D.Start.Y, b.Segment3D.Start.Y, 6);
            Assert.Equal(a.Segment3D.Start.Z, b.Segment3D.Start.Z, 6);
            Assert.Equal(a.ElevationProfile!.BottomLocalZMm, b.ElevationProfile!.BottomLocalZMm, 6);
            Assert.Equal(a.ElevationProfile.CenterLocalZMm, b.ElevationProfile.CenterLocalZMm, 6);
            Assert.Equal(a.ElevationProfile.TopLocalZMm, b.ElevationProfile.TopLocalZMm, 6);

            Assert.Equal(
                expectedRelShift,
                b.ElevationProfile.BottomRelativeElevationMm -
                a.ElevationProfile.BottomRelativeElevationMm,
                3);
            Assert.Equal(
                expectedRelShift,
                b.ElevationProfile.CenterRelativeElevationMm -
                a.ElevationProfile.CenterRelativeElevationMm,
                3);
            Assert.Equal(
                expectedRelShift,
                b.ElevationProfile.TopRelativeElevationMm -
                a.ElevationProfile.TopRelativeElevationMm,
                3);

            Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                a, solved.Geometry.PrimarySlopeDegrees, vm.RafterHeightMm, out var rpA));
            Assert.True(RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                b, solved.Geometry.PrimarySlopeDegrees, vm.RafterHeightMm, out var rpB));
            Assert.Equal(expectedRelShift, rpB - rpA, 3);

            // Closed-form relative from locals (not planner internals)
            var expectedBottomRel =
                wpDatum.ReferenceRelativeElevationMm +
                (b.ElevationProfile.BottomLocalZMm - wpDatum.ReferenceLocalZMm);
            Assert.Equal(expectedBottomRel, b.ElevationProfile.BottomRelativeElevationMm, 6);
        }
    }

    [Theory]
    [InlineData(-500d)]
    [InlineData(0d)]
    [InlineData(750d)]
    public void RelativeReference_PositiveZeroNegative_TransformDisplayOnly_ForPlanDriven(
        double relativeMm)
    {
        var vm = CreateVm(
            Solve(),
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));
        vm.WallPlateEnabled = true;
        vm.WallPlateRow.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        vm.WallPlateRow.PlacementValueText = "700";

        Assert.True(vm.TryGetPreviewPlan(out var baseline));
        var baseItem = baseline!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        var baseLocal = baseItem.ElevationProfile!.BottomLocalZMm;

        vm.RelativeReferenceText = RoofRelativeElevationDatumRules.FormatMetres(
            relativeMm,
            CultureInfo.GetCultureInfo("en-US"));
        Assert.True(vm.TryGetPreviewPlan(out var shifted));
        Assert.True(vm.TryCreateDraft(out _, out var datum));
        var item = shifted!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);

        Assert.Equal(baseLocal, item.ElevationProfile!.BottomLocalZMm, 6);
        Assert.Equal(
            relativeMm + (baseLocal - 0d),
            item.ElevationProfile.BottomRelativeElevationMm,
            6);
        Assert.Equal(relativeMm, datum!.ReferenceRelativeElevationMm, 6);
    }

    [Fact]
    public void StoredInconsistentSourceEave_SurfacesDiagnostic_AndDraftsSafeSourceEaveZero()
    {
        var solved = Solve();
        var vm = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            null,
            false,
            datum: null,
            datumExists: true,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en-US"),
            AutomaticPurlinDialogMode.ProductionEdit,
            existingAutomaticPurlinCount: 0,
            storedDatumLoadError: RoofRelativeElevationDatumError.InconsistentSourceEaveLocalZ);

        Assert.Contains(
            "invalid local Z",
            vm.DatumPersistenceStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.TryCreateDraft(out _, out var datum));
        Assert.Equal(RoofRelativeElevationReferenceKind.SourceEavePlane, datum!.ReferenceKind);
        Assert.Equal(0d, datum.ReferenceLocalZMm, 9);
    }

    private static void AssertUiMatchesDatum(
        AutomaticPurlinDialogViewModel vm,
        RoofRelativeElevationDatum datum)
    {
        Assert.True(double.TryParse(
            NormalizeLocalZText(vm.ReferenceLocalZText),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var uiMm));
        Assert.Equal(datum.ReferenceLocalZMm, uiMm, 3);
    }

    private static string NormalizeLocalZText(string text) =>
        text.Replace(',', '.').Trim();

    private static AutomaticPurlinDialogViewModel CreateVm(
        SolvedFixture solved,
        RoofRelativeElevationDatum datum) =>
        new(
            solved.Geometry,
            solved.Provenance,
            null,
            false,
            datum,
            true,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en-US"),
            AutomaticPurlinDialogMode.ReadOnlyPreview,
            0);

    private static SolvedFixture Solve()
    {
        var points = new RoofPoint2D[] { new(0, 0), new(12000, 0), new(12000, 7000), new(0, 7000) };
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
                new RoofParameters(35d),
                RoofKind.Hip)).Geometry);
        return new SolvedFixture(geometry, provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
