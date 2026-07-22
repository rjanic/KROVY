namespace AcKrovy.Core.Models;

/// <summary>A CAD-neutral straight edge that can participate in a Post footprint.</summary>
public sealed record TimberLineRectangleEdge(
    string Key,
    TimberRectangularFootprintPoint Start,
    TimberRectangularFootprintPoint End);
