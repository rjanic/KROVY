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
    private RoofKind _selectedKind;
    private string _alphaText = "30";
    private string _betaText = "35";
    private string _eaveHeightDifferenceText = "0";
    private string _ridgeDistanceFromEaveAText = string.Empty;
    private AsymmetricGableInputMode _asymmetricInputMode = AsymmetricGableInputMode.EaveHeightDifference;
    private bool _isAsymmetryMirrored;
    private RoofDirection2D? _ridgeDirection;
    private SimpleGableRoofGeometry? _geometry;
    private GableRoofSectionState? _sectionState;
    private string _validationMessage = string.Empty;

    public GableRoofGeometryViewModel(
        RoofFootprint footprint,
        RoofKind initialKind = RoofKind.SimpleGable,
        CultureInfo? culture = null)
    {
        _footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        if (footprint.Vertices.Count != 4)
        {
            throw new ArgumentException("A rectangular footprint is required.", nameof(footprint));
        }

        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        _selectedKind = initialKind == RoofKind.AsymmetricGable
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
            var normalized = value == RoofKind.AsymmetricGable
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

    public bool HasRidgeDirection => _ridgeDirection is not null;

    public string RidgeDirectionText => _ridgeDirection is { } direction
        ? UiStrings.Format(
            UiStrings.GetString("RoofGeometryWindow_RidgeDirectionValueFormat", _culture),
            direction.X,
            direction.Y)
        : UiStrings.GetString("RoofGeometryWindow_RidgeDirectionNotSelected", _culture);

    public string ValidationMessage => _validationMessage;

    public bool CanPreview => _geometry is not null && HasRidgeDirection;

    public bool CanApply => CanPreview;

    public string RunAText => _geometry is null ? "—" : FormatLength(GetUiRunA(_geometry));

    public string RunBText => _geometry is null ? "—" : FormatLength(GetUiRunB(_geometry));

    public string RidgePositionText => _geometry is null ? "—" : FormatLength(GetUiRunA(_geometry));

    public string RidgeElevationText => _geometry is null
        ? "—"
        : FormatLength(GetUiRidgeHeightFromEaveA(_geometry));

    public string TransverseSpanText => _geometry is null
        ? "—"
        : FormatLength(_geometry.Face0RunMm + _geometry.Face1RunMm);

    public GableRoofSectionState? SectionState => _sectionState;

    public void SetRidgeDirection(RoofDirection2D direction)
    {
        _ridgeDirection = direction;
        OnPropertyChanged(nameof(HasRidgeDirection));
        OnPropertyChanged(nameof(RidgeDirectionText));
        Recalculate();
    }

    /// <summary>
    /// Edit-mode seeding: reconstructs the dialog from an existing physical roof so
    /// that an unchanged edit reproduces the exact persisted geometry. Seeds kind,
    /// both face slopes, the signed eave height difference and the PERSISTED ridge
    /// direction (never the footprint-derived fallback). The Mirror flag is UI-only
    /// and not persisted: seeding always picks the deterministic non-mirrored
    /// representation (UI α / Eave A = physical face 0).
    /// </summary>
    public void SeedFromExistingGeometry(SimpleGableRoofGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
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
        Recalculate();
    }

    /// <summary>
    /// Round-trip seed formatting: the persisted value must parse back to the exact
    /// double, otherwise an unchanged edit would produce a different geometry
    /// signature and needlessly regenerate the generated set.
    /// </summary>
    private static string FormatSeedSlope(double degrees) =>
        degrees.ToString("R", CultureInfo.InvariantCulture);

    public bool TryGetGeometry(out SimpleGableRoofGeometry? geometry)
    {
        geometry = HasRidgeDirection ? _geometry : null;
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
        _sectionState = null;
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
            if (!neutral.IsValid || neutral.Geometry is null)
            {
                SetGeometryValidation(neutral.Error);
                NotifyCalculated();
                return;
            }

            var span = neutral.Geometry.Face0RunMm + neutral.Geometry.Face1RunMm;
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
        if (!result.IsValid || result.Geometry is null)
        {
            SetGeometryValidation(result.Error);
            NotifyCalculated();
            return;
        }

        _geometry = result.Geometry;
        if (IsAsymmetricMode && IsDeltaHeightMode)
        {
            SetCalculatedInput(
                ref _ridgeDistanceFromEaveAText,
                GetUiRunA(result.Geometry),
                nameof(RidgeDistanceFromEaveAText));
        }
        else if (IsAsymmetricMode)
        {
            SetCalculatedInput(
                ref _eaveHeightDifferenceText,
                GetUiDeltaHeight(result.Geometry),
                nameof(EaveHeightDifferenceText));
        }
        var uiRunA = GetUiRunA(result.Geometry);
        var uiRunB = GetUiRunB(result.Geometry);
        var eaveAElevation = IsAsymmetricMode && IsAsymmetryMirrored
            ? result.Geometry.EaveHeightDifferenceMm
            : 0d;
        var eaveBElevation = IsAsymmetricMode && IsAsymmetryMirrored
            ? 0d
            : result.Geometry.EaveHeightDifferenceMm;
        _sectionState = new GableRoofSectionState(
            result.Geometry.Face0RunMm + result.Geometry.Face1RunMm,
            uiRunA,
            uiRunB,
            eaveAElevation,
            eaveBElevation,
            result.Geometry.RiseMm,
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
