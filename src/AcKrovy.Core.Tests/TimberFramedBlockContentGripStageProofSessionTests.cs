using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentGripStageProofSessionTests
{
    [Fact]
    public void TryRegisterOnce_AndUnregister_CountsOnce()
    {
        var session = new TimberFramedBlockContentGripStageProofSession();
        Assert.True(session.TryRegisterOnce());
        Assert.False(session.TryRegisterOnce());
        Assert.Equal(1, session.RegisterCount);
        Assert.True(session.OverruleRegistered);
        session.MarkUnregistered();
        Assert.False(session.OverruleRegistered);
        Assert.Equal(1, session.UnregisterCount);
        Assert.True(session.TryRegisterOnce());
        Assert.Equal(2, session.RegisterCount);
    }

    [Fact]
    public void BeginProcessing_ReentrancyThrows_ForceReleaseResets()
    {
        var session = new TimberFramedBlockContentGripStageProofSession();
        using (session.BeginProcessing())
        {
            Assert.True(session.IsProcessing);
            Assert.Throws<InvalidOperationException>(() => session.BeginProcessing());
        }

        Assert.False(session.IsProcessing);
        session.ForceReleaseProcessingGuard();
        Assert.False(session.IsProcessing);
        using (session.BeginProcessing())
        {
            Assert.True(session.IsProcessing);
        }

        Assert.False(session.IsProcessing);
    }

    [Fact]
    public void ClassifyNormalizeCurrent_UsesCallbackFailedFlag()
    {
        var session = new TimberFramedBlockContentGripStageProofSession
        {
            TrackedHandle = "1A",
            LastCallbackFailed = true,
        };
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeProofState.CallbackFailed,
            session.ClassifyNormalizeCurrent("1A", currentPlacementCorrect: true));

        session.LastCallbackFailed = false;
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeProofState.PostGripCorrect,
            session.ClassifyNormalizeCurrent("1A", currentPlacementCorrect: true));

        Assert.Equal(
            TimberFramedBlockContentGripNormalizeProofState.PostGripWrong,
            session.ClassifyNormalizeCurrent("1A", currentPlacementCorrect: false));
    }

    [Fact]
    public void ClearProofRuntime_ResetsGuardAndCounters()
    {
        var session = new TimberFramedBlockContentGripStageProofSession
        {
            ProofEnabled = true,
            TrackedHandle = "X",
            CallbackCount = 3,
            BaseMoveCompletedCount = 3,
            InspectionSuccessCount = 2,
            InspectionTransientSkipCount = 1,
            InspectionNotApplicableCount = 1,
            NormalizeAttemptCount = 4,
            NormalizeChangedCount = 1,
            NormalizeNoOpCount = 2,
            TransientSkipCount = 1,
            ExceptionCount = 1,
            LastCallbackFailed = true,
            LastNormalizeOutcome = TimberFramedBlockContentGripNormalizeOutcome.Failed,
            LastNormalizeReason = "x",
            LastExceptionType = "T",
            LastFailingOperation = "op",
        };
        using (session.BeginProcessing())
        {
            // Force-clear while processing must release guard.
            session.ClearProofRuntime();
        }

        Assert.False(session.ProofEnabled);
        Assert.Equal(string.Empty, session.TrackedHandle);
        Assert.Equal(0, session.CallbackCount);
        Assert.Equal(0, session.BaseMoveCompletedCount);
        Assert.Equal(0, session.InspectionSuccessCount);
        Assert.Equal(0, session.InspectionTransientSkipCount);
        Assert.Equal(0, session.InspectionNotApplicableCount);
        Assert.Equal(0, session.NormalizeAttemptCount);
        Assert.Equal(0, session.NormalizeChangedCount);
        Assert.Equal(0, session.NormalizeNoOpCount);
        Assert.Equal(0, session.TransientSkipCount);
        Assert.Equal(0, session.ExceptionCount);
        Assert.False(session.IsProcessing);
        Assert.False(session.LastCallbackFailed);
        Assert.Null(session.LastNormalizeOutcome);
        Assert.Equal(string.Empty, session.LastNormalizeReason);
        Assert.Equal(string.Empty, session.LastExceptionType);
        Assert.Equal(string.Empty, session.LastFailingOperation);
    }

    [Fact]
    public void RecordNormalizeOutcome_CountsChangedNoOpTransient_NotExceptions()
    {
        var session = new TimberFramedBlockContentGripStageProofSession();
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged,
            "swapped");
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp,
            "already correct");
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.TransientSkip,
            "BlockContentId Null");
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.Failed,
            "hard fail");

        Assert.Equal(1, session.NormalizeChangedCount);
        Assert.Equal(1, session.NormalizeNoOpCount);
        Assert.Equal(1, session.TransientSkipCount);
        Assert.Equal(0, session.ExceptionCount);
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeOutcome.Failed,
            session.LastNormalizeOutcome);
        Assert.Equal("hard fail", session.LastNormalizeReason);
    }

    [Fact]
    public void RecordExceptionDiagnostics_PreservesFirstAndLastCallbackIndex()
    {
        var session = new TimberFramedBlockContentGripStageProofSession();
        session.RecordExceptionDiagnostics(
            callbackIndex: 2,
            typeName: "Autodesk.AutoCAD.Runtime.Exception",
            message: "eWasOpenForWrite",
            stack: "stack-a",
            failingOperation: "GetObject(leader)");
        session.RecordExceptionDiagnostics(
            callbackIndex: 9,
            typeName: "Autodesk.AutoCAD.Runtime.Exception",
            message: "eWasOpenForWrite",
            stack: "stack-b",
            failingOperation: "GetObject(leader)");

        Assert.Equal(2, session.ExceptionCount);
        Assert.Equal(2, session.FirstExceptionCallbackIndex);
        Assert.Equal(9, session.LastExceptionCallbackIndex);
        Assert.Equal("GetObject(leader)", session.LastFailingOperation);
        Assert.Equal("eWasOpenForWrite", session.LastExceptionMessage);
        Assert.Equal("stack-b", session.LastExceptionStack);
    }
}
