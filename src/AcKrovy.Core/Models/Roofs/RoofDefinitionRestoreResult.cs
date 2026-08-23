namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofDefinitionRestoreResult(
    bool IsValid,
    IRoofGeometry? Geometry,
    RoofDefinitionRestoreError Error);
