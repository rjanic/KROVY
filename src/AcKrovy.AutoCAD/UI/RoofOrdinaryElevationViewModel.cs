using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// ViewModel for Výškové osadenie (Elevation seating) section.
///
/// Canonical internal representation: AxisStartElevationMm / AxisEndElevationMm (OS / centerline).
/// The DisplayReference (SH/OS/VH) controls which datum is presented in the text fields.
/// Changing DisplayReference alone does NOT change the canonical axis elevations.
///
/// Three independent calculation modes:
///   LowerUpper:  LowerZ + UpperZ editable; Slope derived (read-only).
///   LowerSlope:  LowerZ + Slope editable; UpperZ derived (read-only).
///   UpperSlope:  UpperZ + Slope editable; LowerZ derived (read-only).
///
/// GeometryChanged: true if the current axis elevations differ from the initial state.
/// DisplayReferenceChanged: true if only the DisplayReference was changed.
/// Both flags are evaluated without AutoCAD geometry access.
///
/// Live recalculation happens on every text change in the ViewModel.
/// The host (RoofOrdinaryElevationWindow) calls ApplyRequested to trigger a transaction.
///
/// Image-choice cards are type-extensible via <see cref="ElevationImageChoiceCatalog"/>;
/// Ordinary is the current pack, other timber types plug in later without a new window.
/// </summary>
public sealed class RoofOrdinaryElevationViewModel : INotifyPropertyChanged
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly CultureInfo _culture;
    private readonly TimberElementType _elementType;
    private readonly double _planLengthMm;
    public double PlanLengthMm => _planLengthMm;
    private readonly double _heightMm;
    private readonly double _initialAxisStartMm;
    private readonly double _initialAxisEndMm;
    private readonly StructuralMemberElevationReferenceKind _initialReference;

    // Current canonical state (axis / OS elevations).
    private double _axisStartMm;
    private double _axisEndMm;
    private StructuralMemberElevationReferenceKind _displayReference;
    private StructuralMemberElevationCalculationMode _calcMode;

    // Text fields (displayed in the selected datum).
    private string _lowerZText = "";
    private string _upperZText = "";
    private string _slopeText = "";
    private bool _updating; // re-entrancy guard

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public RoofOrdinaryElevationViewModel(
        StructuralMemberElevationState initialState,
        double planLengthMm,
        double heightMm,
        CultureInfo? culture = null,
        TimberElementType elementType = TimberElementType.Rafter)
    {
        _culture = culture ?? CultureInfo.GetCultureInfo("sk-SK");
        _elementType = elementType;
        _planLengthMm = planLengthMm;
        _heightMm = heightMm;
        _axisStartMm = initialState.AxisStartElevationMm;
        _axisEndMm = initialState.AxisEndElevationMm;
        _displayReference = initialState.DisplayReference;
        _initialReference = initialState.DisplayReference;
        _initialAxisStartMm = initialState.AxisStartElevationMm;
        _initialAxisEndMm = initialState.AxisEndElevationMm;
        _calcMode = initialState.CalculationMode;
        ReferenceChoices = ElevationImageChoiceCatalog.CreateReferenceChoices(
            _elementType, _displayReference);
        CalculationChoices = ElevationImageChoiceCatalog.CreateCalculationChoices(
            _elementType, _calcMode);
        RefreshTextFields();
    }

    /// <summary>Element type that owns the current image-choice pack.</summary>
    public TimberElementType ElementType => _elementType;

    /// <summary>SH / OS / VH image cards (type-specific pack).</summary>
    public IReadOnlyList<ElevationImageChoiceItem> ReferenceChoices { get; }

    /// <summary>Calculation-mode image cards (type-specific pack).</summary>
    public IReadOnlyList<ElevationImageChoiceItem> CalculationChoices { get; }

    /// <summary>Absolute slope degrees (Ordinary timber field); same underlying axis math.</summary>
    public double AbsoluteSlopeDegrees =>
        Math.Abs(StructuralMemberElevationRules.DeriveSlopeDegrees(
            _axisStartMm, _axisEndMm, _planLengthMm));

    /// <summary>Signed Start→End slope degrees used by elevation recalculation.</summary>
    public double SignedSlopeDegrees =>
        StructuralMemberElevationRules.DeriveSlopeDegrees(
            _axisStartMm, _axisEndMm, _planLengthMm);

    /// <summary>True when both endpoint elevations are equal (slope 0°; fall has no meaning).</summary>
    public bool IsHorizontal =>
        StructuralMemberElevationRules.IsHorizontal(_axisStartMm, _axisEndMm);

    /// <summary>
    /// Timber "Smer spádu" reversed flag derived from current A/B elevations
    /// (true when Start→End is uphill so the arrow points downhill).
    /// </summary>
    public bool IsSlopeDirectionReversed =>
        StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            _axisStartMm, _axisEndMm);

    public void SelectReferenceChoice(ElevationImageChoiceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Payload is not StructuralMemberElevationReferenceKind kind) return;
        SetReference(kind);
    }

    public void SelectCalculationChoice(ElevationImageChoiceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Payload is not StructuralMemberElevationCalculationMode mode) return;
        SetCalcMode(mode);
    }

    // -------------------------------------------------------------------------
    // Public properties — reference selector
    // -------------------------------------------------------------------------

    public bool SH_Selected
    {
        get => _displayReference == StructuralMemberElevationReferenceKind.SH;
        set { if (value) SetReference(StructuralMemberElevationReferenceKind.SH); }
    }

    public bool OS_Selected
    {
        get => _displayReference == StructuralMemberElevationReferenceKind.OS;
        set { if (value) SetReference(StructuralMemberElevationReferenceKind.OS); }
    }

    public bool VH_Selected
    {
        get => _displayReference == StructuralMemberElevationReferenceKind.VH;
        set { if (value) SetReference(StructuralMemberElevationReferenceKind.VH); }
    }

    // -------------------------------------------------------------------------
    // Public properties — calculation mode
    // -------------------------------------------------------------------------

    public bool Mode_LowerUpper
    {
        get => _calcMode == StructuralMemberElevationCalculationMode.LowerUpper;
        set { if (value) SetCalcMode(StructuralMemberElevationCalculationMode.LowerUpper); }
    }

    public bool Mode_LowerSlope
    {
        get => _calcMode == StructuralMemberElevationCalculationMode.LowerSlope;
        set { if (value) SetCalcMode(StructuralMemberElevationCalculationMode.LowerSlope); }
    }

    public bool Mode_UpperSlope
    {
        get => _calcMode == StructuralMemberElevationCalculationMode.UpperSlope;
        set { if (value) SetCalcMode(StructuralMemberElevationCalculationMode.UpperSlope); }
    }

    // -------------------------------------------------------------------------
    // Public properties — text fields
    // -------------------------------------------------------------------------

    /// <summary>Lower-Z endpoint display text. Editable in LowerUpper and LowerSlope modes.</summary>
    public string LowerZText
    {
        get => _lowerZText;
        set
        {
            if (_updating || _lowerZText == value) return;
            _lowerZText = value;
            OnPropertyChanged();
            if (_calcMode == StructuralMemberElevationCalculationMode.LowerUpper ||
                _calcMode == StructuralMemberElevationCalculationMode.LowerSlope)
                RecalculateFromLower();
        }
    }

    /// <summary>Upper-Z endpoint display text. Editable in LowerUpper and UpperSlope modes.</summary>
    public string UpperZText
    {
        get => _upperZText;
        set
        {
            if (_updating || _upperZText == value) return;
            _upperZText = value;
            OnPropertyChanged();
            if (_calcMode == StructuralMemberElevationCalculationMode.LowerUpper ||
                _calcMode == StructuralMemberElevationCalculationMode.UpperSlope)
                RecalculateFromUpper();
        }
    }

    /// <summary>Slope text ("45,00°"). Editable in LowerSlope and UpperSlope modes.</summary>
    public string SlopeText
    {
        get => _slopeText;
        set
        {
            if (_updating || _slopeText == value) return;
            _slopeText = value;
            OnPropertyChanged();
            if (_calcMode == StructuralMemberElevationCalculationMode.LowerSlope ||
                _calcMode == StructuralMemberElevationCalculationMode.UpperSlope)
                RecalculateFromSlope();
        }
    }

    /// <summary>LowerZ is editable in LowerUpper and LowerSlope modes.</summary>
    public bool LowerZEditable =>
        _calcMode is StructuralMemberElevationCalculationMode.LowerUpper
            or StructuralMemberElevationCalculationMode.LowerSlope;

    /// <summary>UpperZ is editable in LowerUpper and UpperSlope modes.</summary>
    public bool UpperZEditable =>
        _calcMode is StructuralMemberElevationCalculationMode.LowerUpper
            or StructuralMemberElevationCalculationMode.UpperSlope;

    /// <summary>Slope is editable in LowerSlope and UpperSlope modes.</summary>
    public bool SlopeEditable =>
        _calcMode is StructuralMemberElevationCalculationMode.LowerSlope
            or StructuralMemberElevationCalculationMode.UpperSlope;

    // -------------------------------------------------------------------------
    // Change detection
    // -------------------------------------------------------------------------

    /// <summary>True if the axis elevations differ from the initial state (geometry change).</summary>
    public bool GeometryChanged =>
        Math.Abs(_axisStartMm - _initialAxisStartMm) > 0.001d ||
        Math.Abs(_axisEndMm - _initialAxisEndMm) > 0.001d;

    /// <summary>True if only the DisplayReference changed (no geometry effect).</summary>
    public bool DisplayReferenceChanged =>
        _displayReference != _initialReference && !GeometryChanged;

    /// <summary>True if any change is pending.</summary>
    public bool HasChanges => GeometryChanged || DisplayReferenceChanged;

    // -------------------------------------------------------------------------
    // Build the requested state for Apply
    // -------------------------------------------------------------------------

    public StructuralMemberElevationState BuildRequestedState() =>
        StructuralMemberElevationRules.CreateSloped(
            _axisStartMm, _axisEndMm, _displayReference, _calcMode);

    // -------------------------------------------------------------------------
    // Reference switching (no geometry change)
    // -------------------------------------------------------------------------

    private void SetReference(StructuralMemberElevationReferenceKind kind)
    {
        if (_displayReference == kind) return;
        _displayReference = kind;
        SyncChoiceSelection(ReferenceChoices, kind);
        RefreshTextFields();
        OnPropertyChanged(nameof(SH_Selected));
        OnPropertyChanged(nameof(OS_Selected));
        OnPropertyChanged(nameof(VH_Selected));
        OnPropertyChanged(nameof(GeometryChanged));
        OnPropertyChanged(nameof(DisplayReferenceChanged));
        OnPropertyChanged(nameof(HasChanges));
    }

    // -------------------------------------------------------------------------
    // Calculation mode switching (no geometry change)
    // -------------------------------------------------------------------------

    private void SetCalcMode(StructuralMemberElevationCalculationMode mode)
    {
        if (_calcMode == mode) return;
        _calcMode = mode;
        SyncChoiceSelection(CalculationChoices, mode);
        RefreshTextFields();
        OnPropertyChanged(nameof(Mode_LowerUpper));
        OnPropertyChanged(nameof(Mode_LowerSlope));
        OnPropertyChanged(nameof(Mode_UpperSlope));
        OnPropertyChanged(nameof(LowerZEditable));
        OnPropertyChanged(nameof(UpperZEditable));
        OnPropertyChanged(nameof(SlopeEditable));
        OnPropertyChanged(nameof(AbsoluteSlopeDegrees));
        OnPropertyChanged(nameof(SignedSlopeDegrees));
    }

    private static void SyncChoiceSelection(
        IReadOnlyList<ElevationImageChoiceItem> choices,
        object selectedPayload)
    {
        foreach (var choice in choices)
            choice.IsSelected = Equals(choice.Payload, selectedPayload);
    }

    // -------------------------------------------------------------------------
    // Live recalculation from changed fields
    // -------------------------------------------------------------------------

    private double CurrentHeightAxisZ() =>
        StructuralMemberElevationRules.HeightAxisZ(
            Math.Abs(StructuralMemberElevationRules.DeriveSlopeDegrees(
                _axisStartMm, _axisEndMm, _planLengthMm)));

    /// <summary>True when Line.Start is the structurally lower (eave-side) axis end.</summary>
    private bool StartIsStructuralLower =>
        StructuralMemberElevationRules.IsStartTheEaveEnd(_axisStartMm, _axisEndMm);

    private void RecalculateFromLower()
    {
        // Structural lower/upper by Z — not blindly Start/End.
        var haz = CurrentHeightAxisZ();
        if (!TryParseDisplayZ(_lowerZText, haz, isLower: true, out var lowerDisplayMm)) return;
        var axisAtLower = StructuralMemberElevationRules.ToAxisElevationMm(
            lowerDisplayMm, _displayReference, _heightMm, haz);
        if (_calcMode == StructuralMemberElevationCalculationMode.LowerUpper)
        {
            if (!TryParseDisplayZ(_upperZText, haz, isLower: false, out var upperDisplayMm)) return;
            var axisAtUpper = StructuralMemberElevationRules.ToAxisElevationMm(
                upperDisplayMm, _displayReference, _heightMm, haz);
            SetStructuralAxisElevations(axisAtLower, axisAtUpper);
        }
        else // LowerSlope: structural lower + signed Start→End slope → other end
        {
            if (!StructuralMemberElevationRules.TryParseSlopeDegrees(_slopeText, out var slopeDeg)) return;
            ApplyLowerAndSignedSlope(axisAtLower, slopeDeg);
        }
    }

    private void RecalculateFromUpper()
    {
        var haz = CurrentHeightAxisZ();
        if (!TryParseDisplayZ(_upperZText, haz, isLower: false, out var upperDisplayMm)) return;
        var axisAtUpper = StructuralMemberElevationRules.ToAxisElevationMm(
            upperDisplayMm, _displayReference, _heightMm, haz);
        if (_calcMode == StructuralMemberElevationCalculationMode.LowerUpper)
        {
            if (!TryParseDisplayZ(_lowerZText, haz, isLower: true, out var lowerDisplayMm)) return;
            var axisAtLower = StructuralMemberElevationRules.ToAxisElevationMm(
                lowerDisplayMm, _displayReference, _heightMm, haz);
            SetStructuralAxisElevations(axisAtLower, axisAtUpper);
        }
        else // UpperSlope
        {
            if (!StructuralMemberElevationRules.TryParseSlopeDegrees(_slopeText, out var slopeDeg)) return;
            ApplyUpperAndSignedSlope(axisAtUpper, slopeDeg);
        }
    }

    private void RecalculateFromSlope()
    {
        if (!StructuralMemberElevationRules.TryParseSlopeDegrees(_slopeText, out var slopeDeg)) return;
        var haz = CurrentHeightAxisZ();
        if (_calcMode == StructuralMemberElevationCalculationMode.LowerSlope)
        {
            if (!TryParseDisplayZ(_lowerZText, haz, isLower: true, out var lowerDisplayMm)) return;
            var axisAtLower = StructuralMemberElevationRules.ToAxisElevationMm(
                lowerDisplayMm, _displayReference, _heightMm, haz);
            ApplyLowerAndSignedSlope(axisAtLower, slopeDeg);
        }
        else // UpperSlope
        {
            if (!TryParseDisplayZ(_upperZText, haz, isLower: false, out var upperDisplayMm)) return;
            var axisAtUpper = StructuralMemberElevationRules.ToAxisElevationMm(
                upperDisplayMm, _displayReference, _heightMm, haz);
            ApplyUpperAndSignedSlope(axisAtUpper, slopeDeg);
        }

        // Normalize display to positive magnitude after a successful parse/apply.
        var normalized = StructuralMemberElevationRules.FormatSlopeDegrees(Math.Abs(slopeDeg), _culture);
        if (_slopeText != normalized)
        {
            _updating = true;
            try
            {
                _slopeText = normalized;
                OnPropertyChanged(nameof(SlopeText));
            }
            finally { _updating = false; }
        }
    }

    /// <summary>
    /// Slope input is a positive magnitude. Fall direction comes from which plan end
    /// is currently the structural lower (lower Z). Horizontal slope (0°) clears fall.
    /// </summary>
    private void ApplyLowerAndSignedSlope(double axisAtLower, double slopeDeg)
    {
        var abs = Math.Abs(slopeDeg);
        if (abs <= 1e-12)
        {
            SetCanonicalAxisElevations(axisAtLower, axisAtLower);
            return;
        }

        if (StartIsStructuralLower)
        {
            // Start lower → End higher: Start→End signed = +abs.
            var axisAtEnd = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(
                axisAtLower, _planLengthMm, abs);
            SetCanonicalAxisElevations(axisAtLower, axisAtEnd);
        }
        else
        {
            // End lower → Start higher: Start→End signed = −abs.
            var axisAtStart = StructuralMemberElevationRules.DeriveStartFromEndAndSlope(
                axisAtLower, _planLengthMm, -abs);
            SetCanonicalAxisElevations(axisAtStart, axisAtLower);
        }
    }

    private void ApplyUpperAndSignedSlope(double axisAtUpper, double slopeDeg)
    {
        var abs = Math.Abs(slopeDeg);
        if (abs <= 1e-12)
        {
            SetCanonicalAxisElevations(axisAtUpper, axisAtUpper);
            return;
        }

        if (StartIsStructuralLower)
        {
            // Start lower, End upper.
            var axisAtStart = StructuralMemberElevationRules.DeriveStartFromEndAndSlope(
                axisAtUpper, _planLengthMm, abs);
            SetCanonicalAxisElevations(axisAtStart, axisAtUpper);
        }
        else
        {
            // End lower, Start upper.
            var axisAtEnd = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(
                axisAtUpper, _planLengthMm, -abs);
            SetCanonicalAxisElevations(axisAtUpper, axisAtEnd);
        }
    }

    // -------------------------------------------------------------------------
    // Canonical axis elevation update and derived-field refresh
    // -------------------------------------------------------------------------

    /// <summary>Map structural lower/upper Z onto Line Start/End without reversing plan roles.</summary>
    private void SetStructuralAxisElevations(double structuralLowerAxisMm, double structuralUpperAxisMm)
    {
        if (StartIsStructuralLower)
            SetCanonicalAxisElevations(structuralLowerAxisMm, structuralUpperAxisMm);
        else
            SetCanonicalAxisElevations(structuralUpperAxisMm, structuralLowerAxisMm);
    }

    private void SetCanonicalAxisElevations(double axisStartMm, double axisEndMm)
    {
        _axisStartMm = axisStartMm;
        _axisEndMm = axisEndMm;
        RefreshDerivedField();
        OnPropertyChanged(nameof(GeometryChanged));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(AbsoluteSlopeDegrees));
        OnPropertyChanged(nameof(SignedSlopeDegrees));
        OnPropertyChanged(nameof(IsHorizontal));
        OnPropertyChanged(nameof(IsSlopeDirectionReversed));
    }

    private void RefreshDerivedField()
    {
        _updating = true;
        try
        {
            var haz = CurrentHeightAxisZ();
            // Lower/upper are physical Z roles — remapped after crossing.
            var lowerAxis = Math.Min(_axisStartMm, _axisEndMm);
            var upperAxis = Math.Max(_axisStartMm, _axisEndMm);
            var absSlope = AbsoluteSlopeDegrees;
            switch (_calcMode)
            {
                case StructuralMemberElevationCalculationMode.LowerUpper:
                    _lowerZText = FormatMetres(StructuralMemberElevationRules.ToDisplayElevationMm(
                        lowerAxis, _displayReference, _heightMm, haz));
                    _upperZText = FormatMetres(StructuralMemberElevationRules.ToDisplayElevationMm(
                        upperAxis, _displayReference, _heightMm, haz));
                    _slopeText = StructuralMemberElevationRules.FormatSlopeDegrees(absSlope, _culture);
                    OnPropertyChanged(nameof(LowerZText));
                    OnPropertyChanged(nameof(UpperZText));
                    OnPropertyChanged(nameof(SlopeText));
                    break;
                case StructuralMemberElevationCalculationMode.LowerSlope:
                    _upperZText = FormatMetres(StructuralMemberElevationRules.ToDisplayElevationMm(
                        upperAxis, _displayReference, _heightMm, haz));
                    OnPropertyChanged(nameof(UpperZText));
                    break;
                case StructuralMemberElevationCalculationMode.UpperSlope:
                    _lowerZText = FormatMetres(StructuralMemberElevationRules.ToDisplayElevationMm(
                        lowerAxis, _displayReference, _heightMm, haz));
                    OnPropertyChanged(nameof(LowerZText));
                    break;
            }

            OnPropertyChanged(nameof(IsHorizontal));
            OnPropertyChanged(nameof(IsSlopeDirectionReversed));
        }
        finally { _updating = false; }
    }

    // -------------------------------------------------------------------------
    // Refresh all text fields from canonical axis state
    // -------------------------------------------------------------------------

    private void RefreshTextFields()
    {
        _updating = true;
        try
        {
            var haz = CurrentHeightAxisZ();
            // Structural lower/upper by Z (not Start/End).
            var lowerAxis = Math.Min(_axisStartMm, _axisEndMm);
            var upperAxis = Math.Max(_axisStartMm, _axisEndMm);
            var lowerDisplay = StructuralMemberElevationRules.ToDisplayElevationMm(
                lowerAxis, _displayReference, _heightMm, haz);
            var upperDisplay = StructuralMemberElevationRules.ToDisplayElevationMm(
                upperAxis, _displayReference, _heightMm, haz);
            // Display slope as positive magnitude; fall direction is separate.
            _lowerZText = FormatMetres(lowerDisplay);
            _upperZText = FormatMetres(upperDisplay);
            _slopeText = StructuralMemberElevationRules.FormatSlopeDegrees(
                AbsoluteSlopeDegrees, _culture);
            OnPropertyChanged(nameof(LowerZText));
            OnPropertyChanged(nameof(UpperZText));
            OnPropertyChanged(nameof(SlopeText));
            OnPropertyChanged(nameof(IsHorizontal));
            OnPropertyChanged(nameof(IsSlopeDirectionReversed));
        }
        finally { _updating = false; }
    }

    // -------------------------------------------------------------------------
    // Parsing helpers
    // -------------------------------------------------------------------------

    private bool TryParseDisplayZ(string? text, double haz, bool isLower, out double valueMm) =>
        RoofRelativeElevationDatumRules.TryParseMetres(text, _culture, out valueMm);

    private string FormatMetres(double valueMm) =>
        RoofRelativeElevationDatumRules.FormatMetres(valueMm, _culture);

    // -------------------------------------------------------------------------
    // INotifyPropertyChanged
    // -------------------------------------------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
