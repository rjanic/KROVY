namespace AcKrovy.Core.Models.Roofs;

public static class RoofStructuralGeneratedDataSchema
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Independent ownership metadata for a future generated structural roof member.
/// It is not ordinary-rafter metadata and contains no transient topology indexes.
/// </summary>
public sealed record RoofStructuralGeneratedData(
    int SchemaVersion,
    string RoofOwnerReference,
    RoofStructuralRole StructuralRole,
    int BoundaryEdgeIdA,
    int BoundaryEdgeIdB)
{
    public RoofStructuralLogicalKey LogicalKey => new(
        StructuralRole,
        BoundaryEdgeIdA,
        BoundaryEdgeIdB);
}

public enum RoofStructuralGeneratedDataError
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
    NonPositiveBoundaryEdgeId,
    SameBoundaryEdgeId,
    NonCanonicalBoundaryPair,
}

public sealed record RoofStructuralGeneratedDataValidationResult(
    bool IsValid,
    RoofStructuralGeneratedData? Data,
    RoofStructuralGeneratedDataError Error);
