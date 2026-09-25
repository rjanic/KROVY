using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// HOST: PlanDistanceFromEave ↔ PlanDistanceFromRidge must preserve the physical
/// WallPlate station and must not validate ridge distance as an eave distance.
/// </summary>
public sealed class AutomaticPurlinWallPlateEaveRidgeConversionTests
{
    private const double ToleranceMm = 3d;

    [Theory]
    [InlineData(15d, 150d)]
    [InlineData(45d, 150d)]
    [InlineData(55d, 150d)]
    public void Host_FromEave_ToFromRidge_RoundTrip_StaysValid(
        double pitchDegrees,
        double fromEaveMm)
    {
        var viewModel = CreateHostLikeViewModel(pitchDegrees);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                viewModelGeometry(viewModel, pitchDegrees),
                out var maxExclusiveMm));
        var fromRidgeMm = maxExclusiveMm - fromEaveMm;
        if (pitchDegrees == 15d)
        {
            Assert.Equal(3000d, maxExclusiveMm, ToleranceMm);
            Assert.Equal(2850d, fromRidgeMm, ToleranceMm);
        }

        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText =
            fromEaveMm.ToString("0.###", CultureInfo.InvariantCulture);
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.False(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.True(viewModel.TryGetPreviewPlan(out var before) && before is not null);
        var beforePhys = CaptureWallPlate(before!);

        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

        Assert.Equal(
            fromRidgeMm.ToString("0.###", CultureInfo.GetCultureInfo("en")),
            Normalize(viewModel.WallPlateRow.PlacementValueText));
        Assert.True(
            string.IsNullOrEmpty(viewModel.ValidationMessage),
            viewModel.ValidationMessage);
        Assert.False(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.False(viewModel.WallPlateTabHasError);
        Assert.False(viewModel.IsSchematicStale);
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.True(viewModel.TryGetPreviewPlan(out var mid) && mid is not null);
        AssertPhysicalUnchanged(beforePhys, CaptureWallPlate(mid!));

        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;

        Assert.Equal(
            fromEaveMm.ToString("0.###", CultureInfo.GetCultureInfo("en")),
            Normalize(viewModel.WallPlateRow.PlacementValueText));
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.False(viewModel.IsSchematicStale);
        Assert.True(viewModel.TryGetPreviewPlan(out var after) && after is not null);
        AssertPhysicalUnchanged(beforePhys, CaptureWallPlate(after!));
    }

    [Theory]
    [InlineData(15d, 69d, false)]
    [InlineData(15d, 70d, true)]
    [InlineData(15d, 150d, true)]
    public void Host15_Boundary_FromEaveAndEquivalentFromRidge(
        double pitchDegrees,
        double fromEaveMm,
        bool expectValid)
    {
        var viewModel = CreateHostLikeViewModel(pitchDegrees);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                viewModelGeometry(viewModel, pitchDegrees),
                out var maxExclusiveMm));
        var fromRidgeMm = maxExclusiveMm - fromEaveMm;

        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText =
            fromEaveMm.ToString("0.###", CultureInfo.InvariantCulture);
        Assert.Equal(expectValid, viewModel.CanApply);

        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
        Assert.Equal(
            fromRidgeMm.ToString("0.###", CultureInfo.GetCultureInfo("en")),
            Normalize(viewModel.WallPlateRow.PlacementValueText));
        Assert.Equal(expectValid, viewModel.CanApply);
        if (!expectValid)
        {
            Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
            Assert.Contains("70", viewModel.ValidationMessage, StringComparison.Ordinal);
            Assert.True(viewModel.IsSchematicStale);
        }
        else
        {
            Assert.False(viewModel.WallPlateRow.PlacementValueHasError);
            Assert.False(viewModel.IsSchematicStale);
        }
    }

    [Fact]
    public void AfterRidgeConversion_WidthIncrease_CanInvalidateWithoutEditingDistance()
    {
        var viewModel = CreateHostLikeViewModel(15d);
        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.PlacementValueText = "150";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        var ridgeText = viewModel.WallPlateRow.PlacementValueText;

        viewModel.WallPlateRow.WidthText = "400";

        Assert.Equal(ridgeText, viewModel.WallPlateRow.PlacementValueText);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.Contains("200", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.True(viewModel.IsSchematicStale);
    }

    [Theory]
    [InlineData(RoofRelativeElevationReferenceKind.WallPlateBottom)]
    [InlineData(RoofRelativeElevationReferenceKind.SourceEavePlane)]
    [InlineData(RoofRelativeElevationReferenceKind.ExplicitLocalPlane)]
    public void AllReferenceKinds_EaveRidgeConversion_StaysValid(
        RoofRelativeElevationReferenceKind datumKind)
    {
        var viewModel = CreateHostLikeViewModel(15d, datumKind);
        viewModel.WallPlateRow.PlacementValueText = "150";
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.False(viewModel.IsSchematicStale);
    }

    private static AutomaticPurlinDialogViewModel CreateHostLikeViewModel(
        double pitchDegrees,
        RoofRelativeElevationReferenceKind datumKind =
            RoofRelativeElevationReferenceKind.WallPlateBottom)
    {
        var solved = Solve(pitchDegrees);
        var viewModel = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout: null,
            layoutExists: false,
            datum: null,
            datumExists: false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en"),
            AutomaticPurlinDialogMode.ProductionEdit);
        while (viewModel.Rows.Count > 0)
        {
            viewModel.RemoveRow(viewModel.Rows[0]);
        }

        Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 125d));
        viewModel.WallPlateEnabled = true;
        viewModel.RidgeEnabled = true;
        viewModel.ReferenceKind = datumKind;
        if (datumKind == RoofRelativeElevationReferenceKind.ExplicitLocalPlane)
        {
            viewModel.ReferenceLocalZText = "0";
        }

        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.HeightText = "140";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText = "700";
        viewModel.WallPlateRow.SelectedSeatingMode =
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        viewModel.WallPlateRow.SeatingDepthValueText = "25";
        _ = viewModel.AddRow();
        viewModel.Rows[0].PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.Rows[0].PlacementValueText = "1800";
        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
        return viewModel;
    }

    // Geometry is private on the VM; re-solve the same footprint used by CreateHostLikeViewModel.
    private static HipRoofGeometry viewModelGeometry(
        AutomaticPurlinDialogViewModel _,
        double pitchDegrees) =>
        Solve(pitchDegrees).Geometry;

    private static string Normalize(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("en"), out var value)
            ? value.ToString("0.###", CultureInfo.GetCultureInfo("en"))
            : text;

    private static WallPlatePhysical CaptureWallPlate(RoofAutomaticPurlinPlan plan)
    {
        var walls = plan.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .OrderBy(item => item.Segment3D.Start.X)
            .ToArray();
        Assert.NotEmpty(walls);
        return new WallPlatePhysical(
            walls.Select(w => w.ElevationProfile!.BottomLocalZMm).ToArray(),
            walls.Select(w => w.PhysicalPlacement!.PurlinCenterLocalZMm).ToArray(),
            walls.Select(w => w.PhysicalPlacement!.RafterUpperSurfaceLocalZMm).ToArray());
    }

    private static void AssertPhysicalUnchanged(WallPlatePhysical before, WallPlatePhysical after)
    {
        Assert.Equal(before.BottomLocalZMm.Length, after.BottomLocalZMm.Length);
        for (var i = 0; i < before.BottomLocalZMm.Length; i++)
        {
            Assert.Equal(before.BottomLocalZMm[i], after.BottomLocalZMm[i], ToleranceMm);
            Assert.Equal(before.CenterLocalZMm[i], after.CenterLocalZMm[i], ToleranceMm);
            Assert.Equal(before.RoofSurfaceLocalZMm[i], after.RoofSurfaceLocalZMm[i], ToleranceMm);
        }
    }

    private static SolvedFixture Solve(double pitchDegrees)
    {
        var points = new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) };
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

    private sealed record WallPlatePhysical(
        double[] BottomLocalZMm,
        double[] CenterLocalZMm,
        double[] RoofSurfaceLocalZMm);

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
