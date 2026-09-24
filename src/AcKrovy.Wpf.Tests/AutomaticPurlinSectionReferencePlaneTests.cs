using System.Globalization;
using System.Threading;
using System.Windows;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// Focused presentation/layout contracts for the active architectural reference
/// plane drawn in the Automatic-Purlin roof-section schematic.
/// </summary>
[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinSectionReferencePlaneTests
{
    [Fact]
    public void SourceEavePlane_ShowsHorizontalGuideAtLocalZeroWithZeroLabel()
    {
        var culture = CultureInfo.GetCultureInfo("sk-SK");
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        // Without schematic rafters, falls back to architectural LocalZ.
        var plane = AutomaticPurlinSectionPresentation.TryCreateReferencePlane(datum, culture);

        Assert.NotNull(plane);
        Assert.Equal(RoofRelativeElevationReferenceKind.SourceEavePlane, plane!.Kind);
        Assert.Equal(0d, plane.LocalZMm);
        Assert.Equal("±0,000", plane.LabelText);
    }

    [Fact]
    public void ExplicitLocalPlane_UsesResolvedLocalZAndRelativeLabel()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            500d,
            1250d);
        var plane = AutomaticPurlinSectionPresentation.TryCreateReferencePlane(datum, culture);

        Assert.NotNull(plane);
        Assert.Equal(RoofRelativeElevationReferenceKind.ExplicitLocalPlane, plane!.Kind);
        Assert.Equal(1250d, plane.LocalZMm);
        Assert.Equal("+0.500", plane.LabelText);
    }

    [Fact]
    public void WallPlateBottom_WithoutSchematicMembers_ProducesNoPlane()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            -250d,
            140d);
        // WallPlateBottom requires schematic wall-plate rectangles; bare datum is not enough.
        Assert.Null(AutomaticPurlinSectionPresentation.TryCreateReferencePlane(datum, culture));
    }

    [Fact]
    public void InvalidOrMissingDatum_ProducesNoReferencePlane()
    {
        Assert.Null(AutomaticPurlinSectionPresentation.TryCreateReferencePlane(
            null,
            CultureInfo.InvariantCulture));
        Assert.Null(AutomaticPurlinSectionPresentation.TryCreateReferencePlane(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                double.NaN,
                0d),
            CultureInfo.InvariantCulture));
        Assert.Null(AutomaticPurlinSectionPresentation.TryCreateReferencePlane(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                double.PositiveInfinity),
            CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Create_SourceEavePlane_AlignsToVisualPlumbCutTopOuterTip()
    {
        var solved = Solve();
        var culture = CultureInfo.GetCultureInfo("en-US");
        var plan = Plan(solved);
        var presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));

        Assert.NotNull(presentation.ReferencePlane);
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                presentation,
                out var visualTopZ));
        Assert.Equal(visualTopZ, presentation.ReferencePlane!.LocalZMm, 6);
        Assert.Equal("±0.000", presentation.ReferencePlane.LabelText);

        // Physical upper face is the CenterLine; drawn SVG upper tip maps onto it.
        var left = Assert.Single(
            presentation.Rafters,
            rafter => rafter.Side == AutomaticPurlinSectionSide.Left);
        var upperEaveZ = Math.Min(left.CenterLine.Z1Mm, left.CenterLine.Z2Mm);
        Assert.Equal(upperEaveZ, visualTopZ, 3);
    }

    [Fact]
    public void Create_ExplicitLocalPlaneZero_MatchesSourceEaveVisualHeight()
    {
        var solved = Solve();
        var culture = CultureInfo.GetCultureInfo("en-US");
        var plan = Plan(solved);
        var sourceEave = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));
        var explicitZero = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                0d));

        Assert.NotNull(sourceEave.ReferencePlane);
        Assert.NotNull(explicitZero.ReferencePlane);
        Assert.Equal(
            sourceEave.ReferencePlane!.LocalZMm,
            explicitZero.ReferencePlane!.LocalZMm,
            6);
        Assert.Equal("±0.000", explicitZero.ReferencePlane.LabelText);
    }

    [Fact]
    public void Create_ExplicitLocalPlaneOffset_KeepsTrueLocalZButClampsVisualExtents()
    {
        var solved = Solve();
        var culture = CultureInfo.GetCultureInfo("en-US");
        var plan = Plan(solved);
        var baseline = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                0d));
        var raised = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                5000d));
        var lowered = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                -3500d));

        Assert.NotNull(baseline.ReferencePlane);
        Assert.NotNull(raised.ReferencePlane);
        Assert.NotNull(lowered.ReferencePlane);
        Assert.Equal(
            baseline.ReferencePlane!.LocalZMm + 5000d,
            raised.ReferencePlane!.LocalZMm,
            6);
        Assert.Equal(
            baseline.ReferencePlane.LocalZMm - 3500d,
            lowered.ReferencePlane!.LocalZMm,
            6);

        Assert.True(
            AutomaticPurlinSectionPresentation.TryResolveReferencePlaneVisualClampRange(
                raised.Members,
                raised.Rafters,
                raised,
                out var minVisual,
                out var maxVisual));
        Assert.Equal(maxVisual, raised.ReferencePlane.VisualLocalZMm, 6);
        Assert.Equal(minVisual, lowered.ReferencePlane!.VisualLocalZMm, 6);
        Assert.True(raised.ReferencePlane.LocalZMm > raised.ReferencePlane.VisualLocalZMm);
        Assert.True(lowered.ReferencePlane.LocalZMm < lowered.ReferencePlane.VisualLocalZMm);
        // Zoom uses visual clamp, not the extreme true LocalZ.
        Assert.Equal(raised.ReferencePlane.VisualLocalZMm, raised.MaxZMm, 6);
        Assert.Equal(lowered.ReferencePlane.VisualLocalZMm, lowered.MinZMm, 6);
        Assert.Equal(
            AutomaticPurlinSectionPresentation.ReferencePlaneVisualMaxAboveRidgeMm,
            1200d);
        Assert.Equal(
            AutomaticPurlinSectionPresentation.ReferencePlaneVisualMinBelowEaveMm,
            1200d);
    }

    [Fact]
    public void Create_IncludesReferencePlaneInExtentsAndLiveRelativeLabelUpdates()
    {
        var solved = Solve();
        var culture = CultureInfo.GetCultureInfo("en-US");
        var plan = Plan(solved);
        var first = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                0d,
                0d));
        Assert.NotNull(first.ReferencePlane);
        Assert.Equal("±0.000", first.ReferencePlane!.LabelText);
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                first,
                out var tipZ));
        Assert.Equal(tipZ, first.ReferencePlane.LocalZMm, 6);

        var updated = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            160d,
            80d,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                750d,
                0d));
        Assert.NotNull(updated.ReferencePlane);
        Assert.Equal("+0.750", updated.ReferencePlane!.LabelText);
        // Relative label changes; schematic anchor stays on the visual eave top tip.
        Assert.Equal(tipZ, updated.ReferencePlane.LocalZMm, 6);
        Assert.Equal(first.ReferencePlane.Kind, updated.ReferencePlane.Kind);
    }

    [Fact]
    public void ViewModel_LiveUpdatesReferencePlaneWhenRelativeElevationChanges()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var viewModel = CreateViewModel(culture);
        Assert.NotNull(viewModel.SectionPresentation.ReferencePlane);
        Assert.Equal(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            viewModel.SectionPresentation.ReferencePlane!.Kind);
        Assert.Equal("±0.000", viewModel.SectionPresentation.ReferencePlane.LabelText);
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                viewModel.SectionPresentation,
                out var tipZ));
        Assert.Equal(tipZ, viewModel.SectionPresentation.ReferencePlane.LocalZMm, 6);
        Assert.False(viewModel.IsLocalReferencePositionVisible);

        viewModel.RelativeReferenceText = "+0.500";
        Assert.Equal("+0.500", viewModel.SectionPresentation.ReferencePlane!.LabelText);
        Assert.Equal(tipZ, viewModel.SectionPresentation.ReferencePlane.LocalZMm, 6);

        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        viewModel.ReferenceLocalZText = "0";
        viewModel.RelativeReferenceText = "±0.000";
        Assert.Equal(tipZ, viewModel.SectionPresentation.ReferencePlane!.LocalZMm, 6);
        Assert.True(viewModel.IsLocalReferencePositionVisible);

        viewModel.ReferenceLocalZText = "900";
        viewModel.RelativeReferenceText = "-0.250";
        Assert.Equal(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            viewModel.SectionPresentation.ReferencePlane!.Kind);
        Assert.Equal("-0.250", viewModel.SectionPresentation.ReferencePlane.LabelText);
        Assert.Equal(tipZ + 900d, viewModel.SectionPresentation.ReferencePlane.LocalZMm, 3);
        Assert.True(viewModel.IsLocalReferencePositionVisible);

        viewModel.WallPlateEnabled = true;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        Assert.False(viewModel.IsLocalReferencePositionVisible);
    }

    [Fact]
    public void ViewModel_WallPlateBottomReference_AlignsToSchematicWallPlateBottom()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var viewModel = CreateViewModel(culture);
        viewModel.WallPlateEnabled = true;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        viewModel.RelativeReferenceText = "±0.000";

        Assert.True(viewModel.TryCreateDraft(out _, out var datum));
        Assert.NotNull(datum);
        Assert.NotNull(viewModel.SectionPresentation.ReferencePlane);
        Assert.Equal(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            viewModel.SectionPresentation.ReferencePlane!.Kind);

        var plates = viewModel.SectionPresentation.Members
            .Where(member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ToArray();
        Assert.NotEmpty(plates);
        Assert.True(
            AutomaticPurlinSectionPresentation.TryResolveSchematicWallPlateBottomLocalZMm(
                viewModel.SectionPresentation.Members,
                out var bottomZ));
        Assert.Equal(bottomZ, viewModel.SectionPresentation.ReferencePlane.LocalZMm, 6);
        var plateBottoms = plates
            .Select(plate => plate.CenterZMm - plate.HeightMm / 2d)
            .ToArray();
        Assert.Equal(plateBottoms.Average(), bottomZ, 6);

        Assert.Equal(
            AutomaticPurlinSectionPresentation.FormatReferenceElevationLabel(
                datum!.ReferenceRelativeElevationMm,
                culture),
            viewModel.SectionPresentation.ReferencePlane.LabelText);
    }

    [Fact]
    public void ViewModel_WallPlateBottomWithoutWallPlate_HasNoReferencePlane()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var viewModel = CreateViewModel(culture);
        viewModel.WallPlateEnabled = false;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;

        Assert.Null(viewModel.SectionPresentation.ReferencePlane);
    }

    [Fact]
    public void SectionView_DrawsReferencePlaneWithoutThrowingOnResize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo("en-US");
                var viewModel = CreateViewModel(culture);
                Assert.NotNull(viewModel.SectionPresentation.ReferencePlane);

                var view = new AutomaticPurlinRoofSectionView
                {
                    Width = 420,
                    Height = 320,
                    Presentation = viewModel.SectionPresentation,
                };
                view.Measure(new Size(420, 320));
                view.Arrange(new Rect(0, 0, 420, 320));
                view.UpdateLayout();
                view.InvalidateVisual();

                view.Width = 280;
                view.Height = 220;
                view.Measure(new Size(280, 220));
                view.Arrange(new Rect(0, 0, 280, 220));
                view.UpdateLayout();
                view.InvalidateVisual();

                Assert.NotNull(view.Presentation?.ReferencePlane);
                Assert.Equal("±0.000", view.Presentation!.ReferencePlane!.LabelText);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure);
    }

    private static AutomaticPurlinDialogViewModel CreateViewModel(CultureInfo culture)
    {
        AppLanguageService.Apply(culture.TwoLetterISOLanguageName);
        var solved = Solve();
        return new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout: null,
            layoutExists: false,
            datum: null,
            datumExists: false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            culture,
            AutomaticPurlinDialogMode.ProductionEdit);
    }

    private static SolvedFixture Solve()
    {
        var points = new[]
        {
            new RoofPoint2D(0, 0),
            new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000),
            new RoofPoint2D(0, 6000),
        };
        var input = new RoofFootprintInput(points, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometryResult = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(geometryResult.IsValid, geometryResult.Error.ToString());
        return new SolvedFixture(
            Assert.IsType<HipRoofGeometry>(geometryResult.Geometry),
            provenance);
    }

    private static RoofAutomaticPurlinPlan Plan(SolvedFixture solved)
    {
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            RoofAutomaticPurlinLayout.Empty,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.SourceEavePlane,
                    0d,
                    0d),
                220d,
                160d));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
