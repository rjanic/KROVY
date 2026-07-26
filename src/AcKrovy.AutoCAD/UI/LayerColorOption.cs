using System.Globalization;
using System.Windows.Media;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Localization;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace AcKrovy.AutoCAD.UI;

public sealed record LayerColorOption(
    int Index,
    string Label,
    string ToolTip,
    WpfBrush Brush)
{
    public string TechnicalLabel => $"ACI {Index}";

    public static IReadOnlyList<LayerColorOption> CreateAll(CultureInfo? culture = null) =>
        AciColorPalette.Indices.Select(index => Create(index, culture)).ToArray();

    public static LayerColorOption Create(int index, CultureInfo? culture = null)
    {
        var rgb = AciColorPalette.GetRgb(index);
        var brush = new SolidColorBrush(WpfColor.FromRgb(rgb.Red, rgb.Green, rgb.Blue));
        brush.Freeze();
        var label = LayerColorDisplayNameProvider.GetDisplayName(index, culture);
        var tooltip = index is >= 1 and <= 6
            ? $"{label} · ACI {index}"
            : $"ACI {index}";
        return new LayerColorOption(index, label, tooltip, brush);
    }
}
