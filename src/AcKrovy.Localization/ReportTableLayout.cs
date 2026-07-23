namespace AcKrovy.Localization;

public static class ReportTableLayout
{
    public const int ColumnCount = 9;

    public const int ItemColumn = 0;
    public const int TypeColumn = 1;
    public const int MaterialColumn = 2;
    public const int WidthColumn = 3;
    public const int HeightColumn = 4;
    public const int PieceLengthColumn = 5;
    public const int CountColumn = 6;
    public const int TotalLengthColumn = 7;
    public const int VolumeColumn = 8;

    public const double DefaultRowHeight = 8d;
    public const double MaximumDataRowHeight = 12d;

    public const double ConservativeCharacterWidth = 4.5d;
    public const double HorizontalCellPadding = 8d;

    public const double MinimumTypeColumnWidth = 42d;
    public const double MinimumMaterialColumnWidth = 48d;

    private static readonly IReadOnlyList<double> StableColumnWidths =
    [
        18d,
        MinimumTypeColumnWidth,
        MinimumMaterialColumnWidth,
        20d,
        20d,
        27d,
        18d,
        30d,
        28d,
    ];

    public static IReadOnlyList<double> ColumnWidths => StableColumnWidths;

    public static IReadOnlyList<double> GetColumnWidths(
        IEnumerable<string> displayedTypeNames,
        IEnumerable<string> displayedMaterialNames)
    {
        if (displayedTypeNames is null)
        {
            throw new ArgumentNullException(nameof(displayedTypeNames));
        }

        if (displayedMaterialNames is null)
        {
            throw new ArgumentNullException(nameof(displayedMaterialNames));
        }

        var widths = StableColumnWidths.ToArray();
        widths[TypeColumn] = CalculateMinimumTextColumnWidth(
            displayedTypeNames,
            widths[TypeColumn]);
        widths[MaterialColumn] = CalculateMinimumTextColumnWidth(
            displayedMaterialNames,
            widths[MaterialColumn]);
        return widths;
    }

    public static double CalculateMinimumTextColumnWidth(
        IEnumerable<string> localizedValues,
        double baseWidth)
    {
        if (localizedValues is null)
        {
            throw new ArgumentNullException(nameof(localizedValues));
        }

        var requiredCharacterCapacity = localizedValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(GetRequiredCharacterCapacityForAtMostTwoLines)
            .DefaultIfEmpty(0)
            .Max();

        var contentWidth =
            (requiredCharacterCapacity * ConservativeCharacterWidth) +
            HorizontalCellPadding;
        return Math.Max(baseWidth, Math.Ceiling(contentWidth));
    }

    public static int GetLongestUnbreakableTokenLength(IEnumerable<string> localizedValues)
    {
        if (localizedValues is null)
        {
            throw new ArgumentNullException(nameof(localizedValues));
        }

        return localizedValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(GetTokens)
            .Select(token => token.Length)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static int GetRequiredCharacterCapacityForAtMostTwoLines(string value)
    {
        var explicitLines = value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
        if (explicitLines.Length > 1)
        {
            return explicitLines
                .Select(line => line.Length)
                .DefaultIfEmpty(0)
                .Max();
        }

        var tokens = GetTokens(value);
        if (tokens.Length == 0)
        {
            return 0;
        }

        var longestToken = tokens.Max(token => token.Length);
        if (tokens.Length == 1)
        {
            return longestToken;
        }

        var bestTwoLineCapacity = int.MaxValue;
        for (var splitIndex = 1; splitIndex < tokens.Length; splitIndex++)
        {
            var firstLineLength = GetJoinedLength(tokens, 0, splitIndex);
            var secondLineLength = GetJoinedLength(
                tokens,
                splitIndex,
                tokens.Length - splitIndex);
            bestTwoLineCapacity = Math.Min(
                bestTwoLineCapacity,
                Math.Max(firstLineLength, secondLineLength));
        }

        return Math.Max(longestToken, bestTwoLineCapacity);
    }

    private static string[] GetTokens(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static int GetJoinedLength(
        IReadOnlyList<string> tokens,
        int startIndex,
        int count)
    {
        var tokenLength = 0;
        for (var index = startIndex; index < startIndex + count; index++)
        {
            tokenLength += tokens[index].Length;
        }

        return tokenLength + Math.Max(0, count - 1);
    }
}
