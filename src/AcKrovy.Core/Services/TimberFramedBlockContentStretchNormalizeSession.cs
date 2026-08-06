namespace AcKrovy.Core.Services;

/// <summary>
/// Immutable snapshot of DEBUG lifecycle flags / command sets for autotest
/// isolation restore. Does not capture the in-flight processing guard.
/// </summary>
public readonly record struct TimberFramedBlockContentStretchNormalizeExternalState(
    bool TraceEnabled,
    bool ProofEnabled,
    string ActiveCommandName,
    string LastObservedCommandName,
    IReadOnlyList<string> ConfirmedCommandNames,
    IReadOnlyList<string> ObservedCommandNames,
    int QueuedCount);

/// <summary>
/// Per-document P4A stretch-normalize session state. Host maps documents to
/// sessions; Core keeps no CAD host types.
/// </summary>
public sealed class TimberFramedBlockContentStretchNormalizeSession
{
    private readonly LiveGeometryRefreshCoordinator<string> _queuedObjectKeys = new();
    private readonly HashSet<string> _observedCommandNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _confirmedCommandNames =
        new(StringComparer.OrdinalIgnoreCase);
    private string _activeCommandName = string.Empty;
    private string _lastObservedCommandName = string.Empty;
    private bool _isProcessing;

    public bool TraceEnabled { get; set; }

    public bool ProofEnabled { get; set; }

    public bool IsProcessing => _isProcessing;

    public int QueuedCount => _queuedObjectKeys.Count;

    public string ActiveCommandName => _activeCommandName;

    public IReadOnlyCollection<string> ObservedCommandNames => _observedCommandNames;

    public IReadOnlyCollection<string> ConfirmedCommandNames => _confirmedCommandNames;

    public void BeginCommand(string? globalCommandName)
    {
        _activeCommandName =
            TimberFramedBlockContentStretchNormalizeRules.NormalizeCommandName(
                globalCommandName);
        ClearQueue();
    }

    public bool TryQueueObjectKey(string objectKey)
    {
        if (_isProcessing || string.IsNullOrWhiteSpace(objectKey))
        {
            return false;
        }

        return _queuedObjectKeys.TryAdd(objectKey.Trim());
    }

    public IReadOnlyList<string> DrainQueue() => _queuedObjectKeys.Drain();

    public void ClearQueue() => _queuedObjectKeys.Clear();

    public void CancelOrFailCommand()
    {
        _activeCommandName = string.Empty;
        ClearQueue();
    }

    public void RememberObservedCommandIfQueued()
    {
        if (_queuedObjectKeys.Count == 0 || _activeCommandName.Length == 0)
        {
            return;
        }

        _observedCommandNames.Add(_activeCommandName);
        _lastObservedCommandName = _activeCommandName;
    }

    public bool ConfirmCommand(string? globalCommandName)
    {
        var normalized =
            TimberFramedBlockContentStretchNormalizeRules.NormalizeCommandName(
                globalCommandName);
        if (normalized.Length == 0)
        {
            return false;
        }

        _confirmedCommandNames.Add(normalized);
        return true;
    }

    public bool ConfirmLastObservedCommand()
    {
        if (_lastObservedCommandName.Length == 0)
        {
            return false;
        }

        return ConfirmCommand(_lastObservedCommandName);
    }

    public void ClearConfirmedCommands() => _confirmedCommandNames.Clear();

    public void ClearObservedCommands()
    {
        _observedCommandNames.Clear();
        _lastObservedCommandName = string.Empty;
    }

    /// <summary>
    /// Snapshot Trace/Proof/confirmed/observed/queue for autotest isolation.
    /// </summary>
    public TimberFramedBlockContentStretchNormalizeExternalState CaptureExternalState() =>
        new(
            TraceEnabled,
            ProofEnabled,
            _activeCommandName,
            _lastObservedCommandName,
            _confirmedCommandNames.OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _observedCommandNames.OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            QueuedCount);

    /// <summary>
    /// Restore a prior <see cref="CaptureExternalState"/> snapshot exactly.
    /// Clears the live queue first, then reapplies flags and command sets.
    /// </summary>
    public void RestoreExternalState(
        TimberFramedBlockContentStretchNormalizeExternalState state)
    {
        ClearQueue();
        TraceEnabled = state.TraceEnabled;
        ProofEnabled = state.ProofEnabled;
        _activeCommandName = state.ActiveCommandName ?? string.Empty;
        ClearConfirmedCommands();
        ClearObservedCommands();
        foreach (var name in state.ConfirmedCommandNames ?? Array.Empty<string>())
        {
            var normalized =
                TimberFramedBlockContentStretchNormalizeRules.NormalizeCommandName(name);
            if (normalized.Length > 0)
            {
                _confirmedCommandNames.Add(normalized);
            }
        }

        foreach (var name in state.ObservedCommandNames ?? Array.Empty<string>())
        {
            var normalized =
                TimberFramedBlockContentStretchNormalizeRules.NormalizeCommandName(name);
            if (normalized.Length > 0)
            {
                _observedCommandNames.Add(normalized);
            }
        }

        _lastObservedCommandName = state.LastObservedCommandName ?? string.Empty;
    }

    /// <summary>
    /// Force normal autotest scenarios: Trace/Proof off, empty queue, no active
    /// synthetic command. Does not change confirmed/observed sets (caller may
    /// clear those separately when isolating).
    /// </summary>
    public void ForceAutotestIsolation()
    {
        TraceEnabled = false;
        ProofEnabled = false;
        ClearQueue();
        _activeCommandName = string.Empty;
    }

    /// <summary>
    /// DEBUG autotest arm: Trace+Proof on, confirm GRIP_STRETCH, clear queues.
    /// Does not claim to fix the UNDO production blocker.
    /// Prefer <see cref="ForceAutotestIsolation"/> + internal drain for the
    /// isolated LifecycleProcessor autotest section.
    /// </summary>
    public void ArmLifecycleTest(string commandName)
    {
        var normalized =
            TimberFramedBlockContentStretchNormalizeRules.NormalizeCommandName(
                commandName);
        TraceEnabled = true;
        ProofEnabled = true;
        ClearQueue();
        ClearConfirmedCommands();
        ClearObservedCommands();
        _activeCommandName = string.Empty;
        if (normalized.Length > 0)
        {
            _confirmedCommandNames.Add(normalized);
        }
    }

    /// <summary>
    /// DEBUG autotest disarm: Trace+Proof off, clear queue/confirmed/observed.
    /// </summary>
    public void DisarmLifecycleTest()
    {
        TraceEnabled = false;
        ProofEnabled = false;
        ClearQueue();
        ClearConfirmedCommands();
        ClearObservedCommands();
        _activeCommandName = string.Empty;
        _lastObservedCommandName = string.Empty;
    }

    public bool ShouldProcessEndedCommand(string? globalCommandName) =>
        TimberFramedBlockContentStretchNormalizeRules.ShouldRunAutomation(
            ProofEnabled,
            globalCommandName,
            _confirmedCommandNames);

    public IDisposable BeginProcessing()
    {
        if (_isProcessing)
        {
            throw new InvalidOperationException(
                "Stretch normalize session is already processing.");
        }

        _isProcessing = true;
        return new ProcessingScope(this);
    }

    public IDisposable SuppressQueue() => _queuedObjectKeys.Suppress();

    /// <summary>
    /// Autotest cleanup: clear a leaked processing guard without throwing.
    /// </summary>
    public void ForceReleaseProcessingGuard() => _isProcessing = false;

    private void EndProcessing() => _isProcessing = false;

    private sealed class ProcessingScope : IDisposable
    {
        private TimberFramedBlockContentStretchNormalizeSession? _owner;

        public ProcessingScope(TimberFramedBlockContentStretchNormalizeSession owner)
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
            owner.EndProcessing();
        }
    }
}
