using System.Globalization;
using System.Windows;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Explicit choice between persisted manual rafter W×H and actual roof rafter W×H.
/// Cancel leaves the conflict unresolved and does not mutate stored layout.
/// </summary>
public partial class AutomaticPurlinRafterSourceConflictDialogWindow : Window
{
    private readonly CultureInfo _culture;
    private readonly Func<string, string> _text;

    internal AutomaticPurlinRafterSourceConflictDialogWindow(
        double manualWidthMm,
        double manualHeightMm,
        double actualWidthMm,
        double actualHeightMm,
        CultureInfo culture,
        SettingsTheme theme)
    {
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _text = key => UiStrings.GetString(key, _culture);
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        Title = _text("AutomaticPurlin_RafterSourceConflictTitle");
        DescriptionText.Text = _text("AutomaticPurlin_RafterSourceConflictDescription");
        ManualCaptionText.Text = _text("AutomaticPurlin_RafterSourceConflictManualCaption");
        ActualCaptionText.Text = _text("AutomaticPurlin_RafterSourceConflictActualCaption");
        ManualProfileText.Text = FormatProfile(manualWidthMm, manualHeightMm);
        ActualProfileText.Text = FormatProfile(actualWidthMm, actualHeightMm);
        KeepManualButton.Content = _text("AutomaticPurlin_RafterSourceConflictKeepManual");
        UseActualButton.Content = _text("AutomaticPurlin_RafterSourceConflictUseActual");
        CancelButton.Content = _text("AutomaticPurlin_Cancel");
    }

    internal AutomaticPurlinRafterSourceConflictChoice Choice { get; private set; } =
        AutomaticPurlinRafterSourceConflictChoice.Cancelled;

    private string FormatProfile(double widthMm, double heightMm) =>
        string.Format(
            _culture,
            _text("AutomaticPurlin_RafterSourceConflictProfileFormat"),
            widthMm.ToString("0.###", _culture),
            heightMm.ToString("0.###", _culture));

    private void KeepManualButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = AutomaticPurlinRafterSourceConflictChoice.KeepManual;
        DialogResult = true;
    }

    private void UseActualButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = AutomaticPurlinRafterSourceConflictChoice.UseActual;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}

internal enum AutomaticPurlinRafterSourceConflictChoice
{
    Cancelled = 0,
    KeepManual = 1,
    UseActual = 2,
}
