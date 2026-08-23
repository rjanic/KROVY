namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofGeometryResult(
    bool IsValid,
    IRoofGeometry? Geometry,
    SimpleGableRoofGeometryError Error);
