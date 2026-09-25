using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutomaticPurlinPlanDistanceBoundaryViewModelTests
{
    [Theory]
    [InlineData(1100d)]
    [InlineData(1200d)]
    [InlineData(1500d)]
    public void PlanDistanceBelowExclusiveMax_KeepsFullAssembly(double distanceMm)
    {
        var (viewModel, geometry) = CreateHostLikeViewModel(pitchDegrees: 45d);
        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                geometry,
                out var maxExclusiveMm));
        Assert.True(distanceMm < maxExclusiveMm);

        viewModel.WallPlateRow.PlacementValueText = distanceMm.ToString(CultureInfo.InvariantCulture);

        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
        Assert.True(string.IsNullOrEmpty(viewModel.ValidationMessage), viewModel.ValidationMessage);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan) && plan is not null);
        Assert.Contains(
            plan!.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.Contains(
            plan.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Contains(
            plan.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate);
        Assert.NotEqual("—", viewModel.WallPlateRow.RoofPlaneRelative);
        Assert.NotEqual("—", viewModel.RidgeTechnicalSummary.BottomRelative);
        Assert.Contains(
            viewModel.SectionPresentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.Intermediate);
    }

    [Fact]
    public void PlanDistanceAboveExclusiveMax_RetainsLastValidPreviewAndNamesRange()
    {
        var (viewModel, geometry) = CreateHostLikeViewModel(pitchDegrees: 45d);
        viewModel.WallPlateRow.PlacementValueText = "700";
        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
        Assert.True(viewModel.TryGetPreviewPlan(out var validPlan) && validPlan is not null);
        var validWallRoofPlane = viewModel.WallPlateRow.RoofPlaneRelative;
        var validRidgeBottom = viewModel.RidgeTechnicalSummary.BottomRelative;
        var validMemberCount = viewModel.SectionPresentation.Members.Count;

        Assert.True(
            RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                geometry,
                out var maxExclusiveMm));
        var invalidDistance = maxExclusiveMm + 50d;
        viewModel.WallPlateRow.PlacementValueText =
            invalidDistance.ToString("0.###", CultureInfo.InvariantCulture);

        Assert.False(string.IsNullOrEmpty(viewModel.ValidationMessage));
        Assert.Contains(
            maxExclusiveMm.ToString("0.###", CultureInfo.GetCultureInfo("en")),
            viewModel.ValidationMessage,
            StringComparison.Ordinal);
        Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.True(viewModel.CanPreview);
        Assert.True(viewModel.TryGetPreviewPlan(out var retained) && retained is not null);
        Assert.Equal(validPlan!.Items.Count, retained!.Items.Count);
        Assert.Equal(validWallRoofPlane, viewModel.WallPlateRow.RoofPlaneRelative);
        Assert.Equal(validRidgeBottom, viewModel.RidgeTechnicalSummary.BottomRelative);
        Assert.Equal(validMemberCount, viewModel.SectionPresentation.Members.Count);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void PlanDistanceZero_WallPlate_IsInvalidAndRetainsLastValidPreview()
    {
        var (viewModel, _) = CreateHostLikeViewModel(pitchDegrees: 45d);
        Assert.True(viewModel.TryGetPreviewPlan(out var validPlan) && validPlan is not null);
        var validMemberCount = viewModel.SectionPresentation.Members.Count;

        viewModel.WallPlateRow.PlacementValueText = "0";

        Assert.False(string.IsNullOrEmpty(viewModel.ValidationMessage));
        Assert.Contains("70", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.Contains("140", viewModel.ValidationMessage, StringComparison.Ordinal);
        Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.True(viewModel.CanPreview);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.TryGetPreviewPlan(out var retained) && retained is not null);
        Assert.Equal(validPlan!.Items.Count, retained!.Items.Count);
        Assert.Equal(validMemberCount, viewModel.SectionPresentation.Members.Count);
    }

    [Theory]
    [InlineData("69.999", false)]
    [InlineData("70", true)]
    [InlineData("70.001", true)]
    public void WallPlateMinPlanDistance_Boundary_MatchesHalfWidth(
        string distanceText,
        bool expectValid)
    {
        var (viewModel, _) = CreateHostLikeViewModel(pitchDegrees: 45d);
        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.PlacementValueText = distanceText;

        if (expectValid)
        {
            Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
            Assert.True(string.IsNullOrEmpty(viewModel.ValidationMessage), viewModel.ValidationMessage);
            Assert.False(viewModel.WallPlateRow.PlacementValueHasError);
            Assert.True(viewModel.CanApply);
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(viewModel.ValidationMessage));
            Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
            Assert.False(viewModel.CanApply);
            Assert.True(viewModel.CanPreview);
        }
    }

    private static (AutomaticPurlinDialogViewModel ViewModel, HipRoofGeometry Geometry)
        CreateHostLikeViewModel(double pitchDegrees)
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
        ClearIntermediateRows(viewModel);
        Assert.True(viewModel.TryApplySelectedRafterDimensions(80d, 125d));
        viewModel.WallPlateEnabled = true;
        viewModel.RidgeEnabled = true;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
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
        return (viewModel, solved.Geometry);
    }

    private static void ClearIntermediateRows(AutomaticPurlinDialogViewModel viewModel)
    {
        while (viewModel.Rows.Count > 0)
        {
            viewModel.RemoveRow(viewModel.Rows[0]);
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

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
