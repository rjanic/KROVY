using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

internal sealed class GableRoofGeometryViewModel : INotifyPropertyChanged
{
    private readonly RoofFootprint _footprint;
    private readonly CultureInfo _culture;
    private readonly RoofDirection2D _fallbackDirection;
    private readonly bool _isMonopitchEditor;
    private readonly bool _isEditMode;
    private RoofKind _selectedKind;
    private string _alphaText = "30";
    private string _betaText = "35";
    private string _eaveHeightDifferenceText = "0";
    private string _ridgeDistanceFromEaveAText = string.Empty;
    private AsymmetricGableInputMode _asymmetricInputMode = AsymmetricGableInputMode.EaveHeightDifference;
    private bool _isAsymmetryMirrored;
    private MonopitchInputMode _monopitchInputMode = MonopitchInputMode.Slope;
    private bool _isMonopitchMirrored;
    private RoofDirection2D? _ridgeDirection;
    private SimpleGableRoofGeometry? _geometry;
    private MonopitchRoofGeometry? _monopitchGeometry;
    private GableRoofSectionState? _sectionState;
    private MonopitchRoofSectionState? _monopitchSectionState;
    private string _validationMessage = string.Empty;

    public GableRoofGeometryViewModel(
        RoofFootprint footprint,
        RoofKind initialKind = RoofKind.SimpleGable,
        CultureInfo? culture = null,
        bool isEditMode = false)
    {
        _footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        if (footprint.Vertices.Count != 4)
        {
            throw new ArgumentException("A rectangular footprint is required.", nameof(footprint));
        }

        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _isMonopitchEditor = initialKind == RoofKind.Monopitch;
        _isEditMode = isEditMode;
        _selectedKind = _isMonopitchEditor
            ? RoofKind.Monopitch
            : initialKind == RoofKind.AsymmetricGable
                ? RoofKind.AsymmetricGable
                : RoofKind.SimpleGable;
        DimensionAMm = footprint.Vertices[0].DistanceTo(footprint.Vertices[1]);
        DimensionBMm = footprint.Vertices[1].DistanceTo(footprint.Vertices[2]);
        var edge = footprint.Vertices[1];
        var start = footprint.Vertices[0];
        if (!RoofDirection2D.TryCreate(edge.X - start.X, edge.Y - start.Y, out _fallbackDirection))
        {
            throw new ArgumentException("The footprint has a degenerate canonical edge.", nameof(footprint));
        }
        Recalculate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double DimensionAMm { get; }

    public double DimensionBMm { get; }

    public string DimensionAText => FormatLength(DimensionAMm);

    public string DimensionBText => FormatLength(DimensionBMm);

    public RoofKind SelectedKind
    {
        get => _selectedKind;
        set
        {
            var normalized = _isMonopitchEditor
                ? RoofKind.Monopitch
                : value == RoofKind.AsymmetricGable
                    ? RoofKind.AsymmetricGable
                    : RoofKind.SimpleGable;
            if (_selectedKind == normalized)
            {
                return;
            }
            _selectedKind = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSymmetricMode));
            OnPropertyChanged(nameof(IsAsymmetricMode));
            OnPropertyChanged(nameof(IsMonopitchMode));
            Recalculate();
        }
    }

    public bool IsSymmetricMode
    {
        get => SelectedKind == RoofKind.SimpleGable;
        set
        {
            if (value)
            {
                SelectedKind = RoofKind.SimpleGable;
            }
        }
    }

    public bool IsAsymmetricMode
    {
        get => SelectedKind == RoofKind.AsymmetricGable;
        set
        {
            if (value)
            {
                SelectedKind = RoofKind.AsymmetricGable;
            }
        }
    }

    public bool IsMonopitchMode => SelectedKind == RoofKind.Monopitch;

    public bool IsGableEditor => !_isMonopitchEditor;

    public bool IsMonopitchEditor => _isMonopitchEditor;

    public string WindowTitle => UiStrings.GetString(
        IsMonopitchMode ? "RoofGeometryWindow_MonopitchTitle" : "RoofGeometryWindow_Title",
        _culture);

    public string WindowHeading => UiStrings.GetString(
        IsMonopitchMode ? "RoofGeometryWindow_MonopitchHeading" : "RoofGeometryWindow_Heading",
        _culture);

    public string WindowDescription => UiStrings.GetString(
        IsMonopitchMode ? "RoofGeometryWindow_MonopitchDescription" : "RoofGeometryWindow_Description",
        _culture);

    public string PrimaryActionText => UiStrings.GetString(
        _isEditMode ? "EditWindow_Apply" : "RoofGeometryWindow_Create",
        _culture);

    public string AlphaText
    {
        get => _alphaText;
        set => SetInput(ref _alphaText, value);
    }

    public string BetaText
    {
        get => _betaText;
        set => SetInput(ref _betaText, value);
    }

    public string EaveHeightDifferenceText
    {
        get => _eaveHeightDifferenceText;
        set => SetInput(ref _eaveHeightDifferenceText, value);
    }

    public string RidgeDistanceFromEaveAText
    {
        get => _ridgeDistanceFromEaveAText;
        set => SetInput(ref _ridgeDistanceFromEaveAText, value);
    }

    public AsymmetricGableInputMode AsymmetricInputMode
    {
        get => _asymmetricInputMode;
        set
        {
            var normalized = value == AsymmetricGableInputMode.RidgeDistanceFromEaveA
                ? AsymmetricGableInputMode.RidgeDistanceFromEaveA
                : AsymmetricGableInputMode.EaveHeightDifference;
            if (_asymmetricInputMode == normalized)
            {
                return;
            }

            if (_geometry is { } geometry)
            {
                if (normalized == AsymmetricGableInputMode.RidgeDistanceFromEaveA)
                {
                    SetCalculatedInput(
                        ref _ridgeDistanceFromEaveAText,
                        GetUiRunA(geometry),
                        nameof(RidgeDistanceFromEaveAText));
                }
                else
                {
                    SetCalculatedInput(
                        ref _eaveHeightDifferenceText,
                        GetUiDeltaHeight(geometry),
                        nameof(EaveHeightDifferenceText));
                }
            }

            _asymmetricInputMode = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDeltaHeightMode));
            OnPropertyChanged(nameof(IsRidgeDistanceMode));
            Recalculate();
        }
    }

    public bool IsDeltaHeightMode
    {
        get => AsymmetricInputMode == AsymmetricGableInputMode.EaveHeightDifference;
        set
        {
            if (value)
            {
                AsymmetricInputMode = AsymmetricGableInputMode.EaveHeightDifference;
            }
        }
    }

    public bool IsRidgeDistanceMode
    {
        get => AsymmetricInputMode == AsymmetricGableInputMode.RidgeDistanceFromEaveA;
        set
        {
            if (value)
            {
                AsymmetricInputMode = AsymmetricGableInputMode.RidgeDistanceFromEaveA;
            }
        }
    }

    public bool IsAsymmetryMirrored
    {
        get => _isAsymmetryMirrored;
        set
        {
            if (_isAsymmetryMirrored == value)
            {
                return;
            }

            _isAsymmetryMirrored = value;
            OnPropertyChanged();
            Recalculate();
        }
    }

    public MonopitchInputMode MonopitchInputMode
    {
        get => _monopitchInputMode;
        set
        {
            var normalized = value == MonopitchInputMode.HeightDifference
                ? MonopitchInputMode.HeightDifference
                : MonopitchInputMode.Slope;
            if (_monopitchInputMode == normalized)
            {
                return;
            }

            if (_monopitchGeometry is { } geometry)
            {
                if (normalized == MonopitchInputMode.HeightDifference)
                {
                    SetCalculatedInput(
                        ref _eaveHeightDifferenceText,
                        geometry.EaveHeightDifferenceMm,
                        nameof(EaveHeightDifferenceText));
                }
                else
                {
                    SetCalculatedSlope(geometry.SlopeDegrees);
                }
            }

            _monopitchInputMode = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMonopitchSlopeMode));
            OnPropertyChanged(nameof(IsMonopitchHeightMode));
            Recalculate();
        }
    }

    public bool IsMonopitchSlopeMode
    {
        get => MonopitchInputMode == MonopitchInputMode.Slope;
        set
        {
            if (value)
            {
                MonopitchInputMode = MonopitchInputMode.Slope;
            }
        }
    }

    public bool IsMonopitchHeightMode
    {
        get => MonopitchInputMode == MonopitchInputMode.HeightDifference;
        set
        {
            if (value)
            {
                MonopitchInputMode = MonopitchInputMode.HeightDifference;
            }
        }
    }

    public bool IsMonopitchMirrored
    {
        get => _isMonopitchMirrored;
        set
        {
            if (_isMonopitchMirrored == value)
            {
                return;
            }

            _isMonopitchMirrored = value;
            OnPropertyChanged();
            Recalculate();
        }
    }

    public bool HasRidgeDirection => _ridgeDirection is not null;

    public string RidgeDirectionText => _ridgeDirection is { } direction
        ? UiStrings.Format(
            UiStrings.GetString("RoofGeometryWindow_RidgeDirectionValueFormat", _culture),
            direction.X,
            direction.Y)
        : UiStrings.GetString("RoofGeometryWindow_RidgeDirectionNotSelected", _culture);

    public string OrientationDirectionText => _ridgeDirection is { } direction
        ? UiStrings.Format(
            UiStrings.GetString("RoofGeometryWindow_RidgeDirectionValueFormat", _culture),
            GetDisplayedOrientationDirection(direction).X,
            GetDisplayedOrientationDirection(direction).Y)
        : UiStrings.GetString(
            IsMonopitchMode
                ? "RoofGeometryWindow_SlopeDirectionNotSelected"
                : "RoofGeometryWindow_RidgeDirectionNotSelected",
            _culture);

    public string OrientationDirectionLabel => UiStrings.GetString(
        IsMonopitchMode
            ? "RoofGeometryWindow_SlopeDirection"
            : "RoofGeometryWindow_RidgeDirection",
        _culture);

    public string PickOrientationDirectionLabel => UiStrings.GetString(
        IsMonopitchMode
            ? "RoofGeometryWindow_PickSlopeDirection"
            : "RoofGeometryWindow_PickRidgeDirection",
        _culture);

    public string ValidationMessage => _validationMessage;

    public bool CanPreview => (_geometry is not null || _monopitchGeometry is not null) &&
        HasRidgeDirection;

    public bool CanApply => CanPreview;

    public string RunAText => IsMonopitchMode
        ? _monopitchGeometry is null ? "—" : FormatLength(_monopitchGeometry.SpanMm)
        : _geometry is null ? "—" : FormatLength(GetUiRunA(_geometry));

    public string RunBText => IsMonopitchMode
        ? "—"
        : _geometry is null ? "—" : FormatLength(GetUiRunB(_geometry));

    public string RidgePositionText => IsMonopitchMode
        ? _monopitchGeometry is null ? "—" : FormatLength(_monopitchGeometry.EaveHeightDifferenceMm)
        : _geometry is null ? "—" : FormatLength(GetUiRunA(_geometry));

    public string RidgeElevationText => IsMonopitchMode
        ? _monopitchGeometry is null ? "—" : FormatLength(_monopitchGeometry.SlopeDegrees)
        : _geometry is null ? "—" : FormatLength(GetUiRidgeHeightFromEaveA(_geometry));

    public string TransverseSpanText => IsMonopitchMode
        ? _monopitchGeometry is null ? "—" : FormatLength(_monopitchGeometry.SpanMm)
        : _geometry is null ? "—" : FormatLength(_geometry.Face0RunMm + _geometry.Face1RunMm);

    public GableRoofSectionState? SectionState => _sectionState;

    public MonopitchRoofSectionState? MonopitchSectionState => _monopitchSectionState;

    public void SetRidgeDirection(RoofDirection2D direction)
    {
        _ridgeDirection = direction;
        OnPropertyChanged(nameof(HasRidgeDirection));
        OnPropertyChanged(nameof(RidgeDirectionText));
        OnPropertyChanged(nameof(OrientationDirectionText));
        Recalculate();
    }

    private RoofDirection2D GetDisplayedOrientationDirection(RoofDirection2D direction) =>
        IsMonopitchMode
            ? MonopitchRoofDirectionPresentationRules.ToPhysicalHighToLow(direction)
            : direction;

    /// <summary>
    /// Edit-mode seeding: reconstructs the dialog from an existing physical roof so
    /// that an unchanged edit reproduces the exact persisted geometry. Seeds kind,
    /// both face slopes, the signed eave height difference and the PERSISTED ridge
    /// direction (never the footprint-derived fallback). The Mirror flag is UI-only
    /// and not persisted: seeding always picks the deterministic non-mirrored
    /// representation (UI α / Eave A = physical face 0).
    /// </summary>
    public void SeedFromExistingGeometry(IRoofGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (geometry is SimpleGableRoofGeometry gable)
        {
            SeedFromExistingGeometry(gable);
            return;
        }

        if (geometry is not MonopitchRoofGeometry monopitch || !_isMonopitchEditor)
        {
            throw new ArgumentException("The geometry does not match this editor.", nameof(geometry));
        }

        _selectedKind = RoofKind.Monopitch;
        _alphaText = FormatSeedSlope(monopitch.SlopeDegrees);
        _eaveHeightDifferenceText = Math.Round(monopitch.EaveHeightDifferenceMm)
            .ToString("0", _culture);
        _monopitchInputMode = MonopitchInputMode.Slope;
        _isMonopitchMirrored = false;
        _ridgeDirection = monopitch.LowToHighDirection;
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(IsMonopitchMode));
        OnPropertyChanged(nameof(AlphaText));
        OnPropertyChanged(nameof(EaveHeightDifferenceText));
        OnPropertyChanged(nameof(MonopitchInputMode));
        OnPropertyChanged(nameof(IsMonopitchSlopeMode));
        OnPropertyChanged(nameof(IsMonopitchHeightMode));
        OnPropertyChanged(nameof(IsMonopitchMirrored));
        OnPropertyChanged(nameof(HasRidgeDirection));
        OnPropertyChanged(nameof(RidgeDirectionText));
        OnPropertyChanged(nameof(OrientationDirectionText));
        Recalculate();
    }

    public void SeedFromExistingGeometry(SimpleGableRoofGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        _selectedKind = geometry.Kind == RoofKind.AsymmetricGable
            ? RoofKind.AsymmetricGable
            : RoofKind.SimpleGable;
        _alphaText = FormatSeedSlope(geometry.Face0SlopeDegrees);
        _betaText = FormatSeedSlope(geometry.Face1SlopeDegrees);
        _eaveHeightDifferenceText = Math.Round(geometry.EaveHeightDifferenceMm)
            .ToString("0", _culture);
        _asymmetricInputMode = AsymmetricGableInputMode.EaveHeightDifference;
        _isAsymmetryMirrored = false;
        _ridgeDirection = geometry.RidgeDirection;
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(IsSymmetricMode));
        OnPropertyChanged(nameof(IsAsymmetricMode));
        OnPropertyChanged(nameof(AlphaText));
        OnPropertyChanged(nameof(BetaText));
        OnPropertyChanged(nameof(EaveHeightDifferenceText));
        OnPropertyChanged(nameof(AsymmetricInputMode));
        OnPropertyChanged(nameof(IsDeltaHeightMode));
        OnPropertyChanged(nameof(IsRidgeDistanceMode));
        OnPropertyChanged(nameof(IsAsymmetryMirrored));
        OnPropertyChanged(nameof(HasRidgeDirection));
        OnPropertyChanged(nameof(RidgeDirectionText));
        OnPropertyChanged(nameof(OrientationDirectionText));
        Recalculate();
    }

    /// <summary>
    /// Round-trip seed formatting: the persisted value must parse back to the exact
    /// double, otherwise an unchanged edit would produce a different geometry
    /// signature and needlessly regenerate the generated set.
    /// </summary>
    private static string FormatSeedSlope(double degrees) =>
        degrees.ToString("R", CultureInfo.InvariantCulture);

    public bool TryGetRoofGeometry(out IRoofGeometry? geometry)
    {
        geometry = HasRidgeDirection
            ? IsMonopitchMode ? _monopitchGeometry : _geometry
            : null;
        return geometry is not null;
    }

    public bool TryGetGeometry(out SimpleGableRoofGeometry? geometry)
    {
        geometry = HasRidgeDirection && !IsMonopitchMode
            ? _geometry
            : null;
        return geometry is not null;
    }

    private void SetInput(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (string.Equals(field, normalized, StringComparison.Ordinal))
        {
            return;
        }
        field = normalized;
        OnPropertyChanged(propertyName);
        Recalculate();
    }

    private void Recalculate()
    {
        _geometry = null;
        _monopitchGeometry = null;
        _sectionState = null;
        _monopitchSectionState = null;
        if (IsMonopitchMode)
        {
            RecalculateMonopitch();
            return;
        }

        if (!TryParse(AlphaText, out var alpha) ||
            (IsAsymmetricMode && !TryParse(BetaText, out _)) ||
            (IsAsymmetricMode && IsDeltaHeightMode &&
                !TryParseWholeMillimeter(EaveHeightDifferenceText, out _)) ||
            (IsAsymmetricMode && IsRidgeDistanceMode &&
                !TryParseWholeMillimeter(RidgeDistanceFromEaveAText, out _)))
        {
            SetValidation("RoofGeometryWindow_ValidationNumber");
            NotifyCalculated();
            return;
        }

        var beta = IsAsymmetricMode && TryParse(BetaText, out var parsedBeta)
            ? parsedBeta
            : alpha;
        var direction = _ridgeDirection ?? _fallbackDirection;
        var uiDeltaHeight = 0d;
        if (IsAsymmetricMode && IsDeltaHeightMode)
        {
            _ = TryParseWholeMillimeter(EaveHeightDifferenceText, out uiDeltaHeight);
        }
        else if (IsAsymmetricMode)
        {
            _ = TryParseWholeMillimeter(RidgeDistanceFromEaveAText, out var enteredRunA);
            var neutralFace0Slope = IsAsymmetryMirrored ? beta : alpha;
            var neutralFace1Slope = IsAsymmetryMirrored ? alpha : beta;
            var neutral = RoofGeometrySolver.Solve(new RoofDefinition(
                _footprint,
                new RoofParameters(
                    neutralFace0Slope,
                    direction,
                    Face1SlopeDegrees: neutralFace1Slope,
                    EaveHeightDifferenceMm: 0d),
                RoofKind.AsymmetricGable));
            if (!neutral.IsValid || neutral.Geometry is not SimpleGableRoofGeometry neutralGeometry)
            {
                SetGeometryValidation(neutral.Error);
                NotifyCalculated();
                return;
            }

            var span = neutralGeometry.Face0RunMm + neutralGeometry.Face1RunMm;
            if (enteredRunA <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm ||
                enteredRunA >= span - SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
            {
                SetValidation("RoofGeometryWindow_ValidationRidgeDistance");
                NotifyCalculated();
                return;
            }

            var runB = span - enteredRunA;
            uiDeltaHeight = enteredRunA * Math.Tan(alpha * Math.PI / 180d) -
                runB * Math.Tan(beta * Math.PI / 180d);
            if (!double.IsFinite(uiDeltaHeight))
            {
                SetValidation("RoofGeometryWindow_ValidationRidgeDistance");
                NotifyCalculated();
                return;
            }
        }

        var physicalFace0Slope = IsAsymmetricMode && IsAsymmetryMirrored ? beta : alpha;
        var physicalFace1Slope = IsAsymmetricMode && IsAsymmetryMirrored ? alpha : beta;
        var physicalDeltaHeight = IsAsymmetricMode && IsAsymmetryMirrored
            ? -uiDeltaHeight
            : uiDeltaHeight;
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            _footprint,
            new RoofParameters(
                physicalFace0Slope,
                direction,
                Face1SlopeDegrees: physicalFace1Slope,
                EaveHeightDifferenceMm: physicalDeltaHeight),
            SelectedKind));
        if (!result.IsValid || result.Geometry is not SimpleGableRoofGeometry gableGeometry)
        {
            SetGeometryValidation(result.Error);
            NotifyCalculated();
            return;
        }

        _geometry = gableGeometry;
        if (IsAsymmetricMode && IsDeltaHeightMode)
        {
            SetCalculatedInput(
                ref _ridgeDistanceFromEaveAText,
                GetUiRunA(gableGeometry),
                nameof(RidgeDistanceFromEaveAText));
        }
        else if (IsAsymmetricMode)
        {
            SetCalculatedInput(
                ref _eaveHeightDifferenceText,
                GetUiDeltaHeight(gableGeometry),
                nameof(EaveHeightDifferenceText));
        }
        var uiRunA = GetUiRunA(gableGeometry);
        var uiRunB = GetUiRunB(gableGeometry);
        var eaveAElevation = IsAsymmetricMode && IsAsymmetryMirrored
            ? gableGeometry.EaveHeightDifferenceMm
            : 0d;
        var eaveBElevation = IsAsymmetricMode && IsAsymmetryMirrored
            ? 0d
            : gableGeometry.EaveHeightDifferenceMm;
        _sectionState = new GableRoofSectionState(
            gableGeometry.Face0RunMm + gableGeometry.Face1RunMm,
            uiRunA,
            uiRunB,
            eaveAElevation,
            eaveBElevation,
            gableGeometry.RiseMm,
            alpha,
            beta,
            IsAsymmetricMode,
            IsAsymmetricMode && IsAsymmetryMirrored,
            UiStrings.GetString("RoofGeometryWindow_EaveA", _culture),
            UiStrings.GetString("RoofGeometryWindow_EaveB", _culture),
            UiStrings.GetString("RoofGeometryWindow_Ridge", _culture),
            UiStrings.GetString("RoofGeometryWindow_TransverseSpan", _culture),
            _culture);
        SetValidation(HasRidgeDirection ? null : "RoofGeometryWindow_ValidationDirectionRequired");
        NotifyCalculated();
    }

    private void RecalculateMonopitch()
    {
        var requestedDirection = _ridgeDirection ?? _fallbackDirection;
        if (IsMonopitchMirrored &&
            !RoofDirection2D.TryCreate(
                -requestedDirection.X,
                -requestedDirection.Y,
                out requestedDirection))
        {
            SetValidation("RoofGeometryWindow_ValidationDirection");
            NotifyCalculated();
            return;
        }

        var probe = RoofGeometrySolver.Solve(new RoofDefinition(
            _footprint,
            new RoofParameters(30d, SlopeDirection: requestedDirection),
            RoofKind.Monopitch));
        if (!probe.IsValid || probe.Geometry is not MonopitchRoofGeometry probeGeometry)
        {
            SetGeometryValidation(probe.Error);
            NotifyCalculated();
            return;
        }

        double slope;
        double heightDifference;
        if (IsMonopitchSlopeMode)
        {
            if (!TryParse(AlphaText, out slope) ||
                !MonopitchRoofMath.TryCalculateHeightDifferenceMm(
                    probeGeometry.SpanMm,
                    slope,
                    out heightDifference))
            {
                SetValidation("RoofGeometryWindow_ValidationSlope");
                NotifyCalculated();
                return;
            }
        }
        else if (!TryParseWholeMillimeter(EaveHeightDifferenceText, out heightDifference) ||
                 !MonopitchRoofMath.TryCalculateSlopeDegrees(
                     probeGeometry.SpanMm,
                     heightDifference,
                     out slope))
        {
            SetValidation("RoofGeometryWindow_ValidationMonopitchHeight");
            NotifyCalculated();
            return;
        }

        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            _footprint,
            new RoofParameters(
                slope,
                EaveHeightDifferenceMm: heightDifference,
                SlopeDirection: requestedDirection),
            RoofKind.Monopitch));
        if (!result.IsValid || result.Geometry is not MonopitchRoofGeometry geometry)
        {
            SetGeometryValidation(result.Error);
            NotifyCalculated();
            return;
        }

        _monopitchGeometry = geometry;
        if (IsMonopitchSlopeMode)
        {
            SetCalculatedInput(
                ref _eaveHeightDifferenceText,
                geometry.EaveHeightDifferenceMm,
                nameof(EaveHeightDifferenceText));
        }
        else
        {
            SetCalculatedSlope(geometry.SlopeDegrees);
        }

        _monopitchSectionState = new MonopitchRoofSectionState(
            geometry.SpanMm,
            geometry.EaveHeightDifferenceMm,
            geometry.SlopeDegrees,
            IsMonopitchMirrored,
            UiStrings.GetString("RoofGeometryWindow_LowEave", _culture),
            UiStrings.GetString("RoofGeometryWindow_HighEave", _culture),
            UiStrings.GetString("RoofGeometryWindow_TransverseSpan", _culture),
            _culture);
        SetValidation(HasRidgeDirection
            ? null
            : "RoofGeometryWindow_ValidationSlopeDirectionRequired");
        NotifyCalculated();
    }

    private void SetValidation(string? resourceKey)
    {
        _validationMessage = resourceKey is null
            ? string.Empty
            : UiStrings.GetString(resourceKey, _culture);
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void SetGeometryValidation(SimpleGableRoofGeometryError error) =>
        SetValidation(error switch
        {
            SimpleGableRoofGeometryError.InvalidEaveHeightDifference =>
                IsRidgeDistanceMode
                    ? "RoofGeometryWindow_ValidationRidgeDistance"
                    : "RoofGeometryWindow_ValidationCombination",
            SimpleGableRoofGeometryError.RidgeDirectionCannotBeResolved =>
                "RoofGeometryWindow_ValidationDirection",
            _ => "RoofGeometryWindow_ValidationSlope",
        });

    private void SetCalculatedInput(ref string field, double value, string propertyName)
    {
        var text = Math.Round(value).ToString("0", _culture);
        if (string.Equals(field, text, StringComparison.Ordinal))
        {
            return;
        }

        field = text;
        OnPropertyChanged(propertyName);
    }

    private void SetCalculatedSlope(double value)
    {
        var text = value.ToString("0.###############", _culture);
        if (string.Equals(_alphaText, text, StringComparison.Ordinal))
        {
            return;
        }

        _alphaText = text;
        OnPropertyChanged(nameof(AlphaText));
    }

    private void NotifyCalculated()
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(RunAText));
        OnPropertyChanged(nameof(RunBText));
        OnPropertyChanged(nameof(RidgePositionText));
        OnPropertyChanged(nameof(RidgeElevationText));
        OnPropertyChanged(nameof(TransverseSpanText));
        OnPropertyChanged(nameof(SectionState));
        OnPropertyChanged(nameof(MonopitchSectionState));
    }

    private bool TryParse(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, _culture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value);

    private bool TryParseWholeMillimeter(string text, out double value)
    {
        if (!TryParse(text, out value))
        {
            return false;
        }

        return Math.Abs(value - Math.Round(value)) <=
            SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;
    }

    private double GetUiRunA(SimpleGableRoofGeometry geometry) =>
        IsAsymmetricMode && IsAsymmetryMirrored
            ? geometry.Face1RunMm
            : geometry.Face0RunMm;

    private double GetUiRunB(SimpleGableRoofGeometry geometry) =>
        IsAsymmetricMode && IsAsymmetryMirrored
            ? geometry.Face0RunMm
            : geometry.Face1RunMm;

    private double GetUiDeltaHeight(SimpleGableRoofGeometry geometry) =>
        IsAsymmetricMode && IsAsymmetryMirrored
            ? -geometry.EaveHeightDifferenceMm
            : geometry.EaveHeightDifferenceMm;

    private double GetUiRidgeHeightFromEaveA(SimpleGableRoofGeometry geometry) =>
        IsAsymmetricMode && IsAsymmetryMirrored
            ? geometry.RiseMm - geometry.EaveHeightDifferenceMm
            : geometry.RiseMm;

    private string FormatLength(double value) =>
        UiStrings.Format(
            UiStrings.GetString("RoofGeometryWindow_LengthValueFormat", _culture),
            value);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record GableRoofSectionState(
    double SpanMm,
    double RunAMm,
    double RunBMm,
    double EaveAElevationMm,
    double EaveBElevationMm,
    double RidgeElevationMm,
    double AlphaDegrees,
    double BetaDegrees,
    bool IsAsymmetric,
    bool IsMirrored,
    string EaveALabel,
    string EaveBLabel,
    string RidgeLabel,
    string SpanLabel,
    CultureInfo Culture);

internal enum AsymmetricGableInputMode
{
    EaveHeightDifference = 0,
    RidgeDistanceFromEaveA = 1,
}

internal enum MonopitchInputMode
{
    Slope = 0,
    HeightDifference = 1,
}

public sealed record MonopitchRoofSectionState(
    double SpanMm,
    double HeightDifferenceMm,
    double SlopeDegrees,
    bool IsMirrored,
    string LowEaveLabel,
    string HighEaveLabel,
    string SpanLabel,
    CultureInfo Culture);
