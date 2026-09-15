using System.Globalization;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>Physical structural roles, separate from ordinary automatic rafters.</summary>
public enum RoofStructuralRole
{
    Undefined = 0,
    Hip = 1,
    Valley = 2,
    Ridge = 3,
}

/// <summary>
/// Stable structural identity inside one roof owner. The owner is deliberately outside
/// this key because every BoundaryEdgeId is owner-scoped. A must be less than B.
/// </summary>
public sealed record RoofStructuralLogicalKey(
    RoofStructuralRole Role,
    int BoundaryEdgeIdA,
    int BoundaryEdgeIdB)
{
    public override string ToString() => string.Join(
        "|",
        Role.ToString(),
        BoundaryEdgeIdA.ToString(CultureInfo.InvariantCulture),
        BoundaryEdgeIdB.ToString(CultureInfo.InvariantCulture));
}

public enum RoofStructuralIdentityError
{
    None = 0,
    UnsupportedRole,
    NonPositiveBoundaryEdgeId,
    SameBoundaryEdgeId,
    NonCanonicalBoundaryPair,
    DuplicateStructuralIdentity,
}

/// <summary>
/// One resolved structural topology edge. TopologyEdgeIndex is transient runtime
/// provenance and is never part of the logical identity or persisted metadata.
/// </summary>
public sealed record ResolvedRoofStructuralEdge(
    RoofStructuralLogicalKey StructuralIdentity,
    int TopologyEdgeIndex,
    RoofSegment3D Segment3D,
    int? OriginatingBoundaryVertexIndex = null,
    int? PhysicalBoundaryAnchorVertexIndex = null,
    int? PhysicalPathAnchorVertexIndex = null,
    bool IsPhysicalFoldTimberEligible = false)
{
    public RoofStructuralRole StructuralRole => StructuralIdentity.Role;
    public int BoundaryEdgeIdA => StructuralIdentity.BoundaryEdgeIdA;
    public int BoundaryEdgeIdB => StructuralIdentity.BoundaryEdgeIdB;
    public double Length3dMm => Segment3D.LengthMm;

    /// <summary>
    /// True only when this exact topology segment reaches its compatible physical
    /// footprint corner. Surviving wavefront lineage alone is insufficient.
    /// </summary>
    public bool HasPhysicalBoundaryAnchor => PhysicalBoundaryAnchorVertexIndex.HasValue;

    /// <summary>
    /// Optional provenance: unambiguous path connected to a compatible footprint
    /// corner, including Valley-junction Hip continuations. Not required for timber
    /// eligibility of a real exposed convex/concave roof fold.
    /// </summary>
    public bool HasPhysicalPathAnchor => PhysicalPathAnchorVertexIndex.HasValue;

    /// <summary>
    /// Timber eligibility follows physical fold semantics: convex Hip folds and
    /// concave Valley folds that pass geometric validation. Horizontal Ridge is
    /// never eligible. Path anchors remain provenance only.
    /// </summary>
    public bool IsAutomaticStructuralTimberEligible =>
        (StructuralRole is RoofStructuralRole.Hip or RoofStructuralRole.Valley) &&
        IsPhysicalFoldTimberEligible;
}

public enum RoofStructuralEdgeResolutionError
{
    None = 0,
    InvalidGeometry,
    InvalidBoundaryProvenance,
    BoundaryProvenanceCountMismatch,
    UnsupportedTopologyEdgeRole,
    StructuralEdgeFaceCountMismatch,
    StructuralEdgeFaceIndexOutOfRange,
    UnresolvedFaceBoundaryIdentity,
    InvalidStructuralIdentity,
    DuplicateStructuralIdentity,
    InvalidStructuralSegment,
}

public sealed record RoofStructuralEdgeResolutionResult(
    bool IsValid,
    IReadOnlyList<ResolvedRoofStructuralEdge> Edges,
    RoofStructuralEdgeResolutionError Error,
    int? FailedTopologyEdgeIndex,
    RoofStructuralLogicalKey? DuplicateIdentity);
