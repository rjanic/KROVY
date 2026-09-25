using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// HOST: WallPlate width edits must immediately revalidate min plan-distance
/// without requiring a PlanDistance / BottomEdge field edit.
/// </summary>
public sealed class AutomaticPurlinWallPlateWidthRevalidationTests
{
    [Theory]
    [InlineData(RoofRelativeElevationReferenceKind.WallPlateBottom)]
    [InlineData(RoofRelativeElevationReferenceKind.SourceEavePlane)]
    [InlineData(RoofRelativeElevationReferenceKind.ExplicitLocalPlane)]
    public void Case1_WidthDecrease_ClearsMinDistanceErrorWithoutEditingPlanDistance(
        RoofRelativeElevationReferenceKind datumKind)
    {
        var viewModel = CreateHostLikeViewModel(datumKind);
        viewModel.WallPlateRow.WidthText = "300";
        viewModel.WallPlateRow.PlacementValueText = "100";
        Assert.False(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.Contains("150", viewModel.ValidationMessage, StringComparison.Ordinal);

        viewModel.WallPlateRow.WidthText = "140";

        Assert.Equal("100", viewModel.WallPlateRow.PlacementValueText);
        Assert.True(
            string.IsNullOrEmpty(viewModel.ValidationMessage),
            viewModel.ValidationMessage);
        Assert.False(
            viewModel.WallPlateRow.PlacementValueHasError,
            viewModel.WallPlateRow.PlacementValueErrorText);
        Assert.False(viewModel.WallPlateTabHasError);
        Assert.False(viewModel.IsSchematicStale);
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan) && plan is not null);
        Assert.Contains(
            plan!.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                    Math.Abs(item.WidthMm - 140d) < 1e-6);
    }

    [Theory]
    [InlineData(RoofRelativeElevationReferenceKind.WallPlateBottom)]
    [InlineData(RoofRelativeElevationReferenceKind.SourceEavePlane)]
    [InlineData(RoofRelativeElevationReferenceKind.ExplicitLocalPlane)]
    public void Case2_WidthIncrease_InvalidatesWithoutEditingPlanDistance(
        RoofRelativeElevationReferenceKind datumKind)
    {
        var viewModel = CreateHostLikeViewModel(datumKind);
        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.PlacementValueText = "100";
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);

        viewModel.WallPlateRow.WidthText = "300";

        Assert.Equal("100", viewModel.WallPlateRow.PlacementValueText);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.True(viewModel.WallPlateTabHasError);
        Assert.Contains("150", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.Contains("300", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.True(viewModel.IsSchematicStale);
        Assert.True(viewModel.CanPreview);
    }

    [Fact]
    public void Case3_WidthIncrease_FromValidWideStation_InvalidatesWithoutEditingDistance()
    {
        var viewModel = CreateHostLikeViewModel(
            RoofRelativeElevationReferenceKind.WallPlateBottom);
        viewModel.WallPlateRow.WidthText = "300";
        viewModel.WallPlateRow.PlacementValueText = "200";
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);

        viewModel.WallPlateRow.WidthText = "500";

        Assert.Equal("200", viewModel.WallPlateRow.PlacementValueText);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.Contains("250", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.Contains("500", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.True(viewModel.IsSchematicStale);
    }

    [Theory]
    [InlineData("160")]
    [InlineData("300")]
    [InlineData("500")]
    public void WidthChange_WhileAlreadyValid_KeepsValidWhenStillAboveMinimum(string widthText)
    {
        var viewModel = CreateHostLikeViewModel(
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        viewModel.WallPlateRow.PlacementValueText = "700";
        viewModel.WallPlateRow.WidthText = widthText;
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.False(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.Equal("700", viewModel.WallPlateRow.PlacementValueText);
    }

    [Fact]
    public void BottomEdge_WidthChange_WhileValid_PreservesPlacementAndUpdatesPlanWidth()
    {
        var viewModel = CreateHostLikeViewModel(
            RoofRelativeElevationReferenceKind.SourceEavePlane);
        viewModel.WallPlateRow.PlacementValueText = "700";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        var bottomText = viewModel.WallPlateRow.PlacementValueText;

        viewModel.WallPlateRow.WidthText = "160";

        Assert.Equal(bottomText, viewModel.WallPlateRow.PlacementValueText);
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
        Assert.False(viewModel.IsSchematicStale);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan) && plan is not null);
        Assert.Contains(
            plan!.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                    Math.Abs(item.WidthMm - 160d) < 1e-6);
    }

    [Fact]
    public void HostFixtures_700And1800_RemainValidAcrossWidth140()
    {
        var viewModel = CreateHostLikeViewModel(
            RoofRelativeElevationReferenceKind.WallPlateBottom);
        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.PlacementValueText = "700";
        viewModel.Rows[0].PlacementValueText = "1800";
        Assert.True(viewModel.CanApply, viewModel.ValidationMessage);
    }

    private static AutomaticPurlinDialogViewModel CreateHostLikeViewModel(
        RoofRelativeElevationReferenceKind datumKind)
    {
        var solved = Solve(45d);
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

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
