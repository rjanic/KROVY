using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// P4B session: reentrancy guard, register-once/unregister, queue isolation
/// counters, exception-safe guard reset. No AutoCAD undo grouping claims.
/// </summary>
public sealed class TimberFramedBlockContentGripUndoProofSessionTests
{
    [Fact]
    public void ReentrancyGuard_BlocksNestedProcessing_AndResetsOnDispose()
    {
        var session = new TimberFramedBlockContentGripUndoProofSession();
        using (session.BeginProcessing())
        {
            Assert.True(session.IsProcessing);
            Assert.Throws<InvalidOperationException>(() => session.BeginProcessing());
        }

        Assert.False(session.IsProcessing);
        using (session.BeginProcessing())
        {
            Assert.True(session.IsProcessing);
        }
    }

    [Fact]
    public void ExceptionSafeGuardReset_ClearsLeakedProcessing()
    {
        var session = new TimberFramedBlockContentGripUndoProofSession();
        _ = session.BeginProcessing();
        Assert.True(session.IsProcessing);

        session.ForceReleaseProcessingGuard();

        Assert.False(session.IsProcessing);
        using (session.BeginProcessing())
        {
            Assert.True(session.IsProcessing);
        }
    }

    [Fact]
    public void RegisterOnce_AndUnregister_AreIdempotent()
    {
        var session = new TimberFramedBlockContentGripUndoProofSession();
        Assert.True(session.TryRegisterOnce());
        Assert.False(session.TryRegisterOnce());
        Assert.Equal(1, session.RegisterCount);
        Assert.True(session.OverruleRegistered);

        session.MarkUnregistered();
        session.MarkUnregistered();
        Assert.Equal(1, session.UnregisterCount);
        Assert.False(session.OverruleRegistered);

        Assert.True(session.TryRegisterOnce());
        Assert.Equal(2, session.RegisterCount);
    }

    [Fact]
    public void QueueIsolationCounters_AndClassifyCurrent()
    {
        var session = new TimberFramedBlockContentGripUndoProofSession();
        var pre = new TimberFramedBlockContentGripUndoProofSnapshot(
            "H1",
            0,
            0,
            0,
            0,
            100,
            0,
            1,
            0,
            50,
            "B_DIMNX",
            "DIMNX",
            true,
            "12",
            "120",
            "60",
            2.7,
            2.5,
            2.5);
        session.PreGripSnapshot = pre;
        session.ExternalLifecycleQueuedCount = 0;
        session.ExternalLifecycleMutations = 0;

        Assert.Equal(
            TimberFramedBlockContentGripUndoProofState.PreGripCorrect,
            session.ClassifyCurrent(pre));
        Assert.Equal(0, session.ExternalLifecycleQueuedCount);
        Assert.Equal(0, session.ExternalLifecycleMutations);

        session.ExternalLifecycleQueuedCount = 2;
        session.ExternalLifecycleMutations = 2;
        session.ClearProofRuntime();
        Assert.False(session.ProofEnabled);
        Assert.Null(session.PreGripSnapshot);
        Assert.Equal(0, session.ExternalLifecycleQueuedCount);
        Assert.Equal(0, session.ExternalLifecycleMutations);
        Assert.False(session.IsProcessing);
    }

    [Fact]
    public void SessionIsolation_DocumentsDoNotShareProofState()
    {
        var a = new TimberFramedBlockContentGripUndoProofSession();
        var b = new TimberFramedBlockContentGripUndoProofSession();
        a.ProofEnabled = true;
        a.TryRegisterOnce();
        a.TrackedHandle = "A";

        Assert.False(b.ProofEnabled);
        Assert.False(b.OverruleRegistered);
        Assert.Empty(b.TrackedHandle);
    }
}
