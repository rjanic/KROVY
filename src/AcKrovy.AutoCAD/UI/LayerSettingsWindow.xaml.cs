using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfMessageBox = System.Windows.MessageBox;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using WpfBrush = System.Windows.Media.Brush;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;

namespace AcKrovy.AutoCAD.UI;

public partial class LayerSettingsWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo SlovakCulture = CultureInfo.GetCultureInfo("sk-SK");
    private CultureInfo _uiCulture;
    private readonly Func<SettingsApplyRequest, SettingsApplyResponse> _applySettings;
    private readonly SettingsApplyChangeTracker _layerProfileChangeTracker = new();
    private readonly SettingsApplyChangeTracker _defaultProfileChangeTracker = new();
    private readonly StatusBannerState _statusBannerState = new();
    private readonly DispatcherTimer _statusBannerTimer;
    private readonly SettingsUiPreferences _loadedUiPreferences;
    private readonly IApplicationLanguageWorkflow _languageWorkflow;
    private ObservableCollection<LayerColorOption> _colorOptions = [];
    private ObservableCollection<AnnotationModeOption> _annotationModeOptions = [];
    private ObservableCollection<AnnotationPresetOption> _annotationPresetOptions = [];
    private ObservableCollection<ItemNumberLeaderStyleOption> _itemNumberLeaderStyleOptions = [];
    private ObservableCollection<AnnotationScaleOption> _annotationScaleOptions = [];
    private ObservableCollection<string> _layerNameOptions = [];
    private IReadOnlyDictionary<string, CadLayerPreset> _layerPresets =
        new Dictionary<string, CadLayerPreset>(StringComparer.OrdinalIgnoreCase);
        private string _roundingStepMmText = Format(TimberElementDefaultProfile.FactoryCuttingLengthRoundingStepMm);
    private string _selectedLanguageCode = AppLanguageService.DefaultLanguageCode;
    private TimberAnnotationMode _selectedAnnotationMode = TimberAnnotationMode.FullLabel;
    private ItemNumberLeaderStyle _selectedItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain;
    private int _loadedDrawingScaleDenominator = TimberAnnotationScaleRules.DefaultDenominator;
    private TimberAnnotationScalePreset _selectedDrawingScalePreset =
        TimberAnnotationScalePreset.Scale50;
    private string _drawingCustomScaleText = "50";
    private SettingsAnnotationPreset _selectedAnnotationPreset =
        SettingsAnnotationPreset.FullLabel;
    private ElementLayerProfile _acceptedLayerProfile = ElementLayerProfile.CreateDefault();
    private TimberElementDefaultProfile _acceptedDefaultProfile =
        TimberElementDefaultProfile.CreateDefault();
    private string? _acceptedLayersUiFingerprint;
    private string? _acceptedAllowancesUiFingerprint;
    private string? _acceptedAnnotationUiFingerprint;
    private long _statusBannerTimerVersion;
    private bool _languageSelectionReady;
    private bool _synchronizingLanguageSelection;

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
    public ObservableCollection<string> LayerNameOptions
    {
        get => _layerNameOptions;
        private set
        {
            _layerNameOptions = value;
            OnPropertyChanged();
        }
    }
    public ObservableCollection<LanguageCardOption> LanguageOptions { get; } =
        new(AppLanguageService.SupportedLanguages.Select(language =>
            new LanguageCardOption(
                language.Code,
                language.UpperCode,
                language.NativeName)));
    public SettingsVisualStateViewModel Visual { get; }
    public ObservableCollection<AnnotationModeOption> AnnotationModeOptions
    {
        get => _annotationModeOptions;
        private set
        {
            _annotationModeOptions = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<AnnotationPresetOption> AnnotationPresetOptions
    {
        get => _annotationPresetOptions;
        private set
        {
            _annotationPresetOptions = value;
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

    public ObservableCollection<AnnotationScaleOption> AnnotationScaleOptions
    {
        get => _annotationScaleOptions;
        private set
        {
            _annotationScaleOptions = value;
            OnPropertyChanged();
        }
    }

    public TimberAnnotationScalePreset SelectedDrawingScalePreset
    {
        get => _selectedDrawingScalePreset;
        set
        {
            if (_selectedDrawingScalePreset == value)
            {
                return;
            }

            _selectedDrawingScalePreset = value;
            OnScaleSelectionChanged();
        }
    }

    public string DrawingCustomScaleText
    {
        get => _drawingCustomScaleText;
        set
        {
            if (_drawingCustomScaleText == value)
            {
                return;
            }

            _drawingCustomScaleText = value;
            OnScaleSelectionChanged();
        }
    }

    public bool IsDrawingCustomScale =>
        SelectedDrawingScalePreset == TimberAnnotationScalePreset.Custom;
    public bool HasDrawingScaleError =>
        IsDrawingCustomScale && !TryReadScaleDenominator(DrawingCustomScaleText, out _);
    public string CurrentDrawingScaleText => UiStrings.Format(
        UiStrings.GetString(
            "SettingsWindow_AnnotationScale_CurrentDrawingFormat",
            _uiCulture),
        _loadedDrawingScaleDenominator);
    public string DefaultNewElementScaleText => UiStrings.Format(
        UiStrings.GetString(
            "SettingsWindow_AnnotationScale_NewElementsDefaultFormat",
            _uiCulture),
        _acceptedDefaultProfile.AnnotationScaleDenominator);
    public string ScaleValidationText => UiStrings.GetString(
        "SettingsWindow_AnnotationScale_ValidationRange",
        _uiCulture);
    public string PreviewDimensionText => FormatScalePreview(
        CurrentScalePreview.DimensionTextHeightMm, "mm");
    public string PreviewItemNumberText => FormatScalePreview(
        CurrentScalePreview.ItemNumberTextHeightMm, "mm");
    public string PreviewSlopeText => FormatScalePreview(
        CurrentScalePreview.SlopeTextHeightMm, "mm");
    public string PreviewBlockScale => FormatScalePreview(
        CurrentScalePreview.FramedBlockScale, string.Empty);
    public string PreviewDimensionModelText => FormatMeasurementLine(
        "SettingsWindow_AnnotationScale_Model",
        CurrentScalePreview.DimensionTextHeightMm,
        "mm");
    public string PreviewDimensionPaperText => FormatMeasurementLine(
        "SettingsWindow_AnnotationScale_Paper",
        CurrentScalePreview.DimensionPaperTextHeightMm,
        "mm");
    public string PreviewItemNumberModelText => FormatMeasurementLine(
        "SettingsWindow_AnnotationScale_Model",
        CurrentScalePreview.ItemNumberTextHeightMm,
        "mm");
    public string PreviewItemNumberPaperText => FormatMeasurementLine(
        "SettingsWindow_AnnotationScale_Paper",
        CurrentScalePreview.ItemNumberPaperTextHeightMm,
        "mm");
    public string PreviewSlopeModelText => FormatMeasurementLine(
        "SettingsWindow_AnnotationScale_Model",
        CurrentScalePreview.SlopeTextHeightMm,
        "mm");
    public string PreviewSlopePaperText => FormatMeasurementLine(
        "SettingsWindow_AnnotationScale_Paper",
        CurrentScalePreview.SlopePaperTextHeightMm,
        "mm");
    public string PreviewBlockModelText => FormatMeasurementLine(
        "SettingsWindow_AnnotationScale_Model",
        CurrentScalePreview.FramedBlockScale,
        string.Empty,
        "×");
    public string PreviewBlockPaperText => string.Concat(
        UiStrings.GetString("SettingsWindow_AnnotationScale_Paper", _uiCulture),
        ": ",
        UiStrings.GetString("SettingsWindow_AnnotationScale_RelativeSize", _uiCulture));
    public string SelectedAnnotationPreviewResourceUri =>
        AnnotationPreviewResourceMap.GetPackUri(SelectedAnnotationPreset);
    public double PreviewPresentationScale => Math.Clamp(
        CurrentScalePreview.FramedBlockScale,
        0.65d,
        1.35d);

    private TimberAnnotationScalePreview CurrentScalePreview =>
        TimberAnnotationScaleSettingsRules.CreatePreview(
            TryGetDrawingScaleDenominator(out var denominator)
                ? denominator
                : TimberAnnotationScaleRules.DefaultDenominator);

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
            UpdateFormState();
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
            SynchronizeSelectedAnnotationPreset();
            NotifyAnnotationPreviewChanged();
            UpdateFormState();
        }
    }

    public bool IsItemNumberLeaderStyleEnabled =>
        SelectedAnnotationMode is
            TimberAnnotationMode.ItemNumberLeader or
            TimberAnnotationMode.DimensionsWithItemNumber;

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
            SynchronizeSelectedAnnotationPreset();
            NotifyAnnotationPreviewChanged();
            UpdateFormState();
        }
    }

    public SettingsAnnotationPreset SelectedAnnotationPreset
    {
        get => _selectedAnnotationPreset;
        set
        {
            var definition = SettingsAnnotationPresetRules.Get(value);
            if (_selectedAnnotationPreset == definition.Preset &&
                _selectedAnnotationMode == definition.AnnotationMode &&
                _selectedItemNumberLeaderStyle == definition.ItemNumberLeaderStyle)
            {
                return;
            }

            _selectedAnnotationPreset = definition.Preset;
            _selectedAnnotationMode = definition.AnnotationMode;
            _selectedItemNumberLeaderStyle = definition.ItemNumberLeaderStyle;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAnnotationMode));
            OnPropertyChanged(nameof(SelectedItemNumberLeaderStyle));
            OnPropertyChanged(nameof(IsItemNumberLeaderStyleEnabled));
            NotifyAnnotationPreviewChanged();
            UpdateFormState();
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
            UpdateFormState();
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
        IReadOnlyList<CadLayerPreset> availableLayerPresets,
        Func<SettingsApplyRequest, SettingsApplyResponse> applySettings,
        IApplicationLanguageWorkflow? languageWorkflow = null,
        AnnotationScaleSettingsState? annotationScaleState = null)
    {
        _applySettings = applySettings ?? throw new ArgumentNullException(nameof(applySettings));
        _languageWorkflow = languageWorkflow ??
            AutoCadApplicationLanguageWorkflow.Shared;
        _loadedUiPreferences = SettingsUiPreferencesStore.Load();
        var normalizedDefaultProfile = defaultProfile.Normalize();
        _acceptedDefaultProfile = normalizedDefaultProfile;
                _selectedAnnotationMode = SettingsSelectionRules.NormalizeAnnotationMode(
            normalizedDefaultProfile.DefaultAnnotationMode);
        _selectedItemNumberLeaderStyle = SettingsSelectionRules.NormalizeItemNumberLeaderStyle(
            normalizedDefaultProfile.DefaultItemNumberLeaderStyle);
        var scaleState = annotationScaleState ??
            new AnnotationScaleSettingsState(
                false,
                TimberAnnotationScaleRules.DefaultDenominator,
                TimberAnnotationScaleRules.DefaultDenominator);
        _loadedDrawingScaleDenominator = scaleState.EffectiveDenominator;
        InitializeScaleSelections(normalizedDefaultProfile.AnnotationScaleDenominator);
        _selectedAnnotationPreset = SettingsAnnotationPresetRules.FromProduction(
            _selectedAnnotationMode,
            _selectedItemNumberLeaderStyle);
        _selectedLanguageCode = AppLanguageService.NormalizeLanguageCode(languageCode);
        _uiCulture = AppLanguageService.CurrentUiCulture;
        Visual = new SettingsVisualStateViewModel(
            _uiCulture,
            _loadedUiPreferences.SelectedSection,
            _loadedUiPreferences.Theme);
        RefreshLocalizedSources();
        RefreshLinetypeOptions(availableLinetypeNames
            .Concat(CadLinetypeNames.SupportedStandardNames)
            .Concat(profile.Styles.Select(style => CadLinetypeNames.Normalize(style.LinetypeName)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList());
        RefreshLayerNameOptions(availableLayerPresets, profile);
        InitializeComponent();
        DataContext = this;
        ApplyWindowPreferences(_loadedUiPreferences);
        ApplyTheme(Visual.SelectedTheme);
        Visual.SectionChanged += Visual_SectionChanged;
        Visual.ThemeChanged += Visual_ThemeChanged;
        _statusBannerTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _statusBannerTimer.Tick += StatusBannerTimer_Tick;
        AppLanguageService.LanguageChanged += AppLanguageService_LanguageChanged;
        Loaded += LayerSettingsWindow_Loaded;
        Closed += LayerSettingsWindow_Closed;
        UpdateActionButtons();
        _acceptedLayerProfile = profile.Normalize();
        ReplaceRows(_acceptedLayerProfile);
        InitializeExistingLayerBaselines();
        ReplaceDefaultRows(normalizedDefaultProfile);
        StylesDataGrid.ItemsSource = Rows;
        DefaultsDataGrid.ItemsSource = DefaultRows;
        _layerProfileChangeTracker.AcceptProfile(
            CreateLayerProfileFingerprint(_acceptedLayerProfile));
        _defaultProfileChangeTracker.AcceptProfile(
            CreateDefaultProfileFingerprint(_acceptedDefaultProfile));
        InitializeAnnotationTextSettings(normalizedDefaultProfile);
        AcceptUiSectionBaselines(SettingsSectionScope.AllEditable);
        Visual.SetFormState(SettingsFormState.NoChanges);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (Visual.SelectedSection == SettingsWindowTabKind.Layers)
        {
            ReplaceRows(ElementLayerProfile.CreateDefault());
            InitializeExistingLayerBaselines();
        }
        else if (Visual.SelectedSection == SettingsWindowTabKind.Manufacturing)
        {
            ReplaceDefaultRows(
                TimberElementDefaultProfile.CreateDefault(),
                replaceAnnotation: false);
        }

        UpdateFormState();
        ShowStatus("SettingsWindow_DefaultsRestored", StatusBannerSeverity.Information);
    }

    private void SaveNewElements_Click(object sender, RoutedEventArgs e) =>
        ApplyAnnotationSettings(TimberAnnotationSettingsApplyScope.NewElementsOnly);

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var scope = Visual.SelectedSection switch
        {
            SettingsWindowTabKind.Layers => SettingsSectionScope.Layers,
            SettingsWindowTabKind.Manufacturing => SettingsSectionScope.Allowances,
            _ => SettingsSectionScope.None,
        };
        ApplySettings(
            SettingsSaveMode.NewElementsOnly,
            scope);
    }

    private void SaveApplySelection_Click(object sender, RoutedEventArgs e) =>
        ApplyAnnotationSettings(TimberAnnotationSettingsApplyScope.SelectedElements);

    private void SaveApplyAll_Click(object sender, RoutedEventArgs e) =>
        ApplyAnnotationSettings(TimberAnnotationSettingsApplyScope.AllElements);

    private void ApplyAnnotationSettings(TimberAnnotationSettingsApplyScope applyScope) =>
        ApplySettings(
            applyScope switch
            {
                TimberAnnotationSettingsApplyScope.SelectedElements =>
                    SettingsSaveMode.SelectedElements,
                TimberAnnotationSettingsApplyScope.AllElements =>
                    SettingsSaveMode.AllElements,
                _ => SettingsSaveMode.NewElementsOnly,
            },
            SettingsSectionScope.Annotation,
            applyScope);

    private void ApplySettings(
        SettingsSaveMode saveMode,
        SettingsSectionScope scope,
        TimberAnnotationSettingsApplyScope? annotationApplyScope = null)
    {
        if (scope.HasFlag(SettingsSectionScope.Layers))
        {
            StylesDataGrid.CommitEdit();
            StylesDataGrid.CommitEdit();
        }
        if (scope.HasFlag(SettingsSectionScope.Allowances))
        {
            DefaultsDataGrid.CommitEdit();
            DefaultsDataGrid.CommitEdit();
        }

        var profile = _acceptedLayerProfile;
        if (scope.HasFlag(SettingsSectionScope.Layers) &&
            !TryBuildProfile(out profile))
        {
            return;
        }

        if (!TryBuildDefaultProfile(
                scope.HasFlag(SettingsSectionScope.Allowances),
                scope.HasFlag(SettingsSectionScope.Annotation),
                out var defaultProfile))
        {
            return;
        }

        var hasValidSelectedScale = TryGetDrawingScaleDenominator(
            out var selectedScaleDenominator);
        if (scope.HasFlag(SettingsSectionScope.Annotation) &&
            !hasValidSelectedScale)
        {
            return;
        }
        if (!hasValidSelectedScale)
        {
            selectedScaleDenominator = _acceptedDefaultProfile.AnnotationScaleDenominator;
        }

        var languageCode = AppLanguageService.NormalizeLanguageCode(SelectedLanguageCode);
        var layerProfileChanged =
            scope.HasFlag(SettingsSectionScope.Layers) &&
            _layerProfileChangeTracker.HasProfileChanged(
                CreateLayerProfileFingerprint(profile));
        var defaultProfileChanged =
            (scope.HasFlag(SettingsSectionScope.Allowances) ||
             scope.HasFlag(SettingsSectionScope.Annotation)) &&
            _defaultProfileChangeTracker.HasProfileChanged(
                CreateDefaultProfileFingerprint(defaultProfile));
        var profileChanged = layerProfileChanged || defaultProfileChanged;
        if (!SettingsApplyDispatchRules.ShouldDispatch(
                saveMode,
                profileChanged))
        {
            ShowStatus("SettingsWindow_NoChanges", StatusBannerSeverity.Information);
            return;
        }

        TimberAnnotationSettingsRequest? annotationSettings = null;
        if (annotationApplyScope.HasValue)
        {
            // DWG TextStyle Ensure must run under DocumentLock inside AcKrovyCommands
            // (modeless Settings must not lock the document here). Pass settings/patch only.
            var pendingTextSettings = defaultProfile.DefaultAnnotationTextSettings
                ?? TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
            var pendingTextPatch = BuildPendingAnnotationTextPatch(pendingTextSettings);
            annotationSettings = new TimberAnnotationSettingsRequest(
                SelectedAnnotationMode,
                SelectedItemNumberLeaderStyle,
                selectedScaleDenominator,
                annotationApplyScope.Value,
                pendingTextSettings,
                pendingTextPatch);
        }

        var request = new SettingsApplyRequest(
            profile,
            defaultProfile,
            languageCode,
            saveMode,
            annotationSettings,
            layerProfileChanged,
            defaultProfileChanged,
            layerProfileChanged
                ? Rows.Select(row => new CadLayerOverrideIntent(
                        row.ElementType,
                        row.SelectedExistingLayerName,
                        row.HasExplicitPropertyChanges))
                    .ToArray()
                : []);
        SetFooterActionsEnabled(false);
        SettingsApplyResponse response;
        try
        {
            response = SettingsWindowOwner.RunWithPreservedPlacement(
                this,
                () => _applySettings(request));
        }
        finally
        {
            SetFooterActionsEnabled(true);
        }
        RefreshLinetypeOptions(response.AvailableLinetypeNames);
        var acceptedProfile = response.AppliedProfile ?? profile;
        RefreshLayerNameOptions(
            response.AvailableLayerPresets,
            layerProfileChanged ? acceptedProfile : _acceptedLayerProfile);
        if (response.ProfileAccepted)
        {
            if (layerProfileChanged)
            {
                _acceptedLayerProfile = acceptedProfile.Normalize();
                ApplyAcceptedLayerNames(_acceptedLayerProfile);
                Profile = _acceptedLayerProfile;
                _layerProfileChangeTracker.AcceptProfile(
                    CreateLayerProfileFingerprint(_acceptedLayerProfile));
                AcceptUiSectionBaselines(SettingsSectionScope.Layers);
            }
            if (defaultProfileChanged)
            {
                _acceptedDefaultProfile = defaultProfile.Normalize();
                DefaultProfile = _acceptedDefaultProfile;
                if (scope.HasFlag(SettingsSectionScope.Annotation) &&
                    _acceptedDefaultProfile.DefaultAnnotationTextSettings is not null)
                {
                    AcceptAnnotationTextSettingsBaseline(
                        _acceptedDefaultProfile.DefaultAnnotationTextSettings);
                    PushPendingTextSettingsToUi();
                }
                _defaultProfileChangeTracker.AcceptProfile(
                    CreateDefaultProfileFingerprint(_acceptedDefaultProfile));
                AcceptUiSectionBaselines(
                    scope & (SettingsSectionScope.Allowances |
                             SettingsSectionScope.Annotation));
            }
            LanguageCode = languageCode;
            if (annotationApplyScope == TimberAnnotationSettingsApplyScope.AllElements)
            {
                _loadedDrawingScaleDenominator = selectedScaleDenominator;
            }
            NotifyScalePropertiesChanged();
            Visual.SetFormState(HasAnyPendingChanges
                ? SettingsFormState.UnsavedChanges
                : SettingsFormState.Applied);
        }

        if (!response.Success)
        {
            ShowStatus(response.ResourceKey, response.Severity, response.ResourceArguments);
            return;
        }

        ApplyToExistingElements = saveMode is SettingsSaveMode.AllElements or SettingsSaveMode.SelectedElements;
        SaveMode = saveMode;
        ShowStatus(
            response.ResourceKey,
            response.Severity,
            response.ResourceArguments);
    }

    private static string CreateLayerProfileFingerprint(ElementLayerProfile profile) =>
        JsonSerializer.Serialize(profile.Normalize());

    private static string CreateDefaultProfileFingerprint(
        TimberElementDefaultProfile profile) =>
        JsonSerializer.Serialize(profile.Normalize());

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

    private bool TryBuildDefaultProfile(
        bool includeAllowances,
        bool includeAnnotation,
        out TimberElementDefaultProfile profile)
    {
        var accepted = _acceptedDefaultProfile.Normalize();
        var roundingStepMm = accepted.GetCuttingLengthRoundingStepMm();
        var styles = accepted.Styles.ToList();

        if (includeAllowances &&
            !TryReadPositiveInteger(RoundingStepMmText, out roundingStepMm))
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

        if (includeAllowances)
        {
            styles = [];
            foreach (var row in DefaultRows)
            {
                if (!TryReadNonNegativeNumber(
                        row.CuttingAllowanceMmText,
                        out var cuttingAllowanceMm))
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

                styles.Add(new TimberElementDefaultStyle(
                    row.ElementType,
                    cuttingAllowanceMm));
            }
        }

        TimberAnnotationTextSettings? pendingAnnotationTextSettings = null;
        if (includeAnnotation &&
            !TryBuildPendingAnnotationTextSettings(out pendingAnnotationTextSettings))
        {
            profile = TimberElementDefaultProfile.CreateDefault();
            return false;
        }

        profile = new TimberElementDefaultProfile
        {
            CuttingLengthRoundingStepMm = roundingStepMm,
            DefaultAnnotationMode = includeAnnotation
                ? SelectedAnnotationMode
                : accepted.DefaultAnnotationMode,
            DefaultItemNumberLeaderStyle = includeAnnotation
                ? SelectedItemNumberLeaderStyle
                : accepted.DefaultItemNumberLeaderStyle,
            AnnotationScaleDenominator = includeAnnotation &&
                TryGetDrawingScaleDenominator(out var annotationScaleDenominator)
                    ? annotationScaleDenominator
                    : accepted.AnnotationScaleDenominator,
            DefaultAnnotationTextSettings = includeAnnotation
                ? pendingAnnotationTextSettings
                : accepted.DefaultAnnotationTextSettings,
            Styles = styles,
        }.Normalize();
        return true;
    }

    private void ReplaceRows(ElementLayerProfile profile)
    {
        var normalizedProfile = profile.Normalize();
        foreach (var existing in Rows)
        {
            existing.PropertyChanged -= EditableRow_PropertyChanged;
        }

        Rows.Clear();
        foreach (var type in Enum.GetValues<TimberElementType>())
        {
            var style = normalizedProfile.GetStyle(type);
            var color = ColorOptions.FirstOrDefault(option => option.Index == style.ColorIndex)
                ?? ColorOptions.First(option => option.Index == 8);
            var row = new LayerSettingsRow(
                type,
                TimberElementTypeDisplayNameProvider.GetDisplayName(type, _uiCulture),
                style.LayerName,
                color,
                style.LinetypeName,
                FormatLinetypeScale(style.LinetypeScale));
            row.PropertyChanged += EditableRow_PropertyChanged;
            Rows.Add(row);
        }
    }

    private void ReplaceDefaultRows(
        TimberElementDefaultProfile profile,
        bool replaceAnnotation = true)
    {
        RoundingStepMmText = Format(profile.GetCuttingLengthRoundingStepMm());
        if (replaceAnnotation)
        {
            SelectedAnnotationMode = profile.DefaultAnnotationMode;
            SelectedItemNumberLeaderStyle = profile.DefaultItemNumberLeaderStyle;
            var textSettings =
                TimberAnnotationTextSettingsRules.NormalizeStored(
                    profile.DefaultAnnotationTextSettings) ??
                TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
            AcceptAnnotationTextSettingsBaseline(textSettings);
            PushPendingTextSettingsToUi();
            NotifyScalePropertiesChanged();
        }
        foreach (var existing in DefaultRows)
        {
            existing.PropertyChanged -= EditableRow_PropertyChanged;
        }

        DefaultRows.Clear();
        foreach (var type in Enum.GetValues<TimberElementType>())
        {
            var row = new ElementDefaultSettingsRow(
                type,
                TimberElementTypeDisplayNameProvider.GetDisplayName(type, _uiCulture),
                Format(profile.GetCuttingAllowanceMm(type)));
            row.PropertyChanged += EditableRow_PropertyChanged;
            DefaultRows.Add(row);
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

    private void InitializeScaleSelections(int drawingDenominator)
    {
        _drawingCustomScaleText = drawingDenominator.ToString(CultureInfo.InvariantCulture);
        _selectedDrawingScalePreset =
            TimberAnnotationScaleSettingsRules.GetPreset(drawingDenominator);
        NotifyScalePropertiesChanged();
    }

    private void OnScaleSelectionChanged()
    {
        NotifyScalePropertiesChanged();
        UpdateFormState();
    }

    private void NotifyScalePropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedDrawingScalePreset));
        OnPropertyChanged(nameof(DrawingCustomScaleText));
        OnPropertyChanged(nameof(IsDrawingCustomScale));
        OnPropertyChanged(nameof(HasDrawingScaleError));
        OnPropertyChanged(nameof(CurrentDrawingScaleText));
        OnPropertyChanged(nameof(DefaultNewElementScaleText));
        OnPropertyChanged(nameof(ScaleValidationText));
        OnPropertyChanged(nameof(PreviewDimensionText));
        OnPropertyChanged(nameof(PreviewItemNumberText));
        OnPropertyChanged(nameof(PreviewSlopeText));
        OnPropertyChanged(nameof(PreviewBlockScale));
        OnPropertyChanged(nameof(PreviewDimensionModelText));
        OnPropertyChanged(nameof(PreviewDimensionPaperText));
        OnPropertyChanged(nameof(PreviewItemNumberModelText));
        OnPropertyChanged(nameof(PreviewItemNumberPaperText));
        OnPropertyChanged(nameof(PreviewSlopeModelText));
        OnPropertyChanged(nameof(PreviewSlopePaperText));
        OnPropertyChanged(nameof(PreviewBlockModelText));
        OnPropertyChanged(nameof(PreviewBlockPaperText));
        OnPropertyChanged(nameof(PreviewPresentationScale));
        NotifyAnnotationTextModelHeightsChanged();
    }

    private void NotifyAnnotationPreviewChanged() =>
        OnPropertyChanged(nameof(SelectedAnnotationPreviewResourceUri));

    private bool TryGetDrawingScaleDenominator(out int denominator) =>
        TryGetScaleDenominator(
            SelectedDrawingScalePreset,
            DrawingCustomScaleText,
            out denominator);

    private static bool TryGetScaleDenominator(
        TimberAnnotationScalePreset preset,
        string customText,
        out int denominator)
    {
        if (preset != TimberAnnotationScalePreset.Custom)
        {
            denominator = TimberAnnotationScaleSettingsRules.GetPresetDenominator(
                preset,
                TimberAnnotationScaleRules.DefaultDenominator);
            return true;
        }

        return TryReadScaleDenominator(customText, out denominator);
    }

    private static bool TryReadScaleDenominator(string text, out int denominator) =>
        int.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out denominator) &&
        TimberAnnotationScaleRules.IsValidDenominator(denominator);

    private string FormatScalePreview(double value, string unit)
    {
        var formatted = value.ToString("0.###", _uiCulture);
        return string.IsNullOrEmpty(unit) ? formatted : $"{formatted} {unit}";
    }

    private string FormatMeasurementLine(
        string resourceKey,
        double value,
        string unit,
        string valuePrefix = "") =>
        string.Concat(
            UiStrings.GetString(resourceKey, _uiCulture),
            ": ",
            valuePrefix,
            FormatScalePreview(value, unit));

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

    private void AnnotationPresetSelector_PreviewKeyDown(
        object sender,
        WpfKeyEventArgs e)
    {
        if (e.Key != WpfKey.Enter ||
            sender is not System.Windows.Controls.ListBox
                { SelectedItem: AnnotationPresetOption option })
        {
            return;
        }

        SelectedAnnotationPreset = option.Preset;
        e.Handled = true;
    }

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

    private void RefreshLayerNameOptions(
        IEnumerable<CadLayerPreset> presets,
        ElementLayerProfile profile)
    {
        var presetArray = presets.ToArray();
        _layerPresets = presetArray
            .GroupBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var candidates = presetArray
            .Select(preset => preset.Name)
            .Concat(profile.Normalize().Styles.Select(style => style.LayerName))
            .Select(name => new CadLayerNameCandidate(name));
        var sorted = CadLayerNameRules.SelectUsableLocalNames(candidates);
        LayerNameOptions = new ObservableCollection<string>(sorted);
    }

    private void ApplyAcceptedLayerNames(ElementLayerProfile profile)
    {
        foreach (var row in Rows)
        {
            row.LayerName = profile.GetStyle(row.ElementType).LayerName;
        }

        InitializeExistingLayerBaselines();
    }

    private void InitializeExistingLayerBaselines()
    {
        foreach (var row in Rows)
        {
            if (_layerPresets.TryGetValue(row.LayerName, out var preset) &&
                TryReadLinetypeScale(row.LinetypeScaleText, out var currentScale))
            {
                row.SetExistingLayerBaseline(preset, currentScale);
            }
        }
    }

    private void LayerNameComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox
            {
                DataContext: LayerSettingsRow row,
            } ||
            e.AddedItems.Count == 0 ||
            e.AddedItems[0] is not string selectedName)
        {
            return;
        }

        HydrateExistingLayer(row, selectedName);
    }

    private void LayerNameComboBox_LostKeyboardFocus(
        object sender,
        WpfKeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox
            {
                DataContext: LayerSettingsRow row,
            } comboBox)
        {
            HydrateExistingLayer(row, comboBox.Text);
        }
    }

    private void HydrateExistingLayer(LayerSettingsRow row, string layerName)
    {
        if (!_layerPresets.TryGetValue(layerName.Trim(), out var preset))
        {
            return;
        }

        var currentScale = TryReadLinetypeScale(row.LinetypeScaleText, out var parsedScale)
            ? parsedScale
            : ElementLayerProfile.GetDefaultLinetypeScale(row.ElementType);
        var scaleResolution = preset.HasMixedEntityLinetypeScales
            ? new CadLayerScaleHydrationResult(
                currentScale,
                loadedFromEntities: false,
                hasMixedValues: true)
            : preset.UniformEntityLinetypeScale is { } scale
                ? new CadLayerScaleHydrationResult(
                    scale,
                    loadedFromEntities: true,
                    hasMixedValues: false)
                : new CadLayerScaleHydrationResult(
                    currentScale,
                    loadedFromEntities: false,
                    hasMixedValues: false);
        var color = ColorOptions.FirstOrDefault(option =>
            option.Index == preset.AciColorIndex) ??
            LayerColorOption.Create(preset.AciColorIndex, _uiCulture);
        row.HydrateFromExistingLayer(
            preset,
            color,
            FormatLinetypeScale(scaleResolution.Value),
            scaleResolution.Value);

        if (scaleResolution.HasMixedValues)
        {
            ShowStatus(
                "SettingsWindow_Layers_MixedLinetypeScale",
                StatusBannerSeverity.Information);
        }
    }

    private void LayerSettingsWindow_Loaded(object sender, RoutedEventArgs e) =>
        _languageSelectionReady = true;

    private void LanguageSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_languageSelectionReady ||
            _synchronizingLanguageSelection ||
            LanguageSelector.SelectedValue is not string languageCode)
        {
            return;
        }

        SelectedLanguageCode = languageCode;
        _languageWorkflow.TryApplyUserSelection(languageCode);
    }

    private void AppLanguageService_LanguageChanged(object? sender, AppLanguageChangedEventArgs e)
    {
        _synchronizingLanguageSelection = true;
        try
        {
            SelectedLanguageCode = e.LanguageCode;
        }
        finally
        {
            _synchronizingLanguageSelection = false;
        }

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
            SettingsSaveMode.NewElementsOnly);

        var colorOptions = new ObservableCollection<LayerColorOption>(
            LayerColorOption.CreateAll(_uiCulture));
        var annotationModeOptions = new ObservableCollection<AnnotationModeOption>(
            selectionSet.AnnotationModes
                .Select(option => new AnnotationModeOption(
                    option.Value,
                    option.DisplayName,
                    UiStrings.GetString(
                        option.Value switch
                        {
                            TimberAnnotationMode.ItemNumberLeader =>
                                "SettingsWindow_Annotation_ItemNumberHelp",
                            TimberAnnotationMode.DimensionsLeader =>
                                "SettingsWindow_Annotation_DimensionsHelp",
                            TimberAnnotationMode.NoAnnotations =>
                                "SettingsWindow_Annotation_NoAnnotationsHelp",
                            _ => "SettingsWindow_Annotation_FullLabelHelp",
                        },
                        _uiCulture))));
        var annotationPresetOptions = new ObservableCollection<AnnotationPresetOption>(
            SettingsAnnotationPresetRules.All.Select(definition =>
                AnnotationPresetOption.Create(definition, _uiCulture)));
        var itemNumberLeaderStyleOptions =
            new ObservableCollection<ItemNumberLeaderStyleOption>(
                selectionSet.ItemNumberLeaderStyles
                    .Select(option => new ItemNumberLeaderStyleOption(
                        option.Value,
                        option.DisplayName)));
        var annotationScaleOptions = new ObservableCollection<AnnotationScaleOption>(
        [
            new(TimberAnnotationScalePreset.Scale25, "1:25"),
            new(TimberAnnotationScalePreset.Scale50, "1:50"),
            new(TimberAnnotationScalePreset.Scale75, "1:75"),
            new(TimberAnnotationScalePreset.Scale100, "1:100"),
            new(
                TimberAnnotationScalePreset.Custom,
                UiStrings.GetString(
                    "SettingsWindow_AnnotationScale_Custom",
                    _uiCulture)),
        ]);
        foreach (var language in LanguageOptions)
        {
            language.AccessibilityName = string.Format(
                _uiCulture,
                UiStrings.GetString(
                    "SettingsWindow_Language_SelectFormat",
                    _uiCulture),
                language.NativeName);
        }
        ColorOptions = colorOptions;
        AnnotationModeOptions = annotationModeOptions;
        AnnotationPresetOptions = annotationPresetOptions;
        ItemNumberLeaderStyleOptions = itemNumberLeaderStyleOptions;
        AnnotationScaleOptions = annotationScaleOptions;
        Visual.RefreshLocalization(_uiCulture);

        foreach (var row in Rows)
        {
            row.SelectedColor = ColorOptions.First(
                option => option.Index == selectedColors[row.ElementType]);
        }

        _selectedAnnotationMode = selectionSet.SelectedAnnotationMode;
        _selectedItemNumberLeaderStyle = selectionSet.SelectedItemNumberLeaderStyle;
        _selectedAnnotationPreset = SettingsAnnotationPresetRules.FromProduction(
            _selectedAnnotationMode,
            _selectedItemNumberLeaderStyle);
        OnPropertyChanged(nameof(SelectedAnnotationMode));
        OnPropertyChanged(nameof(IsItemNumberLeaderStyleEnabled));
        OnPropertyChanged(nameof(SelectedItemNumberLeaderStyle));
        OnPropertyChanged(nameof(SelectedAnnotationPreset));
        NotifyAnnotationPreviewChanged();
        NotifyScalePropertiesChanged();
        RefreshAnnotationTextLocalization();
    }

    private void SynchronizeSelectedAnnotationPreset()
    {
        var preset = SettingsAnnotationPresetRules.FromProduction(
            _selectedAnnotationMode,
            _selectedItemNumberLeaderStyle);
        if (_selectedAnnotationPreset == preset)
        {
            return;
        }

        _selectedAnnotationPreset = preset;
        OnPropertyChanged(nameof(SelectedAnnotationPreset));
        NotifyAnnotationPreviewChanged();
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
        var (backgroundKey, borderKey, textKey) = _statusBannerState.Severity switch
        {
            StatusBannerSeverity.Error => (
                "SettingsErrorBackgroundBrush",
                "SettingsErrorBorderBrush",
                "SettingsErrorTextBrush"),
            StatusBannerSeverity.Warning => (
                "SettingsWarningBackgroundBrush",
                "SettingsWarningBorderBrush",
                "SettingsWarningTextBrush"),
            StatusBannerSeverity.Information => (
                "SettingsInfoBackgroundBrush",
                "SettingsInfoBorderBrush",
                "SettingsInfoTextBrush"),
            _ => (
                "SettingsSuccessBackgroundBrush",
                "SettingsSuccessBorderBrush",
                "SettingsSuccessTextBrush"),
        };
        StatusBanner.Background = (WpfBrush)FindResource(backgroundKey);
        StatusBanner.BorderBrush = (WpfBrush)FindResource(borderKey);
        StatusBannerText.Foreground = (WpfBrush)FindResource(textKey);
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
        SaveWindowPreferences();
        _statusBannerTimer.Stop();
        _statusBannerTimer.Tick -= StatusBannerTimer_Tick;
        AppLanguageService.LanguageChanged -= AppLanguageService_LanguageChanged;
        Loaded -= LayerSettingsWindow_Loaded;
        Visual.SectionChanged -= Visual_SectionChanged;
        Visual.ThemeChanged -= Visual_ThemeChanged;
        Closed -= LayerSettingsWindow_Closed;
        _statusBannerState.Clear();
    }

    private void UpdateActionButtons()
    {
        var section = Visual.SelectedSection;
        RestoreDefaultsButton.Visibility =
            section is SettingsWindowTabKind.Layers or
                SettingsWindowTabKind.Manufacturing
                ? Visibility.Visible
                : Visibility.Collapsed;
        LayersFooterActions.Visibility =
            section == SettingsWindowTabKind.Layers
                ? Visibility.Visible
                : Visibility.Collapsed;
        ManufacturingFooterActions.Visibility =
            section == SettingsWindowTabKind.Manufacturing
                ? Visibility.Visible
                : Visibility.Collapsed;
        AnnotationFooterActions.Visibility =
            section == SettingsWindowTabKind.Annotation
                ? Visibility.Visible
                : Visibility.Collapsed;
        LanguageFooterActions.Visibility =
            section == SettingsWindowTabKind.Language
                ? Visibility.Visible
                : Visibility.Collapsed;
        LayersApplyButton.IsDefault =
            section == SettingsWindowTabKind.Layers;
        ManufacturingApplyButton.IsDefault =
            section == SettingsWindowTabKind.Manufacturing;
        SaveNewElementsButton.IsDefault =
            section == SettingsWindowTabKind.Annotation;
    }

    private void SetFooterActionsEnabled(bool isEnabled)
    {
        LayersFooterActions.IsEnabled = isEnabled;
        ManufacturingFooterActions.IsEnabled = isEnabled;
        AnnotationFooterActions.IsEnabled = isEnabled;
        LanguageFooterActions.IsEnabled = isEnabled;
    }

    private void EditableRow_PropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        UpdateFormState();

    private string CreateLayersUiFingerprint() =>
        JsonSerializer.Serialize(new
        {
            Layers = Rows.Select(row => new
            {
                row.ElementType,
                row.LayerName,
                ColorIndex = row.SelectedColor.Index,
                row.SelectedLinetypeName,
                row.LinetypeScaleText,
            }),
        });

    private string CreateAllowancesUiFingerprint() =>
        JsonSerializer.Serialize(new
        {
            Defaults = DefaultRows.Select(row => new
            {
                row.ElementType,
                row.CuttingAllowanceMmText,
            }),
            RoundingStepMmText,
        });

    private string CreateAnnotationUiFingerprint() =>
        JsonSerializer.Serialize(new
        {
            SelectedAnnotationMode,
            SelectedItemNumberLeaderStyle,
            SelectedDrawingScalePreset,
            DrawingCustomScaleText,
            AnnotationText = CreateAnnotationTextFingerprintPayload(),
        });

    internal bool LayersDirty =>
        _acceptedLayersUiFingerprint is not null &&
        !string.Equals(
            _acceptedLayersUiFingerprint,
            CreateLayersUiFingerprint(),
            StringComparison.Ordinal);

    internal bool AllowancesDirty =>
        _acceptedAllowancesUiFingerprint is not null &&
        !string.Equals(
            _acceptedAllowancesUiFingerprint,
            CreateAllowancesUiFingerprint(),
            StringComparison.Ordinal);

    internal bool AnnotationDirty =>
        _acceptedAnnotationUiFingerprint is not null &&
        !string.Equals(
            _acceptedAnnotationUiFingerprint,
            CreateAnnotationUiFingerprint(),
            StringComparison.Ordinal);

    private bool HasAnyPendingChanges =>
        LayersDirty ||
        AllowancesDirty ||
        AnnotationDirty;

    private void AcceptUiSectionBaselines(SettingsSectionScope scope)
    {
        if (scope.HasFlag(SettingsSectionScope.Layers))
        {
            _acceptedLayersUiFingerprint = CreateLayersUiFingerprint();
        }
        if (scope.HasFlag(SettingsSectionScope.Allowances))
        {
            _acceptedAllowancesUiFingerprint = CreateAllowancesUiFingerprint();
        }
        if (scope.HasFlag(SettingsSectionScope.Annotation))
        {
            _acceptedAnnotationUiFingerprint = CreateAnnotationUiFingerprint();
        }
    }

    private void UpdateFormState()
    {
        if (_acceptedLayersUiFingerprint is null || Rows.Count == 0)
        {
            return;
        }

        Visual.SetFormState(
            HasAnyPendingChanges
                ? SettingsFormState.UnsavedChanges
                : SettingsFormState.NoChanges);
    }

    private void ApplyTheme(SettingsTheme theme)
    {
        var source = theme == SettingsTheme.Dark
            ? "Design/SettingsColors.Dark.xaml"
            : "Design/SettingsColors.Light.xaml";
        Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri(
                $"/AcKrovy.AutoCAD;component/UI/{source}",
                UriKind.RelativeOrAbsolute),
        };
        RenderStatusBanner();
    }

    private void ApplyWindowPreferences(SettingsUiPreferences preferences)
    {
        Width = preferences.Width;
        Height = preferences.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        if (preferences.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowPreferences()
    {
        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            SettingsUiPreferencesStore.Save(new SettingsUiPreferences
            {
                Theme = Visual.SelectedTheme,
                SelectedSection = Visual.SelectedSection,
                Width = Math.Max(bounds.Width, SettingsFashionLookRules.MinimumWindowWidth),
                Height = Math.Max(bounds.Height, SettingsFashionLookRules.MinimumWindowHeight),
                Left = bounds.Left,
                Top = bounds.Top,
                IsMaximized = WindowState == WindowState.Maximized,
            });
        }
        catch
        {
            // UI preference persistence must never block closing or touch the DWG.
        }
    }

    private void Visual_SectionChanged(object? sender, EventArgs e) => UpdateActionButtons();

    private void Visual_ThemeChanged(object? sender, EventArgs e) =>
        ApplyTheme(Visual.SelectedTheme);

    private void LightTheme_Click(object sender, RoutedEventArgs e) =>
        Visual.SelectedTheme = SettingsTheme.Light;

    private void DarkTheme_Click(object sender, RoutedEventArgs e) =>
        Visual.SelectedTheme = SettingsTheme.Dark;
}

public sealed class LayerSettingsRow : INotifyPropertyChanged
{
    private string _elementLabel;
    private string _layerName;
    private LayerColorOption _selectedColor;
    private string _selectedLinetypeName;
    private string _linetypeScaleText;
    private bool _hydratingLayerPreset;
    private string? _selectedExistingLayerName;
    private int? _loadedLayerAciIndex;
    private string? _loadedLayerLinetypeName;
    private double? _loadedEntityLinetypeScale;
    private bool _hasLayerPropertyOverrides;

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
        _layerName = string.IsNullOrWhiteSpace(layerName)
            ? ElementLayerProfile.CreateDefault().GetStyle(elementType).LayerName
            : layerName.Trim();
        _selectedColor = selectedColor;
        _selectedLinetypeName = CadLinetypeNames.Normalize(selectedLinetypeName);
        _linetypeScaleText = linetypeScaleText;
    }

    public TimberElementType ElementType { get; }
    public string? SelectedExistingLayerName => _selectedExistingLayerName;
    public int? LoadedLayerAciIndex => _loadedLayerAciIndex;
    public string? LoadedLayerLinetypeName => _loadedLayerLinetypeName;
    public double? LoadedEntityLinetypeScale => _loadedEntityLinetypeScale;
    public bool HasExplicitPropertyChanges => _hasLayerPropertyOverrides;
    public bool HasLayerPropertyOverrides
    {
        get => _hasLayerPropertyOverrides;
        private set
        {
            if (_hasLayerPropertyOverrides == value)
            {
                return;
            }

            _hasLayerPropertyOverrides = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasExplicitPropertyChanges));
        }
    }

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
            if (!_hydratingLayerPreset &&
                !string.Equals(
                    _selectedExistingLayerName,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                ClearExistingLayerBaseline();
            }
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
            OnPropertyChanged(nameof(AciColorIndex));
            OnPropertyChanged(nameof(PreviewToolTip));
            RecalculateLayerOverrides();
        }
    }

    public int AciColorIndex
    {
        get => _selectedColor.Index;
        set
        {
            if (!AciColorPickerRules.IsValid(value) || _selectedColor.Index == value)
            {
                return;
            }

            _selectedColor = LayerColorOption.Create(value, AppLanguageService.CurrentUiCulture);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedColor));
            OnPropertyChanged(nameof(PreviewToolTip));
            RecalculateLayerOverrides();
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
            OnPropertyChanged(nameof(PreviewToolTip));
            RecalculateLayerOverrides();
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
            OnPropertyChanged(nameof(PreviewToolTip));
            RecalculateLayerOverrides();
        }
    }

    public string PreviewToolTip =>
        $"{SelectedLinetypeName} · × {LinetypeScaleText} · ACI {SelectedColor.Index}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetExistingLayerBaseline(CadLayerPreset preset, double currentScale)
    {
        _selectedExistingLayerName = preset.Name;
        _loadedLayerAciIndex = preset.AciColorIndex;
        _loadedLayerLinetypeName = preset.LinetypeName;
        _loadedEntityLinetypeScale =
            preset.UniformEntityLinetypeScale ?? currentScale;
        NotifyLayerBaselineChanged();
        RecalculateLayerOverrides();
    }

    public void HydrateFromExistingLayer(
        CadLayerPreset preset,
        LayerColorOption color,
        string linetypeScaleText,
        double effectiveScale)
    {
        _hydratingLayerPreset = true;
        LayerName = preset.Name;
        SelectedColor = color;
        SelectedLinetypeName = preset.LinetypeName;
        LinetypeScaleText = linetypeScaleText;
        _hydratingLayerPreset = false;

        _selectedExistingLayerName = preset.Name;
        _loadedLayerAciIndex = preset.AciColorIndex;
        _loadedLayerLinetypeName = preset.LinetypeName;
        _loadedEntityLinetypeScale = effectiveScale;
        NotifyLayerBaselineChanged();
        HasLayerPropertyOverrides = false;
    }

    private void ClearExistingLayerBaseline()
    {
        _selectedExistingLayerName = null;
        _loadedLayerAciIndex = null;
        _loadedLayerLinetypeName = null;
        _loadedEntityLinetypeScale = null;
        NotifyLayerBaselineChanged();
        HasLayerPropertyOverrides = false;
    }

    private void RecalculateLayerOverrides()
    {
        if (_hydratingLayerPreset ||
            (_selectedExistingLayerName is not null &&
             (_loadedLayerAciIndex is null ||
              _loadedLayerLinetypeName is null ||
              _loadedEntityLinetypeScale is null)))
        {
            return;
        }

        if (_selectedExistingLayerName is null)
        {
            HasLayerPropertyOverrides = true;
            return;
        }

        var loadedAciIndex = _loadedLayerAciIndex.GetValueOrDefault();
        var loadedLinetypeName = _loadedLayerLinetypeName!;
        var loadedLinetypeScale = _loadedEntityLinetypeScale.GetValueOrDefault();
        HasLayerPropertyOverrides =
            AciColorIndex != loadedAciIndex ||
            !string.Equals(
                SelectedLinetypeName,
                loadedLinetypeName,
                StringComparison.OrdinalIgnoreCase) ||
            !TryParseScale(LinetypeScaleText, out var currentScale) ||
            Math.Abs(currentScale - loadedLinetypeScale) >
                CadLayerScaleHydrationRules.ComparisonTolerance;
    }

    private void NotifyLayerBaselineChanged()
    {
        OnPropertyChanged(nameof(SelectedExistingLayerName));
        OnPropertyChanged(nameof(LoadedLayerAciIndex));
        OnPropertyChanged(nameof(LoadedLayerLinetypeName));
        OnPropertyChanged(nameof(LoadedEntityLinetypeScale));
    }

    private static bool TryParseScale(string text, out double scale) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            AppLanguageService.CurrentUiCulture,
            out scale) ||
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out scale) ||
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.GetCultureInfo("sk-SK"),
            out scale);

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
    public string UnitLabel => "mm";
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

public sealed record AnnotationModeOption(
    TimberAnnotationMode Mode,
    string DisplayName,
    string Description);

public sealed record AnnotationPresetOption(
    SettingsAnnotationPreset Preset,
    string StableId,
    string PreviewResourceUri,
    string DisplayName,
    string AccessibilityName,
    string PreviewItemText,
    bool ShowsLeader,
    bool ShowsDisabledSymbol,
    bool ShowsFullLabel,
    bool ShowsStandaloneItem,
    bool ShowsDimensionsOnly,
    bool ShowsCombined)
{
    public static AnnotationPresetOption Create(
        SettingsAnnotationPresetDefinition definition,
        CultureInfo culture)
    {
        var displayName = SettingsAnnotationPresetDisplayNameProvider.GetDisplayName(
            definition.Preset,
            culture);
        return new AnnotationPresetOption(
            definition.Preset,
            definition.StableId,
            AnnotationPreviewResourceMap.GetPackUri(definition.Preset),
            displayName,
            displayName,
            definition.Preset switch
            {
                SettingsAnnotationPreset.ItemPlain => "K1",
                SettingsAnnotationPreset.ItemCircle => "K3",
                SettingsAnnotationPreset.ItemRectangle => "K4",
                SettingsAnnotationPreset.ItemSlot => "K5",
                SettingsAnnotationPreset.DimensionsWithItemCircle => "K6",
                SettingsAnnotationPreset.DimensionsWithItemRectangle => "K7",
                SettingsAnnotationPreset.DimensionsWithItemSlot => "K8",
                _ => "K2",
            },
            definition.Preset is not
                SettingsAnnotationPreset.NoAnnotations and not
                SettingsAnnotationPreset.FullLabel,
            definition.Preset == SettingsAnnotationPreset.NoAnnotations,
            definition.Preset == SettingsAnnotationPreset.FullLabel,
            definition.Preset is
                SettingsAnnotationPreset.ItemPlain or
                SettingsAnnotationPreset.ItemCircle or
                SettingsAnnotationPreset.ItemRectangle or
                SettingsAnnotationPreset.ItemSlot,
            definition.Preset == SettingsAnnotationPreset.DimensionsOnly,
            definition.Preset is
                SettingsAnnotationPreset.DimensionsWithItemCircle or
                SettingsAnnotationPreset.DimensionsWithItemRectangle or
                SettingsAnnotationPreset.DimensionsWithItemSlot);
    }
}

public sealed record ItemNumberLeaderStyleOption(
    ItemNumberLeaderStyle Style,
    string DisplayName);

public sealed class LanguageCardOption : INotifyPropertyChanged
{
    private string _accessibilityName = string.Empty;

    public LanguageCardOption(string code, string upperCode, string nativeName)
    {
        var flags = LanguageFlagResourceMap.Get(code);
        Code = code;
        UpperCode = upperCode;
        NativeName = nativeName;
        ColorFlagUri = flags.ColorPackUri;
        BlackFlagUri = flags.BlackPackUri;
    }

    public string Code { get; }
    public string UpperCode { get; }
    public string NativeName { get; }
    public string ColorFlagUri { get; }
    public string BlackFlagUri { get; }
    public string AccessibilityName
    {
        get => _accessibilityName;
        set
        {
            if (string.Equals(_accessibilityName, value, StringComparison.Ordinal))
            {
                return;
            }

            _accessibilityName = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(AccessibilityName)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

[Flags]
internal enum SettingsSectionScope
{
    None = 0,
    Layers = 1,
    Allowances = 2,
    Annotation = 4,
    AllEditable = Layers | Allowances | Annotation,
}

internal sealed record SettingsApplyRequest(
    ElementLayerProfile Profile,
    TimberElementDefaultProfile DefaultProfile,
    string LanguageCode,
    SettingsSaveMode SaveMode,
    TimberAnnotationSettingsRequest? AnnotationSettings,
    bool LayerProfileChanged,
    bool DefaultProfileChanged,
    IReadOnlyList<CadLayerOverrideIntent> LayerOverrideIntents);

internal sealed record AnnotationScaleSettingsState(
    bool HasDrawingOverride,
    int DrawingDenominator,
    int EffectiveDenominator);

public sealed record AnnotationScaleOption(
    TimberAnnotationScalePreset Preset,
    string DisplayName);

internal sealed record SettingsApplyResponse(
    bool Success,
    bool ProfileAccepted,
    StatusBannerSeverity Severity,
    string ResourceKey,
    object[] ResourceArguments,
    IReadOnlyList<string> AvailableLinetypeNames,
    IReadOnlyList<CadLayerPreset> AvailableLayerPresets,
    ElementLayerProfile? AppliedProfile = null);
