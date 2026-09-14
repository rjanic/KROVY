using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinDialogViewModelTests
{
    private const string LayoutIdA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LayoutIdB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void ExistingLayoutAndDatum_LoadExactlyWithoutChangingStableRows()
    {
        var solved = Solve(RectanglePoints());
        var ridge = Assert.Single(Ridges(solved));
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
            35d);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(LayoutIdA, false,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference, 500d),
            new(LayoutIdB, true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge, 1200d,
                ridge.StructuralIdentity, seating),
        ]);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            3580d,
            125d);

        var viewModel = CreateViewModel(solved, layout, true, datum, true);

        Assert.True(viewModel.LayoutExists);
        Assert.True(viewModel.DatumExists);
        Assert.True(viewModel.RidgeEnabled);
        Assert.Equal(new[] { LayoutIdA, LayoutIdB },
            viewModel.Rows.Select(row => row.LayoutItemId));
        Assert.Equal("+3.580", viewModel.RelativeReferenceText);
        Assert.Equal(RoofRelativeElevationReferenceKind.WallPlateBottom, viewModel.ReferenceKind);
        Assert.True(viewModel.TryCreateDraft(out var draftLayout, out var draftDatum));
        Assert.True(draftLayout!.RidgeEnabled);
        Assert.Equal(layout.IntermediateItems, draftLayout.IntermediateItems);
        Assert.Equal(datum, draftDatum);
    }

    [Fact]
    public void MissingLayoutAndDatum_StartAsExplicitUnpersistedEmptyDraft()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);

        Assert.False(viewModel.LayoutExists);
        Assert.False(viewModel.DatumExists);
        Assert.False(viewModel.RidgeEnabled);
        Assert.Empty(viewModel.Rows);
        Assert.Contains("not set", viewModel.DatumPersistenceStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("±0.000", viewModel.RelativeReferenceText);
        Assert.True(viewModel.TryCreateDraft(out var layout, out var datum));
        Assert.False(layout!.RidgeEnabled);
        Assert.Empty(layout.IntermediateItems);
        Assert.Equal(RoofRelativeElevationReferenceKind.ExplicitLocalPlane, datum!.ReferenceKind);
        Assert.Equal(0d, datum.ReferenceRelativeElevationMm);
        Assert.Equal(0d, datum.ReferenceLocalZMm);
    }

    [Fact]
    public void MissingDatum_AllowsInMemoryReferenceToEnableFiveItemPreview()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);

        Assert.False(viewModel.DatumExists);
        viewModel.RelativeReferenceText = "+3.580";
        viewModel.RidgeEnabled = true;
        var row = viewModel.AddRow();

        Assert.True(viewModel.CanPreview);
        Assert.Equal("+4.080", row.BottomRelative);
        Assert.Equal("+4.190", row.CenterRelative);
        Assert.Equal("+4.300", row.TopRelative);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.Equal(5, plan!.Items.Count);
        Assert.All(
            plan.Items.Where(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate),
            item => Assert.Equal(610d, item.Segment3D.Start.Z, 9));
    }

    [Fact]
    public void BottomEdgeDraft_ExposesCoreDerivedElevationsAndFivePreviewSegments()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(
            solved,
            RoofAutomaticPurlinLayout.Empty,
            false,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                3580d,
                0d),
            true);

        viewModel.RidgeEnabled = true;
        var row = viewModel.AddRow();

        Assert.Equal("500", row.PlacementValueText);
        Assert.Equal("+4.080", row.BottomRelative);
        Assert.Equal("+4.190", row.CenterRelative);
        Assert.Equal("+4.300", row.TopRelative);
        Assert.Equal("160 × 220 mm", viewModel.PurlinSectionText);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.Equal(5, plan!.Items.Count);
        Assert.Equal(4, plan.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate));
        Assert.All(plan.Items.Where(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate), item =>
            Assert.Equal(610d, item.Segment3D.Start.Z, 9));

    }

    [Fact]
    public void ModeSwitch_UpdatesDynamicLabelAndAutoSelectsSingleHorizontalRidge()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        var row = viewModel.AddRow();

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        Assert.Equal(
            UiStrings.GetString("AutomaticPurlin_ValueEaveDistance", CultureInfo.GetCultureInfo("en")),
            row.ValueLabel);
        Assert.Null(row.SelectedRidgeReference);
        Assert.True(row.IsSeatingApplicable);
        Assert.Equal(RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            row.SelectedSeatingMode);
        Assert.Equal("25", row.SeatingDepthValueText);
        Assert.Equal("%", row.SeatingUnit);
        Assert.Contains("25", row.SeatingPolicyText, StringComparison.Ordinal);

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
        Assert.Single(row.RidgeReferences);
        Assert.NotNull(row.SelectedRidgeReference);
        Assert.False(row.IsRidgeReferenceVisible);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(
            row.SelectedRidgeReference!.Key,
            Assert.Single(layout!.IntermediateItems).ReferenceRidgeKey);
    }

    [Fact]
    public void DistanceMode_DefaultSeatingProducesPhysicalDerivedValuesAndPreservesIdentity()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        var row = viewModel.AddRow();
        var layoutItemId = row.LayoutItemId;

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;

        Assert.True(viewModel.CanPreview);
        Assert.Equal(layoutItemId, row.LayoutItemId);
        Assert.Equal("500 mm", row.PlanPosition);
        Assert.NotEqual("—", row.RoofPlaneRelative);
        Assert.Equal("40 mm", row.SeatingDepthDerived);
        Assert.NotEqual("—", row.BottomRelative);
        Assert.NotEqual("—", row.CenterRelative);
        Assert.NotEqual("—", row.TopRelative);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        var draftItem = Assert.Single(layout!.IntermediateItems);
        Assert.Equal(layoutItemId, draftItem.LayoutItemId);
        Assert.Equal(
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                25d),
            draftItem.SeatingDepth);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        var intermediate = plan!.Items.Where(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate).ToArray();
        Assert.Equal(4, intermediate.Length);
        Assert.All(intermediate, item => Assert.NotNull(item.PhysicalPlacement));
        Assert.All(intermediate, item => Assert.Equal(
            intermediate[0].Segment3D.Start.Z,
            item.Segment3D.Start.Z,
            9));
    }

    [Fact]
    public void AbsoluteSeating_IsStrictlyValidatedAgainstRafterHeight()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        var row = viewModel.AddRow();
        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        row.SelectedSeatingMode = RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm;
        row.SeatingDepthValueText = "40";

        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
                40d),
            Assert.Single(layout!.IntermediateItems).SeatingDepth);
        Assert.Equal("mm", row.SeatingUnit);
        Assert.True(viewModel.CanPreview);

        row.SeatingDepthValueText = "160";

        Assert.False(viewModel.CanPreview);
        Assert.False(viewModel.TryCreateDraft(out _, out _));
        Assert.NotEmpty(viewModel.ValidationMessage);
    }

    [Fact]
    public void BottomEdgeMode_RemainsAuthoritativeAndOmitsSeating()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        var row = viewModel.AddRow();

        Assert.False(row.IsSeatingApplicable);
        Assert.Equal("—", row.PlanPosition);
        Assert.Equal("—", row.RoofPlaneRelative);
        Assert.Equal("—", row.SeatingDepthDerived);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Null(Assert.Single(layout!.IntermediateItems).SeatingDepth);
        Assert.Equal("+0.500", row.BottomRelative);
    }

    [Fact]
    public void MultipleHorizontalRidgesRequireFriendlyExplicitSelection()
    {
        var solved = Solve(
        [
            new(0, 0), new(8000, 0), new(8000, 3000),
            new(3000, 3000), new(3000, 8000), new(0, 8000),
        ]);
        Assert.True(Ridges(solved).Count > 1);
        var viewModel = CreateViewModel(solved, null, false, null, false);
        var row = viewModel.AddRow();

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

        Assert.True(row.IsRidgeReferenceVisible);
        Assert.Null(row.SelectedRidgeReference);
        Assert.All(row.RidgeReferences, option =>
        {
            Assert.StartsWith("Ridge ", option.Label, StringComparison.Ordinal);
            Assert.DoesNotContain("|", option.Label, StringComparison.Ordinal);
        });
        Assert.False(viewModel.CanPreview);

        row.SelectedRidgeReference = row.RidgeReferences[0];

        Assert.True(viewModel.CanPreview);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(
            row.RidgeReferences[0].Key,
            Assert.Single(layout!.IntermediateItems).ReferenceRidgeKey);
    }

    [Fact]
    public void AddRemovePreservesCanonicalDraftIdentityAndInvalidValueClearsPreview()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        var first = viewModel.AddRow();
        var second = viewModel.AddRow();

        Assert.Equal("Intermediate purlin 1", first.DisplayName);
        Assert.Equal("Intermediate purlin 2", second.DisplayName);
        Assert.Matches("^[0-9a-f]{32}$", first.LayoutItemId);
        Assert.Matches("^[0-9a-f]{32}$", second.LayoutItemId);
        Assert.NotEqual(first.LayoutItemId, second.LayoutItemId);
        Assert.True(viewModel.TryGetPreviewPlan(out _));

        first.PlacementValueText = "invalid";
        Assert.False(viewModel.TryGetPreviewPlan(out _));
        Assert.False(viewModel.CanPreview);
        Assert.NotEmpty(viewModel.ValidationMessage);

        viewModel.RemoveRow(first);
        Assert.Single(viewModel.Rows);
        Assert.Same(second, viewModel.Rows[0]);
        Assert.Equal("Intermediate purlin 1", second.DisplayName);
        Assert.True(viewModel.TryGetPreviewPlan(out _));
    }

    [Fact]
    public void AdvancedDatum_IsCollapsedAndLocalZAppearsOnlyWhenExpanded()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(
                Solve(RectanglePoints()),
                null,
                false,
                null,
                false,
                AppLanguageService.CurrentUiCulture);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            Assert.False(window.AdvancedDatumExpander.IsExpanded);
            Assert.False(window.LocalReferencePanel.IsVisible);
            Assert.Equal(
                UiStrings.GetString("AutomaticPurlin_ReferenceLocalZHelp"),
                window.LocalReferencePanel.ToolTip);

            window.AdvancedDatumExpander.IsExpanded = true;
            window.UpdateLayout();

            Assert.True(window.LocalReferencePanel.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public void RowPlacementMode_SwitchesContextualSeatingAndExpectedDerivedValues()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(
                Solve(RectanglePoints()),
                null,
                false,
                null,
                false,
                AppLanguageService.CurrentUiCulture);
            var row = viewModel.AddRow();
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            var seatingControls = NamedDescendant<FrameworkElement>(window, "SeatingControlsPanel");
            var heightStatus = NamedDescendant<FrameworkElement>(window, "HeightModeSeatingStatus");
            Assert.False(seatingControls.IsVisible);
            Assert.True(heightStatus.IsVisible);
            Assert.Contains(
                UiStrings.GetString("AutomaticPurlin_SeatingNotApplicable"),
                Assert.IsType<TextBlock>(heightStatus).Text,
                StringComparison.OrdinalIgnoreCase);

            row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
            row.PlacementValueText = "1000";
            window.UpdateLayout();

            Assert.True(seatingControls.IsVisible);
            Assert.False(heightStatus.IsVisible);
            Assert.Equal(RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                row.SelectedSeatingMode);
            Assert.Equal("25", row.SeatingDepthValueText);
            Assert.Equal("40 mm", row.SeatingDepthDerived);
            Assert.Equal("+0.577", row.RoofPlaneRelative);
            Assert.Equal("+0.323", row.BottomRelative);
            Assert.Equal("+0.433", row.CenterRelative);
            Assert.Equal("+0.543", row.TopRelative);

            row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
            window.UpdateLayout();

            Assert.True(seatingControls.IsVisible);
            Assert.False(heightStatus.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public void FiveRows_ScrollInsideBoundedBodyWhileFooterStaysVisible()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
            for (var index = 0; index < 5; index++)
            {
                viewModel.AddRow();
            }

            var window = CreateOffscreenWindow(viewModel);
            window.Height = window.MinHeight;
            window.Show();
            window.UpdateLayout();

            Assert.True(window.DialogScrollViewer.ScrollableHeight > 0d);
            Assert.Equal(0d, window.DialogScrollViewer.ScrollableWidth);
            Assert.Equal(ScrollBarVisibility.Disabled,
                window.DialogScrollViewer.HorizontalScrollBarVisibility);
            Assert.True(window.FooterPanel.IsVisible);
            Assert.False(IsDescendantOf(window.FooterPanel, window.DialogScrollViewer));
            Assert.True(IsDescendantOf(window.IntermediateRowsControl, window.DialogScrollViewer));
            Assert.Contains(window.CancelButton, Descendants<Button>(window.FooterPanel));
            Assert.Contains(window.PreviewButton, Descendants<Button>(window.FooterPanel));
            window.Close();
        });
    }

    [Fact]
    public void AddAndCompactRemoveButtons_PreserveRowBindings()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.AddRowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            var row = Assert.Single(viewModel.Rows);
            var remove = Descendants<Button>(window.IntermediateRowsControl)
                .Single(button => ReferenceEquals(button.CommandParameter, row));
            Assert.Equal(0d, remove.MinWidth);

            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Empty(viewModel.Rows);
            window.Close();
        });
    }

    [Fact]
    public void MissingBoundaryIdentityFailsPreviewWithoutCreatingFallbackIdentity()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            null,
            RoofAutomaticPurlinLayout.Empty,
            false,
            null,
            false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en"));

        viewModel.RidgeEnabled = true;

        Assert.False(viewModel.CanPreview);
        Assert.False(viewModel.TryGetPreviewPlan(out _));
        Assert.Contains("boundary identity", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowLoadsInEveryLanguageAndShowsApplyOnlyInProductionMode()
    {
        RunSta(() =>
        {
            var solved = Solve(RectanglePoints());
            foreach (var language in new[] { "sk", "cs", "en", "de", "pl", "fr" })
            {
                AppLanguageService.Apply(language);
                var viewModel = CreateViewModel(
                    solved,
                    null,
                    false,
                    null,
                    false,
                    AppLanguageService.CurrentUiCulture);
                var window = new AutomaticPurlinDialogWindow(viewModel, SettingsTheme.Light)
                {
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
                window.UpdateLayout();

                Assert.NotEqual("AutomaticPurlin_Title", window.Title);
                Assert.Same(window.FindResource("SettingsPrimaryButtonStyle"), window.PreviewButton.Style);
                Assert.Same(window.FindResource("SettingsSecondaryButtonStyle"), window.CancelButton.Style);
                Assert.Contains(window.PreviewButton, Descendants<Button>(window));
                Assert.Contains(window.CancelButton, Descendants<Button>(window));
                Assert.NotNull(window.ApplyButton);
                Assert.False(window.ApplyButton.IsVisible);
                Assert.Equal(ScrollBarVisibility.Auto,
                    window.DialogScrollViewer.VerticalScrollBarVisibility);
                window.Close();

                var productionViewModel = CreateViewModel(
                    solved,
                    null,
                    false,
                    null,
                    false,
                    AppLanguageService.CurrentUiCulture,
                    AutomaticPurlinDialogMode.ProductionEdit);
                productionViewModel.RidgeEnabled = true;
                var productionWindow = CreateOffscreenWindow(productionViewModel);
                productionWindow.Show();
                productionWindow.UpdateLayout();

                Assert.True(productionWindow.ApplyButton.IsVisible);
                Assert.True(productionWindow.ApplyButton.IsEnabled);
                Assert.Same(
                    productionWindow.FindResource("SettingsPrimaryButtonStyle"),
                    productionWindow.ApplyButton.Style.BasedOn);
                productionWindow.Close();
            }
        });
    }

    [Fact]
    public void ProductionApplyRequiresValidDesiredOrExistingMembersAndPreservesDraftOnFailure()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(
            solved,
            null,
            false,
            null,
            false,
            mode: AutomaticPurlinDialogMode.ProductionEdit);

        Assert.True(viewModel.IsProductionEdit);
        Assert.True(viewModel.CanPreview);
        Assert.False(viewModel.CanApply);

        viewModel.RidgeEnabled = true;

        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryBeginApply(out var layout, out var datum, out var preview));
        Assert.NotNull(layout);
        Assert.NotNull(datum);
        Assert.NotEmpty(preview!.Items);
        Assert.False(viewModel.CanApply);

        viewModel.CompleteApplyFailure();

        Assert.True(viewModel.RidgeEnabled);
        Assert.True(viewModel.CanApply);
        Assert.Contains("could not", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.RelativeReferenceText = "invalid";

        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.TryBeginApply(out _, out _, out _));
    }

    [Fact]
    public void ExistingGeneratedMembersAllowExplicitEmptyApplyForRemoval()
    {
        var viewModel = CreateViewModel(
            Solve(RectanglePoints()),
            RoofAutomaticPurlinLayout.Empty,
            true,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                0d),
            true,
            mode: AutomaticPurlinDialogMode.ProductionEdit,
            existingAutomaticPurlinCount: 4);

        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryBeginApply(out var layout, out _, out var preview));
        Assert.False(layout!.RidgeEnabled);
        Assert.Empty(layout.IntermediateItems);
        Assert.Empty(preview!.Items);
    }

    [Fact]
    public void CompactSchematicTracksSelectedRowAndPlacementCues()
    {
        var viewModel = CreateViewModel(
            Solve(RectanglePoints()),
            null,
            false,
            null,
            false);
        var first = viewModel.AddRow();
        var second = viewModel.AddRow();

        Assert.Same(second, viewModel.SelectedRow);
        Assert.False(first.IsSchematicSelected);
        Assert.True(second.IsSchematicSelected);
        Assert.True(viewModel.ShowVerticalHeightCue);
        Assert.False(viewModel.ShowSeatingCue);
        Assert.True(viewModel.SchematicAnyIntermediateEnabled);
        Assert.True(viewModel.SchematicIntermediateEmphasized);
        Assert.False(viewModel.SchematicRidgeActive);

        viewModel.SelectedRow = first;
        first.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;

        Assert.True(first.IsSchematicSelected);
        Assert.False(second.IsSchematicSelected);
        Assert.True(viewModel.ShowEaveDistanceCue);
        Assert.True(viewModel.ShowSeatingCue);

        first.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

        Assert.True(viewModel.ShowRidgeDistanceCue);
        Assert.False(viewModel.ShowEaveDistanceCue);
    }

    [Fact]
    public void SchematicHighlightsTrackRidgeFocusAndEnabledIntermediateRows()
    {
        var viewModel = CreateViewModel(
            Solve(RectanglePoints()),
            null,
            false,
            null,
            false);

        Assert.False(viewModel.SchematicRidgeActive);
        Assert.False(viewModel.SchematicRidgeEmphasized);
        Assert.False(viewModel.SchematicAnyIntermediateEnabled);
        Assert.False(viewModel.SchematicIntermediateEmphasized);
        Assert.Contains("automatic-purlin-roof-section.png", viewModel.SchematicImagePackUri);

        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.SchematicRidgeActive);
        Assert.False(viewModel.SchematicRidgeEmphasized);

        viewModel.SetRidgeInteractionActive(true);
        Assert.True(viewModel.SchematicRidgeEmphasized);

        var row = viewModel.AddRow();
        row.Enabled = true;
        Assert.True(viewModel.SchematicAnyIntermediateEnabled);
        Assert.True(viewModel.SchematicIntermediateEmphasized);

        viewModel.SetIntermediateInteractionActive(true);
        Assert.False(viewModel.SchematicRidgeEmphasized);
        Assert.True(viewModel.SchematicIntermediateEmphasized);

        row.Enabled = false;
        Assert.False(viewModel.SchematicAnyIntermediateEnabled);
        Assert.False(viewModel.SchematicIntermediateEmphasized);
        Assert.True(viewModel.SchematicRidgeActive);
    }

    [Fact]
    public void ProductionApplyUsesTheExactCurrentPreviewPlan()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(
            solved,
            null,
            false,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                3580d,
                0d),
            true,
            mode: AutomaticPurlinDialogMode.ProductionEdit);
        viewModel.RidgeEnabled = true;
        var row = viewModel.AddRow();
        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        row.PlacementValueText = "1000";

        Assert.True(viewModel.TryBeginApply(out var layout, out var datum, out var preview));
        var direct = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout!,
            new RoofAutomaticPurlinPlanningInput(
                datum!,
                TimberElementDefaults.For(TimberElementType.Purlin).HeightMm,
                TimberElementDefaults.For(TimberElementType.Rafter).HeightMm));

        Assert.True(direct.IsValid);
        Assert.Equal(direct.Plan!.Items, preview!.Items);
    }

    private static AutomaticPurlinDialogViewModel CreateViewModel(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout? layout,
        bool layoutExists,
        RoofRelativeElevationDatum? datum,
        bool datumExists,
        CultureInfo? culture = null,
        AutomaticPurlinDialogMode mode = AutomaticPurlinDialogMode.ReadOnlyPreview,
        int existingAutomaticPurlinCount = 0) => new(
            solved.Geometry,
            solved.Provenance,
            layout,
            layoutExists,
            datum,
            datumExists,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.Rafter),
        culture ?? CultureInfo.GetCultureInfo("en"),
        mode,
        existingAutomaticPurlinCount);

    private static AutomaticPurlinDialogWindow CreateOffscreenWindow(
        AutomaticPurlinDialogViewModel viewModel) =>
        new(viewModel, SettingsTheme.Light)
        {
            Left = -30000,
            Top = -30000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

    private static IReadOnlyList<ResolvedRoofStructuralEdge> Ridges(SolvedFixture solved) =>
        RoofStructuralEdgeIdentityResolver.Resolve(solved.Geometry, solved.Provenance).Edges
            .Where(edge => edge.StructuralRole == RoofStructuralRole.Ridge &&
                Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) <=
                RoofAutomaticPurlinPlanner.CoordinateToleranceMm)
            .ToArray();

    private static SolvedFixture Solve(IReadOnlyList<RoofPoint2D> points)
    {
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
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(geometry.IsValid, geometry.Error.ToString());
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private static RoofPoint2D[] RectanglePoints() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static T NamedDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement => Descendants<T>(root).Single(element => element.Name == name);

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Automatic Purlin dialog test timed out.");
        Assert.Null(failure);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
