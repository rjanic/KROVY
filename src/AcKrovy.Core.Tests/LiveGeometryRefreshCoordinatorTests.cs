using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class LiveGeometryRefreshCoordinatorTests
{
    [Fact]
    public void TryAdd_DeduplicatesCandidates()
    {
        var coordinator = new LiveGeometryRefreshCoordinator<int>();

        Assert.True(coordinator.TryAdd(42));
        Assert.False(coordinator.TryAdd(42));
        Assert.Equal(1, coordinator.Count);
    }

    [Fact]
    public void Drain_ReturnsEachCandidateOnceAndClearsBuffer()
    {
        var coordinator = new LiveGeometryRefreshCoordinator<int>();
        coordinator.TryAdd(1);
        coordinator.TryAdd(2);
        coordinator.TryAdd(1);

        var drained = coordinator.Drain();
        var secondDrain = coordinator.Drain();

        Assert.Equal(new[] { 1, 2 }, drained.OrderBy(value => value));
        Assert.Empty(secondDrain);
        Assert.Equal(0, coordinator.Count);
    }

    [Fact]
    public void Suppress_IgnoresCandidatesAndRestoresStateAfterDispose()
    {
        var coordinator = new LiveGeometryRefreshCoordinator<int>();

        using (coordinator.Suppress())
        {
            Assert.True(coordinator.IsSuppressed);
            Assert.False(coordinator.TryAdd(1));
        }

        Assert.False(coordinator.IsSuppressed);
        Assert.True(coordinator.TryAdd(1));
    }

    [Fact]
    public void Suppress_IsExceptionSafeWhenDisposedInFinally()
    {
        var coordinator = new LiveGeometryRefreshCoordinator<int>();
        IDisposable? scope = null;

        try
        {
            scope = coordinator.Suppress();
            throw new InvalidOperationException("simulated");
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            scope?.Dispose();
        }

        Assert.False(coordinator.IsSuppressed);
        Assert.True(coordinator.TryAdd(7));
    }

    [Fact]
    public void Clear_DropsPendingCandidates()
    {
        var coordinator = new LiveGeometryRefreshCoordinator<int>();
        coordinator.TryAdd(1);
        coordinator.TryAdd(2);

        coordinator.Clear();

        Assert.Empty(coordinator.Drain());
    }
}
