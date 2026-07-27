using System.Globalization;
using AcKrovy.Core.Models;

namespace AcKrovy.Localization;

public static class TimberCsvLocalizationProvider
{
    public static TimberCsvLocalization Create(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        return new TimberCsvLocalization(
            new TimberCsvHeaders(
                UiStrings.GetString("Csv_Header_ElementId", culture),
                UiStrings.GetString("Csv_Header_ElementType", culture),
                UiStrings.GetString("Csv_Header_Material", culture),
                UiStrings.GetString("Csv_Header_WidthMm", culture),
                UiStrings.GetString("Csv_Header_HeightMm", culture),
                UiStrings.GetString("Csv_Header_CuttingLengthMm", culture),
                UiStrings.GetString("Csv_Header_Quantity", culture),
                UiStrings.GetString("Csv_Header_TotalLengthM", culture),
                UiStrings.GetString("Csv_Header_VolumeM3", culture),
                UiStrings.GetString("Csv_Header_Note", culture)),
            (type, customName) =>
                type == TimberElementType.Custom &&
                !string.IsNullOrWhiteSpace(customName)
                    ? customName!
                    : TimberElementTypeDisplayNameProvider.GetDisplayName(type, culture),
            material => TimberMaterialDisplayNameProvider.GetDisplayName(material, culture));
    }
}
