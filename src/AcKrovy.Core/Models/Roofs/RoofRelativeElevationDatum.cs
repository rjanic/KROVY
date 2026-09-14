namespace AcKrovy.Core.Models.Roofs;

/// <summary>Meaning of the local plane used as the architectural elevation reference.</summary>
public enum RoofRelativeElevationReferenceKind
{
    ExplicitLocalPlane = 1,
    SourceEavePlane = 2,
    WallPlateBottom = 3,
}

/// <summary>
/// Owner-scoped conversion between the roof-local Z axis and architectural relative
/// elevation. Values are persisted as millimetres; formatted metre text is derived.
/// </summary>
public sealed record RoofRelativeElevationDatum(
    RoofRelativeElevationReferenceKind ReferenceKind,
    double ReferenceRelativeElevationMm,
    double ReferenceLocalZMm);

public static class RoofRelativeElevationDatumSchema
{
    public const int CurrentVersion = 1;
}

public enum RoofRelativeElevationDatumError
{
    None = 0,
    Missing,
    NotAuthoritativeSource,
    IncompletePayload,
    MalformedValueType,
    UnexpectedTrailingValue,
    UnsupportedSchemaVersion,
    UnsupportedReferenceKind,
    InvalidReferenceRelativeElevation,
    InvalidReferenceLocalZ,
}

public sealed record RoofRelativeElevationDatumValidationResult(
    bool IsValid,
    RoofRelativeElevationDatum? Datum,
    RoofRelativeElevationDatumError Error);
