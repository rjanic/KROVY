using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.Windows.Threading;
using AcKrovy.AutoCAD.UI;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class RoofRafterWindowSmokeTests
{
    [Fact]
    public void ConstructsInAllSixLanguagesAndBothThemes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var geometry = Geometry(10000, 8000, 44);
                foreach (var language in new[] { "sk", "cs", "en", "de", "pl", "fr" })
                {
                    AppLanguageService.Apply(language);
                    foreach (var theme in new[] { SettingsTheme.Light, SettingsTheme.Dark })
                    {
                        var window = new RoofRafterWindow(
                            geometry,
                            RoofRafterPreferences.CreateFirstUse("Smrek C24"),
                            theme)
                        {
                            Left = -30000,
                            Top = -30000,
                            ShowInTaskbar = false,
                            WindowStyle = WindowStyle.None,
                        };
                        window.Show();
                        window.UpdateLayout();

                        Assert.False(window.RoofSlopeTextBox.IsTabStop);
                        Assert.True(window.RoofSlopeTextBox.IsReadOnly);
                        Assert.Contains("44", window.RoofSlopeTextBox.Text);
                        Assert.True(window.CreateButton.IsEnabled);
                        Assert.NotNull(window.PreviewLayout);
                        Assert.Equal(26, window.PreviewLayout!.Rafters.Count);
                        Assert.DoesNotContain("RoofRafterWindow_", window.Title);
                        Assert.DoesNotContain("RoofRafterWindow_", window.SummaryTextBlock.Text);
                        window.Close();
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Rafter dialog smoke timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void WidthRecalculatesSummaryAndCancelReturnsNoRequest()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppLanguageService.Apply("sk");
                var window = new RoofRafterWindow(
                    Geometry(10000, 8000, 30),
                    new RoofRafterPreferences(80, 160, 1000, "Smrek C24"),
                    SettingsTheme.Light);
                window.WidthTextBox.Text = "100";
                window.UpdateLayout();

                Assert.Equal(990d, window.PreviewLayout!.ActualSpacingMm, 9);
                Assert.Equal(50d / 10000d, window.PreviewLayout.Rafters[0].StationFraction, 12);
                Assert.Null(window.Request);
                window.Close();
                Assert.Null(window.Request);
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

    [Fact]
    public void CreateReturnsImmutableRequestAndUiPreferenceJsonRoundTripsWithoutSlope()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                AppLanguageService.Apply("en");
                var window = new RoofRafterWindow(
                    Geometry(10000, 8000, 38),
                    new RoofRafterPreferences(100, 180, 1000, "KVH C24 NSi"),
                    SettingsTheme.Dark)
                {
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                Assert.Null(window.Request);
                _ = window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => window.CreateButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent))));
                Assert.True(window.ShowDialog());

                var request = Assert.IsType<RoofRafterCreationRequest>(window.Request);
                Assert.Equal(100d, request.WidthMm);
                Assert.Equal(180d, request.HeightMm);
                Assert.Equal(1000d, request.MaximumSpacingMm);
                Assert.Equal("KVH C24 NSi", request.Material);
                Assert.Equal(38d, request.RoofSlopeDegrees);

                var preferences = new SettingsUiPreferences
                {
                    AutomaticRafterPreferences = request.ToPreferences(),
                };
                var json = JsonSerializer.Serialize(preferences);
                var reopened = JsonSerializer.Deserialize<SettingsUiPreferences>(json)!.Normalize();
                Assert.Equal(request.ToPreferences(), reopened.AutomaticRafterPreferences);
                Assert.DoesNotContain("RoofSlope", json, StringComparison.Ordinal);
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

    private static SimpleGableRoofGeometry Geometry(double length, double width, double slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(length, 0), new(length, width), new(0, width)], true));
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        return SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope, direction))).Geometry!;
    }
}
