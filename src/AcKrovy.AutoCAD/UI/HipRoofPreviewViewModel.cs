using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>Uniform-pitch, preview-only input for the general Hip topology.</summary>
internal sealed class HipRoofPreviewViewModel : INotifyPropertyChanged
{
    private readonly RoofFootprint _footprint;
    private readonly CultureInfo _culture;
    private string _slopeText = "30";
    private string _validationMessage = string.Empty;
    private HipRoofGeometry? _geometry;

    internal HipRoofPreviewViewModel(RoofFootprint footprint, CultureInfo? culture = null)
    {
        _footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        _culture = culture ?? AppLanguageService.CurrentUiCulture;
        Recalculate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle => UiStrings.GetString("CommandUi_RoofHip_Label", _culture);
    public string WindowDescription => UiStrings.GetString("CommandUi_RoofHip_Tooltip", _culture);

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

    public string ValidationMessage => _validationMessage;
    public bool CanPreview => _geometry is not null;

    internal bool TryGetRoofGeometry(out HipRoofGeometry? geometry)
    {
        geometry = _geometry;
        return geometry is not null;
    }

    private void Recalculate()
    {
        _geometry = null;
        if (!TryParseSlope(SlopeText, out var slope))
        {
            SetValidation("RoofGeometryWindow_ValidationNumber");
            return;
        }

        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            _footprint,
            new RoofParameters(slope),
            RoofKind.Hip));
        _geometry = result.IsValid ? result.Geometry as HipRoofGeometry : null;
        SetValidation(_geometry is not null
            ? null
            : result.Error == SimpleGableRoofGeometryError.InvalidSlope
                ? "RoofGeometryWindow_ValidationSlope"
                : "RoofGeometryWindow_ValidationHipTopology");

#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[AK_ROOF_HIP] solve success={result.IsValid} error={result.Error} " +
            $"vertices={_footprint.Vertices.Count} nodes={_geometry?.Topology.Nodes.Count ?? 0}");
#endif
    }

    private bool TryParseSlope(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, _culture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value);

    private void SetValidation(string? resourceKey)
    {
        _validationMessage = resourceKey is null
            ? string.Empty
            : UiStrings.GetString(resourceKey, _culture);
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanPreview));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
