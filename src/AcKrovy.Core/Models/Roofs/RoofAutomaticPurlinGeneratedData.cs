namespace AcKrovy.Core.Models.Roofs;

/// <summary>Independent metadata schema for one future generated Purlin child.</summary>
public static class RoofAutomaticPurlinGeneratedDataSchema
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Owner-scoped automatic-purlin identity. Geometry, elevation, timber product data,
/// generated handles, and transient topology indexes are deliberately absent.
/// </summary>
public sealed record RoofAutomaticPurlinGeneratedData(
    int SchemaVersion,
    string RoofOwnerReference,
    RoofAutomaticPurlinGeneratedKey GeneratedKey)
{
    public RoofAutomaticPurlinGeneratorRole GeneratorRole =>
        GeneratedKey.GeneratorRole;
}

public enum RoofAutomaticPurlinGeneratedDataError
{
    None = 0,
    Missing,
    IncompletePayload,
    MalformedValueType,
    UnexpectedTrailingValue,
    UnsupportedSchemaVersion,
    MissingOwnerReference,
    MalformedOwnerReference,
    UnsupportedRole,
    InvalidRidgeStructuralKey,
    NonPositiveBoundaryEdgeId,
    SameBoundaryEdgeId,
    NonCanonicalBoundaryPair,
    MalformedLayoutItemId,
    NonCanonicalLayoutItemId,
    NonPositiveSourceFaceBoundaryEdgeId,
    MalformedEndpointBoundaryKey,
    UnsupportedEndpointKind,
    IdenticalEndpointBoundaryKeys,
    NonCanonicalEndpointBoundaryOrder,
}

public sealed record RoofAutomaticPurlinGeneratedDataValidationResult(
    bool IsValid,
    RoofAutomaticPurlinGeneratedData? Data,
    RoofAutomaticPurlinGeneratedDataError Error);

