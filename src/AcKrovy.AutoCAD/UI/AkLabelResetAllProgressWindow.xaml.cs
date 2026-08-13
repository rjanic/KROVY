using System.Windows;
using System.Windows.Threading;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class AkLabelResetAllProgressWindow : Window
{
    private readonly int _total;
    private readonly System.Globalization.CultureInfo _uiCulture;

    public AkLabelResetAllProgressWindow(int total, SettingsTheme theme)
    {
        _total = Math.Max(0, total);
        _uiCulture = AppLanguageService.CurrentUiCulture;
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        ProgressBar.IsIndeterminate = _total <= 0;
        if (_total > 0)
        {
            ProgressBar.Maximum = _total;
            ProgressBar.Value = 0;
            ProgressBar.IsIndeterminate = false;
        }

        Report(0, TimeSpan.Zero, estimatedRemaining: null);
    }

    public void Report(int processed, TimeSpan elapsed, TimeSpan? estimatedRemaining)
    {
        var safeProcessed = Math.Clamp(processed, 0, Math.Max(_total, processed));
        if (_total > 0)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Maximum = _total;
            ProgressBar.Value = Math.Min(safeProcessed, _total);
            CountText.Text = UiStrings.Format(
                UiStrings.GetString("AkLabelResetAllProgress_CountFormat", _uiCulture),
                safeProcessed,
                _total);
        }
        else
        {
            ProgressBar.IsIndeterminate = true;
            CountText.Text = UiStrings.Format(
                UiStrings.GetString("AkLabelResetAllProgress_ProcessedFormat", _uiCulture),
                safeProcessed);
        }

        ElapsedText.Text = UiStrings.Format(
            UiStrings.GetString("AkLabelResetAllProgress_ElapsedFormat", _uiCulture),
            FormatDuration(elapsed));

        if (estimatedRemaining is { } eta && eta > TimeSpan.Zero)
        {
            EtaText.Visibility = Visibility.Visible;
            EtaText.Text = UiStrings.Format(
                UiStrings.GetString("AkLabelResetAllProgress_EtaFormat", _uiCulture),
                FormatDuration(eta));
        }
        else
        {
            EtaText.Visibility = Visibility.Collapsed;
            EtaText.Text = string.Empty;
        }

        Dispatcher.Invoke(DispatcherPriority.Render, static () => { });
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}";
        }

        return $"{value.Minutes:D2}:{value.Seconds:D2}";
    }
}
