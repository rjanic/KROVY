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
    private readonly HipRoofGeometry _geometry;
    private readonly RoofBoundaryIdentityProvenanceResult? _boundaryProvenance;
    private readonly CultureInfo _culture;
    private readonly AutomaticPurlinDialogMode _mode;
    private readonly int _existingAutomaticPurlinCount;
    private readonly List<AutomaticPurlinRidgeOption> _ridgeReferences;
    private bool _ridgeEnabled;
    private RoofRelativeElevationReferenceKind _referenceKind;
    private string _relativeReferenceText;
    private string _referenceLocalZText;
    private string _validationMessage = string.Empty;
    private string _previewDiagnosticReason = "None";
    private RoofAutomaticPurlinPlan? _previewPlan;
    private bool _isRecalculating;
    private bool _hasInclinedRidge;
    private readonly double _loadedRelativeReferenceMm;
    private readonly double _loadedReferenceLocalZMm;
    private bool _relativeReferenceEdited;
    private bool _referenceLocalZEdited;
    private AutomaticPurlinRowViewModel? _selectedRow;
    private bool _isApplyInProgress;
    private bool _ridgeInteractionActive;
    private bool _intermediateInteractionActive;

    internal AutomaticPurlinDialogViewModel(
        HipRoofGeometry geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayout? layout,
        bool layoutExists,
        RoofRelativeElevationDatum? datum,
        bool datumExists,
        TimberElementData purlinDefaults,
        TimberElementData rafterDefaults,
        CultureInfo? culture = null,
        AutomaticPurlinDialogMode mode = AutomaticPurlinDialogMode.ReadOnlyPreview,
        int existingAutomaticPurlinCount = 0)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _boundaryProvenance = boundaryProvenance;
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _mode = mode;
        _existingAutomaticPurlinCount = Math.Max(0, existingAutomaticPurlinCount);
        ArgumentNullException.ThrowIfNull(purlinDefaults);
        ArgumentNullException.ThrowIfNull(rafterDefaults);

        LayoutExists = layoutExists;
        DatumExists = datumExists;
        PurlinWidthMm = purlinDefaults.WidthMm;
        PurlinHeightMm = purlinDefaults.HeightMm;
        RafterHeightMm = rafterDefaults.HeightMm;

        var effectiveLayout = layout ?? RoofAutomaticPurlinLayout.Empty;
        _ridgeEnabled = effectiveLayout.RidgeEnabled;
        var effectiveDatum = datum ?? new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            0d);
        _referenceKind = effectiveDatum.ReferenceKind;
        _loadedRelativeReferenceMm = effectiveDatum.ReferenceRelativeElevationMm;
        _loadedReferenceLocalZMm = effectiveDatum.ReferenceLocalZMm;
        _relativeReferenceText = RoofRelativeElevationDatumRules.FormatMetres(
            effectiveDatum.ReferenceRelativeElevationMm);
        _referenceLocalZText = effectiveDatum.ReferenceLocalZMm.ToString("0.###", _culture);

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
        foreach (var item in effectiveLayout.IntermediateItems)
        {
            AddRowCore(item);
        }

        _selectedRow = Rows.FirstOrDefault();
        UpdateSelectedRows();

        Recalculate(raisePreviewChanged: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? PreviewChanged;

    public string WindowTitle => Text("AutomaticPurlin_Title");
    public string WindowDescription => Text(
        IsProductionEdit
            ? "AutomaticPurlin_DescriptionProduction"
            : "AutomaticPurlin_Description");
    public string DatumPersistenceStatus => Text(
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
    internal string PreviewDiagnosticReason => _previewDiagnosticReason;
    public bool CanPreview => _previewPlan is not null;
    public bool IsProductionEdit => _mode == AutomaticPurlinDialogMode.ProductionEdit;
    public bool CanApply =>
        IsProductionEdit &&
        !_isApplyInProgress &&
        _previewPlan is not null &&
        (_previewPlan.Items.Count > 0 || _existingAutomaticPurlinCount > 0);
    public bool HasMultipleRidges => RidgeReferences.Count > 1;
    public double PurlinWidthMm { get; }
    public double PurlinHeightMm { get; }
    public double RafterHeightMm { get; }
    public bool LayoutExists { get; }
    public bool DatumExists { get; }
    public ObservableCollection<AutomaticPurlinRowViewModel> Rows { get; }
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
    public bool ShowSeatingCue => ShowEaveDistanceCue || ShowRidgeDistanceCue;
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

    public RoofRelativeElevationReferenceKind ReferenceKind
    {
        get => _referenceKind;
        set
        {
            if (_referenceKind == value)
            {
                return;
            }

            _referenceKind = value;
            OnPropertyChanged();
            Recalculate();
        }
    }

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

            _referenceLocalZText = normalized;
            _referenceLocalZEdited = true;
            OnPropertyChanged();
            Recalculate();
        }
    }

    internal AutomaticPurlinRowViewModel AddRow()
    {
        var row = AddRowCore(new RoofAutomaticPurlinLayoutItem(
            RoofAutomaticPurlinLayoutItemIdentity.Create(),
            true,
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            500d));
        SelectedRow = row;
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
        RefreshRowDisplayNames();
        if (ReferenceEquals(SelectedRow, row))
        {
            SelectedRow = Rows.Count == 0
                ? null
                : Rows[Math.Min(removedIndex, Rows.Count - 1)];
        }
        Recalculate();
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
        var row = new AutomaticPurlinRowViewModel(
            item,
            PlacementModes,
            SeatingModes,
            RidgeReferences,
            RafterHeightMm,
            _culture,
            Text);
        row.Changed += Row_Changed;
        Rows.Add(row);
        RefreshRowDisplayNames();
        return row;
    }

    private void RefreshRowDisplayNames()
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            Rows[index].SetDisplayIndex(index + 1);
        }
    }

    private void Row_Changed(object? sender, EventArgs e)
    {
        NotifySchematicState();
        Recalculate();
    }

    private void Recalculate(bool raisePreviewChanged = true)
    {
        if (_isRecalculating)
        {
            return;
        }

        _isRecalculating = true;
        try
        {
            _previewPlan = null;
            if (_boundaryProvenance is null || !_boundaryProvenance.IsValid)
            {
                SetValidation(
                    "AutomaticPurlin_ValidationBoundaryIdentity",
                    "BoundaryIdentityUnavailable");
                return;
            }

            if (!TryCreateDatum(out var datum) || datum is null)
            {
                SetValidation(
                    "AutomaticPurlin_ValidationDatum",
                    "InvalidRelativeElevationDatum");
                return;
            }

            if (RidgeEnabled && RidgeReferences.Count == 0)
            {
                SetValidation(
                    _hasInclinedRidge
                        ? "AutomaticPurlin_ValidationInclinedRidge"
                        : "AutomaticPurlin_ValidationNoHorizontalRidge",
                    _hasInclinedRidge ? "InclinedRidge" : "HorizontalRidgeMissing");
                return;
            }

            if (!TryCreateLayout(out var layout, out var rowError) || layout is null)
            {
                SetValidation(
                    rowError ?? "AutomaticPurlin_ValidationValue",
                    rowError == "AutomaticPurlin_ValidationRidgeReference"
                        ? "RidgeReferenceMissing"
                        : "InvalidPlacementValue");
                return;
            }

            var input = new RoofAutomaticPurlinPlanningInput(
                datum,
                PurlinHeightMm,
                RafterHeightMm);
            foreach (var row in Rows)
            {
                row.UpdateDerived(null);
                if (!row.TryCreateItem(out var item, out _) || item is null)
                {
                    continue;
                }

                var oneRow = new RoofAutomaticPurlinLayout(
                    false,
                    new[] { item with { Enabled = true } });
                var rowPlan = RoofAutomaticPurlinPlanner.Create(
                    _geometry,
                    _boundaryProvenance,
                    oneRow,
                    input);
                row.UpdateDerived(rowPlan.Plan?.Items.FirstOrDefault());
            }

            var result = RoofAutomaticPurlinPlanner.Create(
                _geometry,
                _boundaryProvenance,
                layout,
                input);
            if (!result.IsValid || result.Plan is null)
            {
                SetValidation(MapPlanError(result.Error), result.Error.ToString());
                return;
            }

            _previewPlan = result.Plan;
            SetValidation(null, "None");
        }
        finally
        {
            _isRecalculating = false;
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanApply));
            if (raisePreviewChanged)
            {
                PreviewChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool TryCreateDatum(out RoofRelativeElevationDatum? datum)
    {
        datum = null;
        var relativeMm = _loadedRelativeReferenceMm;
        var localZMm = _loadedReferenceLocalZMm;
        if ((_relativeReferenceEdited &&
             !RoofRelativeElevationDatumRules.TryParseMetres(
                 RelativeReferenceText,
                 _culture,
                 out relativeMm)) ||
            (_referenceLocalZEdited &&
             !TryParseMillimetres(ReferenceLocalZText, out localZMm)))
        {
            return false;
        }

        var validation = RoofRelativeElevationDatumRules.Validate(
            RoofRelativeElevationDatumSchema.CurrentVersion,
            ReferenceKind,
            relativeMm,
            localZMm);
        datum = validation.Datum;
        return validation.IsValid;
    }

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

        layout = new RoofAutomaticPurlinLayout(RidgeEnabled, items.AsReadOnly());
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
    private readonly double _rafterHeightMm;
    private RoofAutomaticPurlinSeatingDepth? _distanceSeating;
    private readonly double _loadedPlacementValueMm;
    private bool _placementValueEdited;
    private bool _seatingDepthValueEdited;
    private bool _preserveImplicitSingleRidgeReference;
    private bool _enabled;
    private bool _isSchematicSelected;
    private int _displayIndex;
    private RoofAutomaticPurlinPlacementMode _placementMode;
    private string _placementValueText;
    private string _seatingDepthValueText;
    private AutomaticPurlinRidgeOption? _selectedRidgeReference;
    private string _planPosition = "—";
    private string _roofPlaneRelative = "—";
    private string _seatingDepthDerived = "—";
    private string _bottomRelative = "—";
    private string _centerRelative = "—";
    private string _topRelative = "—";

    internal AutomaticPurlinRowViewModel(
        RoofAutomaticPurlinLayoutItem item,
        IReadOnlyList<AutomaticPurlinPlacementModeOption> placementModes,
        IReadOnlyList<AutomaticPurlinSeatingModeOption> seatingModes,
        IReadOnlyList<AutomaticPurlinRidgeOption> ridgeReferences,
        double rafterHeightMm,
        CultureInfo culture,
        Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(item);
        LayoutItemId = item.LayoutItemId;
        _enabled = item.Enabled;
        _placementMode = item.PlacementMode;
        _loadedPlacementValueMm = item.PlacementValueMm;
        _placementValueText = item.PlacementValueMm.ToString("0.###", culture);
        _culture = culture;
        _text = text;
        _rafterHeightMm = rafterHeightMm;
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? Changed;

    public string LayoutItemId { get; }
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
    public bool IsSeatingApplicable =>
        PlacementMode is RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave or
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
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

            _placementMode = value;
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

    internal void SetSchematicSelected(bool selected)
    {
        if (_isSchematicSelected == selected)
        {
            return;
        }

        _isSchematicSelected = selected;
        OnPropertyChanged(nameof(IsSchematicSelected));
    }

    internal bool TryCreateItem(
        out RoofAutomaticPurlinLayoutItem? item,
        out string? errorResourceKey)
    {
        item = null;
        errorResourceKey = null;
        var value = _loadedPlacementValueMm;
        if ((_placementValueEdited && !TryParseValue(PlacementValueText, out value)) ||
            !double.IsFinite(value) || value <= 0d)
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
                seatingValue <= 0d ||
                (mode == RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight &&
                 seatingValue >= 100d) ||
                (mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm &&
                 seatingValue >= _rafterHeightMm))
            {
                errorResourceKey = "AutomaticPurlin_ValidationSeating";
                return false;
            }

            seating = new RoofAutomaticPurlinSeatingDepth(mode, seatingValue);
            _distanceSeating = seating;
        }

        item = new RoofAutomaticPurlinLayoutItem(
            LayoutItemId,
            Enabled,
            PlacementMode,
            value,
            ridgeKey,
            seating);
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
        _roofPlaneRelative = physical is null
            ? "—"
            : RoofRelativeElevationDatumRules.FormatMetres(
                physical.RafterCenterRelativeElevationMm);
        _seatingDepthDerived = physical is null
            ? "—"
            : string.Format(
                _culture,
                "{0:0.###} {1}",
                physical.SeatingDepthMm,
                _text("AutomaticPurlin_UnitMillimetres"));
        _bottomRelative = profile is null
            ? "—"
            : RoofRelativeElevationDatumRules.FormatMetres(profile.BottomRelativeElevationMm);
        _centerRelative = profile is null
            ? "—"
            : RoofRelativeElevationDatumRules.FormatMetres(profile.CenterRelativeElevationMm);
        _topRelative = profile is null
            ? "—"
            : RoofRelativeElevationDatumRules.FormatMetres(profile.TopRelativeElevationMm);
        OnPropertyChanged(nameof(PlanPosition));
        OnPropertyChanged(nameof(RoofPlaneRelative));
        OnPropertyChanged(nameof(SeatingDepthDerived));
        OnPropertyChanged(nameof(BottomRelative));
        OnPropertyChanged(nameof(CenterRelative));
        OnPropertyChanged(nameof(TopRelative));
    }

    private void NormalizeModeState()
    {
        if (PlacementMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
        {
            _selectedRidgeReference = null;
            return;
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

        if (PlacementMode == RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave)
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
    }

    private bool TryParseValue(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, _culture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value);

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
