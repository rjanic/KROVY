namespace AcKrovy.Core.Models;

public sealed record TimberPolylineSegmentPickResult(
    TimberPolylineSegmentPickStatus Status,
    int? EdgeIndex,
    double DistanceMm);
