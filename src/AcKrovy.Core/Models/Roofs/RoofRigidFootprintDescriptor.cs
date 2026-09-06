namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Translation/rotation-invariant source descriptor in native vertex order.
/// For general polygons the first two edge lengths are a compact source guard;
/// the source entity remains the authority for the complete persisted footprint.
/// </summary>
public sealed record RoofRigidFootprintDescriptor(
    int VertexCount,
    RoofPolygonOrientation SourceOrientation,
    double Edge01LengthMm,
    double Edge12LengthMm);
