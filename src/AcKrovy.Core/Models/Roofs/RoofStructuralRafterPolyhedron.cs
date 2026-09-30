namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// A CAD-neutral request. The resolved edge carries the existing structural identity;
/// ordinary members must already have been physically clipped against this width.
/// </summary>
public sealed record RoofStructuralRafterPolyhedronRequest(
    RoofTopology Topology,
    ResolvedRoofStructuralEdge StructuralEdge,
    double PhysicalEaveElevationMm,
    double StructuralWidthMm,
    RoofStructuralHeightMode HeightMode,
    double? ExplicitVerticalHeightMm,
    IReadOnlyList<RoofAutomaticRafterPhysicalMember> OrdinaryMembers,
    RoofStructuralRafterClipPlane? UpperNodeMiterPlane = null,
    LowerEndCutMode LowerEndCutMode = LowerEndCutMode.Vertical);

/// <summary>Keep the half-space with dot(point - Point, RetainedNormal) >= 0.</summary>
public sealed record RoofStructuralRafterClipPlane(
    RoofPoint3D Point,
    RoofPoint3D RetainedNormal);

/// <summary>
/// Extrudable convex construction prism: one full-width Hip prism with a
/// single top face, or one of two legacy Valley halves. Indices match the
/// ordinary prism convention:
/// 0/1 start top, 2/3 end top, 4/5 start bottom, 6/7 end bottom.
/// Extrude the start cross-section (0,1,5,4) by vertex[2]-vertex[0].
/// End vertices extend beyond the canonical node by the miter reach (or by
/// one millimetre for a non-mitered ridge clip).
/// </summary>
public sealed record RoofStructuralRafterConvexHalf(
    int AdjacentSourceFaceIndex,
    int SideSign,
    IReadOnlyList<RoofPoint3D> SourcePrismVertices,
    IReadOnlyList<RoofPoint3D> ClippedBodyVertices,
    IReadOnlyList<RoofPoint3D> ClippedTopFaceVertices);

/// <summary>
/// Complete boundary-face geometry for one physical structural representation.
/// The logical topology axis and structural key remain separate from clipped vertices.
/// </summary>
public sealed record RoofStructuralRafterPolyhedron(
    RoofStructuralLogicalKey StructuralKey,
    RoofStructuralHeightMode HeightMode,
    RoofStructuralRafterPhysicalModel Geometry,
    IReadOnlyList<int> EaveBoundaryEdgeIndices,
    double MaximumOrdinaryCutVerticalSpanMm,
    IReadOnlyList<RoofStructuralRafterConvexHalf> ConvexHalves,
    IReadOnlyList<RoofStructuralRafterClipPlane> EaveClipPlanes,
    RoofStructuralRafterClipPlane RidgeClipPlane,
    RoofStructuralRafterClipPlane? UpperNodeMiterPlane,
    LowerEndCutMode LowerEndCutMode,
    IReadOnlyList<RoofStructuralRafterClipPlane> LowerEndClipPlanes,
    IReadOnlyList<RoofStructuralRafterClipPlane> RoofEnvelopeClipPlanes);
