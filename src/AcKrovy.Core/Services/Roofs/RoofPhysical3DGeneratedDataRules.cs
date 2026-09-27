using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Validation for generated physical-3D child ownership metadata.</summary>
public static class RoofPhysical3DGeneratedDataRules
{
    public static RoofPhysical3DGeneratedDataValidationResult ValidateStored(
        int schemaVersion,
        string? ownerReference,
        string? roleToken,
        string? structuralId,
        string? generationSignature)
    {
        if (schemaVersion != RoofPhysical3DGeneratedDataSchema.CurrentVersion)
        {
            return Invalid(RoofPhysical3DGeneratedDataError.UnsupportedSchemaVersion);
        }

        if (string.IsNullOrWhiteSpace(ownerReference))
        {
            return Invalid(RoofPhysical3DGeneratedDataError.MissingOwnerReference);
        }

        if (!IsAsciiHandle(ownerReference!))
        {
            return Invalid(RoofPhysical3DGeneratedDataError.MalformedOwnerReference);
        }

        if (string.IsNullOrWhiteSpace(roleToken) ||
            !Enum.TryParse(roleToken, ignoreCase: false, out RoofPhysical3DGeneratedRole role) ||
            !string.Equals(role.ToString(), roleToken, StringComparison.Ordinal))
        {
            return Invalid(RoofPhysical3DGeneratedDataError.UnsupportedRole);
        }

        if (string.IsNullOrWhiteSpace(structuralId))
        {
            return Invalid(RoofPhysical3DGeneratedDataError.MissingStructuralId);
        }

        if (string.IsNullOrWhiteSpace(generationSignature))
        {
            return Invalid(RoofPhysical3DGeneratedDataError.MissingGenerationSignature);
        }

        return new RoofPhysical3DGeneratedDataValidationResult(
            true,
            new RoofPhysical3DGeneratedData(
                schemaVersion,
                ownerReference!,
                role,
                structuralId!,
                generationSignature!),
            RoofPhysical3DGeneratedDataError.None);
    }

    public static RoofPhysical3DGeneratedData Create(
        string ownerReference,
        RoofPhysical3DGeneratedRole role,
        string structuralId,
        string generationSignature)
    {
        var validated = ValidateStored(
            RoofPhysical3DGeneratedDataSchema.CurrentVersion,
            ownerReference,
            role.ToString(),
            structuralId,
            generationSignature);
        if (validated.Data is null)
        {
            throw new ArgumentException("Invalid physical 3D generated data: " + validated.Error);
        }

        return validated.Data;
    }

    private static bool IsAsciiHandle(string value)
    {
        foreach (var ch in value)
        {
            if (ch > 0x7F || char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    private static RoofPhysical3DGeneratedDataValidationResult Invalid(
        RoofPhysical3DGeneratedDataError error) =>
        new(false, null, error);
}
