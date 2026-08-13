namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofDefinitionRestoreResult(
    bool IsValid,
    SimpleGableRoofGeometry? Geometry,
    RoofDefinitionRestoreError Error);
