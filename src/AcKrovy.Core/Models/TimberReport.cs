namespace AcKrovy.Core.Models;

/// <summary>Kompletný výkaz z vybraných alebo všetkých inteligentných prvkov.</summary>
public sealed record TimberReport(
    IReadOnlyList<TimberReportLine> Lines,
    int SourceElementCount,
    double TotalVolumeM3);
