namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Translation/rotation-invariant rectangle descriptor in native source order.
/// </summary>
public sealed record RoofRigidFootprintDescriptor(
    int VertexCount,
    RoofPolygonOrientation SourceOrientation,
    double Edge01LengthMm,
    double Edge12LengthMm);
