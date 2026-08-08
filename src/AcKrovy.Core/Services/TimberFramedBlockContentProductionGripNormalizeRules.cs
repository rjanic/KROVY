using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral P5A production GripOverrule policy.
/// Legacy R2 Combined (DIMNX/DIMPX) keeps Stage E dogleg + content-side.
/// Production R3 Combined (RIGHT/LEFT) is applicable for a MINIMAL path:
/// native MoveGripPointsAt (leader geometry authority) → sync layer-C
/// presentation from FINAL knee→frame landing via Decide() → inspect final
/// knee-side → swap R3_RIGHT/LEFT only. No dogleg rewrite, no forced 60°,
/// no K→D→I geometry normalize, and no vertex rewrite by our code.
/// Source-axis rotation refresh remains a separate lifecycle (may preserve
/// length-only presentation; do not conflate with annotation knee grip).
/// </summary>
public static class TimberFramedBlockContentProductionGripNormalizeRules
{
    /// <summary>
    /// Exact production callback order after native offset — same Stage E model
    /// for legacy R2. R3 uses content-side-only after native move.
    /// </summary>
    public static IReadOnlyList<string> NormalizeCallbackOrder { get; } =
        TimberFramedBlockContentGripStageProofRules.NormalizeCallbackOrder;

    /// <summary>
    /// Production BTR family prefix for current G5 R3 BlockContent.
    /// </summary>
    public const string ProductionBlockFamilyPrefix = "AK_KROVY_FBC_R3_";

    /// <summary>
    /// DEBUG proof RegApp / marker tokens that must never drive production
    /// applicability.
    /// </summary>
    public static IReadOnlyList<string> DebugProofMarkerTokens { get; } =
    [
        "FBC_GRIP_NORMALIZE",
        "FBC_GRIP_PASSTHROUGH",
        "FBC_GRIP_READONLY",
        "FBC_GRIP_UNDO",
        "FBC_CREATE_VERIFY",
    ];

    public static IReadOnlyList<string> DebugProofRegAppNames { get; } =
    [
        "AK_DEV_FBC_GRIP_NORMALIZE",
        "AK_DEV_FBC_GRIP_PASSTHROUGH",
        "AK_DEV_FBC_GRIP_READONLY",
        "AK_DEV_FBC_GRIP_UNDO",
        "AK_DEV_FBC_CREATE",
    ];

    /// <summary>
    /// Production applicability: R3 Combined RIGHT/LEFT (or legacy side-agnostic
    /// R3) and legacy R2 Combined DIMNX/DIMPX. ItemOnly / foreign / G4 fail closed.
    /// </summary>
    public static bool IsProductionApplicableBlockContent(
        string? blockNameOrRawKey,
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight)
    {
        if (!TimberFramedBlockContentStretchNormalizeRules.HasCombinedAttributeContract(
                hasItemNo,
                hasWidth,
                hasHeight))
        {
            return false;
        }

        if (TimberFramedBlockContentVariantRules.IsProductionR3Combined(
                blockNameOrRawKey))
        {
            return true;
        }

        return TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
            blockNameOrRawKey,
            hasItemNo,
            hasWidth,
            hasHeight);
    }

    public static bool IsProductionApplicableBlockContent(
        TimberFramedBlockContentR2VariantParse parse,
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
            parse,
            hasItemNo,
            hasWidth,
            hasHeight);

    /// <summary>
    /// R3 production path after native grip: content-variant swap only.
    /// </summary>
    public static bool IsR3ContentVariantOnlyPath(string? blockNameOrRawKey) =>
        TimberFramedBlockContentVariantRules.IsProductionR3Combined(blockNameOrRawKey);

    /// <summary>
    /// Legacy R2 path: dogleg → content-side (DIMNX/DIMPX).
    /// </summary>
    public static bool IsLegacyR2FullNormalizePath(string? blockNameOrRawKey) =>
        TimberFramedBlockContentVariantRules.IsP3R2CombinedStretchNormalizeTarget(
            blockNameOrRawKey);

    public static bool IsItemOnlyIgnored(TimberFramedBlockContentR2VariantParse parse) =>
        parse.IsItemOnly;

    public static bool IsDebugProofMarkerToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var value = token!;
        foreach (var known in DebugProofMarkerTokens)
        {
            if (value.StartsWith(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsDebugProofRegAppName(string? regAppName)
    {
        if (string.IsNullOrWhiteSpace(regAppName))
        {
            return false;
        }

        var value = regAppName!;
        foreach (var known in DebugProofRegAppNames)
        {
            if (string.Equals(value, known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// When a DEBUG grip proof is armed, production must not process the same
    /// DEBUG-marked entity (exclusivity).
    /// </summary>
    public static bool ShouldYieldToDebugProof(
        bool debugProofArmed,
        bool entityHasDebugProofMarker) =>
        debugProofArmed && entityHasDebugProofMarker;

    public static bool ShouldRegisterOverrule(bool alreadyRegistered) =>
        !alreadyRegistered;

    public static bool ShouldUnregisterOverrule(bool currentlyRegistered) =>
        currentlyRegistered;

    public static string FormatNormalizeOutcome(
        TimberFramedBlockContentGripNormalizeOutcome outcome) =>
        TimberFramedBlockContentGripStageProofRules.FormatNormalizeOutcome(outcome);
}
