namespace AcKrovy.Localization;

public static class ReportMaterialDisplayFormatter
{
    public const string DescriptionSeparator = " – ";
    public const string ReportLineBreak = "\n";

    public static string FormatMaterialForReport(string? displayName)
    {
        if (displayName is null)
        {
            return string.Empty;
        }

        if (displayName.Length == 0)
        {
            return displayName;
        }

        var separatorIndex = displayName.IndexOf(
            DescriptionSeparator,
            StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return displayName;
        }

        var leftPart = displayName.Substring(0, separatorIndex).Trim();
        var rightPart = displayName
            .Substring(separatorIndex + DescriptionSeparator.Length)
            .Trim();
        if (leftPart.Length == 0 || rightPart.Length == 0)
        {
            return displayName;
        }

        return leftPart + ReportLineBreak + rightPart;
    }
}
