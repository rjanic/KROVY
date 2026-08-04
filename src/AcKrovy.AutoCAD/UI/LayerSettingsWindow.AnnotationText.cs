using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfMessageBox = System.Windows.MessageBox;

namespace AcKrovy.AutoCAD.UI;

public partial class LayerSettingsWindow
{
    private enum TextStyleEditorMode
    {
        None = 0,
        Create = 1,
        Edit = 2,
        Rename = 3,
    }

    private TimberAnnotationTextStylePresetLibrary _textStyleLibrary =
        TimberAnnotationTextStylePresetLibrary.CreateDefault();
    private TimberAnnotationTextSettings _acceptedTextSettings =
        TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
    private TimberAnnotationTextSettings _pendingTextSettings =
        TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
    private bool _itemCodeRoleDirty;
    private bool _dimensionRoleDirty;
    private bool _slopeRoleDirty;
    private bool _itemCodeRoleMixed;
    private bool _dimensionRoleMixed;
    private bool _slopeRoleMixed;
    private bool _synchronizingTextSettings;
    private TextStyleEditorMode _textStyleEditorMode;
    private string? _editingTextStyleStableId;
    private ObservableCollection<TextStylePresetListItem> _textStylePresetItems = [];
    private ObservableCollection<TextStylePresetListItem> _customTextStylePresetItems = [];
    private ObservableCollection<TextStyleKindOption> _textStyleKindOptions = [];
    private ObservableCollection<string> _availableFontNames = [];
    private TextStylePresetListItem? _selectedTextStylePresetItem;
    private string _textStyleEditorName = string.Empty;
    private string _textStyleEditorFont = TimberAnnotationTextStylePresetRules.ClassicFontFile;
    private string _textStyleEditorWidthFactorText = "1";
    private string _textStyleEditorObliqueAngleText = "0";
    private AnnotationTextStyleKind _itemCodeStyleKind = AnnotationTextStyleKind.Classic;
    private AnnotationTextStyleKind _dimensionStyleKind = AnnotationTextStyleKind.Classic;
    private AnnotationTextStyleKind _slopeStyleKind = AnnotationTextStyleKind.Classic;
    private string? _itemCodeCustomStyleName;
    private string? _dimensionCustomStyleName;
    private string? _slopeCustomStyleName;
    private string _itemCodePaperHeightText = Format(
        TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm);
    private string _dimensionPaperHeightText = Format(
        TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm);
    private string _slopePaperHeightText = Format(
        TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm);

    public ObservableCollection<TextStylePresetListItem> TextStylePresetItems
    {
        get => _textStylePresetItems;
        private set
        {
            _textStylePresetItems = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<TextStylePresetListItem> CustomTextStylePresetItems
    {
        get => _customTextStylePresetItems;
        private set
        {
            _customTextStylePresetItems = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<TextStyleKindOption> TextStyleKindOptions
    {
        get => _textStyleKindOptions;
        private set
        {
            _textStyleKindOptions = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> AvailableFontNames
    {
        get => _availableFontNames;
        private set
        {
            _availableFontNames = value;
            OnPropertyChanged();
        }
    }

    public TextStylePresetListItem? SelectedTextStylePresetItem
    {
        get => _selectedTextStylePresetItem;
        set
        {
            if (ReferenceEquals(_selectedTextStylePresetItem, value))
            {
                return;
            }

            _selectedTextStylePresetItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditSelectedTextStylePreset));
            OnPropertyChanged(nameof(SelectedTextStylePreviewFontFamily));
            OnPropertyChanged(nameof(SelectedTextStylePreviewFontWeight));
            OnPropertyChanged(nameof(IsApproximatePreview));
            OnPropertyChanged(nameof(SelectedTextStylePreviewStatus));
            OnPropertyChanged(nameof(SelectedTextStyleAutoCadName));
            OnPropertyChanged(nameof(SelectedTextStyleFontName));
            OnPropertyChanged(nameof(SelectedTextStyleType));
        }
    }

    public bool CanEditSelectedTextStylePreset =>
        SelectedTextStylePresetItem?.Kind == TimberAnnotationTextStylePresetKind.User;

    public bool IsTextStyleEditorVisible =>
        _textStyleEditorMode != TextStyleEditorMode.None;

    public string TextStyleEditorName
    {
        get => _textStyleEditorName;
        set
        {
            if (string.Equals(_textStyleEditorName, value, StringComparison.Ordinal))
            {
                return;
            }

            _textStyleEditorName = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string TextStyleEditorFont
    {
        get => _textStyleEditorFont;
        set
        {
            if (string.Equals(_textStyleEditorFont, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _textStyleEditorFont = value ?? TimberAnnotationTextStylePresetRules.ClassicFontFile;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextStyleEditorPreviewFontFamily));
        }
    }

    public string TextStyleEditorWidthFactorText
    {
        get => _textStyleEditorWidthFactorText;
        set
        {
            if (string.Equals(_textStyleEditorWidthFactorText, value, StringComparison.Ordinal))
            {
                return;
            }

            _textStyleEditorWidthFactorText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string TextStyleEditorObliqueAngleText
    {
        get => _textStyleEditorObliqueAngleText;
        set
        {
            if (string.Equals(_textStyleEditorObliqueAngleText, value, StringComparison.Ordinal))
            {
                return;
            }

            _textStyleEditorObliqueAngleText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string TextStyleEditorPreviewSample => "Aa Bb 012";

    public WpfFontFamily TextStyleEditorPreviewFontFamily =>
        CreatePreviewFontFamily(TextStyleEditorFont);

    private AnnotationTextPreviewPresentation SelectedTextStylePreview =>
        ResolvePreviewPresentation(
            SelectedTextStylePresetItem?.FontFile
            ?? TimberAnnotationTextStylePresetRules.ArialFontFile);

    public WpfFontFamily SelectedTextStylePreviewFontFamily =>
        SelectedTextStylePreview.FontFamily;

    public FontWeight SelectedTextStylePreviewFontWeight =>
        SelectedTextStylePreview.FontWeight;

    public bool IsApproximatePreview =>
        SelectedTextStylePreview.IsApproximate;

    public string SelectedTextStylePreviewStatus =>
        SelectedTextStylePresetItem is null
            ? string.Empty
            : ResolvePreviewStatus(SelectedTextStylePresetItem.AutoCadTextStyleName);

    public string SelectedTextStyleAutoCadName =>
        SelectedTextStylePresetItem?.AutoCadTextStyleName ?? string.Empty;

    public string SelectedTextStyleFontName =>
        SelectedTextStylePresetItem?.FontFile ?? string.Empty;

    public string SelectedTextStyleType =>
        string.Equals(
            Path.GetExtension(SelectedTextStylePresetItem?.FontFile),
            ".shx",
            StringComparison.OrdinalIgnoreCase)
            ? "SHX"
            : "TrueType";

    public AnnotationTextStyleKind ItemCodeStyleKind
    {
        get => _itemCodeStyleKind;
        set => SetRoleStyleKind(TimberAnnotationTextRole.ItemCode, value);
    }

    public AnnotationTextStyleKind DimensionStyleKind
    {
        get => _dimensionStyleKind;
        set => SetRoleStyleKind(TimberAnnotationTextRole.Dimension, value);
    }

    public AnnotationTextStyleKind SlopeStyleKind
    {
        get => _slopeStyleKind;
        set => SetRoleStyleKind(TimberAnnotationTextRole.Slope, value);
    }

    public string? ItemCodeCustomStyleName
    {
        get => _itemCodeCustomStyleName;
        set => SetRoleCustomStyleName(TimberAnnotationTextRole.ItemCode, value);
    }

    public string? DimensionCustomStyleName
    {
        get => _dimensionCustomStyleName;
        set => SetRoleCustomStyleName(TimberAnnotationTextRole.Dimension, value);
    }

    public string? SlopeCustomStyleName
    {
        get => _slopeCustomStyleName;
        set => SetRoleCustomStyleName(TimberAnnotationTextRole.Slope, value);
    }

    public bool IsItemCodeCustomStyleVisible =>
        ItemCodeStyleKind == AnnotationTextStyleKind.Custom;

    public bool IsDimensionCustomStyleVisible =>
        DimensionStyleKind == AnnotationTextStyleKind.Custom;

    public bool IsSlopeCustomStyleVisible =>
        SlopeStyleKind == AnnotationTextStyleKind.Custom;

    public string ItemCodePaperHeightText
    {
        get => _itemCodePaperHeightText;
        set => SetRolePaperHeightText(TimberAnnotationTextRole.ItemCode, value);
    }

    public string DimensionPaperHeightText
    {
        get => _dimensionPaperHeightText;
        set => SetRolePaperHeightText(TimberAnnotationTextRole.Dimension, value);
    }

    public string SlopePaperHeightText
    {
        get => _slopePaperHeightText;
        set => SetRolePaperHeightText(TimberAnnotationTextRole.Slope, value);
    }

    public string ItemCodeModelHeightText => FormatRoleModelHeight(TimberAnnotationTextRole.ItemCode);
    public string DimensionModelHeightText => FormatRoleModelHeight(TimberAnnotationTextRole.Dimension);
    public string SlopeModelHeightText => FormatRoleModelHeight(TimberAnnotationTextRole.Slope);

    public string ItemCodePreviewSample =>
        _itemCodeRoleMixed
            ? UiStrings.GetString("SettingsWindow_AnnotationText_Mixed", _uiCulture)
            : "K1";

    public string DimensionPreviewSample =>
        _dimensionRoleMixed
            ? UiStrings.GetString("SettingsWindow_AnnotationText_Mixed", _uiCulture)
            : "80/160";

    public string SlopePreviewSample =>
        _slopeRoleMixed
            ? UiStrings.GetString("SettingsWindow_AnnotationText_Mixed", _uiCulture)
            : "35\u00B0";

    private AnnotationTextPreviewPresentation ItemCodePreview =>
        ResolvePreviewPresentation(ResolveFontFileForStyleName(
            _pendingTextSettings.ItemCodeTextStyleName));

    public WpfFontFamily ItemCodePreviewFontFamily =>
        ItemCodePreview.FontFamily;

    public FontWeight ItemCodePreviewFontWeight =>
        ItemCodePreview.FontWeight;

    private AnnotationTextPreviewPresentation DimensionPreview =>
        ResolvePreviewPresentation(ResolveFontFileForStyleName(
            _pendingTextSettings.DimensionTextStyleName));

    public WpfFontFamily DimensionPreviewFontFamily =>
        DimensionPreview.FontFamily;

    public FontWeight DimensionPreviewFontWeight =>
        DimensionPreview.FontWeight;

    private AnnotationTextPreviewPresentation SlopePreview =>
        ResolvePreviewPresentation(ResolveFontFileForStyleName(
            _pendingTextSettings.SlopeTextStyleName));

    public WpfFontFamily SlopePreviewFontFamily =>
        SlopePreview.FontFamily;

    public FontWeight SlopePreviewFontWeight =>
        SlopePreview.FontWeight;

    public string ItemCodePreviewStatus =>
        ResolveRolePreviewStatus(_pendingTextSettings.ItemCodeTextStyleName);

    public string DimensionPreviewStatus =>
        ResolveRolePreviewStatus(_pendingTextSettings.DimensionTextStyleName);

    public string SlopePreviewStatus =>
        ResolveRolePreviewStatus(_pendingTextSettings.SlopeTextStyleName);

    private void InitializeAnnotationTextSettings(TimberElementDefaultProfile profile)
    {
        _textStyleLibrary = TimberAnnotationTextStylePresetLibraryStore.Load();
        AvailableFontNames = new ObservableCollection<string>(
            AutoCadFontDiscoveryService.ListAvailableFonts()
                .Select(font => font.DisplayName));
        var settings =
            TimberAnnotationTextSettingsRules.NormalizeStored(
                profile.DefaultAnnotationTextSettings) ??
            TimberAnnotationTextStylePresetRules.CreateFreshProfileTextSettings();
        AcceptAnnotationTextSettingsBaseline(settings);
        RefreshTextStylePresetLists(selectStableId: null);
        RefreshTextStyleKindOptions();
        PushPendingTextSettingsToUi();
        CloseTextStyleEditor();
    }

    private void AcceptAnnotationTextSettingsBaseline(TimberAnnotationTextSettings settings)
    {
        var normalized = TimberAnnotationTextSettingsRules.ValidateAndNormalize(settings);
        _acceptedTextSettings = normalized;
        _pendingTextSettings = normalized;
        _itemCodeRoleDirty = false;
        _dimensionRoleDirty = false;
        _slopeRoleDirty = false;
        _itemCodeRoleMixed = false;
        _dimensionRoleMixed = false;
        _slopeRoleMixed = false;
    }

    private void RefreshAnnotationTextLocalization()
    {
        RefreshTextStyleKindOptions();
        RefreshTextStylePresetLists(
            SelectedTextStylePresetItem?.StableId);
        NotifyAnnotationTextPropertiesChanged();
    }

    private void RefreshTextStyleKindOptions()
    {
        var selectedItem = ItemCodeStyleKind;
        var selectedDimension = DimensionStyleKind;
        var selectedSlope = SlopeStyleKind;
        TextStyleKindOptions = new ObservableCollection<TextStyleKindOption>(
        [
            new(
                AnnotationTextStyleKind.Architectural,
                UiStrings.GetString("SettingsTextStylePreset_Architectural", _uiCulture)),
            new(
                AnnotationTextStyleKind.Classic,
                UiStrings.GetString("SettingsTextStylePreset_Classic", _uiCulture)),
            new(
                AnnotationTextStyleKind.Technical,
                UiStrings.GetString("SettingsTextStylePreset_Technical", _uiCulture)),
            new(
                AnnotationTextStyleKind.Arial,
                UiStrings.GetString("SettingsTextStylePreset_Arial", _uiCulture)),
            new(
                AnnotationTextStyleKind.Custom,
                UiStrings.GetString("SettingsWindow_AnnotationText_Custom", _uiCulture)),
        ]);
        _synchronizingTextSettings = true;
        try
        {
            _itemCodeStyleKind = selectedItem;
            _dimensionStyleKind = selectedDimension;
            _slopeStyleKind = selectedSlope;
            OnPropertyChanged(nameof(ItemCodeStyleKind));
            OnPropertyChanged(nameof(DimensionStyleKind));
            OnPropertyChanged(nameof(SlopeStyleKind));
        }
        finally
        {
            _synchronizingTextSettings = false;
        }
    }

    private void RefreshTextStylePresetLists(string? selectStableId)
    {
        var selectedCustomItem = ItemCodeCustomStyleName;
        var selectedCustomDimension = DimensionCustomStyleName;
        var selectedCustomSlope = SlopeCustomStyleName;
        var items = new List<TextStylePresetListItem>();
        foreach (var definition in TimberAnnotationTextStylePresetRules.GetBuiltInDefinitions())
        {
            items.Add(TextStylePresetListItem.FromDefinition(definition, _uiCulture));
        }

        items.Add(TextStylePresetListItem.FromCustomCategory(_uiCulture));

        foreach (var preset in _textStyleLibrary.Normalize().Presets)
        {
            items.Add(TextStylePresetListItem.FromUserPreset(preset));
        }

        TextStylePresetItems = new ObservableCollection<TextStylePresetListItem>(items);
        CustomTextStylePresetItems = new ObservableCollection<TextStylePresetListItem>(
            items.Where(item => item.Kind == TimberAnnotationTextStylePresetKind.User));

        SelectedTextStylePresetItem =
            TextStylePresetItems.FirstOrDefault(item =>
                selectStableId is not null &&
                string.Equals(item.StableId, selectStableId, StringComparison.OrdinalIgnoreCase))
            ?? TextStylePresetItems.FirstOrDefault();

        _synchronizingTextSettings = true;
        try
        {
            _itemCodeCustomStyleName = ResolveExistingCustomStyleName(selectedCustomItem);
            _dimensionCustomStyleName = ResolveExistingCustomStyleName(selectedCustomDimension);
            _slopeCustomStyleName = ResolveExistingCustomStyleName(selectedCustomSlope);
            OnPropertyChanged(nameof(ItemCodeCustomStyleName));
            OnPropertyChanged(nameof(DimensionCustomStyleName));
            OnPropertyChanged(nameof(SlopeCustomStyleName));
        }
        finally
        {
            _synchronizingTextSettings = false;
        }
    }

    private string? ResolveExistingCustomStyleName(string? styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName))
        {
            return CustomTextStylePresetItems.FirstOrDefault()?.AutoCadTextStyleName;
        }

        var match = CustomTextStylePresetItems.FirstOrDefault(item =>
            string.Equals(
                item.AutoCadTextStyleName,
                styleName,
                StringComparison.OrdinalIgnoreCase));
        return match?.AutoCadTextStyleName
            ?? CustomTextStylePresetItems.FirstOrDefault()?.AutoCadTextStyleName;
    }

    private void PushPendingTextSettingsToUi()
    {
        _synchronizingTextSettings = true;
        try
        {
            _itemCodeStyleKind = ResolveStyleKind(_pendingTextSettings.ItemCodeTextStyleName);
            _dimensionStyleKind = ResolveStyleKind(_pendingTextSettings.DimensionTextStyleName);
            _slopeStyleKind = ResolveStyleKind(_pendingTextSettings.SlopeTextStyleName);
            _itemCodeCustomStyleName = ResolveCustomStyleNameForRole(
                TimberAnnotationTextRole.ItemCode);
            _dimensionCustomStyleName = ResolveCustomStyleNameForRole(
                TimberAnnotationTextRole.Dimension);
            _slopeCustomStyleName = ResolveCustomStyleNameForRole(
                TimberAnnotationTextRole.Slope);
            _itemCodePaperHeightText = Format(_pendingTextSettings.ItemCodePaperHeightMm);
            _dimensionPaperHeightText = Format(_pendingTextSettings.DimensionPaperHeightMm);
            _slopePaperHeightText = Format(_pendingTextSettings.SlopePaperHeightMm);
        }
        finally
        {
            _synchronizingTextSettings = false;
        }

        NotifyAnnotationTextPropertiesChanged();
    }

    private string? ResolveCustomStyleNameForRole(TimberAnnotationTextRole role)
    {
        var styleName = _pendingTextSettings.GetTextStyleName(role);
        if (ResolveStyleKind(styleName) != AnnotationTextStyleKind.Custom)
        {
            return CustomTextStylePresetItems.FirstOrDefault()?.AutoCadTextStyleName;
        }

        return ResolveExistingCustomStyleName(styleName);
    }

    private static AnnotationTextStyleKind ResolveStyleKind(string styleName)
    {
        if (TimberAnnotationTextStylePresetRules.TryResolveBuiltInByStyleName(
                styleName,
                out var definition) &&
            definition is not null)
        {
            return definition.BuiltInPreset switch
            {
                TimberAnnotationBuiltInTextStylePreset.Architectural =>
                    AnnotationTextStyleKind.Architectural,
                TimberAnnotationBuiltInTextStylePreset.Classic =>
                    AnnotationTextStyleKind.Classic,
                TimberAnnotationBuiltInTextStylePreset.Technical =>
                    AnnotationTextStyleKind.Technical,
                TimberAnnotationBuiltInTextStylePreset.Arial =>
                    AnnotationTextStyleKind.Arial,
                _ => AnnotationTextStyleKind.Arial,
            };
        }

        return AnnotationTextStyleKind.Custom;
    }

    private void SetRoleStyleKind(
        TimberAnnotationTextRole role,
        AnnotationTextStyleKind value)
    {
        if (GetRoleStyleKind(role) == value)
        {
            return;
        }

        SetRoleStyleKindField(role, value);
        OnPropertyChanged(GetRoleStyleKindPropertyName(role));
        OnPropertyChanged(GetRoleCustomVisiblePropertyName(role));
        if (_synchronizingTextSettings)
        {
            return;
        }

        var styleName = value switch
        {
            AnnotationTextStyleKind.Architectural =>
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            AnnotationTextStyleKind.Classic =>
                TimberAnnotationTextStylePresetRules.ClassicStyleName,
            AnnotationTextStyleKind.Technical =>
                TimberAnnotationTextStylePresetRules.TechnicalStyleName,
            AnnotationTextStyleKind.Arial =>
                TimberAnnotationTextStylePresetRules.ArialStyleName,
            _ => GetRoleCustomStyleName(role)
                ?? CustomTextStylePresetItems.FirstOrDefault()?.AutoCadTextStyleName
                ?? TimberAnnotationTextStylePresetRules.ArialStyleName,
        };
        ApplyPendingRoleStyle(role, styleName);
    }

    private void SetRoleCustomStyleName(
        TimberAnnotationTextRole role,
        string? value)
    {
        if (string.Equals(GetRoleCustomStyleName(role), value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetRoleCustomStyleNameField(role, value);
        OnPropertyChanged(GetRoleCustomStylePropertyName(role));
        if (_synchronizingTextSettings || GetRoleStyleKind(role) != AnnotationTextStyleKind.Custom)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            ApplyPendingRoleStyle(role, value!);
        }
    }

    private void SetRolePaperHeightText(
        TimberAnnotationTextRole role,
        string value)
    {
        if (string.Equals(GetRolePaperHeightText(role), value, StringComparison.Ordinal))
        {
            return;
        }

        SetRolePaperHeightTextField(role, value ?? string.Empty);
        OnPropertyChanged(GetRolePaperHeightPropertyName(role));
        if (_synchronizingTextSettings)
        {
            return;
        }

        if (!TryReadPaperHeight(role, value, out var height))
        {
            UpdateFormState();
            OnPropertyChanged(GetRoleModelHeightPropertyName(role));
            return;
        }

        MarkRoleDirty(role);
        ClearRoleMixed(role);
        _pendingTextSettings = _pendingTextSettings.WithRole(
            role,
            _pendingTextSettings.GetTextStyleName(role),
            height);
        OnPropertyChanged(GetRoleModelHeightPropertyName(role));
        NotifyAnnotationTextPreviewChanged(role);
        UpdateFormState();
    }

    private void ApplyPendingRoleStyle(TimberAnnotationTextRole role, string styleName)
    {
        MarkRoleDirty(role);
        ClearRoleMixed(role);
        _pendingTextSettings = _pendingTextSettings.WithRole(
            role,
            styleName.Trim(),
            _pendingTextSettings.GetPaperHeightMm(role));
        NotifyAnnotationTextPreviewChanged(role);
        UpdateFormState();
    }

    private bool TryBuildPendingAnnotationTextSettings(
        out TimberAnnotationTextSettings settings)
    {
        if (!TryReadPaperHeight(
                TimberAnnotationTextRole.ItemCode,
                ItemCodePaperHeightText,
                out var itemHeight) ||
            !TryReadPaperHeight(
                TimberAnnotationTextRole.Dimension,
                DimensionPaperHeightText,
                out var dimensionHeight) ||
            !TryReadPaperHeight(
                TimberAnnotationTextRole.Slope,
                SlopePaperHeightText,
                out var slopeHeight))
        {
            WpfMessageBox.Show(
                UiStrings.GetString(
                    "SettingsWindow_AnnotationText_InvalidHeight",
                    _uiCulture),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            settings = _pendingTextSettings;
            return false;
        }

        try
        {
            settings = TimberAnnotationTextSettingsRules.ValidateAndNormalize(
                new TimberAnnotationTextSettings(
                    ResolvePendingStyleName(TimberAnnotationTextRole.ItemCode),
                    ResolvePendingStyleName(TimberAnnotationTextRole.Dimension),
                    ResolvePendingStyleName(TimberAnnotationTextRole.Slope),
                    itemHeight,
                    dimensionHeight,
                    slopeHeight));
            _pendingTextSettings = settings;
            return true;
        }
        catch (ArgumentException)
        {
            WpfMessageBox.Show(
                UiStrings.GetString(
                    "SettingsWindow_AnnotationText_InvalidHeight",
                    _uiCulture),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            settings = _pendingTextSettings;
            return false;
        }
    }

    private string ResolvePendingStyleName(TimberAnnotationTextRole role)
    {
        var kind = GetRoleStyleKind(role);
        return kind switch
        {
            AnnotationTextStyleKind.Architectural =>
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            AnnotationTextStyleKind.Classic =>
                TimberAnnotationTextStylePresetRules.ClassicStyleName,
            AnnotationTextStyleKind.Technical =>
                TimberAnnotationTextStylePresetRules.TechnicalStyleName,
            AnnotationTextStyleKind.Arial =>
                TimberAnnotationTextStylePresetRules.ArialStyleName,
            _ => GetRoleCustomStyleName(role)
                ?? _pendingTextSettings.GetTextStyleName(role),
        };
    }

    /// <summary>
    /// Builds a role-scoped patch from dirty flags versus the accepted baseline.
    /// Mixed roles that the user never touched stay Unchanged so Apply Selection /
    /// Apply All preserve per-element values for those roles.
    /// </summary>
    private TimberAnnotationTextSettingsPatch BuildPendingAnnotationTextPatch(
        TimberAnnotationTextSettings pendingSettings)
    {
        return TimberAnnotationTextSettingsPatch.ForRoles(
            BuildRolePatch(
                TimberAnnotationTextRole.ItemCode,
                pendingSettings,
                _itemCodeRoleDirty,
                _itemCodeRoleMixed),
            BuildRolePatch(
                TimberAnnotationTextRole.Dimension,
                pendingSettings,
                _dimensionRoleDirty,
                _dimensionRoleMixed),
            BuildRolePatch(
                TimberAnnotationTextRole.Slope,
                pendingSettings,
                _slopeRoleDirty,
                _slopeRoleMixed));
    }

    private TimberAnnotationTextRolePatch BuildRolePatch(
        TimberAnnotationTextRole role,
        TimberAnnotationTextSettings pendingSettings,
        bool dirty,
        bool mixed)
    {
        if (mixed && !dirty)
        {
            return TimberAnnotationTextRolePatch.Unchanged;
        }

        var pendingStyle = pendingSettings.GetTextStyleName(role);
        var pendingHeight = pendingSettings.GetPaperHeightMm(role);
        var acceptedStyle = _acceptedTextSettings.GetTextStyleName(role);
        var acceptedHeight = _acceptedTextSettings.GetPaperHeightMm(role);
        if (!dirty &&
            string.Equals(pendingStyle, acceptedStyle, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(pendingHeight - acceptedHeight) < 0.000001d)
        {
            return TimberAnnotationTextRolePatch.Unchanged;
        }

        if (!dirty)
        {
            return TimberAnnotationTextRolePatch.Unchanged;
        }

        return TimberAnnotationTextRolePatch.Set(role, pendingStyle, pendingHeight);
    }

    private void AnnotationTextNewStyle_Click(object sender, RoutedEventArgs e)
    {
        _textStyleEditorMode = TextStyleEditorMode.Create;
        _editingTextStyleStableId = null;
        TextStyleEditorName = string.Empty;
        TextStyleEditorFont =
            AvailableFontNames.FirstOrDefault(name =>
                string.Equals(
                    name,
                    TimberAnnotationTextStylePresetRules.ClassicFontFile,
                    StringComparison.OrdinalIgnoreCase))
            ?? AvailableFontNames.FirstOrDefault()
            ?? TimberAnnotationTextStylePresetRules.ClassicFontFile;
        TextStyleEditorWidthFactorText = Format(
            TimberAnnotationTextStylePresetRules.DefaultWidthFactor);
        TextStyleEditorObliqueAngleText = Format(
            TimberAnnotationTextStylePresetRules.DefaultObliqueAngleDegrees);
        OnPropertyChanged(nameof(IsTextStyleEditorVisible));
    }

    private void AnnotationTextEditStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedUserPreset(out var preset))
        {
            return;
        }

        OpenTextStyleEditor(TextStyleEditorMode.Edit, preset);
    }

    private void AnnotationTextRenameStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedUserPreset(out var preset))
        {
            return;
        }

        OpenTextStyleEditor(TextStyleEditorMode.Rename, preset);
    }

    private void AnnotationTextDeleteStyle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedUserPreset(out var preset))
        {
            return;
        }

        ReplacePendingStyleWithArial(preset.AutoCadTextStyleName);
        _textStyleLibrary.Presets.RemoveAll(existing =>
            string.Equals(
                existing.StableId,
                preset.StableId,
                StringComparison.OrdinalIgnoreCase));
        TimberAnnotationTextStylePresetLibraryStore.Save(_textStyleLibrary);
        RefreshTextStylePresetLists(selectStableId: null);
        PushPendingTextSettingsToUi();
        UpdateFormState();
    }

    private void ReplacePendingStyleWithArial(string deletedStyleName)
    {
        foreach (var role in Enum.GetValues<TimberAnnotationTextRole>())
        {
            if (string.Equals(
                    _pendingTextSettings.GetTextStyleName(role),
                    deletedStyleName,
                    StringComparison.OrdinalIgnoreCase))
            {
                _pendingTextSettings = _pendingTextSettings.WithRole(
                    role,
                    TimberAnnotationTextStylePresetRules.ArialStyleName,
                    _pendingTextSettings.GetPaperHeightMm(role));
            }
        }
    }

    private void AnnotationTextSaveStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_textStyleEditorMode == TextStyleEditorMode.None)
        {
            return;
        }

        if (!AutoCadFontDiscoveryService.IsFontAvailable(TextStyleEditorFont))
        {
            WpfMessageBox.Show(
                UiStrings.GetString(
                    "SettingsWindow_AnnotationText_MissingFont",
                    _uiCulture),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryReadPositiveDouble(TextStyleEditorWidthFactorText, out var widthFactor) ||
            !TimberAnnotationTextStylePresetRules.IsValidWidthFactor(widthFactor) ||
            !TryReadSignedDouble(TextStyleEditorObliqueAngleText, out var oblique) ||
            !TimberAnnotationTextStylePresetRules.IsValidObliqueAngle(oblique))
        {
            WpfMessageBox.Show(
                UiStrings.GetString(
                    "SettingsWindow_AnnotationText_InvalidHeight",
                    _uiCulture),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TimberAnnotationTextStylePresetRules.IsValidDisplayName(TextStyleEditorName))
        {
            WpfMessageBox.Show(
                UiStrings.GetString(
                    "SettingsWindow_AnnotationText_DuplicateName",
                    _uiCulture),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var library = _textStyleLibrary.Normalize();
        try
        {
            if (_textStyleEditorMode == TextStyleEditorMode.Create)
            {
                var stableId = Guid.NewGuid().ToString("N");
                var created = TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(
                    new TimberAnnotationUserTextStylePreset
                    {
                        StableId = stableId,
                        DisplayName = TextStyleEditorName,
                        FontFile = TextStyleEditorFont,
                        WidthFactor = widthFactor,
                        ObliqueAngleDegrees = oblique,
                    },
                    library.Presets);
                library.Presets.Add(created);
                _editingTextStyleStableId = created.StableId;
            }
            else
            {
                var index = library.Presets.FindIndex(preset =>
                    string.Equals(
                        preset.StableId,
                        _editingTextStyleStableId,
                        StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    CloseTextStyleEditor();
                    return;
                }

                var existing = library.Presets[index];
                var updated = TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(
                    new TimberAnnotationUserTextStylePreset
                    {
                        StableId = existing.StableId,
                        DisplayName = TextStyleEditorName,
                        FontFile = _textStyleEditorMode == TextStyleEditorMode.Rename
                            ? existing.FontFile
                            : TextStyleEditorFont,
                        WidthFactor = _textStyleEditorMode == TextStyleEditorMode.Rename
                            ? existing.WidthFactor
                            : widthFactor,
                        ObliqueAngleDegrees = _textStyleEditorMode == TextStyleEditorMode.Rename
                            ? existing.ObliqueAngleDegrees
                            : oblique,
                    },
                    library.Presets);
                library.Presets[index] = updated;
                _editingTextStyleStableId = updated.StableId;
            }
        }
        catch (ArgumentException)
        {
            WpfMessageBox.Show(
                UiStrings.GetString(
                    "SettingsWindow_AnnotationText_DuplicateName",
                    _uiCulture),
                UiStrings.MessageDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _textStyleLibrary = library;
        TimberAnnotationTextStylePresetLibraryStore.Save(_textStyleLibrary);
        RefreshTextStylePresetLists(_editingTextStyleStableId);
        CloseTextStyleEditor();
        PushPendingTextSettingsToUi();
    }

    private void AnnotationTextCancelStyle_Click(object sender, RoutedEventArgs e) =>
        CloseTextStyleEditor();

    private void OpenTextStyleEditor(
        TextStyleEditorMode mode,
        TimberAnnotationUserTextStylePreset preset)
    {
        _textStyleEditorMode = mode;
        _editingTextStyleStableId = preset.StableId;
        TextStyleEditorName = preset.DisplayName;
        TextStyleEditorFont = preset.FontFile;
        TextStyleEditorWidthFactorText = Format(preset.WidthFactor);
        TextStyleEditorObliqueAngleText = Format(preset.ObliqueAngleDegrees);
        OnPropertyChanged(nameof(IsTextStyleEditorVisible));
    }

    private void CloseTextStyleEditor()
    {
        _textStyleEditorMode = TextStyleEditorMode.None;
        _editingTextStyleStableId = null;
        OnPropertyChanged(nameof(IsTextStyleEditorVisible));
    }

    private bool TryGetSelectedUserPreset(out TimberAnnotationUserTextStylePreset preset)
    {
        preset = null!;
        var selected = SelectedTextStylePresetItem;
        if (selected is null ||
            selected.Kind != TimberAnnotationTextStylePresetKind.User)
        {
            return false;
        }

        var match = _textStyleLibrary.Normalize().Presets.FirstOrDefault(item =>
            string.Equals(item.StableId, selected.StableId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        preset = match;
        return true;
    }

    private string ResolveFontFileForStyleName(string styleName)
    {
        if (TimberAnnotationTextStylePresetRules.TryResolveBuiltInByStyleName(
                styleName,
                out var builtIn) &&
            builtIn is not null)
        {
            return builtIn.FontFile;
        }

        var user = _textStyleLibrary.Normalize().Presets.FirstOrDefault(preset =>
            string.Equals(
                preset.AutoCadTextStyleName,
                styleName,
                StringComparison.OrdinalIgnoreCase));
        return user?.FontFile
            ?? TimberAnnotationTextStylePresetRules.ArialFontFile;
    }

    private string ResolvePreviewStatus(string styleName)
    {
        var fontFile = ResolveFontFileForStyleName(styleName);
        if (!AutoCadFontDiscoveryService.IsFontAvailable(fontFile))
        {
            return UiStrings.GetString(
                "SettingsWindow_AnnotationText_FontFallbackArial",
                _uiCulture);
        }

        return string.Equals(
                Path.GetExtension(fontFile),
                ".shx",
                StringComparison.OrdinalIgnoreCase)
            ? UiStrings.GetString(
                "SettingsWindow_AnnotationText_ShxPreviewApproximation",
                _uiCulture)
            : string.Empty;
    }

    private string ResolveRolePreviewStatus(string styleName)
    {
        var fontFile = ResolveFontFileForStyleName(styleName);
        return !IsShxFont(fontFile) &&
            !AutoCadFontDiscoveryService.IsFontAvailable(fontFile)
                ? UiStrings.GetString(
                    "SettingsWindow_AnnotationText_FontFallbackArial",
                    _uiCulture)
                : string.Empty;
    }

    private string FormatRoleModelHeight(TimberAnnotationTextRole role)
    {
        if (IsRoleMixed(role) && !IsRoleDirty(role))
        {
            return UiStrings.GetString("SettingsWindow_AnnotationText_Mixed", _uiCulture);
        }

        if (!TryGetDrawingScaleDenominator(out var denominator) ||
            !TryReadPaperHeight(role, GetRolePaperHeightText(role), out var paperHeight))
        {
            return string.Empty;
        }

        var modelHeight = TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            paperHeight,
            denominator);
        return UiStrings.Format(
            UiStrings.GetString(
                "SettingsWindow_AnnotationText_ModelHeightFormat",
                _uiCulture),
            modelHeight.ToString("0.###", _uiCulture));
    }

    private void NotifyAnnotationTextPropertiesChanged()
    {
        OnPropertyChanged(nameof(ItemCodeStyleKind));
        OnPropertyChanged(nameof(DimensionStyleKind));
        OnPropertyChanged(nameof(SlopeStyleKind));
        OnPropertyChanged(nameof(ItemCodeCustomStyleName));
        OnPropertyChanged(nameof(DimensionCustomStyleName));
        OnPropertyChanged(nameof(SlopeCustomStyleName));
        OnPropertyChanged(nameof(IsItemCodeCustomStyleVisible));
        OnPropertyChanged(nameof(IsDimensionCustomStyleVisible));
        OnPropertyChanged(nameof(IsSlopeCustomStyleVisible));
        OnPropertyChanged(nameof(ItemCodePaperHeightText));
        OnPropertyChanged(nameof(DimensionPaperHeightText));
        OnPropertyChanged(nameof(SlopePaperHeightText));
        OnPropertyChanged(nameof(ItemCodeModelHeightText));
        OnPropertyChanged(nameof(DimensionModelHeightText));
        OnPropertyChanged(nameof(SlopeModelHeightText));
        OnPropertyChanged(nameof(ItemCodePreviewSample));
        OnPropertyChanged(nameof(DimensionPreviewSample));
        OnPropertyChanged(nameof(SlopePreviewSample));
        OnPropertyChanged(nameof(ItemCodePreviewFontFamily));
        OnPropertyChanged(nameof(ItemCodePreviewFontWeight));
        OnPropertyChanged(nameof(DimensionPreviewFontFamily));
        OnPropertyChanged(nameof(DimensionPreviewFontWeight));
        OnPropertyChanged(nameof(SlopePreviewFontFamily));
        OnPropertyChanged(nameof(SlopePreviewFontWeight));
        OnPropertyChanged(nameof(ItemCodePreviewStatus));
        OnPropertyChanged(nameof(DimensionPreviewStatus));
        OnPropertyChanged(nameof(SlopePreviewStatus));
        OnPropertyChanged(nameof(CanEditSelectedTextStylePreset));
        OnPropertyChanged(nameof(IsTextStyleEditorVisible));
        OnPropertyChanged(nameof(TextStyleEditorPreviewFontFamily));
        OnPropertyChanged(nameof(SelectedTextStylePreviewFontFamily));
        OnPropertyChanged(nameof(SelectedTextStylePreviewFontWeight));
        OnPropertyChanged(nameof(IsApproximatePreview));
        OnPropertyChanged(nameof(SelectedTextStylePreviewStatus));
        OnPropertyChanged(nameof(SelectedTextStyleAutoCadName));
        OnPropertyChanged(nameof(SelectedTextStyleFontName));
        OnPropertyChanged(nameof(SelectedTextStyleType));
    }

    private void NotifyAnnotationTextPreviewChanged(TimberAnnotationTextRole role)
    {
        OnPropertyChanged(GetRoleModelHeightPropertyName(role));
        switch (role)
        {
            case TimberAnnotationTextRole.ItemCode:
                OnPropertyChanged(nameof(ItemCodePreviewSample));
                OnPropertyChanged(nameof(ItemCodePreviewFontFamily));
                OnPropertyChanged(nameof(ItemCodePreviewFontWeight));
                OnPropertyChanged(nameof(ItemCodePreviewStatus));
                break;
            case TimberAnnotationTextRole.Dimension:
                OnPropertyChanged(nameof(DimensionPreviewSample));
                OnPropertyChanged(nameof(DimensionPreviewFontFamily));
                OnPropertyChanged(nameof(DimensionPreviewFontWeight));
                OnPropertyChanged(nameof(DimensionPreviewStatus));
                break;
            case TimberAnnotationTextRole.Slope:
                OnPropertyChanged(nameof(SlopePreviewSample));
                OnPropertyChanged(nameof(SlopePreviewFontFamily));
                OnPropertyChanged(nameof(SlopePreviewFontWeight));
                OnPropertyChanged(nameof(SlopePreviewStatus));
                break;
        }
    }

    private void NotifyAnnotationTextModelHeightsChanged()
    {
        OnPropertyChanged(nameof(ItemCodeModelHeightText));
        OnPropertyChanged(nameof(DimensionModelHeightText));
        OnPropertyChanged(nameof(SlopeModelHeightText));
    }

    private object CreateAnnotationTextFingerprintPayload() => new
    {
        _pendingTextSettings.ItemCodeTextStyleName,
        _pendingTextSettings.DimensionTextStyleName,
        _pendingTextSettings.SlopeTextStyleName,
        _pendingTextSettings.ItemCodePaperHeightMm,
        _pendingTextSettings.DimensionPaperHeightMm,
        _pendingTextSettings.SlopePaperHeightMm,
        ItemCodePaperHeightText,
        DimensionPaperHeightText,
        SlopePaperHeightText,
        _itemCodeRoleDirty,
        _dimensionRoleDirty,
        _slopeRoleDirty,
    };

    private static WpfFontFamily CreatePreviewFontFamily(string fontFile) =>
        ResolvePreviewPresentation(fontFile).FontFamily;

    private static AnnotationTextPreviewPresentation ResolvePreviewPresentation(
        string fontFile)
    {
        if (string.Equals(
                fontFile,
                TimberAnnotationTextStylePresetRules.ClassicFontFile,
                StringComparison.OrdinalIgnoreCase))
        {
            return new AnnotationTextPreviewPresentation(
                new WpfFontFamily("Times New Roman"),
                FontWeights.Normal,
                IsApproximate: true);
        }
        if (string.Equals(
                fontFile,
                TimberAnnotationTextStylePresetRules.TechnicalFontFile,
                StringComparison.OrdinalIgnoreCase))
        {
            return new AnnotationTextPreviewPresentation(
                new WpfFontFamily("Bahnschrift"),
                FontWeights.Light,
                IsApproximate: true);
        }
        if (IsShxFont(fontFile))
        {
            return new AnnotationTextPreviewPresentation(
                new WpfFontFamily("Segoe UI"),
                FontWeights.Light,
                IsApproximate: true);
        }

        try
        {
            return new AnnotationTextPreviewPresentation(
                new WpfFontFamily(fontFile),
                FontWeights.Normal,
                IsApproximate: false);
        }
        catch
        {
            return new AnnotationTextPreviewPresentation(
                new WpfFontFamily(
                    TimberAnnotationTextStylePresetRules.ArialFontFile),
                FontWeights.Normal,
                IsApproximate: false);
        }
    }

    private static bool IsShxFont(string? fontFile) =>
        string.Equals(
            Path.GetExtension(fontFile),
            ".shx",
            StringComparison.OrdinalIgnoreCase);

    private static bool TryReadPaperHeight(
        TimberAnnotationTextRole role,
        string? raw,
        out double value)
    {
        if (TryReadSignedDouble(raw, out value) &&
            TimberAnnotationTextSettingsRules.IsValidPaperHeightMm(role, value))
        {
            return true;
        }

        value = 0d;
        return false;
    }

    private static bool TryReadPositiveDouble(string? raw, out double value)
    {
        if (TryReadSignedDouble(raw, out value) && value > 0d)
        {
            return true;
        }

        value = 0d;
        return false;
    }

    private static bool TryReadSignedDouble(string? raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, SlovakCulture, out value) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        value = 0d;
        return false;
    }

    private AnnotationTextStyleKind GetRoleStyleKind(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => _itemCodeStyleKind,
            TimberAnnotationTextRole.Dimension => _dimensionStyleKind,
            TimberAnnotationTextRole.Slope => _slopeStyleKind,
            _ => AnnotationTextStyleKind.Classic,
        };

    private void SetRoleStyleKindField(
        TimberAnnotationTextRole role,
        AnnotationTextStyleKind value)
    {
        switch (role)
        {
            case TimberAnnotationTextRole.ItemCode:
                _itemCodeStyleKind = value;
                break;
            case TimberAnnotationTextRole.Dimension:
                _dimensionStyleKind = value;
                break;
            case TimberAnnotationTextRole.Slope:
                _slopeStyleKind = value;
                break;
        }
    }

    private string? GetRoleCustomStyleName(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => _itemCodeCustomStyleName,
            TimberAnnotationTextRole.Dimension => _dimensionCustomStyleName,
            TimberAnnotationTextRole.Slope => _slopeCustomStyleName,
            _ => null,
        };

    private void SetRoleCustomStyleNameField(
        TimberAnnotationTextRole role,
        string? value)
    {
        switch (role)
        {
            case TimberAnnotationTextRole.ItemCode:
                _itemCodeCustomStyleName = value;
                break;
            case TimberAnnotationTextRole.Dimension:
                _dimensionCustomStyleName = value;
                break;
            case TimberAnnotationTextRole.Slope:
                _slopeCustomStyleName = value;
                break;
        }
    }

    private string GetRolePaperHeightText(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => _itemCodePaperHeightText,
            TimberAnnotationTextRole.Dimension => _dimensionPaperHeightText,
            TimberAnnotationTextRole.Slope => _slopePaperHeightText,
            _ => string.Empty,
        };

    private void SetRolePaperHeightTextField(
        TimberAnnotationTextRole role,
        string value)
    {
        switch (role)
        {
            case TimberAnnotationTextRole.ItemCode:
                _itemCodePaperHeightText = value;
                break;
            case TimberAnnotationTextRole.Dimension:
                _dimensionPaperHeightText = value;
                break;
            case TimberAnnotationTextRole.Slope:
                _slopePaperHeightText = value;
                break;
        }
    }

    private void MarkRoleDirty(TimberAnnotationTextRole role)
    {
        switch (role)
        {
            case TimberAnnotationTextRole.ItemCode:
                _itemCodeRoleDirty = true;
                break;
            case TimberAnnotationTextRole.Dimension:
                _dimensionRoleDirty = true;
                break;
            case TimberAnnotationTextRole.Slope:
                _slopeRoleDirty = true;
                break;
        }
    }

    private bool IsRoleDirty(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => _itemCodeRoleDirty,
            TimberAnnotationTextRole.Dimension => _dimensionRoleDirty,
            TimberAnnotationTextRole.Slope => _slopeRoleDirty,
            _ => false,
        };

    private void ClearRoleMixed(TimberAnnotationTextRole role)
    {
        switch (role)
        {
            case TimberAnnotationTextRole.ItemCode:
                _itemCodeRoleMixed = false;
                break;
            case TimberAnnotationTextRole.Dimension:
                _dimensionRoleMixed = false;
                break;
            case TimberAnnotationTextRole.Slope:
                _slopeRoleMixed = false;
                break;
        }
    }

    private bool IsRoleMixed(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => _itemCodeRoleMixed,
            TimberAnnotationTextRole.Dimension => _dimensionRoleMixed,
            TimberAnnotationTextRole.Slope => _slopeRoleMixed,
            _ => false,
        };

    private static string GetRoleStyleKindPropertyName(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => nameof(ItemCodeStyleKind),
            TimberAnnotationTextRole.Dimension => nameof(DimensionStyleKind),
            _ => nameof(SlopeStyleKind),
        };

    private static string GetRoleCustomVisiblePropertyName(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => nameof(IsItemCodeCustomStyleVisible),
            TimberAnnotationTextRole.Dimension => nameof(IsDimensionCustomStyleVisible),
            _ => nameof(IsSlopeCustomStyleVisible),
        };

    private static string GetRoleCustomStylePropertyName(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => nameof(ItemCodeCustomStyleName),
            TimberAnnotationTextRole.Dimension => nameof(DimensionCustomStyleName),
            _ => nameof(SlopeCustomStyleName),
        };

    private static string GetRolePaperHeightPropertyName(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => nameof(ItemCodePaperHeightText),
            TimberAnnotationTextRole.Dimension => nameof(DimensionPaperHeightText),
            _ => nameof(SlopePaperHeightText),
        };

    private static string GetRoleModelHeightPropertyName(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => nameof(ItemCodeModelHeightText),
            TimberAnnotationTextRole.Dimension => nameof(DimensionModelHeightText),
            _ => nameof(SlopeModelHeightText),
        };
}

public enum AnnotationTextStyleKind
{
    Classic = 0,
    Architectural = 1,
    Technical = 2,
    Arial = 3,
    Custom = 4,
}

public sealed record TextStyleKindOption(
    AnnotationTextStyleKind Kind,
    string DisplayName);

internal readonly record struct AnnotationTextPreviewPresentation(
    WpfFontFamily FontFamily,
    FontWeight FontWeight,
    bool IsApproximate);

public sealed class TextStylePresetListItem
{
    private TextStylePresetListItem(
        string stableId,
        TimberAnnotationTextStylePresetKind kind,
        string displayName,
        string autoCadTextStyleName,
        string fontFile)
    {
        StableId = stableId;
        Kind = kind;
        DisplayName = displayName;
        AutoCadTextStyleName = autoCadTextStyleName;
        FontFile = fontFile;
    }

    public string StableId { get; }
    public TimberAnnotationTextStylePresetKind Kind { get; }
    public string DisplayName { get; }
    public string AutoCadTextStyleName { get; }
    public string FontFile { get; }

    public static TextStylePresetListItem FromDefinition(
        TimberAnnotationTextStylePresetDefinition definition,
        CultureInfo culture) =>
        new(
            definition.StableId,
            definition.Kind,
            SettingsTextStylePresetDisplayNameProvider.GetDisplayName(definition, culture),
            definition.AutoCadTextStyleName,
            definition.FontFile);

    public static TextStylePresetListItem FromCustomCategory(CultureInfo culture) =>
        new(
            "ui-custom-category",
            TimberAnnotationTextStylePresetKind.BuiltIn,
            UiStrings.GetString("SettingsWindow_AnnotationText_Custom", culture),
            string.Empty,
            TimberAnnotationTextStylePresetRules.ArialFontFile);

    public static TextStylePresetListItem FromUserPreset(
        TimberAnnotationUserTextStylePreset preset) =>
        new(
            preset.StableId,
            TimberAnnotationTextStylePresetKind.User,
            preset.DisplayName,
            preset.AutoCadTextStyleName,
            preset.FontFile);
}
