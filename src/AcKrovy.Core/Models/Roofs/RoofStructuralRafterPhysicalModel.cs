namespace AcKrovy.Core.Models.Roofs;

/// <summary>A physical face of one Hip/Valley timber, in resolved WCS millimetres.</summary>
public enum RoofStructuralPhysicalFaceKind
{
    TopLeft,
    TopRight,
    SideLeft,
    SideRight,
    Bottom,
    EaveClip,
    RidgeEnd,
    UpperNodeMiter,
    LowerEndProfile,
    RoofEnvelopeClip,
    BottomTrim,
    Top,
}

public sealed record RoofStructuralPhysicalFace(
    RoofStructuralPhysicalFaceKind Kind,
    IReadOnlyList<RoofPoint3D> Vertices);

/// <summary>
/// Geometry only: the host may materialize these boundary faces as a solid.
/// Left and right refer to the directed physical axis from eave to ridge.
/// </summary>
public sealed record RoofStructuralRafterPhysicalModel(
    RoofStructuralRole Role,
    int TopologyEdgeIndex,
    RoofSegment3D UpperAxis,
    double WidthMm,
    double PhysicalVerticalHeightMm,
    double RequiredAutomaticHeightMm,
    bool UsesExplicitHeight,
    string? Warning,
    IReadOnlyList<RoofStructuralPhysicalFace> Faces,
    IReadOnlyList<RoofPoint3D> BodyVertices,
    RoofSegment3D? UpperLeftEdge = null,
    RoofSegment3D? UpperRightEdge = null,
    RoofSegment3D? PhysicalTopAxis = null);
