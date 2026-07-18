using System.Globalization;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.UI;

public partial class ElementEditWindow : Window
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private readonly TimberElementDefaultProfile _defaultProfile;
    private readonly bool _isNewAssignment;
    private bool _isInitializing;

    internal TimberElementPatch? Patch { get; private set; }
    internal TimberElementType? SelectedElementType => (ElementTypeComboBox.SelectedItem as ElementTypeOption)?.Value;
    internal bool CuttingAllowanceWasEdited { get; private set; }
    internal bool UseDefaultCuttingAllowanceByType { get; private set; }

    public ElementEditWindow(
        TimberElementData? seedData,
        bool isNewAssignment,
        TimberElementDefaultProfile? defaultProfile = null,
        bool cuttingAllowanceIsMixed = false)
    {
        InitializeComponent();
        _isInitializing = true;
        _isNewAssignment = isNewAssignment;
        _defaultProfile = (defaultProfile ?? TimberElementDefaultProfile.CreateDefault()).Normalize();

        ElementTypeComboBox.ItemsSource = Enum
            .GetValues<TimberElementType>()
            .Select(type => new ElementTypeOption(type, TimberElementLabels.ToSlovak(type)))
            .ToList();

        LengthModeComboBox.ItemsSource = Enum
            .GetValues<LengthCalculationMode>()
            .Select(mode => new LengthModeOption(mode, ToSlovak(mode)))
            .ToList();

        var data = seedData ?? new TimberElementData();

        ElementTypeComboBox.SelectedItem = ((IEnumerable<ElementTypeOption>)ElementTypeComboBox.ItemsSource)
            .First(item => item.Value == data.ElementType);

        LengthModeComboBox.SelectedItem = ((IEnumerable<LengthModeOption>)LengthModeComboBox.ItemsSource)
            .First(item => item.Value == data.LengthCalculationMode);

        WidthTextBox.Text = Format(data.WidthMm);
        HeightTextBox.Text = Format(data.HeightMm);
        SlopeTextBox.Text = Format(data.SlopeDegrees);
        RoofPlaneTextBox.Text = data.RoofPlaneId;
        AllowanceTextBox.Text = cuttingAllowanceIsMixed
            ? string.Empty
            : Format(data.CuttingAllowanceMm);
        if (cuttingAllowanceIsMixed)
        {
            AllowanceTextBox.ToolTip = "Vybrané prvky majú rôzne výrobné prídavky. Nezaškrtnuté pole ponechá každému prvku pôvodnú hodnotu.";
        }
        ManualLengthTextBox.Text = data.ManualLengthMm is null
            ? string.Empty
            : Format(data.ManualLengthMm.Value);
        MaterialTextBox.Text = data.Material;

        ChangeTypeCheckBox.IsChecked = isNewAssignment;
        ChangeWidthCheckBox.IsChecked = isNewAssignment;
        ChangeHeightCheckBox.IsChecked = isNewAssignment;
        ChangeSlopeCheckBox.IsChecked = isNewAssignment;
        ChangeRoofPlaneCheckBox.IsChecked = isNewAssignment;
        ChangeAllowanceCheckBox.IsChecked = isNewAssignment;
        ChangeLengthModeCheckBox.IsChecked = isNewAssignment;
        ChangeManualLengthCheckBox.IsChecked = isNewAssignment;
        ChangeMaterialCheckBox.IsChecked = isNewAssignment;

        ElementTypeComboBox.SelectionChanged += (_, _) => UpdateAllowanceForSelectedType();
        AllowanceTextBox.TextChanged += (_, _) =>
        {
            if (!_isInitializing)
            {
                CuttingAllowanceWasEdited = true;
                UseDefaultCuttingAllowanceByType = false;
            }
        };
        _isInitializing = false;
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
        UseDefaultCuttingAllowanceByType = true;
        ChangeAllowanceCheckBox.IsChecked = false;
        _isInitializing = true;
        AllowanceTextBox.Text = string.Empty;
        AllowanceTextBox.ToolTip = "Pri použití sa každému vybranému prvku nastaví aktuálny predvolený prídavok podľa jeho typu.";
        _isInitializing = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadOptionalNumber(
                ChangeWidthCheckBox.IsChecked == true,
                WidthTextBox.Text,
                "šírka",
                out var width) ||
            !TryReadOptionalNumber(
                ChangeHeightCheckBox.IsChecked == true,
                HeightTextBox.Text,
                "výška",
                out var height) ||
            !TryReadOptionalNumber(
                ChangeSlopeCheckBox.IsChecked == true,
                SlopeTextBox.Text,
                "sklon",
                out var slope,
                allowZero: true) ||
            !TryReadOptionalWholeNumber(
                ChangeAllowanceCheckBox.IsChecked == true && !UseDefaultCuttingAllowanceByType,
                AllowanceTextBox.Text,
                "výrobný prídavok",
                out var allowance) ||
            !TryReadOptionalNumber(
                ChangeManualLengthCheckBox.IsChecked == true,
                ManualLengthTextBox.Text,
                "ručná dĺžka",
                out var manualLength,
                allowEmpty: true))
        {
            return;
        }

        Patch = new TimberElementPatch(
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
            null);

        DialogResult = true;
    }

    private static bool TryReadOptionalWholeNumber(
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
            if (value >= 0 && Math.Abs(value - rounded) < 0.000001)
            {
                result = rounded;
                return true;
            }
        }

        MessageBox.Show(
            $"Pole „{label}“ musí obsahovať celé nezáporné číslo v milimetroch.",
            "ACAD KROVY",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private static bool TryReadOptionalNumber(
        bool shouldRead,
        string raw,
        string label,
        out double? result,
        bool allowEmpty = false,
        bool allowZero = false)
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
            if (allowZero ? value >= 0 : value > 0)
            {
                result = value;
                return true;
            }
        }

        var condition = allowZero ? "nezáporné číslo" : "kladné číslo";

        MessageBox.Show(
            $"Pole „{label}“ musí obsahovať {condition}.",
            "ACAD KROVY",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private static string Format(double value) =>
        value.ToString("0.###", SlovakCulture);

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

    private static string ToSlovak(LengthCalculationMode mode) =>
        mode switch
        {
            LengthCalculationMode.AutoByElementType => "Automaticky podľa typu",
            LengthCalculationMode.PlanLength => "Pôdorysná dĺžka",
            LengthCalculationMode.SlopeCorrected => "Prepočítať podľa sklonu",
            LengthCalculationMode.ManualLength => "Ručne zadaná dĺžka",
            _ => mode.ToString(),
        };
}
