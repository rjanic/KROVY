using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>Vytvára súhrnný výkaz. Rovnaký typ, materiál, prierez a rezná dĺžka tvoria jeden riadok.</summary>
public static class TimberReportBuilder
{
    public static TimberReport Build(IEnumerable<TimberElementMeasurement> measurements)
    {
        var materialized = measurements.ToList();

        var lines = materialized
            .GroupBy(TimberElementSignature.FromMeasurement)
            .Select(group => new TimberReportLine(
                SelectElementId(group),
                group.Key.ElementType,
                group.Key.Material,
                group.Key.WidthMm,
                group.Key.HeightMm,
                group.Key.CuttingLengthMm,
                group.Count(),
                group.Sum(x => x.CuttingLengthMm),
                group.Sum(x => x.VolumeM3),
                group.First().Data.CustomElementTypeId,
                group.First().Data.CustomElementTypeName,
                group.First().Data.CustomElementTypePrefix))
            .OrderBy(line => line.ElementType)
            .ThenBy(line => line.CustomElementTypeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => TryParseElementNumber(line) ?? int.MaxValue)
            .ThenBy(line => line.ElementId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.WidthMm)
            .ThenBy(line => line.HeightMm)
            .ThenBy(line => line.CuttingLengthMm)
            .ToList();

        return new TimberReport(lines, materialized.Count, lines.Sum(x => x.TotalVolumeM3));
    }

    private static string SelectElementId(IEnumerable<TimberElementMeasurement> measurements) =>
        measurements
            .Select(measurement => measurement.Data.ElementId)
            .FirstOrDefault(elementId => !string.IsNullOrWhiteSpace(elementId)) ?? string.Empty;

    private static int? TryParseElementNumber(TimberReportLine line) =>
        TimberElementIdentityRules.TryParseElementNumber(
            line.ElementId,
            line.ElementType == TimberElementType.Custom
                ? line.CustomElementTypePrefix ?? string.Empty
                : TimberElementIdentityPrefixes.GetPrefix(line.ElementType));
}
