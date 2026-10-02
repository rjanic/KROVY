namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Same-transaction completion after locked recovery. Ports use the existing
/// host services; failure stops later writes and must prevent the caller's commit.</summary>
internal static class RoofStructuralLockedRecoveryCompletion
{
    internal static bool TryCompleteStructuralRecovery(bool structuralRecovery,
        Func<bool> restorePlan, Func<bool> reconcileStructural,
        Func<bool> restoreAnnotations, Func<bool> finalize) =>
        restorePlan() && (!structuralRecovery || reconcileStructural()) &&
        restoreAnnotations() && finalize();
}
