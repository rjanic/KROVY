using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>CAD-neutral schema-1 validation and token rules for generated purlins.</summary>
public static class RoofAutomaticPurlinGeneratedDataRules
{
    public const string RidgeToken = "Ridge";
    public const string IntermediateToken = "Intermediate";

    /// <summary>
    /// Validates an S1 plan key for explicit writing. Intermediate endpoint-key order
    /// is canonicalized; all component identities themselves must already be canonical.
    /// </summary>
    public static RoofAutomaticPurlinGeneratedDataValidationResult Create(
        string? ownerReference,
        RoofAutomaticPurlinGeneratedKey? generatedKey)
    {
        if (generatedKey is RoofAutomaticPurlinRidgeKey ridge)
        {
            if (ridge.StructuralKey is null ||
                ridge.StructuralKey.Role != RoofStructuralRole.Ridge)
            {
                return Invalid(RoofAutomaticPurlinGeneratedDataError.InvalidRidgeStructuralKey);
            }

            return ValidateRidgeStored(
                RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion,
                ownerReference,
                RidgeToken,
                ridge.StructuralKey.BoundaryEdgeIdA,
                ridge.StructuralKey.BoundaryEdgeIdB);
        }

        if (generatedKey is not RoofAutomaticPurlinIntermediateKey intermediate)
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.UnsupportedRole);
        }

        var firstIsValid = RoofAutomaticPurlinBoundaryKeyRules.TryFormat(
            intermediate.EndpointBoundaryKeyA,
            out _,
            out var firstError);
        var secondIsValid = RoofAutomaticPurlinBoundaryKeyRules.TryFormat(
            intermediate.EndpointBoundaryKeyB,
            out _,
            out var secondError);
        if (!firstIsValid || !secondIsValid)
        {
            return Invalid(MapEndpointError(
                firstError != RoofAutomaticPurlinBoundaryKeyError.None
                    ? firstError
                    : secondError));
        }

        var first = intermediate.EndpointBoundaryKeyA;
        var second = intermediate.EndpointBoundaryKeyB;
        if (first == second)
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.IdenticalEndpointBoundaryKeys);
        }

        if (RoofAutomaticPurlinBoundaryKeyRules.Compare(first, second) > 0)
        {
            (first, second) = (second, first);
        }

        var canonicalKey = intermediate with
        {
            EndpointBoundaryKeyA = first,
            EndpointBoundaryKeyB = second,
        };
        var commonError = ValidateCommon(ownerReference, out var normalizedOwner);
        if (commonError != RoofAutomaticPurlinGeneratedDataError.None)
        {
            return Invalid(commonError);
        }

        var keyError = ValidateIntermediateKey(canonicalKey, requireCanonicalOrder: true);
        return keyError == RoofAutomaticPurlinGeneratedDataError.None
            ? Valid(normalizedOwner, canonicalKey)
            : Invalid(keyError);
    }

    public static RoofAutomaticPurlinGeneratedDataValidationResult ValidateRidgeStored(
        int schemaVersion,
        string? ownerReference,
        string? roleToken,
        int boundaryEdgeIdA,
        int boundaryEdgeIdB)
    {
        if (schemaVersion != RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion)
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.UnsupportedSchemaVersion);
        }

        if (!string.Equals(roleToken, RidgeToken, StringComparison.Ordinal))
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.UnsupportedRole);
        }

        var commonError = ValidateCommon(ownerReference, out var normalizedOwner);
        if (commonError != RoofAutomaticPurlinGeneratedDataError.None)
        {
            return Invalid(commonError);
        }

        var structuralKey = new RoofStructuralLogicalKey(
            RoofStructuralRole.Ridge,
            boundaryEdgeIdA,
            boundaryEdgeIdB);
        if (!RoofStructuralIdentityRules.TryValidateCanonical(
                structuralKey,
                out var structuralError))
        {
            return Invalid(MapStructuralError(structuralError));
        }

        return Valid(
            normalizedOwner,
            new RoofAutomaticPurlinRidgeKey(structuralKey));
    }

    public static RoofAutomaticPurlinGeneratedDataValidationResult ValidateIntermediateStored(
        int schemaVersion,
        string? ownerReference,
        string? roleToken,
        string? layoutItemId,
        int sourceFaceBoundaryEdgeId,
        string? endpointBoundaryKeyA,
        string? endpointBoundaryKeyB)
    {
        if (schemaVersion != RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion)
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.UnsupportedSchemaVersion);
        }

        if (!string.Equals(roleToken, IntermediateToken, StringComparison.Ordinal))
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.UnsupportedRole);
        }

        var commonError = ValidateCommon(ownerReference, out var normalizedOwner);
        if (commonError != RoofAutomaticPurlinGeneratedDataError.None)
        {
            return Invalid(commonError);
        }

        if (!RoofAutomaticPurlinLayoutItemIdentity.TryNormalize(
                layoutItemId,
                out var normalizedLayoutItemId))
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.MalformedLayoutItemId);
        }

        if (!string.Equals(layoutItemId, normalizedLayoutItemId, StringComparison.Ordinal))
        {
            return Invalid(RoofAutomaticPurlinGeneratedDataError.NonCanonicalLayoutItemId);
        }

        if (!RoofAutomaticPurlinBoundaryKeyRules.TryParse(
                endpointBoundaryKeyA,
                out var first,
                out var firstError))
        {
            return Invalid(MapEndpointError(firstError));
        }

        if (!RoofAutomaticPurlinBoundaryKeyRules.TryParse(
                endpointBoundaryKeyB,
                out var second,
                out var secondError))
        {
            return Invalid(MapEndpointError(secondError));
        }

        var key = new RoofAutomaticPurlinIntermediateKey(
            normalizedLayoutItemId,
            sourceFaceBoundaryEdgeId,
            first!,
            second!);
        var keyError = ValidateIntermediateKey(key, requireCanonicalOrder: true);
        return keyError == RoofAutomaticPurlinGeneratedDataError.None
            ? Valid(normalizedOwner, key)
            : Invalid(keyError);
    }

    public static string FormatRole(RoofAutomaticPurlinGeneratorRole role) => role switch
    {
        RoofAutomaticPurlinGeneratorRole.Ridge => RidgeToken,
        RoofAutomaticPurlinGeneratorRole.Intermediate => IntermediateToken,
        _ => string.Empty,
    };

    public static bool TryParseRole(
        string? token,
        out RoofAutomaticPurlinGeneratorRole role)
    {
        role = token switch
        {
            RidgeToken => RoofAutomaticPurlinGeneratorRole.Ridge,
            IntermediateToken => RoofAutomaticPurlinGeneratorRole.Intermediate,
            _ => default,
        };
        return token is RidgeToken or IntermediateToken;
    }

    private static RoofAutomaticPurlinGeneratedDataError ValidateIntermediateKey(
        RoofAutomaticPurlinIntermediateKey key,
        bool requireCanonicalOrder)
    {
        if (!RoofAutomaticPurlinLayoutItemIdentity.TryNormalize(
                key.LayoutItemId,
                out var normalizedLayoutItemId))
        {
            return RoofAutomaticPurlinGeneratedDataError.MalformedLayoutItemId;
        }

        if (!string.Equals(key.LayoutItemId, normalizedLayoutItemId, StringComparison.Ordinal))
        {
            return RoofAutomaticPurlinGeneratedDataError.NonCanonicalLayoutItemId;
        }

        if (key.SourceFaceBoundaryEdgeId <= 0)
        {
            return RoofAutomaticPurlinGeneratedDataError.NonPositiveSourceFaceBoundaryEdgeId;
        }

        if (key.EndpointBoundaryKeyA == key.EndpointBoundaryKeyB)
        {
            return RoofAutomaticPurlinGeneratedDataError.IdenticalEndpointBoundaryKeys;
        }

        if (requireCanonicalOrder &&
            RoofAutomaticPurlinBoundaryKeyRules.Compare(
                key.EndpointBoundaryKeyA,
                key.EndpointBoundaryKeyB) > 0)
        {
            return RoofAutomaticPurlinGeneratedDataError.NonCanonicalEndpointBoundaryOrder;
        }

        return RoofAutomaticPurlinGeneratedDataError.None;
    }

    private static RoofAutomaticPurlinGeneratedDataError ValidateCommon(
        string? ownerReference,
        out string normalizedOwner)
    {
        normalizedOwner = string.Empty;
        if (string.IsNullOrWhiteSpace(ownerReference))
        {
            return RoofAutomaticPurlinGeneratedDataError.MissingOwnerReference;
        }

        return RoofStructuralGeneratedDataRules.TryNormalizeOwnerReference(
            ownerReference,
            out normalizedOwner)
            ? RoofAutomaticPurlinGeneratedDataError.None
            : RoofAutomaticPurlinGeneratedDataError.MalformedOwnerReference;
    }

    private static RoofAutomaticPurlinGeneratedDataError MapStructuralError(
        RoofStructuralIdentityError error) => error switch
        {
            RoofStructuralIdentityError.NonPositiveBoundaryEdgeId =>
                RoofAutomaticPurlinGeneratedDataError.NonPositiveBoundaryEdgeId,
            RoofStructuralIdentityError.SameBoundaryEdgeId =>
                RoofAutomaticPurlinGeneratedDataError.SameBoundaryEdgeId,
            RoofStructuralIdentityError.NonCanonicalBoundaryPair =>
                RoofAutomaticPurlinGeneratedDataError.NonCanonicalBoundaryPair,
            _ => RoofAutomaticPurlinGeneratedDataError.InvalidRidgeStructuralKey,
        };

    private static RoofAutomaticPurlinGeneratedDataError MapEndpointError(
        RoofAutomaticPurlinBoundaryKeyError error) => error switch
        {
            RoofAutomaticPurlinBoundaryKeyError.UnsupportedKind =>
                RoofAutomaticPurlinGeneratedDataError.UnsupportedEndpointKind,
            RoofAutomaticPurlinBoundaryKeyError.NonPositiveBoundaryEdgeId =>
                RoofAutomaticPurlinGeneratedDataError.NonPositiveBoundaryEdgeId,
            RoofAutomaticPurlinBoundaryKeyError.SameBoundaryEdgeId =>
                RoofAutomaticPurlinGeneratedDataError.SameBoundaryEdgeId,
            RoofAutomaticPurlinBoundaryKeyError.NonCanonicalBoundaryPair =>
                RoofAutomaticPurlinGeneratedDataError.NonCanonicalBoundaryPair,
            _ => RoofAutomaticPurlinGeneratedDataError.MalformedEndpointBoundaryKey,
        };

    private static RoofAutomaticPurlinGeneratedDataValidationResult Valid(
        string ownerReference,
        RoofAutomaticPurlinGeneratedKey key) => new(
            true,
            new RoofAutomaticPurlinGeneratedData(
                RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion,
                ownerReference,
                key),
            RoofAutomaticPurlinGeneratedDataError.None);

    private static RoofAutomaticPurlinGeneratedDataValidationResult Invalid(
        RoofAutomaticPurlinGeneratedDataError error) => new(false, null, error);
}
