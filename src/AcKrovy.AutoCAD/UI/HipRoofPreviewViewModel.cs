using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Uniform-pitch create or edit input for the general Hip topology.</summary>
internal sealed class HipRoofPreviewViewModel : INotifyPropertyChanged
{
    private readonly RoofFootprint _footprint;
    private readonly CultureInfo _culture;
    private readonly HipRoofDialogMode _mode;
    private readonly bool _supportsPhysical3D;
    private readonly RectangularRoofFootprintDescription? _rectangle;
    private string _slopeText;
    private string _elevationText;
    private string _validationMessage = string.Empty;
    private bool _hasSessionValidation;
    private bool _isEaveElevationMode = true;
    private bool _physical3DEnabled;
    private RoofPhysicalDisplayVisibility _displayVisibility = RoofPhysicalDisplayVisibility.Both;
    private LowerEndCutMode _lowerEndCutMode = LowerEndCutMode.Vertical;
    private RidgeJoinMode _ridgeJoinMode = RidgeJoinMode.Meet;
    private bool _suppressElevationRecalc;
    private HipRoofGeometry? _geometry;
    private RoofAbsoluteElevationState? _elevationState;

    internal HipRoofPreviewViewModel(RoofFootprint footprint, CultureInfo? culture = null)
        : this(footprint, 30d, HipRoofDialogMode.Create, culture)
    {
    }

    internal HipRoofPreviewViewModel(
        RoofFootprint footprint,
        double slopeDegrees,
        HipRoofDialogMode mode,
        CultureInfo? culture = null)
        : this(
            footprint,
            slopeDegrees,
            mode,
            seedElevation: null,
            culture)
    {
    }

    internal HipRoofPreviewViewModel(
        RoofFootprint footprint,
        double slopeDegrees,
        HipRoofDialogMode mode,
        RoofAbsoluteElevationState? seedElevation,
        CultureInfo? culture = null)
    {
        _footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _mode = mode;
        // Roof-surface Faces/Edges remain rectangle-only, but ordinary timber
        // solids can use any solved Hip topology with valid ordinary members.
        _supportsPhysical3D = true;
        _ = RectangularRoofFootprintRules.TryDescribe(footprint, out _rectangle);
        _slopeText = slopeDegrees.ToString("R", _culture);
        if (seedElevation is not null)
        {
            _isEaveElevationMode =
                seedElevation.InputMode == RoofAbsoluteElevationInputMode.Eave;
            _physical3DEnabled = seedElevation.Physical3DEnabled;
            _displayVisibility = seedElevation.DisplayVisibility;
            _lowerEndCutMode = seedElevation.LowerEndCutMode;
            _ridgeJoinMode = seedElevation.RidgeJoinMode;
            _elevationText = RoofAbsoluteElevationRules.FormatMetres(
                seedElevation.EnteredRelativeElevationMm,
                _culture);
        }
        else
        {
            _physical3DEnabled = mode == HipRoofDialogMode.Create && _supportsPhysical3D;
            _displayVisibility = RoofPhysicalDisplayVisibility.Both;
            _elevationText = RoofAbsoluteElevationRules.FormatMetres(0d, _culture);
        }

        Recalculate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle => UiStrings.GetString("CommandUi_RoofHip_Label", _culture);
    public string WindowDescription => UiStrings.GetString("CommandUi_RoofHip_Tooltip", _culture);
    public string PrimaryActionText => UiStrings.GetString(
        _mode == HipRoofDialogMode.Edit ? "EditWindow_Apply" : "RoofGeometryWindow_Create",
        _culture);

    public bool SupportsPhysicalElevation => _supportsPhysical3D;

    public string SlopeText
    {
        get => _slopeText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_slopeText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _slopeText = normalized;
            OnPropertyChanged();
            Recalculate();
        }
    }

    public bool IsEaveElevationMode
    {
        get => _isEaveElevationMode;
        set
        {
            if (_isEaveElevationMode == value)
            {
                return;
            }

            _isEaveElevationMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRidgeElevationMode));
            if (_elevationState is not null)
            {
                var switched = RoofAbsoluteElevationRules.SwitchMode(
                    _elevationState,
                    value
                        ? RoofAbsoluteElevationInputMode.Eave
                        : RoofAbsoluteElevationInputMode.Ridge);
                _suppressElevationRecalc = true;
                try
                {
                    _elevationText = RoofAbsoluteElevationRules.FormatMetres(
                        switched.EnteredRelativeElevationMm,
                        _culture);
                    OnPropertyChanged(nameof(ElevationText));
                }
                finally
                {
                    _suppressElevationRecalc = false;
                }

                _elevationState = switched with
                {
                    Physical3DEnabled = _physical3DEnabled,
                    DisplayVisibility = _displayVisibility,
                };
                NotifyElevationDerived();
            }
            else
            {
                Recalculate();
            }
        }
    }

    public bool IsRidgeElevationMode
    {
        get => !_isEaveElevationMode;
        set
        {
            if (value)
            {
                IsEaveElevationMode = false;
            }
        }
    }

    public string ElevationText
    {
        get => _elevationText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_elevationText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _elevationText = normalized;
            OnPropertyChanged();
            if (!_suppressElevationRecalc)
            {
                Recalculate();
            }
        }
    }

    public bool Physical3DEnabled
    {
        get => _physical3DEnabled;
        set
        {
            if (_physical3DEnabled == value)
            {
                return;
            }

            _physical3DEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowDisplayVisibilityOptions));
            if (_elevationState is not null)
            {
                _elevationState = RoofAbsoluteElevationRules.WithPhysical3DEnabled(
                    _elevationState,
                    value);
                if (value &&
                    _elevationState.DisplayVisibility != _displayVisibility)
                {
                    _elevationState = RoofAbsoluteElevationRules.WithDisplayVisibility(
                        _elevationState,
                        _displayVisibility);
                }
            }
        }
    }

    public bool ShowDisplayVisibilityOptions =>
        _supportsPhysical3D && _physical3DEnabled;

    public bool IsDisplayBoth
    {
        get => _displayVisibility == RoofPhysicalDisplayVisibility.Both;
        set
        {
            if (value)
            {
                SetDisplayVisibility(RoofPhysicalDisplayVisibility.Both);
            }
        }
    }

    public bool IsDisplayPlan2D
    {
        get => _displayVisibility == RoofPhysicalDisplayVisibility.Plan2D;
        set
        {
            if (value)
            {
                SetDisplayVisibility(RoofPhysicalDisplayVisibility.Plan2D);
            }
        }
    }

    public bool IsDisplayModel3D
    {
        get => _displayVisibility == RoofPhysicalDisplayVisibility.Model3D;
        set
        {
            if (value)
            {
                SetDisplayVisibility(RoofPhysicalDisplayVisibility.Model3D);
            }
        }
    }

    private void SetDisplayVisibility(RoofPhysicalDisplayVisibility visibility)
    {
        if (_displayVisibility == visibility)
        {
            return;
        }

        _displayVisibility = visibility;
        OnPropertyChanged(nameof(IsDisplayBoth));
        OnPropertyChanged(nameof(IsDisplayPlan2D));
        OnPropertyChanged(nameof(IsDisplayModel3D));
        if (_elevationState is not null)
        {
            _elevationState = RoofAbsoluteElevationRules.WithDisplayVisibility(
                _elevationState,
                visibility);
        }
    }

    public string CalculatedElevationLabel =>
        _isEaveElevationMode
            ? UiStrings.GetString("RoofHip_CalculatedRidgeElevation", _culture)
            : UiStrings.GetString("RoofHip_CalculatedEaveElevation", _culture);

    public string CalculatedElevationText
    {
        get
        {
            if (_elevationState is null)
            {
                return "—";
            }

            var value = _isEaveElevationMode
                ? _elevationState.ResolvedRidgeRelativeElevationMm
                : _elevationState.ResolvedEaveRelativeElevationMm;
            return RoofAbsoluteElevationRules.FormatMetresWithUnit(value, _culture);
        }
    }

    public string EnteredElevationLabel =>
        _isEaveElevationMode
            ? UiStrings.GetString("RoofHip_EnteredEaveElevation", _culture)
            : UiStrings.GetString("RoofHip_EnteredRidgeElevation", _culture);

    public string ValidationMessage => _validationMessage;
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage);
    public bool HasSessionValidation => _hasSessionValidation;
    public bool CanPreview => _geometry is not null;
    public bool CanApply => _geometry is not null;

    internal bool TryGetRoofGeometry(out HipRoofGeometry? geometry)
    {
        geometry = _geometry;
        return geometry is not null;
    }

    internal bool TryGetElevationState(out RoofAbsoluteElevationState? elevation)
    {
        elevation = _elevationState;
        return elevation is not null;
    }

    /// <summary>
    /// Surfaces a workflow-level validation (e.g. Purlin preflight) without clearing
    /// the currently proposed geometry so Apply remains available for a corrected pitch.
    /// </summary>
    internal void SetSessionValidation(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return;
        }

        _hasSessionValidation = true;
        _validationMessage = TrimDisplayMessage(UiStrings.GetString(resourceKey, _culture));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(HasSessionValidation));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanApply));
    }

    private void Recalculate()
    {
        _geometry = null;
        _elevationState = null;
        if (!TryParseSlope(SlopeText, out var slope))
        {
            SetValidation("RoofGeometryWindow_ValidationNumber");
            NotifyElevationDerived();
            return;
        }

        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            _footprint,
            new RoofParameters(slope),
            RoofKind.Hip));
        _geometry = result.IsValid ? result.Geometry as HipRoofGeometry : null;
        if (_geometry is null)
        {
            SetValidation(result.Error == SimpleGableRoofGeometryError.InvalidSlope
                ? "RoofGeometryWindow_ValidationSlope"
                : "RoofGeometryWindow_ValidationHipTopology");
            NotifyElevationDerived();
            return;
        }

        if (_supportsPhysical3D && _geometry is not null)
        {
            if (!RoofAbsoluteElevationRules.TryParseMetres(
                    ElevationText,
                    _culture,
                    out var enteredMm))
            {
                SetValidation("RoofGeometryWindow_ValidationNumber");
                NotifyElevationDerived();
                return;
            }

            // For a non-rectangular topology the existing geometry RiseMm is
            // the authoritative highest roof point above its eaves. Convert
            // it to the equivalent plan run expected by FromEntered.
            var equivalentHalfWidth = _rectangle?.HalfWidthMm ??
                _geometry.RiseMm / Math.Tan(slope * Math.PI / 180d);
            _elevationState = RoofAbsoluteElevationRules.FromEntered(
                _isEaveElevationMode
                    ? RoofAbsoluteElevationInputMode.Eave
                    : RoofAbsoluteElevationInputMode.Ridge,
                enteredMm,
                equivalentHalfWidth,
                slope,
                _physical3DEnabled,
                _displayVisibility) with
            {
                LowerEndCutMode = _lowerEndCutMode,
                RidgeJoinMode = _ridgeJoinMode,
            };
        }
        else
        {
            _elevationState = null;
            _physical3DEnabled = false;
        }

        SetValidation(null);

#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[AK_ROOF_HIP] solve success={result.IsValid} error={result.Error} " +
            $"vertices={_footprint.Vertices.Count} nodes={_geometry?.Topology.Nodes.Count ?? 0}");
#endif
        NotifyElevationDerived();
    }

    private void NotifyElevationDerived()
    {
        OnPropertyChanged(nameof(CalculatedElevationText));
        OnPropertyChanged(nameof(CalculatedElevationLabel));
        OnPropertyChanged(nameof(EnteredElevationLabel));
        OnPropertyChanged(nameof(Physical3DEnabled));
        OnPropertyChanged(nameof(ShowDisplayVisibilityOptions));
        OnPropertyChanged(nameof(IsDisplayBoth));
        OnPropertyChanged(nameof(IsDisplayPlan2D));
        OnPropertyChanged(nameof(IsDisplayModel3D));
        OnPropertyChanged(nameof(SupportsPhysicalElevation));
    }

    private bool TryParseSlope(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, _culture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value);

    private void SetValidation(string? resourceKey)
    {
        _hasSessionValidation = false;
        _validationMessage = resourceKey is null
            ? string.Empty
            : TrimDisplayMessage(UiStrings.GetString(resourceKey, _culture));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(HasSessionValidation));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanApply));
    }

    private static string TrimDisplayMessage(string message) =>
        message.TrimStart('\r', '\n').TrimEnd();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal enum HipRoofDialogMode
{
    Create = 0,
    Edit = 1,
}
