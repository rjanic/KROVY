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
            .OrderBy(x => x.Key.ElementType)
            .ThenBy(x => x.Key.WidthMm)
            .ThenBy(x => x.Key.HeightMm)
            .ThenBy(x => x.Key.CuttingLengthMm)
            .Select(group => new TimberReportLine(
                SelectElementId(group),
                group.Key.ElementType,
                group.Key.Material,
                group.Key.WidthMm,
                group.Key.HeightMm,
                group.Key.CuttingLengthMm,
                group.Count(),
                group.Sum(x => x.CuttingLengthMm),
                group.Sum(x => x.VolumeM3)))
            .ToList();

        return new TimberReport(lines, materialized.Count, lines.Sum(x => x.TotalVolumeM3));
    }

    private static string SelectElementId(IEnumerable<TimberElementMeasurement> measurements) =>
        measurements
            .Select(measurement => measurement.Data.ElementId)
            .FirstOrDefault(elementId => !string.IsNullOrWhiteSpace(elementId)) ?? string.Empty;
}
