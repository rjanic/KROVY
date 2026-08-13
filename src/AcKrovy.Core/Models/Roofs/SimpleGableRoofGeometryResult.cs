namespace AcKrovy.Core.Models.Roofs;

public sealed record SimpleGableRoofGeometryResult(
    bool IsValid,
    SimpleGableRoofGeometry? Geometry,
    SimpleGableRoofGeometryError Error);
