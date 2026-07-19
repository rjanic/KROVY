namespace AcKrovy.Core.Services;

public sealed class LiveGeometryRefreshCoordinator<TCandidate>
    where TCandidate : notnull
{
    private readonly HashSet<TCandidate> _candidates = new();
    private int _suppressionDepth;

    public bool IsSuppressed => _suppressionDepth > 0;
    public int Count => _candidates.Count;

    public bool TryAdd(TCandidate candidate)
    {
        if (IsSuppressed)
        {
            return false;
        }

        return _candidates.Add(candidate);
    }

    public IReadOnlyList<TCandidate> Drain()
    {
        if (_candidates.Count == 0)
        {
            return Array.Empty<TCandidate>();
        }

        var drained = _candidates.ToList();
        _candidates.Clear();
        return drained;
    }

    public void Clear() => _candidates.Clear();

    public IDisposable Suppress()
    {
        _suppressionDepth++;
        return new SuppressionScope(this);
    }

    private void ReleaseSuppression()
    {
        if (_suppressionDepth > 0)
        {
            _suppressionDepth--;
        }
    }

    private sealed class SuppressionScope : IDisposable
    {
        private LiveGeometryRefreshCoordinator<TCandidate>? _owner;

        public SuppressionScope(LiveGeometryRefreshCoordinator<TCandidate> owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
            {
                return;
            }

            _owner = null;
            owner.ReleaseSuppression();
        }
    }
}
