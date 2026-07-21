namespace AcKrovy.Core.Models;

public sealed record TimberPostFootprintPerpendicularGeometry(
    TimberRectangularFootprintPoint CapStart,
    TimberRectangularFootprintPoint CapEnd,
    TimberRectangularFootprintPoint StemStart,
    TimberRectangularFootprintPoint StemEnd,
    TimberRectangularFootprintPoint TextPosition,
    string Text);
