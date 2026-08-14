using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class RoofTransientNotificationSmokeTests
{
    private static readonly string[] LanguageCodes = ["sk", "cs", "en", "de", "pl", "fr"];
    private static readonly (string TitleKey, string BodyKey)[] NotificationKeys =
    [
        ("Command_Roof_OpenLoopNotificationTitle", "Command_Roof_OpenLoopNotificationBody"),
        ("Command_Roof_InvalidObjectNotificationTitle", "Command_Roof_InvalidObjectNotificationBody"),
        ("Command_Roof_InvalidFootprintNotificationTitle", "Command_Roof_InvalidFootprintNotificationBody"),
        ("Command_Roof_UnsupportedFootprintNotificationTitle", "Command_Roof_UnsupportedFootprintNotificationBody"),
        ("Command_Roof_InvalidDirectionNotificationTitle", "Command_Roof_InvalidDirectionNotificationBody"),
        ("Command_Roof_InvalidSlopeNotificationTitle", "Command_Roof_InvalidSlopeNotificationBody"),
    ];

    [Fact]
    public void RoofValidationNotifications_ConstructInAllLanguagesAndThemes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };

                foreach (var languageCode in LanguageCodes)
                {
                    AppLanguageService.Apply(languageCode);
                    foreach (var notificationKeys in NotificationKeys)
                    {
                        var title = UiStrings.GetString(notificationKeys.TitleKey);
                        var body = UiStrings.GetString(notificationKeys.BodyKey);
                        foreach (var theme in new[] { SettingsTheme.Light, SettingsTheme.Dark })
                        {
                            var window = new TransientNotificationWindow(title, body, theme)
                            {
                                Left = -30000,
                                Top = -30000,
                                ShowInTaskbar = false,
                            };
                            window.Show();
                            window.UpdateLayout();

                            Assert.Equal(TimeSpan.FromMilliseconds(2500), window.AutoCloseDuration);
                            Assert.Equal(title, window.Title);
                            Assert.Equal(title, window.NotificationTitleText.Text);
                            Assert.Equal(body, window.NotificationBodyText.Text);
                            Assert.DoesNotContain("Command_Roof_", window.Title, StringComparison.Ordinal);
                            Assert.NotNull(window.NotificationCard.Background);
                            Assert.NotNull(window.NotificationCard.BorderBrush);
                            Assert.Equal(WindowStyle.None, window.WindowStyle);
                            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                            Assert.True(window.AllowsTransparency);
                            Assert.False(window.ShowInTaskbar);
                            window.Close();
                        }
                    }
                }

                AppLanguageService.Apply("en");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(30)),
            "Roof transient notification localization smoke timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Notification_AutoClosesWithoutUserAction()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var window = new TransientNotificationWindow(
                    "Test title",
                    "Test body",
                    SettingsTheme.Light,
                    TimeSpan.FromMilliseconds(50))
                {
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                };
                var stopwatch = Stopwatch.StartNew();

                _ = window.ShowDialog();

                stopwatch.Stop();
                Assert.False(window.IsVisible);
                Assert.InRange(stopwatch.ElapsedMilliseconds, 20, 2000);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(10)),
            "Roof transient notification auto-close smoke timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Notification_MouseClick_DoesNotDismiss()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var window = new TransientNotificationWindow(
                    "Test title",
                    "Test body",
                    SettingsTheme.Light,
                    TimeSpan.FromMilliseconds(400))
                {
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                };
                window.Show();
                window.UpdateLayout();
                DrainToIdle(window.Dispatcher);

                window.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                    Source = window,
                });
                window.Dispatcher.Invoke(DispatcherPriority.Input, static () => { });

                Assert.True(window.IsVisible);
                Assert.True(window.IsLoaded);

                var closed = SpinUntilClosed(window, TimeSpan.FromSeconds(2));
                Assert.True(closed);
                Assert.False(window.IsVisible);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(10)),
            "Roof transient notification mouse-dismiss smoke timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Notification_Escape_DismissesAfterIdleArming()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var window = new TransientNotificationWindow(
                    "Test title",
                    "Test body",
                    SettingsTheme.Light,
                    TimeSpan.FromMilliseconds(5000))
                {
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                };
                window.Show();
                window.UpdateLayout();
                DrainToIdle(window.Dispatcher);

                window.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window)
                        ?? throw new InvalidOperationException("Missing presentation source."),
                    Environment.TickCount,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.KeyDownEvent,
                    Source = window,
                });

                Assert.True(SpinUntilClosed(window, TimeSpan.FromSeconds(2)));
                Assert.False(window.IsVisible);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(10)),
            "Roof transient notification Esc smoke timed out.");
        Assert.Null(failure);
    }

    private static void DrainToIdle(Dispatcher dispatcher)
    {
        dispatcher.Invoke(DispatcherPriority.Loaded, static () => { });
        dispatcher.Invoke(DispatcherPriority.Input, static () => { });
        dispatcher.Invoke(DispatcherPriority.ApplicationIdle, static () => { });
    }

    private static bool SpinUntilClosed(Window window, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            window.Dispatcher.Invoke(DispatcherPriority.Background, static () => { });
            if (!window.IsVisible)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return !window.IsVisible;
    }
}
