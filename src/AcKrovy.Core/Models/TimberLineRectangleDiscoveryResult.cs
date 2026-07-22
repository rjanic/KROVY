namespace AcKrovy.Core.Models;

public enum TimberLineRectangleDiscoveryStatus
{
    Success,
    NotFound,
    InvalidRectangle,
    Ambiguous,
    Branching,
    DuplicateEdge,
}

public sealed record TimberLineRectangleDiscoveryResult(
    TimberLineRectangleDiscoveryStatus Status,
    TimberRectangularFootprintGeometry? Geometry,
    IReadOnlyList<string> OrderedEdgeKeys)
{
    public const int SelectedWidthEdgeIndex = 0;

    public static TimberLineRectangleDiscoveryResult Failure(
        TimberLineRectangleDiscoveryStatus status) => new(status, null, Array.Empty<string>());
}
