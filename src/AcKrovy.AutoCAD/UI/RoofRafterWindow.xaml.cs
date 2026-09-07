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
    private readonly IRoofGeometry _geometry;
    private readonly CultureInfo _culture;
    private readonly double _minimumAutomaticSpacingMm;
    private RoofRafterRequestValidationResult? _currentValidation;
    private RoofFaceRafterLayout? _currentHipPreviewLayout;
    private bool _initialized;

    internal RoofRafterWindow(
        IRoofGeometry geometry,
        RoofRafterPreferences preferences,
        double minimumAutomaticSpacingMm,
        SettingsTheme theme,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!RoofRafterSpacingRules.IsValidDimension(minimumAutomaticSpacingMm))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAutomaticSpacingMm));
        }

        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        _geometry = geometry;
        _minimumAutomaticSpacingMm = minimumAutomaticSpacingMm;
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
        RoofSlopeTextBox.Text = geometry is SimpleGableRoofGeometry
            { Kind: RoofKind.AsymmetricGable } gableGeometry
            ? UiStrings.Format(
                UiStrings.GetString("RoofRafterWindow_RoofSlopesValueFormat", _culture),
                gableGeometry.Face0SlopeDegrees,
                gableGeometry.Face1SlopeDegrees)
            : UiStrings.Format(
                UiStrings.GetString("RoofRafterWindow_RoofSlopeValueFormat", _culture),
                geometry.PrimarySlopeDegrees);
        _initialized = true;
        UpdateValidationAndSummary();
    }

    internal IReadOnlyList<TimberMaterialDisplayOption> MaterialOptions { get; }

    internal RoofRafterCreationRequest? Request { get; private set; }

    internal RoofRafterLayout? PreviewLayout => _currentValidation?.Layout;

    internal RoofFaceRafterLayout? HipPreviewLayout => _currentHipPreviewLayout;

    internal event Action<RoofRafterLayout?>? PreviewLayoutChanged;
    internal event Action<RoofFaceRafterLayout?>? HipPreviewLayoutChanged;

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
        var inputError = RoofRafterRequestValidator.ValidateAutomaticInputs(
            width,
            height,
            spacing,
            _minimumAutomaticSpacingMm,
            material);
        if (inputError == RoofRafterRequestValidationError.None &&
            !TimberMaterialCatalog.TryGetItem(material!, out _))
        {
            inputError = RoofRafterRequestValidationError.InvalidMaterial;
        }
        RoofRafterRequestValidationResult validation;
        if (inputError != RoofRafterRequestValidationError.None)
        {
            validation = new RoofRafterRequestValidationResult(null, null, inputError);
            _currentHipPreviewLayout = null;
        }
        else if (_geometry is HipRoofGeometry hip)
        {
            var hipResult = RoofFaceRafterLayoutService.Create(hip.Topology, spacing);
            _currentHipPreviewLayout = hipResult.Layout;
            validation = new RoofRafterRequestValidationResult(
                null,
                null,
                hipResult.IsValid
                    ? RoofRafterRequestValidationError.None
                    : RoofRafterRequestValidationError.InvalidRoof);
        }
        else
        {
            _currentHipPreviewLayout = null;
            validation = RoofRafterRequestValidator.Validate(
                _geometry,
                width,
                height,
                spacing,
                _minimumAutomaticSpacingMm,
                material);
        }

        if (validation.IsValid &&
            !TimberMaterialCatalog.TryGetItem(validation.Request!.Material, out _))
        {
            validation = new RoofRafterRequestValidationResult(
                null,
                null,
                RoofRafterRequestValidationError.InvalidMaterial);
        }

        _currentValidation = validation;
        var hipPreviewOnly = _geometry is HipRoofGeometry &&
                             _currentHipPreviewLayout is not null;
        CreateButton.IsEnabled = validation.IsValid && !hipPreviewOnly;
        ValidationTextBlock.Text = validation.Error ==
                                   RoofRafterRequestValidationError.InvalidMaximumSpacing
            ? UiStrings.Format(
                UiStrings.GetString(
                    "RoofRafterWindow_InvalidAutomaticSpacingFormat",
                    _culture),
                _minimumAutomaticSpacingMm)
            : hipPreviewOnly
                ? UiStrings.GetString("RoofRafterWindow_HipPreviewOnly", _culture)
                : validation.IsValid
                    ? string.Empty
                    : UiStrings.GetString(ValidationKey(validation.Error), _culture);
        SummaryTextBlock.Text = _currentHipPreviewLayout is { } hipLayout
            ? UiStrings.Format(
                UiStrings.GetString("RoofRafterWindow_HipSummaryFormat", _culture),
                hipLayout.Segments.Count,
                hipLayout.RequestedSpacingMm)
            : validation.Layout is { } layout
            ? UiStrings.Format(
                UiStrings.GetString("RoofRafterWindow_SummaryFormat", _culture),
                layout.Rafters.Count,
                layout.StationCount,
                layout.ActualSpacingMm)
            : UiStrings.GetString("RoofRafterWindow_SummaryUnavailable", _culture);
        PreviewLayoutChanged?.Invoke(validation.Layout);
        HipPreviewLayoutChanged?.Invoke(_currentHipPreviewLayout);
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
