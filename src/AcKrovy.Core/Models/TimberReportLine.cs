namespace AcKrovy.Core.Models;

/// <summary>Jeden súhrnný riadok výkazu reziva.</summary>
public sealed record TimberReportLine(
    TimberElementType ElementType,
    string Material,
    double WidthMm,
    double HeightMm,
    double CuttingLengthMm,
    int Count,
    double TotalLengthMm,
    double TotalVolumeM3);
