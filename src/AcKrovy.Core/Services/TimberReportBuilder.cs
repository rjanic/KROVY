using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>Vytvára súhrnný výkaz. Rovnaký typ, materiál, prierez a rezná dĺžka tvoria jeden riadok.</summary>
public static class TimberReportBuilder
{
    public static TimberReport Build(IEnumerable<TimberElementMeasurement> measurements)
    {
        var materialized = measurements.ToList();

        var lines = materialized
            .GroupBy(x => new
            {
                x.Data.ElementType,
                x.Data.Material,
                x.Data.WidthMm,
                x.Data.HeightMm,
                x.CuttingLengthMm,
            })
            .OrderBy(x => x.Key.ElementType)
            .ThenBy(x => x.Key.WidthMm)
            .ThenBy(x => x.Key.HeightMm)
            .ThenBy(x => x.Key.CuttingLengthMm)
            .Select(group => new TimberReportLine(
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
}
