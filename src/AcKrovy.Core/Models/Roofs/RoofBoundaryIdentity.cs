namespace AcKrovy.Core.Models.Roofs;

/// <summary>Independent schema for owner-scoped physical boundary-segment identity.</summary>
public static class RoofBoundaryIdentitySchema
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persistent identity owned by one authoritative roof boundary. Numeric edge IDs are
/// unique only within that owner and follow the raw physical segment sequence.
/// </summary>
public sealed record RoofBoundaryIdentity(
    int SchemaVersion,
    int PhysicalSegmentCount,
    RoofPolygonOrientation RawWinding,
    IReadOnlyList<int> BoundaryEdgeIds);

public enum RoofBoundaryIdentityError
{
    None = 0,
    Missing,
    IncompletePayload,
    MalformedValueType,
    UnsupportedSchemaVersion,
    InvalidPhysicalSegmentCount,
    MalformedWindingToken,
    BoundaryEdgeIdCountMismatch,
    NonPositiveBoundaryEdgeId,
    DuplicateBoundaryEdgeId,
    CurrentPhysicalSegmentCountMismatch,
    CurrentRawWindingMismatch,
    InvalidFootprint,
    NotAuthoritativeSource,
}

public sealed record RoofBoundaryIdentityValidationResult(
    bool IsValid,
    RoofBoundaryIdentity? Identity,
    RoofBoundaryIdentityError Error);

/// <summary>Index provenance produced while canonicalizing one footprint.</summary>
public sealed record RoofNormalizedBoundaryEdgeProvenance(
    int NormalizedBoundaryEdgeIndex,
    int RawPhysicalSegmentIndex);

/// <summary>
/// Complete provenance used by future topology consumers. NormalizedBoundaryEdgeIndex
/// is also the source-edge index carried by the corresponding topology face.
/// </summary>
public sealed record RoofBoundaryEdgeProvenance(
    int NormalizedBoundaryEdgeIndex,
    int RawPhysicalSegmentIndex,
    int BoundaryEdgeId);

/// <summary>Order-independent owner-scoped boundary pair for a future structural edge.</summary>
public sealed record RoofBoundaryEdgeIdPair(
    int LowerBoundaryEdgeId,
    int UpperBoundaryEdgeId);

public sealed record RoofFootprintNormalizationResult(
    RoofValidationResult Validation,
    IReadOnlyList<RoofNormalizedBoundaryEdgeProvenance> EdgeProvenance);

public sealed record RoofBoundaryIdentityProvenanceResult(
    bool IsValid,
    RoofFootprint? Footprint,
    IReadOnlyList<RoofBoundaryEdgeProvenance> EdgeProvenance,
    RoofValidationError FootprintError,
    RoofBoundaryIdentityError IdentityError);
