using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Host request for an immutable G5 BlockContent BTR family (R2).
/// Side / angle / annotation denominator are intentionally absent.
/// Combined requires <see cref="DimensionColumnSide"/>; ItemOnly omits it.
/// </summary>
internal sealed record AutoCadFramedBlockContentRequest(
    TimberFramedBlockContentKind ContentKind,
    TimberFramedBlockContentPresentation Presentation,
    string ItemTextStyleName,
    string DimensionTextStyleName,
    double ItemPaperHeightMm,
    double DimensionPaperHeightMm,
    ObjectId ItemTextStyleId,
    ObjectId DimensionTextStyleId,
    string ItemTextForFrameSizing,
    TimberFramedBlockContentDimensionColumnSide? DimensionColumnSide = null)
{
    public AutoCadFramedBlockContentRequest Normalize()
    {
        TimberFramedBlockContentDefinitionRules.ValidateRequest(
            ContentKind,
            Presentation);
        if (!TimberAnnotationTextSettingsRules.IsValidTextStyleName(ItemTextStyleName))
        {
            throw new ArgumentException(
                "Item text style identity is required.",
                nameof(ItemTextStyleName));
        }
        if (!TimberAnnotationTextSettingsRules.IsValidTextStyleName(
                DimensionTextStyleName))
        {
            throw new ArgumentException(
                "Dimension text style identity is required.",
                nameof(DimensionTextStyleName));
        }
        if (!TimberAnnotationTextSettingsRules.IsValidItemCodePaperHeightMm(
                ItemPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(ItemPaperHeightMm));
        }
        if (!TimberAnnotationTextSettingsRules.IsValidDimensionPaperHeightMm(
                DimensionPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(DimensionPaperHeightMm));
        }
        if (ItemTextStyleId.IsNull || !ItemTextStyleId.IsValid)
        {
            throw new ArgumentException(
                "Item TextStyleId must be a valid ObjectId.",
                nameof(ItemTextStyleId));
        }
        if (Presentation == TimberFramedBlockContentPresentation.Combined &&
            (DimensionTextStyleId.IsNull || !DimensionTextStyleId.IsValid))
        {
            throw new ArgumentException(
                "Dimension TextStyleId must be a valid ObjectId for Combined.",
                nameof(DimensionTextStyleId));
        }

        if (Presentation == TimberFramedBlockContentPresentation.Combined)
        {
            if (DimensionColumnSide is null)
            {
                throw new ArgumentNullException(
                    nameof(DimensionColumnSide),
                    "Combined R2 definitions require a dimension column side.");
            }

            if (!Enum.IsDefined(
                    typeof(TimberFramedBlockContentDimensionColumnSide),
                    DimensionColumnSide.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(DimensionColumnSide));
            }
        }

        return this with
        {
            ItemTextStyleName = ItemTextStyleName.Trim(),
            DimensionTextStyleName = DimensionTextStyleName.Trim(),
            ItemTextForFrameSizing = ItemTextForFrameSizing?.Trim() ?? string.Empty,
            DimensionColumnSide =
                Presentation == TimberFramedBlockContentPresentation.Combined
                    ? DimensionColumnSide
                    : null,
        };
    }
}

internal enum AutoCadFramedBlockContentResultKind
{
    ReusedExisting,
    ReusedCollisionVariant,
    CreatedNew,
    CreatedCollisionVariant,
    InvalidRequest,
    DatabaseMismatch,
    ExistingDefinitionInvalid,
}

internal sealed record AutoCadFramedBlockContentResult(
    AutoCadFramedBlockContentResultKind Kind,
    string? RawVariantKey,
    string? CanonicalBlockName,
    string? ResolvedBlockName,
    ObjectId? BlockTableRecordId,
    AutoCadDatabaseIdentityToken? DatabaseIdentity,
    string DiagnosticReason)
{
    public bool Succeeded => Kind is
        AutoCadFramedBlockContentResultKind.ReusedExisting or
        AutoCadFramedBlockContentResultKind.ReusedCollisionVariant or
        AutoCadFramedBlockContentResultKind.CreatedNew or
        AutoCadFramedBlockContentResultKind.CreatedCollisionVariant;

    public bool IsCollision => Kind is
        AutoCadFramedBlockContentResultKind.ReusedCollisionVariant or
        AutoCadFramedBlockContentResultKind.CreatedCollisionVariant;

    public static AutoCadFramedBlockContentResult Reused(
        string rawKey,
        string canonicalName,
        string resolvedName,
        ObjectId blockId,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        bool collision,
        string reason) =>
        new(
            collision
                ? AutoCadFramedBlockContentResultKind.ReusedCollisionVariant
                : AutoCadFramedBlockContentResultKind.ReusedExisting,
            rawKey,
            canonicalName,
            resolvedName,
            blockId,
            databaseIdentity,
            reason);

    public static AutoCadFramedBlockContentResult Created(
        string rawKey,
        string canonicalName,
        string resolvedName,
        ObjectId blockId,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        bool collision,
        string reason) =>
        new(
            collision
                ? AutoCadFramedBlockContentResultKind.CreatedCollisionVariant
                : AutoCadFramedBlockContentResultKind.CreatedNew,
            rawKey,
            canonicalName,
            resolvedName,
            blockId,
            databaseIdentity,
            reason);

    public static AutoCadFramedBlockContentResult InvalidRequest(
        string? rawKey,
        string? canonicalName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        new(
            AutoCadFramedBlockContentResultKind.InvalidRequest,
            rawKey,
            canonicalName,
            null,
            null,
            databaseIdentity,
            reason);

    public static AutoCadFramedBlockContentResult DatabaseMismatch(
        string? rawKey,
        string? canonicalName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        new(
            AutoCadFramedBlockContentResultKind.DatabaseMismatch,
            rawKey,
            canonicalName,
            null,
            null,
            databaseIdentity,
            reason);

    public static AutoCadFramedBlockContentResult ExistingDefinitionInvalid(
        string rawKey,
        string canonicalName,
        AutoCadDatabaseIdentityToken? databaseIdentity,
        string reason) =>
        new(
            AutoCadFramedBlockContentResultKind.ExistingDefinitionInvalid,
            rawKey,
            canonicalName,
            null,
            null,
            databaseIdentity,
            reason);
}

internal enum AutoCadFramedBlockContentCandidateState
{
    Missing,
    Matching,
    Invalid,
}

internal enum AutoCadFramedBlockContentCollisionDecisionKind
{
    Create,
    Reuse,
    Exhausted,
}

internal sealed record AutoCadFramedBlockContentCollisionDecision(
    AutoCadFramedBlockContentCollisionDecisionKind Kind,
    string CandidateName,
    bool IsCollision,
    int CollisionAttempt);

/// <summary>
/// Naming, collision, and validation policy for AK_KROVY_FBC_* definitions.
/// </summary>
internal static partial class AutoCadFramedBlockContentPolicy
{
    public const int MaximumSafeSymbolNameLength =
        TimberFramedBlockContentVariantRules.MaximumSafeBlockNameLength;
    public const int MaximumCollisionAttempts = 64;
    public const int FingerprintHexLength = 12;
    public const string NameFamilyPrefix = "AK_KROVY_FBC_";

    public static string CreateCanonicalName(string rawVariantKey) =>
        TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            rawVariantKey,
            MaximumSafeSymbolNameLength);

    public static string CreateCollisionName(
        string rawVariantKey,
        int collisionAttempt)
    {
        if (collisionAttempt < 1 || collisionAttempt > MaximumCollisionAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(collisionAttempt));
        }

        var canonical = CreateCanonicalName(rawVariantKey);
        var payload = string.Concat(
            "fbc-collision|",
            rawVariantKey,
            "|attempt=",
            collisionAttempt.ToString(CultureInfo.InvariantCulture));
        var suffix = CreateFingerprint(payload);
        var candidate = $"{canonical}_C{suffix}";
        if (candidate.Length <= MaximumSafeSymbolNameLength &&
            IsSafeSymbolName(candidate))
        {
            return candidate;
        }

        return ValidateGeneratedName(
            $"{NameFamilyPrefix}C{suffix}");
    }

    public static bool IsSafeSymbolName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= MaximumSafeSymbolNameLength &&
        SafeSymbolNameRegex().IsMatch(name);

    public static bool IsProductionFamilyName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.StartsWith(NameFamilyPrefix, StringComparison.Ordinal) &&
        !name.StartsWith("AK_G5C_", StringComparison.Ordinal) &&
        !name.StartsWith("AK_DEV_", StringComparison.Ordinal);

    public static AutoCadFramedBlockContentCollisionDecision Select(
        string rawVariantKey,
        Func<string, AutoCadFramedBlockContentCandidateState> inspect)
    {
        ArgumentNullException.ThrowIfNull(rawVariantKey);
        ArgumentNullException.ThrowIfNull(inspect);

        var canonicalName = CreateCanonicalName(rawVariantKey);
        var canonicalState = inspect(canonicalName);
        if (canonicalState == AutoCadFramedBlockContentCandidateState.Missing)
        {
            return new AutoCadFramedBlockContentCollisionDecision(
                AutoCadFramedBlockContentCollisionDecisionKind.Create,
                canonicalName,
                false,
                0);
        }
        if (canonicalState == AutoCadFramedBlockContentCandidateState.Matching)
        {
            return new AutoCadFramedBlockContentCollisionDecision(
                AutoCadFramedBlockContentCollisionDecisionKind.Reuse,
                canonicalName,
                false,
                0);
        }

        for (var attempt = 1; attempt <= MaximumCollisionAttempts; attempt++)
        {
            var collisionName = CreateCollisionName(rawVariantKey, attempt);
            var state = inspect(collisionName);
            if (state == AutoCadFramedBlockContentCandidateState.Missing)
            {
                return new AutoCadFramedBlockContentCollisionDecision(
                    AutoCadFramedBlockContentCollisionDecisionKind.Create,
                    collisionName,
                    true,
                    attempt);
            }
            if (state == AutoCadFramedBlockContentCandidateState.Matching)
            {
                return new AutoCadFramedBlockContentCollisionDecision(
                    AutoCadFramedBlockContentCollisionDecisionKind.Reuse,
                    collisionName,
                    true,
                    attempt);
            }
        }

        return new AutoCadFramedBlockContentCollisionDecision(
            AutoCadFramedBlockContentCollisionDecisionKind.Exhausted,
            canonicalName,
            true,
            MaximumCollisionAttempts);
    }

    private static string CreateFingerprint(string payload)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(digest)[..FingerprintHexLength];
    }

    private static string ValidateGeneratedName(string name) =>
        IsSafeSymbolName(name)
            ? name
            : throw new InvalidOperationException(
                "Generated FBC block name is not a safe AutoCAD symbol name.");

    [GeneratedRegex("^[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSymbolNameRegex();
}
