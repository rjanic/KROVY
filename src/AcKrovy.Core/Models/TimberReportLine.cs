namespace AcKrovy.Core.Models;

/// <summary>Jeden súhrnný riadok výkazu reziva.</summary>
public sealed record TimberReportLine(
    string ElementId,
    TimberElementType ElementType,
    string Material,
    double WidthMm,
    double HeightMm,
    double CuttingLengthMm,
    int Count,
    double TotalLengthMm,
    double TotalVolumeM3,
    string? CustomElementTypeId = null,
    string? CustomElementTypeName = null,
    string? CustomElementTypePrefix = null);
