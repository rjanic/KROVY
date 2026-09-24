using System.Globalization;
using System.Windows;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Small wood-themed dialog for explicit manual rafter cross-section entry.
/// Presentation only — does not create CAD rafters or change geometry formulas.
/// CAD pick suspends this dialog and restores field text only; Confirm updates draft.
/// </summary>
public partial class AutomaticPurlinManualRafterDialogWindow : Window
{
    private readonly CultureInfo _culture;
    private readonly Func<string, string> _text;
    private readonly double _initialWidthMm;
    private readonly double _initialHeightMm;

    internal AutomaticPurlinManualRafterDialogWindow(
        double initialWidthMm,
        double initialHeightMm,
        CultureInfo culture,
        SettingsTheme theme)
    {
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _text = key => UiStrings.GetString(key, _culture);
        _initialWidthMm = initialWidthMm;
        _initialHeightMm = initialHeightMm;
        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        WidthTextBox.Text = FormatSeed(initialWidthMm);
        HeightTextBox.Text = FormatSeed(initialHeightMm);
        Title = _text("AutomaticPurlin_ManualRafterDialogTitle");
        DescriptionText.Text = _text("AutomaticPurlin_ManualRafterDialogDescription");
        WidthLabel.Text = _text("AutomaticPurlin_Width");
        HeightLabel.Text = _text("AutomaticPurlin_Height");
        UnitWidthText.Text = _text("AutomaticPurlin_UnitMillimetres");
        UnitHeightText.Text = _text("AutomaticPurlin_UnitMillimetres");
        SelectFromDrawingButton.Content = _text("AutomaticPurlin_ManualRafterSelectFromDrawing");
        CancelButton.Content = _text("AutomaticPurlin_Cancel");
        ConfirmButton.Content = _text("AutomaticPurlin_Confirm");
        ClearValidation();
    }

    internal double ConfirmedWidthMm { get; private set; }
    internal double ConfirmedHeightMm { get; private set; }

    /// <summary>
    /// True when the user requested CAD pick; the host suspends/restores this dialog.
    /// </summary>
    internal bool IsSuspendedForCadPick { get; private set; }

    /// <summary>
    /// Width to preserve if CAD pick is cancelled or invalid.
    /// </summary>
    internal double PreserveWidthMm { get; private set; }

    /// <summary>
    /// Height to preserve if CAD pick is cancelled or invalid.
    /// </summary>
    internal double PreserveHeightMm { get; private set; }

    internal void ApplyPickedDimensions(double widthMm, double heightMm)
    {
        WidthTextBox.Text = FormatSeed(widthMm);
        HeightTextBox.Text = FormatSeed(heightMm);
        ClearValidation();
    }

    private string FormatSeed(double valueMm) =>
        double.IsFinite(valueMm) && valueMm > 0d
            ? valueMm.ToString("0.###", _culture)
            : string.Empty;

    private void SelectFromDrawingButton_Click(object sender, RoutedEventArgs e)
    {
        ClearValidation();
        PreserveWidthMm = TryParsePositive(WidthTextBox.Text, out var widthMm)
            ? widthMm
            : (double.IsFinite(_initialWidthMm) && _initialWidthMm > 0d ? _initialWidthMm : 0d);
        PreserveHeightMm = TryParsePositive(HeightTextBox.Text, out var heightMm)
            ? heightMm
            : (double.IsFinite(_initialHeightMm) && _initialHeightMm > 0d ? _initialHeightMm : 0d);
        IsSuspendedForCadPick = true;
        Close();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        ClearValidation();
        if (!TryParsePositive(WidthTextBox.Text, out var widthMm) ||
            !TryParsePositive(HeightTextBox.Text, out var heightMm))
        {
            ValidationText.Text = _text("AutomaticPurlin_ValidationDimension");
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        ConfirmedWidthMm = widthMm;
        ConfirmedHeightMm = heightMm;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private bool TryParsePositive(string? text, out double valueMm)
    {
        valueMm = 0d;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!double.TryParse(text.Trim(), NumberStyles.Float, _culture, out valueMm) &&
            !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out valueMm))
        {
            return false;
        }

        return double.IsFinite(valueMm) && valueMm > 0d;
    }

    private void ClearValidation()
    {
        ValidationText.Text = string.Empty;
        ValidationText.Visibility = Visibility.Collapsed;
    }
}
