using System.Globalization;
using System.Resources;

namespace AcKrovy.Localization;

public static class UiStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "AcKrovy.Localization.Resources.UiStrings",
        typeof(UiStrings).Assembly);

    public static string ReportTitle => GetString("Report_Title");
    public static string ReportColumnItem => GetString("Report_Column_Item");
    public static string ReportColumnType => GetString("Report_Column_Type");
    public static string ReportColumnMaterial => GetString("Report_Column_Material");
    public static string ReportColumnWidthMm => GetString("Report_Column_WidthMm");
    public static string ReportColumnHeightMm => GetString("Report_Column_HeightMm");
    public static string ReportColumnPieceLengthM => GetString("Report_Column_PieceLengthM");
    public static string ReportColumnCount => GetString("Report_Column_Count");
    public static string ReportColumnTotalLengthM => GetString("Report_Column_TotalLengthM");
    public static string ReportColumnVolumeM3 => GetString("Report_Column_VolumeM3");
    public static string ReportTotalFormat => GetString("Report_TotalFormat");

    public static string GetString(string resourceKey, CultureInfo? culture = null) =>
        ResourceManager.GetString(resourceKey, culture ?? CultureInfo.CurrentUICulture) ?? resourceKey;
}
