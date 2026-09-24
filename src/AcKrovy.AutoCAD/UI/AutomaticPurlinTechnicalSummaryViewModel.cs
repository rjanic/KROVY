using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Shared Strešná rovina / Spodná / Os / Horná strip values for wall-plate,
/// ridge, and intermediate purlin cards.
/// </summary>
internal sealed class AutomaticPurlinTechnicalSummaryViewModel : INotifyPropertyChanged
{
    private readonly CultureInfo _culture;
    private readonly Func<string, string> _text;
    private readonly RoofAutomaticPurlinGeneratorRole _role;
    private string _roofPlaneRelative = "—";
    private string _bottomRelative = "—";
    private string _centerRelative = "—";
    private string _topRelative = "—";
    private AutomaticPurlinElevationTooltipViewModel _roofPlaneTooltip =
        AutomaticPurlinElevationTooltipViewModel.Empty;
    private AutomaticPurlinElevationTooltipViewModel _bottomTooltip =
        AutomaticPurlinElevationTooltipViewModel.Empty;
    private AutomaticPurlinElevationTooltipViewModel _centerTooltip =
        AutomaticPurlinElevationTooltipViewModel.Empty;
    private AutomaticPurlinElevationTooltipViewModel _topTooltip =
        AutomaticPurlinElevationTooltipViewModel.Empty;

    public AutomaticPurlinTechnicalSummaryViewModel(
        RoofAutomaticPurlinGeneratorRole role,
        CultureInfo culture,
        Func<string, string> text)
    {
        _role = role;
        _culture = culture ?? CultureInfo.InvariantCulture;
        _text = text ?? throw new ArgumentNullException(nameof(text));
        RefreshTooltips();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RoofPlaneRelative => _roofPlaneRelative;
    public string BottomRelative => _bottomRelative;
    public string CenterRelative => _centerRelative;
    public string TopRelative => _topRelative;
    public AutomaticPurlinElevationTooltipViewModel RoofPlaneTooltip => _roofPlaneTooltip;
    public AutomaticPurlinElevationTooltipViewModel BottomTooltip => _bottomTooltip;
    public AutomaticPurlinElevationTooltipViewModel CenterTooltip => _centerTooltip;
    public AutomaticPurlinElevationTooltipViewModel TopTooltip => _topTooltip;

    public void Clear() => UpdateFrom(null);

    public void UpdateFrom(RoofAutomaticPurlinPlanItem? planItem) =>
        UpdateFrom(planItem, roofPlaneRelativeMm: null);

    /// <param name="roofPlaneRelativeMm">
    /// Authoritative Strešná rovina relative to EffectiveDatum (physical upper face at axis).
    /// When null, roof-plane text clears to "—" (do not invent from mid/lower/seating/SVG).
    /// </param>
    public void UpdateFrom(RoofAutomaticPurlinPlanItem? planItem, double? roofPlaneRelativeMm)
    {
        var profile = planItem?.ElevationProfile;
        _roofPlaneRelative = roofPlaneRelativeMm is { } roofPlaneMm && double.IsFinite(roofPlaneMm)
            ? RoofRelativeElevationDatumRules.FormatMetres(roofPlaneMm, _culture)
            : "—";
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
        RefreshTooltips();
        OnPropertyChanged(nameof(RoofPlaneRelative));
        OnPropertyChanged(nameof(BottomRelative));
        OnPropertyChanged(nameof(CenterRelative));
        OnPropertyChanged(nameof(TopRelative));
        OnPropertyChanged(nameof(RoofPlaneTooltip));
        OnPropertyChanged(nameof(BottomTooltip));
        OnPropertyChanged(nameof(CenterTooltip));
        OnPropertyChanged(nameof(TopTooltip));
    }

    private void RefreshTooltips()
    {
        _roofPlaneTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            _role,
            AutomaticPurlinElevationMetric.RoofPlane,
            _roofPlaneRelative,
            _text);
        _bottomTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            _role,
            AutomaticPurlinElevationMetric.Bottom,
            _bottomRelative,
            _text);
        _centerTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            _role,
            AutomaticPurlinElevationMetric.Center,
            _centerRelative,
            _text);
        _topTooltip = AutomaticPurlinElevationTooltipFactory.Create(
            _role,
            AutomaticPurlinElevationMetric.Top,
            _topRelative,
            _text);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

