using System.Globalization;
using System.Text;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberCsvFormatter
{
    public const char DefaultDelimiter = ';';
    public const string LineEnding = "\r\n";

    public static TimberCsvDocument Format(
        IEnumerable<TimberElementMeasurement> measurements,
        TimberCsvExportMode mode,
        TimberCsvLocalization localization,
        CultureInfo culture)
    {
        if (measurements is null)
        {
            throw new ArgumentNullException(nameof(measurements));
        }

        if (localization is null)
        {
            throw new ArgumentNullException(nameof(localization));
        }

        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        var materialized = measurements.ToArray();
        var rows = mode switch
        {
            TimberCsvExportMode.Individual => CreateIndividualRows(
                materialized,
                localization,
                culture),
            TimberCsvExportMode.Summarized => CreateSummarizedRows(
                materialized,
                localization,
                culture),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var builder = new StringBuilder();
        AppendRow(builder, Headers(localization.Headers));
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        return new TimberCsvDocument(
            mode,
            materialized.Length,
            rows.Count,
            builder.ToString());
    }

    private static IReadOnlyList<IReadOnlyList<string>> CreateIndividualRows(
        IReadOnlyList<TimberElementMeasurement> measurements,
        TimberCsvLocalization localization,
        CultureInfo culture) =>
        measurements
            .OrderBy(item => item.Data.ElementType)
            .ThenBy(item => item.Data.CustomElementTypeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => ItemNumber(item.Data) ?? int.MaxValue)
            .ThenBy(item => item.Data.ElementId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Data.WidthMm)
            .ThenBy(item => item.Data.HeightMm)
            .ThenBy(item => item.CuttingLengthMm)
            .Select(item => (IReadOnlyList<string>)new[]
            {
                item.Data.ElementId,
                localization.ElementTypeDisplay(
                    item.Data.ElementType,
                    item.Data.CustomElementTypeName),
                localization.MaterialDisplay(item.Data.Material),
                Number(item.Data.WidthMm, "0.###", culture),
                Number(item.Data.HeightMm, "0.###", culture),
                Number(item.CuttingLengthMm, "0.###", culture),
                "1",
                Number(item.CuttingLengthMm / 1000d, "0.###", culture),
                Number(item.VolumeM3, "0.######", culture),
                item.Data.Note ?? string.Empty,
            })
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<string>> CreateSummarizedRows(
        IReadOnlyList<TimberElementMeasurement> measurements,
        TimberCsvLocalization localization,
        CultureInfo culture)
    {
        var report = TimberReportBuilder.Build(measurements);
        return report.Lines
            .Select(line => (IReadOnlyList<string>)new[]
            {
                line.ElementId,
                localization.ElementTypeDisplay(
                    line.ElementType,
                    line.CustomElementTypeName),
                localization.MaterialDisplay(line.Material),
                Number(line.WidthMm, "0.###", culture),
                Number(line.HeightMm, "0.###", culture),
                Number(line.CuttingLengthMm, "0.###", culture),
                line.Count.ToString(culture),
                Number(line.TotalLengthMm / 1000d, "0.###", culture),
                Number(line.TotalVolumeM3, "0.######", culture),
                string.Empty,
            })
            .ToArray();
    }

    private static IReadOnlyList<string> Headers(TimberCsvHeaders headers) =>
    [
        headers.ElementId,
        headers.ElementType,
        headers.Material,
        headers.WidthMm,
        headers.HeightMm,
        headers.CuttingLengthMm,
        headers.Quantity,
        headers.TotalLengthM,
        headers.VolumeM3,
        headers.Note,
    ];

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(DefaultDelimiter);
            }

            builder.Append(Escape(fields[index]));
        }

        builder.Append(LineEnding);
    }

    private static string Escape(string? value)
    {
        var field = value ?? string.Empty;
        if (field.IndexOfAny(new[] { DefaultDelimiter, '"', '\r', '\n' }) < 0)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    private static string Number(double value, string format, CultureInfo culture) =>
        value.ToString(format, culture);

    private static int? ItemNumber(TimberElementData data)
    {
        var prefix = data.ElementType == TimberElementType.Custom
            ? data.CustomElementTypePrefix ?? string.Empty
            : TimberElementIdentityPrefixes.GetPrefix(data.ElementType);
        return TimberElementIdentityRules.TryParseElementNumber(data.ElementId, prefix);
    }
}
