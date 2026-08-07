using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Portable Stage D/E grip proof policy. Host-only behavior is proven via
/// AK_DEV_FBC_GRIP_READONLY_* / AK_DEV_FBC_GRIP_NORMALIZE_*.
/// </summary>
public sealed class TimberFramedBlockContentGripStageProofRulesTests
{
    [Fact]
    public void CallbackOrders_ReadOnlyThenNormalize_BaseBeforeSharedNormalize()
    {
        Assert.Equal(
            [
                "EnterReentrancyGuard",
                "BaseMoveGripPointsAt",
                "ReadOnlyInspection",
                "ExitReentrancyGuard",
            ],
            TimberFramedBlockContentGripStageProofRules.ReadOnlyCallbackOrder);

        Assert.Equal(
            [
                "EnterReentrancyGuard",
                "BaseMoveGripPointsAt",
                "InspectWriteOpenCallbackMLeader",
                "SharedDoglegNormalize",
                "SharedContentSideNormalize",
                "ExitReentrancyGuard",
            ],
            TimberFramedBlockContentGripStageProofRules.NormalizeCallbackOrder);

        Assert.Equal(
            TimberFramedBlockContentStretchNormalizeRules.NormalizeOperationOrder,
            new[]
            {
                TimberFramedBlockContentStretchNormalizeRules.DoglegStep,
                TimberFramedBlockContentStretchNormalizeRules.ContentSideStep,
            });
    }

    [Fact]
    public void Applicability_R2CombinedAccepted_ItemOnlyForeignRejected()
    {
        var dimnx = CreateCombinedName(
            TimberFramedBlockContentKind.Circle,
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
        Assert.True(
            TimberFramedBlockContentGripStageProofRules.IsApplicableBlockContent(
                dimnx,
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));

        var itemOnly = TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateRawKey(
                TimberFramedBlockContentKind.Circle,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.ItemOnly));
        Assert.True(
            TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                itemOnly,
                out var parse));
        Assert.True(
            TimberFramedBlockContentGripStageProofRules.IsItemOnlyNoOp(parse));
        Assert.False(
            TimberFramedBlockContentGripStageProofRules.IsApplicableBlockContent(
                itemOnly,
                hasItemNo: true,
                hasWidth: false,
                hasHeight: false));
        Assert.False(
            TimberFramedBlockContentGripStageProofRules.IsApplicableBlockContent(
                "FOREIGN_BLOCK",
                hasItemNo: true,
                hasWidth: true,
                hasHeight: true));
    }

    [Fact]
    public void WouldNormalize_DoglegAndContentSideGates()
    {
        Assert.False(
            TimberFramedBlockContentGripStageProofRules.WouldNormalizeDogleg(
                geometryResolved: true,
                mirrored: false,
                directionAlreadyMatches: true));
        Assert.True(
            TimberFramedBlockContentGripStageProofRules.WouldNormalizeDogleg(
                geometryResolved: true,
                mirrored: true,
                directionAlreadyMatches: true));
        Assert.True(
            TimberFramedBlockContentGripStageProofRules.WouldNormalizeDogleg(
                geometryResolved: true,
                mirrored: false,
                directionAlreadyMatches: false));
        Assert.False(
            TimberFramedBlockContentGripStageProofRules.WouldNormalizeDogleg(
                geometryResolved: false,
                mirrored: true,
                directionAlreadyMatches: false));

        Assert.True(
            TimberFramedBlockContentGripStageProofRules.WouldNormalizeContentSide(
                TimberFramedBlockContentDimensionColumnMirrorDecision.Swap));
        Assert.False(
            TimberFramedBlockContentGripStageProofRules.WouldNormalizeContentSide(
                TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp));
    }

    [Fact]
    public void ClassifyNormalizeState_PostWrongFailedUnknown()
    {
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeProofState.CallbackFailed,
            TimberFramedBlockContentGripStageProofRules.ClassifyNormalizeState(
                callbackFailed: true,
                trackedHandle: "A",
                currentHandle: "A",
                currentPlacementCorrect: true));
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeProofState.PostGripCorrect,
            TimberFramedBlockContentGripStageProofRules.ClassifyNormalizeState(
                callbackFailed: false,
                trackedHandle: "A",
                currentHandle: "A",
                currentPlacementCorrect: true));
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeProofState.PostGripWrong,
            TimberFramedBlockContentGripStageProofRules.ClassifyNormalizeState(
                callbackFailed: false,
                trackedHandle: "A",
                currentHandle: "A",
                currentPlacementCorrect: false));
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeProofState.Unknown,
            TimberFramedBlockContentGripStageProofRules.ClassifyNormalizeState(
                callbackFailed: false,
                trackedHandle: "A",
                currentHandle: "B",
                currentPlacementCorrect: true));
        Assert.Equal(
            TimberFramedBlockContentGripStageProofRules.StatePostGripCorrect,
            TimberFramedBlockContentGripStageProofRules.FormatNormalizeState(
                TimberFramedBlockContentGripNormalizeProofState.PostGripCorrect));
        Assert.Equal(
            TimberFramedBlockContentGripStageProofRules.StateCallbackFailed,
            TimberFramedBlockContentGripStageProofRules.FormatNormalizeState(
                TimberFramedBlockContentGripNormalizeProofState.CallbackFailed));
    }

    [Fact]
    public void SameUndoHostSequence_DocumentsDeferredRedoLimitation()
    {
        Assert.Contains(
            "POST→U→PRE→MREDO→POST",
            TimberFramedBlockContentGripStageProofRules
                .SameUndoHostSequenceDeferredDocumentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "do NOT re-enable AK_DEV_FBC_UNDO_PROOF_SETUP",
            TimberFramedBlockContentGripStageProofRules
                .SameUndoHostSequenceDeferredDocumentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PersistentNormalizeWriteCount",
            TimberFramedBlockContentGripStageProofRules
                .SameUndoHostSequenceDeferredDocumentation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StageDReadiness_NotRunPassedFailed_ArmGate()
    {
        Assert.Equal(
            TimberFramedBlockContentGripStageDReadiness.NotRun,
            TimberFramedBlockContentGripStageProofRules.ClassifyStageDReadiness(
                exceptionCount: 0,
                inspectionSuccessCount: 0));
        Assert.Equal(
            TimberFramedBlockContentGripStageDReadiness.Failed,
            TimberFramedBlockContentGripStageProofRules.ClassifyStageDReadiness(
                exceptionCount: 1,
                inspectionSuccessCount: 5));
        Assert.Equal(
            TimberFramedBlockContentGripStageDReadiness.Passed,
            TimberFramedBlockContentGripStageProofRules.ClassifyStageDReadiness(
                exceptionCount: 0,
                inspectionSuccessCount: 1));

        Assert.False(
            TimberFramedBlockContentGripStageProofRules.IsStageDZeroExceptionReady(
                exceptionCount: 0,
                inspectionSuccessCount: 0));
        Assert.True(
            TimberFramedBlockContentGripStageProofRules.IsStageDZeroExceptionReady(
                exceptionCount: 0,
                inspectionSuccessCount: 1));

        var notRunDecision = TimberFramedBlockContentGripStageProofRules.DecideStageEArm(
            TimberFramedBlockContentGripStageDReadiness.NotRun,
            out var notRunReason);
        Assert.Equal(
            TimberFramedBlockContentGripStageEArmDecision.Allowed,
            notRunDecision);
        Assert.Equal(
            TimberFramedBlockContentGripStageProofRules.StageEArmReasonNotRunAllowed,
            notRunReason);

        var passedDecision = TimberFramedBlockContentGripStageProofRules.DecideStageEArm(
            TimberFramedBlockContentGripStageDReadiness.Passed,
            out var passedReason);
        Assert.Equal(
            TimberFramedBlockContentGripStageEArmDecision.Allowed,
            passedDecision);
        Assert.Equal(
            TimberFramedBlockContentGripStageProofRules.StageEArmReasonPassedAllowed,
            passedReason);

        var failedDecision = TimberFramedBlockContentGripStageProofRules.DecideStageEArm(
            TimberFramedBlockContentGripStageDReadiness.Failed,
            out var failedReason);
        Assert.Equal(
            TimberFramedBlockContentGripStageEArmDecision.Blocked,
            failedDecision);
        Assert.Equal(
            TimberFramedBlockContentGripStageProofRules
                .StageEBlockedUnresolvedExceptionsMessage,
            failedReason);

        Assert.Equal(
            "NotRun",
            TimberFramedBlockContentGripStageProofRules.FormatStageDReadiness(
                TimberFramedBlockContentGripStageDReadiness.NotRun));
        Assert.Equal(
            "Allowed",
            TimberFramedBlockContentGripStageProofRules.FormatStageEArmDecision(
                TimberFramedBlockContentGripStageEArmDecision.Allowed));
        Assert.Equal(
            "DirectCallbackEntityWithTransientSkip",
            TimberFramedBlockContentGripStageProofRules.StageEImplementationMode);
        Assert.Equal(
            "TransientGeometryNotReady",
            TimberFramedBlockContentGripStageProofRules.FormatInspectionOutcome(
                TimberFramedBlockContentGripReadOnlyInspectionOutcome
                    .TransientGeometryNotReady));
        Assert.Equal(
            "Success",
            TimberFramedBlockContentGripStageProofRules.FormatInspectionOutcome(
                TimberFramedBlockContentGripReadOnlyInspectionOutcome.Success));
        Assert.Equal(
            "SuccessChanged",
            TimberFramedBlockContentGripStageProofRules.FormatNormalizeOutcome(
                TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged));
        Assert.Equal(
            "TransientSkip",
            TimberFramedBlockContentGripStageProofRules.FormatNormalizeOutcome(
                TimberFramedBlockContentGripNormalizeOutcome.TransientSkip));
        Assert.Equal(
            "SuccessNoOp",
            TimberFramedBlockContentGripStageProofRules.FormatNormalizeOutcome(
                TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp));
    }

    private static string CreateCombinedName(
        TimberFramedBlockContentKind kind,
        TimberFramedBlockContentDimensionColumnSide side) =>
        TimberFramedBlockContentVariantRules.CreateSafeBlockName(
            TimberFramedBlockContentVariantRules.CreateRawKey(
                kind,
                "MEDIUM",
                "Standard",
                "Standard",
                2.7d,
                2.5d,
                TimberFramedBlockContentPresentation.Combined,
                side));
}
