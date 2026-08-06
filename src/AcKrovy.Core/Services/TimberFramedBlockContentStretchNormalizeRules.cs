using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral P4A stretch-normalize policy: filter, command activation, and
/// dogleg → content-side operation order. No CAD host types.
/// </summary>
public static class TimberFramedBlockContentStretchNormalizeRules
{
    public const string DoglegStep = "Dogleg";
    public const string ContentSideStep = "ContentSide";

    /// <summary>
    /// Fixed normalize order after grip STRETCH. Content-side must evaluate
    /// geometry after dogleg normalize.
    /// </summary>
    public static IReadOnlyList<string> NormalizeOperationOrder { get; } =
        [DoglegStep, ContentSideStep];

    public static bool HasCombinedAttributeContract(
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        hasItemNo && hasWidth && hasHeight;

    public static bool IsEligibleBlockContent(
        string? blockNameOrRawKey,
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        HasCombinedAttributeContract(hasItemNo, hasWidth, hasHeight) &&
        TimberFramedBlockContentVariantRules.IsP3R2CombinedStretchNormalizeTarget(
            blockNameOrRawKey);

    public static bool IsEligibleBlockContent(
        TimberFramedBlockContentR2VariantParse parse,
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        parse.IsP3R2CombinedTarget &&
        HasCombinedAttributeContract(hasItemNo, hasWidth, hasHeight);

    public static string NormalizeCommandName(string? globalCommandName)
    {
        if (string.IsNullOrWhiteSpace(globalCommandName))
        {
            return string.Empty;
        }

        return globalCommandName!.Trim().TrimStart('_', '.').ToUpperInvariant();
    }

    public static bool ShouldRunAutomation(
        bool proofEnabled,
        string? globalCommandName,
        IReadOnlyCollection<string> confirmedCommandNames)
    {
        if (!proofEnabled)
        {
            return false;
        }

        var normalized = NormalizeCommandName(globalCommandName);
        if (normalized.Length == 0 || confirmedCommandNames.Count == 0)
        {
            return false;
        }

        foreach (var confirmed in confirmedCommandNames)
        {
            if (string.Equals(
                    NormalizeCommandName(confirmed),
                    normalized,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static TimberFramedBlockContentDimensionColumnSide OppositeColumnSide(
        TimberFramedBlockContentDimensionColumnSide side) =>
        TimberFramedBlockContentVariantRules.OppositeDimensionColumnSide(side);

    /// <summary>
    /// World-space mirror decision: already correct → no BlockContentId change.
    /// </summary>
    public static bool IsContentSideNoOp(
        TimberFramedBlockContentDimensionColumnMirrorDecision decision) =>
        decision == TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp;

    /// <summary>
    /// World-space mirror decision: current wrong, mirrored correct → swap.
    /// </summary>
    public static bool ShouldSwapContentSide(
        TimberFramedBlockContentDimensionColumnMirrorDecision decision) =>
        decision == TimberFramedBlockContentDimensionColumnMirrorDecision.Swap;

    public static bool IsSecondEvaluationUnchanged(bool firstChanged, bool secondChanged) =>
        !secondChanged || !firstChanged;
}
