using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfMessageBox = System.Windows.MessageBox;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

public partial class LayerSettingsWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private CultureInfo _uiCulture;
    private readonly Func<SettingsApplyRequest, SettingsApplyResponse> _applySettings;
    private readonly SettingsApplyChangeTracker _applyChangeTracker = new();
    private readonly StatusBannerState _statusBannerState = new();
    private readonly DispatcherTimer _statusBannerTimer;
    private ObservableCollection<LayerColorOption> _colorOptions = [];
    private ObservableCollection<AnnotationModeOption> _annotationModeOptions = [];
    private ObservableCollection<ItemNumberLeaderStyleOption> _itemNumberLeaderStyleOptions = [];
    private ObservableCollection<SettingsApplyModeOption> _applyModeOptions = [];
    private string _roundingStepMmText = Format(TimberElementDefaultProfile.FactoryCuttingLengthRoundingStepMm);
    private string _selectedLanguageCode = AppLanguageService.DefaultLanguageCode;
    private TimberAnnotationMode _selectedAnnotationMode = TimberAnnotationMode.FullLabel;
    private ItemNumberLeaderStyle _selectedItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain;
    private SettingsSaveMode _selectedApplyMode = SettingsSaveMode.NewElementsOnly;
    private long _statusBannerTimerVersion;

    public ObservableCollection<LayerSettingsRow> Rows { get; } = [];
    public ObservableCollection<ElementDefaultSettingsRow> DefaultRows { get; } = [];
    public ObservableCollection<LayerColorOption> ColorOptions
    {
        get => _colorOptions;
        private set
        {
            _colorOptions = value;
            OnPropertyChanged();
        }
    }
    public ObservableCollection<string> LinetypeOptions { get; } = [];
    public IReadOnlyList<SupportedAppLanguage> LanguageOptions => AppLanguageService.SupportedLanguages;
    public ObservableCollection<AnnotationModeOption> AnnotationModeOptions
    {
        get => _annotationModeOptions;
        private set
        {
            _annotationModeOptions = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ItemNumberLeaderStyleOption> ItemNumberLeaderStyleOptions
    {
        get => _itemNumberLeaderStyleOptions;
        private set
        {
            _itemNumberLeaderStyleOptions = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<SettingsApplyModeOption> ApplyModeOptions
    {
        get => _applyModeOptions;
        private set
        {
            _applyModeOptions = value;
            OnPropertyChanged();
        }
    }

    internal ElementLayerProfile? Profile { get; private set; }
    internal TimberElementDefaultProfile? DefaultProfile { get; private set; }
    internal bool ApplyToExistingElements { get; private set; }
    internal SettingsSaveMode SaveMode { get; private set; }
    internal string LanguageCode { get; private set; } = AppLanguageService.DefaultLanguageCode;

    public SettingsSaveMode SelectedApplyMode
    {
        get => _selectedApplyMode;
        set
        {
            var normalized = SettingsSelectionRules.NormalizeApplyMode(value);
            if (_selectedApplyMode == normalized)
            {
                return;
            }

            _selectedApplyMode = normalized;
            OnPropertyChanged();
        }
    }

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
            PreviewLanguage(normalized);
        }
    }

    public TimberAnnotationMode SelectedAnnotationMode
    {
        get => _selectedAnnotationMode;
        set
        {
            var normalized = TimberAnnotationModeRules.Normalize(value);
            if (_selectedAnnotationMode == normalized)
            {
                return;
            }

            _selectedAnnotationMode = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsItemNumberLeaderStyleEnabled));
        }
    }

    public bool IsItemNumberLeaderStyleEnabled =>
        SelectedAnnotationMode == TimberAnnotationMode.ItemNumberLeader;

    public ItemNumberLeaderStyle SelectedItemNumberLeaderStyle
    {
        get => _selectedItemNumberLeaderStyle;
        set
        {
            var normalized = ItemNumberLeaderStyleRules.Normalize(value);
            if (_selectedItemNumberLeaderStyle == normalized)
            {
                return;
            }

            _selectedItemNumberLeaderStyle = normalized;
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
        string languageCode,
        IReadOnlyList<string> availableLinetypeNames,
        Func<SettingsApplyRequest, SettingsApplyResponse> applySettings)
    {
        _applySettings = applySettings ?? throw new ArgumentNullException(nameof(applySettings));
        var normalizedDefaultProfile = defaultProfile.Normalize();
        _selectedAnnotationMode = SettingsSelectionRules.NormalizeAnnotationMode(
            normalizedDefaultProfile.DefaultAnnotationMode);
        _selectedItemNumberLeaderStyle = SettingsSelectionRules.NormalizeItemNumberLeaderStyle(
            normalizedDefaultProfile.DefaultItemNumberLeaderStyle);
        _selectedApplyMode = SettingsSaveMode.NewElementsOnly;
        _selectedLanguageCode = AppLanguageService.NormalizeLanguageCode(languageCode);
        _uiCulture = AppLanguageService.CurrentUiCulture;
        RefreshLocalizedSources();
        RefreshLinetypeOptions(availableLinetypeNames
            .Concat(CadLinetypeNames.SupportedStandardNames)
            .Concat(profile.Styles.Select(style => CadLinetypeNames.Normalize(style.LinetypeName)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList());
        InitializeComponent();
        DataContext = this;
        _statusBannerTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _statusBannerTimer.Tick += StatusBannerTimer_Tick;
        AppLanguageService.LanguageChanged += AppLanguageService_LanguageChanged;
        Closed += LayerSettingsWindow_Closed;
        SettingsTabControl.SelectionChanged += SettingsTabControl_SelectionChanged;
        UpdateActionButtons();
        ReplaceRows(profile.Normalize());
        ReplaceDefaultRows(normalizedDefaultProfile);
        StylesDataGrid.ItemsSource = Rows;
        DefaultsDataGrid.ItemsSource = DefaultRows;
        _applyChangeTracker.AcceptProfile(CreateProfileFingerprint(
            profile.Normalize(),
            normalizedDefaultProfile,
            _selectedLanguageCode));
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        ReplaceRows(ElementLayerProfile.CreateDefault());
        ReplaceDefaultRows(TimberElementDefaultProfile.CreateDefault());
        ShowStatus("SettingsWindow_DefaultsRestored", StatusBannerSeverity.Information);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
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

        var saveMode = ReferenceEquals(SettingsTabControl.SelectedItem, LanguageTab)
            ? SettingsSaveMode.LanguageOnly
            : SelectedApplyMode;
        var languageCode = AppLanguageService.NormalizeLanguageCode(SelectedLanguageCode);
        var fingerprint = CreateProfileFingerprint(profile, defaultProfile, languageCode);
        var profileChanged = _applyChangeTracker.HasProfileChanged(fingerprint);
        if (!SettingsApplyDispatchRules.ShouldDispatch(saveMode, profileChanged))
        {
            ShowStatus("SettingsWindow_NoChanges", StatusBannerSeverity.Information);
            return;
        }

        var request = new SettingsApplyRequest(
            profile,
            defaultProfile,
            languageCode,
            saveMode,
            profileChanged);
        var response = _applySettings(request);
        RefreshLinetypeOptions(response.AvailableLinetypeNames);
        if (response.ProfileAccepted)
        {
            Profile = profile;
            DefaultProfile = defaultProfile;
            LanguageCode = languageCode;
            _applyChangeTracker.AcceptProfile(fingerprint);
        }

        if (!response.Success)
        {
            ShowStatus(response.ResourceKey, response.Severity, response.ResourceArguments);
            return;
        }

        ApplyToExistingElements = saveMode is SettingsSaveMode.AllElements or SettingsSaveMode.SelectedElements;
        SaveMode = saveMode;
        ShowStatus(response.ResourceKey, response.Severity, response.ResourceArguments);
    }

    private static string CreateProfileFingerprint(
        ElementLayerProfile profile,
        TimberElementDefaultProfile defaultProfile,
        string languageCode) =>
        JsonSerializer.Serialize(new
        {
            LayerProfile = profile.Normalize(),
            DefaultProfile = defaultProfile.Normalize(),
            LanguageCode = AppLanguageService.NormalizeLanguageCode(languageCode),
        });

    private bool TryBuildProfile(out ElementLayerProfile profile)
    {
        var styles = new List<ElementLayerStyle>();
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

            styles.Add(new ElementLayerStyle(
                row.ElementType,
                layerName,
                row.SelectedColor.Index,
                row.SelectedLinetypeName,
                TryReadLinetypeScale(row.LinetypeScaleText, out var linetypeScale)
                    ? linetypeScale
                    : double.NaN));
            if (!ElementLayerProfile.IsValidLinetypeScale(linetypeScale))
            {
                WpfMessageBox.Show(
                    UiStrings.Format(
                        UiStrings.DialogLayersLinetypeScaleFormat,
                        row.ElementLabel,
                        ElementLayerProfile.MinLinetypeScale,
                        ElementLayerProfile.MaxLinetypeScale),
                    UiStrings.MessageDialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                profile = ElementLayerProfile.CreateDefault();
                return false;
            }
        }

        if (ElementLayerProfileConflictRules.TryFindConflict(styles, out var conflictingLayerName))
        {
            WpfMessageBox.Show(
                UiStrings.Format(UiStrings.DialogLayersConflictFormat, conflictingLayerName),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            profile = ElementLayerProfile.CreateDefault();
            return false;
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
            DefaultAnnotationMode = SelectedAnnotationMode,
            DefaultItemNumberLeaderStyle = SelectedItemNumberLeaderStyle,
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
            Rows.Add(new LayerSettingsRow(
                type,
                TimberElementTypeDisplayNameProvider.GetDisplayName(type, _uiCulture),
                style.LayerName,
                color,
                style.LinetypeName,
                FormatLinetypeScale(style.LinetypeScale)));
        }
    }

    private void ReplaceDefaultRows(TimberElementDefaultProfile profile)
    {
        RoundingStepMmText = Format(profile.GetCuttingLengthRoundingStepMm());
        SelectedAnnotationMode = profile.DefaultAnnotationMode;
        SelectedItemNumberLeaderStyle = profile.DefaultItemNumberLeaderStyle;
        DefaultRows.Clear();
        foreach (var type in Enum.GetValues<TimberElementType>())
        {
            DefaultRows.Add(new ElementDefaultSettingsRow(
                type,
                TimberElementTypeDisplayNameProvider.GetDisplayName(type, _uiCulture),
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

    private bool TryReadLinetypeScale(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, _uiCulture, out value) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return ElementLayerProfile.IsValidLinetypeScale(value);
        }

        value = double.NaN;
        return false;
    }

    private string FormatLinetypeScale(double value) =>
        value.ToString("0.###", _uiCulture);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RefreshLinetypeOptions(IEnumerable<string> names)
    {
        var merged = LinetypeOptions
            .Concat(names)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        LinetypeOptions.Clear();
        foreach (var name in merged)
        {
            LinetypeOptions.Add(name);
        }
    }

    private void PreviewLanguage(string languageCode)
    {
        AppLanguageService.Apply(languageCode);
        if (!AcKrovyRibbon.RebuildLocalizedUi(activateTab: false))
        {
            AcKrovyRibbon.ScheduleCreation();
        }

        ClassicToolbarManager.RefreshLocalizedContent();
    }

    private void AppLanguageService_LanguageChanged(object? sender, AppLanguageChangedEventArgs e)
    {
        _uiCulture = AppLanguageService.CurrentUiCulture;
        RefreshLocalizedSources();
        RefreshLocalizedRowLabels();
        RenderStatusBanner();
    }

    private void RefreshLocalizedSources()
    {
        var selectedColors = Rows.ToDictionary(row => row.ElementType, row => row.SelectedColor.Index);
        var selectionSet = SettingsLocalizedSelectionSet.Create(
            _uiCulture,
            SelectedAnnotationMode,
            SelectedItemNumberLeaderStyle,
            SelectedApplyMode);

        var colorOptions = new ObservableCollection<LayerColorOption>(
            LayerColorOption.CreateDefaults(_uiCulture));
        var annotationModeOptions = new ObservableCollection<AnnotationModeOption>(
            selectionSet.AnnotationModes
                .Select(option => new AnnotationModeOption(
                    option.Value,
                    option.DisplayName)));
        var itemNumberLeaderStyleOptions =
            new ObservableCollection<ItemNumberLeaderStyleOption>(
                selectionSet.ItemNumberLeaderStyles
                    .Select(option => new ItemNumberLeaderStyleOption(
                        option.Value,
                        option.DisplayName)));
        var applyModeOptions = new ObservableCollection<SettingsApplyModeOption>(
            selectionSet.ApplyModes
                .Select(option => new SettingsApplyModeOption(
                    option.Value,
                    option.DisplayName)));

        ColorOptions = colorOptions;
        AnnotationModeOptions = annotationModeOptions;
        ItemNumberLeaderStyleOptions = itemNumberLeaderStyleOptions;
        ApplyModeOptions = applyModeOptions;

        foreach (var row in Rows)
        {
            row.SelectedColor = ColorOptions.First(
                option => option.Index == selectedColors[row.ElementType]);
        }

        _selectedAnnotationMode = selectionSet.SelectedAnnotationMode;
        _selectedItemNumberLeaderStyle = selectionSet.SelectedItemNumberLeaderStyle;
        _selectedApplyMode = selectionSet.SelectedApplyMode;
        OnPropertyChanged(nameof(SelectedAnnotationMode));
        OnPropertyChanged(nameof(IsItemNumberLeaderStyleEnabled));
        OnPropertyChanged(nameof(SelectedItemNumberLeaderStyle));
        OnPropertyChanged(nameof(SelectedApplyMode));
    }

    private void RefreshLocalizedRowLabels()
    {
        foreach (var row in Rows)
        {
            row.ElementLabel = TimberElementTypeDisplayNameProvider.GetDisplayName(
                row.ElementType,
                _uiCulture);
        }

        foreach (var row in DefaultRows)
        {
            row.ElementLabel = TimberElementTypeDisplayNameProvider.GetDisplayName(
                row.ElementType,
                _uiCulture);
        }
    }

    private void ShowStatus(
        string resourceKey,
        StatusBannerSeverity severity,
        params object[] arguments)
    {
        _statusBannerTimer.Stop();
        _statusBannerTimerVersion = _statusBannerState.Show(resourceKey, severity, arguments);
        RenderStatusBanner();
        _statusBannerTimer.Start();
    }

    private void RenderStatusBanner()
    {
        if (!_statusBannerState.IsVisible)
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            return;
        }

        StatusBannerText.Text = _statusBannerState.Resolve(_uiCulture);
        (StatusBanner.Background, StatusBanner.BorderBrush) = _statusBannerState.Severity switch
        {
            StatusBannerSeverity.Warning => (
                new MediaSolidColorBrush(MediaColor.FromRgb(255, 244, 214)),
                new MediaSolidColorBrush(MediaColor.FromRgb(180, 122, 25))),
            StatusBannerSeverity.Information => (
                new MediaSolidColorBrush(MediaColor.FromRgb(226, 240, 252)),
                new MediaSolidColorBrush(MediaColor.FromRgb(67, 116, 157))),
            _ => (
                new MediaSolidColorBrush(MediaColor.FromRgb(231, 244, 232)),
                new MediaSolidColorBrush(MediaColor.FromRgb(78, 141, 86))),
        };
        StatusBanner.Visibility = Visibility.Visible;
    }

    private void StatusBannerTimer_Tick(object? sender, EventArgs e)
    {
        _statusBannerTimer.Stop();
        if (_statusBannerState.TryHide(_statusBannerTimerVersion))
        {
            RenderStatusBanner();
        }
    }

    private void LayerSettingsWindow_Closed(object? sender, EventArgs e)
    {
        _statusBannerTimer.Stop();
        _statusBannerTimer.Tick -= StatusBannerTimer_Tick;
        AppLanguageService.LanguageChanged -= AppLanguageService_LanguageChanged;
        Closed -= LayerSettingsWindow_Closed;
        _statusBannerState.Clear();
    }

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
                : ReferenceEquals(SettingsTabControl.SelectedItem, AnnotationTab)
                    ? SettingsWindowTabKind.Annotation
                    : SettingsWindowTabKind.Layers;
        var actions = SettingsWindowActionRules.ForTab(tab);

        RestoreDefaultsButton.Visibility = actions.ShowRestoreDefaults ? Visibility.Visible : Visibility.Collapsed;
        ApplyModePanel.Visibility = actions.ShowApplyActions ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsDefault = true;
    }
}

public sealed class LayerSettingsRow : INotifyPropertyChanged
{
    private string _elementLabel;
    private string _layerName;
    private LayerColorOption _selectedColor;
    private string _selectedLinetypeName;
    private string _linetypeScaleText;

    public LayerSettingsRow(
        TimberElementType elementType,
        string elementLabel,
        string layerName,
        LayerColorOption selectedColor,
        string selectedLinetypeName,
        string linetypeScaleText)
    {
        ElementType = elementType;
        _elementLabel = elementLabel;
        _layerName = layerName;
        _selectedColor = selectedColor;
        _selectedLinetypeName = CadLinetypeNames.Normalize(selectedLinetypeName);
        _linetypeScaleText = linetypeScaleText;
    }

    public TimberElementType ElementType { get; }
    public string ElementLabel
    {
        get => _elementLabel;
        set
        {
            if (_elementLabel == value)
            {
                return;
            }

            _elementLabel = value;
            OnPropertyChanged();
        }
    }

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

    public string SelectedLinetypeName
    {
        get => _selectedLinetypeName;
        set
        {
            var normalized = CadLinetypeNames.Normalize(value);
            if (string.Equals(_selectedLinetypeName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedLinetypeName = normalized;
            OnPropertyChanged();
        }
    }

    public string LinetypeScaleText
    {
        get => _linetypeScaleText;
        set
        {
            if (_linetypeScaleText == value)
            {
                return;
            }

            _linetypeScaleText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ElementDefaultSettingsRow : INotifyPropertyChanged
{
    private string _elementLabel;
    private string _cuttingAllowanceMmText;

    public ElementDefaultSettingsRow(TimberElementType elementType, string elementLabel, string cuttingAllowanceMmText)
    {
        ElementType = elementType;
        _elementLabel = elementLabel;
        _cuttingAllowanceMmText = cuttingAllowanceMmText;
    }

    public TimberElementType ElementType { get; }
    public string ElementLabel
    {
        get => _elementLabel;
        set
        {
            if (_elementLabel == value)
            {
                return;
            }

            _elementLabel = value;
            OnPropertyChanged();
        }
    }

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
    public static IReadOnlyList<LayerColorOption> CreateDefaults(CultureInfo? culture = null) =>
    [
        Create(1, "#FF0000", culture),
        Create(2, "#FFFF00", culture),
        Create(3, "#00CC00", culture),
        Create(4, "#00CCCC", culture),
        Create(5, "#3366FF", culture),
        Create(6, "#CC00CC", culture),
        Create(30, "#FF7F00", culture),
        Create(8, "#777777", culture),
        Create(9, "#B5B5B5", culture),
    ];

    private static LayerColorOption Create(int index, string hex, CultureInfo? culture)
    {
        var brush = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return new LayerColorOption(
            index,
            LayerColorDisplayNameProvider.GetDisplayName(index, culture),
            brush);
    }
}

public sealed record AnnotationModeOption(
    TimberAnnotationMode Mode,
    string DisplayName);

public sealed record ItemNumberLeaderStyleOption(
    ItemNumberLeaderStyle Style,
    string DisplayName);

public sealed record SettingsApplyModeOption(
    SettingsSaveMode Mode,
    string DisplayName);

internal sealed record SettingsApplyRequest(
    ElementLayerProfile Profile,
    TimberElementDefaultProfile DefaultProfile,
    string LanguageCode,
    SettingsSaveMode SaveMode,
    bool ProfileChanged);

internal sealed record SettingsApplyResponse(
    bool Success,
    bool ProfileAccepted,
    StatusBannerSeverity Severity,
    string ResourceKey,
    object[] ResourceArguments,
    IReadOnlyList<string> AvailableLinetypeNames);
