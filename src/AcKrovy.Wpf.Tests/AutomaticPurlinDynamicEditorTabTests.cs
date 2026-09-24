using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
public sealed class AutomaticPurlinDynamicEditorTabTests
{
    [Fact]
    public void EditorTabs_OrderWallPlateRidgeThenEachIntermediate_AndPlusAddsSelects()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel();
            Assert.Equal(
                new[]
                {
                    AutomaticPurlinEditorTabKind.WallPlate,
                    AutomaticPurlinEditorTabKind.Ridge,
                    AutomaticPurlinEditorTabKind.Intermediate,
                },
                viewModel.EditorTabs.Select(tab => tab.Kind));
            Assert.Equal("Pomúrnica", viewModel.EditorTabs[0].Title);
            Assert.Equal("Vrcholová väznica", viewModel.EditorTabs[1].Title);
            Assert.Equal("Medziľahlá väznica 1", viewModel.EditorTabs[2].Title);

            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();
            Assert.Equal(AutomaticPurlinEditorTabKind.WallPlate, viewModel.SelectedEditorTab?.Kind);

            window.AddRowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            Assert.Equal(2, viewModel.Rows.Count);
            Assert.Equal(4, viewModel.EditorTabs.Count);
            Assert.Equal(AutomaticPurlinEditorTabKind.Intermediate, viewModel.SelectedEditorTab?.Kind);
            Assert.Same(viewModel.Rows[1], viewModel.SelectedEditorTab?.IntermediateRow);
            Assert.Equal("Medziľahlá väznica 2", viewModel.SelectedEditorTab?.Title);

            var preserved = "1777";
            viewModel.Rows[0].PlacementValueText = preserved;
            viewModel.SelectedEditorTab = viewModel.EditorTabs[0];
            window.UpdateLayout();
            viewModel.SelectedEditorTab = viewModel.EditorTabs[2];
            window.UpdateLayout();
            Assert.Equal(preserved, viewModel.Rows[0].PlacementValueText);

            window.Close();
        });
    }

    [Fact]
    public void IntermediateTabHeaders_ShowIndependentFieldErrors_WithoutMarkingSiblings()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel();
            var first = viewModel.Rows[0];
            var second = viewModel.AddRow();
            first.PlacementValueText = "not-a-number";
            second.PlacementValueText = "1000";

            Assert.True(first.HasAnyFieldError);
            Assert.False(second.HasAnyFieldError);

            var firstTab = viewModel.EditorTabs.Single(tab =>
                ReferenceEquals(tab.IntermediateRow, first));
            var secondTab = viewModel.EditorTabs.Single(tab =>
                ReferenceEquals(tab.IntermediateRow, second));
            Assert.True(firstTab.HasError);
            Assert.False(secondTab.HasError);
            Assert.Contains("errors", firstTab.AutomationName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(second.DisplayName, secondTab.AutomationName);

            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            viewModel.SelectedEditorTab = secondTab;
            window.UpdateLayout();
            Assert.True(firstTab.HasError);
            Assert.False(secondTab.HasError);

            // Inactive error tab still carries invalid chrome resources.
            var errorTabItem = window.ElementEditorsTabControl.ItemContainerGenerator
                .ContainerFromItem(firstTab) as TabItem;
            Assert.NotNull(errorTabItem);
            Assert.True(PurlinFieldValidation.GetHasError(errorTabItem!) || firstTab.HasError);
            var borderBrush = errorTabItem!.BorderBrush as SolidColorBrush;
            var background = errorTabItem.Background as SolidColorBrush;
            Assert.NotNull(borderBrush);
            Assert.NotNull(background);
            Assert.Equal(Color.FromRgb(0xC6, 0x28, 0x28), borderBrush!.Color);
            Assert.Equal(Color.FromRgb(0xFD, 0xEC, 0xEA), background!.Color);

            window.Close();
        });
    }

    [Fact]
    public void InvalidPlacementTextBox_UsesRedBorderAndLightRedBackground()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel();
            ClearIntermediateRows(viewModel);
            viewModel.RidgeEnabled = false;
            viewModel.WallPlateEnabled = true;
            viewModel.SelectedEditorTab = viewModel.EditorTabs.First(tab =>
                tab.Kind == AutomaticPurlinEditorTabKind.WallPlate);
            viewModel.WallPlateRow.PlacementMode =
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
            viewModel.WallPlateRow.PlacementValueText = "not-a-number";

            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            Assert.True(viewModel.WallPlateRow.PlacementValueHasError);
            Assert.True(viewModel.EditorTabs.First(tab =>
                tab.Kind == AutomaticPurlinEditorTabKind.WallPlate).HasError);

            var placementBox = Descendants<TextBox>(window)
                .First(box => PurlinFieldValidation.GetHasError(box));
            Assert.Equal(2d, placementBox.BorderThickness.Left, 3);
            var borderBrush = Assert.IsType<SolidColorBrush>(placementBox.BorderBrush);
            var background = Assert.IsType<SolidColorBrush>(placementBox.Background);
            var foreground = Assert.IsType<SolidColorBrush>(placementBox.Foreground);
            Assert.Equal(Color.FromRgb(0xC6, 0x28, 0x28), borderBrush.Color);
            Assert.Equal(Color.FromRgb(0xFD, 0xEC, 0xEA), background.Color);
            // Entered value stays dark/readable — never cream header text on the fill.
            Assert.Equal(Color.FromRgb(0x4A, 0x2D, 0x1A), foreground.Color);
            Assert.Null(placementBox.ToolTip);

            var hint = Descendants<TextBlock>(window)
                .First(block => Equals(block.Tag, "PurlinFieldValidationHint") &&
                                block.Visibility == Visibility.Visible);
            Assert.Equal(viewModel.WallPlateRow.PlacementValueErrorText, hint.Text);
            Assert.False(string.IsNullOrWhiteSpace(hint.Text));
            var hintBrush = Assert.IsType<SolidColorBrush>(hint.Foreground);
            Assert.Equal(Color.FromRgb(0x7D, 0x17, 0x12), hintBrush.Color);

            viewModel.WallPlateRow.PlacementValueText = "20";
            window.UpdateLayout();
            Assert.False(viewModel.WallPlateRow.PlacementValueHasError);
            Assert.DoesNotContain(
                Descendants<TextBox>(window),
                box => PurlinFieldValidation.GetHasError(box));
            Assert.DoesNotContain(
                Descendants<TextBlock>(window),
                block => Equals(block.Tag, "PurlinFieldValidationHint") &&
                         block.Visibility == Visibility.Visible &&
                         !string.IsNullOrEmpty(block.Text));

            window.Close();
        });
    }

    [Fact]
    public void TechnicalMetricButtons_BindSelectedElementValues_InRoofPlaneTopCenterBottomOrder()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel();
            viewModel.RidgeEnabled = true;
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            viewModel.SelectedEditorTab = viewModel.EditorTabs.First(tab =>
                tab.Kind == AutomaticPurlinEditorTabKind.Ridge);
            window.UpdateLayout();

            var ridgeButtons = Descendants<Button>(window)
                .Where(button => Equals(button.Tag, "RoofPlane") ||
                                 Equals(button.Tag, "Top") ||
                                 Equals(button.Tag, "Center") ||
                                 Equals(button.Tag, "Bottom"))
                .Where(button => button.IsVisible)
                .ToList();
            Assert.Equal(4, ridgeButtons.Count);
            Assert.Equal(
                new[] { "RoofPlane", "Top", "Center", "Bottom" },
                ridgeButtons.Select(button => button.Tag).Cast<string>());

            Assert.Equal(
                viewModel.RidgeTechnicalSummary.RoofPlaneRelative,
                FindMetricValue(ridgeButtons[0]));
            Assert.Equal(
                viewModel.RidgeTechnicalSummary.TopRelative,
                FindMetricValue(ridgeButtons[1]));
            Assert.Equal(
                viewModel.RidgeTechnicalSummary.CenterRelative,
                FindMetricValue(ridgeButtons[2]));
            Assert.Equal(
                viewModel.RidgeTechnicalSummary.BottomRelative,
                FindMetricValue(ridgeButtons[3]));
            Assert.NotEqual(
                viewModel.RidgeTechnicalSummary.RoofPlaneRelative,
                viewModel.RidgeTechnicalSummary.TopRelative);

            var intermediate = viewModel.Rows[0];
            viewModel.SelectedEditorTab = viewModel.EditorTabs.First(tab =>
                ReferenceEquals(tab.IntermediateRow, intermediate));
            window.UpdateLayout();

            var intermediateButtons = Descendants<Button>(window)
                .Where(button => Equals(button.Tag, "RoofPlane") ||
                                 Equals(button.Tag, "Top") ||
                                 Equals(button.Tag, "Center") ||
                                 Equals(button.Tag, "Bottom"))
                .Where(button => button.IsVisible)
                .ToList();
            Assert.Equal(intermediate.RoofPlaneRelative, FindMetricValue(intermediateButtons[0]));
            Assert.Equal(intermediate.TopRelative, FindMetricValue(intermediateButtons[1]));
            Assert.Equal(intermediate.CenterRelative, FindMetricValue(intermediateButtons[2]));
            Assert.Equal(intermediate.BottomRelative, FindMetricValue(intermediateButtons[3]));

            window.Close();
        });
    }

    [Fact]
    public void TechnicalMetricClick_PinsSingleTooltip_EscapeAndTabSwitchClose()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel();
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            viewModel.SelectedEditorTab = viewModel.EditorTabs.First(tab =>
                tab.Kind == AutomaticPurlinEditorTabKind.Ridge);
            window.UpdateLayout();

            var roofPlane = Descendants<Button>(window)
                .First(button => Equals(button.Tag, "RoofPlane") && button.IsVisible);
            var top = Descendants<Button>(window)
                .First(button => Equals(button.Tag, "Top") && button.IsVisible);

            roofPlane.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.IsTechnicalTooltipPinned);
            Assert.Same(
                viewModel.RidgeTechnicalSummary.RoofPlaneTooltip,
                window.PinnedTechnicalTooltipPresenter.Content);

            top.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.IsTechnicalTooltipPinned);
            Assert.Same(
                viewModel.RidgeTechnicalSummary.TopTooltip,
                window.PinnedTechnicalTooltipPresenter.Content);

            window.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window)!,
                0,
                Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });
            Assert.False(window.IsTechnicalTooltipPinned);

            roofPlane.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.IsTechnicalTooltipPinned);
            viewModel.SelectedEditorTab = viewModel.EditorTabs[0];
            window.UpdateLayout();
            Assert.False(window.IsTechnicalTooltipPinned);

            window.Close();
        });
    }

    [Fact]
    public void ZeroIntermediateRows_StillShowsWallPlateRidgeAndPlus()
    {
        var viewModel = CreateViewModel();
        ClearIntermediateRows(viewModel);
        Assert.Equal(
            new[]
            {
                AutomaticPurlinEditorTabKind.WallPlate,
                AutomaticPurlinEditorTabKind.Ridge,
            },
            viewModel.EditorTabs.Select(tab => tab.Kind));
        Assert.DoesNotContain(
            viewModel.EditorTabs,
            tab => tab.Kind == AutomaticPurlinEditorTabKind.Intermediate);
    }

    private static string FindMetricValue(Button button)
    {
        var values = Descendants<TextBlock>(button).Select(block => block.Text).ToList();
        return Assert.Single(
            values,
            text =>
                text.StartsWith("+", StringComparison.Ordinal) ||
                text.StartsWith("-", StringComparison.Ordinal) ||
                text.StartsWith("±", StringComparison.Ordinal) ||
                text == "—");
    }

    private static AutomaticPurlinDialogViewModel CreateViewModel()
    {
        var solved = Solve(RectanglePoints());
        return new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            null,
            false,
            null,
            false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            AppLanguageService.CurrentUiCulture);
    }

    private static void ClearIntermediateRows(AutomaticPurlinDialogViewModel viewModel)
    {
        while (viewModel.Rows.Count > 0)
        {
            viewModel.RemoveRow(viewModel.Rows[0]);
        }
    }

    private static AutomaticPurlinDialogWindow CreateOffscreenWindow(
        AutomaticPurlinDialogViewModel viewModel) =>
        new(viewModel, SettingsTheme.Light)
        {
            Left = -30000,
            Top = -30000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

    private static SolvedFixture Solve(RoofPoint2D[] points)
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Dynamic editor tab test timed out.");
        Assert.Null(failure);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
