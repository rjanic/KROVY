using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Per-document Stage D/E grip proof session. Host maps documents; Core holds
/// no CAD types. Own reentrancy guard — independent of P4A / UNDO_PROOF.
/// </summary>
public sealed class TimberFramedBlockContentGripStageProofSession
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

    public int CallbackCount { get; set; }

    public int BaseMoveCompletedCount { get; set; }

    public int InspectionSuccessCount { get; set; }

    public int InspectionTransientSkipCount { get; set; }

    public int InspectionNotApplicableCount { get; set; }

    public int NormalizeAttemptCount { get; set; }

    public int NormalizeChangedCount { get; set; }

    public int NormalizeNoOpCount { get; set; }

    public int TransientSkipCount { get; set; }

    public int ExceptionCount { get; set; }

    public bool LastNativeMoveCompleted { get; set; }

    public bool LastCallbackFailed { get; set; }

    public TimberFramedBlockContentGripNormalizeOutcome? LastNormalizeOutcome { get; set; }

    public string LastNormalizeReason { get; set; } = string.Empty;

    public TimberFramedBlockContentGripReadOnlyInspection? LastInspection { get; set; }

    public TimberFramedBlockContentGripReadOnlyInspectionOutcome? LastInspectionOutcome
    {
        get;
        set;
    }

    public string LastInspectionReason { get; set; } = string.Empty;

    public bool? LastCurrentPlacementCorrect { get; set; }

    public bool LastWouldNormalizeDogleg { get; set; }

    public bool LastWouldNormalizeContentSide { get; set; }

    public bool LastDoglegChanged { get; set; }

    public bool LastContentSideChanged { get; set; }

    public string LastDoglegReason { get; set; } = string.Empty;

    public string LastContentSideReason { get; set; } = string.Empty;

    public TimberFramedBlockContentGripNormalizeProofState LastNormalizeState { get; set; } =
        TimberFramedBlockContentGripNormalizeProofState.Unknown;

    public string LastExceptionType { get; set; } = string.Empty;

    public string LastExceptionMessage { get; set; } = string.Empty;

    public string LastExceptionStack { get; set; } = string.Empty;

    public int? FirstExceptionCallbackIndex { get; set; }

    public int? LastExceptionCallbackIndex { get; set; }

    public string LastFailingOperation { get; set; } = string.Empty;

    public double LastCallbackOffsetX { get; set; }

    public double LastCallbackOffsetY { get; set; }

    public double LastCallbackOffsetZ { get; set; }

    public string LastCallbackGripIndices { get; set; } = string.Empty;

    public string LastEntityOpenState { get; set; } = string.Empty;

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
        TrackedHandle = string.Empty;
        CallbackCount = 0;
        BaseMoveCompletedCount = 0;
        InspectionSuccessCount = 0;
        InspectionTransientSkipCount = 0;
        InspectionNotApplicableCount = 0;
        NormalizeAttemptCount = 0;
        NormalizeChangedCount = 0;
        NormalizeNoOpCount = 0;
        TransientSkipCount = 0;
        ExceptionCount = 0;
        LastNativeMoveCompleted = false;
        LastCallbackFailed = false;
        LastNormalizeOutcome = null;
        LastNormalizeReason = string.Empty;
        LastInspection = null;
        LastInspectionOutcome = null;
        LastInspectionReason = string.Empty;
        LastCurrentPlacementCorrect = null;
        LastWouldNormalizeDogleg = false;
        LastWouldNormalizeContentSide = false;
        LastDoglegChanged = false;
        LastContentSideChanged = false;
        LastDoglegReason = string.Empty;
        LastContentSideReason = string.Empty;
        LastNormalizeState = TimberFramedBlockContentGripNormalizeProofState.Unknown;
        LastExceptionType = string.Empty;
        LastExceptionMessage = string.Empty;
        LastExceptionStack = string.Empty;
        FirstExceptionCallbackIndex = null;
        LastExceptionCallbackIndex = null;
        LastFailingOperation = string.Empty;
        LastCallbackOffsetX = 0d;
        LastCallbackOffsetY = 0d;
        LastCallbackOffsetZ = 0d;
        LastCallbackGripIndices = string.Empty;
        LastEntityOpenState = string.Empty;
        ForceReleaseProcessingGuard();
    }

    public void RecordExceptionDiagnostics(
        int callbackIndex,
        string typeName,
        string message,
        string stack,
        string failingOperation)
    {
        ExceptionCount++;
        LastExceptionType = typeName ?? string.Empty;
        LastExceptionMessage = message ?? string.Empty;
        LastExceptionStack = stack ?? string.Empty;
        LastFailingOperation = failingOperation ?? string.Empty;
        FirstExceptionCallbackIndex ??= callbackIndex;
        LastExceptionCallbackIndex = callbackIndex;
    }

    public void RecordNormalizeOutcome(
        TimberFramedBlockContentGripNormalizeOutcome outcome,
        string reason)
    {
        LastNormalizeOutcome = outcome;
        LastNormalizeReason = reason ?? string.Empty;
        switch (outcome)
        {
            case TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged:
                NormalizeChangedCount++;
                break;
            case TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp:
                NormalizeNoOpCount++;
                break;
            case TimberFramedBlockContentGripNormalizeOutcome.TransientSkip:
                TransientSkipCount++;
                break;
            case TimberFramedBlockContentGripNormalizeOutcome.NotApplicable:
            case TimberFramedBlockContentGripNormalizeOutcome.Failed:
                // Counted via LastNormalizeOutcome only — Failed does not
                // auto-increment ExceptionCount (caller decides).
                break;
        }
    }

    public IDisposable BeginProcessing()
    {
        if (_isProcessing)
        {
            throw new InvalidOperationException(
                "Stage D/E grip proof session is already processing.");
        }

        _isProcessing = true;
        return new ProcessingScope(this);
    }

    /// <summary>
    /// Exception-safe cleanup: clear a leaked processing guard without throwing.
    /// </summary>
    public void ForceReleaseProcessingGuard() => _isProcessing = false;

    public TimberFramedBlockContentGripNormalizeProofState ClassifyNormalizeCurrent(
        string? currentHandle,
        bool? currentPlacementCorrect) =>
        TimberFramedBlockContentGripStageProofRules.ClassifyNormalizeState(
            LastCallbackFailed,
            TrackedHandle,
            currentHandle,
            currentPlacementCorrect);

    private void EndProcessing() => _isProcessing = false;

    private sealed class ProcessingScope : IDisposable
    {
        private TimberFramedBlockContentGripStageProofSession? _owner;

        public ProcessingScope(TimberFramedBlockContentGripStageProofSession owner)
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
