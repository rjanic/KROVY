using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class HipRoofPreviewWindowTests
{
    public static IEnumerable<object[]> ConcaveFootprints()
    {
        yield return ["L", new RoofPoint2D[] { new(0, 0), new(8000, 0), new(8000, 3000), new(3000, 3000), new(3000, 8000), new(0, 8000) }];
        yield return ["U", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000), new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000) }];
        yield return ["T", new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000), new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000) }];
    }

    [Theory]
    [MemberData(nameof(ConcaveFootprints))]
    public void ViewModel_SolvesGenericConcaveFootprintsWithoutDirectionInput(
        string name,
        RoofPoint2D[] points)
    {
        var viewModel = new HipRoofPreviewViewModel(Validate(points), CultureInfo.GetCultureInfo("en"));

        Assert.True(viewModel.CanPreview, name);
        Assert.Empty(viewModel.ValidationMessage);
        Assert.True(viewModel.TryGetRoofGeometry(out var geometry));
        Assert.NotNull(geometry);
        Assert.Contains(geometry!.Topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.Valley);
        Assert.DoesNotContain(
            typeof(HipRoofPreviewViewModel).GetProperties(),
            property => property.Name.Contains("Direction", StringComparison.Ordinal));
    }

    [Fact]
    public void SessionValidation_PreservesEnteredPitchAndKeepsApplyEnabled()
    {
        var culture = CultureInfo.GetCultureInfo("sk-SK");
        var viewModel = new HipRoofPreviewViewModel(
            Rectangle(),
            45d,
            HipRoofDialogMode.Edit,
            culture: culture)
        {
            SlopeText = "12",
        };

        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryGetRoofGeometry(out var proposed));
        Assert.Equal(12d, proposed!.PrimarySlopeDegrees);

        viewModel.SetSessionValidation("Command_RoofEdit_PurlinElevationOutsideRoof");

        Assert.True(viewModel.HasSessionValidation);
        Assert.True(viewModel.HasValidationMessage);
        Assert.Equal("12", viewModel.SlopeText);
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.CanPreview);
        Assert.True(viewModel.TryGetRoofGeometry(out var retained));
        Assert.Equal(12d, retained!.PrimarySlopeDegrees);
        Assert.Equal(
            UiStrings.GetString("Command_RoofEdit_PurlinElevationOutsideRoof", culture)
                .TrimStart('\r', '\n')
                .TrimEnd(),
            viewModel.ValidationMessage);
        Assert.Contains("Medziľahlá väznica", viewModel.ValidationMessage);
        Assert.DoesNotContain("Automatické krokvy", viewModel.ValidationMessage);

        viewModel.SlopeText = "55";
        Assert.False(viewModel.HasSessionValidation);
        Assert.True(viewModel.CanApply);
        Assert.Empty(viewModel.ValidationMessage);
        Assert.True(viewModel.TryGetRoofGeometry(out var corrected));
        Assert.Equal(55d, corrected!.PrimarySlopeDegrees);
    }

    [Fact]
    public void SessionValidation_ShowsInlinePanelAndFocusesSlopeWithoutClosing()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var culture = CultureInfo.GetCultureInfo("sk-SK");
            var viewModel = new HipRoofPreviewViewModel(
                Rectangle(),
                45d,
                HipRoofDialogMode.Edit,
                culture: culture)
            {
                SlopeText = "12",
            };
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            viewModel.SetSessionValidation("Command_RoofEdit_PurlinElevationOutsideRoof");
            window.UpdateLayout();
            window.FocusSlopeInput();
            window.Dispatcher.Invoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => { }));

            Assert.True(viewModel.HasValidationMessage);
            Assert.Equal(Visibility.Visible, window.ValidationPanel.Visibility);
            Assert.NotNull(window.ValidationIcon);
            Assert.Equal(Visibility.Visible, window.ValidationIcon.Visibility);
            Assert.Contains("Medziľahlá väznica", window.ValidationTextBlock.Text);
            Assert.True(window.ApplyButton.IsEnabled);
            Assert.True(window.IsVisible);
            Assert.False(window.IsClosed);
            Assert.Equal("12", window.SlopeTextBox.Text);
            Assert.True(
                window.SlopeTextBox.SelectionLength == window.SlopeTextBox.Text.Length ||
                window.SlopeTextBox.IsKeyboardFocused);

            viewModel.SlopeText = "25";
            window.UpdateLayout();
            Assert.False(viewModel.HasValidationMessage);
            Assert.Equal(Visibility.Collapsed, window.ValidationPanel.Visibility);

            window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(HipRoofPreviewDialogAction.Cancel, window.RequestedAction);
            Assert.False(window.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public void InvalidSlope_DisablesPreviewAndUsesLocalizedValidation()
    {
        var culture = CultureInfo.GetCultureInfo("en");
        var viewModel = new HipRoofPreviewViewModel(Rectangle(), culture)
        {
            SlopeText = "not-a-number",
        };

        Assert.False(viewModel.CanPreview);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.TryGetRoofGeometry(out _));
        Assert.Equal(UiStrings.GetString("RoofGeometryWindow_ValidationNumber", culture), viewModel.ValidationMessage);
    }

    [Fact]
    public void PersistedEdit_SeedsEditableSlopeAndOffersLocalizedApply()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var culture = CultureInfo.GetCultureInfo("en");
            var viewModel = new HipRoofPreviewViewModel(
                Rectangle(),
                37.5d,
                HipRoofDialogMode.Edit,
                culture: culture);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            Assert.Equal("37.5", viewModel.SlopeText);
            Assert.True(viewModel.CanPreview);
            Assert.True(viewModel.CanApply);
            Assert.True(window.ApplyButton.IsVisible);
            Assert.False(window.SlopeTextBox.IsReadOnly);
            Assert.Equal(UiStrings.GetString("EditWindow_Apply", culture), window.ApplyButton.Content);
            viewModel.SlopeText = "45";
            Assert.True(viewModel.TryGetRoofGeometry(out var geometry));
            Assert.Equal(45d, geometry!.PrimarySlopeDegrees);

            window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(HipRoofPreviewDialogAction.Cancel, window.RequestedAction);
            window.Close();
        });
    }

    [Fact]
    public void SlopeTextBox_UsesFixedHeightNumericLayoutContract()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var culture = CultureInfo.GetCultureInfo("en");
            var viewModel = new HipRoofPreviewViewModel(Rectangle(), culture)
            {
                SlopeText = "30",
            };
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            var slope = window.SlopeTextBox;
            Assert.Equal("30", slope.Text);
            Assert.Equal(TextAlignment.Right, slope.TextAlignment);
            Assert.Equal(VerticalAlignment.Center, slope.VerticalContentAlignment);
            Assert.Equal(36d, slope.MinHeight, 0.1);
            Assert.Equal(36d, slope.Height, 0.1);
            Assert.Equal(36d, slope.MaxHeight, 0.1);
            Assert.True(slope.ActualHeight >= 35.5, $"ActualHeight={slope.ActualHeight}");
            Assert.True(slope.ActualHeight <= 36.5, $"ActualHeight={slope.ActualHeight}");
            Assert.Equal(new Thickness(10, 0, 10, 0), slope.Padding);
            Assert.Same(window.FindResource("HipSlopeTextBoxStyle"), slope.Style);

            window.Close();
        });
    }

    [Fact]
    public void Window_UsesExistingStylesAndOffersPreviewCreateAndCancel()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var culture = CultureInfo.GetCultureInfo("en");
            var viewModel = new HipRoofPreviewViewModel(Rectangle(), culture);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            Assert.Equal(UiStrings.GetString("CommandUi_RoofHip_Label", culture), window.Title);
            Assert.True(window.PreviewButton.IsEnabled);
            Assert.True(window.ApplyButton.IsEnabled);
            Assert.False(window.SlopeTextBox.IsReadOnly);
            Assert.Equal(UiStrings.GetString("RoofGeometryWindow_Create", culture), window.ApplyButton.Content);
            Assert.Same(window.FindResource("SettingsSecondaryButtonStyle"), window.PreviewButton.Style);
            Assert.Same(window.FindResource("SettingsPrimaryButtonStyle"), window.ApplyButton.Style);

            window.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(HipRoofPreviewDialogAction.Preview, window.RequestedAction);
            Assert.False(window.IsClosed);

            window.PrepareForInteraction();
            window.Show();
            window.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(HipRoofPreviewDialogAction.Apply, window.RequestedAction);
            window.Close();

            var cancelWindow = CreateOffscreenWindow(new HipRoofPreviewViewModel(Rectangle(), culture));
            cancelWindow.Show();
            cancelWindow.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(cancelWindow.IsVisible);
            Assert.Equal(HipRoofPreviewDialogAction.Cancel, cancelWindow.RequestedAction);
            cancelWindow.Close();
        });
    }

    [Fact]
    public void RectangleMapping_UsesTopologyNodesAtSourceElevationAndOmitsEaves()
    {
        var geometry = Solve(Rectangle());
        var segments = RoofTransientPreviewSession.MapTopologySegments(geometry, 1250d);

        Assert.Equal(5, segments.Count);
        Assert.Equal(4, segments.Count(segment => segment.Kind == RoofTopologyEdgeKind.Hip));
        Assert.Single(segments, segment => segment.Kind == RoofTopologyEdgeKind.Ridge);
        Assert.All(segments, segment =>
        {
            Assert.True(segment.Kind is RoofTopologyEdgeKind.Hip or
                RoofTopologyEdgeKind.Ridge or RoofTopologyEdgeKind.Valley);
            Assert.True(segment.Start.Z >= 1250d);
            Assert.True(segment.End.Z >= 1250d);
        });
    }

    [Fact]
    public void CoplanarOwnershipSeams_AreNotMappedToPreviewSegments()
    {
        var footprint = Validate([
            new(0, 0), new(4000, 0), new(4000, 1500), new(8000, 1500),
            new(8000, 0), new(14000, 0), new(14000, 8000), new(8000, 8000),
            new(8000, 3500), new(4000, 3500), new(4000, 6000), new(0, 6000),
        ]);
        var geometry = Solve(footprint);
        Assert.Contains(geometry.Topology.Edges, edge => edge.Kind == RoofTopologyEdgeKind.CoplanarSeam);

        var segments = RoofTransientPreviewSession.MapTopologySegments(geometry, 0d);

        Assert.NotEmpty(segments);
        Assert.DoesNotContain(segments, segment => segment.Kind == RoofTopologyEdgeKind.CoplanarSeam);
        Assert.DoesNotContain(segments, segment => segment.Kind == RoofTopologyEdgeKind.Eave);
    }

    private static HipRoofPreviewWindow CreateOffscreenWindow(HipRoofPreviewViewModel viewModel) =>
        new(viewModel, SettingsTheme.Light)
        {
            Left = -30000,
            Top = -30000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

    private static HipRoofGeometry Solve(RoofFootprint footprint) => Assert.IsType<HipRoofGeometry>(
        RoofGeometrySolver.Solve(new RoofDefinition(footprint, new RoofParameters(30d), RoofKind.Hip)).Geometry);

    private static RoofFootprint Rectangle() =>
        Validate([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)]);

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Hip roof preview dialog test timed out.");
        Assert.Null(failure);
    }
}
