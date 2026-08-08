using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedBlockContentProductionGripNormalizeSessionTests
{
    [Fact]
    public void Guard_Reentrancy_BeginProcessingThenDispose()
    {
        var session = new TimberFramedBlockContentProductionGripNormalizeSession();
        Assert.False(session.IsProcessing);
        using (session.BeginProcessing())
        {
            Assert.True(session.IsProcessing);
            Assert.Throws<InvalidOperationException>(() => session.BeginProcessing());
        }

        Assert.False(session.IsProcessing);
    }

    [Fact]
    public void Guard_ForceRelease_ClearsStuckProcessing()
    {
        var session = new TimberFramedBlockContentProductionGripNormalizeSession();
        using (session.BeginProcessing())
        {
            session.ForceReleaseProcessingGuard();
            Assert.False(session.IsProcessing);
        }
    }

    [Fact]
    public void RegisterUnregister_IdempotentCounts()
    {
        var session = new TimberFramedBlockContentProductionGripNormalizeSession();
        Assert.True(session.TryRegisterOnce());
        Assert.False(session.TryRegisterOnce());
        Assert.Equal(1, session.RegisterCount);
        Assert.True(session.OverruleRegistered);

        Assert.True(session.TryUnregisterOnce());
        Assert.False(session.TryUnregisterOnce());
        Assert.Equal(1, session.UnregisterCount);
        Assert.False(session.OverruleRegistered);

        Assert.True(session.TryRegisterOnce());
        Assert.Equal(2, session.RegisterCount);
    }

    [Fact]
    public void RecordNormalizeOutcome_CountersAndLastScalars()
    {
        var session = new TimberFramedBlockContentProductionGripNormalizeSession();
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged,
            "changed",
            "ABC");
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp,
            "noop",
            "ABC");
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.TransientSkip,
            "skip",
            "ABC");
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.NotApplicable,
            "foreign",
            "DEF");
        session.RecordCaughtException("GHI", "boom");

        Assert.Equal(2, session.ApplicableProcessedCount);
        Assert.Equal(1, session.NormalizeChangedCount);
        Assert.Equal(1, session.NormalizeNoOpCount);
        Assert.Equal(1, session.TransientSkipCount);
        Assert.Equal(1, session.IgnoredForeignCount);
        Assert.Equal(1, session.ExceptionCount);
        Assert.Equal("GHI", session.LastHandle);
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeOutcome.Failed,
            session.LastOutcome);
    }

    [Fact]
    public void NullTransientSkip_DoesNotIncrementException()
    {
        var session = new TimberFramedBlockContentProductionGripNormalizeSession();
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.TransientSkip,
            "BlockContentId Null (transient)",
            null);
        Assert.Equal(1, session.TransientSkipCount);
        Assert.Equal(0, session.ExceptionCount);
        Assert.Equal(
            TimberFramedBlockContentGripNormalizeOutcome.TransientSkip,
            session.LastOutcome);
    }
}
