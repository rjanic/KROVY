using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentStretchNormalizeSessionTests
{
    [Fact]
    public void Queue_DeduplicatesObjectKeys()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.BeginCommand("STRETCH");

        Assert.True(session.TryQueueObjectKey("A"));
        Assert.False(session.TryQueueObjectKey("A"));
        Assert.True(session.TryQueueObjectKey("B"));
        Assert.Equal(2, session.QueuedCount);

        var drained = session.DrainQueue();
        Assert.Equal(new[] { "A", "B" }, drained.OrderBy(v => v));
        Assert.Equal(0, session.QueuedCount);
        Assert.Empty(session.DrainQueue());
    }

    [Fact]
    public void CancelAndFail_ClearQueue()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.BeginCommand("STRETCH");
        session.TryQueueObjectKey("A");

        session.CancelOrFailCommand();

        Assert.Equal(0, session.QueuedCount);
        Assert.Empty(session.ActiveCommandName);
        Assert.Empty(session.DrainQueue());
    }

    [Fact]
    public void BeginCommand_ClearsPreviousQueue()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.BeginCommand("STRETCH");
        session.TryQueueObjectKey("A");

        session.BeginCommand("MOVE");

        Assert.Equal(0, session.QueuedCount);
        Assert.Equal("MOVE", session.ActiveCommandName);
    }

    [Fact]
    public void Reentrancy_SuppressAndProcessingGuard()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.BeginCommand("STRETCH");

        using (session.SuppressQueue())
        {
            Assert.False(session.TryQueueObjectKey("A"));
        }

        Assert.True(session.TryQueueObjectKey("A"));

        using (session.BeginProcessing())
        {
            Assert.True(session.IsProcessing);
            Assert.False(session.TryQueueObjectKey("B"));
            Assert.Throws<InvalidOperationException>(() => session.BeginProcessing());
        }

        Assert.False(session.IsProcessing);
        Assert.True(session.TryQueueObjectKey("B"));
    }

    [Fact]
    public void SessionIsolation_DocumentsDoNotShareQueuesOrProof()
    {
        var docA = new TimberFramedBlockContentStretchNormalizeSession();
        var docB = new TimberFramedBlockContentStretchNormalizeSession();

        docA.ProofEnabled = true;
        docA.ConfirmCommand("STRETCH");
        docA.BeginCommand("STRETCH");
        docA.TryQueueObjectKey("A1");

        docB.BeginCommand("STRETCH");
        docB.TryQueueObjectKey("B1");

        Assert.True(docA.ShouldProcessEndedCommand("STRETCH"));
        Assert.False(docB.ShouldProcessEndedCommand("STRETCH"));
        Assert.Equal(1, docA.QueuedCount);
        Assert.Equal(1, docB.QueuedCount);
        Assert.DoesNotContain("A1", docB.DrainQueue());
    }

    [Fact]
    public void TraceObservation_AndConfirmLast()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        session.TraceEnabled = true;
        session.BeginCommand("GRIP_STRETCH");
        session.TryQueueObjectKey("1");
        session.RememberObservedCommandIfQueued();

        Assert.Contains("GRIP_STRETCH", session.ObservedCommandNames);
        Assert.True(session.ConfirmLastObservedCommand());
        Assert.Contains("GRIP_STRETCH", session.ConfirmedCommandNames);

        session.ProofEnabled = true;
        Assert.True(session.ShouldProcessEndedCommand("GRIP_STRETCH"));
    }

    [Fact]
    public void Defaults_ProofAndTraceOff()
    {
        var session = new TimberFramedBlockContentStretchNormalizeSession();
        Assert.False(session.TraceEnabled);
        Assert.False(session.ProofEnabled);
        Assert.Empty(session.ConfirmedCommandNames);
    }
}
