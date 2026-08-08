using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// In-process production GripOverrule diagnostics and reentrancy guard.
/// CAD-neutral — host maps documents / Overrule lifetime.
/// </summary>
public sealed class TimberFramedBlockContentProductionGripNormalizeSession
{
    private bool _isProcessing;
    private bool _overruleRegistered;
    private int _registerCount;
    private int _unregisterCount;

    public bool IsProcessing => _isProcessing;

    public bool OverruleRegistered => _overruleRegistered;

    public int RegisterCount => _registerCount;

    public int UnregisterCount => _unregisterCount;

    public int ApplicableProcessedCount { get; set; }

    public int IgnoredForeignCount { get; set; }

    public int NormalizeChangedCount { get; set; }

    public int NormalizeNoOpCount { get; set; }

    public int TransientSkipCount { get; set; }

    public int ExceptionCount { get; set; }

    public string LastHandle { get; set; } = string.Empty;

    public TimberFramedBlockContentGripNormalizeOutcome? LastOutcome { get; set; }

    public string LastReason { get; set; } = string.Empty;

    public bool TryRegisterOnce()
    {
        if (!TimberFramedBlockContentProductionGripNormalizeRules.ShouldRegisterOverrule(
                _overruleRegistered))
        {
            return false;
        }

        _overruleRegistered = true;
        _registerCount++;
        return true;
    }

    public bool TryUnregisterOnce()
    {
        if (!TimberFramedBlockContentProductionGripNormalizeRules.ShouldUnregisterOverrule(
                _overruleRegistered))
        {
            return false;
        }

        _overruleRegistered = false;
        _unregisterCount++;
        return true;
    }

    public void MarkRegistered()
    {
        if (!_overruleRegistered)
        {
            _overruleRegistered = true;
            _registerCount++;
        }
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

    public IDisposable BeginProcessing()
    {
        if (_isProcessing)
        {
            throw new InvalidOperationException(
                "Production grip normalize reentrancy guard already held.");
        }

        _isProcessing = true;
        return new ProcessingScope(this);
    }

    public void ForceReleaseProcessingGuard() => _isProcessing = false;

    public void RecordNormalizeOutcome(
        TimberFramedBlockContentGripNormalizeOutcome outcome,
        string? reason,
        string? handle)
    {
        LastOutcome = outcome;
        LastReason = reason ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(handle))
        {
            LastHandle = handle!;
        }

        switch (outcome)
        {
            case TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged:
                ApplicableProcessedCount++;
                NormalizeChangedCount++;
                break;
            case TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp:
                ApplicableProcessedCount++;
                NormalizeNoOpCount++;
                break;
            case TimberFramedBlockContentGripNormalizeOutcome.TransientSkip:
                TransientSkipCount++;
                break;
            case TimberFramedBlockContentGripNormalizeOutcome.NotApplicable:
                IgnoredForeignCount++;
                break;
            case TimberFramedBlockContentGripNormalizeOutcome.Failed:
                ExceptionCount++;
                break;
        }
    }

    public void RecordCaughtException(string? handle, string? reason)
    {
        ExceptionCount++;
        LastOutcome = TimberFramedBlockContentGripNormalizeOutcome.Failed;
        LastReason = reason ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(handle))
        {
            LastHandle = handle!;
        }
    }

    private sealed class ProcessingScope : IDisposable
    {
        private readonly TimberFramedBlockContentProductionGripNormalizeSession _owner;
        private bool _disposed;

        public ProcessingScope(TimberFramedBlockContentProductionGripNormalizeSession owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner._isProcessing = false;
        }
    }
}
