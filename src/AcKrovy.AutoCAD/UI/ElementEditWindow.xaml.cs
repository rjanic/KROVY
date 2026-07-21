using System.Globalization;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class ElementEditWindow : Window
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private readonly TimberElementDefaultProfile _defaultProfile;
    private readonly IReadOnlyList<TimberElementData> _validationData;
    private readonly bool _isNewAssignment;
    private readonly CultureInfo _uiCulture;
    private bool _isInitializing;
    private bool _manualLengthEditingEnabled;
    private readonly bool _usesFootprintPostSlopePresentation;

    internal TimberElementPatch? Patch { get; private set; }
    internal TimberElementType? SelectedElementType => (ElementTypeComboBox.SelectedItem as ElementTypeOption)?.Value;
    internal bool CuttingAllowanceWasEdited { get; private set; }
    internal bool UseDefaultCuttingAllowanceByType { get; private set; }

    public ElementEditWindow(
        TimberElementData? seedData,
        bool isNewAssignment,
        TimberElementDefaultProfile? defaultProfile = null,
        bool cuttingAllowanceIsMixed = false,
        bool slopeDirectionIsMixed = false,
        IReadOnlyList<TimberElementData>? validationData = null)
    {
        InitializeComponent();
        _isInitializing = true;
        _isNewAssignment = isNewAssignment;
        _uiCulture = AppLanguageService.CurrentUiCulture;
        _defaultProfile = (defaultProfile ?? TimberElementDefaultProfile.CreateDefault()).Normalize();

        ElementTypeComboBox.ItemsSource = Enum
            .GetValues<TimberElementType>()
            .Select(type => new ElementTypeOption(
                type,
                TimberElementTypeDisplayNameProvider.GetDisplayName(type, _uiCulture)))
            .ToList();

        LengthModeComboBox.ItemsSource = Enum
            .GetValues<LengthCalculationMode>()
            .Select(mode => new LengthModeOption(
                mode,
                LengthCalculationModeDisplayNameProvider.GetDisplayName(mode, _uiCulture)))
            .ToList();

        SlopeDirectionComboBox.ItemsSource = new[]
        {
            new SlopeDirectionOption(false, SlopeDirectionDisplayNameProvider.GetDisplayName(false, _uiCulture)),
            new SlopeDirectionOption(true, SlopeDirectionDisplayNameProvider.GetDisplayName(true, _uiCulture)),
        };

        var data = seedData ?? new TimberElementData();
        _validationData = validationData ?? new[] { data };
        _usesFootprintPostSlopePresentation =
            !TimberPostFootprintSlopeEditRules.CanEditSlope(_validationData);

        ElementTypeComboBox.SelectedItem = ((IEnumerable<ElementTypeOption>)ElementTypeComboBox.ItemsSource)
            .First(item => item.Value == data.ElementType);

        LengthModeComboBox.SelectedItem = ((IEnumerable<LengthModeOption>)LengthModeComboBox.ItemsSource)
            .First(item => item.Value == data.LengthCalculationMode);
        SlopeDirectionComboBox.SelectedItem = ((IEnumerable<SlopeDirectionOption>)SlopeDirectionComboBox.ItemsSource)
            .First(item => item.IsReversed == data.IsSlopeDirectionReversed);
        if (slopeDirectionIsMixed)
        {
            SlopeDirectionComboBox.ToolTip = GetUiString("EditWindow_SlopeDirectionMixedTooltip");
        }

        WidthTextBox.Text = Format(data.WidthMm);
        HeightTextBox.Text = Format(data.HeightMm);
        SlopeTextBox.Text = Format(TimberPostFootprintSlopeEditRules.ResolveDisplaySlopeDegrees(
            data,
            _validationData));
        RoofPlaneTextBox.Text = data.RoofPlaneId;
        AllowanceTextBox.Text = cuttingAllowanceIsMixed
            ? string.Empty
            : Format(data.CuttingAllowanceMm);
        if (cuttingAllowanceIsMixed)
        {
            AllowanceTextBox.ToolTip = GetUiString("EditWindow_CuttingAllowanceMixedTooltip");
        }
        ManualLengthTextBox.Text = data.ManualLengthMm is null
            ? string.Empty
            : Format(data.ManualLengthMm.Value);
        MaterialTextBox.Text = data.Material;

        ChangeTypeCheckBox.IsChecked = isNewAssignment;
        ChangeWidthCheckBox.IsChecked = isNewAssignment;
        ChangeHeightCheckBox.IsChecked = isNewAssignment;
        ChangeSlopeCheckBox.IsChecked = isNewAssignment;
        ChangeSlopeDirectionCheckBox.IsChecked = isNewAssignment;
        ChangeRoofPlaneCheckBox.IsChecked = isNewAssignment;
        ChangeAllowanceCheckBox.IsChecked = isNewAssignment;
        ChangeLengthModeCheckBox.IsChecked = isNewAssignment;
        ChangeManualLengthCheckBox.IsChecked = isNewAssignment;
        ChangeMaterialCheckBox.IsChecked = isNewAssignment;

        if (_usesFootprintPostSlopePresentation)
        {
            ChangeSlopeCheckBox.IsChecked = false;
            ChangeSlopeCheckBox.IsEnabled = false;
            SlopeTextBox.IsReadOnly = true;
            SlopeTextBox.IsEnabled = false;
            ChangeSlopeDirectionCheckBox.IsChecked = false;
            ChangeSlopeDirectionCheckBox.IsEnabled = false;
            SlopeDirectionComboBox.IsEnabled = false;
        }

        ElementTypeComboBox.SelectionChanged += (_, _) =>
        {
            UpdateAllowanceForSelectedType();
            UpdateManualLengthEditingState();
        };
        LengthModeComboBox.SelectionChanged += (_, _) => UpdateManualLengthEditingState();
        ChangeTypeCheckBox.Checked += (_, _) => UpdateManualLengthEditingState();
        ChangeTypeCheckBox.Unchecked += (_, _) => UpdateManualLengthEditingState();
        ChangeLengthModeCheckBox.Checked += (_, _) => UpdateManualLengthEditingState();
        ChangeLengthModeCheckBox.Unchecked += (_, _) => UpdateManualLengthEditingState();
        AllowanceTextBox.TextChanged += (_, _) =>
        {
            if (!_isInitializing)
            {
                CuttingAllowanceWasEdited = true;
                UseDefaultCuttingAllowanceByType = false;
            }
        };
        _isInitializing = false;
        UpdateManualLengthEditingState();
    }

    private void UpdateManualLengthEditingState()
    {
        var elementTypeOverride = ChangeTypeCheckBox.IsChecked == true
            ? (ElementTypeComboBox.SelectedItem as ElementTypeOption)?.Value
            : null;
        var lengthModeOverride = ChangeLengthModeCheckBox.IsChecked == true
            ? (LengthModeComboBox.SelectedItem as LengthModeOption)?.Value
            : null;

        _manualLengthEditingEnabled = TimberManualLengthEditRules.CanEdit(
            _validationData,
            elementTypeOverride,
            lengthModeOverride);
        ChangeManualLengthCheckBox.IsEnabled = _manualLengthEditingEnabled;
        ManualLengthTextBox.IsEnabled = _manualLengthEditingEnabled;
        ManualLengthTextBox.IsReadOnly = !_manualLengthEditingEnabled;
    }

    private void UpdateAllowanceForSelectedType()
    {
        if (!_isNewAssignment ||
            CuttingAllowanceWasEdited ||
            ChangeAllowanceCheckBox.IsChecked != true ||
            SelectedElementType is not { } type)
        {
            return;
        }

        _isInitializing = true;
        AllowanceTextBox.Text = Format(_defaultProfile.GetCuttingAllowanceMm(type));
        _isInitializing = false;
    }

    private void UseDefaultAllowance_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedElementType is not { } type)
        {
            return;
        }

        UseDefaultCuttingAllowanceByType = true;
        ChangeAllowanceCheckBox.IsChecked = false;
        _isInitializing = true;
        AllowanceTextBox.Text = Format(_defaultProfile.GetCuttingAllowanceMm(type));
        AllowanceTextBox.ToolTip = GetUiString("EditWindow_DefaultAllowanceTooltip");
        _isInitializing = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdateManualLengthEditingState();
        if (!TryReadOptionalNumber(
                ChangeWidthCheckBox.IsChecked == true,
                WidthTextBox.Text,
                GetUiString("Dialog_Edit_FieldWidth"),
                out var width) ||
            !TryReadOptionalNumber(
                ChangeHeightCheckBox.IsChecked == true,
                HeightTextBox.Text,
                GetUiString("Dialog_Edit_FieldHeight"),
                out var height) ||
            !TryReadOptionalSlope(
                !_usesFootprintPostSlopePresentation && ChangeSlopeCheckBox.IsChecked == true,
                SlopeTextBox.Text,
                out var slope) ||
            !TryReadOptionalWholeNumber(
                ChangeAllowanceCheckBox.IsChecked == true && !UseDefaultCuttingAllowanceByType,
                AllowanceTextBox.Text,
                GetUiString("Dialog_Edit_FieldCuttingAllowance"),
                out var allowance) ||
            !TryReadOptionalNumber(
                _manualLengthEditingEnabled && ChangeManualLengthCheckBox.IsChecked == true,
                ManualLengthTextBox.Text,
                GetUiString("Dialog_Edit_FieldManualLength"),
                out var manualLength,
                allowEmpty: true))
        {
            return;
        }

        var patch = new TimberElementPatch(
            ChangeTypeCheckBox.IsChecked == true
                ? (ElementTypeComboBox.SelectedItem as ElementTypeOption)?.Value
                : null,
            width,
            height,
            slope,
            ChangeRoofPlaneCheckBox.IsChecked == true
                ? EmptyToNull(RoofPlaneTextBox.Text)
                : null,
            UseDefaultCuttingAllowanceByType ? null : allowance,
            ChangeLengthModeCheckBox.IsChecked == true
                ? (LengthModeComboBox.SelectedItem as LengthModeOption)?.Value
                : null,
            manualLength,
            ChangeMaterialCheckBox.IsChecked == true
                ? EmptyToNull(MaterialTextBox.Text)
                : null,
            null,
            TimberPostFootprintSlopeEditRules.ResolveSlopeDirectionPatch(
                _validationData,
                ChangeSlopeDirectionCheckBox.IsChecked == true,
                (SlopeDirectionComboBox.SelectedItem as SlopeDirectionOption)?.IsReversed ?? false));

        foreach (var validationData in _validationData)
        {
            var candidate = TimberElementPatcher.Apply(validationData, patch);
            if (TimberCalculator.TryValidateSlopeDegrees(candidate.SlopeDegrees, out _))
            {
                continue;
            }

            MessageBox.Show(
                GetUiString("Error_InvalidSlopeDegrees"),
                GetUiString("Message_DialogTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Patch = patch;
        DialogResult = true;
    }

    private bool TryReadOptionalSlope(bool shouldRead, string raw, out double? result)
    {
        result = null;
        if (!shouldRead)
        {
            return true;
        }

        var parsed = double.TryParse(raw, NumberStyles.Float, SlovakCulture, out var value) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (parsed &&
            TimberCalculator.TryValidateSlopeDegrees(value, out _))
        {
            result = value;
            return true;
        }

        TimberCalculator.TryValidateSlopeDegrees(parsed ? value : double.NaN, out _);
        MessageBox.Show(
            GetUiString("Error_InvalidSlopeDegrees"),
            GetUiString("Message_DialogTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private bool TryReadOptionalWholeNumber(
        bool shouldRead,
        string raw,
        string label,
        out double? result)
    {
        result = null;

        if (!shouldRead)
        {
            return true;
        }

        if (double.TryParse(raw, NumberStyles.Float, SlovakCulture, out var value) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            var rounded = Math.Round(value);
            if (value >= 0 &&
                value <= TimberElementDefaultProfile.MaxCuttingAllowanceMm &&
                Math.Abs(value - rounded) < 0.000001)
            {
                result = rounded;
                return true;
            }
        }

        MessageBox.Show(
            UiStrings.Format(
                GetUiString("Dialog_Edit_WholeNonnegativeFormat"),
                label,
                TimberElementDefaultProfile.MaxCuttingAllowanceMm),
            GetUiString("Message_DialogTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private bool TryReadOptionalNumber(
        bool shouldRead,
        string raw,
        string label,
        out double? result,
        bool allowEmpty = false)
    {
        result = null;

        if (!shouldRead)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(raw) && allowEmpty)
        {
            return true;
        }

        if (double.TryParse(raw, NumberStyles.Float, SlovakCulture, out var value) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            if (value > 0)
            {
                result = value;
                return true;
            }
        }

        MessageBox.Show(
            UiStrings.Format(GetUiString("Dialog_Edit_PositiveNumberFormat"), label),
            GetUiString("Message_DialogTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private static string Format(double value) =>
        value.ToString("0.###", SlovakCulture);

    private string GetUiString(string resourceKey) =>
        UiStrings.GetString(resourceKey, _uiCulture);

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record ElementTypeOption(TimberElementType Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record LengthModeOption(LengthCalculationMode Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SlopeDirectionOption(bool IsReversed, string Label)
    {
        public override string ToString() => Label;
    }

}
