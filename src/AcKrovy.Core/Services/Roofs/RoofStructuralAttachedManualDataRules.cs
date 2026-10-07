using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Owner-scoped persistent ManualStructural identity. Mint only inside an accepted
/// native command transaction. Reads never invent a random identity.
/// </summary>
public static class RoofStructuralAttachedManualIdentityRules
{
    public static string Create() => Guid.NewGuid().ToString("N");

    public static bool TryNormalize(string? identity, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(identity)) return false;
        var trimmed = identity!.Trim();
        if (!Guid.TryParseExact(trimmed, "N", out var guid)) return false;
        normalized = guid.ToString("N");
        return true;
    }

    public static string PhysicalKey(string manualIdentity) =>
        "ManualStructural:" + (TryNormalize(manualIdentity, out var id) ? id : manualIdentity);

    /// <summary>Deterministic migrate for tests / recovery — not used on normal mint.</summary>
    public static string MigrateFromBinding(string ownerReference, string childBinding)
    {
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(
            "KROVY.StructuralAttachedManual.v1|" +
            ownerReference.ToUpperInvariant() + "|" +
            childBinding.ToUpperInvariant()));
        return BitConverter.ToString(bytes).Replace("-", string.Empty)
            .Substring(0, 32).ToLowerInvariant();
    }
}

/// <summary>CAD-neutral schema-1 validation for Structural AttachedManual metadata.</summary>
public static class RoofStructuralAttachedManualDataRules
{
    public static RoofStructuralAttachedManualDataValidationResult Create(
        string? ownerReference,
        string? manualIdentity,
        RoofStructuralLogicalKey sourceKey,
        RoofStructuralAttachedManualCreationKind creationKind,
        double widthMm,
        RoofStructuralHeightMode heightMode,
        double? explicitHeightMm,
        RoofStructuralManualPlacement? placement = null)
    {
        return ValidateStored(
            placement is null
                ? RoofStructuralAttachedManualDataSchema.IdentityVersion
                : RoofStructuralAttachedManualDataSchema.PlacementVersion,
            ownerReference,
            manualIdentity,
            RoofStructuralGeneratedDataRules.FormatRole(sourceKey.Role),
            sourceKey.BoundaryEdgeIdA,
            sourceKey.BoundaryEdgeIdB,
            creationKind.ToString(),
            widthMm,
            heightMode.ToString(),
            explicitHeightMm,
            placement);
    }

    public static RoofStructuralAttachedManualDataValidationResult ValidateStored(
        int schemaVersion,
        string? ownerReference,
        string? manualIdentity,
        string? roleToken,
        int boundaryEdgeIdA,
        int boundaryEdgeIdB,
        string? creationKindToken,
        double widthMm,
        string? heightModeToken,
        double? explicitHeightMm,
        RoofStructuralManualPlacement? placement = null)
    {
        if (schemaVersion is not RoofStructuralAttachedManualDataSchema.IdentityVersion
            and not RoofStructuralAttachedManualDataSchema.PlacementVersion)
            return Invalid(RoofStructuralAttachedManualDataError.UnsupportedSchemaVersion);
        if (string.IsNullOrWhiteSpace(ownerReference))
            return Invalid(RoofStructuralAttachedManualDataError.MissingOwnerReference);
        if (!RoofStructuralGeneratedDataRules.TryNormalizeOwnerReference(
                ownerReference, out var normalizedOwner))
            return Invalid(RoofStructuralAttachedManualDataError.MalformedOwnerReference);
        if (string.IsNullOrWhiteSpace(manualIdentity))
            return Invalid(RoofStructuralAttachedManualDataError.MissingManualIdentity);
        if (!RoofStructuralAttachedManualIdentityRules.TryNormalize(manualIdentity, out var identity))
            return Invalid(RoofStructuralAttachedManualDataError.MalformedManualIdentity);
        if (!RoofStructuralGeneratedDataRules.TryParseRole(roleToken, out var role) ||
            role is not (RoofStructuralRole.Hip or RoofStructuralRole.Valley))
            return Invalid(RoofStructuralAttachedManualDataError.UnsupportedRole);
        var key = new RoofStructuralLogicalKey(role, boundaryEdgeIdA, boundaryEdgeIdB);
        if (!RoofStructuralIdentityRules.TryValidateCanonical(key, out _))
            return Invalid(RoofStructuralAttachedManualDataError.NonCanonicalBoundaryPair);
        if (!Enum.TryParse(creationKindToken, ignoreCase: true, out RoofStructuralAttachedManualCreationKind creationKind))
            return Invalid(RoofStructuralAttachedManualDataError.MalformedValueType);
        if (!IsFinite(widthMm) || widthMm <= 0)
            return Invalid(RoofStructuralAttachedManualDataError.NonPositiveWidth);
        if (!Enum.TryParse(heightModeToken, ignoreCase: true, out RoofStructuralHeightMode heightMode))
            return Invalid(RoofStructuralAttachedManualDataError.UnsupportedHeightMode);
        if (heightMode == RoofStructuralHeightMode.Explicit &&
            (explicitHeightMm is null || !IsFinite(explicitHeightMm.Value) || explicitHeightMm.Value <= 0))
            return Invalid(RoofStructuralAttachedManualDataError.MissingExplicitHeight);
        if (schemaVersion == RoofStructuralAttachedManualDataSchema.PlacementVersion &&
            placement is null)
            return Invalid(RoofStructuralAttachedManualDataError.IncompletePayload);
        if (schemaVersion == RoofStructuralAttachedManualDataSchema.IdentityVersion &&
            placement is not null)
            return Invalid(RoofStructuralAttachedManualDataError.UnexpectedTrailingValue);
        if (placement is not null &&
            !RoofStructuralManualPlacementRules.IsValid(placement))
            return Invalid(RoofStructuralAttachedManualDataError.MalformedValueType);
        return new(
            true,
            new RoofStructuralAttachedManualData(
                schemaVersion, normalizedOwner, identity, role,
                key.BoundaryEdgeIdA, key.BoundaryEdgeIdB, creationKind,
                widthMm, heightMode,
                heightMode == RoofStructuralHeightMode.Explicit ? explicitHeightMm : null,
                placement),
            RoofStructuralAttachedManualDataError.None);
    }

    public static RoofStructuralAttachedManualDataValidationResult WithPlacement(
        RoofStructuralAttachedManualData data,
        RoofStructuralManualPlacement placement) =>
        ValidateStored(
            RoofStructuralAttachedManualDataSchema.PlacementVersion,
            data.RoofOwnerReference,
            data.ManualIdentity,
            RoofStructuralGeneratedDataRules.FormatRole(data.SourceRole),
            data.SourceBoundaryEdgeIdA,
            data.SourceBoundaryEdgeIdB,
            data.CreationKind.ToString(),
            data.WidthMm,
            data.HeightMode.ToString(),
            data.ExplicitHeightMm,
            placement);

    /// <summary>Accepted Manual MIRROR clone: new identity, same provenance/section, reflected stored frame.</summary>
    public static RoofStructuralAttachedManualDataValidationResult CreateMirroredClone(
        RoofStructuralAttachedManualData source,
        RoofSegment3D sourcePlan,
        RoofSegment3D mirroredPlan)
    {
        if (source.Placement is not { } frame)
            return Invalid(RoofStructuralAttachedManualDataError.IncompletePayload);
        if (!RoofStructuralManualPlacementRules.TryReflectFrame(frame, sourcePlan, mirroredPlan, out var reflected) ||
            reflected is null)
            return Invalid(RoofStructuralAttachedManualDataError.MalformedValueType);
        return Create(
            source.RoofOwnerReference,
            RoofStructuralAttachedManualIdentityRules.Create(),
            source.SourceLogicalKey,
            RoofStructuralAttachedManualCreationKind.Mirror,
            source.WidthMm,
            source.HeightMode,
            source.ExplicitHeightMm,
            reflected);
    }

    /// <summary>Accepted Manual COPY: translate the current stored frame, never the Generated provenance fold.</summary>
    public static RoofStructuralAttachedManualDataValidationResult CreateCopiedClone(
        RoofStructuralAttachedManualData source,
        RoofSegment3D sourcePlan,
        RoofSegment3D copiedPlan)
    {
        if (source.Placement is not { } frame)
            return Invalid(RoofStructuralAttachedManualDataError.IncompletePayload);
        if (new[] { sourcePlan.Start, sourcePlan.End, copiedPlan.Start, copiedPlan.End }
                .Any(point => !IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(point.Z)) ||
            !RoofStructuralManualPlacementRules.TryMatchRigidPlanCopy(sourcePlan, copiedPlan, out var dx, out var dy))
            return Invalid(RoofStructuralAttachedManualDataError.MalformedValueType);
        return Create(
            source.RoofOwnerReference,
            RoofStructuralAttachedManualIdentityRules.Create(),
            source.SourceLogicalKey,
            RoofStructuralAttachedManualCreationKind.Copy,
            source.WidthMm,
            source.HeightMode,
            source.ExplicitHeightMm,
            RoofStructuralManualPlacementRules.Translate(frame, dx, dy, 0));
    }

    public static bool IsManualPhysicalKey(string? structuralId) =>
        structuralId is not null &&
        structuralId.StartsWith("ManualStructural:", StringComparison.Ordinal);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofStructuralAttachedManualDataValidationResult Invalid(
        RoofStructuralAttachedManualDataError error) =>
        new(false, null, error);
}
