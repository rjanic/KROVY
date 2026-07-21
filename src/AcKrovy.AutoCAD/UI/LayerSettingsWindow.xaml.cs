using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class LayerSettingsWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private string _roundingStepMmText = Format(TimberElementDefaultProfile.FactoryCuttingLengthRoundingStepMm);
    private string _selectedLanguageCode = AppLanguageService.DefaultLanguageCode;

    public ObservableCollection<LayerSettingsRow> Rows { get; } = [];
    public ObservableCollection<ElementDefaultSettingsRow> DefaultRows { get; } = [];
    public IReadOnlyList<LayerColorOption> ColorOptions { get; } = LayerColorOption.CreateDefaults();
    public IReadOnlyList<SupportedAppLanguage> LanguageOptions => AppLanguageService.SupportedLanguages;

    internal ElementLayerProfile? Profile { get; private set; }
    internal TimberElementDefaultProfile? DefaultProfile { get; private set; }
    internal bool ApplyToExistingElements { get; private set; }
    internal SettingsSaveMode SaveMode { get; private set; }
    internal string LanguageCode { get; private set; } = AppLanguageService.DefaultLanguageCode;

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            var normalized = AppLanguageService.NormalizeLanguageCode(value);
            if (string.Equals(_selectedLanguageCode, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _selectedLanguageCode = normalized;
            OnPropertyChanged();
        }
    }

    public string RoundingStepMmText
    {
        get => _roundingStepMmText;
        set
        {
            if (_roundingStepMmText == value)
            {
                return;
            }

            _roundingStepMmText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    internal LayerSettingsWindow(
        ElementLayerProfile profile,
        TimberElementDefaultProfile defaultProfile,
        string languageCode)
    {
        _selectedLanguageCode = AppLanguageService.NormalizeLanguageCode(languageCode);
        InitializeComponent();
        DataContext = this;
        SettingsTabControl.SelectionChanged += SettingsTabControl_SelectionChanged;
        UpdateActionButtons();
        ReplaceRows(profile.Normalize());
        ReplaceDefaultRows(defaultProfile.Normalize());
        StylesDataGrid.ItemsSource = Rows;
        DefaultsDataGrid.ItemsSource = DefaultRows;
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        ReplaceRows(ElementLayerProfile.CreateDefault());
        ReplaceDefaultRows(TimberElementDefaultProfile.CreateDefault());
    }

    private void SaveAndApply_Click(object sender, RoutedEventArgs e)
    {
        Save(applyToExistingElements: true, SettingsSaveMode.AllElements);
    }

    private void SaveAndApplySelected_Click(object sender, RoutedEventArgs e)
    {
        Save(applyToExistingElements: true, SettingsSaveMode.SelectedElements);
    }

    private void SaveNewElementsOnly_Click(object sender, RoutedEventArgs e)
    {
        Save(applyToExistingElements: false, SettingsSaveMode.NewElementsOnly);
    }

    private void SaveLanguage_Click(object sender, RoutedEventArgs e)
    {
        LanguageCode = AppLanguageService.NormalizeLanguageCode(SelectedLanguageCode);
        ApplyToExistingElements = false;
        SaveMode = SettingsSaveMode.LanguageOnly;
        DialogResult = true;
    }

    private void Save(bool applyToExistingElements, SettingsSaveMode saveMode)
    {
        StylesDataGrid.CommitEdit();
        StylesDataGrid.CommitEdit();
        DefaultsDataGrid.CommitEdit();
        DefaultsDataGrid.CommitEdit();

        if (!TryBuildProfile(out var profile) ||
            !TryBuildDefaultProfile(out var defaultProfile))
        {
            return;
        }

        Profile = profile;
        DefaultProfile = defaultProfile;
        ApplyToExistingElements = applyToExistingElements;
        SaveMode = saveMode;
        LanguageCode = AppLanguageService.NormalizeLanguageCode(SelectedLanguageCode);
        DialogResult = true;
    }

    private bool TryBuildProfile(out ElementLayerProfile profile)
    {
        var styles = new List<ElementLayerStyle>();
        var occupiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows)
        {
            if (!LayerNameValidator.TryValidate(row.LayerName, out var layerName, out var error))
            {
                WpfMessageBox.Show(
                    UiStrings.Format(UiStrings.DialogLayersErrorFormat, row.ElementLabel, error),
                    UiStrings.MessageDialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                profile = ElementLayerProfile.CreateDefault();
                return false;
            }

            if (!occupiedNames.Add(layerName))
            {
                WpfMessageBox.Show(
                    UiStrings.Format(UiStrings.DialogLayersDuplicateFormat, layerName),
                    UiStrings.MessageDialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                profile = ElementLayerProfile.CreateDefault();
                return false;
            }

            styles.Add(new ElementLayerStyle(row.ElementType, layerName, row.SelectedColor.Index));
        }

        profile = new ElementLayerProfile
        {
            Styles = styles,
        };
        return true;
    }

    private bool TryBuildDefaultProfile(out TimberElementDefaultProfile profile)
    {
        if (!TryReadPositiveInteger(RoundingStepMmText, out var roundingStepMm))
        {
            WpfMessageBox.Show(
                UiStrings.Format(
                    UiStrings.DialogSettingsRoundingStepFormat,
                    TimberElementDefaultProfile.MaxCuttingLengthRoundingStepMm),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            profile = TimberElementDefaultProfile.CreateDefault();
            return false;
        }

        var styles = new List<TimberElementDefaultStyle>();

        foreach (var row in DefaultRows)
        {
            if (!TryReadNonNegativeNumber(row.CuttingAllowanceMmText, out var cuttingAllowanceMm))
            {
                WpfMessageBox.Show(
                    UiStrings.Format(
                        UiStrings.DialogSettingsCuttingAllowanceFormat,
                        row.ElementLabel,
                        TimberElementDefaultProfile.MaxCuttingAllowanceMm),
                    UiStrings.MessageDialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                profile = TimberElementDefaultProfile.CreateDefault();
                return false;
            }

            styles.Add(new TimberElementDefaultStyle(row.ElementType, cuttingAllowanceMm));
        }

        profile = new TimberElementDefaultProfile
        {
            CuttingLengthRoundingStepMm = roundingStepMm,
            Styles = styles,
        }.Normalize();
        return true;
    }

    private void ReplaceRows(ElementLayerProfile profile)
    {
        Rows.Clear();
        foreach (var type in Enum.GetValues<TimberElementType>())
        {
            var style = profile.GetStyle(type);
            var color = ColorOptions.FirstOrDefault(option => option.Index == style.ColorIndex)
                ?? ColorOptions.First(option => option.Index == 8);
            Rows.Add(new LayerSettingsRow(type, TimberElementTypeDisplayNameProvider.GetDisplayName(type), style.LayerName, color));
        }
    }

    private void ReplaceDefaultRows(TimberElementDefaultProfile profile)
    {
        RoundingStepMmText = Format(profile.GetCuttingLengthRoundingStepMm());
        DefaultRows.Clear();
        foreach (var type in Enum.GetValues<TimberElementType>())
        {
            DefaultRows.Add(new ElementDefaultSettingsRow(
                type,
                TimberElementTypeDisplayNameProvider.GetDisplayName(type),
                Format(profile.GetCuttingAllowanceMm(type))));
        }
    }

    private static bool TryReadNonNegativeNumber(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, SlovakCulture, out value) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return !double.IsNaN(value) &&
                !double.IsInfinity(value) &&
                value >= 0 &&
                value <= TimberElementDefaultProfile.MaxCuttingAllowanceMm;
        }

        value = 0;
        return false;
    }

    private static bool TryReadPositiveInteger(string raw, out double value)
    {
        if ((double.TryParse(raw, NumberStyles.Float, SlovakCulture, out value) ||
             double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
            !double.IsNaN(value) &&
            !double.IsInfinity(value))
        {
            var rounded = Math.Round(value);
            return value > 0 &&
                Math.Abs(value - rounded) < 0.000001 &&
                value <= TimberElementDefaultProfile.MaxCuttingLengthRoundingStepMm;
        }

        value = 0;
        return false;
    }

    private static string Format(double value) =>
        value.ToString("0.###", SlovakCulture);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SettingsTabControl))
        {
            UpdateActionButtons();
        }
    }

    private void UpdateActionButtons()
    {
        var tab = ReferenceEquals(SettingsTabControl.SelectedItem, LanguageTab)
            ? SettingsWindowTabKind.Language
            : ReferenceEquals(SettingsTabControl.SelectedItem, ManufacturingTab)
                ? SettingsWindowTabKind.Manufacturing
                : SettingsWindowTabKind.Layers;
        var actions = SettingsWindowActionRules.ForTab(tab);

        RestoreDefaultsButton.Visibility = actions.ShowRestoreDefaults ? Visibility.Visible : Visibility.Collapsed;
        StandardSettingsActionsPanel.Visibility = actions.ShowApplyActions ? Visibility.Visible : Visibility.Collapsed;
        LanguageSaveButton.Visibility = actions.ShowLanguageSave ? Visibility.Visible : Visibility.Collapsed;
        SaveAndApplyAllButton.IsDefault = actions.ShowApplyActions;
        LanguageSaveButton.IsDefault = actions.ShowLanguageSave;
    }
}

public sealed class LayerSettingsRow : INotifyPropertyChanged
{
    private string _layerName;
    private LayerColorOption _selectedColor;

    public LayerSettingsRow(TimberElementType elementType, string elementLabel, string layerName, LayerColorOption selectedColor)
    {
        ElementType = elementType;
        ElementLabel = elementLabel;
        _layerName = layerName;
        _selectedColor = selectedColor;
    }

    public TimberElementType ElementType { get; }
    public string ElementLabel { get; }

    public string LayerName
    {
        get => _layerName;
        set
        {
            if (_layerName == value)
            {
                return;
            }

            _layerName = value;
            OnPropertyChanged();
        }
    }

    public LayerColorOption SelectedColor
    {
        get => _selectedColor;
        set
        {
            if (Equals(_selectedColor, value))
            {
                return;
            }

            _selectedColor = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ElementDefaultSettingsRow : INotifyPropertyChanged
{
    private string _cuttingAllowanceMmText;

    public ElementDefaultSettingsRow(TimberElementType elementType, string elementLabel, string cuttingAllowanceMmText)
    {
        ElementType = elementType;
        ElementLabel = elementLabel;
        _cuttingAllowanceMmText = cuttingAllowanceMmText;
    }

    public TimberElementType ElementType { get; }
    public string ElementLabel { get; }

    public string CuttingAllowanceMmText
    {
        get => _cuttingAllowanceMmText;
        set
        {
            if (_cuttingAllowanceMmText == value)
            {
                return;
            }

            _cuttingAllowanceMmText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record LayerColorOption(int Index, string Label, MediaBrush Brush)
{
    public static IReadOnlyList<LayerColorOption> CreateDefaults() =>
    [
        Create(1, "#FF0000"),
        Create(2, "#FFFF00"),
        Create(3, "#00CC00"),
        Create(4, "#00CCCC"),
        Create(5, "#3366FF"),
        Create(6, "#CC00CC"),
        Create(30, "#FF7F00"),
        Create(8, "#777777"),
        Create(9, "#B5B5B5"),
    ];

    private static LayerColorOption Create(int index, string hex)
    {
        var brush = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return new LayerColorOption(index, LayerColorDisplayNameProvider.GetDisplayName(index), brush);
    }
}
