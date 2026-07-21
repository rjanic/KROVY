namespace AcKrovy.Core.Models;

public sealed record TimberRectangularFootprintValidationResult(
    bool IsValid,
    TimberRectangularFootprintGeometry? Geometry,
    TimberRectangularFootprintValidationError Error);
