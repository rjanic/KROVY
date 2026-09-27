namespace AcKrovy.Core.Models.Roofs;

/// <summary>Independent ownership metadata for one generated physical-3D roof entity.</summary>
public static class RoofPhysical3DGeneratedDataSchema
{
    public const int CurrentVersion = 1;
}

public sealed record RoofPhysical3DGeneratedData(
    int SchemaVersion,
    string RoofOwnerReference,
    RoofPhysical3DGeneratedRole Role,
    string StructuralId,
    string GenerationSignature);

public enum RoofPhysical3DGeneratedDataError
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
    MissingStructuralId,
    MissingGenerationSignature,
}

public sealed record RoofPhysical3DGeneratedDataValidationResult(
    bool IsValid,
    RoofPhysical3DGeneratedData? Data,
    RoofPhysical3DGeneratedDataError Error);
