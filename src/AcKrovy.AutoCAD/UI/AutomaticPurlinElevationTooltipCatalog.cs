using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.AutoCAD.UI;

internal enum AutomaticPurlinElevationMetric
{
    Bottom,
    Center,
    Top,
    RoofPlane,
}

/// <summary>
/// Maps WallPlate / Intermediate / Ridge × Spodná / Os / Horná / Strešná rovina
/// onto the 12 embedded tooltip detail images.
/// </summary>
internal static class AutomaticPurlinElevationTooltipCatalog
{
    private const string PackBase =
        "pack://application:,,,/AcKrovy.AutoCAD;component/UI/Assets/RoofPurlins/Tooltips/";

    internal static readonly IReadOnlyList<(
        RoofAutomaticPurlinGeneratorRole Role,
        AutomaticPurlinElevationMetric Metric,
        string FileName)> AllEntries =
    [
        (RoofAutomaticPurlinGeneratorRole.WallPlate, AutomaticPurlinElevationMetric.Bottom, "tooltip_wallplate_spodna.png"),
        (RoofAutomaticPurlinGeneratorRole.WallPlate, AutomaticPurlinElevationMetric.Center, "tooltip_wallplate_os.png"),
        (RoofAutomaticPurlinGeneratorRole.WallPlate, AutomaticPurlinElevationMetric.Top, "tooltip_wallplate_horna.png"),
        (RoofAutomaticPurlinGeneratorRole.WallPlate, AutomaticPurlinElevationMetric.RoofPlane, "tooltip_wallplate_stresna_rovina.png"),
        (RoofAutomaticPurlinGeneratorRole.Intermediate, AutomaticPurlinElevationMetric.Bottom, "tooltip_intermediate_spodna.png"),
        (RoofAutomaticPurlinGeneratorRole.Intermediate, AutomaticPurlinElevationMetric.Center, "tooltip_intermediate_os.png"),
        (RoofAutomaticPurlinGeneratorRole.Intermediate, AutomaticPurlinElevationMetric.Top, "tooltip_intermediate_horna.png"),
        (RoofAutomaticPurlinGeneratorRole.Intermediate, AutomaticPurlinElevationMetric.RoofPlane, "tooltip_intermediate_stresna_rovina.png"),
        (RoofAutomaticPurlinGeneratorRole.Ridge, AutomaticPurlinElevationMetric.Bottom, "tooltip_ridge_spodna.png"),
        (RoofAutomaticPurlinGeneratorRole.Ridge, AutomaticPurlinElevationMetric.Center, "tooltip_ridge_os.png"),
        (RoofAutomaticPurlinGeneratorRole.Ridge, AutomaticPurlinElevationMetric.Top, "tooltip_ridge_horna.png"),
        (RoofAutomaticPurlinGeneratorRole.Ridge, AutomaticPurlinElevationMetric.RoofPlane, "tooltip_ridge_stresna_rovina.png"),
    ];

    internal static string GetFileName(
        RoofAutomaticPurlinGeneratorRole role,
        AutomaticPurlinElevationMetric metric)
    {
        foreach (var entry in AllEntries)
        {
            if (entry.Role == role && entry.Metric == metric)
            {
                return entry.FileName;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(metric),
            $"No tooltip image for {role}/{metric}.");
    }

    internal static string GetPackUriString(
        RoofAutomaticPurlinGeneratorRole role,
        AutomaticPurlinElevationMetric metric) =>
        PackBase + GetFileName(role, metric);

    internal static Uri GetPackUri(
        RoofAutomaticPurlinGeneratorRole role,
        AutomaticPurlinElevationMetric metric) =>
        new(GetPackUriString(role, metric), UriKind.RelativeOrAbsolute);

    internal static bool TryCreateBitmapImage(
        RoofAutomaticPurlinGeneratorRole role,
        AutomaticPurlinElevationMetric metric,
        out BitmapImage? image)
    {
        image = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(GetPackUriString(role, metric), UriKind.RelativeOrAbsolute);
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string TitleKey(AutomaticPurlinElevationMetric metric) =>
        metric switch
        {
            AutomaticPurlinElevationMetric.Bottom => "AutomaticPurlin_BottomEdge",
            AutomaticPurlinElevationMetric.Center => "AutomaticPurlin_Center",
            AutomaticPurlinElevationMetric.Top => "AutomaticPurlin_TopEdge",
            AutomaticPurlinElevationMetric.RoofPlane => "AutomaticPurlin_RoofPlaneElevation",
            _ => "AutomaticPurlin_RoofPlaneElevation",
        };

    internal static string DescriptionKey(
        RoofAutomaticPurlinGeneratorRole role,
        AutomaticPurlinElevationMetric metric) =>
        metric switch
        {
            AutomaticPurlinElevationMetric.Bottom =>
                "AutomaticPurlin_Tooltip_BottomDescription",
            AutomaticPurlinElevationMetric.Center =>
                "AutomaticPurlin_Tooltip_CenterDescription",
            AutomaticPurlinElevationMetric.Top =>
                "AutomaticPurlin_Tooltip_TopDescription",
            AutomaticPurlinElevationMetric.RoofPlane when role == RoofAutomaticPurlinGeneratorRole.Ridge =>
                "AutomaticPurlin_Tooltip_RoofPlaneRidgeDescription",
            AutomaticPurlinElevationMetric.RoofPlane =>
                "AutomaticPurlin_Tooltip_RoofPlaneDescription",
            _ => "AutomaticPurlin_Tooltip_RoofPlaneDescription",
        };
}

internal sealed class AutomaticPurlinElevationTooltipViewModel
{
    public AutomaticPurlinElevationTooltipViewModel(
        ImageSource? image,
        string title,
        string description,
        string currentValueLine)
    {
        Image = image;
        Title = title;
        Description = description;
        CurrentValueLine = currentValueLine;
    }

    public ImageSource? Image { get; }
    public string Title { get; }
    public string Description { get; }
    public string CurrentValueLine { get; }

    public static AutomaticPurlinElevationTooltipViewModel Empty { get; } =
        new(null, string.Empty, string.Empty, string.Empty);
}

internal static class AutomaticPurlinElevationTooltipFactory
{
    public static AutomaticPurlinElevationTooltipViewModel Create(
        RoofAutomaticPurlinGeneratorRole role,
        AutomaticPurlinElevationMetric metric,
        string formattedRelativeValue,
        Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        AutomaticPurlinElevationTooltipCatalog.TryCreateBitmapImage(role, metric, out var image);
        var valueText = string.IsNullOrWhiteSpace(formattedRelativeValue) ||
                        string.Equals(formattedRelativeValue, "—", StringComparison.Ordinal)
            ? "—"
            : formattedRelativeValue.EndsWith(" m", StringComparison.Ordinal)
                ? formattedRelativeValue
                : formattedRelativeValue + " m";
        var currentLine = string.Format(
            text("AutomaticPurlin_Tooltip_CurrentValueFormat"),
            valueText);
        return new AutomaticPurlinElevationTooltipViewModel(
            image,
            text(AutomaticPurlinElevationTooltipCatalog.TitleKey(metric)),
            text(AutomaticPurlinElevationTooltipCatalog.DescriptionKey(role, metric)),
            currentLine);
    }
}

