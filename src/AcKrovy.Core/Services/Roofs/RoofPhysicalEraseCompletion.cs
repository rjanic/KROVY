namespace AcKrovy.Core.Services.Roofs;

/// <summary>Preference-independent once-only completion after the restore transaction closes.</summary>
public sealed class RoofPhysicalEraseCompletion
{
    private bool _completed;
    public bool TryComplete(bool restoreTransactionClosed, Action finalizeModel, Action warning, Action verifyFinalGroup)
    {
        if (_completed || !restoreTransactionClosed) return false;
        _completed = true;
        finalizeModel();
        try { warning(); }
        finally { verifyFinalGroup(); }
        return true;
    }
}
