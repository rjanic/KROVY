using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Per-document P4B grip-undo proof session. Host maps documents to sessions;
/// Core keeps no CAD host types. Own reentrancy guard — independent of P4A.
/// </summary>
public sealed class TimberFramedBlockContentGripUndoProofSession
{
    private bool _isProcessing;
    private bool _overruleRegistered;
    private int _registerCount;
    private int _unregisterCount;

    public bool ProofEnabled { get; set; }

    public bool IsProcessing => _isProcessing;

    public bool OverruleRegistered => _overruleRegistered;

    public int RegisterCount => _registerCount;

    public int UnregisterCount => _unregisterCount;

    public string TrackedHandle { get; set; } = string.Empty;

    public TimberFramedBlockContentGripUndoProofSnapshot? PreGripSnapshot { get; set; }

    public TimberFramedBlockContentGripUndoProofSnapshot? PostGripSnapshot { get; set; }

    public bool LastNormalizeChangedContentOrDogleg { get; set; }

    public string LastDoglegReason { get; set; } = string.Empty;

    public bool LastDoglegApplied { get; set; }

    public bool LastDoglegChanged { get; set; }

    public string LastContentSideReason { get; set; } = string.Empty;

    public bool LastContentSideApplied { get; set; }

    public bool LastContentSideChanged { get; set; }

    public int ExternalLifecycleQueuedCount { get; set; }

    public int ExternalLifecycleMutations { get; set; }

    public void MarkRegistered()
    {
        if (_overruleRegistered)
        {
            return;
        }

        _overruleRegistered = true;
        _registerCount++;
    }

    public void MarkUnregistered()
    {
        if (!_overruleRegistered)
        {
            return;
        }

        _overruleRegistered = false;
        _unregisterCount++;
    }

    public bool TryRegisterOnce()
    {
        if (_overruleRegistered)
        {
            return false;
        }

        MarkRegistered();
        return true;
    }

    public void ClearProofRuntime()
    {
        ProofEnabled = false;
        PreGripSnapshot = null;
        PostGripSnapshot = null;
        LastNormalizeChangedContentOrDogleg = false;
        LastDoglegReason = string.Empty;
        LastDoglegApplied = false;
        LastDoglegChanged = false;
        LastContentSideReason = string.Empty;
        LastContentSideApplied = false;
        LastContentSideChanged = false;
        ExternalLifecycleQueuedCount = 0;
        ExternalLifecycleMutations = 0;
        TrackedHandle = string.Empty;
        ForceReleaseProcessingGuard();
    }

    public IDisposable BeginProcessing()
    {
        if (_isProcessing)
        {
            throw new InvalidOperationException(
                "P4B grip undo proof session is already processing.");
        }

        _isProcessing = true;
        return new ProcessingScope(this);
    }

    /// <summary>
    /// Exception-safe cleanup: clear a leaked processing guard without throwing.
    /// </summary>
    public void ForceReleaseProcessingGuard() => _isProcessing = false;

    public TimberFramedBlockContentGripUndoProofState ClassifyCurrent(
        TimberFramedBlockContentGripUndoProofSnapshot? current) =>
        TimberFramedBlockContentGripUndoProofRules.ClassifyState(
            PreGripSnapshot,
            PostGripSnapshot,
            current,
            LastNormalizeChangedContentOrDogleg);

    private void EndProcessing() => _isProcessing = false;

    private sealed class ProcessingScope : IDisposable
    {
        private TimberFramedBlockContentGripUndoProofSession? _owner;

        public ProcessingScope(TimberFramedBlockContentGripUndoProofSession owner)
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
