using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Builds type-specific elevation image-choice sets.
/// PNGs are optional; when missing, the UI shows a ~64x64 placeholder.
/// Future assets may be 128x128 or 160x160 and are displayed at RecommendedImageSizePx.
/// Future element types plug in different ImageKey / PNG packs without a new window.
/// </summary>
internal static class ElevationImageChoiceCatalog
{
    /// <summary>Display size (px) for elevation choice image slots. Larger assets scale into this slot.</summary>
    public const double RecommendedImageSizePx = 64d;

    public static IReadOnlyList<ElevationImageChoiceItem> CreateReferenceChoices(
        TimberElementType elementType,
        StructuralMemberElevationReferenceKind selected)
    {
        // Ordinary is the only pack today; other types will add their own ImageKeys.
        _ = elementType;
        return
        [
            Create(
                id: "ref-sh",
                title: "SH",
                imageKey: ImageKey(elementType, "Reference", "SH"),
                payload: StructuralMemberElevationReferenceKind.SH,
                selected: selected == StructuralMemberElevationReferenceKind.SH,
                tooltipKey: "RoofOrdinaryElevation_SH_Tooltip"),
            Create(
                id: "ref-os",
                title: "OS",
                imageKey: ImageKey(elementType, "Reference", "OS"),
                payload: StructuralMemberElevationReferenceKind.OS,
                selected: selected == StructuralMemberElevationReferenceKind.OS,
                tooltipKey: "RoofOrdinaryElevation_OS_Tooltip"),
            Create(
                id: "ref-vh",
                title: "VH",
                imageKey: ImageKey(elementType, "Reference", "VH"),
                payload: StructuralMemberElevationReferenceKind.VH,
                selected: selected == StructuralMemberElevationReferenceKind.VH,
                tooltipKey: "RoofOrdinaryElevation_VH_Tooltip"),
        ];
    }

    public static IReadOnlyList<ElevationImageChoiceItem> CreateCalculationChoices(
        TimberElementType elementType,
        StructuralMemberElevationCalculationMode selected)
    {
        _ = elementType;
        return
        [
            Create(
                id: "mode-lower-upper",
                title: UiStrings.GetString("RoofOrdinaryElevation_Mode_LowerUpper"),
                imageKey: ImageKey(elementType, "CalcMode", "LowerUpper"),
                payload: StructuralMemberElevationCalculationMode.LowerUpper,
                selected: selected == StructuralMemberElevationCalculationMode.LowerUpper),
            Create(
                id: "mode-lower-slope",
                title: UiStrings.GetString("RoofOrdinaryElevation_Mode_LowerSlope"),
                imageKey: ImageKey(elementType, "CalcMode", "LowerSlope"),
                payload: StructuralMemberElevationCalculationMode.LowerSlope,
                selected: selected == StructuralMemberElevationCalculationMode.LowerSlope),
            Create(
                id: "mode-upper-slope",
                title: UiStrings.GetString("RoofOrdinaryElevation_Mode_UpperSlope"),
                imageKey: ImageKey(elementType, "CalcMode", "UpperSlope"),
                payload: StructuralMemberElevationCalculationMode.UpperSlope,
                selected: selected == StructuralMemberElevationCalculationMode.UpperSlope),
        ];
    }

    /// <summary>
    /// Resolves a pack URI for a future PNG. Returns null until assets exist
    /// under UI/Assets/Elevation/{Ordinary|...}/{key}.png.
    /// </summary>
    public static ImageSource? TryResolveImage(string imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey)) return null;
        // Convention: Elevation.Ordinary.Reference.SH → UI/Assets/Elevation/Ordinary/Reference/SH.png
        var parts = imageKey.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 ||
            !string.Equals(parts[0], "Elevation", StringComparison.OrdinalIgnoreCase))
            return null;

        var relative = string.Join("/", parts.Skip(1)) + ".png";
        var uri = new Uri(
            $"/AcKrovy.AutoCAD;component/UI/Assets/Elevation/{relative}",
            UriKind.RelativeOrAbsolute);
        try
        {
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info is null) return null;
            info.Stream.Dispose();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static string ImageKey(TimberElementType elementType, string group, string option) =>
        $"Elevation.{PackFolder(elementType)}.{group}.{option}";

    private static string PackFolder(TimberElementType elementType) => elementType switch
    {
        TimberElementType.Rafter => "Ordinary",
        TimberElementType.Purlin => "Purlin",
        TimberElementType.WallPlate => "WallPlate",
        TimberElementType.CollarTie => "CollarTie",
        TimberElementType.Brace => "Brace",
        TimberElementType.TieBeam => "TieBeam",
        TimberElementType.Post => "Post",
        _ => "Generic",
    };

    private static ElevationImageChoiceItem Create(
        string id,
        string title,
        string imageKey,
        object payload,
        bool selected,
        string? tooltipKey = null)
    {
        _ = tooltipKey; // reserved for ToolTip binding when cards are built in XAML
        var item = new ElevationImageChoiceItem
        {
            Id = id,
            Title = title,
            ImageKey = imageKey,
            Payload = payload,
            IsSelected = selected,
            Image = TryResolveImage(imageKey),
        };
        return item;
    }
}
