namespace AcKrovy.Core.Models.Roofs;

/// <summary>Independent owner-scoped automatic-purlin layout schema.</summary>
public static class RoofPurlinLayoutSchema
{
    public const int CurrentVersion = 1;
}

/// <summary>Typed semantic fields decoded from one repeated layout-row payload.</summary>
public sealed record RoofPurlinLayoutStoredItem(
    string LayoutItemId,
    int EnabledValue,
    string PlacementToken,
    double PlacementValueMm,
    string ReferenceRidgeRoleToken,
    int ReferenceRidgeBoundaryEdgeIdA,
    int ReferenceRidgeBoundaryEdgeIdB,
    string SeatingDepthToken,
    double SeatingDepthValue);

public enum RoofPurlinLayoutPersistenceError
{
    None = 0,
    Missing,
    NotAuthoritativeSource,
    IncompletePayload,
    MalformedValueType,
    UnexpectedTrailingValue,
    UnsupportedSchemaVersion,
    InvalidRidgeEnabled,
    InvalidItemCount,
    InvalidEnabled,
    EmptyLayoutItemId,
    MalformedLayoutItemId,
    NonCanonicalLayoutItemId,
    DuplicateLayoutItemId,
    InvalidPlacementToken,
    InvalidPlacementValue,
    InvalidReferenceRidge,
    InvalidSeatingDepthToken,
    InvalidSeatingDepth,
}

public sealed record RoofPurlinLayoutValidationResult(
    bool IsValid,
    RoofAutomaticPurlinLayout? Layout,
    RoofPurlinLayoutPersistenceError Error);
