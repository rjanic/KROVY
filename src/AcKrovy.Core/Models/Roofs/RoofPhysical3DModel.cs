namespace AcKrovy.Core.Models.Roofs;

/// <summary>Stable structural plane identity — not a host CAD entity handle.</summary>
public readonly record struct RoofPhysical3DPlaneId(string Value)
{
    public override string ToString() => Value;
}

public enum RoofPhysical3DEdgeKind
{
    Eave = 0,
    Hip = 1,
    Ridge = 2,
}

public enum RoofPhysical3DGeneratedRole
{
    Face = 0,
    RidgeEdge = 1,
    HipEdge = 2,
    EaveEdge = 3,
}

/// <summary>One planar upper-rafter roof face in physical WCS millimetres.</summary>
public sealed record RoofPhysical3DFace(
    RoofPhysical3DPlaneId PlaneId,
    int SourceEdgeIndex,
    double PitchDegrees,
    IReadOnlyList<RoofPoint3D> Polygon,
    RoofSegment3D EaveSegment);

/// <summary>One shared physical roof edge in WCS millimetres.</summary>
public sealed record RoofPhysical3DEdge(
    RoofPhysical3DEdgeKind Kind,
    string StructuralId,
    RoofSegment3D Segment);

/// <summary>
/// Authoritative rectangular/square hip roof physical 3D model for future purlin and timber use.
/// Coordinates already include the resolved eave relative elevation (decision 2A).
/// </summary>
public sealed record RoofPhysical3DModel(
    string OwnerReference,
    double PitchDegrees,
    RoofAbsoluteElevationState Elevation,
    bool IsPyramidal,
    IReadOnlyList<RoofPoint3D> Vertices,
    IReadOnlyList<RoofPhysical3DEdge> Eaves,
    IReadOnlyList<RoofPhysical3DEdge> Hips,
    IReadOnlyList<RoofPhysical3DEdge> Ridges,
    IReadOnlyList<RoofPhysical3DFace> Faces,
    string GenerationSignature)
{
    public RoofPoint3D? Apex =>
        IsPyramidal
            ? Vertices.OrderByDescending(point => point.Z).FirstOrDefault()
            : null;
}
