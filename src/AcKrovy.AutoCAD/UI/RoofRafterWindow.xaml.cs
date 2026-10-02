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
    private readonly double _minimumAutomaticLengthMm;
    private readonly Func<double, double, double, double, double?>? _automaticHeightResolver;
    private bool _structuralAutomaticMode;
    private bool _updatingStructuralHeightText;
    private double? _resolvedAutomaticHeight;
    private RoofRafterRequestValidationResult? _currentValidation;
    private RoofFaceRafterLayout? _currentHipPreviewLayout;
    private bool _initialized;

    internal RoofRafterWindow(
        IRoofGeometry geometry,
        RoofRafterPreferences preferences,
        double minimumAutomaticSpacingMm,
        SettingsTheme theme,
        CultureInfo? culture = null,
        double minimumAutomaticLengthMm =
            RoofRafterLengthRules.DefaultMinimumAutomaticLengthMm,
        RoofAutomaticRafterPhysicalSettings? physicalSettings = null,
        RoofPhysicalElevationData? structuralSettings = null,
        Func<double, double, double, double, double?>? automaticHeightResolver = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!RoofRafterSpacingRules.IsValidDimension(minimumAutomaticSpacingMm))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAutomaticSpacingMm));
        }
        if (!RoofRafterLengthRules.IsValidMinimumLength(minimumAutomaticLengthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAutomaticLengthMm));
        }

        InitializeComponent();
        FashionWindowTheme.Apply(this, theme);
        _geometry = geometry;
        _minimumAutomaticSpacingMm = minimumAutomaticSpacingMm;
        _minimumAutomaticLengthMm = minimumAutomaticLengthMm;
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _automaticHeightResolver = automaticHeightResolver;
        _structuralAutomaticMode = structuralSettings?.StructuralHeightMode !=
            RoofStructuralHeightMode.Explicit;
        MaterialOptions = TimberMaterialDisplayNameProvider.GetOptions(
            preferences.Material,
            _culture);
        MaterialComboBox.ItemsSource = MaterialOptions;
        MaterialComboBox.SelectedItem = MaterialOptions.FirstOrDefault(item =>
            string.Equals(item.StoredValue, preferences.Material, StringComparison.Ordinal));
        WidthTextBox.Text = FormatInput(preferences.WidthMm);
        HeightTextBox.Text = FormatInput(preferences.HeightMm);
        MaximumSpacingTextBox.Text = FormatInput(preferences.MaximumSpacingMm);
        StructuralSection.Visibility = geometry is HipRoofGeometry
            ? Visibility.Visible : Visibility.Collapsed;
        Physical3DSection.Visibility =
            geometry is HipRoofGeometry ||
            geometry is SimpleGableRoofGeometry { Kind: RoofKind.SimpleGable }
                ? Visibility.Visible
                : Visibility.Collapsed;
        // Absent store => OFF (MissingStoreDefault). New SimpleGable create seeds ON.
        Physical3DEnabledCheckBox.IsChecked =
            structuralSettings?.Physical3DEnabled == true;
        StructuralWidthTextBox.Text = FormatInput(
            structuralSettings?.StructuralWidthMm ??
                RoofStructuralPhysicalSettings.DefaultWidthMm);
        StructuralHeightTextBox.Text = !_structuralAutomaticMode
            ? FormatInput(structuralSettings?.StructuralExplicitHeightMm ?? 0d)
            : string.Empty;
        var cutMode = physicalSettings?.LowerEndCutMode ?? LowerEndCutMode.Vertical;
        var joinMode = physicalSettings?.RidgeJoinMode ?? RidgeJoinMode.Meet;
        LowerEndCutComboBox.SelectedItem = LowerEndCutComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag as string, cutMode.ToString(),
                StringComparison.Ordinal));
        RidgeJoinComboBox.SelectedItem = RidgeJoinComboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag as string, joinMode.ToString(),
                StringComparison.Ordinal));
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

    internal RoofRafterLayout? PreviewLayout =>
        _geometry is HipRoofGeometry ? null : _currentValidation?.Layout;

    internal RoofFaceRafterLayout? HipPreviewLayout => _currentHipPreviewLayout;

    internal event Action<RoofRafterLayout?>? PreviewLayoutChanged;
    internal event Action<RoofFaceRafterLayout?>? HipPreviewLayoutChanged;

    private string FormatInput(double value) => value.ToString("0.###", _culture);

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingStructuralHeightText) return;
        if (ReferenceEquals(sender, StructuralHeightTextBox) &&
            _initialized)
            _structuralAutomaticMode = false;
        UpdateValidationAndSummary();
    }

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
            validation = RoofRafterRequestValidator.Validate(
                hip,
                width,
                height,
                spacing,
                _minimumAutomaticSpacingMm,
                material,
                _minimumAutomaticLengthMm);
            _currentHipPreviewLayout = validation.IsValid
                ? RoofFaceRafterLayoutService.Create(hip.Topology, spacing).Layout
                : null;
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
                material,
                _minimumAutomaticLengthMm);
        }

        if (validation.IsValid &&
            !TimberMaterialCatalog.TryGetItem(validation.Request!.Material, out _))
        {
            validation = new RoofRafterRequestValidationResult(
                null,
                null,
                RoofRafterRequestValidationError.InvalidMaterial);
            _currentHipPreviewLayout = null;
        }

        _currentValidation = validation;
        if (_geometry is HipRoofGeometry && _structuralAutomaticMode)
        {
            var structuralWidth = ParseNumber(StructuralWidthTextBox.Text);
            _resolvedAutomaticHeight = validation.IsValid &&
                !double.IsNaN(structuralWidth) && structuralWidth > 0d
                ? _automaticHeightResolver?.Invoke(width, height, spacing, structuralWidth)
                : null;
            _updatingStructuralHeightText = true;
            try
            {
                StructuralHeightTextBox.Text = _resolvedAutomaticHeight is { } resolved &&
                    double.IsFinite(resolved) && resolved > 0d
                    ? resolved.ToString("0.0", _culture) : string.Empty;
            }
            finally { _updatingStructuralHeightText = false; }
        }
        var structuralValid = TryReadStructuralSettings(out _, out _, out _);
        CreateButton.IsEnabled = validation.IsValid && structuralValid;
        ValidationTextBlock.Text = validation.IsValid && !structuralValid
            ? UiStrings.GetString("RoofRafterWindow_InvalidStructuralDimensions", _culture)
            : validation.Error ==
                                   RoofRafterRequestValidationError.InvalidMaximumSpacing
            ? UiStrings.Format(
                UiStrings.GetString(
                    "RoofRafterWindow_InvalidAutomaticSpacingFormat",
                    _culture),
                _minimumAutomaticSpacingMm)
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
        PreviewLayoutChanged?.Invoke(PreviewLayout);
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

    private bool TryReadStructuralSettings(out double width,
        out RoofStructuralHeightMode heightMode, out double explicitHeight)
    {
        width = RoofStructuralPhysicalSettings.DefaultWidthMm;
        heightMode = RoofStructuralHeightMode.Automatic;
        explicitHeight = 0d;
        if (_geometry is not HipRoofGeometry) return true;
        width = ParseNumber(StructuralWidthTextBox.Text);
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0d)
            return false;
        if (_structuralAutomaticMode)
            return _resolvedAutomaticHeight is { } resolved &&
                double.IsFinite(resolved) && resolved > 0d;
        var text = StructuralHeightTextBox.Text.Trim();
        explicitHeight = ParseNumber(text);
        if (double.IsNaN(explicitHeight) || double.IsInfinity(explicitHeight) ||
            explicitHeight <= 0d)
            return false;
        heightMode = RoofStructuralHeightMode.Explicit;
        return true;
    }

    private void StructuralAutomaticButton_Click(object sender, RoutedEventArgs e)
    {
        _structuralAutomaticMode = true;
        UpdateValidationAndSummary();
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
        if (_currentValidation?.IsValid != true ||
            !TryReadStructuralSettings(out var structuralWidth,
                out var structuralHeightMode, out var explicitHeight))
        {
            return;
        }

        var cut = (LowerEndCutComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var join = (RidgeJoinComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (!Enum.TryParse(cut, out LowerEndCutMode cutMode) ||
            !Enum.TryParse(join, out RidgeJoinMode joinMode))
        {
            return;
        }
        Request = _currentValidation.Request! with
        {
            LowerEndCutMode = cutMode,
            RidgeJoinMode = joinMode,
            StructuralWidthMm = structuralWidth,
            StructuralHeightMode = structuralHeightMode,
            StructuralExplicitHeightMm = explicitHeight,
            Physical3DEnabled = Physical3DSection.Visibility == Visibility.Visible
                ? Physical3DEnabledCheckBox.IsChecked == true
                : null,
        };
        DialogResult = true;
    }
}
