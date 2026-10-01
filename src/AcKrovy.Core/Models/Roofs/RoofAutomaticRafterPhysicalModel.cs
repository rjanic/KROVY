namespace AcKrovy.Core.Models.Roofs;

public enum LowerEndCutMode
{
    Vertical = 0,
    Perpendicular = 1,
    Horizontal = 2,
}

public enum RidgeJoinMode
{
    Meet = 0,
    Overlap = 1,
}

/// <summary>Roof-level defaults for ordinary automatic rafters.</summary>
public sealed record RoofAutomaticRafterPhysicalSettings(
    LowerEndCutMode LowerEndCutMode = LowerEndCutMode.Vertical,
    RidgeJoinMode RidgeJoinMode = RidgeJoinMode.Meet);

/// <summary>
/// One logical ordinary member with independent plan and physical geometry.
/// For Vertical/Perpendicular, vertices 0..3 are the upper face and 4..7
/// are the corresponding lower corners. Horizontal stores the final clipped
/// body vertices; its source prism and face polygons are in HorizontalCut.
/// </summary>
public sealed record RoofAutomaticRafterPhysicalMember(
    RoofGeneratedMemberKey MemberKey,
    int SourceFaceIndex,
    RoofRafterBoundaryRole StartBoundaryRole,
    RoofRafterBoundaryRole EndBoundaryRole,
    RoofSegment3D PlanAxis,
    IReadOnlyList<RoofPoint3D> SolidVertices,
    double WidthMm,
    double HeightMm,
    double PitchDegrees,
    double PhysicalLengthMm,
    RoofHorizontalRafterCut? HorizontalCut = null,
    RoofStructuralRafterSideCut? StructuralCut = null,
    RoofRidgeOverlapCut? RidgeOverlapCut = null,
    string? AttachedManualIdentity = null)
{
    public string PhysicalIdentity => AttachedManualIdentity is null
        ? $"{MemberKey.MemberKind}:{MemberKey.RoofFace}:{MemberKey.StationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        : "AttachedManual:" + AttachedManualIdentity;
}

/// <summary>
/// An Overlap member clipped below the actual opposing roof plane. The
/// source remains a transient construction prism; no second member or
/// persisted identity is introduced.
/// </summary>
public sealed record RoofRidgeOverlapCut(
    RoofPoint3D PlanePoint,
    RoofPoint3D RetainedNormal,
    IReadOnlyList<RoofPoint3D> SourcePrismVertices,
    double RidgeExtensionMm,
    IReadOnlyList<RoofPoint3D> BodyVertices,
    IReadOnlyList<RoofPoint3D> TopFaceVertices,
    IReadOnlyList<RoofPoint3D> BottomFaceVertices,
    IReadOnlyList<RoofPoint3D> CutFaceVertices);

/// <summary>
/// CAD-neutral result of clipping an ordinary prism at the physical WCS eave
/// elevation. SourcePrismVertices are only the transient host construction;
/// BodyVertices/CutFaceVertices describe the retained physical solid.
/// </summary>
public sealed record RoofHorizontalRafterCut(
    double EaveElevationMm,
    IReadOnlyList<RoofPoint3D> SourcePrismVertices,
    IReadOnlyList<RoofPoint3D> BodyVertices,
    IReadOnlyList<RoofPoint3D> CutFaceVertices,
    IReadOnlyList<RoofPoint3D> TopFaceVertices);

/// <summary>One existing Hip/Valley member, resolved by topology edge rather than proximity.</summary>
public sealed record RoofStructuralRafterTrimSource(
    int TopologyEdgeIndex,
    RoofRafterBoundaryRole Role,
    RoofSegment3D Axis,
    double WidthMm);

/// <summary>
/// The facing plumb side of a structural timber. Normal is horizontal and points
/// into the ordinary member; retained ordinary geometry has nonnegative distance.
/// </summary>
public sealed record RoofStructuralRafterSideCut(
    int TopologyEdgeIndex,
    RoofRafterBoundaryRole Role,
    double StructuralWidthMm,
    RoofPoint3D PlanePoint,
    RoofPoint3D PlaneNormal,
    IReadOnlyList<RoofPoint3D> SourcePrismVertices,
    IReadOnlyList<RoofPoint3D> CutFaceVertices,
    IReadOnlyList<RoofPoint3D> TopFaceVertices,
    IReadOnlyList<RoofPoint3D>? BottomFaceVertices = null,
    RoofSegment3D? LowerContactEdge = null);

public sealed record RoofAutomaticRafterPhysicalModel(
    string RoofOwnerReference,
    IReadOnlyList<RoofAutomaticRafterPhysicalMember> Members);
