using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral Stage D/E grip proof policy: would-normalize reporters,
/// STATUS classifier, callback order labels. Reuses P4A eligibility /
/// dogleg / K→D→I rules — does not change those algorithms.
/// </summary>
public static class TimberFramedBlockContentGripStageProofRules
{
    public const string DebugReadOnlyMarkerToken = "FBC_GRIP_READONLY";
    public const string DebugNormalizeMarkerToken = "FBC_GRIP_NORMALIZE";
    public const string RepresentativeCaseKey = "P4B-CIRCLE-COMB-R-90-D50";

    public const string StatePostGripCorrect = "STATE_POST_GRIP_CORRECT";
    public const string StatePostGripWrong = "STATE_POST_GRIP_WRONG";
    public const string StateCallbackFailed = "STATE_CALLBACK_FAILED";
    public const string StateUnknown = "STATE_UNKNOWN";

    /// <summary>
    /// Exact Stage E callback order after native offset.
    /// </summary>
    public static IReadOnlyList<string> NormalizeCallbackOrder { get; } =
    [
        "EnterReentrancyGuard",
        "BaseMoveGripPointsAt",
        "InspectWriteOpenCallbackMLeader",
        "SharedDoglegNormalize",
        "SharedContentSideNormalize",
        "ExitReentrancyGuard",
    ];

    /// <summary>
    /// Stage D callback order: base move then read-only inspection only.
    /// </summary>
    public static IReadOnlyList<string> ReadOnlyCallbackOrder { get; } =
    [
        "EnterReentrancyGuard",
        "BaseMoveGripPointsAt",
        "ReadOnlyInspection",
        "ExitReentrancyGuard",
    ];

    public static bool IsApplicableBlockContent(
        string? blockNameOrRawKey,
        bool hasItemNo,
        bool hasWidth,
        bool hasHeight) =>
        TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
            blockNameOrRawKey,
            hasItemNo,
            hasWidth,
            hasHeight);

    public static bool IsItemOnlyNoOp(TimberFramedBlockContentR2VariantParse parse) =>
        parse.IsItemOnly;

    /// <summary>
    /// Would dogleg normalize change geometry/direction? Mirrors shared
    /// dogleg service no-op gate without writing.
    /// </summary>
    public static bool WouldNormalizeDogleg(
        bool geometryResolved,
        bool mirrored,
        bool directionAlreadyMatches) =>
        geometryResolved && (mirrored || !directionAlreadyMatches);

    /// <summary>
    /// Would content-side normalize swap BlockContentId?
    /// </summary>
    public static bool WouldNormalizeContentSide(
        TimberFramedBlockContentDimensionColumnMirrorDecision decision) =>
        TimberFramedBlockContentStretchNormalizeRules.ShouldSwapContentSide(decision);

    public static TimberFramedBlockContentGripNormalizeProofState ClassifyNormalizeState(
        bool callbackFailed,
        string? trackedHandle,
        string? currentHandle,
        bool? currentPlacementCorrect)
    {
        if (callbackFailed)
        {
            return TimberFramedBlockContentGripNormalizeProofState.CallbackFailed;
        }

        if (string.IsNullOrWhiteSpace(trackedHandle) ||
            string.IsNullOrWhiteSpace(currentHandle) ||
            !string.Equals(trackedHandle, currentHandle, StringComparison.OrdinalIgnoreCase))
        {
            return TimberFramedBlockContentGripNormalizeProofState.Unknown;
        }

        if (currentPlacementCorrect is null)
        {
            return TimberFramedBlockContentGripNormalizeProofState.Unknown;
        }

        return currentPlacementCorrect.Value
            ? TimberFramedBlockContentGripNormalizeProofState.PostGripCorrect
            : TimberFramedBlockContentGripNormalizeProofState.PostGripWrong;
    }

    public static string FormatNormalizeState(
        TimberFramedBlockContentGripNormalizeProofState state) =>
        state switch
        {
            TimberFramedBlockContentGripNormalizeProofState.PostGripCorrect =>
                StatePostGripCorrect,
            TimberFramedBlockContentGripNormalizeProofState.PostGripWrong =>
                StatePostGripWrong,
            TimberFramedBlockContentGripNormalizeProofState.CallbackFailed =>
                StateCallbackFailed,
            _ => StateUnknown,
        };

    public static string FormatDimensionColumnSideToken(
        TimberFramedBlockContentDimensionColumnSide? side) =>
        TimberFramedBlockContentGripUndoProofRules.FormatDimensionColumnSideToken(side);

    /// <summary>
    /// Deferred same-undo host sequence documentation. UNDO_PROOF stays
    /// hard-disabled — Stage E normalize STATUS only; REDO is unresolved.
    /// </summary>
    public static string SameUndoHostSequenceDeferredDocumentation { get; } =
        "POST→U→PRE→MREDO→POST deferred: do NOT re-enable " +
        "AK_DEV_FBC_UNDO_PROOF_SETUP; no SendStringToExecute / nested commands / " +
        "manual UNDO marks. REDO preservation is a separate unresolved limitation.";

    public const string StageEBlockedUnresolvedExceptionsMessage =
        "Stage E blocked: read-only grip proof has unresolved callback exceptions.";

    /// <summary>
    /// Build-level Stage E capability — not a persisted workstation flag.
    /// </summary>
    public const string StageEImplementationMode =
        "DirectCallbackEntityWithTransientSkip";

    public const string StageEArmReasonNotRunAllowed =
        "current build uses direct callback entity and non-throwing transient handling";

    public const string StageEArmReasonPassedAllowed =
        "Stage D proved zero-exception readiness in this AppDomain";

    /// <summary>
    /// Stage D zero-exception counter proof: at least one successful inspection
    /// and no callback exceptions. Used to promote readiness to Passed only —
    /// never treat counter silence (NotRun) as Failed.
    /// </summary>
    public static bool IsStageDZeroExceptionReady(
        int exceptionCount,
        int inspectionSuccessCount) =>
        exceptionCount == 0 && inspectionSuccessCount >= 1;

    /// <summary>
    /// Classify sticky Stage D readiness from live counters. ExceptionCount&gt;0
    /// is Failed; proven zero-exception success is Passed; otherwise NotRun
    /// (including fresh NETLOAD / cleared counters).
    /// </summary>
    public static TimberFramedBlockContentGripStageDReadiness ClassifyStageDReadiness(
        int exceptionCount,
        int inspectionSuccessCount)
    {
        if (exceptionCount > 0)
        {
            return TimberFramedBlockContentGripStageDReadiness.Failed;
        }

        if (IsStageDZeroExceptionReady(exceptionCount, inspectionSuccessCount))
        {
            return TimberFramedBlockContentGripStageDReadiness.Passed;
        }

        return TimberFramedBlockContentGripStageDReadiness.NotRun;
    }

    /// <summary>
    /// Stage E arm gate: Failed blocks; Passed and NotRun allow.
    /// NotRun is allowed because Stage E owns direct callback-entity normalize
    /// and its own exception counters (no cross-session Stage D prerequisite).
    /// </summary>
    public static TimberFramedBlockContentGripStageEArmDecision DecideStageEArm(
        TimberFramedBlockContentGripStageDReadiness readiness,
        out string reason)
    {
        switch (readiness)
        {
            case TimberFramedBlockContentGripStageDReadiness.Failed:
                reason = StageEBlockedUnresolvedExceptionsMessage;
                return TimberFramedBlockContentGripStageEArmDecision.Blocked;
            case TimberFramedBlockContentGripStageDReadiness.Passed:
                reason = StageEArmReasonPassedAllowed;
                return TimberFramedBlockContentGripStageEArmDecision.Allowed;
            default:
                reason = StageEArmReasonNotRunAllowed;
                return TimberFramedBlockContentGripStageEArmDecision.Allowed;
        }
    }

    public static string FormatStageDReadiness(
        TimberFramedBlockContentGripStageDReadiness readiness) =>
        readiness switch
        {
            TimberFramedBlockContentGripStageDReadiness.Passed => "Passed",
            TimberFramedBlockContentGripStageDReadiness.Failed => "Failed",
            _ => "NotRun",
        };

    public static string FormatStageEArmDecision(
        TimberFramedBlockContentGripStageEArmDecision decision) =>
        decision == TimberFramedBlockContentGripStageEArmDecision.Blocked
            ? "Blocked"
            : "Allowed";

    public static string FormatInspectionOutcome(
        TimberFramedBlockContentGripReadOnlyInspectionOutcome outcome) =>
        outcome switch
        {
            TimberFramedBlockContentGripReadOnlyInspectionOutcome.Success =>
                "Success",
            TimberFramedBlockContentGripReadOnlyInspectionOutcome.NotApplicable =>
                "NotApplicable",
            TimberFramedBlockContentGripReadOnlyInspectionOutcome.TransientGeometryNotReady =>
                "TransientGeometryNotReady",
            TimberFramedBlockContentGripReadOnlyInspectionOutcome.ObjectUnavailable =>
                "ObjectUnavailable",
            TimberFramedBlockContentGripReadOnlyInspectionOutcome.InvalidContract =>
                "InvalidContract",
            _ => "Failed",
        };

    public static string FormatNormalizeOutcome(
        TimberFramedBlockContentGripNormalizeOutcome outcome) =>
        outcome switch
        {
            TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged =>
                "SuccessChanged",
            TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp =>
                "SuccessNoOp",
            TimberFramedBlockContentGripNormalizeOutcome.TransientSkip =>
                "TransientSkip",
            TimberFramedBlockContentGripNormalizeOutcome.NotApplicable =>
                "NotApplicable",
            _ => "Failed",
        };
}
