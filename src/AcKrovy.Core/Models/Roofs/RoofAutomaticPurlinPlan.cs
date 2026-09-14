using System.Globalization;
using AcKrovy.Core.Models;

namespace AcKrovy.Core.Models.Roofs;

public enum RoofAutomaticPurlinGeneratorRole
{
    Ridge = 1,
    Intermediate = 2,
}

/// <summary>
/// Stable semantic identity of a face-boundary edge. An eave uses one owner-scoped
/// BoundaryEdgeId; an internal boundary uses the canonical pair of its incident faces.
/// </summary>
public sealed record RoofAutomaticPurlinBoundaryKey(
    RoofTopologyEdgeKind Kind,
    int BoundaryEdgeIdA,
    int BoundaryEdgeIdB)
{
    public override string ToString() => Kind == RoofTopologyEdgeKind.Eave
        ? string.Join(
            "|",
            Kind.ToString(),
            BoundaryEdgeIdA.ToString(CultureInfo.InvariantCulture))
        : string.Join(
            "|",
            Kind.ToString(),
            BoundaryEdgeIdA.ToString(CultureInfo.InvariantCulture),
            BoundaryEdgeIdB.ToString(CultureInfo.InvariantCulture));
}

public abstract record RoofAutomaticPurlinGeneratedKey
{
    public abstract RoofAutomaticPurlinGeneratorRole GeneratorRole { get; }
}

/// <summary>Roof-wide ridge purlin identity reused directly from structural identity.</summary>
public sealed record RoofAutomaticPurlinRidgeKey(
    RoofStructuralLogicalKey StructuralKey) : RoofAutomaticPurlinGeneratedKey
{
    public override RoofAutomaticPurlinGeneratorRole GeneratorRole =>
        RoofAutomaticPurlinGeneratorRole.Ridge;

    public override string ToString() => StructuralKey.ToString();
}

/// <summary>
/// Stable identity for one physical interval produced by one intermediate layout row
/// on one source face. Endpoint boundary keys are stored in canonical order.
/// </summary>
public sealed record RoofAutomaticPurlinIntermediateKey(
    string LayoutItemId,
    int SourceFaceBoundaryEdgeId,
    RoofAutomaticPurlinBoundaryKey EndpointBoundaryKeyA,
    RoofAutomaticPurlinBoundaryKey EndpointBoundaryKeyB) : RoofAutomaticPurlinGeneratedKey
{
    public override RoofAutomaticPurlinGeneratorRole GeneratorRole =>
        RoofAutomaticPurlinGeneratorRole.Intermediate;

    public override string ToString() => string.Join(
        "|",
        GeneratorRole.ToString(),
        LayoutItemId,
        SourceFaceBoundaryEdgeId.ToString(CultureInfo.InvariantCulture),
        EndpointBoundaryKeyA.ToString(),
        EndpointBoundaryKeyB.ToString());
}

/// <summary>One desired CAD-neutral automatic Purlin centerline.</summary>
public sealed record RoofAutomaticPurlinPlanItem(
    RoofAutomaticPurlinGeneratedKey GeneratedKey,
    TimberElementType ElementType,
    RoofSegment3D Segment3D,
    RoofPurlinElevationProfile? ElevationProfile = null,
    RoofPurlinPhysicalPlacement? PhysicalPlacement = null)
{
    public RoofAutomaticPurlinGeneratorRole GeneratorRole => GeneratedKey.GeneratorRole;
    public string? LayoutItemId =>
        (GeneratedKey as RoofAutomaticPurlinIntermediateKey)?.LayoutItemId;
    public double LengthMm => Segment3D.LengthMm;
}

/// <summary>Inspector-ready vertical section elevations; all values are millimetres.</summary>
public sealed record RoofPurlinElevationProfile(
    double BottomLocalZMm,
    double CenterLocalZMm,
    double TopLocalZMm,
    double BottomRelativeElevationMm,
    double CenterRelativeElevationMm,
    double TopRelativeElevationMm,
    double? SeatingDepthMm);

/// <summary>Effective physical inputs supplied by the materialization layer.</summary>
public sealed record RoofAutomaticPurlinPlanningInput(
    RoofRelativeElevationDatum RelativeElevationDatum,
    double PurlinHeightMm,
    double RafterHeightMm);

public sealed record RoofAutomaticPurlinPlacementResolution(
    bool IsValid,
    double? RoofSurfaceLocalZMm,
    double? SeatingDepthMm,
    RoofStructuralLogicalKey? ResolvedReferenceRidgeKey,
    RoofAutomaticPurlinPlanError Error);

/// <summary>Immutable desired state; contains no product defaults or host entities.</summary>
public sealed record RoofAutomaticPurlinPlan(
    IReadOnlyList<RoofAutomaticPurlinPlanItem> Items);

public enum RoofAutomaticPurlinPlanError
{
    None = 0,
    InvalidGeometry,
    InvalidBoundaryProvenance,
    BoundaryProvenanceCountMismatch,
    InvalidLayout,
    EmptyLayoutItemId,
    MalformedLayoutItemId,
    DuplicateLayoutItemId,
    InvalidElevation,
    InvalidRelativeElevationDatum,
    InvalidPhysicalSection,
    InvalidPlacementMode,
    InvalidPlacementValue,
    InvalidReferenceRidge,
    ReferenceRidgeRequired,
    ReferenceRidgeNotFound,
    InclinedReferenceRidge,
    InvalidSeatingDepth,
    MissingFaceNormal,
    InvalidFaceNormal,
    InconsistentFaceNormalVerticalComponent,
    ImpossiblePhysicalPlacement,
    ElevationOutsideRoof,
    CriticalEventElevation,
    InvalidCoordinate,
    UnresolvedFaceBoundaryIdentity,
    InvalidFaceIntersection,
    ZeroLengthSegment,
    DuplicateGeneratedKey,
}

public sealed record RoofAutomaticPurlinPlanResult(
    bool IsValid,
    RoofAutomaticPurlinPlan? Plan,
    RoofAutomaticPurlinPlanError Error,
    string? FailedLayoutItemId,
    RoofAutomaticPurlinGeneratedKey? DuplicateGeneratedKey);
