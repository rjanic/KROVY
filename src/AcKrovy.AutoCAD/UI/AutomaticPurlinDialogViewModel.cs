using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// In-memory Automatic-Purlin draft shared by read-only preview and explicit
/// production-edit dialog modes. All DWG writes remain outside the ViewModel.
/// </summary>
internal sealed class AutomaticPurlinDialogViewModel : INotifyPropertyChanged
{
    internal const double DefaultSeatingPercent = 25d;
    internal const double DefaultWallPlateEaveDistanceMm =
        RoofPurlinLayoutPersistenceRules.DefaultNewDraftWallPlateEaveDistanceMm;
    internal const double DefaultIntermediateEaveDistanceMm =
        RoofPurlinLayoutPersistenceRules.DefaultNewDraftIntermediateEaveDistanceMm;
    /// <summary>Plan-distance used when the user presses Add for an extra intermediate row.</summary>
    internal const double DefaultAddedIntermediateEaveDistanceMm = 1200d;
    private readonly HipRoofGeometry _geometry;
    private readonly RoofBoundaryIdentityProvenanceResult? _boundaryProvenance;
    private readonly CultureInfo _culture;
    private readonly AutomaticPurlinDialogMode _mode;
    private readonly int _existingAutomaticPurlinCount;
    private readonly List<AutomaticPurlinRidgeOption> _ridgeReferences;
    private bool _wallPlateEnabled;
    private bool _ridgeEnabled;
    private RoofRelativeElevationReferenceKind _referenceKind;
    private string _relativeReferenceText;
    private string _referenceLocalZText;
    private string _validationMessage = string.Empty;
    private string _previewDiagnosticReason = "None";
    private RoofAutomaticPurlinPlanError? _lastDatumResolutionError;
    private RoofAutomaticPurlinPlan? _previewPlan;
    private bool _currentDraftIsValid;
    private bool _isSchematicStale;
    private bool _placementModeConversionFailed;
    private bool _isRecalculating;
    /// <summary>
    /// When a field change nests inside Recalculate (e.g. LocalZ sync), queue one
    /// follow-up pass so Width/Height edits are not dropped mid-validation.
    /// </summary>
    private bool _recalculateQueued;
    private int _suppressRowChangedDepth;
    private bool _hasInclinedRidge;
    private readonly double _loadedRelativeReferenceMm;
    /// <summary>
    /// SourceEave-absolute WallPlate bottom used to expand product Place=0 under
    /// WallPlateBottom for Core bootstrap. Seeded from persisted
    /// WallPlateLowerEdgeHeightMm; refreshed from the last valid preview plan.
    /// </summary>
    private double? _wallPlateBottomEdgeBootstrapAbsoluteMm;
    private bool _relativeReferenceEdited;
    /// <summary>
    /// Last valid ExplicitLocalPlane LocalZ (mm). Independent of SourceEave / WallPlateBottom.
    /// </summary>
    private double _explicitReferenceLocalZMm;
    private readonly RoofRelativeElevationDatumError? _storedDatumLoadError;
    private AutomaticPurlinRowViewModel? _selectedRow;
    private AutomaticPurlinEditorTabViewModel? _selectedEditorTab;
    private bool _isSyncingEditorTabSelection;
    private bool _isApplyInProgress;
    private bool _ridgeInteractionActive;
    private bool _intermediateInteractionActive;
    private string _ridgeWidthText = string.Empty;
    private string _ridgeHeightText = string.Empty;
    private readonly double? _storedRidgeWidthMm;
    private readonly double? _storedRidgeHeightMm;
    private readonly double _loadedRidgeWidthMm;
    private readonly double _loadedRidgeHeightMm;
    private bool _ridgeWidthEdited;
    private bool _ridgeHeightEdited;
    private RoofAutomaticPurlinSeatingDepth _ridgeSeating;
    private string _ridgeSeatingDepthValueText = string.Empty;
    private bool _ridgeSeatingDepthValueEdited;
    private AutomaticPurlinSectionPresentation _sectionPresentation =
        AutomaticPurlinSectionPresentation.Empty;
    private double _rafterWidthMm;
    private double _rafterHeightMm;
    private readonly double _initialRafterWidthMm;
    private readonly double _initialRafterHeightMm;
    private bool _relativeReferenceHasError;
    private string _relativeReferenceErrorText = string.Empty;
    private bool _referenceLocalZHasError;
    private string _referenceLocalZErrorText = string.Empty;
    private bool _ridgeWidthHasError;
    private string _ridgeWidthErrorText = string.Empty;
    private bool _ridgeHeightHasError;
    private string _ridgeHeightErrorText = string.Empty;
    private bool _ridgeSeatingHasError;
    private string _ridgeSeatingErrorText = string.Empty;
    private AutomaticPurlinRafterDimensionSource _rafterDimensionSource;
    private RoofAutomaticPurlinRafterSourcePolicy _draftRafterSourcePolicy;
    private double? _recoverableManualRafterWidthMm;
    private double? _recoverableManualRafterHeightMm;
    private RoofAutomaticPurlinAcknowledgedActualKind _draftAcknowledgedActualKind;
    private double? _draftAcknowledgedActualWidthMm;
    private double? _draftAcknowledgedActualHeightMm;
    private bool _hostActualAvailable;
    private double _hostActualWidthMm;
    private double _hostActualHeightMm;
    private AutomaticPurlinManualRafterDialogSession? _manualRafterDialogSession;
    private bool _rafterSourceConflictUnresolved;
    private bool _missingActualTransitionUnresolved;
    private bool _persistedManualRafterInvalid;
    private double _conflictActualRafterWidthMm;
    private double _conflictActualRafterHeightMm;

    internal AutomaticPurlinDialogViewModel(
        HipRoofGeometry geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayout? layout,
        bool layoutExists,
        RoofRelativeElevationDatum? datum,
        bool datumExists,
        TimberElementData purlinDefaults,
        TimberElementData wallPlateDefaults,
        TimberElementData rafterDefaults,
        CultureInfo? culture = null,
        AutomaticPurlinDialogMode mode = AutomaticPurlinDialogMode.ReadOnlyPreview,
        int existingAutomaticPurlinCount = 0,
        RoofRelativeElevationDatumError? storedDatumLoadError = null,
        AutomaticPurlinRafterDimensionSource rafterDimensionSource =
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _boundaryProvenance = boundaryProvenance;
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _mode = mode;
        _existingAutomaticPurlinCount = Math.Max(0, existingAutomaticPurlinCount);
        _storedDatumLoadError = storedDatumLoadError;
        _rafterDimensionSource = rafterDimensionSource;
        ArgumentNullException.ThrowIfNull(purlinDefaults);
        ArgumentNullException.ThrowIfNull(wallPlateDefaults);
        ArgumentNullException.ThrowIfNull(rafterDefaults);
        RidgeTechnicalSummary = new AutomaticPurlinTechnicalSummaryViewModel(
            RoofAutomaticPurlinGeneratorRole.Ridge,
            _culture,
            Text);

        LayoutExists = layoutExists;
        DatumExists = datumExists;
        PurlinWidthMm = purlinDefaults.WidthMm;
        PurlinHeightMm = purlinDefaults.HeightMm;
        WallPlateWidthMm = wallPlateDefaults.WidthMm;
        WallPlateHeightMm = wallPlateDefaults.HeightMm;
        _initialRafterWidthMm = rafterDefaults.WidthMm;
        _initialRafterHeightMm = rafterDefaults.HeightMm;
        _rafterWidthMm = _initialRafterWidthMm;
        _rafterHeightMm = _initialRafterHeightMm;

        // HOST passes Empty when no owner section exists (LayoutExists=false). That must
        // still seed CreateNewDraftDefaults; only a persisted layout (LayoutExists=true)
        // loads verbatim — including an intentionally empty persisted layout.
        var effectiveLayout = !layoutExists || layout is null
            ? RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults(
                DefaultWallPlateEaveDistanceMm,
                DefaultSeatingPercent)
            : layout;
        _wallPlateEnabled = effectiveLayout.WallPlateEnabled;
        _ridgeEnabled = effectiveLayout.RidgeEnabled;
        _storedRidgeWidthMm = effectiveLayout.RidgeWidthMm;
        _storedRidgeHeightMm = effectiveLayout.RidgeHeightMm;
        _loadedRidgeWidthMm = _storedRidgeWidthMm ?? PurlinWidthMm;
        _loadedRidgeHeightMm = _storedRidgeHeightMm ?? PurlinHeightMm;
        _ridgeWidthText = _loadedRidgeWidthMm.ToString("0.###", _culture);
        _ridgeHeightText = _loadedRidgeHeightMm.ToString("0.###", _culture);
        _ridgeWidthEdited = false;
        _ridgeHeightEdited = false;
        _ridgeSeating = effectiveLayout.RidgeSeatingDepth ??
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                DefaultSeatingPercent);
        _ridgeSeatingDepthValueText = _ridgeSeating.Value.ToString("0.###", _culture);
        _ridgeSeatingDepthValueEdited = false;
        // Fail-closed load: inconsistent SourceEave LocalZ arrives as datum=null + error.
        // Seed a safe SourceEave(0, Rel) draft so the user can explicitly resolve and Apply.
        var effectiveDatum = datum ?? new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        _referenceKind = effectiveDatum.ReferenceKind;
        _loadedRelativeReferenceMm = effectiveDatum.ReferenceRelativeElevationMm;
        _explicitReferenceLocalZMm =
            effectiveDatum.ReferenceKind == RoofRelativeElevationReferenceKind.ExplicitLocalPlane
                ? effectiveDatum.ReferenceLocalZMm
                : 0d;
        _relativeReferenceText = RoofRelativeElevationDatumRules.FormatMetres(
            effectiveDatum.ReferenceRelativeElevationMm,
            _culture);
        _referenceLocalZText = FormatLocalZText(ResolveDisplayLocalZMm(effectiveDatum));

        PlacementModes = Array.AsReadOnly(new[]
        {
            new AutomaticPurlinPlacementModeOption(
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                Text("AutomaticPurlin_ModeBottomHeight")),
            new AutomaticPurlinPlacementModeOption(
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                Text("AutomaticPurlin_ModeEaveDistance")),
            new AutomaticPurlinPlacementModeOption(
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
                Text("AutomaticPurlin_ModeRidgeDistance")),
        });
        SeatingModes = Array.AsReadOnly(new[]
        {
            new AutomaticPurlinSeatingModeOption(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                Text("AutomaticPurlin_SeatingPercent")),
            new AutomaticPurlinSeatingModeOption(
                RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
                Text("AutomaticPurlin_SeatingAbsolute")),
        });
        ReferenceKinds = Array.AsReadOnly(new[]
        {
            new AutomaticPurlinReferenceKindOption(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                Text("AutomaticPurlin_ReferenceExplicitPlane")),
            new AutomaticPurlinReferenceKindOption(
                RoofRelativeElevationReferenceKind.SourceEavePlane,
                Text("AutomaticPurlin_ReferenceSourceEave")),
            new AutomaticPurlinReferenceKindOption(
                RoofRelativeElevationReferenceKind.WallPlateBottom,
                Text("AutomaticPurlin_ReferenceWallPlateBottom")),
        });

        _ridgeReferences = ResolveRidgeReferences();
        RidgeReferences = _ridgeReferences.AsReadOnly();
        Rows = new ObservableCollection<AutomaticPurlinRowViewModel>();
        EditorTabs = new ObservableCollection<AutomaticPurlinEditorTabViewModel>();
        foreach (var item in effectiveLayout.IntermediateItems)
        {
            AddRowCore(item);
        }

        WallPlateRow = CreateRow(
            RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(effectiveLayout),
            allowZeroPlacementValue: true,
            defaultWidthMm: WallPlateWidthMm,
            defaultHeightMm: WallPlateHeightMm,
            role: RoofAutomaticPurlinGeneratorRole.WallPlate);
        WallPlateRow.Changed += Row_Changed;
        WallPlateRow.PlacementModeChanging += Row_PlacementModeChanging;
        WallPlateRow.PlacementValueUserEdited += Row_PlacementValueUserEdited;

        SeedWallPlateBottomEdgeBootstrapAbsoluteMm(effectiveLayout);

        _selectedRow = Rows.FirstOrDefault();
        UpdateSelectedRows();

        InitializeRafterSourceFromLayout(effectiveLayout, rafterDimensionSource);
        Recalculate(raisePreviewChanged: false);
        RebuildEditorTabs(preferWallPlate: true);
    }

    private void SeedWallPlateBottomEdgeBootstrapAbsoluteMm(RoofAutomaticPurlinLayout layout)
    {
        var wallPlate = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout);
        if (wallPlate.PlacementMode !=
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference ||
            Math.Abs(wallPlate.PlacementValueMm) > 1e-9 ||
            !double.IsFinite(layout.WallPlateLowerEdgeHeightMm) ||
            Math.Abs(layout.WallPlateLowerEdgeHeightMm) <= 1e-9)
        {
            _wallPlateBottomEdgeBootstrapAbsoluteMm = null;
            return;
        }

        _wallPlateBottomEdgeBootstrapAbsoluteMm = layout.WallPlateLowerEdgeHeightMm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? PreviewChanged;
    /// <summary>Raised when the editor tab selection changes (presentation/tooltip pin only).</summary>
    internal event EventHandler? EditorTabSelectionChanged;

    public string WindowTitle => Text("AutomaticPurlin_Title");
    public string WindowDescription => Text(
        IsProductionEdit
            ? "AutomaticPurlin_DescriptionProduction"
            : "AutomaticPurlin_Description");
    public string DatumPersistenceStatus =>
        _storedDatumLoadError == RoofRelativeElevationDatumError.InconsistentSourceEaveLocalZ
            ? Text("AutomaticPurlin_DatumInconsistentSourceEave")
            : Text(
                DatumExists
                    ? "AutomaticPurlin_DatumStored"
                    : IsProductionEdit
                        ? "AutomaticPurlin_DatumMissingProduction"
                        : "AutomaticPurlin_DatumMissing");
    public string LayoutPersistenceStatus => Text(
        LayoutExists
            ? "AutomaticPurlin_LayoutStored"
            : IsProductionEdit
                ? "AutomaticPurlin_LayoutMissingProduction"
                : "AutomaticPurlin_LayoutMissing");
    public string PurlinSectionText => string.Format(
        _culture,
        "{0:0.###} × {1:0.###} {2}",
        PurlinWidthMm,
        PurlinHeightMm,
        Text("AutomaticPurlin_UnitMillimetres"));
    public string ValidationMessage => _validationMessage;
    public bool RelativeReferenceHasError => _relativeReferenceHasError;
    public string RelativeReferenceErrorText => _relativeReferenceErrorText;
    public bool ReferenceLocalZHasError => _referenceLocalZHasError;
    public string ReferenceLocalZErrorText => _referenceLocalZErrorText;
    public bool RidgeWidthHasError => _ridgeWidthHasError;
    public string RidgeWidthErrorText => _ridgeWidthErrorText;
    public bool RidgeHeightHasError => _ridgeHeightHasError;
    public string RidgeHeightErrorText => _ridgeHeightErrorText;
    public bool RidgeSeatingHasError => _ridgeSeatingHasError;
    public string RidgeSeatingErrorText => _ridgeSeatingErrorText;
    public bool RidgeHasFieldError =>
        RidgeWidthHasError || RidgeHeightHasError || RidgeSeatingHasError;
    public bool ReferencePlaneHasFieldError =>
        RelativeReferenceHasError || ReferenceLocalZHasError;
    internal string PreviewDiagnosticReason => _previewDiagnosticReason;
    public bool CanPreview => _previewPlan is not null;

    /// <summary>
    /// True when the schematic still shows the last valid geometry while the current
    /// draft is invalid (reference/placement edit in progress).
    /// </summary>
    public bool IsSchematicStale => _isSchematicStale;

    public string SchematicStaleMessage =>
        _isSchematicStale ? Text("AutomaticPurlin_SchematicStale") : string.Empty;
    public bool IsProductionEdit => _mode == AutomaticPurlinDialogMode.ProductionEdit;
    /// <summary>
    /// True when width/height come from recovered recipe, CAD pick, or explicit manual entry.
    /// Profile seed alone is never authoritative.
    /// </summary>
    public bool HasAuthoritativeRafterProfile =>
        _rafterDimensionSource is
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe or
            AutomaticPurlinRafterDimensionSource.SelectedRafter or
            AutomaticPurlinRafterDimensionSource.ExplicitManual;

    /// <summary>Roof has no measured/recovered rafters and user has not confirmed manual W×H.</summary>
    public bool RequiresManualRafterInput =>
        _rafterDimensionSource == AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed &&
        !_rafterSourceConflictUnresolved &&
        !_missingActualTransitionUnresolved &&
        !_persistedManualRafterInvalid;

    public bool HasExplicitManualRafterProfile =>
        _rafterDimensionSource == AutomaticPurlinRafterDimensionSource.ExplicitManual &&
        !_missingActualTransitionUnresolved;

    /// <summary>Prefer CAD pick when actual roof rafters (or a prior pick) supply dimensions.</summary>
    public bool PrefersCadRafterPick =>
        _rafterDimensionSource is
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe or
            AutomaticPurlinRafterDimensionSource.SelectedRafter;

    public AutomaticPurlinRafterDimensionSource RafterDimensionSource => _rafterDimensionSource;

    public bool ShowNoRafterFooterWarning => RequiresManualRafterInput;

    public bool ShowManualRafterFooterInfo => HasExplicitManualRafterProfile;

    public bool ShowRafterSourceConflictWarning => _rafterSourceConflictUnresolved;

    /// <summary>
    /// PreferActual persisted but actual rafters are gone; recoverable manual exists.
    /// Requires explicit confirmation before Apply / durable PreferManual.
    /// </summary>
    public bool ShowMissingActualTransitionWarning => _missingActualTransitionUnresolved;

    public bool ShowPersistedManualRafterInvalidWarning => _persistedManualRafterInvalid;

    /// <summary>Provisional recoverable manual used only for temporary preview while unresolved.</summary>
    public bool ShowProvisionalManualRafterFooterInfo =>
        _missingActualTransitionUnresolved &&
        _recoverableManualRafterWidthMm is not null &&
        _recoverableManualRafterHeightMm is not null;

    public string NoRafterWarningTitle => Text("AutomaticPurlin_NoRafterWarningTitle");

    public string NoRafterWarningDetail => Text("AutomaticPurlin_NoRafterWarningDetail");

    public string EnterRafterDimensionsActionText => Text("AutomaticPurlin_EnterRafterDimensions");

    public string ResolveRafterSourceConflictActionText =>
        Text("AutomaticPurlin_ResolveRafterSourceConflict");

    public string MissingActualTransitionTitle =>
        Text("AutomaticPurlin_MissingActualTransitionTitle");

    public string MissingActualTransitionDetail =>
        Text("AutomaticPurlin_MissingActualTransitionDetail");

    public string ConfirmStoredManualRafterActionText =>
        Text("AutomaticPurlin_ConfirmStoredManualRafter");

    public string ManualRafterFooterInfoText =>
        string.Format(
            _culture,
            Text("AutomaticPurlin_ManualRafterProfileInfoFormat"),
            RafterWidthMm.ToString("0.###", _culture),
            RafterHeightMm.ToString("0.###", _culture));

    public string ProvisionalManualRafterFooterInfoText =>
        string.Format(
            _culture,
            Text("AutomaticPurlin_ProvisionalManualRafterProfileInfoFormat"),
            (_recoverableManualRafterWidthMm ?? RafterWidthMm).ToString("0.###", _culture),
            (_recoverableManualRafterHeightMm ?? RafterHeightMm).ToString("0.###", _culture));

    public string RafterSourceConflictFooterText => Text("AutomaticPurlin_RafterSourceConflictFooter");

    public string PersistedManualRafterInvalidFooterText =>
        Text("AutomaticPurlin_PersistedManualRafterInvalid");

    public double? RecoverableManualRafterWidthMm => _recoverableManualRafterWidthMm;

    public double? RecoverableManualRafterHeightMm => _recoverableManualRafterHeightMm;

    public double ConflictActualRafterWidthMm => _conflictActualRafterWidthMm;

    public double ConflictActualRafterHeightMm => _conflictActualRafterHeightMm;

    /// <summary>
    /// Current-roof actual availability retained independently of the draft/manual candidate.
    /// </summary>
    internal bool HostActualAvailable => _hostActualAvailable;

    internal double HostActualWidthMm => _hostActualWidthMm;

    internal double HostActualHeightMm => _hostActualHeightMm;

    public RoofAutomaticPurlinRafterSourcePolicy DraftRafterSourcePolicy => _draftRafterSourcePolicy;

    public RoofAutomaticPurlinAcknowledgedActualKind DraftAcknowledgedActualKind =>
        _draftAcknowledgedActualKind;

    public double? DraftAcknowledgedActualWidthMm => _draftAcknowledgedActualWidthMm;

    public double? DraftAcknowledgedActualHeightMm => _draftAcknowledgedActualHeightMm;

    public bool CanApply =>
        IsProductionEdit &&
        !_isApplyInProgress &&
        _currentDraftIsValid &&
        _previewPlan is not null &&
        HasAuthoritativeRafterProfile &&
        !_rafterSourceConflictUnresolved &&
        !_missingActualTransitionUnresolved &&
        !_persistedManualRafterInvalid &&
        (_previewPlan.Items.Count > 0 || _existingAutomaticPurlinCount > 0);
    public bool HasMultipleRidges => RidgeReferences.Count > 1;
    public double PurlinWidthMm { get; }
    public double PurlinHeightMm { get; }
    public double WallPlateWidthMm { get; }
    public double WallPlateHeightMm { get; }
    public double RafterWidthMm => _rafterWidthMm;
    public double RafterHeightMm => _rafterHeightMm;
    public double InitialRafterWidthMm => _initialRafterWidthMm;
    public double InitialRafterHeightMm => _initialRafterHeightMm;
    public string RafterDimensionText => string.Format(
        _culture,
        "{0:0.###} × {1:0.###}",
        RafterWidthMm,
        RafterHeightMm);
    public bool LayoutExists { get; }
    public bool DatumExists { get; }
    public ObservableCollection<AutomaticPurlinRowViewModel> Rows { get; }
    /// <summary>
    /// Presentation-only ordered editor tabs: WallPlate, Ridge, then each Intermediate.
    /// </summary>
    public ObservableCollection<AutomaticPurlinEditorTabViewModel> EditorTabs { get; }
    public IReadOnlyList<AutomaticPurlinPlacementModeOption> PlacementModes { get; }
    public IReadOnlyList<AutomaticPurlinSeatingModeOption> SeatingModes { get; }
    public IReadOnlyList<AutomaticPurlinReferenceKindOption> ReferenceKinds { get; }
    public IReadOnlyList<AutomaticPurlinRidgeOption> RidgeReferences { get; }
    public AutomaticPurlinRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value))
            {
                return;
            }

            _selectedRow = value;
            OnPropertyChanged();
            UpdateSelectedRows();
            NotifySchematicState();
            SyncSelectedEditorTabFromSelectedRow();
        }
    }

    public AutomaticPurlinEditorTabViewModel? SelectedEditorTab
    {
        get => _selectedEditorTab;
        set
        {
            if (ReferenceEquals(_selectedEditorTab, value))
            {
                return;
            }

            _selectedEditorTab = value;
            OnPropertyChanged();
            if (!_isSyncingEditorTabSelection)
            {
                _isSyncingEditorTabSelection = true;
                try
                {
                    if (value?.Kind == AutomaticPurlinEditorTabKind.Intermediate)
                    {
                        SelectedRow = value.IntermediateRow;
                        SetIntermediateInteractionActive(true);
                    }
                    else
                    {
                        SetIntermediateInteractionActive(false);
                    }
                }
                finally
                {
                    _isSyncingEditorTabSelection = false;
                }
            }

            EditorTabSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool SchematicRidgeActive => RidgeEnabled;
    public bool SchematicRidgeEmphasized => RidgeEnabled && _ridgeInteractionActive;
    public bool SchematicIntermediateActive => SelectedRow is { Enabled: true };
    public bool SchematicAnyIntermediateEnabled => Rows.Any(static row => row.Enabled);
    public bool SchematicIntermediateEmphasized =>
        SelectedRow is { Enabled: true } &&
        (_intermediateInteractionActive || SelectedRow.IsSchematicSelected);
    public RoofAutomaticPurlinPlacementMode? SchematicPlacementMode =>
        SelectedRow?.PlacementMode;
    public bool ShowEaveDistanceCue =>
        SelectedRow?.PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
    public bool ShowRidgeDistanceCue =>
        SelectedRow?.PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
    public bool ShowVerticalHeightCue =>
        SelectedRow?.PlacementMode ==
        RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
    public bool ShowSeatingCue => true;
    public bool SchematicRidgeReferenceUnresolved =>
        ShowRidgeDistanceCue &&
        SelectedRow is not null &&
        SelectedRow.RidgeReferences.Count != 1 &&
        SelectedRow.SelectedRidgeReference is null;
    public string SchematicCaption => SchematicRidgeReferenceUnresolved
        ? Text("AutomaticPurlin_SchematicRidgeUnresolved")
        : SelectedRow?.ValueLabel ?? Text("AutomaticPurlin_SchematicNoSelection");
    public string SchematicImagePackUri { get; } =
        "pack://application:,,,/AcKrovy.AutoCAD;component/UI/Assets/Schematics/automatic-purlin-roof-section.png";

    internal void SetRidgeInteractionActive(bool active)
    {
        if (_ridgeInteractionActive == active)
        {
            return;
        }

        _ridgeInteractionActive = active;
        if (active)
        {
            _intermediateInteractionActive = false;
        }

        NotifySchematicState();
    }

    internal void SetIntermediateInteractionActive(bool active)
    {
        if (_intermediateInteractionActive == active)
        {
            return;
        }

        _intermediateInteractionActive = active;
        if (active)
        {
            _ridgeInteractionActive = false;
        }

        NotifySchematicState();
    }

    public bool RidgeEnabled
    {
        get => _ridgeEnabled;
        set
        {
            if (_ridgeEnabled == value)
            {
                return;
            }

            _ridgeEnabled = value;
            OnPropertyChanged();
            NotifySchematicState();
            Recalculate();
        }
    }

    public AutomaticPurlinRowViewModel WallPlateRow { get; }

    /// <summary>
    /// Strešná rovina / Spodná / Os / Horná for the ridge card — presentation only.
    /// </summary>
    public AutomaticPurlinTechnicalSummaryViewModel RidgeTechnicalSummary { get; }

    public AutomaticPurlinSectionPresentation SectionPresentation => _sectionPresentation;

    /// <summary>
    /// Read-only pitch of the roof geometry opened into this dialog (culture-aware).
    /// </summary>
    public string CurrentRoofPitchText =>
        string.Format(
            _culture,
            Text("AutomaticPurlin_CurrentRoofPitchFormat"),
            _geometry.PrimarySlopeDegrees.ToString("0.0", _culture));

    /// <summary>Read-only roof-kind label for the reference-panel info strip.</summary>
    public string RoofInfoTypeText => _geometry.Kind switch
    {
        RoofKind.Hip => Text("CommandUi_RoofHip_Label"),
        _ => _geometry.Kind.ToString(),
    };

    /// <summary>Read-only pitch value for the reference-panel info strip (degrees).</summary>
    public string RoofInfoPitchText =>
        _geometry.PrimarySlopeDegrees.ToString("0.0", _culture) + "°";

    /// <summary>
    /// Read-only roof rise from authoritative topology (<see cref="HipRoofGeometry.RiseMm"/>),
    /// formatted as signed metres — no new height formula.
    /// </summary>
    public string RoofInfoHeightText =>
        RoofRelativeElevationDatumRules.FormatMetresWithUnit(_geometry.RiseMm, _culture);

    /// <summary>Presentation-only: WallPlate tab header error from existing field validation.</summary>
    public bool WallPlateTabHasError => WallPlateRow.HasAnyFieldError;

    /// <summary>Presentation-only: Intermediate tab header error from existing row field validation.</summary>
    public bool IntermediateTabHasError => Rows.Any(static row => row.HasAnyFieldError);

    /// <summary>Presentation-only: Ridge tab header error from existing ridge field validation.</summary>
    public bool RidgeTabHasError => RidgeHasFieldError;

    public string WallPlateTabAutomationName => FormatTabAutomationName(
        "AutomaticPurlin_TabWallPlate",
        WallPlateTabHasError);

    public string IntermediateTabAutomationName => FormatTabAutomationName(
        "AutomaticPurlin_TabIntermediate",
        IntermediateTabHasError);

    public string RidgeTabAutomationName => FormatTabAutomationName(
        "AutomaticPurlin_TabRidge",
        RidgeTabHasError);

    private string FormatTabAutomationName(string titleKey, bool hasError) =>
        FormatTabAutomationNameFromTitle(Text(titleKey), hasError);

    public string RidgeWidthText
    {
        get => _ridgeWidthText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_ridgeWidthText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _ridgeWidthText = normalized;
            _ridgeWidthEdited = true;
            OnPropertyChanged();
            Recalculate();
        }
    }

    public string RidgeHeightText
    {
        get => _ridgeHeightText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_ridgeHeightText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _ridgeHeightText = normalized;
            _ridgeHeightEdited = true;
            OnPropertyChanged();
            Recalculate();
        }
    }

    public RoofAutomaticPurlinSeatingDepthMode SelectedRidgeSeatingMode
    {
        get => _ridgeSeating.Mode is
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight or
            RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm
                ? _ridgeSeating.Mode
                : RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        set
        {
            if (value is not RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight and
                not RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm)
            {
                return;
            }

            if (_ridgeSeating.Mode == value)
            {
                return;
            }

            _ridgeSeating = new RoofAutomaticPurlinSeatingDepth(value, _ridgeSeating.Value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RidgeSeatingUnit));
            OnPropertyChanged(nameof(RidgeSeatingDepthDerived));
            Recalculate();
        }
    }

    public string RidgeSeatingDepthValueText
    {
        get => _ridgeSeatingDepthValueText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_ridgeSeatingDepthValueText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _ridgeSeatingDepthValueText = normalized;
            _ridgeSeatingDepthValueEdited = true;
            if (TryParseMillimetres(normalized, out var parsedValue))
            {
                _ridgeSeating = new RoofAutomaticPurlinSeatingDepth(
                    SelectedRidgeSeatingMode,
                    parsedValue);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(RidgeSeatingDepthDerived));
            Recalculate();
        }
    }

    public string RidgeSeatingUnit =>
        SelectedRidgeSeatingMode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight
            ? "%"
            : Text("AutomaticPurlin_UnitMillimetres");

    public string RidgeSeatingDepthDerived
    {
        get
        {
            if (!TryResolveRidgeSeatingDepth(out var depthMm) || depthMm is null)
            {
                return "—";
            }

            return string.Format(
                _culture,
                "{0:0.###} {1}",
                depthMm.Value,
                Text("AutomaticPurlin_UnitMillimetres"));
        }
    }

    public bool WallPlateEnabled
    {
        get => _wallPlateEnabled;
        set
        {
            if (_wallPlateEnabled == value)
            {
                return;
            }

            _wallPlateEnabled = value;
            OnPropertyChanged();
            Recalculate();
        }
    }

    public RoofRelativeElevationReferenceKind ReferenceKind
    {
        get => _referenceKind;
        set
        {
            if (_referenceKind == value)
            {
                return;
            }

            // Capture Explicit LocalZ from the visible field before leaving Explicit.
            if (_referenceKind == RoofRelativeElevationReferenceKind.ExplicitLocalPlane &&
                TryParseMillimetres(ReferenceLocalZText, out var explicitLocalZMm))
            {
                _explicitReferenceLocalZMm = explicitLocalZMm;
            }

            TryCaptureBottomEdgePhysicalState(out var physicalCapture);
            var previousKind = _referenceKind;
            _referenceKind = value;
            SyncReferenceLocalZTextForCurrentKind(raisePropertyChanged: true);
            RebaseBottomEdgePlacementsForReferenceChange(previousKind, value, physicalCapture);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalReferencePositionVisible));
            Recalculate();
        }
    }

    /// <summary>
    /// "Lokálna poloha" is editable only for ExplicitLocalPlane.
    /// Schematic SourceEave/Explicit guides may map to the plumb-cut tip for drawing only;
    /// technical LocalZ for SourceEave remains roof-local 0 (see section presentation).
    /// </summary>
    public bool IsLocalReferencePositionVisible =>
        ReferenceKind == RoofRelativeElevationReferenceKind.ExplicitLocalPlane;

    public string RelativeReferenceText
    {
        get => _relativeReferenceText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_relativeReferenceText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _relativeReferenceText = normalized;
            _relativeReferenceEdited = true;
            OnPropertyChanged();
            Recalculate();
        }
    }

    public string ReferenceLocalZText
    {
        get => _referenceLocalZText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_referenceLocalZText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            var rebaseExplicit = false;
            var newLocalZMm = 0d;
            if (ReferenceKind == RoofRelativeElevationReferenceKind.ExplicitLocalPlane &&
                TryParseMillimetres(_referenceLocalZText, out _) &&
                TryParseMillimetres(normalized, out newLocalZMm))
            {
                rebaseExplicit = true;
            }

            BottomEdgePhysicalCapture? physicalCapture = null;
            if (rebaseExplicit)
            {
                TryCaptureBottomEdgePhysicalState(out physicalCapture);
            }

            _referenceLocalZText = normalized;
            if (rebaseExplicit)
            {
                _explicitReferenceLocalZMm = newLocalZMm;
                RebaseBottomEdgePlacementsToReferenceLocalZ(newLocalZMm, physicalCapture);
            }

            OnPropertyChanged();
            Recalculate();
        }
    }

    internal AutomaticPurlinRowViewModel AddRow()
    {
        var row = AddRowCore(new RoofAutomaticPurlinLayoutItem(
            RoofAutomaticPurlinLayoutItemIdentity.Create(),
            true,
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            DefaultAddedIntermediateEaveDistanceMm,
            SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                DefaultSeatingPercent)));
        SelectedRow = row;
        RebuildEditorTabs(selectIntermediateRow: row);
        Recalculate();
        return row;
    }

    internal void RemoveRow(AutomaticPurlinRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var removedIndex = Rows.IndexOf(row);
        if (removedIndex < 0 || !Rows.Remove(row))
        {
            return;
        }

        row.Changed -= Row_Changed;
        row.PlacementModeChanging -= Row_PlacementModeChanging;
        row.PlacementValueUserEdited -= Row_PlacementValueUserEdited;
        RefreshRowDisplayNames();
        if (ReferenceEquals(SelectedRow, row))
        {
            SelectedRow = Rows.Count == 0
                ? null
                : Rows[Math.Min(removedIndex, Rows.Count - 1)];
        }

        RebuildEditorTabs(selectIntermediateRow: SelectedRow);
        Recalculate();
    }

    internal bool TryApplySelectedRafterDimensions(double widthMm, double heightMm) =>
        TryApplyRafterDimensions(
            widthMm,
            heightMm,
            AutomaticPurlinRafterDimensionSource.SelectedRafter);

    /// <summary>
    /// Starts a CAD pick from the nested manual-rafter dialog. Stores preserve seeds
    /// for Escape/invalid selection. Does not mutate draft dimensions or XData.
    /// </summary>
    internal void BeginManualDialogCadPick(double preserveWidthMm, double preserveHeightMm) =>
        _manualRafterDialogSession = new AutomaticPurlinManualRafterDialogSession
        {
            SeedWidthMm = preserveWidthMm,
            SeedHeightMm = preserveHeightMm,
            ReopenAfterPick = false,
            AdoptAsSelectedIfUnedited = false,
        };

    /// <summary>
    /// Completes a CAD pick started from the manual dialog. Populates reopen seeds only;
    /// does not write the main ViewModel draft until Confirm.
    /// </summary>
    internal void CompleteManualDialogCadPick(
        bool success,
        double? pickedWidthMm,
        double? pickedHeightMm,
        bool isCurrentRoofGeneratedRafter)
    {
        var preserveWidth = _manualRafterDialogSession?.SeedWidthMm ?? _rafterWidthMm;
        var preserveHeight = _manualRafterDialogSession?.SeedHeightMm ?? _rafterHeightMm;
        if (!success ||
            pickedWidthMm is not { } widthMm ||
            pickedHeightMm is not { } heightMm ||
            !double.IsFinite(widthMm) ||
            widthMm <= 0d ||
            !double.IsFinite(heightMm) ||
            heightMm <= 0d)
        {
            _manualRafterDialogSession = new AutomaticPurlinManualRafterDialogSession
            {
                SeedWidthMm = preserveWidth,
                SeedHeightMm = preserveHeight,
                ReopenAfterPick = true,
                AdoptAsSelectedIfUnedited = false,
            };
            return;
        }

        _manualRafterDialogSession = new AutomaticPurlinManualRafterDialogSession
        {
            SeedWidthMm = widthMm,
            SeedHeightMm = heightMm,
            ReopenAfterPick = true,
            AdoptAsSelectedIfUnedited = isCurrentRoofGeneratedRafter,
            PickedWidthMm = widthMm,
            PickedHeightMm = heightMm,
        };
    }

    /// <summary>
    /// Consumes a one-shot reopen request after CAD pick. Leaves adopt/picked
    /// snapshot available until Confirm or Cancel clears the session.
    /// </summary>
    internal bool TryConsumeManualDialogReopen(
        out AutomaticPurlinManualRafterDialogSession session)
    {
        if (_manualRafterDialogSession is not { ReopenAfterPick: true } pending)
        {
            session = null!;
            return false;
        }

        session = pending;
        _manualRafterDialogSession = pending with { ReopenAfterPick = false };
        return true;
    }

    /// <summary>
    /// Current-roof generated pick may adopt SelectedRafter only when Confirm W×H
    /// still match the unedited pick. External / ownership-unknown picks stay manual.
    /// </summary>
    internal bool ShouldAdoptPickedRafterAsSelected(double widthMm, double heightMm) =>
        _manualRafterDialogSession is
        {
            AdoptAsSelectedIfUnedited: true,
            PickedWidthMm: { } pickedWidth,
            PickedHeightMm: { } pickedHeight,
        } &&
        RoofAutomaticPurlinRafterSourceRules.AreCrossSectionsEqual(
            widthMm,
            heightMm,
            pickedWidth,
            pickedHeight);

    internal void ClearManualDialogSession() => _manualRafterDialogSession = null;

    /// <summary>
    /// Updates the CURRENT roof's actual W×H snapshot without changing draft policy,
    /// acknowledgement, or treating the values as a SelectedRafter adoption.
    /// Used after fresh discovery / recipe change within the same dialog session.
    /// </summary>
    internal void RefreshHostActualRafterDimensions(double widthMm, double heightMm)
    {
        if (!double.IsFinite(widthMm) || widthMm <= 0d ||
            !double.IsFinite(heightMm) || heightMm <= 0d)
        {
            return;
        }

        _hostActualAvailable = true;
        _hostActualWidthMm = widthMm;
        _hostActualHeightMm = heightMm;
    }

    /// <summary>
    /// Applies explicit manual W×H for the current dialog draft and marks PreferManual.
    /// Does not create CAD rafters. Persists only on successful Apply.
    /// </summary>
    internal bool TryApplyManualRafterDimensions(double widthMm, double heightMm) =>
        TryApplyRafterDimensions(
            widthMm,
            heightMm,
            AutomaticPurlinRafterDimensionSource.ExplicitManual);

    /// <summary>
    /// Keeps the persisted manual profile after an explicit conflict choice.
    /// Acknowledges the conflicting actual W×H so PreferManual stays valid until actual changes.
    /// </summary>
    internal bool TryResolveRafterSourceConflictKeepManual()
    {
        if (!_rafterSourceConflictUnresolved ||
            _recoverableManualRafterWidthMm is not { } widthMm ||
            _recoverableManualRafterHeightMm is not { } heightMm)
        {
            return false;
        }

        SetDraftAcknowledgement(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            _conflictActualRafterWidthMm,
            _conflictActualRafterHeightMm);
        return TryApplyRafterDimensions(
            widthMm,
            heightMm,
            AutomaticPurlinRafterDimensionSource.ExplicitManual,
            updateAcknowledgement: false);
    }

    /// <summary>
    /// PreferActual lost its actual rafters: confirm the recoverable stored manual profile.
    /// Draft only — PreferManual + NonePresent persist on successful Apply.
    /// </summary>
    internal bool TryConfirmStoredManualAfterMissingActual()
    {
        if (!_missingActualTransitionUnresolved ||
            _recoverableManualRafterWidthMm is not { } widthMm ||
            _recoverableManualRafterHeightMm is not { } heightMm)
        {
            return false;
        }

        SetDraftAcknowledgement(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            null,
            null);
        return TryApplyRafterDimensions(
            widthMm,
            heightMm,
            AutomaticPurlinRafterDimensionSource.ExplicitManual,
            updateAcknowledgement: false);
    }

    /// <summary>
    /// Uses actual roof rafter dimensions after an explicit conflict choice.
    /// Preserves the unused manual profile as a recoverable alternative.
    /// </summary>
    internal bool TryResolveRafterSourceConflictUseActual()
    {
        if (!_rafterSourceConflictUnresolved)
        {
            return false;
        }

        SetDraftAcknowledgement(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            _conflictActualRafterWidthMm,
            _conflictActualRafterHeightMm);
        return TryApplyRafterDimensions(
            _conflictActualRafterWidthMm,
            _conflictActualRafterHeightMm,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            updateAcknowledgement: false);
    }

    private bool TryApplyRafterDimensions(
        double widthMm,
        double heightMm,
        AutomaticPurlinRafterDimensionSource source,
        bool updateAcknowledgement = true)
    {
        if (!double.IsFinite(widthMm) || widthMm <= 0d ||
            !double.IsFinite(heightMm) || heightMm <= 0d)
        {
            return false;
        }

        _rafterWidthMm = widthMm;
        _rafterHeightMm = heightMm;
        _rafterDimensionSource = source;
        _rafterSourceConflictUnresolved = false;
        _missingActualTransitionUnresolved = false;
        _persistedManualRafterInvalid = false;
        if (source == AutomaticPurlinRafterDimensionSource.ExplicitManual)
        {
            var previousManualWidthMm = _recoverableManualRafterWidthMm;
            var previousManualHeightMm = _recoverableManualRafterHeightMm;
            _recoverableManualRafterWidthMm = widthMm;
            _recoverableManualRafterHeightMm = heightMm;
            _draftRafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual;
            if (updateAcknowledgement)
            {
                if (!_hostActualAvailable)
                {
                    SetDraftAcknowledgement(
                        RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
                        null,
                        null);
                }
                else if (RoofAutomaticPurlinRafterSourceRules.AreCrossSectionsEqual(
                             widthMm,
                             heightMm,
                             _hostActualWidthMm,
                             _hostActualHeightMm))
                {
                    // Manual matches current-roof actual — acknowledge without conflict.
                    SetDraftAcknowledgement(
                        RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
                        _hostActualWidthMm,
                        _hostActualHeightMm);
                }
                else if (previousManualWidthMm is { } previousWidth &&
                         previousManualHeightMm is { } previousHeight &&
                         RoofAutomaticPurlinRafterSourceRules.AreCrossSectionsEqual(
                             widthMm,
                             heightMm,
                             previousWidth,
                             previousHeight) &&
                         _draftAcknowledgedActualKind ==
                             RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile &&
                         _draftAcknowledgedActualWidthMm is { } ackWidth &&
                         _draftAcknowledgedActualHeightMm is { } ackHeight &&
                         RoofAutomaticPurlinRafterSourceRules.AreCrossSectionsEqual(
                             ackWidth,
                             ackHeight,
                             _hostActualWidthMm,
                             _hostActualHeightMm))
                {
                    // Same PreferManual profile re-confirmed; keep ExplicitProfile so an
                    // already acknowledged difference is not repeatedly warned.
                }
                else
                {
                    // New differing manual candidate (e.g. external-roof W×H copy).
                    // Do NOT invent ExplicitProfile(host): that suppressed same-session
                    // conflict until reopen. Force reconcile via Unspecified.
                    SetDraftAcknowledgement(
                        RoofAutomaticPurlinAcknowledgedActualKind.Unspecified,
                        null,
                        null);
                }
            }

            WallPlateRow.SetRafterHeightMm(heightMm);
            foreach (var row in Rows)
            {
                row.SetRafterHeightMm(heightMm);
            }

            OnPropertyChanged(nameof(RafterWidthMm));
            OnPropertyChanged(nameof(RafterHeightMm));
            OnPropertyChanged(nameof(RafterDimensionText));
            OnPropertyChanged(nameof(RecoverableManualRafterWidthMm));
            OnPropertyChanged(nameof(RecoverableManualRafterHeightMm));
            OnPropertyChanged(nameof(DraftRafterSourcePolicy));
            OnPropertyChanged(nameof(DraftAcknowledgedActualKind));
            OnPropertyChanged(nameof(DraftAcknowledgedActualWidthMm));
            OnPropertyChanged(nameof(DraftAcknowledgedActualHeightMm));
            ReconcileDraftRafterSourceAgainstHostActual();
            return true;
        }

        if (source is
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe or
            AutomaticPurlinRafterDimensionSource.SelectedRafter)
        {
            _draftRafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual;
            _hostActualAvailable = true;
            _hostActualWidthMm = widthMm;
            _hostActualHeightMm = heightMm;
            if (updateAcknowledgement)
            {
                SetDraftAcknowledgement(
                    RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
                    widthMm,
                    heightMm);
            }
        }

        WallPlateRow.SetRafterHeightMm(heightMm);
        foreach (var row in Rows)
        {
            row.SetRafterHeightMm(heightMm);
        }

        OnPropertyChanged(nameof(RafterWidthMm));
        OnPropertyChanged(nameof(RafterHeightMm));
        OnPropertyChanged(nameof(RafterDimensionText));
        OnPropertyChanged(nameof(RecoverableManualRafterWidthMm));
        OnPropertyChanged(nameof(RecoverableManualRafterHeightMm));
        OnPropertyChanged(nameof(DraftRafterSourcePolicy));
        OnPropertyChanged(nameof(DraftAcknowledgedActualKind));
        OnPropertyChanged(nameof(DraftAcknowledgedActualWidthMm));
        OnPropertyChanged(nameof(DraftAcknowledgedActualHeightMm));
        NotifyRafterSourcePresentation();
        Recalculate();
        return true;
    }

    /// <summary>
    /// Re-runs schema-3 source rules against the CURRENT roof's actual W×H after a
    /// new manual candidate is confirmed in-session. External picks never overwrite
    /// <see cref="_hostActualWidthMm"/>; reopen must not be required for conflict UI.
    /// </summary>
    private void ReconcileDraftRafterSourceAgainstHostActual()
    {
        _rafterSourceConflictUnresolved = false;
        _missingActualTransitionUnresolved = false;
        _persistedManualRafterInvalid = false;

        if (!RoofAutomaticPurlinRafterSourceRules.TryValidateManualProfile(
                _recoverableManualRafterWidthMm,
                _recoverableManualRafterHeightMm,
                out _) ||
            !RoofAutomaticPurlinRafterSourceRules.TryValidateAcknowledgedActual(
                _draftAcknowledgedActualKind,
                _draftAcknowledgedActualWidthMm,
                _draftAcknowledgedActualHeightMm,
                out _))
        {
            _persistedManualRafterInvalid = true;
            _rafterDimensionSource = AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
            NotifyRafterSourcePresentation();
            Recalculate();
            return;
        }

        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            ManualRafterWidthMm = _recoverableManualRafterWidthMm,
            ManualRafterHeightMm = _recoverableManualRafterHeightMm,
            RafterSourcePolicy = _draftRafterSourcePolicy,
            AcknowledgedActualKind = _draftAcknowledgedActualKind,
            AcknowledgedActualWidthMm = _draftAcknowledgedActualWidthMm,
            AcknowledgedActualHeightMm = _draftAcknowledgedActualHeightMm,
        };

        // Always pass stored host actual — _rafterWidthMm may already be the manual candidate.
        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            _hostActualAvailable,
            _hostActualWidthMm,
            _hostActualHeightMm,
            actualAmbiguous: false);

        ApplyRafterSourceResolution(
            resolved,
            hostSourceHint: AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe);
        NotifyRafterSourcePresentation();
        Recalculate();
    }

    private void ApplyRafterSourceResolution(
        RoofAutomaticPurlinRafterSourceRules.Resolution resolved,
        AutomaticPurlinRafterDimensionSource hostSourceHint)
    {
        switch (resolved.Outcome)
        {
            case RoofAutomaticPurlinRafterSourceRules.Outcome.UseManual:
                _rafterWidthMm = resolved.WidthMm!.Value;
                _rafterHeightMm = resolved.HeightMm!.Value;
                _rafterDimensionSource = AutomaticPurlinRafterDimensionSource.ExplicitManual;
                _draftRafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual;
                WallPlateRow.SetRafterHeightMm(_rafterHeightMm);
                foreach (var row in Rows)
                {
                    row.SetRafterHeightMm(_rafterHeightMm);
                }

                break;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.MissingActualWithRecoverableManual:
                _missingActualTransitionUnresolved = true;
                _rafterWidthMm = resolved.WidthMm!.Value;
                _rafterHeightMm = resolved.HeightMm!.Value;
                _rafterDimensionSource = AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
                _draftRafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual;
                WallPlateRow.SetRafterHeightMm(_rafterHeightMm);
                foreach (var row in Rows)
                {
                    row.SetRafterHeightMm(_rafterHeightMm);
                }

                break;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.UseActual:
                _rafterDimensionSource = hostSourceHint is
                    AutomaticPurlinRafterDimensionSource.SelectedRafter
                        ? AutomaticPurlinRafterDimensionSource.SelectedRafter
                        : AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe;
                _draftRafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual;
                break;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.Conflict:
                _rafterSourceConflictUnresolved = true;
                _conflictActualRafterWidthMm = _hostActualWidthMm;
                _conflictActualRafterHeightMm = _hostActualHeightMm;
                if (resolved.WidthMm is { } conflictManualWidth &&
                    resolved.HeightMm is { } conflictManualHeight)
                {
                    _rafterWidthMm = conflictManualWidth;
                    _rafterHeightMm = conflictManualHeight;
                    WallPlateRow.SetRafterHeightMm(_rafterHeightMm);
                    foreach (var row in Rows)
                    {
                        row.SetRafterHeightMm(_rafterHeightMm);
                    }
                }

                // Source classification is independent of conflict: a PreferManual /
                // external W×H candidate remains ExplicitManual while the user chooses
                // Keep Manual / Use Actual. UnresolvedProfileSeed here left SelectedRafter
                // semantics looking "stuck" until reopen.
                _rafterDimensionSource = AutomaticPurlinRafterDimensionSource.ExplicitManual;
                _draftRafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual;
                break;

            case RoofAutomaticPurlinRafterSourceRules.Outcome.InvalidManual:
                _persistedManualRafterInvalid = true;
                _rafterDimensionSource = AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
                break;

            default:
                _rafterDimensionSource = AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
                break;
        }
    }

    private void SetDraftAcknowledgement(
        RoofAutomaticPurlinAcknowledgedActualKind kind,
        double? widthMm,
        double? heightMm)
    {
        _draftAcknowledgedActualKind = kind;
        _draftAcknowledgedActualWidthMm = widthMm;
        _draftAcknowledgedActualHeightMm = heightMm;
    }

    private void NotifyRafterSourcePresentation()
    {
        OnPropertyChanged(nameof(RafterDimensionSource));
        OnPropertyChanged(nameof(HasAuthoritativeRafterProfile));
        OnPropertyChanged(nameof(RequiresManualRafterInput));
        OnPropertyChanged(nameof(HasExplicitManualRafterProfile));
        OnPropertyChanged(nameof(PrefersCadRafterPick));
        OnPropertyChanged(nameof(ShowNoRafterFooterWarning));
        OnPropertyChanged(nameof(ShowManualRafterFooterInfo));
        OnPropertyChanged(nameof(ShowRafterSourceConflictWarning));
        OnPropertyChanged(nameof(ShowMissingActualTransitionWarning));
        OnPropertyChanged(nameof(ShowProvisionalManualRafterFooterInfo));
        OnPropertyChanged(nameof(ShowPersistedManualRafterInvalidWarning));
        OnPropertyChanged(nameof(ManualRafterFooterInfoText));
        OnPropertyChanged(nameof(ProvisionalManualRafterFooterInfoText));
        OnPropertyChanged(nameof(RafterSourceConflictFooterText));
        OnPropertyChanged(nameof(PersistedManualRafterInvalidFooterText));
        OnPropertyChanged(nameof(CanApply));
    }

    private void InitializeRafterSourceFromLayout(
        RoofAutomaticPurlinLayout layout,
        AutomaticPurlinRafterDimensionSource hostSource)
    {
        _draftRafterSourcePolicy = layout.RafterSourcePolicy;
        _recoverableManualRafterWidthMm = layout.ManualRafterWidthMm;
        _recoverableManualRafterHeightMm = layout.ManualRafterHeightMm;
        _draftAcknowledgedActualKind = layout.AcknowledgedActualKind;
        _draftAcknowledgedActualWidthMm = layout.AcknowledgedActualWidthMm;
        _draftAcknowledgedActualHeightMm = layout.AcknowledgedActualHeightMm;
        _hostActualAvailable = hostSource is
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe or
            AutomaticPurlinRafterDimensionSource.SelectedRafter;
        _hostActualWidthMm = _rafterWidthMm;
        _hostActualHeightMm = _rafterHeightMm;
        _rafterSourceConflictUnresolved = false;
        _missingActualTransitionUnresolved = false;
        _persistedManualRafterInvalid = false;

        if (!RoofAutomaticPurlinRafterSourceRules.TryValidateManualProfile(
                layout.ManualRafterWidthMm,
                layout.ManualRafterHeightMm,
                out _) ||
            !RoofAutomaticPurlinRafterSourceRules.TryValidateAcknowledgedActual(
                layout.AcknowledgedActualKind,
                layout.AcknowledgedActualWidthMm,
                layout.AcknowledgedActualHeightMm,
                out _))
        {
            _persistedManualRafterInvalid = true;
            _rafterDimensionSource = AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed;
            NotifyRafterSourcePresentation();
            return;
        }

        var actualAvailable = _hostActualAvailable;
        var resolved = RoofAutomaticPurlinRafterSourceRules.Resolve(
            layout,
            actualAvailable,
            _hostActualWidthMm,
            _hostActualHeightMm,
            actualAmbiguous: false);

        ApplyRafterSourceResolution(resolved, hostSource);
        NotifyRafterSourcePresentation();
    }

    internal bool TryGetPreviewPlan(out RoofAutomaticPurlinPlan? plan)
    {
        plan = _previewPlan;
        return plan is not null;
    }

    internal bool TryCreateDraft(
        out RoofAutomaticPurlinLayout? layout,
        out RoofRelativeElevationDatum? datum)
    {
        layout = null;
        datum = null;
        if (!TryCreateDatum(out datum) || !TryCreateLayout(out layout, out _))
        {
            return false;
        }

        return true;
    }

    internal bool TryBeginApply(
        out RoofAutomaticPurlinLayout? layout,
        out RoofRelativeElevationDatum? datum,
        out RoofAutomaticPurlinPlan? previewPlan)
    {
        layout = null;
        datum = null;
        previewPlan = null;
        if (!CanApply ||
            !TryCreateDraft(out layout, out datum) ||
            layout is null ||
            datum is null ||
            _previewPlan is null)
        {
            return false;
        }

        previewPlan = _previewPlan;
        _isApplyInProgress = true;
        OnPropertyChanged(nameof(CanApply));
        return true;
    }

    internal void CompleteApplyFailure()
    {
        _isApplyInProgress = false;
        SetValidation("AutomaticPurlin_ApplyFailed", "ProductionApplyFailed");
        OnPropertyChanged(nameof(CanApply));
    }

    internal void ForcePreview() => Recalculate();

    private AutomaticPurlinRowViewModel AddRowCore(RoofAutomaticPurlinLayoutItem item)
    {
        var row = CreateRow(item, allowZeroPlacementValue: false);
        row.Changed += Row_Changed;
        row.PlacementModeChanging += Row_PlacementModeChanging;
        row.PlacementValueUserEdited += Row_PlacementValueUserEdited;
        Rows.Add(row);
        RefreshRowDisplayNames();
        return row;
    }

    private AutomaticPurlinRowViewModel CreateRow(
        RoofAutomaticPurlinLayoutItem item,
        bool allowZeroPlacementValue,
        double? defaultWidthMm = null,
        double? defaultHeightMm = null,
        RoofAutomaticPurlinGeneratorRole role = RoofAutomaticPurlinGeneratorRole.Intermediate) =>
        new(
            item,
            PlacementModes,
            SeatingModes,
            RidgeReferences,
            RafterHeightMm,
            _culture,
            Text,
            allowZeroPlacementValue,
            defaultWidthMm ?? PurlinWidthMm,
            defaultHeightMm ?? PurlinHeightMm,
            role);

    private void RefreshRowDisplayNames()
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            Rows[index].SetDisplayIndex(index + 1);
        }

        RefreshEditorTabPresentation();
    }

    private void RebuildEditorTabs(
        AutomaticPurlinRowViewModel? selectIntermediateRow = null,
        bool preferWallPlate = false)
    {
        var previousKind = _selectedEditorTab?.Kind;
        var previousRowId = _selectedEditorTab?.IntermediateRow?.LayoutItemId;

        EditorTabs.Clear();
        EditorTabs.Add(CreateEditorTab(
            AutomaticPurlinEditorTabKind.WallPlate,
            intermediateRow: null));
        EditorTabs.Add(CreateEditorTab(
            AutomaticPurlinEditorTabKind.Ridge,
            intermediateRow: null));
        foreach (var row in Rows)
        {
            EditorTabs.Add(CreateEditorTab(
                AutomaticPurlinEditorTabKind.Intermediate,
                intermediateRow: row));
        }

        AutomaticPurlinEditorTabViewModel? next = null;
        if (selectIntermediateRow is not null)
        {
            next = EditorTabs.FirstOrDefault(tab =>
                ReferenceEquals(tab.IntermediateRow, selectIntermediateRow));
        }
        else if (preferWallPlate)
        {
            next = EditorTabs.FirstOrDefault(tab =>
                tab.Kind == AutomaticPurlinEditorTabKind.WallPlate);
        }
        else if (previousKind == AutomaticPurlinEditorTabKind.Intermediate &&
                 previousRowId is not null)
        {
            next = EditorTabs.FirstOrDefault(tab =>
                string.Equals(
                    tab.IntermediateRow?.LayoutItemId,
                    previousRowId,
                    StringComparison.Ordinal));
        }
        else if (previousKind is { } kind)
        {
            next = EditorTabs.FirstOrDefault(tab => tab.Kind == kind);
        }

        _isSyncingEditorTabSelection = true;
        try
        {
            SelectedEditorTab = next ?? EditorTabs.FirstOrDefault();
        }
        finally
        {
            _isSyncingEditorTabSelection = false;
        }
    }

    private AutomaticPurlinEditorTabViewModel CreateEditorTab(
        AutomaticPurlinEditorTabKind kind,
        AutomaticPurlinRowViewModel? intermediateRow)
    {
        ResolveEditorTabPresentation(kind, intermediateRow, out var title, out var hasError, out var automation);
        return new AutomaticPurlinEditorTabViewModel(
            kind,
            this,
            intermediateRow,
            title,
            hasError,
            automation);
    }

    private void RefreshEditorTabPresentation()
    {
        foreach (var tab in EditorTabs)
        {
            ResolveEditorTabPresentation(
                tab.Kind,
                tab.IntermediateRow,
                out var title,
                out var hasError,
                out var automation);
            tab.UpdatePresentation(title, hasError, automation);
        }
    }

    private void ResolveEditorTabPresentation(
        AutomaticPurlinEditorTabKind kind,
        AutomaticPurlinRowViewModel? intermediateRow,
        out string title,
        out bool hasError,
        out string automationName)
    {
        switch (kind)
        {
            case AutomaticPurlinEditorTabKind.WallPlate:
                title = Text("AutomaticPurlin_TabWallPlate");
                hasError = WallPlateTabHasError;
                automationName = FormatTabAutomationNameFromTitle(title, hasError);
                return;
            case AutomaticPurlinEditorTabKind.Ridge:
                title = Text("AutomaticPurlin_TabRidge");
                hasError = RidgeTabHasError;
                automationName = FormatTabAutomationNameFromTitle(title, hasError);
                return;
            case AutomaticPurlinEditorTabKind.Intermediate:
                title = intermediateRow?.DisplayName ?? Text("AutomaticPurlin_TabIntermediate");
                hasError = intermediateRow?.HasAnyFieldError == true;
                automationName = FormatTabAutomationNameFromTitle(title, hasError);
                return;
            default:
                title = string.Empty;
                hasError = false;
                automationName = string.Empty;
                return;
        }
    }

    private void SyncSelectedEditorTabFromSelectedRow()
    {
        if (_isSyncingEditorTabSelection || SelectedRow is null)
        {
            return;
        }

        var match = EditorTabs.FirstOrDefault(tab =>
            ReferenceEquals(tab.IntermediateRow, SelectedRow));
        if (match is null || ReferenceEquals(SelectedEditorTab, match))
        {
            return;
        }

        _isSyncingEditorTabSelection = true;
        try
        {
            SelectedEditorTab = match;
        }
        finally
        {
            _isSyncingEditorTabSelection = false;
        }
    }

    private void Row_Changed(object? sender, EventArgs e)
    {
        if (_suppressRowChangedDepth > 0)
        {
            return;
        }

        NotifySchematicState();
        Recalculate();
    }

    private void Row_PlacementValueUserEdited(object? sender, EventArgs e) =>
        _placementModeConversionFailed = false;

    private void Row_PlacementModeChanging(
        AutomaticPurlinRowViewModel row,
        RoofAutomaticPurlinPlacementMode previousMode,
        RoofAutomaticPurlinPlacementMode nextMode)
    {
        if (previousMode == nextMode)
        {
            return;
        }

        if (!TryConvertPlacementModePreservingPhysical(row, previousMode, nextMode))
        {
            _placementModeConversionFailed = true;
        }
        else
        {
            _placementModeConversionFailed = false;
        }
    }

    private void Recalculate(bool raisePreviewChanged = true)
    {
        if (_isRecalculating)
        {
            // Width/Height (and other) edits that nest inside an in-flight pass must not
            // be dropped — otherwise min-distance validation stays stale until another
            // field (e.g. PlanDistance) forces a fresh Recalculate.
            _recalculateQueued = true;
            return;
        }

        do
        {
            _recalculateQueued = false;
            RecalculateCore(raisePreviewChanged);
        }
        while (_recalculateQueued);
    }

    private void RecalculateCore(bool raisePreviewChanged)
    {
        _isRecalculating = true;
        RoofRelativeElevationDatum? presentationDatum = null;
        RoofAutomaticPurlinLayout? presentationLayout = null;
        var draftIsValid = false;
        try
        {
            RefreshAllFieldValidation();
            if (_placementModeConversionFailed)
            {
                _currentDraftIsValid = false;
                SetValidation(
                    "AutomaticPurlin_ValidationPlacementModeConversion",
                    "PlacementModeConversionUnavailable");
                return;
            }

            if (_boundaryProvenance is null || !_boundaryProvenance.IsValid)
            {
                _currentDraftIsValid = false;
                SetValidation(
                    "AutomaticPurlin_ValidationBoundaryIdentity",
                    "BoundaryIdentityUnavailable");
                return;
            }

            if (!TryCreateDatum(out var datum) || datum is null)
            {
                _currentDraftIsValid = false;
                if (_lastDatumResolutionError is { } datumError)
                {
                    MarkPlanDistancePlacementFieldErrors(datumError);
                    if (datumError == RoofAutomaticPurlinPlanError.ElevationOutsideRoof &&
                        RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                            _geometry,
                            out var maxExclusiveMm))
                    {
                        _previewDiagnosticReason = datumError.ToString();
                        _validationMessage = string.Format(
                            _culture,
                            Text("AutomaticPurlin_ValidationPlanDistanceRange"),
                            maxExclusiveMm.ToString("0.###", _culture));
                        OnPropertyChanged(nameof(ValidationMessage));
                    }
                    else if (datumError ==
                             RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum)
                    {
                        _previewDiagnosticReason = datumError.ToString();
                        _validationMessage = BuildWallPlateMinPlanDistanceValidationMessage();
                        OnPropertyChanged(nameof(ValidationMessage));
                    }
                    else
                    {
                        SetValidation(MapPlanError(datumError), datumError.ToString());
                    }

                    return;
                }

                SetValidation(
                    "AutomaticPurlin_ValidationDatum",
                    "InvalidRelativeElevationDatum");
                return;
            }

            presentationDatum = datum;

            if (RidgeEnabled && RidgeReferences.Count == 0)
            {
                _currentDraftIsValid = false;
                SetValidation(
                    _hasInclinedRidge
                        ? "AutomaticPurlin_ValidationInclinedRidge"
                        : "AutomaticPurlin_ValidationNoHorizontalRidge",
                    _hasInclinedRidge ? "InclinedRidge" : "HorizontalRidgeMissing");
                return;
            }

            if (!TryCreateLayout(out var layout, out var rowError) || layout is null)
            {
                _currentDraftIsValid = false;
                MarkPlanDistancePlacementFieldErrors();
                SetValidation(
                    rowError ?? "AutomaticPurlin_ValidationValue",
                    rowError == "AutomaticPurlin_ValidationRidgeReference"
                        ? "RidgeReferenceMissing"
                        : "InvalidPlacementValue");
                return;
            }

            presentationLayout = layout;

            var result = RoofAutomaticPurlinPlanner.Create(
                _geometry,
                _boundaryProvenance,
                layout,
                CreatePlanningInput(datum));
            if (!result.IsValid || result.Plan is null)
            {
                _currentDraftIsValid = false;
                MarkPlanDistancePlacementFieldErrors(result.Error, result.FailedLayoutItemId);
                if (result.Error == RoofAutomaticPurlinPlanError.ElevationOutsideRoof &&
                    RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                        _geometry,
                        out var maxExclusiveMm))
                {
                    _previewDiagnosticReason = result.Error.ToString();
                    _validationMessage = string.Format(
                        _culture,
                        Text("AutomaticPurlin_ValidationPlanDistanceRange"),
                        maxExclusiveMm.ToString("0.###", _culture));
                    OnPropertyChanged(nameof(ValidationMessage));
                }
                else if (result.Error ==
                         RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum)
                {
                    _previewDiagnosticReason = result.Error.ToString();
                    _validationMessage = BuildWallPlateMinPlanDistanceValidationMessage();
                    OnPropertyChanged(nameof(ValidationMessage));
                }
                else
                {
                    SetValidation(MapPlanError(result.Error), result.Error.ToString());
                }

                return;
            }

            _previewPlan = result.Plan;
            RefreshWallPlateBottomEdgeBootstrapAbsoluteMmFromPreview();
            draftIsValid = true;
            _currentDraftIsValid = true;
            _placementModeConversionFailed = false;
            ApplyDerivedTechnicalValues(result.Plan);
            SetValidation(null, "None");
        }
        finally
        {
            _isRecalculating = false;
            if (!draftIsValid)
            {
                _currentDraftIsValid = false;
            }

            if (draftIsValid)
            {
                _isSchematicStale = false;
                // Only draw WallPlateBottom when WallPlate is enabled and resolved.
                var datumForPlane = presentationDatum;
                if (datumForPlane?.ReferenceKind ==
                        RoofRelativeElevationReferenceKind.WallPlateBottom &&
                    !WallPlateEnabled)
                {
                    datumForPlane = null;
                }

                _sectionPresentation = AutomaticPurlinSectionPresentation.Create(
                    _geometry,
                    _previewPlan,
                    ResolveSectionTitle,
                    Text,
                    _culture,
                    RafterHeightMm,
                    RafterWidthMm,
                    datumForPlane,
                    presentationLayout);
                RefreshTechnicalRoofPlaneValues(datumForPlane);
            }
            else if (_previewPlan is null)
            {
                _isSchematicStale = false;
                _sectionPresentation = AutomaticPurlinSectionPresentation.Empty;
                RefreshTechnicalRoofPlaneValues(null);
            }
            else
            {
                // Retain last valid schematic + technical values while the draft is invalid.
                _isSchematicStale = true;
            }

            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(IsSchematicStale));
            OnPropertyChanged(nameof(SchematicStaleMessage));
            OnPropertyChanged(nameof(SectionPresentation));
            if (raisePreviewChanged)
            {
                PreviewChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void ApplyDerivedTechnicalValues(RoofAutomaticPurlinPlan plan)
    {
        foreach (var row in Rows)
        {
            row.UpdateDerived(
                plan.Items.FirstOrDefault(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate &&
                    string.Equals(item.LayoutItemId, row.LayoutItemId, StringComparison.Ordinal)));
        }

        WallPlateRow.UpdateDerived(
            plan.Items.FirstOrDefault(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate));
    }

    private void MarkPlanDistancePlacementFieldErrors(
        RoofAutomaticPurlinPlanError? planError = null,
        string? failedLayoutItemId = null)
    {
        if (planError == RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum)
        {
            if (WallPlateEnabled)
            {
                WallPlateRow.SetPlacementValueError(BuildWallPlateMinPlanDistanceValidationMessage());
            }

            NotifyTabErrorPresentation();
            return;
        }

        if (planError is RoofAutomaticPurlinPlanError.ElevationOutsideRoof or
            RoofAutomaticPurlinPlanError.CriticalEventElevation)
        {
            var planDistanceMessage = BuildPlanDistanceRangeValidationMessage(planError.Value);
            var bottomEdgeMessage = planError == RoofAutomaticPurlinPlanError.CriticalEventElevation
                ? Text("AutomaticPurlin_ValidationCriticalElevation")
                : Text("AutomaticPurlin_ValidationOutsideRoof");
            if (string.IsNullOrWhiteSpace(failedLayoutItemId) ||
                string.Equals(
                    failedLayoutItemId,
                    RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                    StringComparison.Ordinal))
            {
                if (WallPlateEnabled)
                {
                    WallPlateRow.SetPlacementValueError(
                        IsPlanDistanceMode(WallPlateRow.PlacementMode)
                            ? planDistanceMessage
                            : bottomEdgeMessage);
                }
            }

            foreach (var row in Rows)
            {
                if (!string.IsNullOrWhiteSpace(failedLayoutItemId) &&
                    !string.Equals(failedLayoutItemId, row.LayoutItemId, StringComparison.Ordinal))
                {
                    continue;
                }

                // Broadcast (null id) keeps prior PlanDistance-only scope so sibling
                // BottomEdge intermediates are not falsely marked.
                if (string.IsNullOrWhiteSpace(failedLayoutItemId) &&
                    !IsPlanDistanceMode(row.PlacementMode))
                {
                    continue;
                }

                row.SetPlacementValueError(
                    IsPlanDistanceMode(row.PlacementMode)
                        ? planDistanceMessage
                        : bottomEdgeMessage);
            }

            NotifyTabErrorPresentation();
            return;
        }

        // WallPlate plan-distance zero (and any station below width/2) is rejected by Core.
    }

    private string BuildPlanDistanceRangeValidationMessage(RoofAutomaticPurlinPlanError error)
    {
        if (error == RoofAutomaticPurlinPlanError.CriticalEventElevation)
        {
            return Text("AutomaticPurlin_ValidationCriticalElevation");
        }

        if (RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                _geometry,
                out var maxExclusiveMm) &&
            maxExclusiveMm > 0d)
        {
            return string.Format(
                _culture,
                Text("AutomaticPurlin_ValidationPlanDistanceRange"),
                maxExclusiveMm.ToString("0.###", _culture));
        }

        return Text("AutomaticPurlin_ValidationOutsideRoof");
    }

    private string BuildWallPlateMinPlanDistanceValidationMessage()
    {
        var widthMm = ResolveDraftWallPlateWidthMm();
        var minimumMm =
            RoofAutomaticPurlinWallPlatePlanDistanceRules.ResolveMinimumPlanDistanceFromEaveMm(
                widthMm);
        return string.Format(
            _culture,
            Text("AutomaticPurlin_ValidationWallPlateMinPlanDistance"),
            minimumMm.ToString("0.###", _culture),
            widthMm.ToString("0.###", _culture));
    }

    /// <summary>
    /// Planning input must use the live WallPlate Width/Height text, not the frozen
    /// constructor defaults — otherwise min-distance validation ignores width edits.
    /// </summary>
    private RoofAutomaticPurlinPlanningInput CreatePlanningInput(
        RoofRelativeElevationDatum datum) =>
        new(datum, PurlinHeightMm, RafterHeightMm)
        {
            PurlinWidthMm = PurlinWidthMm,
            WallPlatesEnabled = WallPlateEnabled,
            WallPlateWidthMm = ResolveDraftWallPlateWidthMm(),
            WallPlateHeightMm = ResolveDraftWallPlateHeightMm(),
        };

    private double ResolveDraftWallPlateWidthMm() =>
        TryParseMillimetres(WallPlateRow.WidthText, out var widthMm) && widthMm > 0d
            ? widthMm
            : WallPlateWidthMm;

    private double ResolveDraftWallPlateHeightMm() =>
        TryParseMillimetres(WallPlateRow.HeightText, out var heightMm) && heightMm > 0d
            ? heightMm
            : WallPlateHeightMm;

    /// <summary>
    /// Strešná rovina from physical upper-rafter-face geometry (Core rules).
    /// Never from schematic SVG/affine edges. Spodná/Os/Horná stay on plan profiles.
    /// </summary>
    private void RefreshTechnicalRoofPlaneValues(RoofRelativeElevationDatum? datum)
    {
        if (_previewPlan is null || datum is null)
        {
            WallPlateRow.SetRoofPlaneRelative(null);
            foreach (var row in Rows)
            {
                row.SetRoofPlaneRelative(null);
            }

            if (!RidgeEnabled || _previewPlan is null)
            {
                RidgeTechnicalSummary.Clear();
            }
            else
            {
                var ridgeOnly = _previewPlan.Items.FirstOrDefault(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
                RidgeTechnicalSummary.UpdateFrom(ridgeOnly, roofPlaneRelativeMm: null);
            }

            return;
        }

        var pitchDegrees = _geometry.PrimarySlopeDegrees;
        var rafterHeightMm = RafterHeightMm;

        WallPlateRow.SetRoofPlaneRelative(
            TryResolvePlanItemRoofPlaneRelativeMm(
                _previewPlan.Items.FirstOrDefault(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate),
                pitchDegrees,
                rafterHeightMm));

        foreach (var row in Rows)
        {
            row.SetRoofPlaneRelative(
                TryResolvePlanItemRoofPlaneRelativeMm(
                    _previewPlan.Items.FirstOrDefault(item =>
                        item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate &&
                        string.Equals(item.LayoutItemId, row.LayoutItemId, StringComparison.Ordinal)),
                    pitchDegrees,
                    rafterHeightMm));
        }

        if (!RidgeEnabled)
        {
            RidgeTechnicalSummary.Clear();
            return;
        }

        var ridgeItem = _previewPlan.Items.FirstOrDefault(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        RidgeTechnicalSummary.UpdateFrom(
            ridgeItem,
            TryResolvePlanItemRoofPlaneRelativeMm(ridgeItem, pitchDegrees, rafterHeightMm));
    }

    private static double? TryResolvePlanItemRoofPlaneRelativeMm(
        RoofAutomaticPurlinPlanItem? item,
        double pitchDegrees,
        double rafterHeightMm)
    {
        if (!RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                item,
                pitchDegrees,
                rafterHeightMm,
                out var roofPlaneRelativeMm))
        {
            return null;
        }

        return roofPlaneRelativeMm;
    }

    private string ResolveSectionTitle(RoofAutomaticPurlinPlanItem item) =>
        item.GeneratorRole switch
        {
            RoofAutomaticPurlinGeneratorRole.WallPlate => Text("AutomaticPurlin_WallPlateSection"),
            RoofAutomaticPurlinGeneratorRole.Ridge => Text("AutomaticPurlin_SchematicRidge"),
            _ => ResolveIntermediateSectionTitle(item.LayoutItemId),
        };

    private string ResolveIntermediateSectionTitle(string? layoutItemId)
    {
        if (layoutItemId is null)
        {
            return Text("AutomaticPurlin_SchematicIntermediate");
        }

        for (var index = 0; index < Rows.Count; index++)
        {
            if (string.Equals(Rows[index].LayoutItemId, layoutItemId, StringComparison.Ordinal))
            {
                return string.Format(
                    _culture,
                    Text("AutomaticPurlin_IntermediateRowFormat"),
                    index + 1);
            }
        }

        return Text("AutomaticPurlin_SchematicIntermediate");
    }

    private bool TryCreateDatum(out RoofRelativeElevationDatum? datum)
    {
        datum = null;
        _lastDatumResolutionError = null;
        var relativeMm = _loadedRelativeReferenceMm;
        if (_relativeReferenceEdited &&
            !RoofRelativeElevationDatumRules.TryParseMetres(
                RelativeReferenceText,
                _culture,
                out relativeMm))
        {
            return false;
        }

        switch (ReferenceKind)
        {
            case RoofRelativeElevationReferenceKind.WallPlateBottom when WallPlateEnabled:
            {
                if (!TryCreateLayout(out var layout, out _) || layout is null)
                {
                    return false;
                }

                var requested = new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.WallPlateBottom,
                    relativeMm,
                    0d);
                var resolved = RoofAutomaticPurlinPlanner.ResolveEffectiveDatum(
                    _geometry,
                    _boundaryProvenance,
                    layout,
                    CreatePlanningInput(requested) with { WallPlatesEnabled = true });
                if (!resolved.IsValid || resolved.Datum is null)
                {
                    _lastDatumResolutionError = resolved.Error;
                    return false;
                }

                datum = resolved.Datum;
                SetReferenceLocalZTextIfChanged(FormatLocalZText(datum.ReferenceLocalZMm));
                return true;
            }

            case RoofRelativeElevationReferenceKind.WallPlateBottom:
            {
                // Wall plates disabled: matches ResolveEffectiveDatum passthrough — LocalZ
                // comes from the visible/synced field (seeded on load), never a cross-kind cache.
                if (!TryParseMillimetres(ReferenceLocalZText, out var localZMm))
                {
                    return false;
                }

                var validation = RoofRelativeElevationDatumRules.Validate(
                    RoofRelativeElevationDatumSchema.CurrentVersion,
                    RoofRelativeElevationReferenceKind.WallPlateBottom,
                    relativeMm,
                    localZMm);
                datum = validation.Datum;
                return validation.IsValid;
            }

            case RoofRelativeElevationReferenceKind.SourceEavePlane:
            {
                // Roof-local eave plane is Z = 0. Ignore hidden/stale LocalZ text.
                SetReferenceLocalZTextIfChanged(FormatLocalZText(0d));
                var validation = RoofRelativeElevationDatumRules.Validate(
                    RoofRelativeElevationDatumSchema.CurrentVersion,
                    RoofRelativeElevationReferenceKind.SourceEavePlane,
                    relativeMm,
                    0d);
                datum = validation.Datum;
                return validation.IsValid;
            }

            case RoofRelativeElevationReferenceKind.ExplicitLocalPlane:
            {
                if (!TryParseMillimetres(ReferenceLocalZText, out var localZMm))
                {
                    return false;
                }

                _explicitReferenceLocalZMm = localZMm;
                var validation = RoofRelativeElevationDatumRules.Validate(
                    RoofRelativeElevationDatumSchema.CurrentVersion,
                    RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                    relativeMm,
                    localZMm);
                datum = validation.Datum;
                return validation.IsValid;
            }

            default:
                return false;
        }
    }

    private void SyncReferenceLocalZTextForCurrentKind(bool raisePropertyChanged)
    {
        var localZMm = ReferenceKind switch
        {
            RoofRelativeElevationReferenceKind.SourceEavePlane => 0d,
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane => _explicitReferenceLocalZMm,
            // WallPlateBottom text is filled after ResolveEffectiveDatum in TryCreateDatum.
            RoofRelativeElevationReferenceKind.WallPlateBottom =>
                TryParseMillimetres(_referenceLocalZText, out var current) ? current : 0d,
            _ => 0d,
        };

        var text = FormatLocalZText(localZMm);
        if (string.Equals(_referenceLocalZText, text, StringComparison.Ordinal))
        {
            return;
        }

        _referenceLocalZText = text;
        if (raisePropertyChanged)
        {
            OnPropertyChanged(nameof(ReferenceLocalZText));
        }
    }

    private void SetReferenceLocalZTextIfChanged(string text)
    {
        if (string.Equals(_referenceLocalZText, text, StringComparison.Ordinal))
        {
            return;
        }

        _referenceLocalZText = text;
        OnPropertyChanged(nameof(ReferenceLocalZText));
    }

    private string FormatLocalZText(double localZMm) =>
        localZMm.ToString("0.###", _culture);

    private readonly record struct BottomEdgeMemberCapture(
        string LayoutItemId,
        RoofAutomaticPurlinGeneratorRole Role,
        double BottomLocalZMm,
        double CenterLocalZMm,
        double TopLocalZMm,
        double StationXMm);

    private sealed class BottomEdgePhysicalCapture
    {
        public List<BottomEdgeMemberCapture> Members { get; } = new();
        public double? WallPlateBottomLocalZMm { get; set; }
    }

    private bool TryCaptureBottomEdgePhysicalState(out BottomEdgePhysicalCapture? capture)
    {
        capture = null;
        if (_previewPlan is null)
        {
            return false;
        }

        var result = new BottomEdgePhysicalCapture();
        foreach (var item in _previewPlan.Items)
        {
            if (item.ElevationProfile is null)
            {
                continue;
            }

            var stationX = 0.5d * (item.Segment3D.Start.X + item.Segment3D.End.X);
            result.Members.Add(new BottomEdgeMemberCapture(
                item.LayoutItemId ?? string.Empty,
                item.GeneratorRole,
                item.ElevationProfile.BottomLocalZMm,
                item.ElevationProfile.CenterLocalZMm,
                item.ElevationProfile.TopLocalZMm,
                stationX));

            if (item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                result.WallPlateBottomLocalZMm is null)
            {
                result.WallPlateBottomLocalZMm = item.ElevationProfile.BottomLocalZMm;
            }
        }

        if (result.Members.Count == 0)
        {
            return false;
        }

        capture = result;
        return true;
    }

    private void RebaseBottomEdgePlacementsForReferenceChange(
        RoofRelativeElevationReferenceKind previousKind,
        RoofRelativeElevationReferenceKind nextKind,
        BottomEdgePhysicalCapture? capture)
    {
        if (capture is null)
        {
            return;
        }

        // WallPlateBottom + BottomEdge WP with non-eave physical bottom cannot use Place=0
        // under Core bootstrap (would force LocalZ=0). Convert WP to plan-distance first so
        // the wall-plate bottom can define the datum without circular BottomEdge resolution.
        if (nextKind == RoofRelativeElevationReferenceKind.WallPlateBottom &&
            WallPlateEnabled &&
            WallPlateRow.PlacementMode ==
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference &&
            capture.WallPlateBottomLocalZMm is { } wpBottom &&
            Math.Abs(wpBottom) > 1e-6 &&
            TryFindCapturedMember(
                capture,
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                RoofAutomaticPurlinGeneratorRole.WallPlate,
                out var wpCapture))
        {
            TryConvertRowToPlanDistanceFromEavePreservingPhysical(
                WallPlateRow,
                wpCapture);
        }

        if (!TryResolveReferenceLocalZForRebase(nextKind, capture, out var newReferenceLocalZMm))
        {
            return;
        }

        RebaseBottomEdgePlacementsToReferenceLocalZ(newReferenceLocalZMm, capture);
        _ = previousKind;
    }

    private void RebaseBottomEdgePlacementsToReferenceLocalZ(
        double newReferenceLocalZMm,
        BottomEdgePhysicalCapture? capture)
    {
        if (capture is null || !double.IsFinite(newReferenceLocalZMm))
        {
            return;
        }

        _suppressRowChangedDepth++;
        try
        {
            if (WallPlateEnabled &&
                WallPlateRow.PlacementMode ==
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference &&
                TryFindCapturedMember(
                    capture,
                    RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                    RoofAutomaticPurlinGeneratorRole.WallPlate,
                    out var wallCapture))
            {
                var wallOffset = wallCapture.BottomLocalZMm - newReferenceLocalZMm;
                WallPlateRow.SetPlacementValueText(FormatLocalZText(wallOffset));
            }

            foreach (var row in Rows)
            {
                if (!row.Enabled ||
                    row.PlacementMode !=
                        RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
                {
                    continue;
                }

                if (!TryFindCapturedMember(
                        capture,
                        row.LayoutItemId,
                        RoofAutomaticPurlinGeneratorRole.Intermediate,
                        out var memberCapture))
                {
                    continue;
                }

                var offset = memberCapture.BottomLocalZMm - newReferenceLocalZMm;
                row.SetPlacementValueText(FormatLocalZText(offset));
            }
        }
        finally
        {
            _suppressRowChangedDepth--;
        }
    }

    private bool TryResolveReferenceLocalZForRebase(
        RoofRelativeElevationReferenceKind kind,
        BottomEdgePhysicalCapture capture,
        out double referenceLocalZMm)
    {
        referenceLocalZMm = 0d;
        switch (kind)
        {
            case RoofRelativeElevationReferenceKind.SourceEavePlane:
                referenceLocalZMm = 0d;
                return true;

            case RoofRelativeElevationReferenceKind.ExplicitLocalPlane:
                if (TryParseMillimetres(ReferenceLocalZText, out var explicitLocalZMm))
                {
                    referenceLocalZMm = explicitLocalZMm;
                    return true;
                }

                referenceLocalZMm = _explicitReferenceLocalZMm;
                return double.IsFinite(referenceLocalZMm);

            case RoofRelativeElevationReferenceKind.WallPlateBottom:
                if (!WallPlateEnabled || capture.WallPlateBottomLocalZMm is not { } wpBottom)
                {
                    return false;
                }

                // WP bottom defines zero. Do not resolve it from BottomEdge Place under
                // WallPlateBottom (circular); use the captured physical bottom.
                referenceLocalZMm = wpBottom;
                return double.IsFinite(referenceLocalZMm);

            default:
                return false;
        }
    }

    private static bool TryFindCapturedMember(
        BottomEdgePhysicalCapture capture,
        string layoutItemId,
        RoofAutomaticPurlinGeneratorRole role,
        out BottomEdgeMemberCapture member)
    {
        foreach (var candidate in capture.Members)
        {
            if (candidate.Role != role)
            {
                continue;
            }

            if (role == RoofAutomaticPurlinGeneratorRole.WallPlate ||
                string.Equals(candidate.LayoutItemId, layoutItemId, StringComparison.Ordinal))
            {
                member = candidate;
                return true;
            }
        }

        member = default;
        return false;
    }

    private bool TryConvertPlacementModePreservingPhysical(
        AutomaticPurlinRowViewModel row,
        RoofAutomaticPurlinPlacementMode previousMode,
        RoofAutomaticPurlinPlacementMode nextMode)
    {
        if (IsPlanDistanceMode(previousMode) && IsPlanDistanceMode(nextMode))
        {
            return TryConvertPlanDistanceEaveRidge(row, previousMode, nextMode);
        }

        if (previousMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference &&
            IsPlanDistanceMode(nextMode))
        {
            return TryConvertBottomEdgeToPlanDistance(row, nextMode);
        }

        if (IsPlanDistanceMode(previousMode) &&
            nextMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
        {
            return TryConvertPlanDistanceToBottomEdge(row);
        }

        return true;
    }

    private static bool IsPlanDistanceMode(RoofAutomaticPurlinPlacementMode mode) =>
        mode is RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave or
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

    private bool TryConvertPlanDistanceEaveRidge(
        AutomaticPurlinRowViewModel row,
        RoofAutomaticPurlinPlacementMode previousMode,
        RoofAutomaticPurlinPlacementMode nextMode)
    {
        if (!TryParseMillimetres(row.PlacementValueText, out var currentMm) ||
            !RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                _geometry,
                out var maxExclusiveMm) ||
            maxExclusiveMm <= 0d)
        {
            return false;
        }

        // Same member-axis convention: d_eave + d_ridge = horizontal eave→ridge run.
        var convertedMm = maxExclusiveMm - currentMm;
        if (!double.IsFinite(convertedMm) ||
            convertedMm < 0d ||
            (nextMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave &&
             convertedMm >= maxExclusiveMm) ||
            (previousMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave &&
             currentMm >= maxExclusiveMm))
        {
            return false;
        }

        if (nextMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge &&
            row.SelectedRidgeReference is null &&
            row.RidgeReferences.Count == 1)
        {
            _suppressRowChangedDepth++;
            try
            {
                row.SelectedRidgeReference = row.RidgeReferences[0];
                row.SetPlacementValueText(FormatLocalZText(convertedMm));
            }
            finally
            {
                _suppressRowChangedDepth--;
            }
        }
        else
        {
            _suppressRowChangedDepth++;
            try
            {
                row.SetPlacementValueText(FormatLocalZText(convertedMm));
            }
            finally
            {
                _suppressRowChangedDepth--;
            }
        }

        return true;
    }

    private bool TryConvertBottomEdgeToPlanDistance(
        AutomaticPurlinRowViewModel row,
        RoofAutomaticPurlinPlacementMode nextMode)
    {
        if (_previewPlan is null ||
            !TryFindPlanItemForRow(row, out var planItem) ||
            planItem is null ||
            !TryResolvePlanDistanceFromEaveMm(planItem, out var fromEaveMm) ||
            !RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                _geometry,
                out var maxExclusiveMm))
        {
            return false;
        }

        var valueMm = nextMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge
            ? maxExclusiveMm - fromEaveMm
            : fromEaveMm;
        if (!double.IsFinite(valueMm) || valueMm < 0d ||
            (nextMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave &&
             valueMm >= maxExclusiveMm))
        {
            return false;
        }

        _suppressRowChangedDepth++;
        try
        {
            if (nextMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge &&
                row.SelectedRidgeReference is null &&
                row.RidgeReferences.Count == 1)
            {
                row.SelectedRidgeReference = row.RidgeReferences[0];
            }

            row.SetPlacementValueText(FormatLocalZText(valueMm));
        }
        finally
        {
            _suppressRowChangedDepth--;
        }

        return true;
    }

    private bool TryConvertPlanDistanceToBottomEdge(AutomaticPurlinRowViewModel row)
    {
        if (_previewPlan is null ||
            !TryFindPlanItemForRow(row, out var planItem) ||
            planItem?.ElevationProfile is null ||
            !TryResolveReferenceLocalZForModeConversion(out var referenceLocalZMm))
        {
            return false;
        }

        // Authoritative physical bottom from the last valid plan — never RoofPlane /
        // seating-contact / upper-face Z, and never a datum rebuilt from the still-
        // unconverted PlanDistance PlacementValue text.
        var offsetMm =
            planItem.ElevationProfile.BottomLocalZMm - referenceLocalZMm;
        if (!double.IsFinite(offsetMm))
        {
            return false;
        }

        _suppressRowChangedDepth++;
        try
        {
            row.SetPlacementValueText(FormatLocalZText(offsetMm));
        }
        finally
        {
            _suppressRowChangedDepth--;
        }

        return true;
    }

    /// <summary>
    /// Reference LocalZ for placement-mode conversion. Must not call
    /// <see cref="TryCreateDatum"/> while the row still holds the previous mode's
    /// PlacementValue (e.g. PlanDistance 700 under WallPlateBottom would bootstrap
    /// ReferenceLocalZ=700 and yield BottomEdge offset ≈ −RoofPlane).
    /// </summary>
    private bool TryResolveReferenceLocalZForModeConversion(out double referenceLocalZMm)
    {
        referenceLocalZMm = 0d;
        switch (ReferenceKind)
        {
            case RoofRelativeElevationReferenceKind.SourceEavePlane:
                referenceLocalZMm = 0d;
                return true;

            case RoofRelativeElevationReferenceKind.ExplicitLocalPlane:
                if (TryParseMillimetres(ReferenceLocalZText, out referenceLocalZMm))
                {
                    return true;
                }

                referenceLocalZMm = _explicitReferenceLocalZMm;
                return double.IsFinite(referenceLocalZMm);

            case RoofRelativeElevationReferenceKind.WallPlateBottom:
                if (_previewPlan is null)
                {
                    return false;
                }

                var wallPlate = _previewPlan.Items.FirstOrDefault(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                    item.ElevationProfile is not null);
                if (wallPlate?.ElevationProfile is null)
                {
                    return false;
                }

                // WP physical bottom defines the WallPlateBottom zero reference.
                referenceLocalZMm = wallPlate.ElevationProfile.BottomLocalZMm;
                return double.IsFinite(referenceLocalZMm);

            default:
                return false;
        }
    }

    private bool TryConvertRowToPlanDistanceFromEavePreservingPhysical(
        AutomaticPurlinRowViewModel row,
        BottomEdgeMemberCapture capture)
    {
        _ = capture;
        if (_previewPlan is null ||
            !TryFindPlanItemForRow(row, out var planItem) ||
            planItem is null ||
            !TryResolvePlanDistanceFromEaveMm(planItem, out var fromEaveMm))
        {
            return false;
        }

        if (!RoofAutomaticPurlinPlanner.TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                _geometry,
                out var maxExclusiveMm) ||
            fromEaveMm < 0d ||
            fromEaveMm >= maxExclusiveMm)
        {
            return false;
        }

        _suppressRowChangedDepth++;
        try
        {
            row.SetPlacementModeSilent(RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave);
            row.SetPlacementValueText(FormatLocalZText(fromEaveMm));
        }
        finally
        {
            _suppressRowChangedDepth--;
        }

        return true;
    }

    private bool TryFindPlanItemForRow(
        AutomaticPurlinRowViewModel row,
        out RoofAutomaticPurlinPlanItem? planItem)
    {
        planItem = null;
        if (_previewPlan is null)
        {
            return false;
        }

        if (row.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
        {
            planItem = _previewPlan.Items.FirstOrDefault(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
            return planItem is not null;
        }

        planItem = _previewPlan.Items.FirstOrDefault(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate &&
            string.Equals(item.LayoutItemId, row.LayoutItemId, StringComparison.Ordinal));
        return planItem is not null;
    }

    private bool TryResolvePlanDistanceFromEaveMm(
        RoofAutomaticPurlinPlanItem item,
        out double planDistanceMm)
    {
        planDistanceMm = 0d;
        if (!double.IsFinite(_geometry.Topology.PitchDegrees) ||
            _geometry.Topology.PitchDegrees <= 0d ||
            _geometry.Topology.PitchDegrees >= 90d)
        {
            return false;
        }

        var tanPitch = Math.Tan(_geometry.Topology.PitchDegrees * Math.PI / 180d);
        if (!double.IsFinite(tanPitch) || tanPitch <= 0d)
        {
            return false;
        }

        // Member-axis convention: PlanDistance drives roof-surface LocalZ = d·tan(pitch).
        var surfaceLocalZMm = item.PhysicalPlacement?.RafterUpperSurfaceLocalZMm;
        if (surfaceLocalZMm is null || !double.IsFinite(surfaceLocalZMm.Value))
        {
            return false;
        }

        planDistanceMm = surfaceLocalZMm.Value / tanPitch;
        return double.IsFinite(planDistanceMm) && planDistanceMm >= 0d;
    }

    private static double ResolveDisplayLocalZMm(RoofRelativeElevationDatum datum) =>
        datum.ReferenceKind switch
        {
            RoofRelativeElevationReferenceKind.SourceEavePlane => 0d,
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane => datum.ReferenceLocalZMm,
            _ => datum.ReferenceLocalZMm,
        };

    private bool TryCreateLayout(
        out RoofAutomaticPurlinLayout? layout,
        out string? errorResourceKey)
    {
        layout = null;
        errorResourceKey = null;
        var items = new List<RoofAutomaticPurlinLayoutItem>(Rows.Count);
        foreach (var row in Rows)
        {
            if (!row.TryCreateItem(out var item, out errorResourceKey) || item is null)
            {
                return false;
            }

            items.Add(item);
        }

        if (!WallPlateRow.TryCreateItem(out var wallPlatePlacement, out errorResourceKey) ||
            wallPlatePlacement is null)
        {
            return false;
        }

        // Under WallPlateBottom, product BottomEdge Place=0 is relative zero. Persist that
        // Place and stash SourceEave-absolute bottom in WallPlateLowerEdgeHeightMm;
        // Planner.Create expands via PrepareWallPlateBottomEdgeZeroForBootstrapPlanning.
        // Do not expand Place into the product layout (that caused HOST Place=596.285).
        var wallPlateLowerEdgeHeightMm =
            wallPlatePlacement.PlacementMode ==
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference
                ? ResolveWallPlateBottomEdgeBootstrapAbsoluteMm(
                    wallPlatePlacement.PlacementValueMm)
                : 0d;

        if (!TryResolveOptionalSectionDimension(
                RidgeWidthText,
                _loadedRidgeWidthMm,
                _ridgeWidthEdited,
                _storedRidgeWidthMm is not null,
                out var ridgeWidthMm) ||
            !TryResolveOptionalSectionDimension(
                RidgeHeightText,
                _loadedRidgeHeightMm,
                _ridgeHeightEdited,
                _storedRidgeHeightMm is not null,
                out var ridgeHeightMm))
        {
            errorResourceKey = "AutomaticPurlin_ValidationDimension";
            return false;
        }

        if (!TryCreateRidgeSeating(out var ridgeSeating, out errorResourceKey) ||
            ridgeSeating is null)
        {
            return false;
        }

        layout = new RoofAutomaticPurlinLayout(RidgeEnabled, items.AsReadOnly())
        {
            WallPlateEnabled = WallPlateEnabled,
            WallPlateLowerEdgeHeightMm = wallPlateLowerEdgeHeightMm,
            WallPlatePlacement = wallPlatePlacement with
            {
                LayoutItemId = RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                Enabled = true,
            },
            RidgeWidthMm = ridgeWidthMm,
            RidgeHeightMm = ridgeHeightMm,
            RidgeSeatingDepth = ridgeSeating,
            ManualRafterWidthMm = _recoverableManualRafterWidthMm,
            ManualRafterHeightMm = _recoverableManualRafterHeightMm,
            RafterSourcePolicy = ResolveDraftRafterSourcePolicyForPersist(),
            AcknowledgedActualKind = _draftAcknowledgedActualKind,
            AcknowledgedActualWidthMm = _draftAcknowledgedActualWidthMm,
            AcknowledgedActualHeightMm = _draftAcknowledgedActualHeightMm,
        };
        return true;
    }

    /// <summary>
    /// Absolute SourceEave LocalZ of the WallPlate bottom for Core bootstrap under
    /// WallPlateBottom. When product Place is already non-zero it is treated as that
    /// absolute (legacy). When Place≈0, use preview / loaded stash.
    /// </summary>
    private double ResolveWallPlateBottomEdgeBootstrapAbsoluteMm(double productPlacementValueMm)
    {
        if (ReferenceKind != RoofRelativeElevationReferenceKind.WallPlateBottom ||
            Math.Abs(productPlacementValueMm) > 1e-9)
        {
            return productPlacementValueMm;
        }

        if (_previewPlan is not null)
        {
            var wallPlate = _previewPlan.Items.FirstOrDefault(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                item.ElevationProfile is not null);
            if (wallPlate?.ElevationProfile is not null &&
                double.IsFinite(wallPlate.ElevationProfile.BottomLocalZMm) &&
                Math.Abs(wallPlate.ElevationProfile.BottomLocalZMm) > 1e-9)
            {
                return wallPlate.ElevationProfile.BottomLocalZMm;
            }
        }

        if (_wallPlateBottomEdgeBootstrapAbsoluteMm is { } stashed &&
            double.IsFinite(stashed) &&
            Math.Abs(stashed) > 1e-9)
        {
            return stashed;
        }

        return 0d;
    }

    private void RefreshWallPlateBottomEdgeBootstrapAbsoluteMmFromPreview()
    {
        if (ReferenceKind != RoofRelativeElevationReferenceKind.WallPlateBottom ||
            !WallPlateEnabled ||
            WallPlateRow.PlacementMode !=
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference ||
            _previewPlan is null)
        {
            return;
        }

        if (!TryParseMillimetres(WallPlateRow.PlacementValueText, out var placeMm) ||
            Math.Abs(placeMm) > 1e-9)
        {
            return;
        }

        var wallPlate = _previewPlan.Items.FirstOrDefault(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate &&
            item.ElevationProfile is not null);
        if (wallPlate?.ElevationProfile is null ||
            !double.IsFinite(wallPlate.ElevationProfile.BottomLocalZMm))
        {
            return;
        }

        _wallPlateBottomEdgeBootstrapAbsoluteMm =
            wallPlate.ElevationProfile.BottomLocalZMm;
    }

    private RoofAutomaticPurlinRafterSourcePolicy ResolveDraftRafterSourcePolicyForPersist()
    {
        if (_rafterSourceConflictUnresolved ||
            _missingActualTransitionUnresolved ||
            _persistedManualRafterInvalid)
        {
            return _draftRafterSourcePolicy;
        }

        return _rafterDimensionSource switch
        {
            // ExplicitManual may be PreferManual (user choice) or PreferActual
            // (silent planning fallback when actual rafters are absent) — never flip silently.
            AutomaticPurlinRafterDimensionSource.ExplicitManual =>
                _draftRafterSourcePolicy is
                    RoofAutomaticPurlinRafterSourcePolicy.PreferManual or
                    RoofAutomaticPurlinRafterSourcePolicy.PreferActual
                    ? _draftRafterSourcePolicy
                    : RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe or
            AutomaticPurlinRafterDimensionSource.SelectedRafter =>
                RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            _ => _draftRafterSourcePolicy,
        };
    }

    private bool TryCreateRidgeSeating(
        out RoofAutomaticPurlinSeatingDepth? seating,
        out string? errorResourceKey)
    {
        seating = null;
        errorResourceKey = null;
        var mode = SelectedRidgeSeatingMode;
        var seatingValue = _ridgeSeating.Value;
        if ((_ridgeSeatingDepthValueEdited &&
             !TryParseMillimetres(RidgeSeatingDepthValueText, out seatingValue)) ||
            !double.IsFinite(seatingValue) ||
            seatingValue < 0d ||
            (mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
             seatingValue > 100d) ||
            (mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm &&
             seatingValue > RafterHeightMm))
        {
            errorResourceKey = "AutomaticPurlin_ValidationSeating";
            return false;
        }

        seating = new RoofAutomaticPurlinSeatingDepth(mode, seatingValue);
        _ridgeSeating = seating;
        return true;
    }

    private bool TryResolveRidgeSeatingDepth(out double? depthMm)
    {
        depthMm = null;
        if (!TryCreateRidgeSeating(out var seating, out _) || seating is null)
        {
            return false;
        }

        if (seating.Mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm)
        {
            depthMm = seating.Value;
            return true;
        }

        depthMm = RafterHeightMm * seating.Value / 100d;
        return true;
    }

    private bool TryResolveOptionalSectionDimension(
        string text,
        double loadedValueMm,
        bool edited,
        bool hadStoredValue,
        out double? valueMm)
    {
        valueMm = null;
        if (!edited && !hadStoredValue)
        {
            return true;
        }

        var value = loadedValueMm;
        if (edited && !TryParseMillimetres(text, out value))
        {
            return false;
        }

        if (!double.IsFinite(value) || value <= 0d)
        {
            return false;
        }

        valueMm = value;
        return true;
    }

    private List<AutomaticPurlinRidgeOption> ResolveRidgeReferences()
    {
        if (_boundaryProvenance is null || !_boundaryProvenance.IsValid)
        {
            return [];
        }

        var resolved = RoofStructuralEdgeIdentityResolver.Resolve(
            _geometry,
            _boundaryProvenance);
        if (!resolved.IsValid)
        {
            return [];
        }

        var ridges = resolved.Edges
            .Where(edge => edge.StructuralRole == RoofStructuralRole.Ridge)
            .ToArray();
        _hasInclinedRidge = ridges.Any(edge =>
            Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) >
            RoofAutomaticPurlinPlanner.CoordinateToleranceMm);
        return ridges
            .Where(edge =>
                Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) <=
                RoofAutomaticPurlinPlanner.CoordinateToleranceMm)
            .Select((edge, index) => new AutomaticPurlinRidgeOption(
                edge.StructuralIdentity,
                string.Format(_culture, Text("AutomaticPurlin_RidgeNumberFormat"), index + 1)))
            .ToList();
    }

    private bool TryParseMillimetres(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, _culture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value);

    private string MapPlanError(RoofAutomaticPurlinPlanError error) => error switch
    {
        RoofAutomaticPurlinPlanError.ReferenceRidgeRequired or
        RoofAutomaticPurlinPlanError.ReferenceRidgeNotFound or
        RoofAutomaticPurlinPlanError.InvalidReferenceRidge =>
            "AutomaticPurlin_ValidationRidgeReference",
        RoofAutomaticPurlinPlanError.InclinedReferenceRidge =>
            "AutomaticPurlin_ValidationInclinedRidge",
        RoofAutomaticPurlinPlanError.InvalidSeatingDepth =>
            "AutomaticPurlin_ValidationSeating",
        RoofAutomaticPurlinPlanError.ElevationOutsideRoof =>
            "AutomaticPurlin_ValidationOutsideRoof",
        RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum =>
            "AutomaticPurlin_ValidationWallPlateMinPlanDistance",
        RoofAutomaticPurlinPlanError.CriticalEventElevation =>
            "AutomaticPurlin_ValidationCriticalElevation",
        RoofAutomaticPurlinPlanError.InvalidBoundaryProvenance or
        RoofAutomaticPurlinPlanError.BoundaryProvenanceCountMismatch or
        RoofAutomaticPurlinPlanError.UnresolvedFaceBoundaryIdentity =>
            "AutomaticPurlin_ValidationBoundaryIdentity",
        _ => "AutomaticPurlin_ValidationLayout",
    };

    private void SetValidation(string? resourceKey, string diagnosticReason)
    {
        _previewDiagnosticReason = diagnosticReason;
        _validationMessage = resourceKey is null ? string.Empty : Text(resourceKey);
        OnPropertyChanged(nameof(ValidationMessage));
    }


    private void RefreshAllFieldValidation()
    {
        RefreshReferenceFieldValidation();
        WallPlateRow.RefreshFieldValidation();
        foreach (var row in Rows)
        {
            row.RefreshFieldValidation();
        }

        RefreshRidgeFieldValidation();
        OnPropertyChanged(nameof(RelativeReferenceHasError));
        OnPropertyChanged(nameof(RelativeReferenceErrorText));
        OnPropertyChanged(nameof(ReferenceLocalZHasError));
        OnPropertyChanged(nameof(ReferenceLocalZErrorText));
        OnPropertyChanged(nameof(ReferencePlaneHasFieldError));
        OnPropertyChanged(nameof(RidgeWidthHasError));
        OnPropertyChanged(nameof(RidgeWidthErrorText));
        OnPropertyChanged(nameof(RidgeHeightHasError));
        OnPropertyChanged(nameof(RidgeHeightErrorText));
        OnPropertyChanged(nameof(RidgeSeatingHasError));
        OnPropertyChanged(nameof(RidgeSeatingErrorText));
        OnPropertyChanged(nameof(RidgeHasFieldError));
        NotifyTabErrorPresentation();
    }

    private void NotifyTabErrorPresentation()
    {
        OnPropertyChanged(nameof(WallPlateTabHasError));
        OnPropertyChanged(nameof(IntermediateTabHasError));
        OnPropertyChanged(nameof(RidgeTabHasError));
        OnPropertyChanged(nameof(WallPlateTabAutomationName));
        OnPropertyChanged(nameof(IntermediateTabAutomationName));
        OnPropertyChanged(nameof(RidgeTabAutomationName));
        RefreshEditorTabPresentation();
    }

    private string FormatTabAutomationNameFromTitle(string title, bool hasError) =>
        hasError
            ? string.Format(_culture, Text("AutomaticPurlin_TabErrorFormat"), title)
            : title;

    private void RefreshReferenceFieldValidation()
    {
        _relativeReferenceHasError = false;
        _relativeReferenceErrorText = string.Empty;
        _referenceLocalZHasError = false;
        _referenceLocalZErrorText = string.Empty;

        var relativeMm = _loadedRelativeReferenceMm;
        if (_relativeReferenceEdited &&
            !RoofRelativeElevationDatumRules.TryParseMetres(
                RelativeReferenceText,
                _culture,
                out relativeMm))
        {
            _relativeReferenceHasError = true;
            _relativeReferenceErrorText = Text("AutomaticPurlin_ValidationDatum");
        }

        if (ReferenceKind == RoofRelativeElevationReferenceKind.WallPlateBottom &&
            WallPlateEnabled)
        {
            return;
        }

        if (ReferenceKind == RoofRelativeElevationReferenceKind.SourceEavePlane)
        {
            return;
        }

        // ExplicitLocalPlane, or WallPlateBottom with wall plates disabled (passthrough LocalZ).
        if (ReferenceKind != RoofRelativeElevationReferenceKind.ExplicitLocalPlane &&
            !(ReferenceKind == RoofRelativeElevationReferenceKind.WallPlateBottom &&
              !WallPlateEnabled))
        {
            return;
        }

        if (!TryParseMillimetres(ReferenceLocalZText, out var localZMm))
        {
            _referenceLocalZHasError = true;
            _referenceLocalZErrorText = Text("AutomaticPurlin_ValidationDatum");
            return;
        }

        if (_relativeReferenceHasError)
        {
            return;
        }

        var validation = RoofRelativeElevationDatumRules.Validate(
            RoofRelativeElevationDatumSchema.CurrentVersion,
            ReferenceKind,
            relativeMm,
            localZMm);
        if (!validation.IsValid)
        {
            _relativeReferenceHasError = true;
            _relativeReferenceErrorText = Text("AutomaticPurlin_ValidationDatum");
            _referenceLocalZHasError = true;
            _referenceLocalZErrorText = Text("AutomaticPurlin_ValidationDatum");
        }
    }

    private void RefreshRidgeFieldValidation()
    {
        _ridgeWidthHasError = false;
        _ridgeWidthErrorText = string.Empty;
        _ridgeHeightHasError = false;
        _ridgeHeightErrorText = string.Empty;
        _ridgeSeatingHasError = false;
        _ridgeSeatingErrorText = string.Empty;

        if (!TryResolveOptionalSectionDimension(
                RidgeWidthText,
                _loadedRidgeWidthMm,
                _ridgeWidthEdited,
                _storedRidgeWidthMm is not null,
                out _))
        {
            _ridgeWidthHasError = true;
            _ridgeWidthErrorText = Text("AutomaticPurlin_ValidationDimension");
        }

        if (!TryResolveOptionalSectionDimension(
                RidgeHeightText,
                _loadedRidgeHeightMm,
                _ridgeHeightEdited,
                _storedRidgeHeightMm is not null,
                out _))
        {
            _ridgeHeightHasError = true;
            _ridgeHeightErrorText = Text("AutomaticPurlin_ValidationDimension");
        }

        var mode = SelectedRidgeSeatingMode;
        var seatingValue = _ridgeSeating.Value;
        if ((_ridgeSeatingDepthValueEdited &&
             !TryParseMillimetres(RidgeSeatingDepthValueText, out seatingValue)) ||
            !double.IsFinite(seatingValue) ||
            seatingValue < 0d ||
            (mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
             seatingValue > 100d) ||
            (mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm &&
             seatingValue > RafterHeightMm))
        {
            _ridgeSeatingHasError = true;
            _ridgeSeatingErrorText = Text("AutomaticPurlin_ValidationSeating");
        }
    }

    private void UpdateSelectedRows()
    {
        foreach (var row in Rows)
        {
            row.SetSchematicSelected(ReferenceEquals(row, SelectedRow));
        }
    }

    private void NotifySchematicState()
    {
        OnPropertyChanged(nameof(SchematicRidgeActive));
        OnPropertyChanged(nameof(SchematicRidgeEmphasized));
        OnPropertyChanged(nameof(SchematicIntermediateActive));
        OnPropertyChanged(nameof(SchematicAnyIntermediateEnabled));
        OnPropertyChanged(nameof(SchematicIntermediateEmphasized));
        OnPropertyChanged(nameof(SchematicPlacementMode));
        OnPropertyChanged(nameof(ShowEaveDistanceCue));
        OnPropertyChanged(nameof(ShowRidgeDistanceCue));
        OnPropertyChanged(nameof(ShowVerticalHeightCue));
        OnPropertyChanged(nameof(ShowSeatingCue));
        OnPropertyChanged(nameof(SchematicRidgeReferenceUnresolved));
        OnPropertyChanged(nameof(SchematicCaption));
    }

    private string Text(string key) => UiStrings.GetString(key, _culture);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class AutomaticPurlinRowViewModel : INotifyPropertyChanged
{
    private readonly CultureInfo _culture;
    private readonly Func<string, string> _text;
    private double _rafterHeightMm;
    private readonly bool _allowZeroPlacementValue;
    private RoofAutomaticPurlinSeatingDepth? _distanceSeating;
    private readonly double _loadedPlacementValueMm;
    private readonly double? _storedWidthMm;
    private readonly double? _storedHeightMm;
    private readonly double _loadedWidthMm;
    private readonly double _loadedHeightMm;
    private bool _placementValueEdited;
    private bool _seatingDepthValueEdited;
    private bool _widthEdited;
    private bool _heightEdited;
    private bool _preserveImplicitSingleRidgeReference;
    private bool _enabled;
    private bool _isSchematicSelected;
    private int _displayIndex;
    private RoofAutomaticPurlinPlacementMode _placementMode;
    private string _placementValueText;
    private string _seatingDepthValueText;
    private string _widthText;
    private string _heightText;
    private AutomaticPurlinRidgeOption? _selectedRidgeReference;
    private string _planPosition = "—";
    private string _roofPlaneRelative = "—";
    private string _seatingDepthDerived = "—";
    private string _bottomRelative = "—";
    private string _centerRelative = "—";
    private string _topRelative = "—";
    private bool _placementValueHasError;
    private string _placementValueErrorText = string.Empty;
    private bool _ridgeReferenceHasError;
    private string _ridgeReferenceErrorText = string.Empty;
    private bool _widthHasError;
    private string _widthErrorText = string.Empty;
    private bool _heightHasError;
    private string _heightErrorText = string.Empty;
    private bool _seatingHasError;
    private string _seatingErrorText = string.Empty;

    internal AutomaticPurlinRowViewModel(
        RoofAutomaticPurlinLayoutItem item,
        IReadOnlyList<AutomaticPurlinPlacementModeOption> placementModes,
        IReadOnlyList<AutomaticPurlinSeatingModeOption> seatingModes,
        IReadOnlyList<AutomaticPurlinRidgeOption> ridgeReferences,
        double rafterHeightMm,
        CultureInfo culture,
        Func<string, string> text,
        bool allowZeroPlacementValue = false,
        double defaultWidthMm = 160d,
        double defaultHeightMm = 220d,
        RoofAutomaticPurlinGeneratorRole generatorRole = RoofAutomaticPurlinGeneratorRole.Intermediate)
    {
        ArgumentNullException.ThrowIfNull(item);
        LayoutItemId = item.LayoutItemId;
        GeneratorRole = generatorRole;
        _enabled = item.Enabled;
        _placementMode = item.PlacementMode;
        _loadedPlacementValueMm = item.PlacementValueMm;
        _placementValueText = item.PlacementValueMm.ToString("0.###", culture);
        _storedWidthMm = item.WidthMm;
        _storedHeightMm = item.HeightMm;
        _loadedWidthMm = item.WidthMm ?? defaultWidthMm;
        _loadedHeightMm = item.HeightMm ?? defaultHeightMm;
        _widthText = _loadedWidthMm.ToString("0.###", culture);
        _heightText = _loadedHeightMm.ToString("0.###", culture);
        _widthEdited = false;
        _heightEdited = false;
        _culture = culture;
        _text = text;
        _rafterHeightMm = rafterHeightMm;
        _allowZeroPlacementValue = allowZeroPlacementValue;
        PlacementModes = placementModes;
        SeatingModes = seatingModes;
        RidgeReferences = ridgeReferences;
        _distanceSeating = item.SeatingDepth;
        _seatingDepthValueText = (item.SeatingDepth?.Value ??
            AutomaticPurlinDialogViewModel.DefaultSeatingPercent).ToString("0.###", culture);
        _selectedRidgeReference = ridgeReferences.FirstOrDefault(option =>
            option.Key == item.ReferenceRidgeKey);
        _preserveImplicitSingleRidgeReference =
            item.PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge &&
            item.ReferenceRidgeKey is null &&
            ridgeReferences.Count == 1;
        NormalizeModeState();
        RefreshElevationTooltips();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? Changed;
    internal event EventHandler? PlacementValueUserEdited;
    internal event Action<
        AutomaticPurlinRowViewModel,
        RoofAutomaticPurlinPlacementMode,
        RoofAutomaticPurlinPlacementMode>? PlacementModeChanging;

    public string LayoutItemId { get; }
    public RoofAutomaticPurlinGeneratorRole GeneratorRole { get; }
    public string DisplayName => string.Format(
        _culture,
        _text("AutomaticPurlin_IntermediateRowFormat"),
        _displayIndex);
    public IReadOnlyList<AutomaticPurlinPlacementModeOption> PlacementModes { get; }
    public IReadOnlyList<AutomaticPurlinSeatingModeOption> SeatingModes { get; }
    public IReadOnlyList<AutomaticPurlinRidgeOption> RidgeReferences { get; }
    public string PlanPosition => _planPosition;
    public string RoofPlaneRelative => _roofPlaneRelative;
    public string SeatingDepthDerived => _seatingDepthDerived;
    public string BottomRelative => _bottomRelative;
    public string CenterRelative => _centerRelative;
    public string TopRelative => _topRelative;
    public AutomaticPurlinElevationTooltipViewModel RoofPlaneTooltip { get; private set; } =
        AutomaticPurlinElevationTooltipViewModel.Empty;
    public AutomaticPurlinElevationTooltipViewModel BottomTooltip { get; private set; } =
        AutomaticPurlinElevationTooltipViewModel.Empty;
    public AutomaticPurlinElevationTooltipViewModel CenterTooltip { get; private set; } =
        AutomaticPurlinElevationTooltipViewModel.Empty;
    public AutomaticPurlinElevationTooltipViewModel TopTooltip { get; private set; } =
        AutomaticPurlinElevationTooltipViewModel.Empty;
    public bool PlacementValueHasError => _placementValueHasError;
    public string PlacementValueErrorText => _placementValueErrorText;
    public bool RidgeReferenceHasError => _ridgeReferenceHasError;
    public string RidgeReferenceErrorText => _ridgeReferenceErrorText;
    public bool WidthHasError => _widthHasError;
    public string WidthErrorText => _widthErrorText;
    public bool HeightHasError => _heightHasError;
    public string HeightErrorText => _heightErrorText;
    public bool SeatingHasError => _seatingHasError;
    public string SeatingErrorText => _seatingErrorText;
    public bool HasAnyFieldError =>
        PlacementValueHasError ||
        RidgeReferenceHasError ||
        WidthHasError ||
        HeightHasError ||
        SeatingHasError;
    public bool IsSchematicSelected => _isSchematicSelected;
    public string ValueLabel => _text(PlacementMode switch
    {
        RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference =>
            "AutomaticPurlin_ValueBottomHeight",
        RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave =>
            "AutomaticPurlin_ValueEaveDistance",
        _ => "AutomaticPurlin_ValueRidgeDistance",
    });
    public bool IsRidgeReferenceVisible =>
        PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge &&
        RidgeReferences.Count > 1;
    public bool IsSeatingApplicable => true;
    public string SeatingPolicyText => !IsSeatingApplicable || _distanceSeating is null
        ? _text("AutomaticPurlin_SeatingNotApplicable")
        : _distanceSeating.Mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight
            ? string.Format(_culture, "{0:0.###} %", _distanceSeating.Value)
            : string.Format(
                _culture,
                "{0:0.###} {1}",
                _distanceSeating.Value,
                _text("AutomaticPurlin_UnitMillimetres"));
    public string SeatingStatusText =>
        $"{_text("AutomaticPurlin_SeatingDepth")}: {SeatingPolicyText}";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            ChangedProperty();
        }
    }

    public RoofAutomaticPurlinPlacementMode PlacementMode
    {
        get => _placementMode;
        set
        {
            if (_placementMode == value)
            {
                return;
            }

            var previous = _placementMode;
            _placementMode = value;
            PlacementModeChanging?.Invoke(this, previous, value);
            _preserveImplicitSingleRidgeReference = false;
            NormalizeModeState();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueLabel));
            OnPropertyChanged(nameof(IsRidgeReferenceVisible));
            OnPropertyChanged(nameof(IsSeatingApplicable));
            OnPropertyChanged(nameof(SelectedRidgeReference));
            OnPropertyChanged(nameof(SelectedSeatingMode));
            OnPropertyChanged(nameof(SeatingDepthValueText));
            OnPropertyChanged(nameof(SeatingUnit));
            OnPropertyChanged(nameof(SeatingPolicyText));
            OnPropertyChanged(nameof(SeatingStatusText));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string PlacementValueText
    {
        get => _placementValueText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_placementValueText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _placementValueText = normalized;
            _placementValueEdited = true;
            PlacementValueUserEdited?.Invoke(this, EventArgs.Empty);
            ChangedProperty();
        }
    }

    public string WidthText
    {
        get => _widthText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_widthText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _widthText = normalized;
            _widthEdited = true;
            ChangedProperty();
        }
    }

    public string HeightText
    {
        get => _heightText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_heightText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _heightText = normalized;
            _heightEdited = true;
            ChangedProperty();
        }
    }

    public AutomaticPurlinRidgeOption? SelectedRidgeReference
    {
        get => _selectedRidgeReference;
        set
        {
            if (Equals(_selectedRidgeReference, value))
            {
                return;
            }

            _selectedRidgeReference = value;
            _preserveImplicitSingleRidgeReference = false;
            ChangedProperty();
        }
    }

    public RoofAutomaticPurlinSeatingDepthMode SelectedSeatingMode
    {
        get => _distanceSeating?.Mode is
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight or
            RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm
                ? _distanceSeating.Mode
                : RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        set
        {
            if (value is not RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight and
                not RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm)
            {
                return;
            }

            var currentValue = _distanceSeating?.Value ??
                AutomaticPurlinDialogViewModel.DefaultSeatingPercent;
            if (_distanceSeating?.Mode == value)
            {
                return;
            }

            _distanceSeating = new RoofAutomaticPurlinSeatingDepth(value, currentValue);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SeatingUnit));
            OnPropertyChanged(nameof(SeatingPolicyText));
            OnPropertyChanged(nameof(SeatingStatusText));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SeatingDepthValueText
    {
        get => _seatingDepthValueText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_seatingDepthValueText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _seatingDepthValueText = normalized;
            _seatingDepthValueEdited = true;
            if (TryParseValue(normalized, out var parsedValue))
            {
                _distanceSeating = new RoofAutomaticPurlinSeatingDepth(
                    SelectedSeatingMode,
                    parsedValue);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SeatingPolicyText));
            OnPropertyChanged(nameof(SeatingStatusText));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SeatingUnit => SelectedSeatingMode ==
        RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight
            ? "%"
            : _text("AutomaticPurlin_UnitMillimetres");

    internal void SetDisplayIndex(int displayIndex)
    {
        if (_displayIndex == displayIndex)
        {
            return;
        }

        _displayIndex = displayIndex;
        OnPropertyChanged(nameof(DisplayName));
    }

    internal void SetRafterHeightMm(double rafterHeightMm)
    {
        if (!double.IsFinite(rafterHeightMm) || rafterHeightMm <= 0d)
        {
            return;
        }

        _rafterHeightMm = rafterHeightMm;
        OnPropertyChanged(nameof(SeatingDepthDerived));
        OnPropertyChanged(nameof(SeatingStatusText));
    }

    internal void SetSchematicSelected(bool selected)
    {
        if (_isSchematicSelected == selected)
        {
            return;
        }

        _isSchematicSelected = selected;
        OnPropertyChanged(nameof(IsSchematicSelected));
    }


    internal void RefreshFieldValidation()
    {
        _placementValueHasError = false;
        _placementValueErrorText = string.Empty;
        _ridgeReferenceHasError = false;
        _ridgeReferenceErrorText = string.Empty;
        _widthHasError = false;
        _widthErrorText = string.Empty;
        _heightHasError = false;
        _heightErrorText = string.Empty;
        _seatingHasError = false;
        _seatingErrorText = string.Empty;

        var value = _loadedPlacementValueMm;
        if ((_placementValueEdited && !TryParseValue(PlacementValueText, out value)) ||
            !IsPlacementValueAllowed(value))
        {
            _placementValueHasError = true;
            _placementValueErrorText = _text("AutomaticPurlin_ValidationValue");
        }

        if (PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge)
        {
            var ridgeKey = _preserveImplicitSingleRidgeReference
                ? null
                : SelectedRidgeReference?.Key;
            if (RidgeReferences.Count != 1 && ridgeKey is null)
            {
                _ridgeReferenceHasError = true;
                _ridgeReferenceErrorText = _text("AutomaticPurlin_ValidationRidgeReference");
            }
        }

        if (IsSeatingApplicable)
        {
            var mode = SelectedSeatingMode;
            var seatingValue = _distanceSeating?.Value ??
                AutomaticPurlinDialogViewModel.DefaultSeatingPercent;
            if ((_seatingDepthValueEdited &&
                 !TryParseValue(SeatingDepthValueText, out seatingValue)) ||
                !double.IsFinite(seatingValue) ||
                seatingValue < 0d ||
                (mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
                 seatingValue > 100d) ||
                (mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm &&
                 seatingValue > _rafterHeightMm))
            {
                _seatingHasError = true;
                _seatingErrorText = _text("AutomaticPurlin_ValidationSeating");
            }
        }

        if (_widthEdited || _storedWidthMm is not null)
        {
            var resolvedWidth = _loadedWidthMm;
            if ((_widthEdited && !TryParseValue(WidthText, out resolvedWidth)) ||
                !double.IsFinite(resolvedWidth) ||
                resolvedWidth <= 0d)
            {
                _widthHasError = true;
                _widthErrorText = _text("AutomaticPurlin_ValidationDimension");
            }
        }

        if (_heightEdited || _storedHeightMm is not null)
        {
            var resolvedHeight = _loadedHeightMm;
            if ((_heightEdited && !TryParseValue(HeightText, out resolvedHeight)) ||
                !double.IsFinite(resolvedHeight) ||
                resolvedHeight <= 0d)
            {
                _heightHasError = true;
                _heightErrorText = _text("AutomaticPurlin_ValidationDimension");
            }
        }

        OnPropertyChanged(nameof(PlacementValueHasError));
        OnPropertyChanged(nameof(PlacementValueErrorText));
        OnPropertyChanged(nameof(RidgeReferenceHasError));
        OnPropertyChanged(nameof(RidgeReferenceErrorText));
        OnPropertyChanged(nameof(WidthHasError));
        OnPropertyChanged(nameof(WidthErrorText));
        OnPropertyChanged(nameof(HeightHasError));
        OnPropertyChanged(nameof(HeightErrorText));
        OnPropertyChanged(nameof(SeatingHasError));
        OnPropertyChanged(nameof(SeatingErrorText));
        OnPropertyChanged(nameof(HasAnyFieldError));
    }

    internal void SetPlacementValueError(string message)
    {
        _placementValueHasError = true;
        _placementValueErrorText = message ?? string.Empty;
        OnPropertyChanged(nameof(PlacementValueHasError));
        OnPropertyChanged(nameof(PlacementValueErrorText));
        OnPropertyChanged(nameof(HasAnyFieldError));
    }

    /// <summary>
    /// Updates the placement value without raising <see cref="Changed"/> (parent suppresses
    /// or batches a single Recalculate after datum/mode conversion).
    /// </summary>
    internal void SetPlacementValueText(string text)
    {
        var normalized = text ?? string.Empty;
        if (string.Equals(_placementValueText, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _placementValueText = normalized;
        _placementValueEdited = true;
        OnPropertyChanged(nameof(PlacementValueText));
    }

    /// <summary>
    /// Switches placement mode without conversion callbacks or <see cref="Changed"/>.
    /// </summary>
    internal void SetPlacementModeSilent(RoofAutomaticPurlinPlacementMode mode)
    {
        if (_placementMode == mode)
        {
            return;
        }

        _placementMode = mode;
        _preserveImplicitSingleRidgeReference = false;
        NormalizeModeState();
        OnPropertyChanged(nameof(PlacementMode));
        OnPropertyChanged(nameof(ValueLabel));
        OnPropertyChanged(nameof(IsRidgeReferenceVisible));
        OnPropertyChanged(nameof(IsSeatingApplicable));
        OnPropertyChanged(nameof(SelectedRidgeReference));
        OnPropertyChanged(nameof(SelectedSeatingMode));
        OnPropertyChanged(nameof(SeatingDepthValueText));
        OnPropertyChanged(nameof(SeatingUnit));
        OnPropertyChanged(nameof(SeatingPolicyText));
        OnPropertyChanged(nameof(SeatingStatusText));
    }

    internal bool TryCreateItem(
        out RoofAutomaticPurlinLayoutItem? item,
        out string? errorResourceKey)
    {
        item = null;
        errorResourceKey = null;
        var value = _loadedPlacementValueMm;
        if ((_placementValueEdited && !TryParseValue(PlacementValueText, out value)) ||
            !IsPlacementValueAllowed(value))
        {
            errorResourceKey = "AutomaticPurlin_ValidationValue";
            return false;
        }

        var ridgeKey = PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge
            ? _preserveImplicitSingleRidgeReference
                ? null
                : SelectedRidgeReference?.Key
            : null;
        if (PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge &&
            RidgeReferences.Count != 1 && ridgeKey is null)
        {
            errorResourceKey = "AutomaticPurlin_ValidationRidgeReference";
            return false;
        }

        RoofAutomaticPurlinSeatingDepth? seating = null;
        if (IsSeatingApplicable)
        {
            var mode = SelectedSeatingMode;
            var seatingValue = _distanceSeating?.Value ??
                AutomaticPurlinDialogViewModel.DefaultSeatingPercent;
            if ((_seatingDepthValueEdited &&
                 !TryParseValue(SeatingDepthValueText, out seatingValue)) ||
                !double.IsFinite(seatingValue) ||
                seatingValue < 0d ||
                (mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
                 seatingValue > 100d) ||
                (mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm &&
                 seatingValue > _rafterHeightMm))
            {
                errorResourceKey = "AutomaticPurlin_ValidationSeating";
                return false;
            }

            seating = new RoofAutomaticPurlinSeatingDepth(mode, seatingValue);
            _distanceSeating = seating;
        }

        double? widthMm = null;
        var resolvedWidth = _loadedWidthMm;
        if (GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
        {
            // WallPlate: always flow live WidthText so min plan-distance revalidates on
            // width edits even when the row was seeded without a stored WidthMm.
            if (TryParseValue(WidthText, out resolvedWidth) &&
                double.IsFinite(resolvedWidth) &&
                resolvedWidth > 0d)
            {
                widthMm = resolvedWidth;
            }
            else if (_widthEdited || _storedWidthMm is not null)
            {
                errorResourceKey = "AutomaticPurlin_ValidationDimension";
                return false;
            }
        }
        else if (_widthEdited || _storedWidthMm is not null)
        {
            if ((_widthEdited && !TryParseValue(WidthText, out resolvedWidth)) ||
                !double.IsFinite(resolvedWidth) ||
                resolvedWidth <= 0d)
            {
                errorResourceKey = "AutomaticPurlin_ValidationDimension";
                return false;
            }

            widthMm = resolvedWidth;
        }

        double? heightMm = null;
        var resolvedHeight = _loadedHeightMm;
        if (GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
        {
            if (TryParseValue(HeightText, out resolvedHeight) &&
                double.IsFinite(resolvedHeight) &&
                resolvedHeight > 0d)
            {
                heightMm = resolvedHeight;
            }
            else if (_heightEdited || _storedHeightMm is not null)
            {
                errorResourceKey = "AutomaticPurlin_ValidationDimension";
                return false;
            }
        }
        else if (_heightEdited || _storedHeightMm is not null)
        {
            if ((_heightEdited && !TryParseValue(HeightText, out resolvedHeight)) ||
                !double.IsFinite(resolvedHeight) ||
                resolvedHeight <= 0d)
            {
                errorResourceKey = "AutomaticPurlin_ValidationDimension";
                return false;
            }

            heightMm = resolvedHeight;
        }

        item = new RoofAutomaticPurlinLayoutItem(
            LayoutItemId,
            Enabled,
            PlacementMode,
            value,
            ridgeKey,
            seating,
            widthMm,
            heightMm);
        return true;
    }

    internal void UpdateDerived(RoofAutomaticPurlinPlanItem? planItem)
    {
        var profile = planItem?.ElevationProfile;
        var physical = planItem?.PhysicalPlacement;
        var planPositionMm = TryParseValue(PlacementValueText, out var parsedPlanPositionMm)
            ? parsedPlanPositionMm
            : _loadedPlacementValueMm;
        _planPosition = physical is null || !IsSeatingApplicable
            ? "—"
            : string.Format(
                _culture,
                "{0:0.###} {1}",
                planPositionMm,
                _text("AutomaticPurlin_UnitMillimetres"));
        _roofPlaneRelative = "—";
        _seatingDepthDerived = physical is null
            ? "—"
            : string.Format(
                _culture,
                "{0:0.###} {1}",
                physical.SeatingDepthMm,
                _text("AutomaticPurlin_UnitMillimetres"));
        _bottomRelative = profile is null
            ? "—"
            : RoofRelativeElevationDatumRules.FormatMetres(
                profile.BottomRelativeElevationMm,
                _culture);
        _centerRelative = profile is null
            ? "—"
            : RoofRelativeElevationDatumRules.FormatMetres(
                profile.CenterRelativeElevationMm,
                _culture);
        _topRelative = profile is null
            ? "—"
            : RoofRelativeElevationDatumRules.FormatMetres(
                profile.TopRelativeElevationMm,
                _culture);
        RefreshElevationTooltips();
        OnPropertyChanged(nameof(PlanPosition));
        OnPropertyChanged(nameof(RoofPlaneRelative));
        OnPropertyChanged(nameof(SeatingDepthDerived));
        OnPropertyChanged(nameof(BottomRelative));
        OnPropertyChanged(nameof(CenterRelative));
        OnPropertyChanged(nameof(TopRelative));
        OnPropertyChanged(nameof(RoofPlaneTooltip));
        OnPropertyChanged(nameof(BottomTooltip));
        OnPropertyChanged(nameof(CenterTooltip));
        OnPropertyChanged(nameof(TopTooltip));
    }

    internal void SetRoofPlaneRelative(double? roofPlaneRelativeMm)
    {
        _roofPlaneRelative = roofPlaneRelativeMm is { } value && double.IsFinite(value)
            ? RoofRelativeElevationDatumRules.FormatMetres(value, _culture)
            : "—";
        RefreshElevationTooltips();
        OnPropertyChanged(nameof(RoofPlaneRelative));
        OnPropertyChanged(nameof(RoofPlaneTooltip));
    }

    private void RefreshElevationTooltips()
    {
        RoofPlaneTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            GeneratorRole,
            AutomaticPurlinElevationMetric.RoofPlane,
            _roofPlaneRelative,
            _text);
        BottomTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            GeneratorRole,
            AutomaticPurlinElevationMetric.Bottom,
            _bottomRelative,
            _text);
        CenterTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            GeneratorRole,
            AutomaticPurlinElevationMetric.Center,
            _centerRelative,
            _text);
        TopTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            GeneratorRole,
            AutomaticPurlinElevationMetric.Top,
            _topRelative,
            _text);
    }

    private void NormalizeModeState()
    {
        if (PlacementMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
        {
            _selectedRidgeReference = null;
        }
        else if (PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave)
        {
            _selectedRidgeReference = null;
        }
        else if (RidgeReferences.Count == 1)
        {
            _selectedRidgeReference = RidgeReferences[0];
        }
        else if (_selectedRidgeReference is not null &&
                 !RidgeReferences.Contains(_selectedRidgeReference))
        {
            _selectedRidgeReference = null;
        }

        if (_distanceSeating is null ||
            _distanceSeating.Mode == RoofAutomaticPurlinSeatingDepthMode.None)
        {
            _distanceSeating = new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                AutomaticPurlinDialogViewModel.DefaultSeatingPercent);
            _seatingDepthValueText = AutomaticPurlinDialogViewModel.DefaultSeatingPercent
                .ToString("0.###", _culture);
            _seatingDepthValueEdited = false;
        }
    }

    private bool TryParseValue(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, _culture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value);

    /// <summary>
    /// BottomEdge accepts any finite signed offset; plan-distance stays strictly positive.
    /// Physical roof bounds are owned by the planner.
    /// </summary>
    private bool IsPlacementValueAllowed(double value) =>
        RoofPurlinLayoutPersistenceRules.IsValidPlacementValueMm(PlacementMode, value);

    private void ChangedProperty([CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanged(propertyName);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed record AutomaticPurlinPlacementModeOption(
    RoofAutomaticPurlinPlacementMode Mode,
    string Label);

internal sealed record AutomaticPurlinSeatingModeOption(
    RoofAutomaticPurlinSeatingDepthMode Mode,
    string Label);

internal sealed record AutomaticPurlinReferenceKindOption(
    RoofRelativeElevationReferenceKind Kind,
    string Label);

internal sealed record AutomaticPurlinRidgeOption(
    RoofStructuralLogicalKey Key,
    string Label);

internal enum AutomaticPurlinDialogMode
{
    ReadOnlyPreview,
    ProductionEdit,
}
