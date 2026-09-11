using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral schema-1 validation and token rules.</summary>
public static class RoofStructuralGeneratedDataRules
{
    public const string HipToken = "Hip";
    public const string ValleyToken = "Valley";
    public const string RidgeToken = "Ridge";

    public static RoofStructuralGeneratedDataValidationResult Create(
        string? ownerReference,
        RoofStructuralRole role,
        int firstBoundaryEdgeId,
        int secondBoundaryEdgeId)
    {
        if (!RoofStructuralIdentityRules.TryCreate(
                role,
                firstBoundaryEdgeId,
                secondBoundaryEdgeId,
                out var identity,
                out var identityError) ||
            identity is null)
        {
            return Invalid(Map(identityError));
        }

        return ValidateStored(
            RoofStructuralGeneratedDataSchema.CurrentVersion,
            ownerReference,
            FormatRole(role),
            identity.BoundaryEdgeIdA,
            identity.BoundaryEdgeIdB);
    }

    public static RoofStructuralGeneratedDataValidationResult ValidateStored(
        int schemaVersion,
        string? ownerReference,
        string? roleToken,
        int boundaryEdgeIdA,
        int boundaryEdgeIdB)
    {
        if (schemaVersion != RoofStructuralGeneratedDataSchema.CurrentVersion)
        {
            return Invalid(RoofStructuralGeneratedDataError.UnsupportedSchemaVersion);
        }

        if (string.IsNullOrWhiteSpace(ownerReference))
        {
            return Invalid(RoofStructuralGeneratedDataError.MissingOwnerReference);
        }

        if (!TryNormalizeOwnerReference(ownerReference, out var normalizedOwner))
        {
            return Invalid(RoofStructuralGeneratedDataError.MalformedOwnerReference);
        }

        if (!TryParseRole(roleToken, out var role))
        {
            return Invalid(RoofStructuralGeneratedDataError.UnsupportedRole);
        }

        var key = new RoofStructuralLogicalKey(role, boundaryEdgeIdA, boundaryEdgeIdB);
        if (!RoofStructuralIdentityRules.TryValidateCanonical(key, out var identityError))
        {
            return Invalid(Map(identityError));
        }

        return new RoofStructuralGeneratedDataValidationResult(
            true,
            new RoofStructuralGeneratedData(
                schemaVersion,
                normalizedOwner,
                role,
                boundaryEdgeIdA,
                boundaryEdgeIdB),
            RoofStructuralGeneratedDataError.None);
    }

    public static string FormatRole(RoofStructuralRole role) => role switch
    {
        RoofStructuralRole.Hip => HipToken,
        RoofStructuralRole.Valley => ValleyToken,
        RoofStructuralRole.Ridge => RidgeToken,
        _ => string.Empty,
    };

    public static bool TryParseRole(
        string? token,
        out RoofStructuralRole role)
    {
        role = token switch
        {
            HipToken => RoofStructuralRole.Hip,
            ValleyToken => RoofStructuralRole.Valley,
            RidgeToken => RoofStructuralRole.Ridge,
            _ => RoofStructuralRole.Undefined,
        };
        return RoofStructuralIdentityRules.IsSupportedRole(role);
    }

    public static bool TryNormalizeOwnerReference(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;
        if (!long.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var handleValue) ||
            handleValue <= 0)
        {
            return false;
        }

        normalized = handleValue.ToString("X", CultureInfo.InvariantCulture);
        return true;
    }

    private static RoofStructuralGeneratedDataError Map(
        RoofStructuralIdentityError error) => error switch
        {
            RoofStructuralIdentityError.UnsupportedRole =>
                RoofStructuralGeneratedDataError.UnsupportedRole,
            RoofStructuralIdentityError.NonPositiveBoundaryEdgeId =>
                RoofStructuralGeneratedDataError.NonPositiveBoundaryEdgeId,
            RoofStructuralIdentityError.SameBoundaryEdgeId =>
                RoofStructuralGeneratedDataError.SameBoundaryEdgeId,
            RoofStructuralIdentityError.NonCanonicalBoundaryPair =>
                RoofStructuralGeneratedDataError.NonCanonicalBoundaryPair,
            _ => RoofStructuralGeneratedDataError.UnsupportedRole,
        };

    private static RoofStructuralGeneratedDataValidationResult Invalid(
        RoofStructuralGeneratedDataError error) => new(false, null, error);
}
