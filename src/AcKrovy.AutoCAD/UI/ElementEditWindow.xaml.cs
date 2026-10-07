using System.Globalization;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;

using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;

namespace AcKrovy.AutoCAD.UI;

public partial class ElementEditWindow : Window
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private readonly TimberElementDefaultProfile _defaultProfile;
    private readonly IReadOnlyList<TimberElementData> _validationData;
    private readonly bool _isNewAssignment;
    private readonly CultureInfo _uiCulture;
    private readonly string _originalStoredMaterial;
    private readonly CustomElementDefinition? _originalCustomDefinition;
    private bool _isInitializing;
    private bool _manualLengthEditingEnabled;
    private readonly bool _usesFootprintPostSlopePresentation;

    private readonly RoofOrdinaryElevationViewModel? _elevationVm;

    internal TimberElementPatch? Patch { get; private set; }
    internal RoofOrdinaryElevationViewModel? ElevationViewModel => _elevationVm;
    internal StructuralMemberElevationState? RequestedElevationState => _elevationVm?.BuildRequestedState();
    internal bool ElevationGeometryChanged => _elevationVm?.GeometryChanged ?? false;
    internal bool ElevationDisplayReferenceChanged => _elevationVm?.DisplayReferenceChanged ?? false;

    internal TimberElementType? SelectedElementType => (ElementTypeComboBox.SelectedItem as ElementTypeOption)?.Value;
    internal bool CuttingAllowanceWasEdited { get; private set; }
    internal bool UseDefaultCuttingAllowanceByType { get; private set; }
    internal CustomElementDefinition? RenamedCustomDefinition { get; private set; }
    internal event Action<string>? CustomDefinitionNameChanged;

    public ElementEditWindow(
        TimberElementData? seedData,
        bool isNewAssignment,
        TimberElementDefaultProfile? defaultProfile = null,
        bool cuttingAllowanceIsMixed = false,
        bool slopeDirectionIsMixed = false,
        IReadOnlyList<TimberElementData>? validationData = null,
        StructuralMemberElevationState? elevationState = null,
        double? planLengthMm = null)
    {
        InitializeComponent();
        _isInitializing = true;
        _isNewAssignment = isNewAssignment;
        _uiCulture = AppLanguageService.CurrentUiCulture;
        _defaultProfile = (defaultProfile ?? TimberElementDefaultProfile.CreateDefault()).Normalize();

        var data = seedData ?? new TimberElementData();
        _validationData = validationData ?? new[] { data };

        // Initialize Elevation seating section if state is provided (single selection only).
        if (elevationState is not null && planLengthMm is { } len && _validationData.Count == 1)
        {
            _elevationVm = new RoofOrdinaryElevationViewModel(
                elevationState,
                len,
                data.HeightMm,
                _uiCulture,
                elementType: data.ElementType);
            InitializeElevationSection();
        }
        else
        {
            ElevationSection.Visibility = Visibility.Collapsed;
        }

        _originalCustomDefinition = ResolveRenameableCustomDefinition(_validationData);
        ElementTypeComboBox.ItemsSource = Enum
            .GetValues<TimberElementType>()
            .Where(type => type != TimberElementType.Custom || data.ElementType == TimberElementType.Custom)
            .Select(type => new ElementTypeOption(
                type,
                type == TimberElementType.Custom
                    ? TimberElementDisplayNameProvider.GetDisplayName(data, _uiCulture)
                    : TimberElementTypeDisplayNameProvider.GetDisplayName(type, _uiCulture)))
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

        _originalStoredMaterial = data.Material;
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
        MaterialComboBox.ItemsSource = TimberMaterialDisplayNameProvider.GetOptions(
            data.Material,
            _uiCulture);
        MaterialComboBox.SelectedItem =
            ((IEnumerable<TimberMaterialDisplayOption>)MaterialComboBox.ItemsSource)
            .First(option => string.Equals(
                option.StoredValue,
                data.Material,
                StringComparison.Ordinal));

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

        if (data.ElementType == TimberElementType.Custom)
        {
            ChangeTypeCheckBox.IsChecked = false;
            ChangeTypeCheckBox.IsEnabled = false;
            ElementTypeComboBox.IsEnabled = false;
            RenameCustomDefinitionButton.Visibility =
                _originalCustomDefinition is null
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

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
        MaterialComboBox.SelectionChanged += (_, _) =>
        {
            if (TimberMaterialEditRules.ShouldActivateApplyFlag(
                    _isInitializing,
                    (MaterialComboBox.SelectedItem as TimberMaterialDisplayOption)?.StoredValue,
                    _originalStoredMaterial))
            {
                ChangeMaterialCheckBox.IsChecked = true;
            }
        };
        _isInitializing = false;
        UpdateManualLengthEditingState();
    }

    private void RenameCustomDefinition_Click(object sender, RoutedEventArgs e)
    {
        if (_originalCustomDefinition is null)
        {
            return;
        }

        var currentDefinition =
            RenamedCustomDefinition ?? _originalCustomDefinition;
        var dialog = new CustomElementDefinitionRenameWindow(currentDefinition)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true ||
            dialog.RenamedDefinition is not { } renamed)
        {
            return;
        }

        RenamedCustomDefinition =
            CustomElementDefinitionRenameRules.HasChanged(
                _originalCustomDefinition,
                renamed)
                ? renamed
                : null;

        var displayedDefinition =
            RenamedCustomDefinition ?? _originalCustomDefinition;
        var options = ((IEnumerable<ElementTypeOption>)ElementTypeComboBox.ItemsSource)
            .Select(option => option.Value == TimberElementType.Custom
                ? new ElementTypeOption(option.Value, displayedDefinition.Name)
                : option)
            .ToList();
        ElementTypeComboBox.ItemsSource = options;
        ElementTypeComboBox.SelectedItem =
            options.First(option => option.Value == TimberElementType.Custom);
        CustomDefinitionNameChanged?.Invoke(displayedDefinition.Name);
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
                !_usesFootprintPostSlopePresentation &&
                (ChangeSlopeCheckBox.IsChecked == true || (_elevationVm?.GeometryChanged ?? false)),
                ResolveSlopeTextForPatch(),
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
            TimberMaterialEditRules.ResolvePatchValue(
                ChangeMaterialCheckBox.IsChecked == true,
                (MaterialComboBox.SelectedItem as TimberMaterialDisplayOption)?.StoredValue),
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

    private static CustomElementDefinition? ResolveRenameableCustomDefinition(
        IReadOnlyList<TimberElementData> data)
    {
        if (data.Count == 0 ||
            !CustomElementDefinitionRules.TryFromElementData(
                data[0],
                out var definition) ||
            definition is null ||
            data.Any(item =>
                item.ElementType != TimberElementType.Custom ||
                !string.Equals(
                    item.CustomElementTypeId,
                    definition.Id,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return definition;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private string ResolveSlopeTextForPatch()
    {
        // One slope value: when elevation is active, AbsoluteSlopeDegrees is authoritative.
        if (_elevationVm is not null && ElevationSection.Visibility == Visibility.Visible)
            return Format(_elevationVm.AbsoluteSlopeDegrees);
        return SlopeTextBox.Text;
    }

    private void MainSlopeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_elevationVm is null ||
            ElevationSection.Visibility != Visibility.Visible ||
            !_elevationVm.SlopeEditable)
            return;

        if (!double.TryParse(SlopeTextBox.Text, NumberStyles.Float, SlovakCulture, out var absolute) &&
            !double.TryParse(SlopeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out absolute))
            return;

        // Slope is always a positive magnitude; fall direction is separate (Smer spádu).
        absolute = Math.Abs(absolute);
        _elevationVm.SlopeText = StructuralMemberElevationRules.FormatSlopeDegrees(absolute, _uiCulture);
        ChangeSlopeCheckBox.IsChecked = true;
    }

    private void ElevationImageChoice_Click(object sender, RoutedEventArgs e)
    {
        if (_elevationVm is null) return;
        if (sender is not System.Windows.Controls.Button button) return;
        if (button.DataContext is not ElevationImageChoiceItem item) return;

        if (string.Equals(button.Tag as string, "Reference", StringComparison.Ordinal))
            _elevationVm.SelectReferenceChoice(item);
        else if (string.Equals(button.Tag as string, "Calculation", StringComparison.Ordinal))
            _elevationVm.SelectCalculationChoice(item);

        ApplyElevationFieldEditability();
        SyncMainSlopeFromElevation();
    }

    private void InitializeElevationSection()
    {
        if (_elevationVm is null) return;

        ElevationSection.Visibility = Visibility.Visible;
        ElevationSection.DataContext = _elevationVm;

        // Main Sklon [°] stays visible as the element parameter.
        // Elevation "Previazaný sklon" is always a linked read-only mirror (not a second editor).

        LowerZTextBox.SetBinding(System.Windows.Controls.TextBox.TextProperty,
            new System.Windows.Data.Binding(nameof(RoofOrdinaryElevationViewModel.LowerZText))
            {
                Source = _elevationVm,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus,
            });
        UpperZTextBox.SetBinding(System.Windows.Controls.TextBox.TextProperty,
            new System.Windows.Data.Binding(nameof(RoofOrdinaryElevationViewModel.UpperZText))
            {
                Source = _elevationVm,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus,
            });
        SlopeElevationTextBox.SetBinding(System.Windows.Controls.TextBox.TextProperty,
            new System.Windows.Data.Binding(nameof(RoofOrdinaryElevationViewModel.SlopeText))
            {
                Source = _elevationVm,
                Mode = System.Windows.Data.BindingMode.OneWay,
            });

        _elevationVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is
                nameof(RoofOrdinaryElevationViewModel.LowerZEditable) or
                nameof(RoofOrdinaryElevationViewModel.UpperZEditable) or
                nameof(RoofOrdinaryElevationViewModel.SlopeEditable))
            {
                ApplyElevationFieldEditability();
            }

            if (e.PropertyName is
                nameof(RoofOrdinaryElevationViewModel.SlopeText) or
                nameof(RoofOrdinaryElevationViewModel.AbsoluteSlopeDegrees) or
                nameof(RoofOrdinaryElevationViewModel.GeometryChanged))
            {
                SyncMainSlopeFromElevation();
            }

            if (e.PropertyName is nameof(RoofOrdinaryElevationViewModel.IsSlopeDirectionReversed) or
                nameof(RoofOrdinaryElevationViewModel.IsHorizontal))
            {
                SyncSlopeDirectionFromElevation();
            }
        };

        ApplyElevationFieldEditability();
        SyncMainSlopeFromElevation();
    }

    private void ApplyElevationFieldEditability()
    {
        if (_elevationVm is null) return;
        LowerZTextBox.IsReadOnly = !_elevationVm.LowerZEditable;
        UpperZTextBox.IsReadOnly = !_elevationVm.UpperZEditable;
        // Elevation slope row is always a linked display — never a second independent editor.
        SlopeElevationTextBox.IsReadOnly = true;
        // Main Sklon [°] is editable only in Mode B/C (LowerSlope / UpperSlope).
        if (!_usesFootprintPostSlopePresentation)
        {
            SlopeTextBox.IsReadOnly = !_elevationVm.SlopeEditable;
            SlopeTextBox.IsEnabled = true;
            ChangeSlopeCheckBox.IsEnabled = _elevationVm.SlopeEditable;
            if (_elevationVm.SlopeEditable)
                ChangeSlopeCheckBox.IsChecked = true;
        }
    }

    private void SyncMainSlopeFromElevation()
    {
        if (_elevationVm is null) return;
        // Keep the main element slope field mirrored to the same underlying elevation math.
        if (!SlopeTextBox.IsKeyboardFocusWithin)
            SlopeTextBox.Text = Format(_elevationVm.AbsoluteSlopeDegrees);
        if (_elevationVm.GeometryChanged)
            ChangeSlopeCheckBox.IsChecked = true;
    }

    private void SyncSlopeDirectionFromElevation()
    {
        if (_elevationVm is null || SlopeDirectionComboBox.ItemsSource is null) return;
        // Horizontal: fall has no meaning — leave combo alone (do not invent a direction).
        if (_elevationVm.IsHorizontal) return;
        var target = _elevationVm.IsSlopeDirectionReversed;
        var match = ((IEnumerable<SlopeDirectionOption>)SlopeDirectionComboBox.ItemsSource)
            .FirstOrDefault(item => item.IsReversed == target);
        if (match is not null && !Equals(SlopeDirectionComboBox.SelectedItem, match))
        {
            SlopeDirectionComboBox.SelectedItem = match;
            ChangeSlopeDirectionCheckBox.IsChecked = true;
        }
    }

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
