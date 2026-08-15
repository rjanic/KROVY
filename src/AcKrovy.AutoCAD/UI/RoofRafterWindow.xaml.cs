using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Compact, drawing-neutral Stage 6 rafter parameter task dialog.</summary>
public partial class RoofRafterWindow : Window
{
    private readonly SimpleGableRoofGeometry _geometry;
    private readonly CultureInfo _culture;
    private RoofRafterRequestValidationResult? _currentValidation;
    private bool _initialized;

    internal RoofRafterWindow(
        SimpleGableRoofGeometry geometry,
        RoofRafterPreferences preferences,
        SettingsTheme theme,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(preferences);

        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        _geometry = geometry;
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        MaterialOptions = TimberMaterialDisplayNameProvider.GetOptions(
            preferences.Material,
            _culture);
        MaterialComboBox.ItemsSource = MaterialOptions;
        MaterialComboBox.SelectedItem = MaterialOptions.FirstOrDefault(item =>
            string.Equals(item.StoredValue, preferences.Material, StringComparison.Ordinal));
        WidthTextBox.Text = FormatInput(preferences.WidthMm);
        HeightTextBox.Text = FormatInput(preferences.HeightMm);
        MaximumSpacingTextBox.Text = FormatInput(preferences.MaximumSpacingMm);
        RoofSlopeTextBox.Text = UiStrings.Format(
            UiStrings.GetString("RoofRafterWindow_RoofSlopeValueFormat", _culture),
            geometry.SlopeDegrees);
        _initialized = true;
        UpdateValidationAndSummary();
    }

    internal IReadOnlyList<TimberMaterialDisplayOption> MaterialOptions { get; }

    internal RoofRafterCreationRequest? Request { get; private set; }

    internal SimpleGableRafterLayout? PreviewLayout => _currentValidation?.Layout;

    private string FormatInput(double value) => value.ToString("0.###", _culture);

    private void Input_Changed(object sender, TextChangedEventArgs e) =>
        UpdateValidationAndSummary();

    private void Input_Changed(object sender, SelectionChangedEventArgs e) =>
        UpdateValidationAndSummary();

    private void UpdateValidationAndSummary()
    {
        if (!_initialized)
        {
            return;
        }

        var width = ParseNumber(WidthTextBox.Text);
        var height = ParseNumber(HeightTextBox.Text);
        var spacing = ParseNumber(MaximumSpacingTextBox.Text);
        var material = (MaterialComboBox.SelectedItem as TimberMaterialDisplayOption)?.StoredValue;
        var validation = RoofRafterRequestValidator.Validate(
            _geometry,
            width,
            height,
            spacing,
            material);

        if (validation.IsValid &&
            !TimberMaterialCatalog.TryGetItem(validation.Request!.Material, out _))
        {
            validation = new RoofRafterRequestValidationResult(
                null,
                null,
                RoofRafterRequestValidationError.InvalidMaterial);
        }

        _currentValidation = validation;
        CreateButton.IsEnabled = validation.IsValid;
        ValidationTextBlock.Text = validation.IsValid
            ? string.Empty
            : UiStrings.GetString(ValidationKey(validation.Error), _culture);
        SummaryTextBlock.Text = validation.Layout is { } layout
            ? UiStrings.Format(
                UiStrings.GetString("RoofRafterWindow_SummaryFormat", _culture),
                layout.Rafters.Count,
                layout.StationCount,
                layout.ActualSpacingMm)
            : UiStrings.GetString("RoofRafterWindow_SummaryUnavailable", _culture);
    }

    private double ParseNumber(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, _culture, out var value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return double.NaN;
    }

    private static string ValidationKey(RoofRafterRequestValidationError error) => error switch
    {
        RoofRafterRequestValidationError.InvalidWidth => "RoofRafterWindow_InvalidWidth",
        RoofRafterRequestValidationError.WidthDoesNotFitRoof => "RoofRafterWindow_WidthDoesNotFitRoof",
        RoofRafterRequestValidationError.InvalidHeight => "RoofRafterWindow_InvalidHeight",
        RoofRafterRequestValidationError.InvalidMaximumSpacing => "RoofRafterWindow_InvalidSpacing",
        RoofRafterRequestValidationError.InvalidMaterial => "RoofRafterWindow_InvalidMaterial",
        _ => "RoofRafterWindow_InvalidRoof",
    };

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateValidationAndSummary();
        if (_currentValidation?.IsValid != true)
        {
            return;
        }

        Request = _currentValidation.Request;
        DialogResult = true;
    }
}
