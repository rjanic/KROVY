namespace AcKrovy.Core.Models;

public sealed record TimberRectangularFootprintSegment(
    int Index,
    TimberRectangularFootprintPoint Start,
    TimberRectangularFootprintPoint End,
    double LengthMm);
