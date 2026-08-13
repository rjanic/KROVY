namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofValidationResult(
    bool IsValid,
    RoofFootprint? Footprint,
    RoofValidationError Error,
    RoofPolygonOrientation SourceOrientation);
