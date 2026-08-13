using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AcKrovy.Localization;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Compact Fashion Look notice that closes automatically and requires no user action.
/// It contains no drawing or AutoCAD database behavior.
/// Mouse clicks never dismiss — selection mouse-up must not bleed into this modal.
/// </summary>
public partial class TransientNotificationWindow : Window
{
    internal static readonly TimeSpan DefaultAutoCloseDuration =
        TimeSpan.FromMilliseconds(2500);

    private readonly DispatcherTimer _autoCloseTimer;
    private bool _escapeArmed;

    internal TransientNotificationWindow(
        string title,
        string body,
        SettingsTheme theme,
        TimeSpan? autoCloseDuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        Title = title;
        NotificationTitleText.Text = title;
        NotificationBodyText.Text = body;
        AutoCloseDuration = NormalizeDuration(autoCloseDuration);
        _autoCloseTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = AutoCloseDuration,
        };
        _autoCloseTimer.Tick += AutoCloseTimer_Tick;
        Loaded += TransientNotificationWindow_Loaded;
        Closed += TransientNotificationWindow_Closed;
    }

    internal TimeSpan AutoCloseDuration { get; }

    private static TimeSpan NormalizeDuration(TimeSpan? duration) =>
        duration is { } value && value > TimeSpan.Zero
            ? value
            : DefaultAutoCloseDuration;

    private void TransientNotificationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Display and auto-close start immediately; Esc is armed only after idle
        // so leftover keyboard input from the host prompt cannot dismiss instantly.
        _autoCloseTimer.Start();
        Focus();
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(ArmEscapeDismiss));
    }

    private void ArmEscapeDismiss()
    {
        if (_escapeArmed || !IsLoaded)
        {
            return;
        }

        _escapeArmed = true;
        KeyDown += Window_KeyDown;
    }

    private void AutoCloseTimer_Tick(object? sender, EventArgs e) => Dismiss();

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != WpfKey.Escape)
        {
            return;
        }

        e.Handled = true;
        Dismiss();
    }

    private void Dismiss()
    {
        _autoCloseTimer.Stop();
        Close();
    }

    private void TransientNotificationWindow_Closed(object? sender, EventArgs e)
    {
        _autoCloseTimer.Stop();
        _autoCloseTimer.Tick -= AutoCloseTimer_Tick;
        if (_escapeArmed)
        {
            KeyDown -= Window_KeyDown;
            _escapeArmed = false;
        }

        Loaded -= TransientNotificationWindow_Loaded;
        Closed -= TransientNotificationWindow_Closed;
    }
}
