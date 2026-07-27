using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Localization;
using FormsScreen = System.Windows.Forms.Screen;

namespace AcKrovy.AutoCAD.UI;

public partial class DiagnosticsWindow : Window
{
    private const double PreferredWidth = 720;
    private const double PreferredMinimumWidth = 600;
    private const double PreferredMinimumHeight = 420;
    private const double PreferredMaximumWidth = 960;
    private const double PreferredMaximumHeight = 760;
    private const double WorkAreaMargin = 32;
    private const double SmallestUsableWidth = 360;
    private const double SmallestUsableHeight = 320;

    private readonly string _summary;
    private readonly string _logDirectory;

    internal DiagnosticsWindow(
        IReadOnlyList<DiagnosticsInfoRow> informationRows,
        IReadOnlyList<DiagnosticsInfoRow> settingsRows,
        IReadOnlyList<string> recentEvents,
        string summary,
        string logDirectory,
        SettingsTheme theme)
    {
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        SourceInitialized += DiagnosticsWindow_SourceInitialized;
        _summary = summary;
        _logDirectory = logDirectory;
        DataContext = new DiagnosticsViewModel(
            informationRows,
            settingsRows,
            recentEvents);
    }

    private void DiagnosticsWindow_SourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= DiagnosticsWindow_SourceInitialized;
        var workArea = ResolveWorkAreaInDeviceIndependentPixels();
        var availableWidth = Math.Max(
            SmallestUsableWidth,
            workArea.Width - WorkAreaMargin);
        var availableHeight = Math.Max(
            SmallestUsableHeight,
            workArea.Height - WorkAreaMargin);

        MaxWidth = Math.Min(PreferredMaximumWidth, availableWidth);
        MaxHeight = Math.Min(PreferredMaximumHeight, availableHeight);
        MinWidth = Math.Min(PreferredMinimumWidth, MaxWidth);
        MinHeight = Math.Min(PreferredMinimumHeight, MaxHeight);
        Width = Math.Min(PreferredWidth, MaxWidth);
    }

    private Rect ResolveWorkAreaInDeviceIndependentPixels()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            var referenceHandle = helper.Owner != IntPtr.Zero
                ? helper.Owner
                : helper.Handle;
            var pixelArea = FormsScreen.FromHandle(referenceHandle).WorkingArea;
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ??
                Matrix.Identity;
            var topLeft = transform.Transform(
                new System.Windows.Point(pixelArea.Left, pixelArea.Top));
            var bottomRight = transform.Transform(
                new System.Windows.Point(pixelArea.Right, pixelArea.Bottom));
            return new Rect(topLeft, bottomRight);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_summary);
            AcKrovyDiagnostics.Info("DiagnosticsSummaryCopied", "Diagnostic summary copied.");
        }
        catch (Exception exception)
        {
            AcKrovyDiagnostics.Warning(
                "DiagnosticsClipboardFailed",
                "Diagnostic summary could not be copied.",
                exception: exception);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_logDirectory}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AcKrovyDiagnostics.Warning(
                "DiagnosticsOpenLogsFailed",
                "Log directory could not be opened.",
                exception: exception);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record DiagnosticsInfoRow(string Label, string Value);

internal sealed record DiagnosticsViewModel(
    IReadOnlyList<DiagnosticsInfoRow> InformationRows,
    IReadOnlyList<DiagnosticsInfoRow> SettingsRows,
    IReadOnlyList<string> RecentEvents);
