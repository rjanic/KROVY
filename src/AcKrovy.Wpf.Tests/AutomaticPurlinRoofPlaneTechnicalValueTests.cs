using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutomaticPurlinRoofPlaneTechnicalValueTests
{
    [Fact]
    public void HostCase_Dialog_WallPlateBottom_ReportsPlus0_343_NotSvgPlus0_331()
    {
        var solved = Solve(pitchDegrees: 45d);
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
            AutomaticPurlinDialogMode.ReadOnlyPreview);
        ClearIntermediateRows(viewModel);
        Assert.True(viewModel.TryApplySelectedRafterDimensions(80d, 125d));
        viewModel.WallPlateEnabled = true;
        viewModel.RidgeEnabled = false;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.HeightText = "140";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText = "700";
        viewModel.WallPlateRow.SelectedSeatingMode =
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        viewModel.WallPlateRow.SeatingDepthValueText = "25";

        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
        Assert.Equal("±0.000", viewModel.WallPlateRow.BottomRelative);
        Assert.Equal("+0.070", viewModel.WallPlateRow.CenterRelative);
        Assert.Equal("+0.140", viewModel.WallPlateRow.TopRelative);

        var expectedMm = RoofAutomaticPurlinRoofPlaneRules.HostWallPlateRoofPlaneRelativeMm(
            125d, 140d, 140d, 25d, 45d);
        Assert.Equal(342.5825214724774d, expectedMm, 9);
        Assert.Equal("+0.343", viewModel.WallPlateRow.RoofPlaneRelative);
        Assert.Equal(
            RoofRelativeElevationDatumRules.FormatMetres(expectedMm),
            viewModel.WallPlateRow.RoofPlaneRelative);

        // Historical SVG under-read (~+0.331) is fixed; schematic and technical now agree.
        var left = Assert.Single(
            viewModel.SectionPresentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                      member.Side == AutomaticPurlinSectionSide.Left);
        Assert.True(viewModel.TryCreateDraft(out _, out var datum) && datum is not null);
        Assert.True(
            AutomaticPurlinSectionPresentation.TryResolveRoofPlaneLocalZMm(
                viewModel.SectionPresentation,
                left,
                out var svgLocalZ));
        var svgRelative = RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum!, svgLocalZ);
        Assert.Equal(
            viewModel.WallPlateRow.RoofPlaneRelative,
            RoofRelativeElevationDatumRules.FormatMetres(svgRelative));
        Assert.Equal("+0.343", RoofRelativeElevationDatumRules.FormatMetres(svgRelative));
    }

    [Fact]
    public void HostCase_Dialog_SourceEave_BottomEdgeZero_ReportsPhysicalBottomAtReference()
    {
        var solved = Solve(pitchDegrees: 45d);
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
            AutomaticPurlinDialogMode.ReadOnlyPreview);
        ClearIntermediateRows(viewModel);
        Assert.True(viewModel.TryApplySelectedRafterDimensions(80d, 125d));
        viewModel.WallPlateEnabled = true;
        viewModel.RidgeEnabled = false;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.SourceEavePlane;
        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.HeightText = "140";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        viewModel.WallPlateRow.PlacementValueText = "0";
        viewModel.WallPlateRow.SelectedSeatingMode =
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        viewModel.WallPlateRow.SeatingDepthValueText = "25";

        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
        Assert.Equal("±0.000", viewModel.WallPlateRow.BottomRelative);
        Assert.Equal("+0.070", viewModel.WallPlateRow.CenterRelative);
        Assert.Equal("+0.140", viewModel.WallPlateRow.TopRelative);
        Assert.Equal("+0.343", viewModel.WallPlateRow.RoofPlaneRelative);

        Assert.True(viewModel.TryGetPreviewPlan(out var plan) && plan is not null);
        var wall = plan!.Items.First(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.Equal(0d, wall.ElevationProfile!.BottomLocalZMm, 6);
        Assert.Equal(140d, wall.ElevationProfile.TopLocalZMm, 6);
        Assert.Equal(342.5825214724774d, wall.PhysicalPlacement!.RafterUpperSurfaceLocalZMm, 6);

        var left = Assert.Single(
            viewModel.SectionPresentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                      member.Side == AutomaticPurlinSectionSide.Left);
        Assert.Contains(viewModel.WallPlateRow.BottomRelative, left.BottomText, StringComparison.Ordinal);
        Assert.Contains(viewModel.WallPlateRow.TopRelative, left.TopText, StringComparison.Ordinal);
        Assert.Equal(140d, left.HeightMm, 3);
        Assert.Equal(wall.ElevationProfile.CenterLocalZMm, wall.PhysicalPlacement.PurlinCenterLocalZMm, 6);
        Assert.NotNull(viewModel.SectionPresentation.ReferencePlane);
        Assert.Equal(
            viewModel.SectionPresentation.ReferencePlane!.LocalZMm,
            left.CenterZMm - left.HeightMm / 2d,
            6);
        Assert.Equal(
            viewModel.SectionPresentation.ReferencePlane.LocalZMm + 140d,
            left.CenterZMm + left.HeightMm / 2d,
            6);
    }

    [Fact]
    public void CurrentRoofPitchText_UsesGeometryPitchAndLocalizedFormat()
    {
        var solved = Solve(pitchDegrees: 45d);
        var en = new AutomaticPurlinDialogViewModel(
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
            AutomaticPurlinDialogMode.ReadOnlyPreview);
        Assert.Equal("Current roof pitch: 45.0°", en.CurrentRoofPitchText);

        var sk = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout: null,
            layoutExists: false,
            datum: null,
            datumExists: false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("sk-SK"),
            AutomaticPurlinDialogMode.ReadOnlyPreview);
        Assert.Equal("Aktuálny sklon strechy: 45,0°", sk.CurrentRoofPitchText);
        Assert.Contains(
            "{0}",
            UiStrings.GetString(
                "AutomaticPurlin_CurrentRoofPitchFormat",
                CultureInfo.GetCultureInfo("en")));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(100d)]
    public void WallPlate_TechnicalRoofPlane_MatchesPhysicalFormula_At45(double seatingPercent)
    {
        const double rafterHeightMm = 125d;
        var solved = Solve(45d);
        var sample = CreatePresentationForWallPlateSeating(solved, rafterHeightMm, seatingPercent);
        var item = sample.Plan.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                item,
                45d,
                rafterHeightMm,
                out var expected));
        Assert.Equal(
            item.ElevationProfile!.TopRelativeElevationMm +
            (item.WidthMm / 2d) * Math.Tan(Math.PI / 4d) +
            (rafterHeightMm - item.PhysicalPlacement!.SeatingDepthMm) / Math.Cos(Math.PI / 4d),
            expected,
            9);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(100d)]
    public void WallPlate_TechnicalRoofPlane_MatchesPhysicalFormula_At30(double seatingPercent)
    {
        const double rafterHeightMm = 125d;
        var solved = Solve(30d);
        var sample = CreatePresentationForWallPlateSeating(solved, rafterHeightMm, seatingPercent);
        var item = sample.Plan.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                item,
                30d,
                rafterHeightMm,
                out var expected));
        var pitchRad = 30d * Math.PI / 180d;
        Assert.Equal(
            item.ElevationProfile!.TopRelativeElevationMm +
            (item.WidthMm / 2d) * Math.Tan(pitchRad) +
            (rafterHeightMm - item.PhysicalPlacement!.SeatingDepthMm) / Math.Cos(pitchRad),
            expected,
            9);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(50d)]
    [InlineData(100d)]
    public void Intermediate_TechnicalRoofPlane_MatchesPhysicalFormula_At30And45(
        double seatingPercent)
    {
        const double rafterHeightMm = 125d;
        foreach (var pitch in new[] { 30d, 45d })
        {
            var solved = Solve(pitch);
            var sample = CreatePresentationForIntermediateSeating(solved, rafterHeightMm, seatingPercent);
            var item = sample.Plan.Items.First(i =>
                i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate);
            Assert.True(
                RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                    item,
                    pitch,
                    rafterHeightMm,
                    out var expected));
            var pitchRad = pitch * Math.PI / 180d;
            Assert.Equal(
                item.ElevationProfile!.TopRelativeElevationMm +
                (item.WidthMm / 2d) * Math.Tan(pitchRad) +
                (rafterHeightMm - item.PhysicalPlacement!.SeatingDepthMm) / Math.Cos(pitchRad),
                expected,
                9);
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(100d)]
    public void Ridge_TechnicalRoofPlane_MatchesMemberFormula_NotCenterlineHalfOverCos(
        double seatingPercent)
    {
        const double rafterHeightMm = 125d;
        foreach (var pitch in new[] { 30d, 45d })
        {
            var solved = Solve(pitch);
            var sample = CreatePresentationForRidgeSeating(solved, rafterHeightMm, seatingPercent);
            var sampleItem = sample.Plan.Items.First(i =>
                i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);

            Assert.True(
                RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                    sampleItem, pitch, rafterHeightMm, out var samplePlane));

            var pitchRad = pitch * Math.PI / 180d;
            var expected =
                sampleItem.ElevationProfile!.TopRelativeElevationMm +
                (sampleItem.WidthMm / 2d) * Math.Tan(pitchRad) +
                (rafterHeightMm - sampleItem.PhysicalPlacement!.SeatingDepthMm) /
                Math.Cos(pitchRad);
            Assert.Equal(expected, samplePlane, 9);
            // RoofPlane is the physical UPPER rafter face at the ridge axis — identical to
            // centroid + H/(2·cos(pitch)) at that station, and distinct from Top unless D=H.
            Assert.Equal(
                sampleItem.PhysicalPlacement.RafterUpperSurfaceRelativeElevationMm,
                samplePlane,
                9);
            Assert.Equal(
                sampleItem.PhysicalPlacement.RafterCenterRelativeElevationMm +
                rafterHeightMm / (2d * Math.Cos(pitchRad)),
                samplePlane,
                9);
            Assert.NotEqual(
                sampleItem.ElevationProfile.TopRelativeElevationMm,
                samplePlane,
                0);
        }
    }

    [Fact]
    public void HostRidge_WallPlateBottom_Reports2210_2320_2430_AndRoofPlane2643()
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
            AutomaticPurlinDialogMode.ReadOnlyPreview);
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
        viewModel.RidgeWidthText = "160";
        viewModel.RidgeHeightText = "220";
        viewModel.SelectedRidgeSeatingMode =
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        viewModel.RidgeSeatingDepthValueText = "25";

        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
        Assert.Equal("±0.000", viewModel.WallPlateRow.BottomRelative);
        Assert.Equal("+0.140", viewModel.WallPlateRow.TopRelative);
        Assert.Equal("+0.343", viewModel.WallPlateRow.RoofPlaneRelative);

        Assert.True(viewModel.TryGetPreviewPlan(out var plan) && plan is not null);
        var ridge = plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        var profile = ridge.ElevationProfile!;
        Assert.Equal(2210d, profile.BottomRelativeElevationMm, 6);
        Assert.Equal(2320d, profile.CenterRelativeElevationMm, 6);
        Assert.Equal(2430d, profile.TopRelativeElevationMm, 6);
        Assert.Equal(110d, profile.CenterRelativeElevationMm - profile.BottomRelativeElevationMm, 9);
        Assert.Equal(110d, profile.TopRelativeElevationMm - profile.CenterRelativeElevationMm, 9);

        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                ridge, 45d, 125d, out var roofPlaneMm));
        Assert.Equal(2642.5825214724774d, roofPlaneMm, 6);
        Assert.Equal("+2.210", viewModel.RidgeTechnicalSummary.BottomRelative);
        Assert.Equal("+2.320", viewModel.RidgeTechnicalSummary.CenterRelative);
        Assert.Equal("+2.430", viewModel.RidgeTechnicalSummary.TopRelative);
        Assert.Equal("+2.643", viewModel.RidgeTechnicalSummary.RoofPlaneRelative);
        Assert.NotEqual("+2.550", viewModel.RidgeTechnicalSummary.RoofPlaneRelative);
    }

    [Fact]
    public void DialogViewModel_WallPlateBottom_ReportsPhysicalUpperRoofPlane()
    {
        var solved = Solve(30d);
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
            AutomaticPurlinDialogMode.ReadOnlyPreview);
        ClearIntermediateRows(viewModel);
        Assert.True(viewModel.TryApplySelectedRafterDimensions(80d, 125d));
        viewModel.WallPlateEnabled = true;
        viewModel.RidgeEnabled = false;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText = "700";
        viewModel.WallPlateRow.SelectedSeatingMode =
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        viewModel.WallPlateRow.SeatingDepthValueText = "25";

        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage);
        Assert.Equal("±0.000", viewModel.WallPlateRow.BottomRelative);
        Assert.Equal("+0.070", viewModel.WallPlateRow.CenterRelative);
        Assert.Equal("+0.140", viewModel.WallPlateRow.TopRelative);

        Assert.True(viewModel.TryGetPreviewPlan(out var plan) && plan is not null);
        var item = plan!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.True(
            RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                item,
                solved.Geometry.PrimarySlopeDegrees,
                125d,
                out var expectedMm));
        Assert.Equal(
            RoofRelativeElevationDatumRules.FormatMetres(expectedMm),
            viewModel.WallPlateRow.RoofPlaneRelative);
        Assert.NotEqual("+0.000", viewModel.WallPlateRow.RoofPlaneRelative);
        Assert.NotEqual(viewModel.WallPlateRow.TopRelative, viewModel.WallPlateRow.RoofPlaneRelative);
    }

    private static void ClearIntermediateRows(AutomaticPurlinDialogViewModel viewModel)
    {
        while (viewModel.Rows.Count > 0)
        {
            viewModel.RemoveRow(viewModel.Rows[0]);
        }
    }

    private sealed record PresentationFixture(
        AutomaticPurlinSectionPresentation Presentation,
        RoofAutomaticPurlinPlan Plan,
        RoofRelativeElevationDatum Datum);

    private static PresentationFixture CreatePresentationForWallPlateSeating(
        SolvedFixture solved,
        double rafterHeightMm,
        double seatingPercent)
    {
        var requested = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            0d);
        var plan = CreateWallPlatePlan(
            solved,
            700d,
            seatingPercent,
            rafterHeightMm,
            140d,
            140d,
            requested);
        var bottomLocalZ = plan.Items
            .First(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ElevationProfile!.BottomLocalZMm;
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            bottomLocalZ);
        return new PresentationFixture(
            CreatePresentation(solved, plan, rafterHeightMm, datum),
            plan,
            datum);
    }

    private static PresentationFixture CreatePresentationForIntermediateSeating(
        SolvedFixture solved,
        double rafterHeightMm,
        double seatingPercent)
    {
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        var plan = CreatePlan(
            solved,
            new RoofAutomaticPurlinLayout(false,
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
            ]),
            rafterHeightMm,
            datum);
        return new PresentationFixture(
            CreatePresentation(solved, plan, rafterHeightMm, datum),
            plan,
            datum);
    }

    private static PresentationFixture CreatePresentationForRidgeSeating(
        SolvedFixture solved,
        double rafterHeightMm,
        double seatingPercent)
    {
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        var plan = CreatePlan(
            solved,
            new RoofAutomaticPurlinLayout(true, Array.Empty<RoofAutomaticPurlinLayoutItem>())
            {
                RidgeWidthMm = 160d,
                RidgeHeightMm = 220d,
                RidgeSeatingDepth = new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
            },
            rafterHeightMm,
            datum);
        return new PresentationFixture(
            CreatePresentation(solved, plan, rafterHeightMm, datum),
            plan,
            datum);
    }

    private static RoofAutomaticPurlinPlan CreateWallPlatePlan(
        SolvedFixture solved,
        double planDistanceFromEaveMm,
        double seatingPercent,
        double rafterHeightMm,
        double wallPlateWidthMm,
        double wallPlateHeightMm,
        RoofRelativeElevationDatum? datum = null)
    {
        datum ??= new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        return CreatePlan(
            solved,
            new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
            {
                WallPlateEnabled = true,
                WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                    RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    planDistanceFromEaveMm,
                    SeatingDepth: new(
                        RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                        seatingPercent),
                    WidthMm: wallPlateWidthMm,
                    HeightMm: wallPlateHeightMm),
            },
            rafterHeightMm,
            datum,
            wallPlateWidthMm,
            wallPlateHeightMm);
    }

    private static RoofAutomaticPurlinPlan CreatePlan(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout,
        double rafterHeightMm,
        RoofRelativeElevationDatum datum,
        double? wallPlateWidthMm = null,
        double? wallPlateHeightMm = null)
    {
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                datum,
                TimberElementDefaults.For(TimberElementType.Purlin).HeightMm,
                rafterHeightMm)
            {
                PurlinWidthMm = TimberElementDefaults.For(TimberElementType.Purlin).WidthMm,
                WallPlatesEnabled = layout.WallPlateEnabled,
                WallPlateWidthMm = wallPlateWidthMm ??
                    TimberElementDefaults.For(TimberElementType.WallPlate).WidthMm,
                WallPlateHeightMm = wallPlateHeightMm ??
                    TimberElementDefaults.For(TimberElementType.WallPlate).HeightMm,
            });
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation(
        SolvedFixture solved,
        RoofAutomaticPurlinPlan plan,
        double rafterHeightMm,
        RoofRelativeElevationDatum? datum)
    {
        var culture = CultureInfo.GetCultureInfo("en");
        return AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            rafterHeightMm,
            TimberElementDefaults.For(TimberElementType.Rafter).WidthMm,
            datum);
    }

    private static SolvedFixture Solve(double pitchDegrees = 30d)
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
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}

